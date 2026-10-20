using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ApirApir.Multiplayer;
using Unity.Services.CloudSave;
using Unity.Services.CloudSave.Models;
using UnityEngine;

namespace ApirApir.PlayerProfiles
{
    public sealed class UnityAuthenticationPlayerIdentity : ICloudPlayerIdentity
    {
        public string SignedInPlayerId => UnityAccountAuthenticationRuntime.PlayerId;

        public async Task<CloudAuthenticationResult> EnsureAuthenticatedAsync(
            CancellationToken cancellationToken = default)
        {
            try
            {
                AccountAuthenticationResult authentication =
                    await UnityAccountAuthenticationRuntime.Service.RequireAuthenticatedAsync(
                        cancellationToken);
                if (!authentication.Succeeded)
                {
                    CloudAuthenticationStatus status =
                        authentication.Status == AccountAuthenticationStatus.Cancelled
                            ? CloudAuthenticationStatus.Cancelled
                            : authentication.Status == AccountAuthenticationStatus.SessionExpired
                                ? CloudAuthenticationStatus.Expired
                                : CloudAuthenticationStatus.Unavailable;
                    return new CloudAuthenticationResult(
                        status,
                        errorMessage: authentication.ErrorMessage);
                }

                return new CloudAuthenticationResult(
                    CloudAuthenticationStatus.Success,
                    authentication.PlayerId);
            }
            catch (OperationCanceledException)
            {
                return new CloudAuthenticationResult(CloudAuthenticationStatus.Cancelled);
            }
            catch (Exception exception)
            {
                return new CloudAuthenticationResult(
                    CloudAuthenticationStatus.Unavailable,
                    errorMessage: exception.Message);
            }
        }
    }

    /// <summary>
    /// Unity Gaming Services adapter for the signed-in player's Default Player Data.
    /// The complete record is stored under one key and updated with a Cloud Save write lock.
    /// </summary>
    public sealed class UnityCloudPlayerProfileStore : ICloudPlayerProfileStore
    {
        public const string ProfileKey = "apir_apir_player_profile_v1";

        private const int MaximumPayloadBytes = 1024 * 1024;

        [Serializable]
        private sealed class CloudPayload
        {
            public int schemaVersion;
            public string playerId;
            public string playerName;
            public int gamesPlayed;
            public string[] processedMatchIds;
        }

        public async Task<CloudProfileStoreLoadResult> LoadAsync(
            string authenticatedPlayerId,
            CancellationToken cancellationToken = default)
        {
            if (!OwnsAuthenticatedAccount(authenticatedPlayerId))
            {
                return new CloudProfileStoreLoadResult(
                    CloudProfileStoreLoadStatus.Unauthorized,
                    errorMessage: "The requested profile does not match the signed-in Unity account.");
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Dictionary<string, Item> values = await CloudSaveService.Instance.Data.Player.LoadAsync(
                    new HashSet<string>(StringComparer.Ordinal) { ProfileKey });
                cancellationToken.ThrowIfCancellationRequested();
                if (!values.TryGetValue(ProfileKey, out Item item) || item == null)
                {
                    return new CloudProfileStoreLoadResult(CloudProfileStoreLoadStatus.NotFound);
                }

                string json = item.Value.GetAs<string>();
                if (string.IsNullOrWhiteSpace(json) ||
                    Encoding.UTF8.GetByteCount(json) > MaximumPayloadBytes)
                {
                    return new CloudProfileStoreLoadResult(CloudProfileStoreLoadStatus.Corrupt);
                }

                CloudPayload payload = JsonUtility.FromJson<CloudPayload>(json);
                if (payload == null)
                {
                    return new CloudProfileStoreLoadResult(CloudProfileStoreLoadStatus.Corrupt);
                }

                if (payload.schemaVersion != CloudPlayerProfileRecord.CurrentSchemaVersion)
                {
                    return new CloudProfileStoreLoadResult(
                        CloudProfileStoreLoadStatus.UnsupportedSchema);
                }

                var record = new CloudPlayerProfileRecord(
                    payload.schemaVersion,
                    payload.playerId,
                    payload.playerName,
                    payload.gamesPlayed,
                    payload.processedMatchIds);
                return new CloudProfileStoreLoadResult(
                    CloudProfileStoreLoadStatus.Success,
                    record,
                    item.WriteLock);
            }
            catch (OperationCanceledException)
            {
                return new CloudProfileStoreLoadResult(CloudProfileStoreLoadStatus.Cancelled);
            }
            catch (CloudSaveException exception)
            {
                return MapLoadException(exception);
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is InvalidOperationException)
            {
                return new CloudProfileStoreLoadResult(
                    CloudProfileStoreLoadStatus.Corrupt,
                    errorMessage: exception.Message);
            }
        }

