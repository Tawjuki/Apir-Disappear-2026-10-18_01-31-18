using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ApirApir.Battle
{
    internal readonly struct PassiveUsageKey : IEquatable<PassiveUsageKey>
    {
        public PassiveUsageKey(PartCombatState part, PartPassiveDefinition passive)
        {
            Part = part;
            Passive = passive;
        }

        public PartCombatState Part { get; }
        public PartPassiveDefinition Passive { get; }

        public bool Equals(PassiveUsageKey other) =>
            ReferenceEquals(Part, other.Part) && ReferenceEquals(Passive, other.Passive);

        public override bool Equals(object obj) => obj is PassiveUsageKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (RuntimeHelpers.GetHashCode(Part) * 397) ^ RuntimeHelpers.GetHashCode(Passive);
            }
        }
    }

    internal readonly struct PendingPassiveTrigger
    {
        public PendingPassiveTrigger(PassiveUsageKey key, bool oncePerExchange, bool oncePerDuel)
        {
            Key = key;
            OncePerExchange = oncePerExchange;
            OncePerDuel = oncePerDuel;
        }

        public PassiveUsageKey Key { get; }
        public bool OncePerExchange { get; }
        public bool OncePerDuel { get; }
    }

    public sealed class PartCombatState
    {
        internal PartCombatState(EquippedPart equippedPart)
        {
            Definition = equippedPart.Definition;
            CurrentMaintenance = equippedPart.CurrentMaintenance;
            ContributesHitPointsThisBattle = CurrentMaintenance > 0;
        }

        public PartDefinition Definition { get; }
        public int CurrentMaintenance { get; private set; }
        public bool IsFunctional => CurrentMaintenance > 0;
        internal bool ContributesHitPointsThisBattle { get; }

        internal bool TryReduceMaintenance()
        {
            if (CurrentMaintenance <= 0)
            {
                return false;
            }

            CurrentMaintenance--;
            return true;
        }
    }

    public sealed class TimedAttackModifier
    {
        internal TimedAttackModifier(float percent, int remainingExchanges)
        {
            Percent = percent;
            RemainingExchanges = remainingExchanges;
        }

        public float Percent { get; }
        public int RemainingExchanges { get; internal set; }
    }

    public sealed class CardCombatState
    {
        private readonly Dictionary<string, SkillDefinition> skills;
        private readonly Dictionary<string, int> cooldowns;
        private readonly Dictionary<string, int> statuses;
        private readonly HashSet<string> statusesAppliedThisExchange;
        private readonly HashSet<PassiveUsageKey> passivesTriggeredThisExchange;
        private readonly HashSet<PassiveUsageKey> passivesTriggeredThisDuel;
        private readonly List<PendingPassiveTrigger> pendingDirectHitPassives;
        private readonly List<TimedAttackModifier> temporaryAttackModifiers;
        private readonly PartCombatState[] parts;

        internal CardCombatState(CardBattleLoadout loadout, CardStatCalculator statCalculator)
        {
            if (loadout == null)
            {
                throw new ArgumentNullException(nameof(loadout));
            }

            PlayerId = loadout.PlayerId;
            CardId = loadout.CardId;
            BaseHitPoints = loadout.BaseHitPoints;
            BaseAttack = loadout.BaseAttack;

            parts = new PartCombatState[loadout.Parts.Count];
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = new PartCombatState(loadout.Parts[i]);
            }

            skills = new Dictionary<string, SkillDefinition>(StringComparer.Ordinal);
            cooldowns = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < loadout.Skills.Count; i++)
            {
                SkillDefinition skill = loadout.Skills[i];
                skills.Add(skill.Id, skill);
                cooldowns.Add(skill.Id, 0);
            }

            statuses = new Dictionary<string, int>(StringComparer.Ordinal);
            statusesAppliedThisExchange = new HashSet<string>(StringComparer.Ordinal);
            passivesTriggeredThisExchange = new HashSet<PassiveUsageKey>();
            passivesTriggeredThisDuel = new HashSet<PassiveUsageKey>();
            pendingDirectHitPassives = new List<PendingPassiveTrigger>(1);
            temporaryAttackModifiers = new List<TimedAttackModifier>();
            MaximumHitPoints = statCalculator.CalculateMaximumHitPoints(this, true);
            CurrentHitPoints = MaximumHitPoints;
        }

        public string PlayerId { get; }
        public string CardId { get; }
        public int BaseHitPoints { get; }
        public int BaseAttack { get; }
        public int CurrentHitPoints { get; private set; }
        public int MaximumHitPoints { get; private set; }
        public bool IsDefeated => CurrentHitPoints <= 0;
        public IReadOnlyList<PartCombatState> Parts => parts;
        public IReadOnlyDictionary<string, int> Statuses => statuses;
        public IReadOnlyList<TimedAttackModifier> TemporaryAttackModifiers => temporaryAttackModifiers;
        public int EquippedSkillCount => skills.Count;
        public bool ReceivedDirectDamagePreviousExchange { get; internal set; }
        internal bool ReceivedDirectDamageThisExchange { get; set; }

        public bool TryGetSkill(string skillId, out SkillDefinition skill)
        {
            return skills.TryGetValue(skillId ?? string.Empty, out skill);
        }

        public int GetCooldown(string skillId)
        {
            return cooldowns.TryGetValue(skillId ?? string.Empty, out int value) ? value : 0;
        }

        public bool HasAnyAvailableSkill()
        {
            foreach (KeyValuePair<string, SkillDefinition> pair in skills)
            {
                if (GetCooldown(pair.Key) == 0)
                {
                    return true;
                }
            }

            return false;
        }

        internal IEnumerable<SkillDefinition> Skills => skills.Values;
        internal Dictionary<string, int> MutableCooldowns => cooldowns;
        internal Dictionary<string, int> MutableStatuses => statuses;
        internal HashSet<string> StatusesAppliedThisExchange => statusesAppliedThisExchange;

        internal bool WasPassiveTriggeredThisExchange(PassiveUsageKey key) =>
            passivesTriggeredThisExchange.Contains(key);

        internal bool WasPassiveTriggeredThisDuel(PassiveUsageKey key) =>
            passivesTriggeredThisDuel.Contains(key);

        internal void MarkPassiveTriggered(
            PassiveUsageKey key,
            bool exchangeLimited,
            bool duelLimited)
        {
            if (exchangeLimited)
            {
                passivesTriggeredThisExchange.Add(key);
            }

            if (duelLimited)
            {
                passivesTriggeredThisDuel.Add(key);
            }
        }

        internal List<PendingPassiveTrigger> PendingDirectHitPassives => pendingDirectHitPassives;

        public bool HasStatus(string statusId)
        {
            return statuses.ContainsKey(statusId ?? string.Empty);
        }

        internal int ApplyDamage(int amount)
        {
            int applied = Math.Min(Math.Max(0, amount), CurrentHitPoints);
            CurrentHitPoints -= applied;
            return applied;
        }

        internal int Heal(int amount)
        {
            if (IsDefeated)
            {
                return 0;
            }

            int previous = CurrentHitPoints;
            CurrentHitPoints = Math.Min(MaximumHitPoints, CurrentHitPoints + Math.Max(0, amount));
            return CurrentHitPoints - previous;
        }

        internal void AddStatus(string statusId, int duration)
        {
            if (statuses.TryGetValue(statusId, out int existing))
            {
                statuses[statusId] = Math.Max(existing, duration);
            }
            else
            {
                statuses.Add(statusId, duration);
            }
        }

        internal bool RemoveStatus(string statusId)
        {
            string id = statusId ?? string.Empty;
            statusesAppliedThisExchange.Remove(id);
            return statuses.Remove(id);
        }

        internal void AddTemporaryAttackModifier(float percent, int duration)
        {
            temporaryAttackModifiers.Add(new TimedAttackModifier(percent, duration));
        }

        internal void AdvanceTimedEffects()
        {
            for (int i = temporaryAttackModifiers.Count - 1; i >= 0; i--)
            {
                temporaryAttackModifiers[i].RemainingExchanges--;
                if (temporaryAttackModifiers[i].RemainingExchanges <= 0)
                {
                    temporaryAttackModifiers.RemoveAt(i);
                }
            }
        }

        internal void CompleteExchangeHistory()
        {
            ReceivedDirectDamagePreviousExchange = ReceivedDirectDamageThisExchange;
            ReceivedDirectDamageThisExchange = false;
            passivesTriggeredThisExchange.Clear();
        }

        internal void FinalizeAfterBattle(CardStatCalculator statCalculator)
        {
            MaximumHitPoints = statCalculator.CalculateMaximumHitPoints(this, false);
            CurrentHitPoints = Math.Min(CurrentHitPoints, MaximumHitPoints);
        }
    }
}
