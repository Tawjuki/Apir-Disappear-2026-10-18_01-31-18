using System;
using System.Collections.Generic;
using ApirApir.Battle;
using ApirApir.Crafting;

namespace ApirApir.Cards
{
    public sealed class AssemblyCardStatCalculator
    {
        public const int BlankCardHitPoints = 100;
        public const int BlankCardAttack = 10;

        public CardEffectiveStats Calculate(
            IReadOnlyList<OwnedCardPart> equippedParts,
            ISet<string> retainedHitPointInstances = null)
        {
            int flatHitPoints = 0;
            float hitPointBonus = 0f;
            float attackBonus = 0f;
            float speedBonus = 0f;
            float criticalBonus = 0f;
            var modifiers = new Dictionary<string, float>(StringComparer.Ordinal);
            var passives = new List<string>();
            var countedInstances = new HashSet<string>(StringComparer.Ordinal);

            if (equippedParts != null)
            {
                for (int i = 0; i < equippedParts.Count; i++)
                {
                    OwnedCardPart part = equippedParts[i];
                    if (part == null || !countedInstances.Add(part.InstanceId))
                    {
                        continue;
                    }

                    bool functional = part.CurrentMaintenance > 0;
                    if (functional ||
                        (retainedHitPointInstances != null && retainedHitPointInstances.Contains(part.InstanceId)))
                    {
                        flatHitPoints += part.Definition.HitPointContribution;
                        hitPointBonus += part.Definition.HitPointBonusPercent;
                    }

                    if (!functional)
                    {
                        continue;
                    }

                    attackBonus += part.Definition.AttackBonusPercent;
                    speedBonus += part.Definition.SpeedBonusPercent;
                    criticalBonus += part.Definition.CriticalChanceBonusPercent;
                    foreach (KeyValuePair<string, float> modifier in part.Definition.AdditionalStatModifiers)
                    {
                        modifiers.TryGetValue(modifier.Key, out float current);
                        modifiers[modifier.Key] = current + modifier.Value;
                    }

                    for (int j = 0; j < part.Definition.PassiveAbilityReferences.Count; j++)
                    {
                        passives.Add(part.Definition.PassiveAbilityReferences[j]);
                    }
                }
            }

            int effectiveAttack = Math.Max(0, (int)Math.Round(
                BlankCardAttack * (1m + (decimal)attackBonus / 100m),
                MidpointRounding.AwayFromZero));
            int hitPoints = CardHitPointCalculator.Calculate(
                BlankCardHitPoints, flatHitPoints, hitPointBonus);
            return new CardEffectiveStats(
                hitPoints,
                effectiveAttack,
                attackBonus,
                modifiers,
                passives.AsReadOnly(),
                speedBonus,
                criticalBonus);
        }
    }
}
