using System;
using System.Collections.Generic;
using System.Linq;
using ApirApir.Battle;
using ApirApir.Cards;
using ApirApir.Crafting;
using NUnit.Framework;
using UnityEngine;

namespace ApirApir.AugmentedReality.Tests
{
    public sealed class AugmentedRealitySystemTests
    {
        private const string OwnerA = "persistent-player-a";
        private const string OwnerB = "persistent-player-b";

        [Test]
        public void CreatingCards_AssignsUniqueGlobalIds()
        {
            TestContext context = CreateContext();
            RegisteredARCard first = Register(context, OwnerA, "request-1", "assembled-1").Card;
            RegisteredARCard second = Register(context, OwnerA, "request-2", "assembled-2").Card;

            Assert.That(first.CardId, Is.Not.EqualTo(second.CardId));
        }

        [Test]
        public void CardNumbers_AreSequentialPerCreator()
        {
            TestContext context = CreateContext();

            Assert.That(Register(context, OwnerA, "request-1", "assembled-1").Card.CardNumber, Is.EqualTo(1));
            Assert.That(Register(context, OwnerA, "request-2", "assembled-2").Card.CardNumber, Is.EqualTo(2));
            Assert.That(Register(context, OwnerA, "request-3", "assembled-3").Card.CardNumber, Is.EqualTo(3));
        }

        [Test]
        public void MatchingCardNumbers_FromDifferentCreatorsDoNotConflict()
        {
            TestContext context = CreateContext();
            RegisteredARCard first = Register(context, OwnerA, "request-a", "assembled-a").Card;
            RegisteredARCard second = Register(context, OwnerB, "request-b", "assembled-b").Card;

            Assert.That(first.CardNumber, Is.EqualTo(1));
            Assert.That(second.CardNumber, Is.EqualTo(1));
            Assert.That(first.CardId, Is.Not.EqualTo(second.CardId));
        }

        [Test]
        public void Registration_UsesExistingAssembledCardProvider()
        {
            TestContext context = CreateContext();
            ARCardOperationResult result = Register(context, OwnerA, "request", "assembled-a");

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Card.SourceAssembledCardId, Is.EqualTo("assembled-a"));
            Assert.That(context.Snapshots.LookupCount, Is.EqualTo(1));
        }

        [Test]
        public void Registration_PreservesHeadBodyAndLegs()
        {
            TestContext context = CreateContext();
            ARCardConfigurationSnapshot snapshot = Register(
                context, OwnerA, "request", "assembled-a").Card.Configuration;

            Assert.That(snapshot.HeadDefinitionId, Is.EqualTo("bakunawa-head"));
            Assert.That(snapshot.BodyDefinitionId, Is.EqualTo("magsasaka-body"));
            Assert.That(snapshot.LegsDefinitionId, Is.EqualTo("pulis-legs"));
            Assert.That(snapshot.HeadVisualAssetId, Is.EqualTo("visual/bakunawa-head"));
        }

        [Test]
        public void PrintableData_GeneratesCompactValidQrPayload()
        {
            TestContext context = CreateContext();
            RegisteredARCard card = Register(context, OwnerA, "request", "assembled-a").Card;

            ARCardOperationResult result = context.Service.TryCreatePrintableData(
                card.CardId, out PrintableCardData printable);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(printable.QrPayload, Is.EqualTo("APIRAR:1:" + card.CardId));
            Assert.That(printable.QrPayload, Does.Not.Contain(OwnerA));
        }

        [Test]
        public void QrPayload_ParsesCardReference()
        {
            var qr = new QRCodeService();
            string id = Id(42);

            Assert.That(qr.TryParsePayload(qr.CreatePayload(id), out string parsed), Is.True);
            Assert.That(parsed, Is.EqualTo(id));
        }

        [TestCase("")]
        [TestCase("APIRAR:1:")]
        [TestCase("APIRAR:2:00000000000000000000000000000001")]
        [TestCase("APIRAR:1:not-a-card-id")]
        [TestCase("https://example.invalid/card/1")]
        public void QrPayload_RejectsMalformedInput(string payload)
        {
            Assert.That(new QRCodeService().TryParsePayload(payload, out _), Is.False);
        }

