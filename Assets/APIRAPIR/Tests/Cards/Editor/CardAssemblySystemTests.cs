using System;
using System.Collections.Generic;
using System.Linq;
using ApirApir.AugmentedReality;
using ApirApir.Battle;
using ApirApir.Cards;
using ApirApir.Crafting;
using NUnit.Framework;

namespace ApirApir.CardTests
{
    public sealed class CardAssemblySystemTests
    {
        [Test]
        public void DefaultCatalogContainsExactlyThirtyThreeDistinctParts()
        {
            CraftingCatalog catalog = DefaultCraftingCatalog.Create();

            Assert.That(catalog.Parts.Count, Is.EqualTo(33));
            Assert.That(catalog.Parts.Keys.Distinct().Count(), Is.EqualTo(33));
            Assert.That(catalog.Parts.ContainsKey("part_common_pulis_head"), Is.True);
            Assert.That(catalog.Parts.ContainsKey("part_rare_pulis_head"), Is.True);
        }

        [Test]
        public void CatalogUsesEstablishedMaintenanceCapsAndNoInventedBonuses()
        {
            CraftingCatalog catalog = DefaultCraftingCatalog.Create();

            Assert.That(catalog.Parts.Values.Where(value => value.Rarity == PartRarity.Common)
                .All(value => value.MaximumMaintenance == 20), Is.True);
            Assert.That(catalog.Parts.Values.Where(value => value.Rarity == PartRarity.Rare)
                .All(value => value.MaximumMaintenance == 15), Is.True);
            Assert.That(catalog.Parts.Values.Where(value => value.Rarity == PartRarity.Epic)
                .All(value => value.MaximumMaintenance == 10), Is.True);
            Assert.That(catalog.Parts.Values.All(value => value.HitPointContribution == 0 &&
                value.AttackBonusPercent == 0f && value.PassiveAbilityReferences.Count == 0), Is.True);
        }

        [Test]
        public void BlankCardIsUsableAtOneHundredHpAndTenAttack()
        {
            TestContext context = CreateContext();
            CardBuildSnapshot snapshot = context.Service.InitializeCard("alice", "card").Snapshot;

            Assert.That(snapshot.IsUsable, Is.True);
            Assert.That(snapshot.Stats.MaximumHitPoints, Is.EqualTo(100));
            Assert.That(snapshot.Stats.EffectiveAttack, Is.EqualTo(10));
            Assert.That(snapshot.Head.IsEmpty && snapshot.Body.IsEmpty && snapshot.Legs.IsEmpty, Is.True);
            Assert.That(context.Service.TryGetLoadout("alice", "card", out CardBattleLoadout loadout), Is.True);
            Assert.That(loadout.Parts, Is.Empty);
        }

        [TestCase(PartSlot.Head)]
        [TestCase(PartSlot.Body)]
        [TestCase(PartSlot.Legs)]
        public void AnySingleSlotBuildIsValid(PartSlot slot)
        {
            TestContext context = CreateContext();
            OwnedCardPart part = context.AddPart("alice", "one", slot, PartRarity.Common);
            context.Service.InitializeCard("alice", "card");

            CardAssemblyOperationResult result = context.Service.EquipPart(
                "alice", "card", slot, part.InstanceId, 0, "equip");

            Assert.That(result.Succeeded, Is.True);
            Assert.That(context.Service.ValidateCardBuild("alice", "card"), Is.True);
        }

        [Test]
        public void MixedCharacterAndRarityBuildIsValid()
        {
            TestContext context = CreateContext();
            context.AddPart("alice", "bakunawa", PartSlot.Head, PartRarity.Epic);
            context.AddPart("alice", "magsasaka", PartSlot.Body, PartRarity.Common);
            context.AddPart("alice", "pulis", PartSlot.Legs, PartRarity.Rare);
            context.Service.InitializeCard("alice", "card");

            Assert.That(context.Service.EquipHead("alice", "card", "bakunawa", 0, "h").Succeeded, Is.True);
            Assert.That(context.Service.EquipBody("alice", "card", "magsasaka", 1, "b").Succeeded, Is.True);
            Assert.That(context.Service.EquipLegs("alice", "card", "pulis", 2, "l").Succeeded, Is.True);
            Assert.That(context.Service.ValidateCardBuild("alice", "card"), Is.True);
        }

