using System;
using System.Collections.Generic;

namespace ApirApir.Battle
{
    public sealed class CombatModifierCalculator
    {
        private readonly StatusEffectCatalog statuses;
        private readonly BattleSettings settings;

        public CombatModifierCalculator(StatusEffectCatalog statuses, BattleSettings settings)
        {
            this.statuses = statuses ?? throw new ArgumentNullException(nameof(statuses));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public float GetAttackModifierPercent(CardCombatState card) =>
            GetPartModifier(card, value => value.AttackBonusPercent) +
            GetStatusModifier(card, value => value.OutgoingDamageModifierPercent) +
            GetTemporaryAttackModifier(card);

        public float GetSpeedModifierPercent(CardCombatState card) =>
            GetPartModifier(card, value => value.SpeedBonusPercent) +
            GetStatusModifier(card, value => value.SpeedModifierPercent);

        public float GetCriticalChance(CardCombatState card)
        {
            float modifier = GetPartModifier(card, value => value.CriticalChanceBonusPercent) +
                             GetStatusModifier(card, value => value.CriticalChanceModifierPercent);
            return Clamp01(settings.BaseCriticalChance * (1f + modifier / 100f));
        }

        public float GetDodgeChance(CardCombatState card)
        {
            float speed = GetSpeedModifierPercent(card);
            float direct = GetPartModifier(card, value => value.DodgeChanceBonusPercent) / 100f;
            float result = settings.BaseDodgeChance * (1f + speed / 100f) + direct;
            return Math.Max(settings.MinimumDodgeChance, Math.Min(settings.MaximumDodgeChance, result));
        }

        public float GetIncomingDamageMultiplier(CardCombatState card)
        {
            float modifier = 0f;
            foreach (string statusId in card.Statuses.Keys)
            {
                if (statuses.TryGet(statusId, out StatusEffectDefinition definition) &&
                    !definition.ConsumedByNextDirectHit)
                {
                    modifier += definition.IncomingDamageModifierPercent;
                }
            }

            return Math.Max(0f, 1f + modifier / 100f);
        }

        public float GetHealingReceivedMultiplier(CardCombatState card)
        {
            float modifier = GetStatusModifier(card, value => value.HealingReceivedModifierPercent);
            return Math.Max(0f, 1f + modifier / 100f);
        }

        private float GetStatusModifier(
            CardCombatState card,
            Func<StatusEffectDefinition, float> selector)
        {
            float result = 0f;
            foreach (string statusId in card.Statuses.Keys)
            {
                if (statuses.TryGet(statusId, out StatusEffectDefinition definition))
                {
                    result += selector(definition);
                }
            }

            return result;
        }

        private static float GetPartModifier(
            CardCombatState card,
            Func<PartDefinition, float> selector)
        {
            float result = 0f;
            for (int i = 0; i < card.Parts.Count; i++)
            {
                if (card.Parts[i].IsFunctional)
                {
                    result += selector(card.Parts[i].Definition);
                }
            }

            return result;
        }

        private static float GetTemporaryAttackModifier(CardCombatState card)
        {
            float result = 0f;
            for (int i = 0; i < card.TemporaryAttackModifiers.Count; i++)
            {
                result += card.TemporaryAttackModifiers[i].Percent;
            }

            return result;
        }

        private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));
    }

    public sealed class StatusEffectSystem
    {
        private readonly StatusEffectCatalog catalog;
        private readonly StatusReapplicationPolicy reapplicationPolicy;
        private readonly IRandomNumberProvider random;
        private readonly PartPassiveRuntimeSystem passives;

        public StatusEffectSystem(
            StatusEffectCatalog catalog,
            StatusReapplicationPolicy reapplicationPolicy,
            IRandomNumberProvider random,
            PartPassiveRuntimeSystem passives = null)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.reapplicationPolicy = reapplicationPolicy;
            this.random = random ?? throw new ArgumentNullException(nameof(random));
            this.passives = passives;
        }

        public StatusEffectCatalog Catalog => catalog;

        public bool TryApply(CardCombatState target, string statusId, out int duration)
        {
            return TryApply(target, statusId, out duration, out _);
        }

        public bool TryApply(
            CardCombatState target,
            string statusId,
            out int duration,
            out bool blockedByImmunity)
        {
            duration = 0;
            blockedByImmunity = false;
            if (target == null || !catalog.TryGet(statusId, out StatusEffectDefinition definition))
            {
                return false;
            }

            if (passives != null && passives.IsImmune(target, definition.Id))
            {
                blockedByImmunity = true;
                return false;
            }

            duration = definition.DurationExchanges;
            if (target.MutableStatuses.TryGetValue(definition.Id, out int current))
            {
                if (reapplicationPolicy == StatusReapplicationPolicy.Ignore)
                {
                    duration = current;
                    return false;
                }

                target.MutableStatuses[definition.Id] = Math.Max(current, definition.DurationExchanges);
                target.StatusesAppliedThisExchange.Add(definition.Id);
                duration = target.MutableStatuses[definition.Id];
                return true;
            }

            target.MutableStatuses.Add(definition.Id, definition.DurationExchanges);
            target.StatusesAppliedThisExchange.Add(definition.Id);
            return true;
        }

        public bool TryRemoveOneNegative(CardCombatState target, out string removedStatusId)
        {
            removedStatusId = string.Empty;
            for (int i = 0; i < catalog.Negative.Count; i++)
            {
                string statusId = catalog.Negative[i].Id;
                if (target.RemoveStatus(statusId))
                {
                    removedStatusId = statusId;
                    return true;
                }
            }

            return false;
        }

        public bool TryPreventAction(
            CardCombatState card,
            out string preventingStatusId,
            out bool consumed)
        {
            preventingStatusId = string.Empty;
            consumed = false;
            for (int i = 0; i < catalog.Negative.Count; i++)
            {
                StatusEffectDefinition definition = catalog.Negative[i];
                if (!card.HasStatus(definition.Id) || definition.ActionPreventionProbability <= 0f)
                {
                    continue;
                }

                bool prevented = random.NextUnit() < definition.ActionPreventionProbability;
                preventingStatusId = definition.Id;
                if (definition.ConsumedByNextActionAttempt)
                {
                    card.RemoveStatus(definition.Id);
                    consumed = true;
                }

                if (prevented)
                {
                    preventingStatusId = definition.Id;
                    return true;
                }
            }

            return false;
        }

        public bool TryConsumeNextHitAmplifier(CardCombatState card, out float multiplier, out string statusId)
        {
            multiplier = 1f;
            statusId = string.Empty;
            for (int i = 0; i < catalog.Negative.Count; i++)
            {
                StatusEffectDefinition definition = catalog.Negative[i];
                if (!definition.ConsumedByNextDirectHit || !card.HasStatus(definition.Id))
                {
                    continue;
                }

                multiplier = Math.Max(0f, 1f + definition.IncomingDamageModifierPercent / 100f);
                statusId = definition.Id;
                card.RemoveStatus(definition.Id);
                return true;
            }

            return false;
        }

        public void ProcessPeriodicEffects(
            CardCombatState card,
            Action<string, int> applyPeriodicDamage,
            Action<string, int> applyPeriodicHealing)
        {
            var activeIds = new List<string>(card.MutableStatuses.Keys);
            for (int i = 0; i < activeIds.Count; i++)
            {
                string statusId = activeIds[i];
                if (card.StatusesAppliedThisExchange.Contains(statusId) ||
                    !catalog.TryGet(statusId, out StatusEffectDefinition definition))
                {
                    continue;
                }

                int damage = definition.PeriodicDamage;
                if (definition.MaximumHitPointDamagePercent > 0f)
                {
                    damage += Math.Max(1, (int)Math.Round(
                        (decimal)card.MaximumHitPoints *
                        (decimal)definition.MaximumHitPointDamagePercent / 100m,
                        MidpointRounding.AwayFromZero));
                }

                if (damage > 0 && random.NextUnit() < definition.PeriodicDamageProbability)
                {
                    applyPeriodicDamage?.Invoke(statusId, damage);
                }

                if (definition.PeriodicHealing > 0)
                {
                    applyPeriodicHealing?.Invoke(statusId, definition.PeriodicHealing);
                }
            }
        }

        public void AdvanceDurations(
            CardCombatState card,
            Action<string, int> onDurationChanged,
            Action<string> onExpired)
        {
            var activeIds = new List<string>(card.MutableStatuses.Keys);
            for (int i = 0; i < activeIds.Count; i++)
            {
                string statusId = activeIds[i];
                if (!catalog.TryGet(statusId, out StatusEffectDefinition definition))
                {
                    card.RemoveStatus(statusId);
                    onExpired?.Invoke(statusId);
                    continue;
                }

                // A duration means future complete exchanges. Applying a one-exchange status as
                // the second action must not make it expire before it can affect an action.
                if (card.StatusesAppliedThisExchange.Remove(statusId) ||
                    definition.ConsumedByNextActionAttempt)
                {
                    continue;
                }

                int next = card.MutableStatuses[statusId] - 1;
                if (next <= 0)
                {
                    card.MutableStatuses.Remove(statusId);
                    onExpired?.Invoke(statusId);
                }
                else
                {
                    card.MutableStatuses[statusId] = next;
                    onDurationChanged?.Invoke(statusId, next);
                }
            }
        }
    }
}
