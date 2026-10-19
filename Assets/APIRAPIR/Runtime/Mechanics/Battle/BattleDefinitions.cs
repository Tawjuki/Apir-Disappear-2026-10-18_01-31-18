using System;
using System.Collections.Generic;

namespace ApirApir.Battle
{
    public sealed class PartDefinition
    {
        public PartDefinition(
            string id,
            PartSlot slot,
            PartRarity rarity,
            int additionalHitPoints = 0,
            float attackBonusPercent = 0f,
            IReadOnlyList<IPassiveBattleModifier> passiveModifiers = null,
            float speedBonusPercent = 0f,
            float criticalChanceBonusPercent = 0f,
            float dodgeChanceBonusPercent = 0f)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A part requires a stable ID.", nameof(id));
            }

            if (additionalHitPoints < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(additionalHitPoints));
            }

            Id = id;
            Slot = slot;
            Rarity = rarity;
            AdditionalHitPoints = additionalHitPoints;
            AttackBonusPercent = attackBonusPercent;
            SpeedBonusPercent = speedBonusPercent;
            CriticalChanceBonusPercent = criticalChanceBonusPercent;
            DodgeChanceBonusPercent = dodgeChanceBonusPercent;
            PassiveModifiers = Copy(passiveModifiers ?? Array.Empty<IPassiveBattleModifier>());
        }

        public string Id { get; }
        public PartSlot Slot { get; }
        public PartRarity Rarity { get; }
        public int AdditionalHitPoints { get; }
        public float AttackBonusPercent { get; }
        public float SpeedBonusPercent { get; }
        public float CriticalChanceBonusPercent { get; }
        public float DodgeChanceBonusPercent { get; }
        public IReadOnlyList<IPassiveBattleModifier> PassiveModifiers { get; }

        private static IPassiveBattleModifier[] Copy(IReadOnlyList<IPassiveBattleModifier> source)
        {
            var copy = new IPassiveBattleModifier[source.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = source[i] ?? throw new ArgumentException("Passive modifiers cannot contain null entries.");
            }

            return copy;
        }

        public int MaximumMaintenance
        {
            get
            {
                switch (Rarity)
                {
                    case PartRarity.Common:
                        return 20;
                    case PartRarity.Rare:
                        return 15;
                    case PartRarity.Epic:
                        return 10;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        public double TearChance
        {
            get
            {
                switch (Rarity)
                {
                    case PartRarity.Common:
                        return 1d;
                    case PartRarity.Rare:
                        return 0.5d;
                    case PartRarity.Epic:
                        return 0.125d;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }
    }

    public sealed class EquippedPart
    {
        public EquippedPart(PartDefinition definition, int? currentMaintenance = null)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            int maintenance = currentMaintenance ?? definition.MaximumMaintenance;
            if (maintenance < 0 || maintenance > definition.MaximumMaintenance)
            {
                throw new ArgumentOutOfRangeException(nameof(currentMaintenance));
            }

            CurrentMaintenance = maintenance;
        }

        public PartDefinition Definition { get; }
        public int CurrentMaintenance { get; }
    }

    public interface IPassiveBattleModifier
    {
        decimal ModifyOutgoingDamage(CardCombatState source, CardCombatState target, SkillDefinition skill, decimal damage);
        decimal ModifyIncomingDamage(CardCombatState source, CardCombatState target, SkillDefinition skill, decimal damage);
    }

    public interface ISkillEffect
    {
        void Resolve(SkillEffectContext context);
    }

    public sealed class DamageSkillEffect : ISkillEffect
    {
        public DamageSkillEffect(int skillPower, int hitCount = 1)
        {
            if (skillPower < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(skillPower));
            }

            if (hitCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(hitCount));
            }

            SkillPower = skillPower;
            HitCount = hitCount;
        }

        public int SkillPower { get; }
        public int HitCount { get; }

        public void Resolve(SkillEffectContext context)
        {
            for (int hit = 0; hit < HitCount; hit++)
            {
                context.DealDamage(SkillPower);
            }
        }
    }

    public sealed class HealingSkillEffect : ISkillEffect
    {
        public HealingSkillEffect(int amount)
        {
            if (amount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            Amount = amount;
        }

        public int Amount { get; }

        public void Resolve(SkillEffectContext context)
        {
            context.HealSource(Amount);
        }
    }

    public sealed class StatusSkillEffect : ISkillEffect
    {
        public StatusSkillEffect(string statusId, int durationInExchanges)
        {
            if (string.IsNullOrWhiteSpace(statusId))
            {
                throw new ArgumentException("A status requires a stable ID.", nameof(statusId));
            }

            if (durationInExchanges <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(durationInExchanges));
            }

            StatusId = statusId;
            DurationInExchanges = durationInExchanges;
        }

        public string StatusId { get; }
        public int DurationInExchanges { get; }

        public void Resolve(SkillEffectContext context)
        {
            context.ApplyStatusToTarget(StatusId, DurationInExchanges);
        }
    }

    public sealed class TemporaryAttackModifierSkillEffect : ISkillEffect
    {
        public TemporaryAttackModifierSkillEffect(float percent, int durationInExchanges, bool applyToSource = true)
        {
            if (durationInExchanges <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(durationInExchanges));
            }

            Percent = percent;
            DurationInExchanges = durationInExchanges;
            ApplyToSource = applyToSource;
        }

        public float Percent { get; }
        public int DurationInExchanges { get; }
        public bool ApplyToSource { get; }

        public void Resolve(SkillEffectContext context)
        {
            context.ApplyTemporaryAttackModifier(Percent, DurationInExchanges, ApplyToSource);
        }
    }

    public sealed class SkillDefinition
    {
        public SkillDefinition(
            string id,
            SkillCategory category,
            int cooldownExchanges,
            IReadOnlyList<ISkillEffect> effects,
            string displayName = null,
            string description = null,
            string soulId = null,
            int rank = 0,
            int talinoUnlockCost = 0,
            SkillTargetType targetType = SkillTargetType.Enemy,
            SkillExecutionDefinition execution = null)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A skill requires a stable ID.", nameof(id));
            }

            if (cooldownExchanges < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cooldownExchanges));
            }

            if (category == SkillCategory.Normal && cooldownExchanges != 0)
            {
                throw new ArgumentException("Normal skills must have zero cooldown.", nameof(cooldownExchanges));
            }

            if (rank < 0 || rank > 5 || talinoUnlockCost < 0)
            {
                throw new ArgumentOutOfRangeException("Skill rank or Talino cost is invalid.");
            }

            if (category == SkillCategory.Normal &&
                (rank != 0 || talinoUnlockCost != 0 || !string.IsNullOrWhiteSpace(soulId)))
            {
                throw new ArgumentException("Normal skills cannot require a Soul, rank, or Talino.");
            }

            if (category == SkillCategory.Soul &&
                (rank < 1 || rank > 5 || string.IsNullOrWhiteSpace(soulId)))
            {
                throw new ArgumentException("Soul skills require a Soul ID and rank from one to five.");
            }

            Id = id;
            Category = category;
            CooldownExchanges = cooldownExchanges;
            Effects = CopyEffects(effects ?? throw new ArgumentNullException(nameof(effects)));
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName.Trim();
            Description = description == null ? string.Empty : description.Trim();
            SoulId = soulId == null ? string.Empty : soulId.Trim();
            Rank = rank;
            TalinoUnlockCost = talinoUnlockCost;
            TargetType = targetType;
            Execution = execution;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public SkillCategory Category { get; }
        public string SoulId { get; }
        public int Rank { get; }
        public int TalinoUnlockCost { get; }
        public int CooldownExchanges { get; }
        public SkillTargetType TargetType { get; }
        public SkillExecutionDefinition Execution { get; }
        public IReadOnlyList<ISkillEffect> Effects { get; }

        private static ISkillEffect[] CopyEffects(IReadOnlyList<ISkillEffect> source)
        {
            var copy = new ISkillEffect[source.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = source[i] ?? throw new ArgumentException("Skill effects cannot contain null entries.");
            }

            return copy;
        }
    }

    public sealed class CardBattleLoadout
    {
        public const int MaximumEquippedSkills = 4;

        public CardBattleLoadout(
            string playerId,
            string cardId,
            IReadOnlyList<EquippedPart> parts,
            IReadOnlyList<SkillDefinition> skills,
            int baseHitPoints = 100,
            int baseAttack = 10)
        {
            if (string.IsNullOrWhiteSpace(playerId))
            {
                throw new ArgumentException("A loadout requires a player ID.", nameof(playerId));
            }

            if (string.IsNullOrWhiteSpace(cardId))
            {
                throw new ArgumentException("A loadout requires a card ID.", nameof(cardId));
            }

            if (baseHitPoints <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(baseHitPoints));
            }

            if (baseAttack < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(baseAttack));
            }

            parts = parts ?? Array.Empty<EquippedPart>();
            skills = skills ?? Array.Empty<SkillDefinition>();
            if (parts.Count > 3)
            {
                throw new ArgumentException("A card can equip at most three parts.", nameof(parts));
            }

            if (skills.Count > MaximumEquippedSkills)
            {
                throw new ArgumentException("A card can equip at most four skills.", nameof(skills));
            }

            ValidateUniqueParts(parts);
            ValidateUniqueSkills(skills);

            PlayerId = playerId;
            CardId = cardId;
            BaseHitPoints = baseHitPoints;
            BaseAttack = baseAttack;
            Parts = Copy(parts);
            Skills = Copy(skills);
        }

        public string PlayerId { get; }
        public string CardId { get; }
        public int BaseHitPoints { get; }
        public int BaseAttack { get; }
        public IReadOnlyList<EquippedPart> Parts { get; }
        public IReadOnlyList<SkillDefinition> Skills { get; }

        private static void ValidateUniqueParts(IReadOnlyList<EquippedPart> parts)
        {
            var slots = new HashSet<PartSlot>();
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] == null || !slots.Add(parts[i].Definition.Slot))
                {
                    throw new ArgumentException("Equipped parts must be non-null and use unique slots.", nameof(parts));
                }
            }
        }

        private static void ValidateUniqueSkills(IReadOnlyList<SkillDefinition> skills)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < skills.Count; i++)
            {
                if (skills[i] == null || !ids.Add(skills[i].Id))
                {
                    throw new ArgumentException("Equipped skills must be non-null and use unique IDs.", nameof(skills));
                }
            }
        }

        private static T[] Copy<T>(IReadOnlyList<T> source)
        {
            var copy = new T[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                copy[i] = source[i];
            }

            return copy;
        }
    }
}
