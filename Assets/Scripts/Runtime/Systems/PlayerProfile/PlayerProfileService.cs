using System;
using System.Collections.Generic;

namespace ApirApir.PlayerProfiles
{
    public sealed class PlayerProfileService
    {
        private const int MaximumPlayerIdLength = 128;
        private const int MaximumProfileCount = 1024;
        private const int MaximumMatchIdLength = 128;

        private readonly IPlayerProfileRepository repository;
        private readonly IPlayerIdProvider playerIdProvider;
        private readonly PlayerNamePolicy namePolicy;
        private readonly CharacterSelectionService characterSelection;
        private readonly Dictionary<string, PlayerProfile> profiles;
        private readonly HashSet<string> processedMatchIds;

        public PlayerProfileService(
            IPlayerProfileRepository repository,
            ICharacterCatalog characterCatalog,
            IPlayerIdProvider playerIdProvider = null,
            int maximumPlayerNameLength = PlayerNamePolicy.DefaultMaximumLength)
        {
            this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
            this.playerIdProvider = playerIdProvider ?? new GuidPlayerIdProvider();
            namePolicy = new PlayerNamePolicy(maximumPlayerNameLength);
            characterSelection = new CharacterSelectionService(
                characterCatalog ?? throw new ArgumentNullException(nameof(characterCatalog)));
            profiles = new Dictionary<string, PlayerProfile>(StringComparer.Ordinal);
            processedMatchIds = new HashSet<string>(StringComparer.Ordinal);
        }

        public event Action<PlayerProfileSnapshot> ProfileCreated;
        public event Action<PlayerProfileSnapshot> ProfileLoaded;
        public event Action<PlayerProfileSnapshot> ProfileUpdated;
        public event Action<PlayerProfileSnapshot> PlayerNameChanged;
        public event Action<PlayerProfileSnapshot> CharacterSelectionChanged;
        public event Action<PlayerProfileSnapshot> GamesPlayedUpdated;
        public event Action<PlayerProfileSnapshot> GamesWonUpdated;
        public event Action<PlayerProfileSnapshot> ThirdPlaceFinishesUpdated;
        public event Action<PlayerProfileSnapshot> ProfileSaved;

        public int ProfileCount => profiles.Count;
        public int MaximumPlayerNameLength => namePolicy.MaximumLength;

        public ProfileRepositoryLoadStatus LoadProfiles()
        {
            ProfileRepositoryLoadResult result;
            try
            {
                result = repository.Load();
            }
            catch (Exception)
            {
                return ProfileRepositoryLoadStatus.CorruptOrUnsupported;
            }

            if (result == null || result.Status != ProfileRepositoryLoadStatus.Success ||
                result.State == null)
            {
                return result == null
                    ? ProfileRepositoryLoadStatus.CorruptOrUnsupported
                    : result.Status;
            }

            var loadedProfiles = new Dictionary<string, PlayerProfile>(StringComparer.Ordinal);
            int count = Math.Min(result.State.Profiles.Count, MaximumProfileCount);
            for (int i = 0; i < count; i++)
            {
                if (TryRestore(result.State.Profiles[i], out PlayerProfile profile) &&
                    !loadedProfiles.ContainsKey(profile.PlayerId))
                {
                    loadedProfiles.Add(profile.PlayerId, profile);
                }
            }

            var loadedMatchIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < result.State.ProcessedMatchIds.Count; i++)
            {
                string matchId = NormalizeIdentifier(result.State.ProcessedMatchIds[i]);
                if (matchId.Length > 0 && matchId.Length <= MaximumMatchIdLength)
                {
                    loadedMatchIds.Add(matchId);
                }
            }

            profiles.Clear();
            foreach (KeyValuePair<string, PlayerProfile> pair in loadedProfiles)
            {
                profiles.Add(pair.Key, pair.Value);
            }

            processedMatchIds.Clear();
            processedMatchIds.UnionWith(loadedMatchIds);

            foreach (PlayerProfile profile in profiles.Values)
            {
                ProfileLoaded?.Invoke(profile.CreateSnapshot());
            }

            return ProfileRepositoryLoadStatus.Success;
        }

        public ProfileOperationStatus TryCreateProfile(
            string requestedName,
            string selectedCharacterId,
            out PlayerProfileSnapshot createdProfile,
            string establishedPlayerId = null)
        {
            createdProfile = null;
            if (!namePolicy.TryNormalize(requestedName, out string normalizedName))
            {
                return ProfileOperationStatus.InvalidPlayerName;
            }

            if (!characterSelection.TryValidate(selectedCharacterId, out string characterId))
            {
                return ProfileOperationStatus.InvalidCharacterId;
            }

            string playerId = NormalizeIdentifier(establishedPlayerId);
            if (playerId.Length == 0)
            {
                playerId = CreateUniquePlayerId();
            }

            if (!IsValidIdentifier(playerId))
            {
                return ProfileOperationStatus.InvalidPlayerId;
            }

            if (profiles.ContainsKey(playerId))
            {
                return ProfileOperationStatus.PlayerAlreadyExists;
            }

            var profile = new PlayerProfile(playerId, normalizedName, characterId, 0, 0, 0);
            if (!TryPersist(profile, null, null))
            {
                return ProfileOperationStatus.PersistenceFailed;
            }

            profiles.Add(playerId, profile);
            createdProfile = profile.CreateSnapshot();
            ProfileCreated?.Invoke(createdProfile);
            ProfileSaved?.Invoke(createdProfile);
            return ProfileOperationStatus.Success;
        }

