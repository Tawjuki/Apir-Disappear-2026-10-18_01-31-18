using System;
using System.Collections.Generic;
using ApirApir.Battle;
using ApirApir.Cards;

namespace ApirApir.Souls
{
    /// <summary>
    /// Match-lifetime authoritative owner of Soul ownership, Talino, unlocks, and the four-slot
    /// active loadout. Battle instances consume immutable definitions and cannot mutate progression.
    /// </summary>
    public sealed class SoulProgressionService : ICardSkillLoadoutSource
    {
        private sealed class Account
        {
            public Account(string playerId, int talino)
            {
                PlayerId = playerId;
                Talino = talino;
            }

            public string PlayerId { get; }
            public long Revision { get; set; }
            public int Talino { get; set; }
            public string SelectedSoulId { get; set; } = string.Empty;
            public HashSet<string> OwnedSouls { get; } = new HashSet<string>(StringComparer.Ordinal);
            public HashSet<string> UnlockedSkills { get; } = new HashSet<string>(StringComparer.Ordinal);
            public List<string> EquippedSkills { get; } = new List<string>(4);
            public HashSet<string> ProcessedCommands { get; } = new HashSet<string>(StringComparer.Ordinal);
        }

        private readonly object sync = new object();
        private readonly ISoulProgressionAuthority authority;
        private readonly SkillCatalog catalog;
        private readonly SoulCatalog souls;
        private readonly Dictionary<string, Account> accounts =
            new Dictionary<string, Account>(StringComparer.Ordinal);

        public SoulProgressionService(
            ISoulProgressionAuthority authority,
            SkillCatalog catalog = null,
            SoulCatalog souls = null)
        {
            this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
            this.catalog = catalog ?? DefaultSkillCatalog.Create();
            this.souls = souls ?? DefaultSoulCatalog.Create();
        }

        public event Action<SoulProgressionEvent> EventPublished;
        public event Action<SoulProgressionSnapshot> SnapshotChanged;

        public SoulProgressionResult InitializePlayer(string playerId, int initialTalino = 0)
        {
            string player = Normalize(playerId);
            if (!authority.IsAuthoritative)
            {
                return Reject(SoulProgressionFailure.NotAuthoritative, player);
            }

            if (player.Length == 0 || initialTalino < 0)
            {
                return Reject(SoulProgressionFailure.InvalidRequest, player);
            }

            lock (sync)
            {
                if (accounts.TryGetValue(player, out Account existing))
                {
                    return SoulProgressionResult.Success(CreateSnapshot(existing));
                }

                var account = new Account(player, initialTalino);
                accounts.Add(player, account);
                Publish(account, SoulProgressionEventType.PlayerInitialized);
                return Changed(account);
            }
        }

        public SoulProgressionResult GrantSoul(
            string playerId,
            string soulId,
            long expectedRevision,
            string commandId)
        {
            return Mutate(playerId, expectedRevision, commandId, account =>
            {
                string soul = Normalize(soulId);
                if (!souls.Contains(soul)) return SoulProgressionFailure.SoulNotFound;
                if (!account.OwnedSouls.Add(soul)) return SoulProgressionFailure.SoulAlreadyOwned;
                Commit(account, SoulProgressionEventType.SoulGranted, soul);
                return SoulProgressionFailure.None;
            });
        }

        public SoulProgressionResult AddTalino(
            string playerId,
            int amount,
            long expectedRevision,
            string commandId)
        {
            if (amount <= 0)
            {
                return Reject(SoulProgressionFailure.InvalidRequest, Normalize(playerId));
            }

            return Mutate(playerId, expectedRevision, commandId, account =>
            {
                if (account.Talino > int.MaxValue - amount)
                {
                    return SoulProgressionFailure.InvalidRequest;
                }

                account.Talino += amount;
                Commit(account, SoulProgressionEventType.TalinoChanged, value: account.Talino);
                return SoulProgressionFailure.None;
            });
        }