        [Test]
        public void Identification_RejectsUnknownCardId()
        {
            TestContext context = CreateContext();
            var scanner = new MockQRCodeScanner();
            using (var identification = new ARCardIdentificationService(scanner, context.Service))
            {
                ARCardFailure failure = ARCardFailure.None;
                identification.CardIdentificationFailed += (_, value) => failure = value;
                Assert.That(identification.Start(out _), Is.True);

                scanner.SubmitDecodedPayload(new QRCodeService().CreatePayload(Id(99)));

                Assert.That(failure, Is.EqualTo(ARCardFailure.CardNotFound));
            }
        }

        [Test]
        public void AnotherPlayer_CanRetrieveOwnersActiveCard()
        {
            TestContext context = CreateContext();
            RegisteredARCard card = Register(context, OwnerA, "request", "assembled-a").Card;

            ARCardOperationResult viewedByB = context.Service.Validate(card.CardId);

            Assert.That(viewedByB.Succeeded, Is.True);
            Assert.That(viewedByB.Card.OwnerPlayerId, Is.EqualTo(OwnerA));
        }

        [Test]
        public void ViewingCard_DoesNotGrantOwnership()
        {
            TestContext context = CreateContext();
            RegisteredARCard card = Register(context, OwnerA, "request", "assembled-a").Card;

            context.Service.Validate(card.CardId);

            Assert.That(context.Service.CheckOwnership(card.CardId, OwnerB), Is.False);
            Assert.That(context.Service.CheckOwnership(card.CardId, OwnerA), Is.True);
        }