        public bool TryGetProfile(string playerId, out PlayerProfileSnapshot profile)
        {
            profile = null;
            string normalizedId = NormalizeIdentifier(playerId);
            if (!profiles.TryGetValue(normalizedId, out PlayerProfile stored))
            {
                return false;
            }

            profile = stored.CreateSnapshot();
            return true;
        }

        public IReadOnlyList<PlayerProfileSnapshot> GetProfiles()
        {
            var result = new List<PlayerProfileSnapshot>(profiles.Count);
            foreach (PlayerProfile profile in profiles.Values)
            {
                result.Add(profile.CreateSnapshot());
            }

            return result;
        }

        public ProfileOperationStatus TryUpdatePlayerName(
            string authenticatedPlayerId,
            string targetPlayerId,
            string requestedName)
        {
            ProfileOperationStatus ownership = ValidateOwnership(authenticatedPlayerId, targetPlayerId);
            if (ownership != ProfileOperationStatus.Success)
            {
                return ownership;
            }

            if (!namePolicy.TryNormalize(requestedName, out string normalizedName))
            {
                return ProfileOperationStatus.InvalidPlayerName;
            }

            PlayerProfile profile = profiles[NormalizeIdentifier(targetPlayerId)];
            if (string.Equals(profile.PlayerName, normalizedName, StringComparison.Ordinal))
            {
                return ProfileOperationStatus.Unchanged;
            }

            PlayerProfile staged = profile.Copy();
            staged.SetPlayerName(normalizedName);
            if (!TryPersist(staged, profile.PlayerId, null))
            {
                return ProfileOperationStatus.PersistenceFailed;
            }

            profile.SetPlayerName(normalizedName);
            PlayerProfileSnapshot snapshot = profile.CreateSnapshot();
            PlayerNameChanged?.Invoke(snapshot);
            ProfileUpdated?.Invoke(snapshot);
            ProfileSaved?.Invoke(snapshot);
            return ProfileOperationStatus.Success;
        }

        public ProfileOperationStatus TryUpdateCharacter(
            string authenticatedPlayerId,
            string targetPlayerId,
            string requestedCharacterId)
        {
            ProfileOperationStatus ownership = ValidateOwnership(authenticatedPlayerId, targetPlayerId);
            if (ownership != ProfileOperationStatus.Success)
            {
                return ownership;
            }

            if (!characterSelection.TryValidate(requestedCharacterId, out string characterId))
            {
                return ProfileOperationStatus.InvalidCharacterId;
            }

            PlayerProfile profile = profiles[NormalizeIdentifier(targetPlayerId)];
            if (string.Equals(profile.SelectedCharacterId, characterId, StringComparison.Ordinal))
            {
                return ProfileOperationStatus.Unchanged;
            }

            PlayerProfile staged = profile.Copy();
            staged.SetSelectedCharacter(characterId);
            if (!TryPersist(staged, profile.PlayerId, null))
            {
                return ProfileOperationStatus.PersistenceFailed;
            }

            profile.SetSelectedCharacter(characterId);
            PlayerProfileSnapshot snapshot = profile.CreateSnapshot();
            CharacterSelectionChanged?.Invoke(snapshot);
            ProfileUpdated?.Invoke(snapshot);
            ProfileSaved?.Invoke(snapshot);
            return ProfileOperationStatus.Success;
        }