        public SoulProgressionResult SelectSoul(
            string playerId,
            string soulId,
            long expectedRevision,
            string commandId)
        {
            return Mutate(playerId, expectedRevision, commandId, account =>
            {
                string soul = Normalize(soulId);
                if (!souls.Contains(soul)) return SoulProgressionFailure.SoulNotFound;
                if (!account.OwnedSouls.Contains(soul)) return SoulProgressionFailure.SoulNotOwned;

                account.SelectedSoulId = soul;
                for (int i = account.EquippedSkills.Count - 1; i >= 0; i--)
                {
                    SkillDefinition equipped = GetRequiredSkill(account.EquippedSkills[i]);
                    if (equipped.Category == SkillCategory.Soul && equipped.SoulId != soul)
                    {
                        string removed = account.EquippedSkills[i];
                        account.EquippedSkills.RemoveAt(i);
                        Publish(account, SoulProgressionEventType.SkillUnequipped, removed);
                    }
                }

                Commit(account, SoulProgressionEventType.SoulSelected, soul);
                return SoulProgressionFailure.None;
            });
        }

        public SoulProgressionResult UnlockSkill(
            string playerId,
            string skillId,
            long expectedRevision,
            string commandId)
        {
            return Mutate(playerId, expectedRevision, commandId, account =>
            {
                if (!catalog.TryGet(Normalize(skillId), out SkillDefinition skill))
                    return SoulProgressionFailure.SkillNotFound;
                if (skill.Category != SkillCategory.Soul)
                    return SoulProgressionFailure.SkillNotUnlockable;
                if (!account.OwnedSouls.Contains(skill.SoulId))
                    return SoulProgressionFailure.SoulNotOwned;
                if (account.UnlockedSkills.Contains(skill.Id))
                    return SoulProgressionFailure.SkillAlreadyUnlocked;
                if (account.Talino < skill.TalinoUnlockCost)
                    return SoulProgressionFailure.InsufficientTalino;

                account.Talino -= skill.TalinoUnlockCost;
                account.UnlockedSkills.Add(skill.Id);
                Commit(account, SoulProgressionEventType.SkillUnlocked, skill.Id, account.Talino);
                return SoulProgressionFailure.None;
            });
        }

        public SoulProgressionResult EquipSkill(
            string playerId,
            string skillId,
            long expectedRevision,
            string commandId)
        {
            return Mutate(playerId, expectedRevision, commandId, account =>
            {
                if (!catalog.TryGet(Normalize(skillId), out SkillDefinition skill))
                    return SoulProgressionFailure.SkillNotFound;
                if (account.EquippedSkills.Contains(skill.Id))
                    return SoulProgressionFailure.SkillAlreadyEquipped;
                if (account.EquippedSkills.Count >= CardBattleLoadout.MaximumEquippedSkills)
                    return SoulProgressionFailure.SkillLimitReached;
                if (skill.Category == SkillCategory.Soul)
                {
                    if (!account.UnlockedSkills.Contains(skill.Id))
                        return SoulProgressionFailure.SkillNotUnlocked;
                    if (!string.Equals(account.SelectedSoulId, skill.SoulId, StringComparison.Ordinal))
                        return SoulProgressionFailure.SkillNotPermittedBySelectedSoul;
                }
                else if (skill.Category != SkillCategory.Normal)
                {
                    return SoulProgressionFailure.SkillNotUnlockable;
                }

                account.EquippedSkills.Add(skill.Id);
                Commit(account, SoulProgressionEventType.SkillEquipped, skill.Id);
                return SoulProgressionFailure.None;
            });
        }

        public SoulProgressionResult UnequipSkill(
            string playerId,
            string skillId,
            long expectedRevision,
            string commandId)
        {
            return Mutate(playerId, expectedRevision, commandId, account =>
            {
                string skill = Normalize(skillId);
                if (!account.EquippedSkills.Remove(skill))
                    return SoulProgressionFailure.SkillNotEquipped;
                Commit(account, SoulProgressionEventType.SkillUnequipped, skill);
                return SoulProgressionFailure.None;
            });
        }

