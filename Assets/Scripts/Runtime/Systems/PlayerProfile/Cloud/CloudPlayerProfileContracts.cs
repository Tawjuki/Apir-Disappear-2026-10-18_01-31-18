using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ApirApir.PlayerProfiles
{
    public enum CloudProfileSessionState : byte
    {
        Uninitialized,
        AuthenticationStarted,
        ProfileLoading,
        ProfileNeedsName,
        ProfileReady,
        Failed
    }

    public enum CloudProfileFailure : byte
    {
        None,
        AuthenticationUnavailable,
        AuthenticationExpired,
        ProfileNotFound,
        CloudUnavailable,
        Unauthorized,
        CorruptProfile,
        UnsupportedSchema,
        WriteConflict,
        InvalidPlayerName,
        InvalidMatch,
        NotReady,
        Cancelled
    }

    public enum CloudProfileCommandStatus : byte
    {
        Success,
        Unchanged,
        NeedsPlayerName,
        ProfileAlreadyExists,
        InvalidPlayerName,
        InvalidMatch,
        PlayerNotInMatch,
        DuplicateMatch,
        NotReady,
        AuthenticationFailed,
        PersistenceFailed,
        CorruptOrUnsupported,
        Cancelled
    }

    public enum CloudAuthenticationStatus : byte
    {
        Success,
        Unavailable,
        Expired,
        Cancelled
    }

    public readonly struct CloudAuthenticationResult
    {
        public CloudAuthenticationResult(
            CloudAuthenticationStatus status,
            string playerId = null,
            string errorMessage = null)
        {
            Status = status;
            PlayerId = playerId == null ? string.Empty : playerId.Trim();
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public CloudAuthenticationStatus Status { get; }
        public string PlayerId { get; }
        public string ErrorMessage { get; }
        public bool Succeeded => Status == CloudAuthenticationStatus.Success && PlayerId.Length > 0;
    }

    public interface ICloudPlayerIdentity
    {
        string SignedInPlayerId { get; }

        Task<CloudAuthenticationResult> EnsureAuthenticatedAsync(
            CancellationToken cancellationToken = default);
    }

    public enum CloudProfileStoreLoadStatus : byte
    {
        Success,
        NotFound,
        Unavailable,
        Unauthorized,
        Corrupt,
        UnsupportedSchema,
        Cancelled
    }

    public enum CloudProfileStoreSaveStatus : byte
    {
        Success,
        Conflict,
        Unavailable,
        Unauthorized,
        Cancelled
    }

    /// <summary>
    /// Immutable profile record persisted as one Cloud Save item. Keeping the profile and
    /// processed match IDs together prevents a successful count update without its duplicate key.
    /// </summary>
    public sealed class CloudPlayerProfileRecord
    {
        public const int CurrentSchemaVersion = 1;

        public CloudPlayerProfileRecord(
            int schemaVersion,
            string playerId,
            string playerName,
            int gamesPlayed,
            IEnumerable<string> processedMatchIds)
        {
            SchemaVersion = schemaVersion;
            PlayerId = playerId == null ? string.Empty : playerId.Trim();
            PlayerName = playerName ?? string.Empty;
            GamesPlayed = gamesPlayed;

            var copiedMatchIds = new List<string>();
            if (processedMatchIds != null)
            {
                foreach (string matchId in processedMatchIds)
                {
                    copiedMatchIds.Add(matchId ?? string.Empty);
                }
            }

            ProcessedMatchIds = copiedMatchIds.AsReadOnly();
        }

        public int SchemaVersion { get; }
        public string PlayerId { get; }
        public string PlayerName { get; }
        public int GamesPlayed { get; }
        public IReadOnlyList<string> ProcessedMatchIds { get; }
    }

    public sealed class CloudProfileStoreLoadResult
    {
        public CloudProfileStoreLoadResult(
            CloudProfileStoreLoadStatus status,
            CloudPlayerProfileRecord record = null,
            string writeLock = null,
            string errorMessage = null)
        {
            Status = status;
            Record = record;
            WriteLock = writeLock ?? string.Empty;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public CloudProfileStoreLoadStatus Status { get; }
        public CloudPlayerProfileRecord Record { get; }
        public string WriteLock { get; }
        public string ErrorMessage { get; }
    }

    public sealed class CloudProfileStoreSaveResult
    {
        public CloudProfileStoreSaveResult(
            CloudProfileStoreSaveStatus status,
            string writeLock = null,
            string errorMessage = null)
        {
            Status = status;
            WriteLock = writeLock ?? string.Empty;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public CloudProfileStoreSaveStatus Status { get; }
        public string WriteLock { get; }
        public string ErrorMessage { get; }
    }

    public interface ICloudPlayerProfileStore
    {
        Task<CloudProfileStoreLoadResult> LoadAsync(
            string authenticatedPlayerId,
            CancellationToken cancellationToken = default);

        Task<CloudProfileStoreSaveResult> SaveAsync(
            CloudPlayerProfileRecord record,
            string expectedWriteLock,
            CancellationToken cancellationToken = default);
    }
}
