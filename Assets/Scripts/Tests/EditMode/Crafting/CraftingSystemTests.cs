using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ApirApir.Battle;
using ApirApir.Shop;
using NUnit.Framework;

namespace ApirApir.Crafting.Tests
{
    public sealed class CraftingSystemTests
    {
        [Test]
        public void AvailableAssetCatalog_UsesThirteenMaterialsAndCorrectedBakunawaRecipes()
        {
            CraftingCatalog catalog = DefaultCraftingCatalog.CreateAvailableAssetCatalog();

            Assert.That(catalog.Materials.Count, Is.EqualTo(13));
            Assert.That(catalog.Materials["common_kandila"].Rarity, Is.EqualTo(MaterialRarity.Common));
            Assert.That(catalog.Materials["rare_rosaryo"].Rarity, Is.EqualTo(MaterialRarity.Rare));
            CraftingRecipe head = catalog.Recipes["recipe_epic_bakunawa_head"];
            Assert.That(head.Requirements.Select(value => $"{value.MaterialId}:{value.Quantity}"),
                Is.EquivalentTo(new[]
                {
                    "epic_ngipin_dragon:1", "rare_salamin:1", "common_bakal:2"
                }));
            CraftingRecipe hybrid = catalog.Recipes["recipe_epic_bakunawa_body_legs"];
            Assert.That(hybrid.Requirements.Select(value => $"{value.MaterialId}:{value.Quantity}"),
                Is.EquivalentTo(new[]
                {
                    "epic_lawas_dragon:1", "epic_kuko_dragon:1", "rare_tinta:3"
                }));
            Assert.That(hybrid.OutputPart.OccupiedRegions,
                Is.EqualTo(PartEquipmentRegion.Body | PartEquipmentRegion.Legs));
            Assert.That(catalog.Parts.Count, Is.EqualTo(11));
            Assert.That(catalog.Parts.Keys.Count(value => value.Contains("pari")), Is.EqualTo(3));
            Assert.That(catalog.Parts["part_rare_pari_head"].DisplayName, Is.EqualTo("Pari Head"));
            Assert.That(catalog.Parts["part_rare_pari_body"].DisplayName, Is.EqualTo("Pari Body"));
            Assert.That(catalog.Parts["part_rare_pari_legs"].DisplayName, Is.EqualTo("Pari Legs"));
            Assert.That(catalog.Recipes["recipe_rare_pari_head"].Requirements
                    .Select(value => $"{value.MaterialId}:{value.Quantity}"),
                Is.EquivalentTo(new[] { "rare_rosaryo:1", "common_tela:2" }));
            Assert.That(catalog.Recipes["recipe_rare_pari_body"].Requirements
                    .Select(value => $"{value.MaterialId}:{value.Quantity}"),
                Is.EquivalentTo(new[]
                {
                    "rare_rosaryo:1", "common_kandila:2", "common_tela:2"
                }));
            Assert.That(catalog.Recipes["recipe_rare_pari_legs"].Requirements
                    .Select(value => $"{value.MaterialId}:{value.Quantity}"),
                Is.EquivalentTo(new[] { "rare_rosaryo:1", "common_goma:2" }));
        }

        [Test]
        public void ProductionInventory_CraftIsAtomicAndExposesSameOwnedPartToAssembly()
        {
            CraftingCatalog catalog = DefaultCraftingCatalog.CreateAvailableAssetCatalog();
            var inventory = new PlayerCraftingInventory("player");
            var inventories = new PlayerCraftingInventoryCollection();
            Assert.That(inventories.Register(inventory), Is.True);
            CraftingRecipe recipe = catalog.Recipes["recipe_common_magsasaka_head"];
            foreach (MaterialRequirement requirement in recipe.Requirements)
            {
                inventory.TryAddMaterial(
                    catalog.Materials[requirement.MaterialId], requirement.Quantity, out _);
            }

            var controller = new CraftingController(
                "match", catalog, new FixedCraftingAuthority(true),
                new FixedCraftingAccessPolicy(true), inventories, new GuidPartInstanceIdProvider());
            CraftingResult result = controller.Craft(
                new CraftingRequest("match", recipe.Id, "command"), "player");

            Assert.That(result.Succeeded, Is.True);
            Assert.That(recipe.Requirements.All(value =>
                inventory.GetMaterialQuantity(value.MaterialId) == 0), Is.True);
            Assert.That(inventory.TryGetOwnedPart(result.CreatedPart.InstanceId, out OwnedCardPart owned),
                Is.True);
            Assert.That(owned, Is.SameAs(result.CreatedPart));
        }