        [Test]
        public void WrongSlotAndUnownedPartAreRejected()
        {
            TestContext context = CreateContext();
            context.AddPart("alice", "head", PartSlot.Head, PartRarity.Common);
            context.AddPart("alice", "foreign", PartSlot.Body, PartRarity.Common, "bob");
            context.Service.InitializeCard("alice", "card");

            Assert.That(context.Service.EquipBody("alice", "card", "head", 0, "wrong").Failure,
                Is.EqualTo(CardAssemblyFailure.WrongSlot));
            Assert.That(context.Service.EquipBody("alice", "card", "foreign", 0, "foreign").Failure,
                Is.EqualTo(CardAssemblyFailure.PartNotOwned));
        }

        [Test]
        public void OneInstanceCannotOccupyMoreThanOneCard()
        {
            TestContext context = CreateContext();
            context.AddPart("alice", "head", PartSlot.Head, PartRarity.Common);
            context.Service.InitializeCard("alice", "first");
            context.Service.InitializeCard("alice", "second");
            context.Service.EquipHead("alice", "first", "head", 0, "first-equip");

            CardAssemblyOperationResult result = context.Service.EquipHead(
                "alice", "second", "head", 0, "second-equip");

            Assert.That(result.Failure, Is.EqualTo(CardAssemblyFailure.AlreadyEquipped));
        }

        [Test]
        public void ReplacementReturnsOriginalInstanceToBagWithoutChangingMaintenance()
        {
            TestContext context = CreateContext();
            OwnedCardPart first = context.AddPart("alice", "first", PartSlot.Head, PartRarity.Common);
            OwnedCardPart second = context.AddPart("alice", "second", PartSlot.Head, PartRarity.Rare);
            first.TrySetMaintenance(7);
            second.TrySetMaintenance(5);
            context.Service.InitializeCard("alice", "card");
            context.Service.EquipHead("alice", "card", "first", 0, "one");

            CardAssemblyOperationResult result = context.Service.ReplacePart(
                "alice", "card", PartSlot.Head, "second", 1, "two");

            Assert.That(result.Succeeded, Is.True);
            Assert.That(first.IsEquipped, Is.False);
            Assert.That(first.CurrentMaintenance, Is.EqualTo(7));
            Assert.That(second.CurrentMaintenance, Is.EqualTo(5));
            Assert.That(context.Inventory.Parts.Count, Is.EqualTo(2));
        }

        [Test]
        public void UnequipPreservesOwnedInstanceAndMaintenance()
        {
            TestContext context = CreateContext();
            OwnedCardPart part = context.AddPart("alice", "body", PartSlot.Body, PartRarity.Common);
            part.TrySetMaintenance(3);
            context.Service.InitializeCard("alice", "card");
            context.Service.EquipBody("alice", "card", "body", 0, "equip");

            CardAssemblyOperationResult result = context.Service.UnequipBody("alice", "card", 1, "unequip");

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Snapshot.Body.IsEmpty, Is.True);
            Assert.That(part.CurrentMaintenance, Is.EqualTo(3));
            Assert.That(context.Inventory.TryGetOwnedPart("body", out _), Is.True);
        }

        [Test]
        public void EquipmentChangesRecalculateConfiguredStats()
        {
            TestContext context = CreateContext();
            context.AddPart("alice", "head", PartSlot.Head, PartRarity.Common, hp: 25, attack: 50f);
            context.Service.InitializeCard("alice", "card");

            CardBuildSnapshot snapshot = context.Service.EquipHead(
                "alice", "card", "head", 0, "equip").Snapshot;

            Assert.That(snapshot.Stats.MaximumHitPoints, Is.EqualTo(125));
            Assert.That(snapshot.Stats.AttackBonusPercent, Is.EqualTo(50f));
            Assert.That(snapshot.Stats.EffectiveAttack, Is.EqualTo(15));
        }

