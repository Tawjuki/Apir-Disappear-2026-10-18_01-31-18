using System;
using System.Collections.Generic;

namespace ApirApir.Souls
{
    public enum SoulProgressionFailure : byte
    {
        None,
        NotAuthoritative,
        InvalidRequest,
        PlayerNotFound,
        SoulNotFound,
        SoulNotOwned,
        SoulAlreadyOwned,
        SkillNotFound,
        SkillNotUnlockable,
        SkillAlreadyUnlocked,
        InsufficientTalino,
        SkillNotUnlocked,
        SkillNotPermittedBySelectedSoul,
        SkillAlreadyEquipped,
        SkillNotEquipped,
        SkillLimitReached,
        RevisionConflict,
        DuplicateCommand
    }

    public enum SoulProgressionEventType : byte
    {
        PlayerInitialized,
        SoulGranted,
        SoulSelected,
        TalinoChanged,
        SkillUnlocked,
        SkillEquipped,
        SkillUnequipped,
        RequestRejected
    }

    public interface ISoulProgressionAuthority
    {
        bool IsAuthoritative { get; }
    }

    public sealed class FixedSoulProgressionAuthority : ISoulProgressionAuthority
    {
        public FixedSoulProgressionAuthority(bool isAuthoritative)
        {
            IsAuthoritative = isAuthoritative;
        }

        public bool IsAuthoritative { get; }
    }

    public sealed class SoulProgressionSnapshot
    {
        public SoulProgressionSnapshot(
            string playerId,
            long revision,
            int talino,
            string selectedSoulId,
            IReadOnlyList<string> ownedSoulIds,
            IReadOnlyList<string> unlockedSkillIds,
            IReadOnlyList<string> equippedSkillIds)
        {
            PlayerId = playerId ?? string.Empty;
            Revision = revision;
            Talino = talino;
            SelectedSoulId = selectedSoulId ?? string.Empty;
            OwnedSoulIds = Copy(ownedSoulIds);
            UnlockedSkillIds = Copy(unlockedSkillIds);
            EquippedSkillIds = Copy(equippedSkillIds);
        }

        public string PlayerId { get; }
        public long Revision { get; }
        public int Talino { get; }
        public string SelectedSoulId { get; }
        public IReadOnlyList<string> OwnedSoulIds { get; }
        public IReadOnlyList<string> UnlockedSkillIds { get; }
        public IReadOnlyList<string> EquippedSkillIds { get; }

        private static string[] Copy(IReadOnlyList<string> source)
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
    }

    public readonly struct SoulProgressionResult
    {
        public SoulProgressionResult(
            SoulProgressionFailure failure,
            SoulProgressionSnapshot snapshot)
        {
            Failure = failure;
            Snapshot = snapshot;
        }

        public bool Succeeded => Failure == SoulProgressionFailure.None;
        public SoulProgressionFailure Failure { get; }
        public SoulProgressionSnapshot Snapshot { get; }

        public static SoulProgressionResult Success(SoulProgressionSnapshot snapshot) =>
            new SoulProgressionResult(SoulProgressionFailure.None, snapshot);

        public static SoulProgressionResult Rejected(
            SoulProgressionFailure failure,
            SoulProgressionSnapshot snapshot = null) =>
            new SoulProgressionResult(failure, snapshot);
    }

    public sealed class SoulProgressionEvent
    {
        public SoulProgressionEvent(
            SoulProgressionEventType type,
            string playerId,
            long revision,
            string subjectId = null,
            int value = 0,
            SoulProgressionFailure failure = SoulProgressionFailure.None)
        {
            Type = type;
            PlayerId = playerId ?? string.Empty;
            Revision = revision;
            SubjectId = subjectId ?? string.Empty;
            Value = value;
            Failure = failure;
        }

        public SoulProgressionEventType Type { get; }
        public string PlayerId { get; }
        public long Revision { get; }
        public string SubjectId { get; }
        public int Value { get; }
        public SoulProgressionFailure Failure { get; }
    }
}
