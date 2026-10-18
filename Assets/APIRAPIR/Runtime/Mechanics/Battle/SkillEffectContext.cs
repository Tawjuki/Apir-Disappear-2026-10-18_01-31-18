using System;

namespace ApirApir.Battle
{
    public sealed class SkillEffectContext
    {
        private readonly BattleResolver resolver;

        internal SkillEffectContext(
            BattleResolver resolver,
            CardCombatState source,
            CardCombatState target,
            SkillDefinition skill)
        {
            this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Target = target ?? throw new ArgumentNullException(nameof(target));
            Skill = skill ?? throw new ArgumentNullException(nameof(skill));
        }

        public CardCombatState Source { get; }
        public CardCombatState Target { get; }
        public SkillDefinition Skill { get; }

        public bool DealDamage(int skillPower)
        {
            return resolver.DealDamage(Source, Target, Skill, skillPower);
        }

        public int HealSource(int amount)
        {
            return resolver.Heal(Source, amount);
        }

        public void ApplyStatusToTarget(string statusId, int durationInExchanges)
        {
            resolver.ApplyStatus(Source, Target, statusId, durationInExchanges);
        }

        public void ApplyTemporaryAttackModifier(float percent, int durationInExchanges, bool applyToSource)
        {
            resolver.ApplyTemporaryAttackModifier(
                Source,
                applyToSource ? Source : Target,
                percent,
                durationInExchanges);
        }
    }
}