        [Test]
        public void Revocation_RejectsNonOwner()
        {
            TestContext context = CreateContext();
            RegisteredARCard card = Register(context, OwnerA, "request", "assembled-a").Card;

            ARCardOperationResult result = context.Service.Revoke(OwnerB, card.CardId);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure, Is.EqualTo(ARCardFailure.Unauthorized));
            Assert.That(context.Service.Validate(card.CardId).Succeeded, Is.True);
        }

        [Test]
        public void Revocation_AllowsOwnerAndCreatesTombstone()
        {
            TestContext context = CreateContext();
            RegisteredARCard card = Register(context, OwnerA, "request", "assembled-a").Card;

            ARCardOperationResult result = context.Service.Revoke(OwnerA, card.CardId);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Card.Status, Is.EqualTo(ARCardStatus.Revoked));
            Assert.That(context.Service.GetCard(card.CardId).Card.Status, Is.EqualTo(ARCardStatus.Revoked));
        }

        [Test]
        public void RepeatedRevocation_IsIdempotentlyRejected()
        {
            TestContext context = CreateContext();
            RegisteredARCard card = Register(context, OwnerA, "request", "assembled-a").Card;
            context.Service.Revoke(OwnerA, card.CardId);

            ARCardOperationResult repeated = context.Service.Revoke(OwnerA, card.CardId);

            Assert.That(repeated.Succeeded, Is.False);
            Assert.That(repeated.Failure, Is.EqualTo(ARCardFailure.AlreadyRevoked));
        }

        [Test]
        public void RevokedCard_FailsAuthoritativeValidation()
        {
            TestContext context = CreateContext();
            RegisteredARCard card = Register(context, OwnerA, "request", "assembled-a").Card;
            context.Service.Revoke(OwnerA, card.CardId);

            ARCardOperationResult validation = context.Service.Validate(card.CardId);

            Assert.That(validation.Succeeded, Is.False);
            Assert.That(validation.Failure, Is.EqualTo(ARCardFailure.CardRevoked));
        }

        [Test]
        public void RevokedCardId_IsNeverReused()
        {
            TestContext context = CreateContext(ids: new[] { Id(1), Id(2) });
            RegisteredARCard first = Register(context, OwnerA, "request-1", "assembled-1").Card;
            context.Service.Revoke(OwnerA, first.CardId);

            RegisteredARCard second = Register(context, OwnerA, "request-2", "assembled-2").Card;

            Assert.That(second.CardId, Is.Not.EqualTo(first.CardId));
            Assert.That(second.CardNumber, Is.EqualTo(2));
        }

        [Test]
        public void ActiveCardLimit_IsConfigurableAndRevocationFreesSlot()
        {
            TestContext context = CreateContext(maximumActiveCards: 1);
            RegisteredARCard first = Register(context, OwnerA, "request-1", "assembled-1").Card;

            Assert.That(Register(context, OwnerA, "request-2", "assembled-2").Failure,
                Is.EqualTo(ARCardFailure.ActiveCardLimitReached));
            context.Service.Revoke(OwnerA, first.CardId);
            Assert.That(Register(context, OwnerA, "request-2", "assembled-2").Succeeded, Is.True);
        }

        [Test]
        public void DuplicateRegistrationRequest_ReturnsSameRecordWithoutSecondEvent()
        {
            TestContext context = CreateContext();
            int events = 0;
            context.Service.CardRegistered += _ => events++;

            ARCardOperationResult first = Register(context, OwnerA, "same-request", "assembled-a");
            ARCardOperationResult duplicate = Register(context, OwnerA, "same-request", "assembled-a");

            Assert.That(duplicate.Card.CardId, Is.EqualTo(first.Card.CardId));
            Assert.That(duplicate.WasDuplicateRequest, Is.True);
            Assert.That(context.Service.GetCardsByOwner(OwnerA).Count, Is.EqualTo(1));
            Assert.That(events, Is.EqualTo(1));
        }

        [Test]
        public void RepeatedTrackingDetection_DoesNotCreateDuplicateVisual()
        {
            TestContext context = CreateContext();
            RegisteredARCard card = Register(context, OwnerA, "request", "assembled-a").Card;
            var resolver = new FakeAssetResolver();
            using (var visualizer = new ARCardVisualizer(context.Service, resolver))
            {
                ARTrackedPose pose = new ARTrackedPose(Vector3.one, Quaternion.identity);
                Assert.That(visualizer.HandleTrackingFound(card, pose), Is.True);
                Assert.That(visualizer.HandleTrackingFound(card, pose), Is.True);

                Assert.That(resolver.CreateCount, Is.EqualTo(1));
                Assert.That(visualizer.ActiveVisualCount, Is.EqualTo(1));
            }
        }

        [Test]
        public void TrackingLossAndRecovery_HidesThenReusesVisual()
        {
            TestContext context = CreateContext();
            RegisteredARCard card = Register(context, OwnerA, "request", "assembled-a", "target-a").Card;
            var provider = new MockARImageTrackingProvider();
            var tracking = new ARImageTrackingService(provider);
            var resolver = new FakeAssetResolver();
            using (var visualizer = new ARCardVisualizer(context.Service, resolver))
            {
                tracking.CardTrackingFound += (found, pose) => visualizer.HandleTrackingFound(found, pose);
                tracking.CardPoseUpdated += visualizer.UpdateTrackedPose;
                tracking.CardTrackingLost += visualizer.HandleTrackingLost;
                Assert.That(tracking.TryTrackCard(card), Is.True);
                Assert.That(tracking.Start(out _), Is.True);
                ARTrackedPose pose = new ARTrackedPose(new Vector3(1, 2, 3), Quaternion.identity);

                provider.Submit(new ARTrackedImageObservation("target-a", ARImageTrackingState.Found, pose));
                provider.Submit(new ARTrackedImageObservation("target-a", ARImageTrackingState.Lost, pose));
                provider.Submit(new ARTrackedImageObservation("target-a", ARImageTrackingState.Tracking, pose));

                Assert.That(resolver.CreateCount, Is.EqualTo(1));
                Assert.That(resolver.LastVisual.HideCount, Is.EqualTo(1));
                Assert.That(resolver.LastVisual.AcquiredCount, Is.EqualTo(2));
            }

            tracking.Dispose();
        }

        [Test]
        public void AssetResolver_ReceivesExactRegisteredPartReferences()
        {
            TestContext context = CreateContext();
            RegisteredARCard card = Register(context, OwnerA, "request", "assembled-a").Card;
            var resolver = new FakeAssetResolver();
            using (var visualizer = new ARCardVisualizer(context.Service, resolver))
            {
                visualizer.HandleTrackingFound(card, new ARTrackedPose(Vector3.zero, Quaternion.identity));

                Assert.That(resolver.LastResolved.Configuration.HeadVisualAssetId,
                    Is.EqualTo("visual/bakunawa-head"));
                Assert.That(resolver.LastResolved.Configuration.BodyVisualAssetId,
                    Is.EqualTo("visual/magsasaka-body"));
                Assert.That(resolver.LastResolved.Configuration.LegsVisualAssetId,
                    Is.EqualTo("visual/pulis-legs"));
            }
        }

        [Test]
        public void ConfigurableAssetResolver_MapsLayeredAndOptional3DReferences()
        {
            TestContext context = CreateContext();
            RegisteredARCard card = Register(context, OwnerA, "request", "assembled-a").Card;
            var factory = new FakeVisualFactory();
            var resolver = new ConfigurableARCardAssetResolver(factory);
            resolver.Register(new ARVisualAssetReference(
                "visual/bakunawa-head", "sprites/bakunawa-head", ARVisualAssetKind.Layered2D));
            resolver.Register(new ARVisualAssetReference(
                "visual/magsasaka-body", "sprites/magsasaka-body", ARVisualAssetKind.Layered2D));
            resolver.Register(new ARVisualAssetReference(
                "visual/pulis-legs", "prefabs/pulis-legs", ARVisualAssetKind.Prefab3D));

            Assert.That(resolver.TryCreateVisual(card, out IARCardVisualInstance visual), Is.True);
            Assert.That(factory.Head.ContentKey, Is.EqualTo("sprites/bakunawa-head"));
            Assert.That(factory.Body.Kind, Is.EqualTo(ARVisualAssetKind.Layered2D));
            Assert.That(factory.Legs.Kind, Is.EqualTo(ARVisualAssetKind.Prefab3D));
            visual.Dispose();
        }

        [Test]
        public void MissingVisualAssets_FailsSafelyWithoutInstance()
        {
            TestContext context = CreateContext();
            RegisteredARCard card = Register(context, OwnerA, "request", "assembled-a").Card;
            var resolver = new FakeAssetResolver { IsAvailable = false };
            using (var visualizer = new ARCardVisualizer(context.Service, resolver))
            {
                bool failed = false;
                visualizer.ARCharacterCreationFailed += _ => failed = true;

                Assert.That(visualizer.HandleTrackingFound(
                    card, new ARTrackedPose(Vector3.zero, Quaternion.identity)), Is.False);
                Assert.That(failed, Is.True);
                Assert.That(visualizer.ActiveVisualCount, Is.Zero);
            }
        }

        [Test]
        public void RegistryOutage_DoesNotUseCachedCardAsActive()
        {
            TestContext context = CreateContext();
            RegisteredARCard card = Register(context, OwnerA, "request", "assembled-a").Card;
            context.Registry.IsAvailable = false;
            var resolver = new FakeAssetResolver();
            using (var visualizer = new ARCardVisualizer(context.Service, resolver))
            {
                Assert.That(context.Service.Validate(card.CardId).Failure,
                    Is.EqualTo(ARCardFailure.RegistryUnavailable));
                Assert.That(visualizer.HandleTrackingFound(
                    card, new ARTrackedPose(Vector3.zero, Quaternion.identity)), Is.False);
                Assert.That(resolver.CreateCount, Is.Zero);
            }
        }

        [Test]
        public void RegisteredSnapshot_ContainsNoCombatOrInventoryState()
        {
            string[] propertyNames = typeof(ARCardConfigurationSnapshot)
                .GetProperties()
                .Select(property => property.Name)
                .ToArray();

            Assert.That(propertyNames, Does.Not.Contain("HitPoints"));
            Assert.That(propertyNames, Does.Not.Contain("Cooldowns"));
            Assert.That(propertyNames, Does.Not.Contain("Maintenance"));
            Assert.That(propertyNames, Does.Not.Contain("Inventory"));
        }

        [Test]
        public void Revocation_RemovesActivelyDisplayedVisual()
        {
            TestContext context = CreateContext();
            RegisteredARCard card = Register(context, OwnerA, "request", "assembled-a").Card;
            var resolver = new FakeAssetResolver();
            using (var visualizer = new ARCardVisualizer(context.Service, resolver))
            {
                visualizer.HandleTrackingFound(card, new ARTrackedPose(Vector3.zero, Quaternion.identity));
                context.Service.Revoke(OwnerA, card.CardId);

                Assert.That(visualizer.ActiveVisualCount, Is.Zero);
                Assert.That(resolver.LastVisual.Disposed, Is.True);
            }
        }

        [Test]
        public void MockLifecycle_HandlesInitializePauseResumeAndShutdown()
        {
            var platform = new MockARPlatformSession();
            using (var session = new ARSessionController(platform))
            {
                Assert.That(session.Start(), Is.True);
                Assert.That(session.State, Is.EqualTo(ARSessionState.Running));
                session.Pause();
                Assert.That(session.State, Is.EqualTo(ARSessionState.Paused));
                session.Resume();
                Assert.That(session.State, Is.EqualTo(ARSessionState.Running));
                session.Stop();
                Assert.That(session.State, Is.EqualTo(ARSessionState.Stopped));
            }
        }

        [Test]
        public void CameraPermissionDenied_FailsInitializationHonestly()
        {
            var platform = new MockARPlatformSession(
                ARAvailability.Available, CameraPermissionState.Denied);
            using (var session = new ARSessionController(platform))
            {
                string failure = null;
                session.ARInitializationFailed += reason => failure = reason;

                Assert.That(session.Initialize(), Is.False);
                Assert.That(failure, Does.Contain("permission"));
            }
        }

        [Test]
        public void AsynchronousPlatformInitialization_WaitsForProviderCompletion()
        {
            var platform = new FakeAsynchronousPlatformSession();
            using (var session = new ARSessionController(platform))
            {
                int initializedEvents = 0;
                session.ARInitialized += () => initializedEvents++;

                Assert.That(session.Initialize(), Is.True);
                Assert.That(session.State, Is.EqualTo(ARSessionState.Initializing));
                Assert.That(session.Start(), Is.False);

                platform.Complete(true, string.Empty);

                Assert.That(session.State, Is.EqualTo(ARSessionState.Initialized));
                Assert.That(initializedEvents, Is.EqualTo(1));
                Assert.That(session.Start(), Is.True);
                Assert.That(session.State, Is.EqualTo(ARSessionState.Running));
            }
        }

        [Test]
        public void ARFoundationDependencyContract_UsesOfficialUnityPackages()
        {
            Assert.That(ARFoundationIntegrationStatus.ARFoundationPackage,
                Is.EqualTo("com.unity.xr.arfoundation"));
            Assert.That(ARFoundationIntegrationStatus.XRPluginManagementPackage,
                Is.EqualTo("com.unity.xr.management"));
            Assert.That(ARFoundationIntegrationStatus.AndroidProviderPackage,
                Is.EqualTo("com.unity.xr.arcore"));
        }

        [Test]
        public void CardToARRecord_PreservesHybridRegionsAndExactAuthoritativeStats()
        {
            CraftingCatalog crafting = DefaultCraftingCatalog.CreateAvailableAssetCatalog();
            CraftablePartDefinition hybrid = crafting.Parts["part_epic_bakunawa_body_legs"];
            var bodyAndLegs = new OwnedCardPart("hybrid-instance", OwnerA, hybrid);
            var expectedStats = new CardEffectiveStats(
                275,
                44,
                12.5f,
                new Dictionary<string, float>(StringComparer.Ordinal) { ["guard"] = 3f },
                new[] { "passive_bakunawa" },
                8f,
                6f);
            var snapshot = new CardBuildSnapshot(
                OwnerA,
                "assembled-hybrid",
                7,
                false,
                new CardPartSlotSnapshot(PartSlot.Head, null),
                new CardPartSlotSnapshot(PartSlot.Body, bodyAndLegs),
                new CardPartSlotSnapshot(PartSlot.Legs, null),
                expectedStats);
            var factory = new ARCardRecordFactory(crafting, DefaultSkillCatalog.Create());

            Assert.That(factory.TryCreate(snapshot, OwnerA, "Bakunawa", Array.Empty<string>(),
                out ARCardRecord record, out string error), Is.True, error);
            Assert.That(record.body.definitionId, Is.EqualTo(hybrid.Id));
            Assert.That(record.body.occupiedRegions,
                Is.EqualTo((int)(PartEquipmentRegion.Body | PartEquipmentRegion.Legs)));
            Assert.That(record.legs, Is.Null);
            Assert.That(record.statistics.maximumHitPoints, Is.EqualTo(expectedStats.MaximumHitPoints));
            Assert.That(record.statistics.effectiveAttack, Is.EqualTo(expectedStats.EffectiveAttack));
            Assert.That(record.statistics.modifierIds, Is.EqualTo(new[] { "guard" }));
            Assert.That(record.statistics.modifierValues, Is.EqualTo(new[] { 3f }));
        }

        [Test]
        public void CardToARQrTexture_RoundTripsOnlyTheCardReference()
        {
            string expectedCardId = Id(77);
            var codec = new ZXingQRCodeCodec();
            Texture2D texture = codec.CreateCardIdTexture(expectedCardId, 256);
            try
            {
                Assert.That(codec.TryDecode(
                    texture.GetPixels32(), texture.width, texture.height, out string decodedCardId),
                    Is.True);
                Assert.That(decodedCardId, Is.EqualTo(expectedCardId));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void StandaloneCatalog_ContainsOnlyArtworkBackedSupportedParts()
        {
            StandaloneARCardCatalog catalog = StandaloneARCardCatalog.CreateDefault();

            Assert.That(catalog.Heads.Select(part => part.Id), Is.EquivalentTo(new[]
            {
                "part_common_magsasaka_head",
                "part_rare_abogado_head",
                "part_rare_pari_head",
                "part_epic_bakunawa_head"
            }));
            Assert.That(catalog.Bodies.Select(part => part.Id), Is.EquivalentTo(new[]
            {
                "part_common_magsasaka_body",
                "part_rare_abogado_body",
                "part_rare_pari_body",
                "part_epic_bakunawa_body_legs"
            }));
            Assert.That(catalog.Legs.Select(part => part.Id), Is.EquivalentTo(new[]
            {
                "part_common_magsasaka_legs",
                "part_rare_abogado_legs",
                "part_rare_pari_legs"
            }));
        }

        [Test]
        public void StandaloneComposition_RejectsHybridWithSeparateLegs()
        {
            StandaloneARCardCatalog catalog = StandaloneARCardCatalog.CreateDefault();

            bool succeeded = catalog.TryCompose(
                Id(91), 1, null, "part_epic_bakunawa_body_legs",
                "part_common_magsasaka_legs", out _, out string error);

            Assert.That(succeeded, Is.False);
            Assert.That(error, Does.Contain("overlap"));
        }

        [Test]
        public void StandaloneComposition_CountsBakunawaHybridExactlyOnce()
        {
            StandaloneARCardCatalog catalog = StandaloneARCardCatalog.CreateDefault();

            Assert.That(catalog.TryCompose(
                Id(92), 3, "part_epic_bakunawa_head", "part_epic_bakunawa_body_legs",
                null, out StandaloneARCardComposition composition, out string error), Is.True, error);

            Assert.That(composition.Legs, Is.Null);
            Assert.That(composition.Body.OccupiedRegions,
                Is.EqualTo(PartEquipmentRegion.Body | PartEquipmentRegion.Legs));
            Assert.That(composition.Statistics.MaximumHitPoints, Is.EqualTo(285));
            Assert.That(composition.Statistics.EffectiveAttack, Is.EqualTo(22));
            Assert.That(composition.Statistics.AttackBonusPercent, Is.EqualTo(124f));
            Assert.That(composition.Statistics.SpeedBonusPercent, Is.EqualTo(15f));
            Assert.That(composition.Statistics.CriticalChanceBonusPercent, Is.EqualTo(10f));
            Assert.That(composition.Statistics.ActivePassiveReferences.Count, Is.EqualTo(4));
        }

        [Test]
        public void StandaloneRecord_UsesAuthenticatedOwnerAndSurvivesSerialization()
        {
            CraftingCatalog definitions = DefaultCraftingCatalog.CreateAvailableAssetCatalog();
            var catalog = new StandaloneARCardCatalog(definitions);
            Assert.That(catalog.TryCompose(
                Id(93), 4, "part_common_magsasaka_head", "part_rare_abogado_body",
                "part_common_magsasaka_legs", out StandaloneARCardComposition composition,
                out string compositionError), Is.True, compositionError);
            var factory = new ARCardRecordFactory(definitions, DefaultSkillCatalog.Create());

            Assert.That(factory.TryCreateStandalone(
                composition, OwnerA, "Standalone Test", new[] { DefaultSkillCatalog.BasicAttackId },
                out ARCardRecord record, out string error), Is.True, error);

            Assert.That(record.ownerPlayerId, Is.EqualTo(OwnerA));
            Assert.That(record.sourceAssembledCardId, Is.EqualTo("standalone-" + Id(93)));
            Assert.That(record.cardId, Has.Length.EqualTo(32));
            Assert.That(record.selectedSkillIds, Is.EqualTo(new[] { DefaultSkillCatalog.BasicAttackId }));
            string json = JsonUtility.ToJson(record, false);
            ARCardRecord restored = JsonUtility.FromJson<ARCardRecord>(json);
            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.IsValidBasicShape(), Is.True);
            Assert.That(restored.cardName, Is.EqualTo("Standalone Test"));
            Assert.That(restored.statistics.maximumHitPoints,
                Is.EqualTo(record.statistics.maximumHitPoints));
        }

        private static TestContext CreateContext(int maximumActiveCards = 10, string[] ids = null)
        {
            var snapshots = new FakeSnapshotProvider();
            snapshots.Add(OwnerA, "assembled-a", Configuration());
            snapshots.Add(OwnerA, "assembled-1", Configuration());
            snapshots.Add(OwnerA, "assembled-2", Configuration());
            snapshots.Add(OwnerA, "assembled-3", Configuration());
            snapshots.Add(OwnerB, "assembled-b", Configuration());
            var tags = new FakeTagProvider();
            tags.Add(OwnerA, "Player A#persistent-player-a");
            tags.Add(OwnerB, "Player B#persistent-player-b");
            var generator = new QueueIdGenerator(ids ?? Enumerable.Range(1, 32).Select(Id));
            var registry = new DevelopmentInMemoryARCardRegistry(generator);
            var service = new CardRegistrationService(
                registry, snapshots, tags, maximumActiveCards, clock: new FixedClock());
            return new TestContext(service, registry, snapshots);
        }

        private static ARCardOperationResult Register(
            TestContext context,
            string owner,
            string requestId,
            string assembledCardId,
            string trackingTargetId = "target")
        {
            return context.Service.Register(
                owner, new ARCardRegistrationRequest(requestId, assembledCardId, trackingTargetId));
        }

        private static ARCardConfigurationSnapshot Configuration()
        {
            return new ARCardConfigurationSnapshot(
                "bakunawa-head", "visual/bakunawa-head",
                "magsasaka-body", "visual/magsasaka-body",
                "pulis-legs", "visual/pulis-legs");
        }

        private static string Id(int value)
        {
            return value.ToString("x32");
        }

        private sealed class TestContext
        {
            public TestContext(
                CardRegistrationService service,
                DevelopmentInMemoryARCardRegistry registry,
                FakeSnapshotProvider snapshots)
            {
                Service = service;
                Registry = registry;
                Snapshots = snapshots;
            }

            public CardRegistrationService Service { get; }
            public DevelopmentInMemoryARCardRegistry Registry { get; }
            public FakeSnapshotProvider Snapshots { get; }
        }

        private sealed class FakeSnapshotProvider : IAssembledCardSnapshotProvider
        {
            private readonly Dictionary<string, ARCardConfigurationSnapshot> values =
                new Dictionary<string, ARCardConfigurationSnapshot>(StringComparer.Ordinal);
            public int LookupCount { get; private set; }

            public void Add(string ownerId, string cardId, ARCardConfigurationSnapshot snapshot)
            {
                values[ownerId + "|" + cardId] = snapshot;
            }

            public bool TryGetSnapshot(
                string authenticatedOwnerPlayerId,
                string assembledCardId,
                out ARCardConfigurationSnapshot configuration)
            {
                LookupCount++;
                return values.TryGetValue(authenticatedOwnerPlayerId + "|" + assembledCardId, out configuration);
            }
        }

        private sealed class FakeTagProvider : IPlayerTagProvider
        {
            private readonly Dictionary<string, string> values =
                new Dictionary<string, string>(StringComparer.Ordinal);

            public void Add(string playerId, string tag) { values[playerId] = tag; }
            public bool TryGetPlayerTag(string playerId, out string playerTag)
            {
                return values.TryGetValue(playerId, out playerTag);
            }
        }

        private sealed class QueueIdGenerator : IARCardIdGenerator
        {
            private readonly Queue<string> values;
            public QueueIdGenerator(IEnumerable<string> values) { this.values = new Queue<string>(values); }
            public string CreateCardId() { return values.Dequeue(); }
        }

        private sealed class FixedClock : IARClock
        {
            public long UtcNowUnixMilliseconds => 123456789L;
        }

        private sealed class FakeAssetResolver : IARCardAssetResolver
        {
            public bool IsAvailable { get; set; } = true;
            public int CreateCount { get; private set; }
            public RegisteredARCard LastResolved { get; private set; }
            public FakeVisual LastVisual { get; private set; }

            public bool TryCreateVisual(RegisteredARCard card, out IARCardVisualInstance visual)
            {
                LastResolved = card;
                if (!IsAvailable)
                {
                    visual = null;
                    return false;
                }

                CreateCount++;
                LastVisual = new FakeVisual(card.CardId);
                visual = LastVisual;
                return true;
            }
        }

        private sealed class FakeVisual : IARCardVisualInstance
        {
            public FakeVisual(string cardId) { CardId = cardId; }
            public string CardId { get; }
            public int HideCount { get; private set; }
            public int AcquiredCount { get; private set; }
            public bool Disposed { get; private set; }
            public void ApplyPose(Vector3 position, Quaternion rotation, float scale) { }
            public void Show() { }
            public void Hide() { HideCount++; }
            public void OnTrackingAcquired() { AcquiredCount++; }
            public void OnTrackingLost() { }
            public void Dispose() { Disposed = true; }
        }

        private sealed class FakeVisualFactory : IARCardVisualFactory
        {
            public ARVisualAssetReference Head { get; private set; }
            public ARVisualAssetReference Body { get; private set; }
            public ARVisualAssetReference Legs { get; private set; }

            public bool TryCreate(
                RegisteredARCard card,
                ARVisualAssetReference head,
                ARVisualAssetReference body,
                ARVisualAssetReference legs,
                out IARCardVisualInstance visual)
            {
                Head = head;
                Body = body;
                Legs = legs;
                visual = new FakeVisual(card.CardId);
                return true;
            }
        }

        private sealed class FakeAsynchronousPlatformSession :
            IARPlatformSession,
            IARAsynchronousPlatformSession
        {
            private Action<bool, string> completion;

            public ARAvailability Availability { get; private set; } = ARAvailability.Unknown;
            public CameraPermissionState CameraPermission { get; private set; } =
                CameraPermissionState.Unknown;

            public void BeginInitialize(Action<bool, string> callback)
            {
                completion = callback;
            }

            public void Complete(bool succeeded, string failureReason)
            {
                Availability = succeeded ? ARAvailability.Available : ARAvailability.Unavailable;
                CameraPermission = succeeded
                    ? CameraPermissionState.Granted
                    : CameraPermissionState.Denied;
                Action<bool, string> callback = completion;
                completion = null;
                callback?.Invoke(succeeded, failureReason);
            }

            public bool TryInitialize(out string failureReason)
            {
                failureReason = string.Empty;
                return true;
            }

            public bool TryStart(out string failureReason)
            {
                failureReason = string.Empty;
                return true;
            }

            public void Pause() { }
            public void Resume() { }
            public void Stop() { }
            public void Dispose() { completion = null; }
        }
    }
}