        [Test]
        public void Catalog_HasExactlyTwentyOneMaterialDefinitions()
        {
            CraftingCatalog catalog = DefaultCraftingCatalog.Create();

            Assert.That(catalog.Materials.Count, Is.EqualTo(21));
            Assert.That(catalog.Materials.Values.Count(m => m.Rarity == MaterialRarity.Common), Is.EqualTo(6));
            Assert.That(catalog.Materials.Values.Count(m => m.Rarity == MaterialRarity.Rare), Is.EqualTo(6));
            Assert.That(catalog.Materials.Values.Count(m => m.Rarity == MaterialRarity.Epic), Is.EqualTo(9));
        }

        [Test]
        public void Catalog_HasExactlyThirtyThreeRecipes()
        {
            CraftingCatalog catalog = DefaultCraftingCatalog.Create();

            Assert.That(catalog.Recipes.Count, Is.EqualTo(33));
            Assert.That(catalog.Recipes.Values.Count(r => r.Rarity == PartRarity.Common), Is.EqualTo(12));
            Assert.That(catalog.Recipes.Values.Count(r => r.Rarity == PartRarity.Rare), Is.EqualTo(12));
            Assert.That(catalog.Recipes.Values.Count(r => r.Rarity == PartRarity.Epic), Is.EqualTo(9));
        }

        [Test]
        public void Catalog_ContainsTheExactInitialRecipeFormulas()
        {
            CraftingCatalog catalog = DefaultCraftingCatalog.Create();
            var expected = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "recipe_common_magsasaka_head", "common_dayami:2,common_tali:1" },
                { "recipe_common_magsasaka_body", "common_tali:2,common_tela:3" },
                { "recipe_common_magsasaka_legs", "common_goma:2,common_tela:1" },
                { "recipe_common_mangangahoy_head", "common_tali:1,common_tela:2" },
                { "recipe_common_mangangahoy_body", "common_bakal:2,common_kahoy:3" },
                { "recipe_common_mangangahoy_legs", "common_goma:2,common_kahoy:1" },
                { "recipe_common_pulis_head", "common_bakal:1,common_tela:2" },
                { "recipe_common_pulis_body", "common_bakal:2,common_tela:3" },
                { "recipe_common_pulis_legs", "common_bakal:1,common_goma:2" },
                { "recipe_common_mangingisda_head", "common_dayami:2,common_tela:1" },
                { "recipe_common_mangingisda_body", "common_kahoy:2,common_tali:3" },
                { "recipe_common_mangingisda_legs", "common_goma:2,common_tali:1" },
                { "recipe_rare_abogado_head", "common_tela:1,rare_salamin:2" },
                { "recipe_rare_abogado_body", "common_tela:2,rare_salamin:1,rare_tinta:2" },
                { "recipe_rare_abogado_legs", "common_goma:1,rare_tinta:2" },
                { "recipe_rare_pari_head", "common_bakal:1,rare_tsapa:2" },
                { "recipe_rare_pari_body", "common_tela:2,rare_tinta:1,rare_tsapa:2" },
                { "recipe_rare_pari_legs", "common_goma:1,rare_tinta:1,rare_tsapa:1" },
                { "recipe_rare_manggagayuma_head", "common_tali:1,rare_bote:1,rare_salamin:1" },
                { "recipe_rare_manggagayuma_body", "common_tela:2,rare_bote:2,rare_ugat:1" },
                { "recipe_rare_manggagayuma_legs", "common_tali:1,rare_kandila:1,rare_ugat:1" },
                { "recipe_rare_mangkukulam_head", "common_kahoy:1,rare_kandila:1,rare_ugat:1" },
                { "recipe_rare_mangkukulam_body", "common_tela:2,rare_bote:1,rare_kandila:2" },
                { "recipe_rare_mangkukulam_legs", "common_kahoy:1,rare_ugat:2" },
                { "recipe_epic_bakunawa_head", "common_bakal:3,epic_ngipin_dragon:1,rare_salamin:2" },
                { "recipe_epic_bakunawa_body", "common_tela:4,epic_lawas_dragon:1,rare_tinta:3" },
                { "recipe_epic_bakunawa_legs", "common_goma:3,epic_kuko_dragon:1,rare_tsapa:2" },
                { "recipe_epic_lalahon_head", "common_dayami:3,epic_korona_abo:1,rare_kandila:2" },
                { "recipe_epic_lalahon_body", "common_kahoy:4,epic_puso_bulkan:1,rare_ugat:3" },
                { "recipe_epic_lalahon_legs", "common_bakal:3,epic_batong_lava:1,rare_tinta:2" },
                { "recipe_epic_mayari_head", "common_tela:3,epic_hiyas_buwan:1,rare_salamin:2" },
                { "recipe_epic_mayari_body", "common_bakal:4,epic_sinag_buwan:1,rare_kandila:3" },
                { "recipe_epic_mayari_legs", "common_goma:3,epic_alikabok_bituin:1,rare_tinta:2" }
            };

