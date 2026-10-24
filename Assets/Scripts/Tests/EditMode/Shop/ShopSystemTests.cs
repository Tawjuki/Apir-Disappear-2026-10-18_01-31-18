using System;
using System.Collections.Generic;
using System.Linq;
using ApirApir.Battle;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

namespace ApirApir.Shop.Tests
{
    public sealed class ShopSystemTests
    {
        private static readonly DateTimeOffset StartTime =
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        [Test]
        public void SnapshotRpc_UsesOnlyNetcodeSupportedPayloadParameters()
        {
            System.Reflection.MethodInfo method = typeof(NetcodeShopBridge).GetMethod(
                "PublishSnapshotClientRpc",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);
            Type[] parameterTypes = method.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
            Assert.That(parameterTypes, Is.EqualTo(new[] { typeof(string), typeof(ClientRpcParams) }));
            Assert.That(parameterTypes.Any(type => type.IsArray), Is.False);
        }

        [Test]
        public void SnapshotPayload_RoundTripsAllNineListingsInOrder()
        {
            WireSnapshotPayload payload = CreateWirePayload();

            bool decoded = TryDecodeSnapshot(JsonUtility.ToJson(payload), out ShopNetworkSnapshot snapshot);

            Assert.That(decoded, Is.True);
            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot.MatchId, Is.EqualTo("match"));
            Assert.That(snapshot.RoundId, Is.EqualTo("round-1"));
            Assert.That(snapshot.OfferIds.Count, Is.EqualTo(ShopInventory.TotalListingCount));
            Assert.That(snapshot.OfferIds, Is.EqualTo(payload.OfferIds));
            Assert.That(snapshot.ItemIds, Is.EqualTo(payload.ItemIds));
            Assert.That(snapshot.ListingKinds.Take(6),
                Is.All.EqualTo((byte)ShopListingKind.Randomized));
            Assert.That(snapshot.ListingKinds.Skip(6),
                Is.All.EqualTo((byte)ShopListingKind.Permanent));
            Assert.That(snapshot.PurchasePrices, Is.EqualTo(payload.PurchasePrices));
            Assert.That(snapshot.RemainingStock, Is.EqualTo(payload.RemainingStock));
            Assert.That(snapshot.UnlimitedStock, Is.EqualTo(payload.UnlimitedStock));
        }

        [Test]
        public void SnapshotPayload_RejectsMalformedOrMisalignedData()
        {
            WireSnapshotPayload mismatched = CreateWirePayload();
            mismatched.ItemIds = mismatched.ItemIds.Take(8).ToArray();

            Assert.That(TryDecodeSnapshot("{not-json", out _), Is.False);
            Assert.That(TryDecodeSnapshot(JsonUtility.ToJson(mismatched), out _), Is.False);
            Assert.That(TryDecodeSnapshot(string.Empty, out _), Is.False);
        }

        [Test]
        public void Rotation_HasSixRandomAndThreePermanentListings()
        {
            TestContext context = CreateContext(new SequenceRandom(
                new[] { 0.1d, 0.6d, 0.8d, 0.95d, 0.1d, 0.95d }));
            context.Controller.BeginShopPhase("round-1", StartTime);
            IReadOnlyList<ShopOffer> offers = context.Controller.CurrentInventory.Offers;

            Assert.That(offers.Count, Is.EqualTo(9));
            Assert.That(offers.Count(o => o.ListingKind == ShopListingKind.Randomized), Is.EqualTo(6));
            Assert.That(offers.Count(o => o.ListingKind == ShopListingKind.Permanent), Is.EqualTo(3));
            Assert.That(offers[6].Item.Id, Is.EqualTo("papel"));
            Assert.That(offers[7].Item.Id, Is.EqualTo("krayola"));
            Assert.That(offers[8].Item.Id, Is.EqualTo("lapis"));
            Assert.That(offers.Take(6).Any(o => o.Item.Category == ShopItemCategory.MaintenanceSupply), Is.False);
        }

