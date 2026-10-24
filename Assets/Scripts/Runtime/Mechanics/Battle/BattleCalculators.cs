using System;
using System.Collections.Generic;

namespace ApirApir.Battle
{
    public static class CardHitPointCalculator
    {
        public static int Calculate(int baseHitPoints, int flatBonus, float percentageBonus)
        {
            if (baseHitPoints <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(baseHitPoints));
            }

            int percentageHitPoints = (int)Math.Round(
                (decimal)baseHitPoints * (decimal)percentageBonus / 100m,
                MidpointRounding.AwayFromZero);
            return Math.Max(1, baseHitPoints + Math.Max(0, flatBonus) + percentageHitPoints);
        }
    }

    public interface IRandomNumberProvider
    {
        double NextUnit();
    }

    public sealed class SystemRandomNumberProvider : IRandomNumberProvider
    {
        private readonly Random random;

        public SystemRandomNumberProvider(int? seed = null)
        {
            random = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        public double NextUnit()
        {
            return random.NextDouble();
        }
    }

    public sealed class CardStatCalculator
    {
        public int CalculateMaximumHitPoints(CardCombatState card, bool retainBrokenPartHitPoints)
        {
            if (card == null)
            {
                throw new ArgumentNullException(nameof(card));
            }

            int flatBonus = 0;
            float percentageBonus = 0f;
            for (int i = 0; i < card.Parts.Count; i++)
            {
                PartCombatState part = card.Parts[i];
                if ((retainBrokenPartHitPoints && part.ContributesHitPointsThisBattle) || part.IsFunctional)
                {
                    flatBonus += part.Definition.AdditionalHitPoints;
                    percentageBonus += part.Definition.HitPointBonusPercent;
                }
            }

            return CardHitPointCalculator.Calculate(card.BaseHitPoints, flatBonus, percentageBonus);
        }

        public float CalculateAttackBonusPercent(CardCombatState card)
        {
            if (card == null)
            {
                throw new ArgumentNullException(nameof(card));
            }

            float result = 0f;
            for (int i = 0; i < card.Parts.Count; i++)
            {
                if (card.Parts[i].IsFunctional)
                {
                    result += card.Parts[i].Definition.AttackBonusPercent;
                }
            }

            for (int i = 0; i < card.TemporaryAttackModifiers.Count; i++)
            {
                result += card.TemporaryAttackModifiers[i].Percent;
            }

            return result;
        }
    }

    public readonly struct DamageCalculationContext
    {
        public DamageCalculationContext(
            CardCombatState source,
            CardCombatState target,
            SkillDefinition skill,
            int skillPower,
            float attackBonusPercent)
        {
            Source = source;
            Target = target;
            Skill = skill;
            SkillPower = skillPower;
            AttackBonusPercent = attackBonusPercent;
        }

        public CardCombatState Source { get; }
        public CardCombatState Target { get; }
        public SkillDefinition Skill { get; }
        public int SkillPower { get; }
        public float AttackBonusPercent { get; }
    }

    public interface IDamageCalculationStrategy
    {
        decimal CalculateUnroundedDamage(DamageCalculationContext context);
    }

    /// <summary>
    /// Provisional rule: positive skill power replaces base ATK; a zero-power hit uses base ATK.
    /// The combined percentage attack bonus is then applied and rounded once, away from zero.
    /// Replace this strategy when the final base-ATK/skill-power formula is approved.
    /// </summary>
    public sealed class ProvisionalDamageCalculationStrategy : IDamageCalculationStrategy
    {
        public decimal CalculateUnroundedDamage(DamageCalculationContext context)
        {
            int rawDamage = context.SkillPower > 0 ? context.SkillPower : context.Source.BaseAttack;
            decimal multiplier = 1m + (decimal)context.AttackBonusPercent / 100m;
            return Math.Max(0m, rawDamage * multiplier);
        }
    }

    public sealed class DamageCalculator
    {
        private readonly CardStatCalculator statCalculator;
        private readonly IDamageCalculationStrategy strategy;

        public DamageCalculator(CardStatCalculator statCalculator, IDamageCalculationStrategy strategy)
        {
            this.statCalculator = statCalculator ?? throw new ArgumentNullException(nameof(statCalculator));
            this.strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        }

        public int Calculate(CardCombatState source, CardCombatState target, SkillDefinition skill, int skillPower)
        {
            return CalculateWithModifiers(
                source,
                target,
                skill,
                skillPower,
                statCalculator.CalculateAttackBonusPercent(source),
                1f,
                false,
                1f);
        }

        public int CalculateWithModifiers(
            CardCombatState source,
            CardCombatState target,
            SkillDefinition skill,
            int skillPower,
            float attackBonusPercent,
            float incomingDamageMultiplier,
            bool critical,
            float criticalDamageMultiplier,
            int flatDamageBonus = 0)
        {
            var context = new DamageCalculationContext(
                source,
                target,
                skill,
                skillPower,
                attackBonusPercent);
            decimal damage = Math.Max(0m, strategy.CalculateUnroundedDamage(context));

            ApplyOutgoingPassives(source, target, skill, ref damage);
            damage += Math.Max(0, flatDamageBonus);
            ApplyIncomingPassives(source, target, skill, ref damage);
            damage *= Math.Max(0m, (decimal)incomingDamageMultiplier);
            if (critical)
            {
                damage *= Math.Max(1m, (decimal)criticalDamageMultiplier);
            }

            return Math.Max(0, (int)Math.Round(damage, MidpointRounding.AwayFromZero));
        }

        private static void ApplyOutgoingPassives(
            CardCombatState source,
            CardCombatState target,
            SkillDefinition skill,
            ref decimal damage)
        {
            for (int i = 0; i < source.Parts.Count; i++)
            {
                PartCombatState part = source.Parts[i];
                if (!part.IsFunctional)
                {
                    continue;
                }

                IReadOnlyList<IPassiveBattleModifier> modifiers = part.Definition.PassiveModifiers;
                for (int j = 0; j < modifiers.Count; j++)
                {
                    damage = Math.Max(0m, modifiers[j].ModifyOutgoingDamage(source, target, skill, damage));
                }
            }
        }

        private static void ApplyIncomingPassives(
            CardCombatState source,
            CardCombatState target,
            SkillDefinition skill,
            ref decimal damage)
        {
            for (int i = 0; i < target.Parts.Count; i++)
            {
                PartCombatState part = target.Parts[i];
                if (!part.IsFunctional)
                {
                    continue;
                }

                IReadOnlyList<IPassiveBattleModifier> modifiers = part.Definition.PassiveModifiers;
                for (int j = 0; j < modifiers.Count; j++)
                {
                    damage = Math.Max(0m, modifiers[j].ModifyIncomingDamage(source, target, skill, damage));
                }
            }
        }
    }

    public sealed class PartMaintenanceSystem
    {
        private readonly IRandomNumberProvider random;

        public PartMaintenanceSystem(IRandomNumberProvider random)
        {
            this.random = random ?? throw new ArgumentNullException(nameof(random));
        }

        public bool TryProcessTear(PartCombatState part, out bool disabled)
        {
            if (part == null)
            {
                throw new ArgumentNullException(nameof(part));
            }

            double roll = random.NextUnit();
            if (!part.IsFunctional || roll >= part.Definition.TearChance)
            {
                disabled = false;
                return false;
            }

            bool wasFunctional = part.IsFunctional;
            part.TryReduceMaintenance();
            disabled = wasFunctional && !part.IsFunctional;
            return true;
        }
    }

    public sealed class KayaKulobResolver
    {
        private readonly IRandomNumberProvider random;

        public KayaKulobResolver(IRandomNumberProvider random)
        {
            this.random = random ?? throw new ArgumentNullException(nameof(random));
        }

        public string ChooseKaya(string firstPlayerId, string secondPlayerId)
        {
            return random.NextUnit() < 0.5d ? firstPlayerId : secondPlayerId;
        }
    }

    public sealed class SkillCooldownSystem
    {
        public void StartCooldown(CardCombatState card, SkillDefinition skill)
        {
            if (skill.CooldownExchanges > 0)
            {
                card.MutableCooldowns[skill.Id] = skill.CooldownExchanges;
            }
        }

        public void AdvanceAfterExchange(
            CardCombatState card,
            ISet<string> skillsUsedThisExchange,
            CooldownConvention convention,
            Action<string, int> onChanged)
        {
            var skillIds = new List<string>(card.MutableCooldowns.Keys);
            for (int i = 0; i < skillIds.Count; i++)
            {
                string skillId = skillIds[i];
                int remaining = card.MutableCooldowns[skillId];
                if (remaining <= 0)
                {
                    continue;
                }

                bool usedNow = skillsUsedThisExchange.Contains(skillId);
                if (usedNow && convention == CooldownConvention.FullExchangesAfterUse)
                {
                    continue;
                }

                int next = remaining - 1;
                card.MutableCooldowns[skillId] = next;
                onChanged?.Invoke(skillId, next);
            }
        }
    }
}