        public async Task<CloudProfileStoreSaveResult> SaveAsync(
            CloudPlayerProfileRecord record,
            string expectedWriteLock,
            CancellationToken cancellationToken = default)
        {
            if (record == null || !OwnsAuthenticatedAccount(record.PlayerId))
            {
                return new CloudProfileStoreSaveResult(
                    CloudProfileStoreSaveStatus.Unauthorized,
                    errorMessage: "Cloud profiles can only be saved for the signed-in Unity account.");
            }

            var payload = new CloudPayload
            {
                schemaVersion = record.SchemaVersion,
                playerId = record.PlayerId,
                playerName = record.PlayerName,
                gamesPlayed = record.GamesPlayed,
                processedMatchIds = CopyMatchIds(record.ProcessedMatchIds)
            };
            string json = JsonUtility.ToJson(payload, false);
            if (string.IsNullOrWhiteSpace(json) ||
                Encoding.UTF8.GetByteCount(json) > MaximumPayloadBytes)
            {
                return new CloudProfileStoreSaveResult(
                    CloudProfileStoreSaveStatus.Unavailable,
                    errorMessage: "The cloud profile payload is invalid or too large.");
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Dictionary<string, string> locks;
                if (string.IsNullOrEmpty(expectedWriteLock))
                {
                    IDictionary<string, object> creation = new Dictionary<string, object>
                    {
                        { ProfileKey, json }
                    };
                    locks = await CloudSaveService.Instance.Data.Player.SaveAsync(creation);
                }
                else
                {
                    IDictionary<string, SaveItem> update = new Dictionary<string, SaveItem>
                    {
                        { ProfileKey, new SaveItem(json, expectedWriteLock) }
                    };
                    locks = await CloudSaveService.Instance.Data.Player.SaveAsync(update);
                }

                cancellationToken.ThrowIfCancellationRequested();
                return locks != null && locks.TryGetValue(ProfileKey, out string newWriteLock) &&
                       !string.IsNullOrEmpty(newWriteLock)
                    ? new CloudProfileStoreSaveResult(
                        CloudProfileStoreSaveStatus.Success,
                        newWriteLock)
                    : new CloudProfileStoreSaveResult(
                        CloudProfileStoreSaveStatus.Unavailable,
                        errorMessage: "Cloud Save did not return a write lock.");
            }
            catch (OperationCanceledException)
            {
                return new CloudProfileStoreSaveResult(CloudProfileStoreSaveStatus.Cancelled);
            }
            catch (CloudSaveConflictException exception)
            {
                return new CloudProfileStoreSaveResult(
                    CloudProfileStoreSaveStatus.Conflict,
                    errorMessage: exception.Message);
            }
            catch (CloudSaveException exception)
            {
                return MapSaveException(exception);
            }
        }

        private static bool OwnsAuthenticatedAccount(string requestedPlayerId)
        {
            string authenticatedId = UnityAccountAuthenticationRuntime.PlayerId;
            return authenticatedId.Length > 0 &&
                   string.Equals(
                       authenticatedId,
                       requestedPlayerId == null ? string.Empty : requestedPlayerId.Trim(),
                       StringComparison.Ordinal);
        }

        private static string[] CopyMatchIds(IReadOnlyList<string> source)
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<string>();
            }

            var result = new string[source.Count];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = source[i] ?? string.Empty;
            }

            return result;
        }

        private static CloudProfileStoreLoadResult MapLoadException(CloudSaveException exception)
        {
            if (IsUnauthorized(exception))
            {
                return new CloudProfileStoreLoadResult(
                    CloudProfileStoreLoadStatus.Unauthorized,
                    errorMessage: exception.Message);
            }

            return new CloudProfileStoreLoadResult(
                CloudProfileStoreLoadStatus.Unavailable,
                errorMessage: exception.Message);
        }

        private static CloudProfileStoreSaveResult MapSaveException(CloudSaveException exception)
        {
            if (exception.Reason == CloudSaveExceptionReason.Conflict)
            {
                return new CloudProfileStoreSaveResult(
                    CloudProfileStoreSaveStatus.Conflict,
                    errorMessage: exception.Message);
            }

            if (IsUnauthorized(exception))
            {
                return new CloudProfileStoreSaveResult(
                    CloudProfileStoreSaveStatus.Unauthorized,
                    errorMessage: exception.Message);
            }

            return new CloudProfileStoreSaveResult(
                CloudProfileStoreSaveStatus.Unavailable,
                errorMessage: exception.Message);
        }

        private static bool IsUnauthorized(CloudSaveException exception)
        {
            return exception.Reason == CloudSaveExceptionReason.Unauthorized ||
                   exception.Reason == CloudSaveExceptionReason.PlayerIdMissing ||
                   exception.Reason == CloudSaveExceptionReason.AccessTokenMissing;
        }
    }
}
