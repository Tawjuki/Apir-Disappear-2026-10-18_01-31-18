using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ApirApir.PlayerProfiles
{
    /// <summary>
    /// Coordinates one authenticated account with an asynchronous cloud store. It deliberately
    /// exposes no rename operation: a valid name can only be supplied while creating a profile.
    /// </summary>
    public sealed class CloudPlayerProfileCoordinator : IDisposable
    {
        private const int MaximumPlayerIdLength = 128;
        private const int MaximumMatchIdLength = 128;
        private const int MaximumConflictRetries = 2;

        private readonly ICloudPlayerIdentity identity;
        private readonly ICloudPlayerProfileStore store;
        private readonly PlayerNamePolicy namePolicy;
        private readonly SemaphoreSlim operationGate = new SemaphoreSlim(1, 1);
        private readonly HashSet<string> processedMatchIds =
            new HashSet<string>(StringComparer.Ordinal);

        private ICompletedMatchResultSource matchSource;
        private PlayerProfileSnapshot currentProfile;
        private string currentPlayerId = string.Empty;
        private string writeLock = string.Empty;
        private bool disposed;

        public CloudPlayerProfileCoordinator(
            ICloudPlayerIdentity identity,
            ICloudPlayerProfileStore store,
            int maximumPlayerNameLength = PlayerNamePolicy.DefaultMaximumLength)
        {
            this.identity = identity ?? throw new ArgumentNullException(nameof(identity));
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            namePolicy = new PlayerNamePolicy(maximumPlayerNameLength);
        }

        public CloudProfileSessionState State { get; private set; } =
            CloudProfileSessionState.Uninitialized;

        public CloudProfileFailure LastFailure { get; private set; }
        public string LastError { get; private set; } = string.Empty;
        public string AuthenticatedPlayerId => currentPlayerId;
        public PlayerProfileSnapshot CurrentProfile => Copy(currentProfile);
        public bool IsReady => State == CloudProfileSessionState.ProfileReady && currentProfile != null;

        public event Action<CloudProfileSessionState> StateChanged;
        public event Action<string> AuthenticationSucceeded;
        public event Action<string> ProfileNeedsName;
        public event Action<PlayerProfileSnapshot> ProfileCreated;
        public event Action<PlayerProfileSnapshot> ProfileLoaded;
        public event Action<PlayerProfileSnapshot> ProfileReady;
        public event Action<PlayerProfileSnapshot> GamesPlayedUpdated;
        public event Action<CloudProfileFailure, string> ProfileSaveFailed;
        public event Action ProfileCleared;

        public async Task<CloudProfileCommandStatus> InitializeAsync(
            CancellationToken cancellationToken = default)
        {
            if (!await EnterGateAsync(cancellationToken))
            {
                return CloudProfileCommandStatus.Cancelled;
            }

            try
            {
                ThrowIfDisposed();
                string existingAuthenticatedId = identity.SignedInPlayerId == null
                    ? string.Empty
                    : identity.SignedInPlayerId.Trim();
                if (currentPlayerId.Length > 0 &&
                    !string.Equals(currentPlayerId, existingAuthenticatedId, StringComparison.Ordinal))
                {
                    ClearProfile();
                }

                if (existingAuthenticatedId.Length > 0 &&
                    string.Equals(currentPlayerId, existingAuthenticatedId, StringComparison.Ordinal))
                {
                    if (IsReady)
                    {
                        return CloudProfileCommandStatus.Unchanged;
                    }

                    if (State == CloudProfileSessionState.ProfileNeedsName)
                    {
                        return CloudProfileCommandStatus.NeedsPlayerName;
                    }
                }

                SetState(CloudProfileSessionState.AuthenticationStarted);

                CloudAuthenticationResult authentication;
                try
                {
                    authentication = await identity.EnsureAuthenticatedAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return Fail(CloudProfileFailure.Cancelled, string.Empty,
                        CloudProfileCommandStatus.Cancelled);
                }
                catch (Exception exception)
                {
                    return Fail(CloudProfileFailure.AuthenticationUnavailable, exception.Message,
                        CloudProfileCommandStatus.AuthenticationFailed);
                }

                if (!authentication.Succeeded || !IsValidIdentifier(authentication.PlayerId))
                {
                    CloudProfileFailure failure = authentication.Status == CloudAuthenticationStatus.Expired
                        ? CloudProfileFailure.AuthenticationExpired
                        : authentication.Status == CloudAuthenticationStatus.Cancelled
                            ? CloudProfileFailure.Cancelled
                            : CloudProfileFailure.AuthenticationUnavailable;
                    return Fail(failure, authentication.ErrorMessage,
                        failure == CloudProfileFailure.Cancelled
                            ? CloudProfileCommandStatus.Cancelled
                            : CloudProfileCommandStatus.AuthenticationFailed);
                }

                string authenticatedId = authentication.PlayerId.Trim();
                if (currentPlayerId.Length > 0 &&
                    !string.Equals(currentPlayerId, authenticatedId, StringComparison.Ordinal))
                {
                    ClearProfile();
                }

                currentPlayerId = authenticatedId;
                AuthenticationSucceeded?.Invoke(currentPlayerId);
                SetState(CloudProfileSessionState.ProfileLoading);
                return await LoadCurrentProfileAsync(true, cancellationToken);
            }
            finally
            {
                operationGate.Release();
            }
        }

        public async Task<CloudProfileCommandStatus> CreateProfileAsync(
            string requestedName,
            CancellationToken cancellationToken = default)
        {
            if (!namePolicy.TryNormalize(requestedName, out string normalizedName))
            {
                LastFailure = CloudProfileFailure.InvalidPlayerName;
                return CloudProfileCommandStatus.InvalidPlayerName;
            }

            if (!await EnterGateAsync(cancellationToken))
            {
                return CloudProfileCommandStatus.Cancelled;
            }

            try
            {
                ThrowIfDisposed();
                if (State == CloudProfileSessionState.ProfileReady && currentProfile != null)
                {
                    return CloudProfileCommandStatus.ProfileAlreadyExists;
                }

                if (State != CloudProfileSessionState.ProfileNeedsName ||
                    !IsCurrentAuthenticatedAccount())
                {
                    return CloudProfileCommandStatus.NotReady;
                }

                SetState(CloudProfileSessionState.ProfileLoading);
                CloudProfileStoreLoadResult latest = await SafeLoadAsync(cancellationToken);
                if (latest.Status == CloudProfileStoreLoadStatus.Success)
                {
                    CloudProfileCommandStatus loaded = ApplyLoadedRecord(latest, true);
                    return loaded == CloudProfileCommandStatus.Success
                        ? CloudProfileCommandStatus.ProfileAlreadyExists
                        : loaded;
                }

                if (latest.Status != CloudProfileStoreLoadStatus.NotFound)
                {
                    return FailFromLoad(latest);
                }

                var created = new CloudPlayerProfileRecord(
                    CloudPlayerProfileRecord.CurrentSchemaVersion,
                    currentPlayerId,
                    normalizedName,
                    0,
                    Array.Empty<string>());
                CloudProfileStoreSaveResult saved = await SafeSaveAsync(created, null, cancellationToken);
                if (saved.Status == CloudProfileStoreSaveStatus.Conflict)
                {
                    CloudProfileStoreLoadResult concurrent = await SafeLoadAsync(cancellationToken);
                    if (concurrent.Status == CloudProfileStoreLoadStatus.Success)
                    {
                        CloudProfileCommandStatus loaded = ApplyLoadedRecord(concurrent, true);
                        return loaded == CloudProfileCommandStatus.Success
                            ? CloudProfileCommandStatus.ProfileAlreadyExists
                            : loaded;
                    }

                    return FailFromLoad(concurrent);
                }

                if (saved.Status != CloudProfileStoreSaveStatus.Success)
                {
                    return FailFromSave(saved);
                }

                ApplyRecord(created, saved.WriteLock);
                PlayerProfileSnapshot snapshot = Copy(currentProfile);
                ProfileCreated?.Invoke(snapshot);
                SetState(CloudProfileSessionState.ProfileReady);
                ProfileReady?.Invoke(Copy(snapshot));
                return CloudProfileCommandStatus.Success;
            }
            catch (OperationCanceledException)
            {
                return Fail(CloudProfileFailure.Cancelled, string.Empty,
                    CloudProfileCommandStatus.Cancelled);
            }
            finally
            {
                operationGate.Release();
            }
        }

        public async Task<CloudProfileCommandStatus> ProcessCompletedMatchAsync(
            CompletedMatchResult result,
            CancellationToken cancellationToken = default)
        {
            if (!CompletedMatchPolicy.TryValidate(result, out IReadOnlyList<PlayerFinalPlacement> placements))
            {
                return CloudProfileCommandStatus.InvalidMatch;
            }

            if (!await EnterGateAsync(cancellationToken))
            {
                return CloudProfileCommandStatus.Cancelled;
            }

            try
            {
                ThrowIfDisposed();
                if (!IsReady || !IsCurrentAuthenticatedAccount())
                {
                    return CloudProfileCommandStatus.NotReady;
                }

                bool playerParticipated = false;
                for (int i = 0; i < placements.Count; i++)
                {
                    if (string.Equals(
                            placements[i].PlayerId,
                            currentPlayerId,
                            StringComparison.Ordinal))
                    {
                        playerParticipated = true;
                        break;
                    }
                }

                if (!playerParticipated)
                {
                    return CloudProfileCommandStatus.PlayerNotInMatch;
                }

                string matchId = result.MatchId.Trim();
                if (processedMatchIds.Contains(matchId))
                {
                    return CloudProfileCommandStatus.DuplicateMatch;
                }

                CloudPlayerProfileRecord staged = CreateUpdatedRecord(
                    currentProfile.PlayerName,
                    currentProfile.GamesPlayed + 1,
                    processedMatchIds,
                    matchId);
                string expectedLock = writeLock;

                for (int attempt = 0; attempt <= MaximumConflictRetries; attempt++)
                {
                    CloudProfileStoreSaveResult saved =
                        await SafeSaveAsync(staged, expectedLock, cancellationToken);
                    if (saved.Status == CloudProfileStoreSaveStatus.Success)
                    {
                        ApplyRecord(staged, saved.WriteLock);
                        PlayerProfileSnapshot snapshot = Copy(currentProfile);
                        GamesPlayedUpdated?.Invoke(snapshot);
                        ProfileReady?.Invoke(Copy(snapshot));
                        return CloudProfileCommandStatus.Success;
                    }

                    if (saved.Status != CloudProfileStoreSaveStatus.Conflict ||
                        attempt == MaximumConflictRetries)
                    {
                        return FailFromSave(saved);
                    }

                    CloudProfileStoreLoadResult latest = await SafeLoadAsync(cancellationToken);
                    if (latest.Status != CloudProfileStoreLoadStatus.Success)
                    {
                        return FailFromLoad(latest);
                    }

                    if (!TryValidateRecord(latest.Record, out string normalizedName,
                            out HashSet<string> cloudMatchIds, out CloudProfileFailure validationFailure))
                    {
                        return FailValidation(validationFailure);
                    }

                    if (cloudMatchIds.Contains(matchId))
                    {
                        ApplyRecord(latest.Record, latest.WriteLock);
                        return CloudProfileCommandStatus.DuplicateMatch;
                    }

                    cloudMatchIds.UnionWith(processedMatchIds);
                    staged = CreateUpdatedRecord(
                        normalizedName,
                        Math.Max(latest.Record.GamesPlayed, currentProfile.GamesPlayed) + 1,
                        cloudMatchIds,
                        matchId);
                    expectedLock = latest.WriteLock;
                }

                return CloudProfileCommandStatus.PersistenceFailed;
            }
            catch (OperationCanceledException)
            {
                return Fail(CloudProfileFailure.Cancelled, string.Empty,
                    CloudProfileCommandStatus.Cancelled);
            }
            finally
            {
                operationGate.Release();
            }
        }

        public void Attach(ICompletedMatchResultSource source)
        {
            ThrowIfDisposed();
            if (ReferenceEquals(matchSource, source))
            {
                return;
            }

            Detach();
            matchSource = source ?? throw new ArgumentNullException(nameof(source));
            matchSource.MatchCompleted += OnMatchCompleted;
        }

        public void Detach()
        {
            if (matchSource == null)
            {
                return;
            }

            matchSource.MatchCompleted -= OnMatchCompleted;
            matchSource = null;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Detach();
        }

        private async Task<CloudProfileCommandStatus> LoadCurrentProfileAsync(
            bool publishLoaded,
            CancellationToken cancellationToken)
        {
            CloudProfileStoreLoadResult loaded = await SafeLoadAsync(cancellationToken);
            if (loaded.Status == CloudProfileStoreLoadStatus.NotFound)
            {
                currentProfile = null;
                processedMatchIds.Clear();
                writeLock = string.Empty;
                LastFailure = CloudProfileFailure.ProfileNotFound;
                LastError = string.Empty;
                SetState(CloudProfileSessionState.ProfileNeedsName);
                ProfileNeedsName?.Invoke(currentPlayerId);
                return CloudProfileCommandStatus.NeedsPlayerName;
            }

            if (loaded.Status != CloudProfileStoreLoadStatus.Success)
            {
                return FailFromLoad(loaded);
            }

            return ApplyLoadedRecord(loaded, publishLoaded);
        }

        private CloudProfileCommandStatus ApplyLoadedRecord(
            CloudProfileStoreLoadResult loaded,
            bool publishLoaded)
        {
            if (!TryValidateRecord(loaded.Record, out _, out _, out CloudProfileFailure failure))
            {
                return FailValidation(failure);
            }

            ApplyRecord(loaded.Record, loaded.WriteLock);
            PlayerProfileSnapshot snapshot = Copy(currentProfile);
            if (publishLoaded)
            {
                ProfileLoaded?.Invoke(snapshot);
            }

            SetState(CloudProfileSessionState.ProfileReady);
            ProfileReady?.Invoke(Copy(snapshot));
            return CloudProfileCommandStatus.Success;
        }

        private bool TryValidateRecord(
            CloudPlayerProfileRecord record,
            out string normalizedName,
            out HashSet<string> normalizedMatchIds,
            out CloudProfileFailure failure)
        {
            normalizedName = string.Empty;
            normalizedMatchIds = new HashSet<string>(StringComparer.Ordinal);
            failure = CloudProfileFailure.CorruptProfile;
            if (record == null)
            {
                return false;
            }

            if (record.SchemaVersion != CloudPlayerProfileRecord.CurrentSchemaVersion)
            {
                failure = CloudProfileFailure.UnsupportedSchema;
                return false;
            }

            if (!string.Equals(record.PlayerId, currentPlayerId, StringComparison.Ordinal) ||
                !namePolicy.TryNormalize(record.PlayerName, out normalizedName) ||
                !string.Equals(normalizedName, record.PlayerName, StringComparison.Ordinal) ||
                record.GamesPlayed < 0 || record.ProcessedMatchIds == null)
            {
                return false;
            }

            for (int i = 0; i < record.ProcessedMatchIds.Count; i++)
            {
                string matchId = NormalizeIdentifier(record.ProcessedMatchIds[i]);
                if (matchId.Length == 0 || matchId.Length > MaximumMatchIdLength ||
                    !normalizedMatchIds.Add(matchId))
                {
                    return false;
                }
            }

            failure = CloudProfileFailure.None;
            return true;
        }

        private void ApplyRecord(CloudPlayerProfileRecord record, string newWriteLock)
        {
            currentPlayerId = record.PlayerId;
            currentProfile = new PlayerProfileSnapshot(
                record.PlayerId,
                record.PlayerName,
                string.Empty,
                record.GamesPlayed,
                0,
                0);
            processedMatchIds.Clear();
            for (int i = 0; i < record.ProcessedMatchIds.Count; i++)
            {
                processedMatchIds.Add(record.ProcessedMatchIds[i]);
            }

            writeLock = newWriteLock ?? string.Empty;
            LastFailure = CloudProfileFailure.None;
            LastError = string.Empty;
        }

        private CloudPlayerProfileRecord CreateUpdatedRecord(
            string playerName,
            int gamesPlayed,
            IEnumerable<string> existingMatchIds,
            string additionalMatchId)
        {
            var matchIds = new HashSet<string>(StringComparer.Ordinal);
            if (existingMatchIds != null)
            {
                matchIds.UnionWith(existingMatchIds);
            }

            matchIds.Add(additionalMatchId);
            return new CloudPlayerProfileRecord(
                CloudPlayerProfileRecord.CurrentSchemaVersion,
                currentPlayerId,
                playerName,
                gamesPlayed,
                matchIds);
        }

        private async Task<CloudProfileStoreLoadResult> SafeLoadAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                return await store.LoadAsync(currentPlayerId, cancellationToken) ??
                       new CloudProfileStoreLoadResult(
                           CloudProfileStoreLoadStatus.Unavailable,
                           errorMessage: "Cloud profile load returned no result.");
            }
            catch (OperationCanceledException)
            {
                return new CloudProfileStoreLoadResult(CloudProfileStoreLoadStatus.Cancelled);
            }
            catch (Exception exception)
            {
                return new CloudProfileStoreLoadResult(
                    CloudProfileStoreLoadStatus.Unavailable,
                    errorMessage: exception.Message);
            }
        }

        private async Task<CloudProfileStoreSaveResult> SafeSaveAsync(
            CloudPlayerProfileRecord record,
            string expectedWriteLock,
            CancellationToken cancellationToken)
        {
            try
            {
                return await store.SaveAsync(record, expectedWriteLock, cancellationToken) ??
                       new CloudProfileStoreSaveResult(
                           CloudProfileStoreSaveStatus.Unavailable,
                           errorMessage: "Cloud profile save returned no result.");
            }
            catch (OperationCanceledException)
            {
                return new CloudProfileStoreSaveResult(CloudProfileStoreSaveStatus.Cancelled);
            }
            catch (Exception exception)
            {
                return new CloudProfileStoreSaveResult(
                    CloudProfileStoreSaveStatus.Unavailable,
                    errorMessage: exception.Message);
            }
        }

        private CloudProfileCommandStatus FailFromLoad(CloudProfileStoreLoadResult result)
        {
            switch (result.Status)
            {
                case CloudProfileStoreLoadStatus.Corrupt:
                    return FailValidation(CloudProfileFailure.CorruptProfile);
                case CloudProfileStoreLoadStatus.UnsupportedSchema:
                    return FailValidation(CloudProfileFailure.UnsupportedSchema);
                case CloudProfileStoreLoadStatus.Unauthorized:
                    return Fail(CloudProfileFailure.Unauthorized, result.ErrorMessage,
                        CloudProfileCommandStatus.AuthenticationFailed);
                case CloudProfileStoreLoadStatus.Cancelled:
                    return Fail(CloudProfileFailure.Cancelled, result.ErrorMessage,
                        CloudProfileCommandStatus.Cancelled);
                case CloudProfileStoreLoadStatus.NotFound:
                    return Fail(CloudProfileFailure.ProfileNotFound, result.ErrorMessage,
                        CloudProfileCommandStatus.NotReady);
                default:
                    return Fail(CloudProfileFailure.CloudUnavailable, result.ErrorMessage,
                        CloudProfileCommandStatus.PersistenceFailed);
            }
        }

        private CloudProfileCommandStatus FailFromSave(CloudProfileStoreSaveResult result)
        {
            CloudProfileFailure failure;
            CloudProfileCommandStatus status;
            switch (result.Status)
            {
                case CloudProfileStoreSaveStatus.Conflict:
                    failure = CloudProfileFailure.WriteConflict;
                    status = CloudProfileCommandStatus.PersistenceFailed;
                    break;
                case CloudProfileStoreSaveStatus.Unauthorized:
                    failure = CloudProfileFailure.Unauthorized;
                    status = CloudProfileCommandStatus.AuthenticationFailed;
                    break;
                case CloudProfileStoreSaveStatus.Cancelled:
                    failure = CloudProfileFailure.Cancelled;
                    status = CloudProfileCommandStatus.Cancelled;
                    break;
                default:
                    failure = CloudProfileFailure.CloudUnavailable;
                    status = CloudProfileCommandStatus.PersistenceFailed;
                    break;
            }

            LastFailure = failure;
            LastError = result.ErrorMessage ?? string.Empty;
            ProfileSaveFailed?.Invoke(failure, LastError);
            if (currentProfile == null)
            {
                SetState(CloudProfileSessionState.Failed);
            }

            return status;
        }

        private CloudProfileCommandStatus FailValidation(CloudProfileFailure failure)
        {
            return Fail(failure, string.Empty, CloudProfileCommandStatus.CorruptOrUnsupported);
        }

        private CloudProfileCommandStatus Fail(
            CloudProfileFailure failure,
            string message,
            CloudProfileCommandStatus status)
        {
            LastFailure = failure;
            LastError = message ?? string.Empty;
            if (failure != CloudProfileFailure.ProfileNotFound)
            {
                SetState(CloudProfileSessionState.Failed);
            }

            return status;
        }

        private bool IsCurrentAuthenticatedAccount()
        {
            return currentPlayerId.Length > 0 &&
                   string.Equals(identity.SignedInPlayerId, currentPlayerId, StringComparison.Ordinal);
        }

        private void ClearProfile()
        {
            currentProfile = null;
            currentPlayerId = string.Empty;
            writeLock = string.Empty;
            processedMatchIds.Clear();
            ProfileCleared?.Invoke();
        }

        private void SetState(CloudProfileSessionState state)
        {
            State = state;
            StateChanged?.Invoke(state);
        }

        private async Task<bool> EnterGateAsync(CancellationToken cancellationToken)
        {
            try
            {
                await operationGate.WaitAsync(cancellationToken);
                return true;
            }
            catch (OperationCanceledException)
            {
                LastFailure = CloudProfileFailure.Cancelled;
                return false;
            }
        }

        private void OnMatchCompleted(CompletedMatchResult result)
        {
            _ = ProcessPublishedMatchAsync(result);
        }

        private async Task ProcessPublishedMatchAsync(CompletedMatchResult result)
        {
            try
            {
                await ProcessCompletedMatchAsync(result);
            }
            catch (Exception exception)
            {
                LastFailure = CloudProfileFailure.CloudUnavailable;
                LastError = exception.Message;
                ProfileSaveFailed?.Invoke(LastFailure, LastError);
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(CloudPlayerProfileCoordinator));
            }
        }

        private static bool IsValidIdentifier(string value)
        {
            string normalized = NormalizeIdentifier(value);
            return normalized.Length > 0 && normalized.Length <= MaximumPlayerIdLength;
        }

        private static string NormalizeIdentifier(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }

        private static PlayerProfileSnapshot Copy(PlayerProfileSnapshot source)
        {
            return source == null
                ? null
                : new PlayerProfileSnapshot(
                    source.PlayerId,
                    source.PlayerName,
                    source.SelectedCharacterId,
                    source.GamesPlayed,
                    source.GamesWon,
                    source.ThirdPlaceFinishes);
        }
    }
}