        internal StatisticsProcessStatus TryCommitCompletedMatch(
            string matchId,
            IReadOnlyList<PlayerFinalPlacement> placements)
        {
            string normalizedMatchId = NormalizeIdentifier(matchId);
            if (processedMatchIds.Contains(normalizedMatchId))
            {
                return StatisticsProcessStatus.DuplicateMatch;
            }

            var staged = new Dictionary<string, PlayerProfile>(StringComparer.Ordinal);
            for (int i = 0; i < placements.Count; i++)
            {
                PlayerFinalPlacement placement = placements[i];
                if (!profiles.TryGetValue(placement.PlayerId, out PlayerProfile profile))
                {
                    return StatisticsProcessStatus.PlayerNotFound;
                }

                PlayerProfile copy = profile.Copy();
                copy.ApplyPlacement(placement.Placement);
                staged.Add(copy.PlayerId, copy);
            }

            if (!TryPersist(null, null, staged, normalizedMatchId))
            {
                return StatisticsProcessStatus.PersistenceFailed;
            }

            processedMatchIds.Add(normalizedMatchId);
            for (int i = 0; i < placements.Count; i++)
            {
                PlayerFinalPlacement placement = placements[i];
                PlayerProfile profile = profiles[placement.PlayerId];
                profile.ApplyPlacement(placement.Placement);
                PlayerProfileSnapshot snapshot = profile.CreateSnapshot();
                GamesPlayedUpdated?.Invoke(snapshot);
                if (placement.Placement == FinalMatchPlacement.First)
                {
                    GamesWonUpdated?.Invoke(snapshot);
                }
                else if (placement.Placement == FinalMatchPlacement.Third)
                {
                    ThirdPlaceFinishesUpdated?.Invoke(snapshot);
                }

                ProfileUpdated?.Invoke(snapshot);
                ProfileSaved?.Invoke(snapshot);
            }

            return StatisticsProcessStatus.Success;
        }

        internal bool HasProcessedMatch(string matchId)
        {
            return processedMatchIds.Contains(NormalizeIdentifier(matchId));
        }

        private ProfileOperationStatus ValidateOwnership(
            string authenticatedPlayerId,
            string targetPlayerId)
        {
            string authenticated = NormalizeIdentifier(authenticatedPlayerId);
            string target = NormalizeIdentifier(targetPlayerId);
            if (!IsValidIdentifier(authenticated) || !IsValidIdentifier(target))
            {
                return ProfileOperationStatus.InvalidPlayerId;
            }

            if (!string.Equals(authenticated, target, StringComparison.Ordinal))
            {
                return ProfileOperationStatus.Unauthorized;
            }

            return profiles.ContainsKey(target)
                ? ProfileOperationStatus.Success
                : ProfileOperationStatus.PlayerNotFound;
        }

        private bool TryRestore(PlayerProfileSnapshot snapshot, out PlayerProfile profile)
        {
            profile = null;
            if (snapshot == null || !IsValidIdentifier(snapshot.PlayerId) ||
                !namePolicy.TryNormalize(snapshot.PlayerName, out string normalizedName) ||
                snapshot.GamesPlayed < 0 || snapshot.GamesWon < 0 ||
                snapshot.ThirdPlaceFinishes < 0 ||
                snapshot.GamesWon > snapshot.GamesPlayed ||
                snapshot.ThirdPlaceFinishes > snapshot.GamesPlayed)
            {
                return false;
            }

            string characterId = NormalizeIdentifier(snapshot.SelectedCharacterId);
            if (characterId.Length > CharacterSelectionService.MaximumCharacterIdLength)
            {
                return false;
            }

            profile = new PlayerProfile(
                NormalizeIdentifier(snapshot.PlayerId),
                normalizedName,
                characterId,
                snapshot.GamesPlayed,
                snapshot.GamesWon,
                snapshot.ThirdPlaceFinishes);
            return true;
        }

        private string CreateUniquePlayerId()
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                string candidate;
                try
                {
                    candidate = NormalizeIdentifier(playerIdProvider.CreatePlayerId());
                }
                catch (Exception)
                {
                    continue;
                }

                if (IsValidIdentifier(candidate) && !profiles.ContainsKey(candidate))
                {
                    return candidate;
                }
            }

            return string.Empty;
        }

        private bool TryPersist(
            PlayerProfile replacement,
            string replacementPlayerId,
            IReadOnlyDictionary<string, PlayerProfile> replacements,
            string additionalMatchId = null)
        {
            var snapshots = new List<PlayerProfileSnapshot>(profiles.Count + 1);
            bool addedReplacement = false;
            foreach (PlayerProfile existing in profiles.Values)
            {
                if (replacements != null && replacements.TryGetValue(existing.PlayerId, out PlayerProfile staged))
                {
                    snapshots.Add(staged.CreateSnapshot());
                }
                else if (replacement != null &&
                         string.Equals(existing.PlayerId, replacementPlayerId, StringComparison.Ordinal))
                {
                    snapshots.Add(replacement.CreateSnapshot());
                    addedReplacement = true;
                }
                else
                {
                    snapshots.Add(existing.CreateSnapshot());
                }
            }

            if (replacement != null && !addedReplacement)
            {
                snapshots.Add(replacement.CreateSnapshot());
            }

            var matchIds = new List<string>(processedMatchIds);
            if (!string.IsNullOrEmpty(additionalMatchId))
            {
                matchIds.Add(additionalMatchId);
            }

            try
            {
                return repository.TrySave(new PlayerProfileRepositoryState(snapshots, matchIds));
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool TryPersist(
            PlayerProfile replacement,
            string replacementPlayerId,
            IReadOnlyDictionary<string, PlayerProfile> replacements)
        {
            return TryPersist(replacement, replacementPlayerId, replacements, null);
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
    }
}