            Assert.That(catalog.Recipes.Keys, Is.EquivalentTo(expected.Keys));
            foreach (KeyValuePair<string, string> pair in expected)
            {
                string actual = string.Join(",", Recipe(catalog, pair.Key).Requirements
                    .OrderBy(r => r.MaterialId)
                    .Select(r => r.MaterialId + ":" + r.Quantity));
                Assert.That(actual, Is.EqualTo(pair.Value), pair.Key);
            }
        }

        [Test]
        public void CommonRecipes_UseOnlyCommonMaterialsAndSpecifiedTotals()
        {
            CraftingCatalog catalog = DefaultCraftingCatalog.Create();

            foreach (CraftingRecipe recipe in catalog.Recipes.Values.Where(r => r.Rarity == PartRarity.Common))
            {
                Assert.That(recipe.Requirements.All(r => GetRarity(catalog, r) == MaterialRarity.Common), Is.True, recipe.Id);
                int expected = recipe.Slot == PartSlot.Body ? 5 : 3;
                Assert.That(recipe.Requirements.Sum(r => r.Quantity), Is.EqualTo(expected), recipe.Id);
            }
        }

        [Test]
        public void RareRecipes_UseRareAndCommonMaterialsAndSpecifiedTotals()
        {
            CraftingCatalog catalog = DefaultCraftingCatalog.Create();

            foreach (CraftingRecipe recipe in catalog.Recipes.Values.Where(r => r.Rarity == PartRarity.Rare))
            {
                Assert.That(recipe.Requirements.Any(r => GetRarity(catalog, r) == MaterialRarity.Rare), Is.True, recipe.Id);
                Assert.That(recipe.Requirements.Any(r => GetRarity(catalog, r) == MaterialRarity.Common), Is.True, recipe.Id);
                Assert.That(recipe.Requirements.All(r => GetRarity(catalog, r) != MaterialRarity.Epic), Is.True, recipe.Id);
                int expectedRare = recipe.Slot == PartSlot.Body ? 3 : 2;
                int expectedCommon = recipe.Slot == PartSlot.Body ? 2 : 1;
                Assert.That(SumRarity(catalog, recipe, MaterialRarity.Rare), Is.EqualTo(expectedRare), recipe.Id);
                Assert.That(SumRarity(catalog, recipe, MaterialRarity.Common), Is.EqualTo(expectedCommon), recipe.Id);
            }
        }

        [Test]
        public void EpicRecipes_UseOneUniqueEpicIngredientPlusSpecifiedRareAndCommonAmounts()
        {
            CraftingCatalog catalog = DefaultCraftingCatalog.Create();
            var epicIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (CraftingRecipe recipe in catalog.Recipes.Values.Where(r => r.Rarity == PartRarity.Epic))
            {
                MaterialRequirement[] epic = recipe.Requirements
                    .Where(r => GetRarity(catalog, r) == MaterialRarity.Epic).ToArray();
                Assert.That(epic.Length, Is.EqualTo(1), recipe.Id);
                Assert.That(epic[0].Quantity, Is.EqualTo(1), recipe.Id);
                Assert.That(epicIds.Add(epic[0].MaterialId), Is.True, recipe.Id);
                int expectedRare = recipe.Slot == PartSlot.Body ? 3 : 2;
                int expectedCommon = recipe.Slot == PartSlot.Body ? 4 : 3;
                Assert.That(SumRarity(catalog, recipe, MaterialRarity.Rare), Is.EqualTo(expectedRare), recipe.Id);
                Assert.That(SumRarity(catalog, recipe, MaterialRarity.Common), Is.EqualTo(expectedCommon), recipe.Id);
            }

            Assert.That(epicIds.Count, Is.EqualTo(9));
        }

        [Test]
        public void EveryCharacterSet_HasHeadBodyAndLegs()
        {
            CraftingCatalog catalog = DefaultCraftingCatalog.Create();

            foreach (IGrouping<string, CraftingRecipe> group in catalog.Recipes.Values.GroupBy(r => r.CharacterId))
            {
                Assert.That(group.Count(), Is.EqualTo(3), group.Key);
                Assert.That(group.Select(r => r.Slot), Is.EquivalentTo(new[] { PartSlot.Head, PartSlot.Body, PartSlot.Legs }), group.Key);
            }

            Assert.That(catalog.Recipes.Values.Select(r => r.CharacterId).Distinct().Count(), Is.EqualTo(11));
        }

        [Test]
        public void CommonPulisAndRarePari_AreDistinctDefinitions()
        {
            CraftingCatalog catalog = DefaultCraftingCatalog.Create();
            CraftingRecipe common = Recipe(catalog, "recipe_common_pulis_head");
            CraftingRecipe rare = Recipe(catalog, "recipe_rare_pari_head");

            Assert.That(common.CharacterId, Is.Not.EqualTo(rare.CharacterId));
            Assert.That(common.OutputPart.Id, Is.Not.EqualTo(rare.OutputPart.Id));
            Assert.That(common.OutputPart.CharacterDisplayName, Is.EqualTo("Pulis"));
            Assert.That(rare.OutputPart.CharacterDisplayName, Is.EqualTo("Pari"));
            Assert.That(common.Rarity, Is.EqualTo(PartRarity.Common));
            Assert.That(rare.Rarity, Is.EqualTo(PartRarity.Rare));
        }

        [Test]
        public void LegacyRarePulisIdentifiers_ResolveToCanonicalPariContent()
        {
            CraftingCatalog catalog = DefaultCraftingCatalog.Create();

            Assert.That(catalog.TryGetRecipe("recipe_rare_pulis_head", out CraftingRecipe recipe), Is.True);
            Assert.That(recipe.Id, Is.EqualTo("recipe_rare_pari_head"));
            Assert.That(catalog.TryGetPart("part_rare_pulis_head", out CraftablePartDefinition part), Is.True);
            Assert.That(part.Id, Is.EqualTo("part_rare_pari_head"));
        }

        [Test]
        public void Craft_ConsumesExactRecipeQuantities()
        {
            TestContext context = CreateContext();
            context.Inventory.Set("common_dayami", 5);
            context.Inventory.Set("common_tali", 4);

            CraftingResult result = context.Controller.Craft(
                new CraftingRequest("match", "recipe_common_magsasaka_head", "cmd-1"), "player");

            Assert.That(result.Succeeded, Is.True);
            Assert.That(context.Inventory.GetMaterialQuantity("common_dayami"), Is.EqualTo(3));
            Assert.That(context.Inventory.GetMaterialQuantity("common_tali"), Is.EqualTo(3));
        }

        [Test]
        public void Craft_RejectsInsufficientMaterialsWithoutCreatingPart()
        {
            TestContext context = CreateContext();
            context.Inventory.Set("common_dayami", 2);

            CraftingResult result = context.Controller.Craft(
                new CraftingRequest("match", "recipe_common_magsasaka_head", "cmd-1"), "player");

            Assert.That(result.Failure, Is.EqualTo(CraftingFailure.InsufficientMaterials));
            Assert.That(context.Inventory.Parts, Is.Empty);
            Assert.That(context.Inventory.GetMaterialQuantity("common_dayami"), Is.EqualTo(2));
        }

        [Test]
        public void FailedInventoryTransaction_NeverPartiallyConsumesResources()
        {
            TestContext context = CreateContext();
            SeedRecipe(context, "recipe_common_magsasaka_head");
            context.Inventory.FailNextCraft = true;
            Dictionary<string, int> before = context.Inventory.CopyMaterials();

            CraftingResult result = context.Controller.Craft(
                new CraftingRequest("match", "recipe_common_magsasaka_head", "cmd-1"), "player");

            Assert.That(result.Failure, Is.EqualTo(CraftingFailure.InventoryTransactionFailed));
            Assert.That(context.Inventory.CopyMaterials(), Is.EqualTo(before));
            Assert.That(context.Inventory.Parts, Is.Empty);
        }

        [Test]
        public void SuccessfulCraft_CreatesExactlyOneOwnedPartForAuthenticatedPlayer()
        {
            TestContext context = CreateContext();
            SeedRecipe(context, "recipe_common_magsasaka_head");

            CraftingResult result = context.Controller.Craft(
                new CraftingRequest("match", "recipe_common_magsasaka_head", "cmd-1"), "player");

            Assert.That(result.Succeeded, Is.True);
            Assert.That(context.Inventory.Parts.Count, Is.EqualTo(1));
            Assert.That(result.CreatedPart, Is.SameAs(context.Inventory.Parts[0]));
            Assert.That(result.CreatedPart.OwnerPlayerId, Is.EqualTo("player"));
        }

        [TestCase("recipe_common_magsasaka_head", 20)]
        [TestCase("recipe_rare_abogado_head", 15)]
        [TestCase("recipe_epic_bakunawa_head", 10)]
        public void CraftedParts_StartAtRarityMaximumMaintenance(string recipeId, int expectedMp)
        {
            TestContext context = CreateContext();
            SeedRecipe(context, recipeId);

            CraftingResult result = context.Controller.Craft(
                new CraftingRequest("match", recipeId, "cmd-1"), "player");

            Assert.That(result.CreatedPart.CurrentMaintenance, Is.EqualTo(expectedMp));
            Assert.That(result.CreatedPart.Definition.InitialMaintenance, Is.EqualTo(expectedMp));
        }

        [Test]
        public void DuplicatePartInstances_HaveIndependentMaintenanceConditions()
        {
            TestContext context = CreateContext();
            SeedRecipe(context, "recipe_common_magsasaka_head", 2);
            CraftingResult first = context.Controller.Craft(
                new CraftingRequest("match", "recipe_common_magsasaka_head", "cmd-1"), "player");
            CraftingResult second = context.Controller.Craft(
                new CraftingRequest("match", "recipe_common_magsasaka_head", "cmd-2"), "player");

            Assert.That(first.CreatedPart.InstanceId, Is.Not.EqualTo(second.CreatedPart.InstanceId));
            Assert.That(first.CreatedPart.TrySetMaintenance(5), Is.True);
            Assert.That(first.CreatedPart.CurrentMaintenance, Is.EqualTo(5));
            Assert.That(second.CreatedPart.CurrentMaintenance, Is.EqualTo(20));
        }

        [Test]
        public void Craft_DoesNotAutomaticallyEquipPart()
        {
            TestContext context = CreateContext();
            SeedRecipe(context, "recipe_common_magsasaka_head");

            CraftingResult result = context.Controller.Craft(
                new CraftingRequest("match", "recipe_common_magsasaka_head", "cmd-1"), "player");

            Assert.That(result.CreatedPart.IsEquipped, Is.False);
        }

        [Test]
        public void Craft_DoesNotAlterSoulOrSkillLoadout()
        {
            TestContext context = CreateContext();
            SeedRecipe(context, "recipe_common_magsasaka_head");
            string soul = context.Inventory.SelectedSoulId;
            string[] skills = context.Inventory.SkillIds.ToArray();

            context.Controller.Craft(
                new CraftingRequest("match", "recipe_common_magsasaka_head", "cmd-1"), "player");

            Assert.That(context.Inventory.SelectedSoulId, Is.EqualTo(soul));
            Assert.That(context.Inventory.SkillIds, Is.EqualTo(skills));
        }

        [Test]
        public void DuplicateCommand_DoesNotSpendOrCreateTwice()
        {
            TestContext context = CreateContext();
            SeedRecipe(context, "recipe_common_magsasaka_head", 2);
            var request = new CraftingRequest("match", "recipe_common_magsasaka_head", "same-command");

            CraftingResult first = context.Controller.Craft(request, "player");
            CraftingResult repeated = context.Controller.Craft(request, "player");

            Assert.That(first.Succeeded, Is.True);
            Assert.That(repeated.Failure, Is.EqualTo(CraftingFailure.DuplicateCommand));
            Assert.That(context.Inventory.Parts.Count, Is.EqualTo(1));
            Assert.That(context.Inventory.GetMaterialQuantity("common_dayami"), Is.EqualTo(2));
            Assert.That(context.Inventory.GetMaterialQuantity("common_tali"), Is.EqualTo(1));
        }

        [Test]
        public void SimultaneousCommands_CannotSpendTheSameMaterialsTwice()
        {
            TestContext context = CreateContext();
            SeedRecipe(context, "recipe_common_magsasaka_head");
            CraftingResult first = null;
            CraftingResult second = null;

            Task.WaitAll(
                Task.Run(() => first = context.Controller.Craft(
                    new CraftingRequest("match", "recipe_common_magsasaka_head", "cmd-a"), "player")),
                Task.Run(() => second = context.Controller.Craft(
                    new CraftingRequest("match", "recipe_common_magsasaka_head", "cmd-b"), "player")));

            Assert.That(new[] { first, second }.Count(r => r.Succeeded), Is.EqualTo(1));
            Assert.That(context.Inventory.Parts.Count, Is.EqualTo(1));
            Assert.That(context.Inventory.GetMaterialQuantity("common_dayami"), Is.Zero);
            Assert.That(context.Inventory.GetMaterialQuantity("common_tali"), Is.Zero);
        }

        [Test]
        public void UnauthorizedPhase_RejectsCraftingWithoutMutation()
        {
            TestContext context = CreateContext(false);
            SeedRecipe(context, "recipe_common_magsasaka_head");

            CraftingResult result = context.Controller.Craft(
                new CraftingRequest("match", "recipe_common_magsasaka_head", "cmd-1"), "player");

            Assert.That(result.Failure, Is.EqualTo(CraftingFailure.CraftingNotAllowed));
            Assert.That(context.Inventory.Parts, Is.Empty);
            Assert.That(context.Inventory.GetMaterialQuantity("common_dayami"), Is.EqualTo(2));
        }

        [Test]
        public void ShopCatalog_UsesExactSameMaterialAndPartDefinitionObjects()
        {
            CraftingCatalog crafting = DefaultCraftingCatalog.Create();
            var shop = new ShopItemCatalog();
            crafting.RegisterWithShop(shop);

            foreach (MaterialDefinition material in crafting.Materials.Values)
            {
                Assert.That(shop.TryGet(material.Id, out IShopItemDefinition found), Is.True);
                Assert.That(found, Is.SameAs(material));
            }

            foreach (CraftablePartDefinition part in crafting.Parts.Values)
            {
                Assert.That(shop.TryGet(part.Id, out IShopItemDefinition found), Is.True);
                Assert.That(found, Is.SameAs(part));
            }

            Assert.That(shop.GetCategory(ShopItemCategory.CommonMaterial).Count, Is.EqualTo(6));
            Assert.That(shop.GetCategory(ShopItemCategory.RareMaterial).Count, Is.EqualTo(6));
            Assert.That(shop.GetCategory(ShopItemCategory.EpicMaterial).Count, Is.EqualTo(9));
            Assert.That(shop.GetCategory(ShopItemCategory.CompletedPart).Count, Is.EqualTo(33));
        }

        [Test]
        public void MaterialRewardIntegration_ProvidesMaterialsThatCraftingConsumes()
        {
            TestContext context = CreateContext();
            MaterialRewardResult dayami = context.Controller.AwardMaterial(
                new MaterialRewardRequest("match", "common_dayami", 2, "reward-1", "patintero"), "player");
            MaterialRewardResult tali = context.Controller.AwardMaterial(
                new MaterialRewardRequest("match", "common_tali", 1, "reward-2", "event"), "player");

            CraftingResult crafted = context.Controller.Craft(
                new CraftingRequest("match", "recipe_common_magsasaka_head", "craft-1"), "player");

            Assert.That(dayami.Succeeded && tali.Succeeded && crafted.Succeeded, Is.True);
            Assert.That(context.Inventory.GetMaterialQuantity("common_dayami"), Is.Zero);
            Assert.That(context.Inventory.GetMaterialQuantity("common_tali"), Is.Zero);
        }

        [Test]
        public void InvalidAndMissingReferences_ReturnClearFailures()
        {
            TestContext context = CreateContext();

            CraftingResult missingRecipe = context.Controller.Craft(
                new CraftingRequest("match", "recipe_missing", "cmd-1"), "player");
            MaterialRewardResult missingMaterial = context.Controller.AwardMaterial(
                new MaterialRewardRequest("match", "material_missing", 1, "reward-1"), "player");
            CraftingResult invalid = context.Controller.Craft(
                new CraftingRequest("wrong-match", "recipe_common_magsasaka_head", "cmd-2"), "player");

            Assert.That(missingRecipe.Failure, Is.EqualTo(CraftingFailure.RecipeNotFound));
            Assert.That(missingMaterial.Failure, Is.EqualTo(CraftingFailure.MissingMaterialDefinition));
            Assert.That(invalid.Failure, Is.EqualTo(CraftingFailure.InvalidRequest));
        }

        [Test]
        public void Recipes_AreAssociatedWithMatchingCharacterRaritySlotAndPartMetadata()
        {
            CraftingCatalog catalog = DefaultCraftingCatalog.Create();

            foreach (CraftingRecipe recipe in catalog.Recipes.Values)
            {
                Assert.That(recipe.Id, Does.StartWith("recipe_" + recipe.CharacterId + "_"));
                Assert.That(recipe.OutputPart.Id, Does.StartWith("part_" + recipe.CharacterId + "_"));
                Assert.That(recipe.OutputPart.RecipeId, Is.EqualTo(recipe.Id));
                Assert.That(recipe.OutputPart.CharacterId, Is.EqualTo(recipe.CharacterId));
                Assert.That(recipe.OutputPart.Rarity, Is.EqualTo(recipe.Rarity));
                Assert.That(recipe.OutputPart.Slot, Is.EqualTo(recipe.Slot));
                Assert.That(recipe.OutputPart.BattleDefinition.Rarity, Is.EqualTo(recipe.Rarity));
                Assert.That(recipe.OutputPart.BattleDefinition.Slot, Is.EqualTo(recipe.Slot));
            }
        }

        [Test]
        public void EpicIngredient_ForAnotherCharacterCannotSubstitute()
        {
            TestContext context = CreateContext();
            context.Inventory.Set("epic_korona_abo", 1);
            context.Inventory.Set("rare_salamin", 2);
            context.Inventory.Set("common_bakal", 3);

            CraftingResult result = context.Controller.Craft(
                new CraftingRequest("match", "recipe_epic_bakunawa_head", "cmd-1"), "player");

            Assert.That(result.Failure, Is.EqualTo(CraftingFailure.InsufficientMaterials));
            Assert.That(context.Inventory.GetMaterialQuantity("epic_korona_abo"), Is.EqualTo(1));
            Assert.That(context.Inventory.Parts, Is.Empty);
        }

        [Test]
        public void Service_RejectsUnknownMatchAndForwardsRegisteredControllerEvents()
        {
            TestContext context = CreateContext();
            SeedRecipe(context, "recipe_common_magsasaka_head");
            var service = new AuthoritativeCraftingService();
            var events = new List<CraftingEvent>();
            service.EventPublished += events.Add;
            Assert.That(service.Register(context.Controller), Is.True);

            CraftingResult unknown = service.Craft(
                new CraftingRequest("unknown", "recipe_common_magsasaka_head", "x"), "player");
            CraftingResult result = service.Craft(
                new CraftingRequest("match", "recipe_common_magsasaka_head", "cmd-1"), "player");

            Assert.That(unknown.Failure, Is.EqualTo(CraftingFailure.InvalidRequest));
            Assert.That(result.Succeeded, Is.True);
            Assert.That(events.Any(e => e.Type == CraftingEventType.CraftingSucceeded), Is.True);
        }

        [Test]
        public void CraftableQuery_ChangesAfterAtomicConsumption()
        {
            TestContext context = CreateContext();
            SeedRecipe(context, "recipe_common_magsasaka_head");
            Assert.That(context.Controller.GetCraftableRecipes("player").Select(r => r.Id),
                Does.Contain("recipe_common_magsasaka_head"));

            context.Controller.Craft(
                new CraftingRequest("match", "recipe_common_magsasaka_head", "cmd-1"), "player");

            Assert.That(context.Controller.GetCraftableRecipes("player").Select(r => r.Id),
                Does.Not.Contain("recipe_common_magsasaka_head"));
        }

        [Test]
        public void NonAuthoritativeController_RejectsBeforeInventoryMutation()
        {
            CraftingCatalog catalog = DefaultCraftingCatalog.Create();
            var inventory = new TestInventory("player");
            var provider = new TestInventoryProvider(inventory);
            var controller = new CraftingController(
                "match", catalog, new FixedCraftingAuthority(false),
                new FixedCraftingAccessPolicy(true), provider, new SequencePartIdProvider());
            SeedRecipe(catalog, inventory, "recipe_common_magsasaka_head");

            CraftingResult result = controller.Craft(
                new CraftingRequest("match", "recipe_common_magsasaka_head", "cmd-1"), "player");

            Assert.That(result.Failure, Is.EqualTo(CraftingFailure.NotAuthoritative));
            Assert.That(inventory.Parts, Is.Empty);
        }

        private static MaterialRarity GetRarity(CraftingCatalog catalog, MaterialRequirement requirement)
        {
            Assert.That(catalog.TryGetMaterial(requirement.MaterialId, out MaterialDefinition material), Is.True);
            return material.Rarity;
        }

        private static int SumRarity(
            CraftingCatalog catalog,
            CraftingRecipe recipe,
            MaterialRarity rarity)
        {
            return recipe.Requirements
                .Where(r => GetRarity(catalog, r) == rarity)
                .Sum(r => r.Quantity);
        }

        private static CraftingRecipe Recipe(CraftingCatalog catalog, string id)
        {
            Assert.That(catalog.TryGetRecipe(id, out CraftingRecipe recipe), Is.True, id);
            return recipe;
        }

        private static TestContext CreateContext(bool canCraft = true)
        {
            CraftingCatalog catalog = DefaultCraftingCatalog.Create();
            var inventory = new TestInventory("player");
            var provider = new TestInventoryProvider(inventory);
            var controller = new CraftingController(
                "match",
                catalog,
                new FixedCraftingAuthority(true),
                new FixedCraftingAccessPolicy(canCraft),
                provider,
                new SequencePartIdProvider());
            return new TestContext(catalog, inventory, controller);
        }

        private static void SeedRecipe(TestContext context, string recipeId, int multiples = 1)
        {
            SeedRecipe(context.Catalog, context.Inventory, recipeId, multiples);
        }

        private static void SeedRecipe(
            CraftingCatalog catalog,
            TestInventory inventory,
            string recipeId,
            int multiples = 1)
        {
            CraftingRecipe recipe = Recipe(catalog, recipeId);
            foreach (MaterialRequirement requirement in recipe.Requirements)
            {
                inventory.Set(requirement.MaterialId, requirement.Quantity * multiples);
            }
        }

        private sealed class TestContext
        {
            public TestContext(CraftingCatalog catalog, TestInventory inventory, CraftingController controller)
            {
                Catalog = catalog;
                Inventory = inventory;
                Controller = controller;
            }

            public CraftingCatalog Catalog { get; }
            public TestInventory Inventory { get; }
            public CraftingController Controller { get; }
        }

        private sealed class TestInventoryProvider : ICraftingPlayerInventoryProvider
        {
            private readonly TestInventory inventory;

            public TestInventoryProvider(TestInventory inventory)
            {
                this.inventory = inventory;
            }

            public bool TryGetInventory(string playerId, out ICraftingPlayerInventory found)
            {
                found = string.Equals(inventory.PlayerId, playerId, StringComparison.Ordinal)
                    ? inventory
                    : null;
                return found != null;
            }
        }

        private sealed class SequencePartIdProvider : IPartInstanceIdProvider
        {
            private int next;

            public string CreateInstanceId(string playerId, CraftablePartDefinition definition)
            {
                int value = Interlocked.Increment(ref next);
                return playerId + "-part-" + value;
            }
        }

        private sealed class TestInventory : ICraftingPlayerInventory
        {
            private readonly object transactionLock = new object();
            private readonly Dictionary<string, int> materials =
                new Dictionary<string, int>(StringComparer.Ordinal);

            public TestInventory(string playerId)
            {
                PlayerId = playerId;
                Parts = new List<OwnedCardPart>();
                SelectedSoulId = "soul-selected";
                SkillIds = new List<string> { "skill-a", "skill-b" };
            }

            public string PlayerId { get; }
            public List<OwnedCardPart> Parts { get; }
            public string SelectedSoulId { get; }
            public List<string> SkillIds { get; }
            public bool FailNextCraft { get; set; }

            public int GetMaterialQuantity(string materialId)
            {
                lock (transactionLock)
                {
                    return materials.TryGetValue(materialId ?? string.Empty, out int quantity)
                        ? quantity
                        : 0;
                }
            }

            public void Set(string materialId, int quantity)
            {
                lock (transactionLock)
                {
                    materials[materialId] = quantity;
                }
            }

            public Dictionary<string, int> CopyMaterials()
            {
                lock (transactionLock)
                {
                    return new Dictionary<string, int>(materials, StringComparer.Ordinal);
                }
            }

            public bool TryCraft(
                CraftingRecipe recipe,
                OwnedCardPart newPart,
                out IReadOnlyList<MaterialBalanceChange> materialChanges)
            {
                lock (transactionLock)
                {
                    if (FailNextCraft)
                    {
                        FailNextCraft = false;
                        materialChanges = Array.Empty<MaterialBalanceChange>();
                        return false;
                    }

                    foreach (MaterialRequirement requirement in recipe.Requirements)
                    {
                        if (GetMaterialQuantity(requirement.MaterialId) < requirement.Quantity)
                        {
                            materialChanges = Array.Empty<MaterialBalanceChange>();
                            return false;
                        }
                    }

                    var changes = new MaterialBalanceChange[recipe.Requirements.Count];
                    for (int i = 0; i < recipe.Requirements.Count; i++)
                    {
                        MaterialRequirement requirement = recipe.Requirements[i];
                        int remaining = materials[requirement.MaterialId] - requirement.Quantity;
                        materials[requirement.MaterialId] = remaining;
                        changes[i] = new MaterialBalanceChange(
                            requirement.MaterialId, requirement.Quantity, remaining);
                    }

                    Parts.Add(newPart);
                    materialChanges = changes;
                    return true;
                }
            }

            public bool TryAddMaterial(
                MaterialDefinition material,
                int quantity,
                out int resultingQuantity)
            {
                lock (transactionLock)
                {
                    if (material == null || quantity <= 0)
                    {
                        resultingQuantity = 0;
                        return false;
                    }

                    resultingQuantity = GetMaterialQuantity(material.Id) + quantity;
                    materials[material.Id] = resultingQuantity;
                    return true;
                }
            }
        }
    }
}