        [Test]
        public void ZeroMaintenanceDisablesNonHpButRetainsHpUntilBattleUnlock()
        {
            TestContext context = CreateContext();
            OwnedCardPart part = context.AddPart(
                "alice", "head", PartSlot.Head, PartRarity.Common, hp: 25, attack: 50f);
            context.Service.InitializeCard("alice", "card");
            context.Service.EquipHead("alice", "card", "head", 0, "equip");
            context.Service.LockForBattle("alice", "card");
            part.TrySetMaintenance(0);

            CardBuildSnapshot during = context.Service.RecalculateCardStats("alice", "card").Snapshot;
            CardBuildSnapshot after = context.Service.UnlockForPreparation("alice", "card").Snapshot;

            Assert.That(during.Stats.MaximumHitPoints, Is.EqualTo(125));
            Assert.That(during.Stats.EffectiveAttack, Is.EqualTo(10));
            Assert.That(after.Stats.MaximumHitPoints, Is.EqualTo(100));
            Assert.That(part.IsEquipped, Is.True);
        }

        [Test]
        public void ActiveBattleRejectsEquipmentChanges()
        {
            TestContext context = CreateContext();
            context.AddPart("alice", "head", PartSlot.Head, PartRarity.Common);
            context.Service.InitializeCard("alice", "card");
            context.Service.LockForBattle("alice", "card");

            CardAssemblyOperationResult result = context.Service.EquipHead(
                "alice", "card", "head", 0, "equip");

            Assert.That(result.Failure, Is.EqualTo(CardAssemblyFailure.EquipmentLocked));
        }

        [Test]
        public void AccessPolicyRejectsChangesOutsidePreparation()
        {
            TestContext context = CreateContext(allowChanges: false);
            context.AddPart("alice", "head", PartSlot.Head, PartRarity.Common);
            context.Service.InitializeCard("alice", "card");

            Assert.That(context.Service.EquipHead("alice", "card", "head", 0, "equip").Failure,
                Is.EqualTo(CardAssemblyFailure.EquipmentLocked));
        }

        [Test]
        public void UnlockAfterDefeatDoesNotDestroyBuildOrParts()
        {
            TestContext context = CreateContext();
            OwnedCardPart part = context.AddPart("alice", "head", PartSlot.Head, PartRarity.Common);
            context.Service.InitializeCard("alice", "card");
            context.Service.EquipHead("alice", "card", "head", 0, "equip");
            context.Service.LockForBattle("alice", "card");

            CardBuildSnapshot snapshot = context.Service.UnlockForPreparation("alice", "card").Snapshot;

            Assert.That(snapshot.Head.InstanceId, Is.EqualTo("head"));
            Assert.That(part.IsEquipped, Is.True);
            Assert.That(context.Inventory.Parts.Count, Is.EqualTo(1));
        }

        [Test]
        public void ExistingSkillLoadoutIsReadWithoutBeingMutated()
        {
            var basic = new SkillDefinition("basic", SkillCategory.Normal, 0, Array.Empty<ISkillEffect>());
            var skillSource = new TestSkillSource(basic);
            TestContext context = CreateContext(skills: skillSource);
            context.Service.InitializeCard("alice", "card");

            Assert.That(context.Service.TryGetLoadout("alice", "card", out CardBattleLoadout loadout), Is.True);
            Assert.That(loadout.Skills.Select(value => value.Id), Is.EqualTo(new[] { "basic" }));
            Assert.That(skillSource.Skills.Count, Is.EqualTo(1));
        }

