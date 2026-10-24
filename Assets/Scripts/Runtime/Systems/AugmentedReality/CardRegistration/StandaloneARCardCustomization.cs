using System;
using System.Collections.Generic;
using ApirApir.Battle;
using ApirApir.Cards;
using ApirApir.Crafting;

namespace ApirApir.AugmentedReality
{
    /// <summary>
    /// Read-only content catalog used by the standalone CardAR scene. It reuses validated static
    /// definitions, but never reads or mutates crafting inventory, card builds, or battle state.
    /// </summary>
    public sealed class StandaloneARCardCatalog
    {
        private readonly CraftingCatalog parts;
        private readonly CraftablePartDefinition[] heads;
        private readonly CraftablePartDefinition[] bodies;
        private readonly CraftablePartDefinition[] legs;

        public StandaloneARCardCatalog(CraftingCatalog partCatalog)
        {
            parts = partCatalog ?? throw new ArgumentNullException(nameof(partCatalog));
            var headList = new List<CraftablePartDefinition>();
            var bodyList = new List<CraftablePartDefinition>();
            var legList = new List<CraftablePartDefinition>();
            foreach (CraftablePartDefinition part in parts.Parts.Values)
            {
                if (part == null) continue;
                switch (part.Slot)
                {
                    case PartSlot.Head: headList.Add(part); break;
                    case PartSlot.Body: bodyList.Add(part); break;
                    case PartSlot.Legs: legList.Add(part); break;
                }
            }

            Comparison<CraftablePartDefinition> comparison = (left, right) =>
                string.Compare(left.DisplayName, right.DisplayName, StringComparison.Ordinal);
            headList.Sort(comparison);
            bodyList.Sort(comparison);
            legList.Sort(comparison);
            heads = headList.ToArray();
            bodies = bodyList.ToArray();
            legs = legList.ToArray();
        }

        public IReadOnlyList<CraftablePartDefinition> Heads => heads;
        public IReadOnlyList<CraftablePartDefinition> Bodies => bodies;
        public IReadOnlyList<CraftablePartDefinition> Legs => legs;

        public static StandaloneARCardCatalog CreateDefault() =>
            new StandaloneARCardCatalog(DefaultCraftingCatalog.CreateAvailableAssetCatalog());

        public bool TryGetPart(string definitionId, out CraftablePartDefinition part) =>
            parts.Parts.TryGetValue(definitionId ?? string.Empty, out part);

        public bool TryCompose(
            string draftId,
            long revision,
            string headId,
            string bodyId,
            string legsId,
            out StandaloneARCardComposition composition,
            out string error)
        {
            composition = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(draftId) || revision < 0)
            {
                error = "The standalone AR card draft is invalid.";
                return false;
            }

            if (!TryResolve(headId, PartSlot.Head, out CraftablePartDefinition head, out error) ||
                !TryResolve(bodyId, PartSlot.Body, out CraftablePartDefinition body, out error) ||
                !TryResolve(legsId, PartSlot.Legs, out CraftablePartDefinition legsPart, out error))
            {
                return false;
            }

            PartEquipmentRegion occupied = PartEquipmentRegion.None;
            if (!TryOccupy(head, ref occupied) || !TryOccupy(body, ref occupied) ||
                !TryOccupy(legsPart, ref occupied))
            {
                error = "Selected parts overlap. A hybrid Body & Legs part cannot be combined with separate legs.";
                return false;
            }

            CardEffectiveStats statistics = CalculateStatistics(head, body, legsPart);
            composition = new StandaloneARCardComposition(
                draftId.Trim(), revision, head, body, legsPart, statistics);
            return true;
        }

        private bool TryResolve(
            string definitionId,
            PartSlot requiredSlot,
            out CraftablePartDefinition part,
            out string error)
        {
            part = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(definitionId)) return true;
            if (!TryGetPart(definitionId.Trim(), out part) || part == null || part.Slot != requiredSlot)
            {
                part = null;
                error = "The selected " + requiredSlot + " part is not supported by Card AR.";
                return false;
            }
            return true;
        }

        private static bool TryOccupy(CraftablePartDefinition part, ref PartEquipmentRegion occupied)
        {
            if (part == null) return true;
            if ((occupied & part.OccupiedRegions) != 0) return false;
            occupied |= part.OccupiedRegions;
            return true;
        }

        private static CardEffectiveStats CalculateStatistics(params CraftablePartDefinition[] selected)
        {
            int flatHitPoints = 0;
            float hitPointBonus = 0f;
            float attackBonus = 0f;
            float speedBonus = 0f;
            float criticalBonus = 0f;
            var modifiers = new Dictionary<string, float>(StringComparer.Ordinal);
            var passives = new List<string>();
            var counted = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < selected.Length; i++)
            {
                CraftablePartDefinition part = selected[i];
                if (part == null || !counted.Add(part.Id)) continue;
                flatHitPoints += part.HitPointContribution;
                hitPointBonus += part.HitPointBonusPercent;
                attackBonus += part.AttackBonusPercent;
                speedBonus += part.SpeedBonusPercent;
                criticalBonus += part.CriticalChanceBonusPercent;
                foreach (KeyValuePair<string, float> modifier in part.AdditionalStatModifiers)
                {
                    modifiers.TryGetValue(modifier.Key, out float current);
                    modifiers[modifier.Key] = current + modifier.Value;
                }
                for (int j = 0; j < part.PassiveAbilityReferences.Count; j++)
                    passives.Add(part.PassiveAbilityReferences[j]);
            }

            int attack = Math.Max(0, (int)Math.Round(
                AssemblyCardStatCalculator.BlankCardAttack * (1m + (decimal)attackBonus / 100m),
                MidpointRounding.AwayFromZero));
            int hitPoints = CardHitPointCalculator.Calculate(
                AssemblyCardStatCalculator.BlankCardHitPoints, flatHitPoints, hitPointBonus);
            return new CardEffectiveStats(
                hitPoints, attack, attackBonus, modifiers, passives.AsReadOnly(), speedBonus, criticalBonus);
        }
    }

    public sealed class StandaloneARCardComposition
    {
        public StandaloneARCardComposition(
            string draftId,
            long revision,
            CraftablePartDefinition head,
            CraftablePartDefinition body,
            CraftablePartDefinition legs,
            CardEffectiveStats statistics)
        {
            DraftId = draftId ?? string.Empty;
            Revision = revision;
            Head = head;
            Body = body;
            Legs = legs;
            Statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
        }

        public string DraftId { get; }
        public long Revision { get; }
        public CraftablePartDefinition Head { get; }
        public CraftablePartDefinition Body { get; }
        public CraftablePartDefinition Legs { get; }
        public CardEffectiveStats Statistics { get; }
        public string SourceId => "standalone-" + DraftId;
    }
}