        [TestCase(0.0d, ShopItemCategory.CommonMaterial)]
        [TestCase(0.499999d, ShopItemCategory.CommonMaterial)]
        [TestCase(0.5d, ShopItemCategory.RareMaterial)]
        [TestCase(0.749999d, ShopItemCategory.RareMaterial)]
        [TestCase(0.75d, ShopItemCategory.EpicMaterial)]
        [TestCase(0.899999d, ShopItemCategory.EpicMaterial)]
        [TestCase(0.9d, ShopItemCategory.CompletedPart)]
        [TestCase(0.999999d, ShopItemCategory.CompletedPart)]
        public void CategoryRoll_UsesExactProbabilityBoundaries(double roll, ShopItemCategory expected)
        {
            Assert.That(ShopRotationGenerator.RollCategory(roll), Is.EqualTo(expected));
        }

        [Test]
        public void CategoryDistribution_MatchesConfiguredWeightsAcrossLargeDeterministicSample()
        {
            var counts = new Dictionary<ShopItemCategory, int>();
            for (int i = 0; i < 10000; i++)
            {
                ShopItemCategory category = ShopRotationGenerator.RollCategory(i / 10000d);
                counts[category] = counts.TryGetValue(category, out int count) ? count + 1 : 1;
            }

            Assert.That(counts[ShopItemCategory.CommonMaterial], Is.EqualTo(5000));
            Assert.That(counts[ShopItemCategory.RareMaterial], Is.EqualTo(2500));
            Assert.That(counts[ShopItemCategory.EpicMaterial], Is.EqualTo(1500));
            Assert.That(counts[ShopItemCategory.CompletedPart], Is.EqualTo(1000));
        }

        [Test]
        public void SeededRandom_ProducesReproducibleRotations()
        {
            TestContext first = CreateContext(new SystemShopRandomProvider(12345));
            TestContext second = CreateContext(new SystemShopRandomProvider(12345));
            first.Controller.BeginShopPhase("round", StartTime);
            second.Controller.BeginShopPhase("round", StartTime);

            Assert.That(
                second.Controller.CurrentInventory.Offers.Select(o => o.Item.Id),
                Is.EqualTo(first.Controller.CurrentInventory.Offers.Select(o => o.Item.Id)));
        }

        [Test]
        public void ReopeningSameRound_DoesNotReroll()
        {
            var random = new CountingRandom(0.1d);
            TestContext context = CreateContext(random);
            context.Controller.BeginShopPhase("round", StartTime);
            long version = context.Controller.CurrentInventory.RotationVersion;
            string[] itemIds = context.Controller.CurrentInventory.Offers.Select(o => o.Item.Id).ToArray();

            context.Controller.EndShopPhase();
            context.Controller.BeginShopPhase("round", StartTime.AddMinutes(1));

            Assert.That(context.Controller.CurrentInventory.RotationVersion, Is.EqualTo(version));
            Assert.That(context.Controller.CurrentInventory.Offers.Select(o => o.Item.Id), Is.EqualTo(itemIds));
            Assert.That(random.UnitCalls, Is.EqualTo(6));
        }

        [Test]
        public void NewRound_GeneratesNewRotationVersion()
        {
            TestContext context = CreateContext(new SequenceRandom(
                new[] { 0.1d, 0.1d, 0.1d, 0.1d, 0.1d, 0.1d,
                        0.95d, 0.95d, 0.95d, 0.95d, 0.95d, 0.95d }));
            context.Controller.BeginShopPhase("round-1", StartTime);
            context.Controller.EndShopPhase();
            context.Controller.BeginShopPhase("round-2", StartTime.AddMinutes(1));

            Assert.That(context.Controller.CurrentInventory.RotationVersion, Is.EqualTo(2));
            Assert.That(context.Controller.CurrentInventory.RoundId, Is.EqualTo("round-2"));
            Assert.That(context.Controller.CurrentInventory.Offers.Take(6)
                .All(o => o.Item.Category == ShopItemCategory.CompletedPart), Is.True);
        }

        [Test]
        public void PreviouslyGeneratedRoundId_CannotBeRerolledAfterANewerRound()
        {
            var random = new CountingRandom(0.1d);
            TestContext context = CreateContext(random);
            context.Controller.BeginShopPhase("round-1", StartTime);
            context.Controller.EndShopPhase();
            context.Controller.BeginShopPhase("round-2", StartTime.AddMinutes(1));
            context.Controller.EndShopPhase();

            bool reopenedOldRound = context.Controller.BeginShopPhase(
                "round-1",
                StartTime.AddMinutes(2));

            Assert.That(reopenedOldRound, Is.False);
            Assert.That(context.Controller.CurrentInventory.RoundId, Is.EqualTo("round-2"));
            Assert.That(random.UnitCalls, Is.EqualTo(12));
        }

