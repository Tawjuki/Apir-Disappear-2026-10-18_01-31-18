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
        private readonly StatusEffectSystem statusSystem;
        private readonly CombatModifierCalculator modifiers;
        private readonly BattleSettings settings;
        private readonly IRandomNumberProvider random;
        private readonly Action<BattleEvent> publish;
        private readonly HashSet<string> defeatedPlayers;

        internal BattleResolver(
            BattleState state,
            DamageCalculator damageCalculator,
            PartMaintenanceSystem maintenanceSystem,
            SkillCooldownSystem cooldownSystem,
            StatusEffectSystem statusSystem,
            CombatModifierCalculator modifiers,
            BattleSettings settings,
            IRandomNumberProvider random,
            Action<BattleEvent> publish)
        {
            this.state = state ?? throw new ArgumentNullException(nameof(state));
            this.damageCalculator = damageCalculator ?? throw new ArgumentNullException(nameof(damageCalculator));
            this.maintenanceSystem = maintenanceSystem ?? throw new ArgumentNullException(nameof(maintenanceSystem));
            this.cooldownSystem = cooldownSystem ?? throw new ArgumentNullException(nameof(cooldownSystem));
            this.statusSystem = statusSystem ?? throw new ArgumentNullException(nameof(statusSystem));
            this.modifiers = modifiers ?? throw new ArgumentNullException(nameof(modifiers));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.random = random ?? throw new ArgumentNullException(nameof(random));
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

            source.TryGetSkill(action.SkillId, out SkillDefinition skill);
            bool isAttack = skill.Execution == null || skill.Execution.DamageInstances > 0;
            if (isAttack)
            {
                bool prevented = statusSystem.TryPreventAction(
                    source, out string evaluatedStatusId, out bool consumed);
                if (consumed)
                {
                    Publish(BattleEventType.StatusEffectRemoved, source.PlayerId, source.PlayerId,
                        evaluatedStatusId);
                }

                if (prevented)
                {
                    Publish(BattleEventType.ActionPrevented, source.PlayerId, target.PlayerId,
                        evaluatedStatusId);
                    return;
                }
            }

            usedSkillIds.Add(skill.Id);
            cooldownSystem.StartCooldown(source, skill);
            if (skill.CooldownExchanges > 0)
            {
                Publish(BattleEventType.CooldownStarted, source.PlayerId, source.PlayerId,
                    skill.Id, skill.CooldownExchanges);
            }

            Publish(BattleEventType.SkillActivated, source.PlayerId, target.PlayerId, skill.Id);
            if (skill.Execution != null)
            {
                ResolveConfiguredSkill(source, target, skill);
                return;
            }

            var context = new SkillEffectContext(this, source, target, skill);
            for (int i = 0; i < skill.Effects.Count; i++)
            {
                skill.Effects[i].Resolve(context);
            }
        }

        private void ResolveConfiguredSkill(
            CardCombatState source,
            CardCombatState target,
            SkillDefinition skill)
        {
            SkillExecutionDefinition execution = skill.Execution;
            if (execution.SpecialRule == SkillSpecialRule.CleanseOneNegative &&
                statusSystem.TryRemoveOneNegative(source, out string removed))
            {
                Publish(BattleEventType.StatusEffectRemoved, source.PlayerId, source.PlayerId, removed);
            }

            if (execution.SpecialRule == SkillSpecialRule.SacrificeHitPoints)
            {
                ApplyNonHitDamage(source, source, skill.Id, execution.SpecialValue);
            }

            int damage = execution.SpecialRule == SkillSpecialRule.PreviousExchangeRetaliation &&
                         source.ReceivedDirectDamagePreviousExchange
                ? execution.AlternateDamage
                : execution.DamagePerInstance;
            int connectedHits = 0;
            for (int i = 0; i < execution.DamageInstances; i++)
            {
                if (DealDamage(source, target, skill, damage))
                {
                    connectedHits++;
                }

                if (target.IsDefeated)
                {
                    break;
                }
            }

            if (execution.Healing > 0)
            {
                Heal(source, execution.Healing);
            }

            for (int i = 0; i < execution.StatusApplications.Count; i++)
            {
                SkillStatusApplication application = execution.StatusApplications[i];
                bool eligible = application.Target == SkillTargetType.Self ||
                                execution.DamageInstances == 0 || connectedHits > 0;
                RollStatusApplication(
                    source,
                    application.Target == SkillTargetType.Self ? source : target,
                    application.StatusId,
                    application.Probability,
                    eligible);
            }

            if (execution.SpecialRule == SkillSpecialRule.RandomNegativeStatus)
            {
                RollRandomStatus(source, target, statusSystem.Catalog.Negative,
                    execution.SpecialProbability, connectedHits > 0);
            }
            else if (execution.SpecialRule == SkillSpecialRule.RandomPositiveStatus)
            {
                RollRandomStatus(source, source, statusSystem.Catalog.Positive,
                    execution.SpecialProbability, true);
            }
        }

        private void RollRandomStatus(
            CardCombatState source,
            CardCombatState target,
            IReadOnlyList<StatusEffectDefinition> pool,
            float probability,
            bool eligible)
        {
            if (!eligible || pool.Count == 0 || probability <= 0f ||
                (probability < 1f && random.NextUnit() >= probability))
            {
                Publish(BattleEventType.StatusApplicationFailed, source.PlayerId, target.PlayerId,
                    "random-status");
                return;
            }

            int index = Math.Min(pool.Count - 1, (int)(random.NextUnit() * pool.Count));
            ApplyRegisteredStatus(source, target, pool[index].Id);
        }

        private void RollStatusApplication(
            CardCombatState source,
            CardCombatState target,
            string statusId,
            float probability,
            bool eligible)
        {
            if (!eligible || probability <= 0f ||
                (probability < 1f && random.NextUnit() >= probability))
            {
                Publish(BattleEventType.StatusApplicationFailed, source.PlayerId, target.PlayerId,
                    statusId, (int)Math.Round(probability * 100f));
                return;
            }

            ApplyRegisteredStatus(source, target, statusId);
        }

        private void ApplyRegisteredStatus(
            CardCombatState source,
            CardCombatState target,
            string statusId)
        {
            if (statusSystem.TryApply(target, statusId, out int duration))
            {
                Publish(BattleEventType.StatusEffectApplied, source.PlayerId, target.PlayerId,
                    statusId, duration);
            }
            else
            {
                Publish(BattleEventType.StatusApplicationFailed, source.PlayerId, target.PlayerId,
                    statusId);
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

            if (random.NextUnit() < modifiers.GetDodgeChance(target))
            {
                Publish(BattleEventType.AttackDodged, source.PlayerId, target.PlayerId, skill.Id);
                return false;
            }

            bool critical = random.NextUnit() < modifiers.GetCriticalChance(source);
            float incomingMultiplier = modifiers.GetIncomingDamageMultiplier(target);
            if (statusSystem.TryConsumeNextHitAmplifier(
                    target, out float nextHitMultiplier, out string consumedStatus))
            {
                incomingMultiplier *= nextHitMultiplier;
                Publish(BattleEventType.StatusEffectRemoved, target.PlayerId, target.PlayerId,
                    consumedStatus);
            }

            int calculated = damageCalculator.CalculateWithModifiers(
                source, target, skill, skillPower, modifiers.GetAttackModifierPercent(source),
                incomingMultiplier, critical, settings.CriticalDamageMultiplier);
            int applied = target.ApplyDamage(calculated);
            if (applied <= 0)
            {
                return false;
            }

            target.ReceivedDirectDamageThisExchange = true;
            Publish(BattleEventType.DirectHit, source.PlayerId, target.PlayerId, skill.Id, applied);
            if (critical)
            {
                Publish(BattleEventType.CriticalHit, source.PlayerId, target.PlayerId, skill.Id, applied);
            }

            PublishDamage(source, target, skill.Id, applied);
            ProcessMaintenance(source, target);
            PublishDefeatIfNeeded(source, target);
            return true;
        }

        private void ProcessMaintenance(CardCombatState source, CardCombatState target)
        {
            for (int i = 0; i < target.Parts.Count; i++)
            {
                PartCombatState part = target.Parts[i];
                if (!maintenanceSystem.TryProcessTear(part, out bool disabled))
                {
                    continue;
                }

                Publish(BattleEventType.PartMaintenanceChanged, source.PlayerId, target.PlayerId,
                    part.Definition.Id, part.CurrentMaintenance, part.Definition.MaximumMaintenance);
                if (disabled)
                {
                    Publish(BattleEventType.PartDisabled, source.PlayerId, target.PlayerId,
                        part.Definition.Id);
                }
            }
        }

        internal int Heal(CardCombatState target, int amount)
        {
            int adjusted = Math.Max(0, (int)Math.Round(
                amount * modifiers.GetHealingReceivedMultiplier(target),
                MidpointRounding.AwayFromZero));
            int applied = target.Heal(adjusted);
            if (applied > 0)
            {
                Publish(BattleEventType.HealingApplied, target.PlayerId, target.PlayerId,
                    target.CardId, applied);
                PublishHitPoints(target, target, target.CardId);
            }

            return applied;
        }

        internal void ApplyStatus(
            CardCombatState source,
            CardCombatState target,
            string statusId,
            int durationInExchanges)
        {
            if (statusSystem.Catalog.TryGet(statusId, out _))
            {
                ApplyRegisteredStatus(source, target, statusId);
                return;
            }

            target.AddStatus(statusId, durationInExchanges);
            Publish(BattleEventType.StatusEffectApplied, source.PlayerId, target.PlayerId,
                statusId, durationInExchanges);
        }

        internal void ProcessPeriodicStatusEffects()
        {
            CardCombatState first = state.Fighters[state.FirstPlayerId];
            CardCombatState second = state.Fighters[state.SecondPlayerId];
            if (!settings.AllowSimultaneousPeriodicDefeat &&
                (first.IsDefeated || second.IsDefeated))
            {
                return;
            }

            ProcessPeriodicStatusEffects(first);
            if (settings.AllowSimultaneousPeriodicDefeat ||
                !first.IsDefeated)
            {
                ProcessPeriodicStatusEffects(second);
            }
        }

        internal void AdvanceStatusDurations()
        {
            AdvanceStatusDurations(state.Fighters[state.FirstPlayerId]);
            AdvanceStatusDurations(state.Fighters[state.SecondPlayerId]);
            state.Fighters[state.FirstPlayerId].CompleteExchangeHistory();
            state.Fighters[state.SecondPlayerId].CompleteExchangeHistory();
        }

        private void ProcessPeriodicStatusEffects(CardCombatState card)
        {
            statusSystem.ProcessPeriodicEffects(
                card,
                (statusId, amount) =>
                {
                    int applied = ApplyNonHitDamage(card, card, statusId, amount);
                    if (applied > 0)
                    {
                        Publish(BattleEventType.PeriodicStatusDamage, card.PlayerId, card.PlayerId,
                            statusId, applied);
                    }
                },
                (statusId, amount) =>
                {
                    int applied = Heal(card, amount);
                    if (applied > 0)
                    {
                        Publish(BattleEventType.PeriodicStatusHealing, card.PlayerId, card.PlayerId,
                            statusId, applied);
                    }
                });
        }

        private void AdvanceStatusDurations(CardCombatState card)
        {
            statusSystem.AdvanceDurations(
                card,
                (statusId, remaining) => Publish(
                    BattleEventType.StatusDurationChanged,
                    card.PlayerId,
                    card.PlayerId,
                    statusId,
                    remaining),
                statusId => Publish(BattleEventType.StatusEffectExpired, card.PlayerId,
                    card.PlayerId, statusId));
        }

        private int ApplyNonHitDamage(
            CardCombatState actor,
            CardCombatState target,
            string subjectId,
            int amount)
        {
            int applied = target.ApplyDamage(amount);
            if (applied > 0)
            {
                PublishDamage(actor, target, subjectId, applied);
                PublishDefeatIfNeeded(actor, target);
            }

            return applied;
        }

        private void PublishDamage(
            CardCombatState source,
            CardCombatState target,
            string subjectId,
            int applied)
        {
            Publish(BattleEventType.DamageReceived, source.PlayerId, target.PlayerId,
                subjectId, applied);
            PublishHitPoints(source, target, target.CardId);
        }

        private void PublishHitPoints(
            CardCombatState source,
            CardCombatState target,
            string subjectId)
        {
            Publish(BattleEventType.HitPointsChanged, source.PlayerId, target.PlayerId,
                subjectId, target.CurrentHitPoints, target.MaximumHitPoints);
        }

        private void PublishDefeatIfNeeded(CardCombatState source, CardCombatState target)
        {
            if (target.IsDefeated && defeatedPlayers.Add(target.PlayerId))
            {
                Publish(BattleEventType.CardDefeated, source.PlayerId, target.PlayerId, target.CardId);
            }
        }

        internal void ApplyTemporaryAttackModifier(
            CardCombatState source,
            CardCombatState target,
            float percent,
            int durationInExchanges)
        {
            target.AddTemporaryAttackModifier(percent, durationInExchanges);
            Publish(BattleEventType.TemporaryModifierApplied, source.PlayerId, target.PlayerId,
                "attack-percent", (int)Math.Round(percent, MidpointRounding.AwayFromZero),
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
            publish(new BattleEvent(type, state.BattleId, state.ExchangeNumber, actorPlayerId,
                targetPlayerId, subjectId, value, secondaryValue));
        }
    }
}
