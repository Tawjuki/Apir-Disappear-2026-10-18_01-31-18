using System;
using System.Collections.Generic;
using ApirApir.Battle;

namespace ApirApir.Crafting
{
    public interface ICraftingContentResolver
    {
        object ResolveMaterialInventoryDefinition(string materialId);
        PartDefinition ResolvePartDefinition(string partId, PartSlot slot, PartRarity rarity);
        int ResolveMarketValue(string itemId);
    }

    /// <summary>
    /// Code-backed initial content catalog. It can later be replaced by authored data
    /// without changing CraftingCatalog or CraftingController.
    /// </summary>
    public static class DefaultCraftingCatalog
    {
        public static CraftingCatalog Create(ICraftingContentResolver resolver = null)
        {
            var catalog = new CraftingCatalog();
            RegisterMaterials(catalog, resolver);
            RegisterCommonRecipes(catalog, resolver);
            RegisterRareRecipes(catalog, resolver);
            RegisterEpicRecipes(catalog, resolver);
            return catalog;
        }

        /// <summary>
        /// Focused preparation catalog for the character artwork currently present in the
        /// project. It keeps the established 33-part catalog intact while applying the
        /// defense-day recipes from the current implementation brief. Pari is intentionally
        /// not registered until an authoritative rarity/stat definition is supplied.
        /// </summary>
        public static CraftingCatalog CreateAvailableAssetCatalog(
            ICraftingContentResolver resolver = null)
        {
            var catalog = new CraftingCatalog();
            AddMaterial(catalog, resolver, "common_dayami", "Dayami", MaterialRarity.Common);
            AddMaterial(catalog, resolver, "common_tali", "Tali", MaterialRarity.Common);
            AddMaterial(catalog, resolver, "common_tela", "Tela", MaterialRarity.Common);
            AddMaterial(catalog, resolver, "common_goma", "Goma", MaterialRarity.Common);
            AddMaterial(catalog, resolver, "common_bakal", "Bakal", MaterialRarity.Common);
            AddMaterial(catalog, resolver, "common_kandila", "Kandila", MaterialRarity.Common);
            AddMaterial(catalog, resolver, "rare_salamin", "Salamin", MaterialRarity.Rare);
            AddMaterial(catalog, resolver, "rare_tinta", "Tinta", MaterialRarity.Rare);
            AddMaterial(catalog, resolver, "rare_tsapa", "Tsapa", MaterialRarity.Rare);
            AddMaterial(catalog, resolver, "rare_rosaryo", "Rosaryo", MaterialRarity.Rare);
            AddMaterial(catalog, resolver, "epic_ngipin_dragon", "Ngipin ng Dragon", MaterialRarity.Epic);
            AddMaterial(catalog, resolver, "epic_lawas_dragon", "Lawas ng Dragon", MaterialRarity.Epic);
            AddMaterial(catalog, resolver, "epic_kuko_dragon", "Kuko ng Dragon", MaterialRarity.Epic);

            AddCharacter(catalog, resolver, "common_magsasaka", "Magsasaka", PartRarity.Common,
                Req("common_dayami", 2, "common_tali", 1),
                Req("common_tela", 3, "common_tali", 2),
                Req("common_goma", 2, "common_tela", 1));
            AddCharacter(catalog, resolver, "rare_abogado", "Abogado", PartRarity.Rare,
                Req("rare_salamin", 2, "common_tela", 1),
                Req("rare_tinta", 2, "rare_salamin", 1, "common_tela", 2),
                Req("rare_tinta", 2, "common_goma", 1));

            AddPart(catalog, resolver, "epic_bakunawa", "Bakunawa", PartSlot.Head,
                PartRarity.Epic,
                Req("epic_ngipin_dragon", 1, "rare_salamin", 1, "common_bakal", 2));
            AddBakunawaBodyLegsHybrid(catalog, resolver);
            return catalog;
        }

