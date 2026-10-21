using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ApirApir.Multiplayer;
using Unity.Services.CloudCode;
using Unity.Services.CloudSave;
using Unity.Services.CloudSave.Models;
using UnityEngine;

namespace ApirApir.AugmentedReality
{
    public enum SharedARCardStoreStatus : byte
    {
        Success,
        NotFound,
        InvalidRecord,
        Unauthorized,
        Conflict,
        CloudUnavailable,
        Cancelled,
        LocalCacheOnly
    }

    public sealed class SharedARCardStoreResult
    {
        public SharedARCardStoreResult(
            SharedARCardStoreStatus status,
            ARCardRecord record = null,
            string message = null)
        {
            Status = status;
            Record = record;
            Message = message ?? string.Empty;
        }

        public SharedARCardStoreStatus Status { get; }
        public ARCardRecord Record { get; }
        public string Message { get; }
        public bool IsCloudSuccess => Status == SharedARCardStoreStatus.Success;
    }

    public interface ISharedARCardStore
    {
        Task<SharedARCardStoreResult> SaveAsync(ARCardRecord record, CancellationToken cancellationToken = default);
        Task<SharedARCardStoreResult> LoadAsync(string cardId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Uploads through a server-authoritative Cloud Code script and reads public Cloud Save Game
    /// Data by card ID. The Android client never receives a service-account credential.
    /// </summary>
    public sealed class UnitySharedARCardStore : ISharedARCardStore
    {
        public const string SaveEndpoint = "apir-apir-ar-card-save";
        public const string RecordKey = "record";
        public const int MaximumJsonBytes = 48 * 1024;

        [Serializable]
        private sealed class SaveResponse
        {
            public bool saved = false;
            public bool duplicate = false;
            public string cardId = string.Empty;
        }

        public async Task<SharedARCardStoreResult> SaveAsync(
            ARCardRecord record,
            CancellationToken cancellationToken = default)
        {
            if (record == null || !record.IsValidBasicShape())
                return new SharedARCardStoreResult(SharedARCardStoreStatus.InvalidRecord);

            string json = JsonUtility.ToJson(record, false);
            if (Encoding.UTF8.GetByteCount(json) > MaximumJsonBytes)
                return new SharedARCardStoreResult(SharedARCardStoreStatus.InvalidRecord, message: "Card record is too large.");

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                AccountAuthenticationResult authentication =
                    await UnityAccountAuthenticationRuntime.Service.RequireAuthenticatedAsync(cancellationToken);
                if (!authentication.Succeeded ||
                    !string.Equals(authentication.PlayerId, record.ownerPlayerId, StringComparison.Ordinal))
                {
                    return new SharedARCardStoreResult(SharedARCardStoreStatus.Unauthorized);
                }

                var args = new Dictionary<string, object>
                {
                    { "cardId", record.cardId },
                    { "recordJson", json }
                };
                SaveResponse response = await CloudCodeService.Instance.CallEndpointAsync<SaveResponse>(SaveEndpoint, args);
                cancellationToken.ThrowIfCancellationRequested();
                if (response == null || (!response.saved && !response.duplicate) ||
                    !string.Equals(response.cardId, record.cardId, StringComparison.Ordinal))
                {
                    return new SharedARCardStoreResult(SharedARCardStoreStatus.CloudUnavailable,
                        message: "Cloud Code returned an invalid acknowledgement.");
                }

                ARCardLocalCache.TryWrite(record);
                return new SharedARCardStoreResult(SharedARCardStoreStatus.Success, record,
                    response.duplicate ? "Card already existed with identical data." : "Uploaded to shared cloud storage.");
            }
            catch (OperationCanceledException)
            {
                return new SharedARCardStoreResult(SharedARCardStoreStatus.Cancelled);
            }
            catch (Exception exception)
            {
                ARCardLocalCache.TryWrite(record);
                return new SharedARCardStoreResult(SharedARCardStoreStatus.LocalCacheOnly, record,
                    "Shared upload failed; saved to this device only. " + exception.Message);
            }
        }

        public async Task<SharedARCardStoreResult> LoadAsync(
            string cardId,
            CancellationToken cancellationToken = default)
        {
            string normalized = NormalizeCardId(cardId);
            if (normalized.Length == 0)
                return new SharedARCardStoreResult(SharedARCardStoreStatus.InvalidRecord);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                AccountAuthenticationResult authentication =
                    await UnityAccountAuthenticationRuntime.Service.RequireAuthenticatedAsync(cancellationToken);
                if (!authentication.Succeeded)
                    return new SharedARCardStoreResult(SharedARCardStoreStatus.Unauthorized,
                        message: authentication.ErrorMessage);
                Dictionary<string, Item> values = await CloudSaveService.Instance.Data.Custom.LoadAsync(
                    normalized, new HashSet<string>(StringComparer.Ordinal) { RecordKey });
                cancellationToken.ThrowIfCancellationRequested();
                if (!values.TryGetValue(RecordKey, out Item item) || item == null)
                    return new SharedARCardStoreResult(SharedARCardStoreStatus.NotFound);

                string json = item.Value.GetAs<string>();
                ARCardRecord record = Parse(json, normalized);
                if (record == null)
                    return new SharedARCardStoreResult(SharedARCardStoreStatus.InvalidRecord);
                ARCardLocalCache.TryWrite(record);
                return new SharedARCardStoreResult(SharedARCardStoreStatus.Success, record);
            }
            catch (OperationCanceledException)
            {
                return new SharedARCardStoreResult(SharedARCardStoreStatus.Cancelled);
            }
            catch (Exception exception)
            {
                if (ARCardLocalCache.TryRead(normalized, out ARCardRecord cached))
                    return new SharedARCardStoreResult(SharedARCardStoreStatus.LocalCacheOnly, cached,
                        "Cloud lookup failed; showing this device's cached copy. " + exception.Message);
                return new SharedARCardStoreResult(SharedARCardStoreStatus.CloudUnavailable, message: exception.Message);
            }
        }

        internal static ARCardRecord Parse(string json, string expectedCardId)
        {
            if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > MaximumJsonBytes)
                return null;
            try
            {
                ARCardRecord record = JsonUtility.FromJson<ARCardRecord>(json);
                return record != null && record.IsValidBasicShape() &&
                       string.Equals(record.cardId, expectedCardId, StringComparison.Ordinal)
                    ? record : null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static string NormalizeCardId(string value)
        {
            string normalized = value == null ? string.Empty : value.Trim().ToLowerInvariant();
            if (normalized.Length != 32) return string.Empty;
            for (int i = 0; i < normalized.Length; i++)
                if (!Uri.IsHexDigit(normalized[i])) return string.Empty;
            return normalized;
        }
    }

    internal static class ARCardLocalCache
    {
        public static bool TryWrite(ARCardRecord record)
        {
            try
            {
                string directory = Path.Combine(Application.persistentDataPath, "ARCardCache");
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, record.cardId + ".json"),
                    JsonUtility.ToJson(record, false), Encoding.UTF8);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool TryRead(string cardId, out ARCardRecord record)
        {
            record = null;
            try
            {
                string path = Path.Combine(Application.persistentDataPath, "ARCardCache", cardId + ".json");
                if (!File.Exists(path)) return false;
                record = UnitySharedARCardStore.Parse(File.ReadAllText(path, Encoding.UTF8), cardId);
                return record != null;
            }
            catch (Exception)
            {
                record = null;
                return false;
            }
        }
    }
}