        [Test]
        public void SnapshotContainsOwnerDefinitionAndVisualIdsInLayerOrder()
        {
            TestContext context = CreateContext();
            context.AddPart("alice", "head", PartSlot.Head, PartRarity.Common);
            context.AddPart("alice", "body", PartSlot.Body, PartRarity.Common);
            context.AddPart("alice", "legs", PartSlot.Legs, PartRarity.Common);
            context.Service.InitializeCard("alice", "card");
            context.Service.EquipHead("alice", "card", "head", 0, "h");
            context.Service.EquipBody("alice", "card", "body", 1, "b");
            CardBuildSnapshot snapshot = context.Service.EquipLegs(
                "alice", "card", "legs", 2, "l").Snapshot;

            Assert.That(snapshot.OwnerPlayerId, Is.EqualTo("alice"));
            Assert.That(snapshot.Head.DefinitionId, Is.EqualTo("definition-head"));
            Assert.That(snapshot.Body.DefinitionId, Is.EqualTo("definition-body"));
            Assert.That(snapshot.Legs.DefinitionId, Is.EqualTo("definition-legs"));
            Assert.That(snapshot.Legs.VisualLayerOrder, Is.LessThan(snapshot.Body.VisualLayerOrder));
            Assert.That(snapshot.Body.VisualLayerOrder, Is.LessThan(snapshot.Head.VisualLayerOrder));
        }

        [Test]
        public void ExistingArProviderConsumesAssemblyLoadoutWithoutCreatingCardIds()
        {
            TestContext context = CreateContext();
            context.AddPart("alice", "head", PartSlot.Head, PartRarity.Common);
            context.Service.InitializeCard("alice", "assembled");
            context.Service.EquipHead("alice", "assembled", "head", 0, "equip");
            var provider = new CardAssemblyARSnapshotProvider(context.Service);

            bool found = provider.TryGetSnapshot("alice", "assembled", out ARCardConfigurationSnapshot snapshot);

            Assert.That(found, Is.True);
            Assert.That(snapshot.HeadDefinitionId, Is.EqualTo("definition-head"));
            Assert.That(snapshot.HeadVisualAssetId, Is.EqualTo("definition-head"));
        }

        [Test]
        public void StaleAndDuplicateRequestsLeaveInventoryAndBuildUnchanged()
        {
            TestContext context = CreateContext();
            context.AddPart("alice", "head", PartSlot.Head, PartRarity.Common);
            context.AddPart("alice", "other", PartSlot.Head, PartRarity.Common);
            context.Service.InitializeCard("alice", "card");
            CardAssemblyOperationResult first = context.Service.EquipHead(
                "alice", "card", "head", 0, "command");

            CardAssemblyOperationResult duplicate = context.Service.EquipHead(
                "alice", "card", "other", 1, "command");
            CardAssemblyOperationResult stale = context.Service.EquipHead(
                "alice", "card", "other", 0, "stale");

            Assert.That(first.Succeeded, Is.True);
            Assert.That(duplicate.Failure, Is.EqualTo(CardAssemblyFailure.DuplicateCommand));
            Assert.That(stale.Failure, Is.EqualTo(CardAssemblyFailure.StaleRevision));
            Assert.That(context.Service.GetEquippedParts("alice", "card").Single().InstanceId,
                Is.EqualTo("head"));
            Assert.That(context.Inventory.Parts.Count, Is.EqualTo(2));
        }

        [Test]
        public void EventsReportInitializationReplacementAndBattleLockLifecycle()
        {
            TestContext context = CreateContext();
            context.AddPart("alice", "first", PartSlot.Head, PartRarity.Common);
            context.AddPart("alice", "second", PartSlot.Head, PartRarity.Common);
            var events = new List<CardAssemblyEventType>();
            context.Service.EventPublished += value => events.Add(value.Type);

            context.Service.InitializeCard("alice", "card");
            context.Service.EquipHead("alice", "card", "first", 0, "one");
            context.Service.ReplacePart("alice", "card", PartSlot.Head, "second", 1, "two");
            context.Service.LockForBattle("alice", "card");
            context.Service.UnlockForPreparation("alice", "card");

            Assert.That(events, Does.Contain(CardAssemblyEventType.CardInitialized));
            Assert.That(events, Does.Contain(CardAssemblyEventType.HeadEquipped));
            Assert.That(events, Does.Contain(CardAssemblyEventType.PartReplaced));
            Assert.That(events, Does.Contain(CardAssemblyEventType.CardBuildLockedForBattle));
            Assert.That(events, Does.Contain(CardAssemblyEventType.CardBuildUnlockedForPreparation));
        }