        public bool TryGetSnapshot(string playerId, out SoulProgressionSnapshot snapshot)
        {
            lock (sync)
            {
                if (accounts.TryGetValue(Normalize(playerId), out Account account))
                {
                    snapshot = CreateSnapshot(account);
                    return true;
                }
            }

            snapshot = null;
            return false;
        }

        public bool TryGetSkills(string playerId, out IReadOnlyList<SkillDefinition> skills)
        {
            lock (sync)
            {
                if (!accounts.TryGetValue(Normalize(playerId), out Account account))
                {
                    skills = Array.Empty<SkillDefinition>();
                    return false;
                }

                var result = new SkillDefinition[account.EquippedSkills.Count];
                for (int i = 0; i < result.Length; i++)
                {
                    result[i] = GetRequiredSkill(account.EquippedSkills[i]);
                }

                skills = result;
                return true;
            }
        }

        private SoulProgressionResult Mutate(
            string playerId,
            long expectedRevision,
            string commandId,
            Func<Account, SoulProgressionFailure> operation)
        {
            string player = Normalize(playerId);
            string command = Normalize(commandId);
            if (!authority.IsAuthoritative)
                return Reject(SoulProgressionFailure.NotAuthoritative, player);
            if (player.Length == 0 || command.Length == 0 || expectedRevision < 0)
                return Reject(SoulProgressionFailure.InvalidRequest, player);

            lock (sync)
            {
                if (!accounts.TryGetValue(player, out Account account))
                    return Reject(SoulProgressionFailure.PlayerNotFound, player);
                if (account.ProcessedCommands.Contains(command))
                    return Reject(SoulProgressionFailure.DuplicateCommand, player, account);
                if (account.Revision != expectedRevision)
                    return Reject(SoulProgressionFailure.RevisionConflict, player, account);

                account.ProcessedCommands.Add(command);
                SoulProgressionFailure failure = operation(account);
                if (failure != SoulProgressionFailure.None)
                    return Reject(failure, player, account);
                return Changed(account);
            }
        }

        private SoulProgressionResult Changed(Account account)
        {
            SoulProgressionSnapshot snapshot = CreateSnapshot(account);
            SnapshotChanged?.Invoke(snapshot);
            return SoulProgressionResult.Success(snapshot);
        }

        private void Commit(
            Account account,
            SoulProgressionEventType type,
            string subjectId = null,
            int value = 0)
        {
            account.Revision++;
            Publish(account, type, subjectId, value);
        }

        private SoulProgressionResult Reject(
            SoulProgressionFailure failure,
            string playerId,
            Account account = null)
        {
            SoulProgressionSnapshot snapshot = account == null ? null : CreateSnapshot(account);
            EventPublished?.Invoke(new SoulProgressionEvent(
                SoulProgressionEventType.RequestRejected,
                playerId,
                account == null ? 0 : account.Revision,
                failure: failure));
            return SoulProgressionResult.Rejected(failure, snapshot);
        }

        private void Publish(
            Account account,
            SoulProgressionEventType type,
            string subjectId = null,
            int value = 0)
        {
            EventPublished?.Invoke(new SoulProgressionEvent(
                type, account.PlayerId, account.Revision, subjectId, value));
        }

        private SoulProgressionSnapshot CreateSnapshot(Account account)
        {
            var unlocked = new List<string>(catalog.NormalSkills.Count + account.UnlockedSkills.Count);
            for (int i = 0; i < catalog.NormalSkills.Count; i++)
                unlocked.Add(catalog.NormalSkills[i].Id);
            unlocked.AddRange(account.UnlockedSkills);
            unlocked.Sort(StringComparer.Ordinal);

            var souls = new List<string>(account.OwnedSouls);
            souls.Sort(StringComparer.Ordinal);
            return new SoulProgressionSnapshot(
                account.PlayerId,
                account.Revision,
                account.Talino,
                account.SelectedSoulId,
                souls,
                unlocked,
                account.EquippedSkills);
        }

        private SkillDefinition GetRequiredSkill(string skillId)
        {
            if (!catalog.TryGet(skillId, out SkillDefinition skill))
                throw new InvalidOperationException("A stored skill is missing from the catalog.");
            return skill;
        }

        private static string Normalize(string value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
