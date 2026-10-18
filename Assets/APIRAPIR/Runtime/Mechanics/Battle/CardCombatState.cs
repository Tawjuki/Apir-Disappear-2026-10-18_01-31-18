using System;
using System.Collections.Generic;

namespace ApirApir.Battle
{
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

        internal void AddTemporaryAttackModifier(float percent, int duration)
        {
            temporaryAttackModifiers.Add(new TimedAttackModifier(percent, duration));
        }

        internal void AdvanceTimedEffects()
        {
            if (statuses.Count > 0)
            {
                var keys = new List<string>(statuses.Keys);
                for (int i = 0; i < keys.Count; i++)
                {
                    int next = statuses[keys[i]] - 1;
                    if (next <= 0)
                    {
                        statuses.Remove(keys[i]);
                    }
                    else
                    {
                        statuses[keys[i]] = next;
                    }
                }
            }

            for (int i = temporaryAttackModifiers.Count - 1; i >= 0; i--)
            {
                temporaryAttackModifiers[i].RemainingExchanges--;
                if (temporaryAttackModifiers[i].RemainingExchanges <= 0)
                {
                    temporaryAttackModifiers.RemoveAt(i);
                }
            }
        }

        internal void FinalizeAfterBattle(CardStatCalculator statCalculator)
        {
            MaximumHitPoints = statCalculator.CalculateMaximumHitPoints(this, false);
            CurrentHitPoints = Math.Min(CurrentHitPoints, MaximumHitPoints);
        }
    }
}