        private static TestContext CreateContext(
            bool allowChanges = true,
            ICardSkillLoadoutSource skills = null)
        {
            var inventory = new TestInventory("alice");
            var provider = new TestInventoryProvider(inventory);
            var service = new AuthoritativeCardAssemblyService(
                provider,
                new FixedCardAssemblyAccessPolicy(allowChanges),
                new FixedCardAssemblyAuthority(true),
                skills);
            return new TestContext(service, inventory);
        }

        private sealed class TestContext
        {
            public TestContext(AuthoritativeCardAssemblyService service, TestInventory inventory)
            {
                Service = service;
                Inventory = inventory;
            }

            public AuthoritativeCardAssemblyService Service { get; }
            public TestInventory Inventory { get; }

            public OwnedCardPart AddPart(
                string inventoryOwner,
                string instanceId,
                PartSlot slot,
                PartRarity rarity,
                string actualOwner = null,
                int hp = 0,
                float attack = 0f)
            {
                string definitionId = "definition-" + instanceId;
                var battle = new PartDefinition(definitionId, slot, rarity, hp, attack);
                var definition = new CraftablePartDefinition(
                    definitionId,
                    definitionId,
                    "character-" + instanceId,
                    slot,
                    rarity,
                    "recipe-" + instanceId,
                    battle,
                    characterDisplayName: "Character");
                var part = new OwnedCardPart(instanceId, actualOwner ?? inventoryOwner, definition);
                Inventory.Parts.Add(instanceId, part);
                return part;
            }
        }

        private sealed class TestInventoryProvider : IOwnedCardPartInventoryProvider
        {
            private readonly TestInventory inventory;

            public TestInventoryProvider(TestInventory inventory)
            {
                this.inventory = inventory;
            }

            public bool TryGetInventory(string playerId, out IOwnedCardPartInventory found)
            {
                found = string.Equals(playerId, inventory.PlayerId, StringComparison.Ordinal)
                    ? inventory
                    : null;
                return found != null;
            }
        }

        private sealed class TestInventory : IOwnedCardPartInventory
        {
            public TestInventory(string playerId)
            {
                PlayerId = playerId;
            }

            public string PlayerId { get; }
            public Dictionary<string, OwnedCardPart> Parts { get; } =
                new Dictionary<string, OwnedCardPart>(StringComparer.Ordinal);

            public int GetMaterialQuantity(string materialId) => 0;

            public bool TryCraft(
                CraftingRecipe recipe,
                OwnedCardPart newPart,
                out IReadOnlyList<MaterialBalanceChange> materialChanges)
            {
                materialChanges = Array.Empty<MaterialBalanceChange>();
                return false;
            }

            public bool TryAddMaterial(MaterialDefinition material, int quantity, out int resultingQuantity)
            {
                resultingQuantity = 0;
                return false;
            }

            public bool TryGetOwnedPart(string instanceId, out OwnedCardPart part)
            {
                return Parts.TryGetValue(instanceId ?? string.Empty, out part);
            }
        }

        private sealed class TestSkillSource : ICardSkillLoadoutSource
        {
            public TestSkillSource(params SkillDefinition[] skills)
            {
                Skills = skills;
            }

            public IReadOnlyList<SkillDefinition> Skills { get; }

            public bool TryGetSkills(string playerId, out IReadOnlyList<SkillDefinition> skills)
            {
                skills = Skills;
                return true;
            }
        }
    }
}