        private static void RegisterMaterials(CraftingCatalog catalog, ICraftingContentResolver resolver)
        {
            AddMaterial(catalog, resolver, "common_tela", "Tela", MaterialRarity.Common);
            AddMaterial(catalog, resolver, "common_tali", "Tali", MaterialRarity.Common);
            AddMaterial(catalog, resolver, "common_kahoy", "Kahoy", MaterialRarity.Common);
            AddMaterial(catalog, resolver, "common_goma", "Goma", MaterialRarity.Common);
            AddMaterial(catalog, resolver, "common_bakal", "Bakal", MaterialRarity.Common);
            AddMaterial(catalog, resolver, "common_dayami", "Dayami", MaterialRarity.Common);

            AddMaterial(catalog, resolver, "rare_salamin", "Salamin", MaterialRarity.Rare);
            AddMaterial(catalog, resolver, "rare_tinta", "Tinta", MaterialRarity.Rare);
            AddMaterial(catalog, resolver, "rare_tsapa", "Tsapa", MaterialRarity.Rare);
            AddMaterial(catalog, resolver, "rare_bote", "Bote", MaterialRarity.Rare);
            AddMaterial(catalog, resolver, "rare_ugat", "Ugat", MaterialRarity.Rare);
            AddMaterial(catalog, resolver, "rare_kandila", "Kandila", MaterialRarity.Rare);

            AddMaterial(catalog, resolver, "epic_ngipin_dragon", "Ngipin ng Dragon", MaterialRarity.Epic);
            AddMaterial(catalog, resolver, "epic_lawas_dragon", "Lawas ng Dragon", MaterialRarity.Epic);
            AddMaterial(catalog, resolver, "epic_kuko_dragon", "Kuko ng Dragon", MaterialRarity.Epic);
            AddMaterial(catalog, resolver, "epic_korona_abo", "Korona ng Abo", MaterialRarity.Epic);
            AddMaterial(catalog, resolver, "epic_puso_bulkan", "Puso ng Bulkan", MaterialRarity.Epic);
            AddMaterial(catalog, resolver, "epic_batong_lava", "Batong Lava", MaterialRarity.Epic);
            AddMaterial(catalog, resolver, "epic_hiyas_buwan", "Hiyas ng Buwan", MaterialRarity.Epic);
            AddMaterial(catalog, resolver, "epic_sinag_buwan", "Sinag ng Buwan", MaterialRarity.Epic);
            AddMaterial(catalog, resolver, "epic_alikabok_bituin", "Alikabok ng Bituin", MaterialRarity.Epic);
        }

        private static void RegisterCommonRecipes(CraftingCatalog catalog, ICraftingContentResolver resolver)
        {
            AddCharacter(catalog, resolver, "common_magsasaka", "Magsasaka", PartRarity.Common,
                Req("common_dayami", 2, "common_tali", 1),
                Req("common_tela", 3, "common_tali", 2),
                Req("common_goma", 2, "common_tela", 1));

            AddCharacter(catalog, resolver, "common_mangangahoy", "Mangangahoy", PartRarity.Common,
                Req("common_tela", 2, "common_tali", 1),
                Req("common_kahoy", 3, "common_bakal", 2),
                Req("common_goma", 2, "common_kahoy", 1));

            AddCharacter(catalog, resolver, "common_pulis", "Pulis", PartRarity.Common,
                Req("common_tela", 2, "common_bakal", 1),
                Req("common_tela", 3, "common_bakal", 2),
                Req("common_goma", 2, "common_bakal", 1));

            AddCharacter(catalog, resolver, "common_mangingisda", "Mangingisda", PartRarity.Common,
                Req("common_dayami", 2, "common_tela", 1),
                Req("common_tali", 3, "common_kahoy", 2),
                Req("common_goma", 2, "common_tali", 1));
        }

