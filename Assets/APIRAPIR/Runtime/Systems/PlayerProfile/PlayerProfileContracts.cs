using System;
using System.Collections.Generic;

namespace ApirApir.PlayerProfiles
{
    public enum ProfileOperationStatus : byte
    {
        Success,
        Unchanged,
        InvalidPlayerId,
        InvalidPlayerName,
        InvalidCharacterId,
        PlayerNotFound,
        PlayerAlreadyExists,
        Unauthorized,
        PersistenceFailed
    }

    public enum ProfileRepositoryLoadStatus : byte
    {
        Success,
        NotFound,
        CorruptOrUnsupported
    }

    public enum FinalMatchPlacement : byte
    {
        First = 1,
        Second = 2,
        Third = 3,
        Fourth = 4
    }

    public enum StatisticsProcessStatus : byte
    {
        Success,
        NotAuthoritative,
        IncompleteMatch,
        InvalidResult,
        DuplicateMatch,
        PlayerNotFound,
        PersistenceFailed
    }

    public interface IPlayerIdProvider
    {
        string CreatePlayerId();
    }

    public sealed class GuidPlayerIdProvider : IPlayerIdProvider
    {
        public string CreatePlayerId()
        {
            return Guid.NewGuid().ToString("N");
        }
    }

    public interface ICharacterCatalog
    {
        bool Contains(string characterId);
    }

    public interface IPlayerProfileAuthority
    {
        bool IsAuthoritative { get; }
    }

    public interface ICompletedMatchResultSource
    {
        event Action<CompletedMatchResult> MatchCompleted;
    }

    public interface IPlayerProfileRepository
    {
        ProfileRepositoryLoadResult Load();
        bool TrySave(PlayerProfileRepositoryState state);
    }

    public sealed class ProfileRepositoryLoadResult
    {
        public ProfileRepositoryLoadResult(
            ProfileRepositoryLoadStatus status,
            PlayerProfileRepositoryState state = null)
        {
            Status = status;
            State = state;
        }

        public ProfileRepositoryLoadStatus Status { get; }
        public PlayerProfileRepositoryState State { get; }
    }

    /// <summary>
    /// Persistence boundary. Processed match IDs are metadata and are deliberately kept
    /// separate from the six primary profile fields.
    /// </summary>
    public sealed class PlayerProfileRepositoryState
    {
        public PlayerProfileRepositoryState(
            IEnumerable<PlayerProfileSnapshot> profiles,
            IEnumerable<string> processedMatchIds)
        {
            Profiles = CopyProfiles(profiles).AsReadOnly();
            ProcessedMatchIds = CopyStrings(processedMatchIds).AsReadOnly();
        }

        public IReadOnlyList<PlayerProfileSnapshot> Profiles { get; }
        public IReadOnlyList<string> ProcessedMatchIds { get; }

        private static List<PlayerProfileSnapshot> CopyProfiles(
            IEnumerable<PlayerProfileSnapshot> profiles)
        {
            var result = new List<PlayerProfileSnapshot>();
            if (profiles == null)
            {
                return result;
            }

            foreach (PlayerProfileSnapshot profile in profiles)
            {
                if (profile != null)
                {
                    result.Add(new PlayerProfileSnapshot(
                        profile.PlayerId,
                        profile.PlayerName,
                        profile.SelectedCharacterId,
                        profile.GamesPlayed,
                        profile.GamesWon,
                        profile.ThirdPlaceFinishes));
                }
            }

            return result;
        }

        private static List<string> CopyStrings(IEnumerable<string> values)
        {
            var result = new List<string>();
            if (values == null)
            {
                return result;
            }

            foreach (string value in values)
            {
                result.Add(value ?? string.Empty);
            }

            return result;
        }
    }

    public readonly struct PlayerFinalPlacement
    {
        public PlayerFinalPlacement(string playerId, FinalMatchPlacement placement)
        {
            PlayerId = playerId ?? string.Empty;
            Placement = placement;
        }

        public string PlayerId { get; }
        public FinalMatchPlacement Placement { get; }
    }

    public sealed class CompletedMatchResult
    {
        public CompletedMatchResult(
            string matchId,
            bool isCompleted,
            IEnumerable<PlayerFinalPlacement> placements)
        {
            MatchId = matchId ?? string.Empty;
            IsCompleted = isCompleted;
            var copied = new List<PlayerFinalPlacement>();
            if (placements != null)
            {
                copied.AddRange(placements);
            }

            Placements = copied.AsReadOnly();
        }

        public string MatchId { get; }
        public bool IsCompleted { get; }
        public IReadOnlyList<PlayerFinalPlacement> Placements { get; }
    }
}
