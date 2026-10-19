using System;
using System.Collections.Generic;
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
            int hitPoints = BlankCardHitPoints;
            float attackBonus = 0f;
            var modifiers = new Dictionary<string, float>(StringComparer.Ordinal);
            var passives = new List<string>();

            if (equippedParts != null)
            {
                for (int i = 0; i < equippedParts.Count; i++)
                {
                    OwnedCardPart part = equippedParts[i];
                    if (part == null)
                    {
                        continue;
                    }

                    bool functional = part.CurrentMaintenance > 0;
                    if (functional ||
                        (retainedHitPointInstances != null && retainedHitPointInstances.Contains(part.InstanceId)))
                    {
                        hitPoints += part.Definition.HitPointContribution;
                    }

                    if (!functional)
                    {
                        continue;
                    }

                    attackBonus += part.Definition.AttackBonusPercent;
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
            return new CardEffectiveStats(
                Math.Max(1, hitPoints),
                effectiveAttack,
                attackBonus,
                modifiers,
                passives.AsReadOnly());
        }
    }
}
