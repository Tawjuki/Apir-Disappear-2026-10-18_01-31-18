using System;
using System.Collections.Generic;

namespace ApirApir.Battle
{
    public sealed class BattleResolver
    {
        private readonly BattleState state;
        private readonly DamageCalculator damageCalculator;
        private readonly PartMaintenanceSystem maintenanceSystem;
        private readonly SkillCooldownSystem cooldownSystem;
        private readonly Action<BattleEvent> publish;
        private readonly HashSet<string> defeatedPlayers;

        internal BattleResolver(
            BattleState state,
            DamageCalculator damageCalculator,
            PartMaintenanceSystem maintenanceSystem,
            SkillCooldownSystem cooldownSystem,
            Action<BattleEvent> publish)
        {
            this.state = state ?? throw new ArgumentNullException(nameof(state));
            this.damageCalculator = damageCalculator ?? throw new ArgumentNullException(nameof(damageCalculator));
            this.maintenanceSystem = maintenanceSystem ?? throw new ArgumentNullException(nameof(maintenanceSystem));
            this.cooldownSystem = cooldownSystem ?? throw new ArgumentNullException(nameof(cooldownSystem));
            this.publish = publish ?? throw new ArgumentNullException(nameof(publish));
            defeatedPlayers = new HashSet<string>(StringComparer.Ordinal);
        }

        internal ActionRejectionReason Validate(BattleAction action)
        {
            if (state.Phase != BattlePhase.SelectingActions)
            {
                return ActionRejectionReason.BattleNotSelecting;
            }

            if (!state.TryGetFighter(action.PlayerId, out CardCombatState fighter))
            {
                return ActionRejectionReason.UnknownPlayer;
            }

            string commandKey = action.PlayerId + "\n" + action.CommandId;
            if (string.IsNullOrWhiteSpace(action.CommandId) || !state.ProcessedCommands.Add(commandKey))
            {
                return ActionRejectionReason.DuplicateCommand;
            }

            if (state.SubmittedActions.ContainsKey(action.PlayerId))
            {
                return ActionRejectionReason.ActionAlreadySubmitted;
            }

            if (fighter.IsDefeated)
            {
                return ActionRejectionReason.FighterDefeated;
            }

            if (action.Kind == BattleActionKind.Pass)
            {
                return ActionRejectionReason.None;
            }

            if (string.IsNullOrWhiteSpace(action.SkillId))
            {
                return ActionRejectionReason.MissingSkill;
            }

            if (!fighter.TryGetSkill(action.SkillId, out _))
            {
                return ActionRejectionReason.SkillNotEquipped;
            }

            return fighter.GetCooldown(action.SkillId) > 0
                ? ActionRejectionReason.SkillOnCooldown
                : ActionRejectionReason.None;
        }

        internal void ResolveAction(BattleAction action, ISet<string> usedSkillIds)
        {
            CardCombatState source = state.Fighters[action.PlayerId];
            CardCombatState target = state.Fighters[state.GetOpponentId(action.PlayerId)];
            if (action.Kind == BattleActionKind.Pass)
            {
                Publish(BattleEventType.ActionSkipped, source.PlayerId);
                return;
            }

            SkillDefinition skill = null;
            source.TryGetSkill(action.SkillId, out skill);
            usedSkillIds.Add(skill.Id);
            cooldownSystem.StartCooldown(source, skill);
            Publish(BattleEventType.SkillActivated, source.PlayerId, target.PlayerId, skill.Id);

            var context = new SkillEffectContext(this, source, target, skill);
            for (int i = 0; i < skill.Effects.Count; i++)
            {
                skill.Effects[i].Resolve(context);
            }
        }

        internal bool DealDamage(
            CardCombatState source,
            CardCombatState target,
            SkillDefinition skill,
            int skillPower)
        {
            if (target.IsDefeated)
            {
                return false;
            }

            int calculated = damageCalculator.Calculate(source, target, skill, skillPower);
            int applied = target.ApplyDamage(calculated);
            if (applied <= 0)
            {
                return false;
            }

            Publish(BattleEventType.DamageReceived, source.PlayerId, target.PlayerId, skill.Id, applied);
            Publish(
                BattleEventType.HitPointsChanged,
                source.PlayerId,
                target.PlayerId,
                target.CardId,
                target.CurrentHitPoints,
                target.MaximumHitPoints);

            for (int i = 0; i < target.Parts.Count; i++)
            {
                PartCombatState part = target.Parts[i];
                if (!maintenanceSystem.TryProcessTear(part, out bool disabled))
                {
                    continue;
                }

                Publish(
                    BattleEventType.PartMaintenanceChanged,
                    source.PlayerId,
                    target.PlayerId,
                    part.Definition.Id,
                    part.CurrentMaintenance,
                    part.Definition.MaximumMaintenance);
                if (disabled)
                {
                    Publish(
                        BattleEventType.PartDisabled,
                        source.PlayerId,
                        target.PlayerId,
                        part.Definition.Id);
                }
            }

            if (target.IsDefeated && defeatedPlayers.Add(target.PlayerId))
            {
                Publish(BattleEventType.CardDefeated, source.PlayerId, target.PlayerId, target.CardId);
            }

            return true;
        }

        internal int Heal(CardCombatState target, int amount)
        {
            int applied = target.Heal(amount);
            if (applied > 0)
            {
                Publish(BattleEventType.HealingApplied, target.PlayerId, target.PlayerId, target.CardId, applied);
                Publish(
                    BattleEventType.HitPointsChanged,
                    target.PlayerId,
                    target.PlayerId,
                    target.CardId,
                    target.CurrentHitPoints,
                    target.MaximumHitPoints);
            }

            return applied;
        }

        internal void ApplyStatus(
            CardCombatState source,
            CardCombatState target,
            string statusId,
            int durationInExchanges)
        {
            target.AddStatus(statusId, durationInExchanges);
            Publish(
                BattleEventType.StatusEffectApplied,
                source.PlayerId,
                target.PlayerId,
                statusId,
                durationInExchanges);
        }

        internal void ApplyTemporaryAttackModifier(
            CardCombatState source,
            CardCombatState target,
            float percent,
            int durationInExchanges)
        {
            target.AddTemporaryAttackModifier(percent, durationInExchanges);
            Publish(
                BattleEventType.TemporaryModifierApplied,
                source.PlayerId,
                target.PlayerId,
                "attack-percent",
                (int)Math.Round(percent, MidpointRounding.AwayFromZero),
                durationInExchanges);
        }

        private void Publish(
            BattleEventType type,
            string actorPlayerId = null,
            string targetPlayerId = null,
            string subjectId = null,
            int value = 0,
            int secondaryValue = 0)
        {
            publish(new BattleEvent(
                type,
                state.BattleId,
                state.ExchangeNumber,
                actorPlayerId,
                targetPlayerId,
                subjectId,
                value,
                secondaryValue));
        }
    }
}
