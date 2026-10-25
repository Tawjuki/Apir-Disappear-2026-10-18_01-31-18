using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ApirApir.Battle
{
    /// <summary>Authoritative initial tuning for all 33 craftable card parts.</summary>
    public static class DefaultPartDefinitionCatalog
    {
        private static readonly PartPassiveCatalog PassiveCatalog = PartPassiveCatalog.CreateDefault();
        private static readonly IReadOnlyDictionary<string, PartDefinition> Parts =
            new ReadOnlyDictionary<string, PartDefinition>(Build());

        public static IReadOnlyDictionary<string, PartDefinition> Definitions => Parts;
        public static PartPassiveCatalog Passives => PassiveCatalog;

        public static bool TryGet(string partId, out PartDefinition definition) =>
            Parts.TryGetValue(NormalizeId(partId), out definition);

        /// <summary>
        /// Maps identifiers written before Rare Pulis was corrected to Pari. Keeping this
        /// read path allows existing card records to resolve without registering duplicate parts.
        /// </summary>
        public static string NormalizeId(string partId)
        {
            switch (partId ?? string.Empty)
            {
                case "part_rare_pulis_head": return "part_rare_pari_head";
                case "part_rare_pulis_body": return "part_rare_pari_body";
                case "part_rare_pulis_legs": return "part_rare_pari_legs";
                default: return partId ?? string.Empty;
            }
        }

        public static PartDefinition GetRequired(string partId)
        {
            if (!TryGet(partId, out PartDefinition definition))
            {
                throw new KeyNotFoundException("Unknown default card part: " + partId);
            }

            return definition;
        }

        private static Dictionary<string, PartDefinition> Build()
        {
            var result = new Dictionary<string, PartDefinition>(33, StringComparer.Ordinal);

            Add(result, "part_common_magsasaka_head", PartSlot.Head, PartRarity.Common, 15, 12);
            Add(result, "part_common_magsasaka_body", PartSlot.Body, PartRarity.Common, 25, 13);
            Add(result, "part_common_magsasaka_legs", PartSlot.Legs, PartRarity.Common, 12, 10, 5);
            Add(result, "part_common_mangangahoy_head", PartSlot.Head, PartRarity.Common, 18, 16);
            Add(result, "part_common_mangangahoy_body", PartSlot.Body, PartRarity.Common, 28, 15);
            Add(result, "part_common_mangangahoy_legs", PartSlot.Legs, PartRarity.Common, 14, 12, 5);
            Add(result, "part_common_pulis_head", PartSlot.Head, PartRarity.Common, 18, 14, crit: 5);
            Add(result, "part_common_pulis_body", PartSlot.Body, PartRarity.Common, 26, 14);
            Add(result, "part_common_pulis_legs", PartSlot.Legs, PartRarity.Common, 13, 11, 5);
            Add(result, "part_common_mangingisda_head", PartSlot.Head, PartRarity.Common, 16, 12, crit: 5);
            Add(result, "part_common_mangingisda_body", PartSlot.Body, PartRarity.Common, 23, 13);
            Add(result, "part_common_mangingisda_legs", PartSlot.Legs, PartRarity.Common, 15, 11, 10);

            Add(result, "part_rare_abogado_head", PartSlot.Head, PartRarity.Rare, 30, 25, crit: 15,
                passiveIds: new[] { PartPassiveIds.MatibayNaIsip });
            Add(result, "part_rare_abogado_body", PartSlot.Body, PartRarity.Rare, 42, 23,
                passiveIds: new[] { PartPassiveIds.HulingDepensa });
            Add(result, "part_rare_abogado_legs", PartSlot.Legs, PartRarity.Rare, 26, 19, 5);
            Add(result, "part_rare_pari_head", PartSlot.Head, PartRarity.Rare, 35, 29, crit: 10,
                passiveIds: new[] { PartPassiveIds.BanalNaPanalangin });
            Add(result, "part_rare_pari_body", PartSlot.Body, PartRarity.Rare, 45, 26,
                passiveIds: new[] { PartPassiveIds.BanalNaPanangga });
            Add(result, "part_rare_pari_legs", PartSlot.Legs, PartRarity.Rare, 28, 22, 10);
            Add(result, "part_rare_manggagayuma_head", PartSlot.Head, PartRarity.Rare, 28, 25, crit: 10,
                passiveIds: new[] { PartPassiveIds.MahikangPaggaling });
            Add(result, "part_rare_manggagayuma_body", PartSlot.Body, PartRarity.Rare, 38, 22,
                passiveIds: new[] { PartPassiveIds.MalinisNaGayuma });
            Add(result, "part_rare_manggagayuma_legs", PartSlot.Legs, PartRarity.Rare, 27, 19, 10);
            Add(result, "part_rare_mangkukulam_head", PartSlot.Head, PartRarity.Rare, 32, 27, crit: 10,
                passiveIds: new[] { PartPassiveIds.SumpangDagdag });
            Add(result, "part_rare_mangkukulam_body", PartSlot.Body, PartRarity.Rare, 40, 25,
                passiveIds: new[] { PartPassiveIds.PanlabanSaLamig });
            Add(result, "part_rare_mangkukulam_legs", PartSlot.Legs, PartRarity.Rare, 29, 21, 5, 5);

            Add(result, "part_epic_bakunawa_head", PartSlot.Head, PartRarity.Epic, 70, 50, crit: 10,
                passiveIds: new[] { PartPassiveIds.NgipinNgDragon });
            Add(result, "part_epic_bakunawa_body", PartSlot.Body, PartRarity.Epic, 65, 43,
                passiveIds: new[]
                {
                    PartPassiveIds.BalatNgDragonSunog,
                    PartPassiveIds.BalatNgDragonLaban
                });
            Add(result, "part_epic_bakunawa_legs", PartSlot.Legs, PartRarity.Epic, 50, 31, 15,
                passiveIds: new[] { PartPassiveIds.KaliskisNgBakunawa });
            Add(result, "part_epic_lalahon_head", PartSlot.Head, PartRarity.Epic, 65, 48, crit: 10,
                passiveIds: new[] { PartPassiveIds.PootNgBulkan });
            Add(result, "part_epic_lalahon_body", PartSlot.Body, PartRarity.Epic, 70, 42,
                passiveIds: new[] { PartPassiveIds.DugoNgBulkan });
            Add(result, "part_epic_lalahon_legs", PartSlot.Legs, PartRarity.Epic, 52, 31, 10, 5,
                new[] { PartPassiveIds.Naglalagablab });
            Add(result, "part_epic_mayari_head", PartSlot.Head, PartRarity.Epic, 60, 46, crit: 25,
                passiveIds: new[]
                {
                    PartPassiveIds.MataNgBuwanImmunity,
                    PartPassiveIds.MataNgBuwanHealing
                });
            Add(result, "part_epic_mayari_body", PartSlot.Body, PartRarity.Epic, 62, 40, crit: 10,
                passiveIds: new[] { PartPassiveIds.LiwanagNgBuwan });
            Add(result, "part_epic_mayari_legs", PartSlot.Legs, PartRarity.Epic, 55, 29, 20, 10,
                new[] { PartPassiveIds.HakbangNgBuwan });

            if (result.Count != 33)
            {
                throw new InvalidOperationException("The default card-part catalog must contain exactly 33 parts.");
            }

            return result;
        }

        private static void Add(
            IDictionary<string, PartDefinition> destination,
            string id,
            PartSlot slot,
            PartRarity rarity,
            float hitPoints,
            float attack,
            float speed = 0f,
            float crit = 0f,
            IReadOnlyList<string> passiveIds = null)
        {
            PartPassiveDefinition[] passives;
            if (passiveIds == null || passiveIds.Count == 0)
            {
                passives = Array.Empty<PartPassiveDefinition>();
            }
            else
            {
                passives = new PartPassiveDefinition[passiveIds.Count];
                for (int i = 0; i < passives.Length; i++)
                {
                    passives[i] = PassiveCatalog.GetRequired(passiveIds[i]);
                }
            }

            destination.Add(id, new PartDefinition(
                id,
                slot,
                rarity,
                attackBonusPercent: attack,
                speedBonusPercent: speed,
                criticalChanceBonusPercent: crit,
                hitPointBonusPercent: hitPoints,
                passiveAbilities: passives));
        }
    }
}