        private static void RegisterRareRecipes(CraftingCatalog catalog, ICraftingContentResolver resolver)
        {
            AddCharacter(catalog, resolver, "rare_abogado", "Abogado", PartRarity.Rare,
                Req("rare_salamin", 2, "common_tela", 1),
                Req("rare_tinta", 2, "rare_salamin", 1, "common_tela", 2),
                Req("rare_tinta", 2, "common_goma", 1));

            AddCharacter(catalog, resolver, "rare_pulis", "Pulis", PartRarity.Rare,
                Req("rare_tsapa", 2, "common_bakal", 1),
                Req("rare_tsapa", 2, "rare_tinta", 1, "common_tela", 2),
                Req("rare_tsapa", 1, "rare_tinta", 1, "common_goma", 1));

            AddCharacter(catalog, resolver, "rare_manggagayuma", "Manggagayuma", PartRarity.Rare,
                Req("rare_bote", 1, "rare_salamin", 1, "common_tali", 1),
                Req("rare_bote", 2, "rare_ugat", 1, "common_tela", 2),
                Req("rare_ugat", 1, "rare_kandila", 1, "common_tali", 1));

            AddCharacter(catalog, resolver, "rare_mangkukulam", "Mangkukulam", PartRarity.Rare,
                Req("rare_ugat", 1, "rare_kandila", 1, "common_kahoy", 1),
                Req("rare_kandila", 2, "rare_bote", 1, "common_tela", 2),
                Req("rare_ugat", 2, "common_kahoy", 1));
        }

        private static void RegisterEpicRecipes(CraftingCatalog catalog, ICraftingContentResolver resolver)
        {
            AddCharacter(catalog, resolver, "epic_bakunawa", "Bakunawa", PartRarity.Epic,
                Req("epic_ngipin_dragon", 1, "rare_salamin", 2, "common_bakal", 3),
                Req("epic_lawas_dragon", 1, "rare_tinta", 3, "common_tela", 4),
                Req("epic_kuko_dragon", 1, "rare_tsapa", 2, "common_goma", 3));

            AddCharacter(catalog, resolver, "epic_lalahon", "Lalahon", PartRarity.Epic,
                Req("epic_korona_abo", 1, "rare_kandila", 2, "common_dayami", 3),
                Req("epic_puso_bulkan", 1, "rare_ugat", 3, "common_kahoy", 4),
                Req("epic_batong_lava", 1, "rare_tinta", 2, "common_bakal", 3));

            AddCharacter(catalog, resolver, "epic_mayari", "Mayari", PartRarity.Epic,
                Req("epic_hiyas_buwan", 1, "rare_salamin", 2, "common_tela", 3),
                Req("epic_sinag_buwan", 1, "rare_kandila", 3, "common_bakal", 4),
                Req("epic_alikabok_bituin", 1, "rare_tinta", 2, "common_goma", 3));
        }

        private static void AddMaterial(
            CraftingCatalog catalog,
            ICraftingContentResolver resolver,
            string id,
            string displayName,
            MaterialRarity rarity)
        {
            catalog.RegisterMaterial(new MaterialDefinition(
                id,
                displayName,
                rarity,
                resolver?.ResolveMarketValue(id) ?? 0,
                resolver?.ResolveMaterialInventoryDefinition(id)));
        }

        private static void AddCharacter(
            CraftingCatalog catalog,
            ICraftingContentResolver resolver,
            string characterId,
            string displayName,
            PartRarity rarity,
            MaterialRequirement[] head,
            MaterialRequirement[] body,
            MaterialRequirement[] legs)
        {
            AddPart(catalog, resolver, characterId, displayName, PartSlot.Head, rarity, head);
            AddPart(catalog, resolver, characterId, displayName, PartSlot.Body, rarity, body);
            AddPart(catalog, resolver, characterId, displayName, PartSlot.Legs, rarity, legs);
        }

        private static void AddPart(
            CraftingCatalog catalog,
            ICraftingContentResolver resolver,
            string characterId,
            string characterDisplayName,
            PartSlot slot,
            PartRarity rarity,
            MaterialRequirement[] requirements)
        {
            string suffix = slot.ToString().ToLowerInvariant();
            string partId = "part_" + characterId + "_" + suffix;
            string recipeId = "recipe_" + characterId + "_" + suffix;
            PartDefinition battleDefinition = resolver?.ResolvePartDefinition(partId, slot, rarity) ??
                                              DefaultPartDefinitionCatalog.GetRequired(partId);
            var part = new CraftablePartDefinition(
                partId,
                characterDisplayName + " " + slot,
                characterId,
                slot,
                rarity,
                recipeId,
                battleDefinition,
                resolver?.ResolveMarketValue(partId) ?? 0,
                characterDisplayName: characterDisplayName,
                visualAssetId: partId);
            catalog.RegisterPartAndRecipe(part, new CraftingRecipe(recipeId, part, requirements));
        }