        [Test]
        public void AllPlayersReadSameAuthoritativeSnapshot()
        {
            TestContext context = CreateContext(new SequenceRandom(new[] { 0.1d }));
            context.Controller.BeginShopPhase("round", StartTime);

            ShopSnapshot first = context.Controller.CreateSnapshot("p1");
            ShopSnapshot second = context.Controller.CreateSnapshot("p2");

            Assert.That(second.RotationVersion, Is.EqualTo(first.RotationVersion));
            Assert.That(second.Offers.Select(o => o.OfferId), Is.EqualTo(first.Offers.Select(o => o.OfferId)));
            Assert.That(second.Offers.Select(o => o.ItemId), Is.EqualTo(first.Offers.Select(o => o.ItemId)));
        }

        [Test]
        public void Purchase_DeductsMarketValueAndAddsInventoryItem()
        {
            TestContext context = CreateContext(new SequenceRandom(new[] { 0.1d }));
            context.Accounts.Add("p1", 500);
            context.Controller.BeginShopPhase("round", StartTime);
            ShopOffer offer = context.Controller.CurrentInventory.Offers[0];

            ShopTransactionResult result = Purchase(context, "p1", offer, 2, "buy-1");

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.CurrencyAmount, Is.EqualTo(offer.Item.MarketValue * 2));
            Assert.That(context.Accounts["p1"].CurrencyBalance, Is.EqualTo(300));
            Assert.That(context.Accounts["p1"].GetItemQuantity(offer.Item.Id), Is.EqualTo(2));
        }

        [Test]
        public void InsufficientFunds_RejectsWithoutSideEffects()
        {
            TestContext context = CreateContext(new SequenceRandom(new[] { 0.1d }));
            context.Accounts.Add("p1", 50);
            context.Controller.BeginShopPhase("round", StartTime);
            ShopOffer offer = context.Controller.CurrentInventory.Offers[0];

            ShopTransactionResult result = Purchase(context, "p1", offer, 1, "buy-1");

            Assert.That(result.Failure, Is.EqualTo(ShopTransactionFailure.InsufficientFunds));
            Assert.That(context.Accounts["p1"].CurrencyBalance, Is.EqualTo(50));
            Assert.That(context.Accounts["p1"].GetItemQuantity(offer.Item.Id), Is.Zero);
        }

        [Test]
        public void Sale_PaysSeventyFivePercentAndRemovesQuantity()
        {
            TestContext context = CreateContext(new SequenceRandom(new[] { 0.1d }));
            context.Accounts.Add("p1", 0);
            context.Accounts["p1"].SetQuantity("common", 3);
            context.Controller.BeginShopPhase("round", StartTime);

            ShopTransactionResult result = context.Controller.Sell(
                new ShopSaleRequest("match", 1, "common", 2, "sell-1"),
                "p1");

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.CurrencyAmount, Is.EqualTo(150));
            Assert.That(context.Accounts["p1"].CurrencyBalance, Is.EqualTo(150));
            Assert.That(context.Accounts["p1"].GetItemQuantity("common"), Is.EqualTo(1));
        }

        [Test]
        public void SaleRounding_IsIsolatedInPricingPolicy()
        {
            IShopItemDefinition item = Material(
                "odd",
                ShopItemCategory.CommonMaterial,
                ShopMaterialRarity.Common,
                101);
            var policy = new MarketValueShopPricingPolicy(0.75m, MidpointRounding.AwayFromZero);

            Assert.That(policy.CalculateSalePayout(item, 1), Is.EqualTo(76));
        }

        [Test]
        public void DuplicatePurchase_CannotCreateExtraItems()
        {
            TestContext context = CreateContext(new SequenceRandom(new[] { 0.1d }));
            context.Accounts.Add("p1", 500);
            context.Controller.BeginShopPhase("round", StartTime);
            ShopOffer offer = context.Controller.CurrentInventory.Offers[0];
            Purchase(context, "p1", offer, 1, "same-command");

            ShopTransactionResult duplicate = Purchase(context, "p1", offer, 1, "same-command");

            Assert.That(duplicate.Failure, Is.EqualTo(ShopTransactionFailure.DuplicateCommand));
            Assert.That(context.Accounts["p1"].GetItemQuantity(offer.Item.Id), Is.EqualTo(1));
            Assert.That(context.Accounts["p1"].CurrencyBalance, Is.EqualTo(400));
        }

        [Test]
        public void DuplicateSale_CannotGenerateExtraCurrency()
        {
            TestContext context = CreateContext(new SequenceRandom(new[] { 0.1d }));
            context.Accounts.Add("p1", 0);
            context.Accounts["p1"].SetQuantity("common", 2);
            context.Controller.BeginShopPhase("round", StartTime);
            var request = new ShopSaleRequest("match", 1, "common", 1, "same-command");
            context.Controller.Sell(request, "p1");

            ShopTransactionResult duplicate = context.Controller.Sell(request, "p1");

            Assert.That(duplicate.Failure, Is.EqualTo(ShopTransactionFailure.DuplicateCommand));
            Assert.That(context.Accounts["p1"].CurrencyBalance, Is.EqualTo(75));
            Assert.That(context.Accounts["p1"].GetItemQuantity("common"), Is.EqualTo(1));
        }

        [Test]
        public void LimitedStock_CannotBeOversold()
        {
            TestContext context = CreateContext(
                new SequenceRandom(new[] { 0.1d }),
                new FixedStockPolicy(1));
            context.Accounts.Add("p1", 500);
            context.Accounts.Add("p2", 500);
            context.Controller.BeginShopPhase("round", StartTime);
            ShopOffer offer = context.Controller.CurrentInventory.Offers[0];
            Purchase(context, "p1", offer, 1, "buy-1");

            ShopTransactionResult second = Purchase(context, "p2", offer, 1, "buy-2");

            Assert.That(second.Failure, Is.EqualTo(ShopTransactionFailure.OutOfStock));
            Assert.That(context.Accounts["p2"].CurrencyBalance, Is.EqualTo(500));
            Assert.That(offer.RemainingStock, Is.Zero);
        }

        [Test]
        public void FailedAtomicAccountMutation_DoesNotConsumeStock()
        {
            TestContext context = CreateContext(
                new SequenceRandom(new[] { 0.1d }),
                new FixedStockPolicy(1));
            context.Accounts.Add("p1", 500);
            context.Accounts["p1"].FailNextTransaction = true;
            context.Controller.BeginShopPhase("round", StartTime);
            ShopOffer offer = context.Controller.CurrentInventory.Offers[0];

            ShopTransactionResult result = Purchase(context, "p1", offer, 1, "buy-1");

            Assert.That(result.Failure, Is.EqualTo(ShopTransactionFailure.AccountTransactionFailed));
            Assert.That(offer.RemainingStock, Is.EqualTo(1));
            Assert.That(context.Accounts["p1"].CurrencyBalance, Is.EqualTo(500));
        }

        [Test]
        public void StaleRotationRequest_IsRejected()
        {
            TestContext context = CreateContext(new SequenceRandom(new[] { 0.1d }));
            context.Accounts.Add("p1", 500);
            context.Controller.BeginShopPhase("round", StartTime);
            ShopOffer offer = context.Controller.CurrentInventory.Offers[0];

            ShopTransactionResult result = context.Controller.Purchase(
                new ShopPurchaseRequest("match", 0, offer.OfferId, 1, "buy-1"),
                "p1");

            Assert.That(result.Failure, Is.EqualTo(ShopTransactionFailure.StaleRotation));
            Assert.That(context.Accounts["p1"].CurrencyBalance, Is.EqualTo(500));
        }

        [Test]
        public void TransactionsOutsideShopPhase_AreRejected()
        {
            TestContext context = CreateContext(new SequenceRandom(new[] { 0.1d }));
            context.Accounts.Add("p1", 500);
            context.Controller.BeginShopPhase("round", StartTime);
            ShopOffer offer = context.Controller.CurrentInventory.Offers[0];
            context.Controller.EndShopPhase();

            ShopTransactionResult result = Purchase(context, "p1", offer, 1, "buy-1");

            Assert.That(result.Failure, Is.EqualTo(ShopTransactionFailure.ShopClosed));
        }

        [Test]
        public void NewlyRegisteredCatalogItem_IsImmediatelyEligible()
        {
            TestContext context = CreateContext(
                new SequenceRandom(new[] { 0.1d }, new[] { 1, 0, 0, 0, 0, 0 }));
            context.Catalog.Register(Material(
                "new-common",
                ShopItemCategory.CommonMaterial,
                ShopMaterialRarity.Common,
                20));
            context.Controller.BeginShopPhase("round", StartTime);

            Assert.That(context.Controller.CurrentInventory.Offers[0].Item.Id, Is.EqualTo("new-common"));
        }

        [Test]
        public void EmptyRequiredCatalogPool_ProducesConfigurationError()
        {
            ShopItemCatalog catalog = CreateCatalog(includeEpic: false);
            PermanentShopItems permanent = GetPermanent(catalog);
            var generator = new ShopRotationGenerator(
                catalog,
                permanent,
                new SequenceRandom(new[] { 0.1d }),
                new MarketValueShopPricingPolicy(),
                new UnlimitedShopStockPolicy());

            ShopConfigurationException exception = Assert.Throws<ShopConfigurationException>(
                () => generator.Generate("round", 1));
            Assert.That(exception.Message, Does.Contain("EpicMaterial"));
        }

        [Test]
        public void DefaultDuplicatePolicy_AllowsSixIdenticalCommonOffers()
        {
            TestContext context = CreateContext(new SequenceRandom(new[] { 0.1d }));
            context.Controller.BeginShopPhase("round", StartTime);

            Assert.That(context.Controller.CurrentInventory.Offers.Take(6)
                .All(o => o.Item.Id == "common"), Is.True);
        }

        [Test]
        public void PermanentListing_RemainsWhenItsLimitedStockIsZero()
        {
            TestContext context = CreateContext(
                new SequenceRandom(new[] { 0.1d }),
                new FixedStockPolicy(0));
            context.Controller.BeginShopPhase("round", StartTime);

            Assert.That(context.Controller.CurrentInventory.Offers.Count, Is.EqualTo(9));
            Assert.That(context.Controller.CurrentInventory.Offers[6].Item.Id, Is.EqualTo("papel"));
            Assert.That(context.Controller.CurrentInventory.Offers[6].IsAvailable, Is.False);
        }

        [Test]
        public void CompletedPart_IsProtectedFromSelling()
        {
            TestContext context = CreateContext(new SequenceRandom(new[] { 0.1d }));
            context.Accounts.Add("p1", 0);
            context.Accounts["p1"].SetQuantity("part", 1);
            context.Controller.BeginShopPhase("round", StartTime);

            ShopTransactionResult result = context.Controller.Sell(
                new ShopSaleRequest("match", 1, "part", 1, "sell-part"),
                "p1");

            Assert.That(result.Failure, Is.EqualTo(ShopTransactionFailure.ItemNotSellable));
            Assert.That(context.Accounts["p1"].GetItemQuantity("part"), Is.EqualTo(1));
        }

        [Test]
        public void NonAuthoritativeShop_CannotGenerateOrTransact()
        {
            TestContext context = CreateContext(
                new SequenceRandom(new[] { 0.1d }),
                authoritative: false);
            context.Accounts.Add("p1", 500);

            Assert.That(context.Controller.BeginShopPhase("round", StartTime), Is.False);
            ShopTransactionResult result = context.Controller.Purchase(
                new ShopPurchaseRequest("match", 1, "1:0", 1, "buy"),
                "p1");
            Assert.That(result.Failure, Is.EqualTo(ShopTransactionFailure.NotAuthoritative));
        }

        private static ShopTransactionResult Purchase(
            TestContext context,
            string playerId,
            ShopOffer offer,
            int quantity,
            string commandId)
        {
            return context.Controller.Purchase(
                new ShopPurchaseRequest(
                    "match",
                    context.Controller.CurrentInventory.RotationVersion,
                    offer.OfferId,
                    quantity,
                    commandId),
                playerId);
        }

        private static bool TryDecodeSnapshot(
            string serializedSnapshot,
            out ShopNetworkSnapshot snapshot)
        {
            System.Reflection.MethodInfo method = typeof(NetcodeShopBridge).GetMethod(
                "TryDeserializeSnapshot",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            object[] arguments = { serializedSnapshot, null };
            bool succeeded = (bool)method.Invoke(null, arguments);
            snapshot = arguments[1] as ShopNetworkSnapshot;
            return succeeded;
        }

        private static WireSnapshotPayload CreateWirePayload()
        {
            int count = ShopInventory.TotalListingCount;
            return new WireSnapshotPayload
            {
                MatchId = "match",
                RoundId = "round-1",
                RotationVersion = 4,
                State = (byte)ShopLifecycleState.Open,
                ClosesAtUnixMilliseconds = StartTime.ToUnixTimeMilliseconds(),
                OfferIds = Enumerable.Range(0, count).Select(i => "offer-" + i).ToArray(),
                ItemIds = Enumerable.Range(0, count).Select(i => "item-" + i).ToArray(),
                Categories = Enumerable.Range(0, count)
                    .Select(i => (byte)(i < 6
                        ? ShopItemCategory.CommonMaterial
                        : ShopItemCategory.MaintenanceSupply)).ToArray(),
                ListingKinds = Enumerable.Range(0, count)
                    .Select(i => (byte)(i < 6
                        ? ShopListingKind.Randomized
                        : ShopListingKind.Permanent)).ToArray(),
                PurchasePrices = Enumerable.Range(0, count).Select(i => 100 + i).ToArray(),
                RemainingStock = Enumerable.Range(0, count).Select(i => i).ToArray(),
                UnlimitedStock = Enumerable.Range(0, count).Select(i => i % 2 == 0).ToArray()
            };
        }

        private static TestContext CreateContext(
            IShopRandomProvider random,
            IShopStockPolicy stockPolicy = null,
            bool authoritative = true)
        {
            ShopItemCatalog catalog = CreateCatalog();
            PermanentShopItems permanent = GetPermanent(catalog);
            var pricing = new MarketValueShopPricingPolicy();
            var accounts = new FakeAccounts();
            var generator = new ShopRotationGenerator(
                catalog,
                permanent,
                random,
                pricing,
                stockPolicy ?? new UnlimitedShopStockPolicy());
            var controller = new ShopController(
                "match",
                new ShopSettings(),
                new FixedShopAuthority(authoritative),
                generator,
                catalog,
                pricing,
                accounts,
                new AllowRegisteredShopPlayers(accounts));
            return new TestContext(controller, catalog, accounts);
        }

        private static ShopItemCatalog CreateCatalog(bool includeEpic = true)
        {
            var catalog = new ShopItemCatalog();
            catalog.Register(Material(
                "common", ShopItemCategory.CommonMaterial, ShopMaterialRarity.Common, 100));
            catalog.Register(Material(
                "rare", ShopItemCategory.RareMaterial, ShopMaterialRarity.Rare, 200));
            if (includeEpic)
            {
                catalog.Register(Material(
                    "epic", ShopItemCategory.EpicMaterial, ShopMaterialRarity.Epic, 300));
            }

            catalog.Register(new ShopItemDefinition(
                "part", "Part", ShopItemCategory.CompletedPart, new object(), 400,
                completedPartSlot: PartSlot.Head));
            catalog.Register(Supply("papel", 10));
            catalog.Register(Supply("krayola", 20));
            catalog.Register(Supply("lapis", 30));
            return catalog;
        }

        private static PermanentShopItems GetPermanent(ShopItemCatalog catalog)
        {
            catalog.TryGet("papel", out IShopItemDefinition papel);
            catalog.TryGet("krayola", out IShopItemDefinition krayola);
            catalog.TryGet("lapis", out IShopItemDefinition lapis);
            return new PermanentShopItems(papel, krayola, lapis);
        }

        private static IShopItemDefinition Material(
            string id,
            ShopItemCategory category,
            ShopMaterialRarity rarity,
            int value)
        {
            return new ShopItemDefinition(
                id, id, category, new object(), value, true, rarity);
        }

        private static IShopItemDefinition Supply(string id, int value)
        {
            return new ShopItemDefinition(
                id, id, ShopItemCategory.MaintenanceSupply, new object(), value);
        }

        private sealed class TestContext
        {
            public TestContext(
                ShopController controller,
                ShopItemCatalog catalog,
                FakeAccounts accounts)
            {
                Controller = controller;
                Catalog = catalog;
                Accounts = accounts;
            }

            public ShopController Controller { get; }
            public ShopItemCatalog Catalog { get; }
            public FakeAccounts Accounts { get; }
        }

        [Serializable]
        private sealed class WireSnapshotPayload
        {
            public string MatchId;
            public string RoundId;
            public long RotationVersion;
            public byte State;
            public long ClosesAtUnixMilliseconds;
            public string[] OfferIds;
            public string[] ItemIds;
            public byte[] Categories;
            public byte[] ListingKinds;
            public int[] PurchasePrices;
            public int[] RemainingStock;
            public bool[] UnlimitedStock;
        }

        private sealed class SequenceRandom : IShopRandomProvider
        {
            private readonly Queue<double> units;
            private readonly Queue<int> indexes;
            private readonly double fallbackUnit;

            public SequenceRandom(double[] units, int[] indexes = null)
            {
                this.units = new Queue<double>(units ?? Array.Empty<double>());
                this.indexes = new Queue<int>(indexes ?? Array.Empty<int>());
                fallbackUnit = units != null && units.Length > 0 ? units[units.Length - 1] : 0d;
            }

            public double NextUnit()
            {
                return units.Count > 0 ? units.Dequeue() : fallbackUnit;
            }

            public int NextIndex(int exclusiveMaximum)
            {
                int value = indexes.Count > 0 ? indexes.Dequeue() : 0;
                return value % exclusiveMaximum;
            }
        }

        private sealed class CountingRandom : IShopRandomProvider
        {
            private readonly double value;

            public CountingRandom(double value)
            {
                this.value = value;
            }

            public int UnitCalls { get; private set; }

            public double NextUnit()
            {
                UnitCalls++;
                return value;
            }

            public int NextIndex(int exclusiveMaximum)
            {
                return 0;
            }
        }

        private sealed class FixedStockPolicy : IShopStockPolicy
        {
            private readonly int quantity;

            public FixedStockPolicy(int quantity)
            {
                this.quantity = quantity;
            }

            public ShopStock CreateInitialStock(
                IShopItemDefinition item,
                ShopListingKind listingKind,
                int listingIndex)
            {
                return ShopStock.Limited(quantity);
            }
        }

        private sealed class FakeAccounts : IShopPlayerAccountProvider
        {
            private readonly Dictionary<string, FakeAccount> accounts =
                new Dictionary<string, FakeAccount>(StringComparer.Ordinal);

            public FakeAccount this[string playerId] => accounts[playerId];

            public void Add(string playerId, int balance)
            {
                accounts.Add(playerId, new FakeAccount(playerId, balance));
            }

            public bool TryGetAccount(string playerId, out IShopPlayerAccount account)
            {
                bool found = accounts.TryGetValue(playerId ?? string.Empty, out FakeAccount concrete);
                account = concrete;
                return found;
            }
        }

        private sealed class FakeAccount : IShopPlayerAccount
        {
            private readonly Dictionary<string, int> quantities =
                new Dictionary<string, int>(StringComparer.Ordinal);

            public FakeAccount(string playerId, int balance)
            {
                PlayerId = playerId;
                CurrencyBalance = balance;
            }

            public string PlayerId { get; }
            public int CurrencyBalance { get; private set; }
            public bool FailNextTransaction { get; set; }

            public int GetItemQuantity(string itemId)
            {
                return quantities.TryGetValue(itemId, out int value) ? value : 0;
            }

            public void SetQuantity(string itemId, int quantity)
            {
                quantities[itemId] = quantity;
            }

            public bool TryPurchase(
                IShopItemDefinition item,
                int quantity,
                int totalCost,
                out int resultingBalance,
                out int resultingItemQuantity)
            {
                if (FailNextTransaction)
                {
                    FailNextTransaction = false;
                    resultingBalance = CurrencyBalance;
                    resultingItemQuantity = GetItemQuantity(item.Id);
                    return false;
                }

                if (CurrencyBalance < totalCost)
                {
                    resultingBalance = CurrencyBalance;
                    resultingItemQuantity = GetItemQuantity(item.Id);
                    return false;
                }

                CurrencyBalance -= totalCost;
                resultingItemQuantity = GetItemQuantity(item.Id) + quantity;
                quantities[item.Id] = resultingItemQuantity;
                resultingBalance = CurrencyBalance;
                return true;
            }

            public bool TrySell(
                IShopItemDefinition item,
                int quantity,
                int payout,
                out int resultingBalance,
                out int resultingItemQuantity)
            {
                if (FailNextTransaction || GetItemQuantity(item.Id) < quantity)
                {
                    FailNextTransaction = false;
                    resultingBalance = CurrencyBalance;
                    resultingItemQuantity = GetItemQuantity(item.Id);
                    return false;
                }

                resultingItemQuantity = GetItemQuantity(item.Id) - quantity;
                quantities[item.Id] = resultingItemQuantity;
                CurrencyBalance += payout;
                resultingBalance = CurrencyBalance;
                return true;
            }
        }
    }
}