        private static void AddBakunawaBodyLegsHybrid(
            CraftingCatalog catalog,
            ICraftingContentResolver resolver)
        {
            const string partId = "part_epic_bakunawa_body_legs";
            const string recipeId = "recipe_epic_bakunawa_body_legs";
            PartDefinition definition = resolver?.ResolvePartDefinition(
                partId, PartSlot.Body, PartRarity.Epic) ?? CreateBakunawaHybridDefinition();
            var part = new CraftablePartDefinition(
                partId,
                "Bakunawa Body & Legs",
                "epic_bakunawa",
                PartSlot.Body,
                PartRarity.Epic,
                recipeId,
                definition,
                resolver?.ResolveMarketValue(partId) ?? 0,
                characterDisplayName: "Bakunawa",
                visualAssetId: partId,
                visualLayerOrder: 1,
                occupiedRegions: PartEquipmentRegion.Body | PartEquipmentRegion.Legs);
            catalog.RegisterPartAndRecipe(part, new CraftingRecipe(
                recipeId,
                part,
                Req("epic_lawas_dragon", 1, "epic_kuko_dragon", 1, "rare_tinta", 3)));
        }

        private static PartDefinition CreateBakunawaHybridDefinition()
        {
            PartDefinition body = DefaultPartDefinitionCatalog.GetRequired("part_epic_bakunawa_body");
            PartDefinition legs = DefaultPartDefinitionCatalog.GetRequired("part_epic_bakunawa_legs");
            var passiveModifiers = new List<IPassiveBattleModifier>(
                body.PassiveModifiers.Count + legs.PassiveModifiers.Count);
            passiveModifiers.AddRange(body.PassiveModifiers);
            passiveModifiers.AddRange(legs.PassiveModifiers);
            var passiveAbilities = new List<PartPassiveDefinition>(
                body.PassiveAbilities.Count + legs.PassiveAbilities.Count);
            var passiveIds = new HashSet<string>(StringComparer.Ordinal);
            AddUniquePassives(body.PassiveAbilities, passiveAbilities, passiveIds);
            AddUniquePassives(legs.PassiveAbilities, passiveAbilities, passiveIds);

            return new PartDefinition(
                "part_epic_bakunawa_body_legs",
                PartSlot.Body,
                PartRarity.Epic,
                body.AdditionalHitPoints + legs.AdditionalHitPoints,
                body.AttackBonusPercent + legs.AttackBonusPercent,
                passiveModifiers,
                body.SpeedBonusPercent + legs.SpeedBonusPercent,
                body.CriticalChanceBonusPercent + legs.CriticalChanceBonusPercent,
                body.DodgeChanceBonusPercent + legs.DodgeChanceBonusPercent,
                body.HitPointBonusPercent + legs.HitPointBonusPercent,
                passiveAbilities);
        }

        private static void AddUniquePassives(
            IReadOnlyList<PartPassiveDefinition> source,
            ICollection<PartPassiveDefinition> destination,
            ISet<string> seen)
        {
            for (int i = 0; i < source.Count; i++)
            {
                if (seen.Add(source[i].Id))
                {
                    destination.Add(source[i]);
                }
            }
        }

        private static MaterialRequirement[] Req(params object[] values)
        {
            if (values == null || values.Length == 0 || values.Length % 2 != 0)
            {
                throw new ArgumentException("Requirements must be alternating material IDs and quantities.");
            }

            var result = new MaterialRequirement[values.Length / 2];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = new MaterialRequirement((string)values[i * 2], (int)values[i * 2 + 1]);
            }

            return result;
        }
    }
}
