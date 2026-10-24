using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ApirApir.PlayerProfiles;
using NUnit.Framework;

namespace ApirApir.Tests.PlayerProfiles
{
    public sealed class CloudPlayerProfileTests
    {
        [Test]
        public async Task FirstLogin_UsesAuthenticationIdAndRequestsName()
        {
            var identity = new FakeIdentity("auth-player-1");
            var service = new CloudPlayerProfileCoordinator(identity, new MemoryCloudStore());
            string requestedFor = null;
            service.ProfileNeedsName += playerId => requestedFor = playerId;

            CloudProfileCommandStatus result = await service.InitializeAsync();

            Assert.That(result, Is.EqualTo(CloudProfileCommandStatus.NeedsPlayerName));
            Assert.That(service.AuthenticatedPlayerId, Is.EqualTo("auth-player-1"));
            Assert.That(requestedFor, Is.EqualTo("auth-player-1"));
            Assert.That(service.CurrentProfile, Is.Null);
        }

        [Test]
        public async Task FirstProfile_NormalizesNameAndStartsAtZeroGames()
        {
            var service = CreateNewService("auth-player-1", new MemoryCloudStore());
            await service.InitializeAsync();

            CloudProfileCommandStatus result = await service.CreateProfileAsync("  Maria   Clara  ");

            Assert.That(result, Is.EqualTo(CloudProfileCommandStatus.Success));
            Assert.That(service.CurrentProfile.PlayerId, Is.EqualTo("auth-player-1"));
            Assert.That(service.CurrentProfile.PlayerName, Is.EqualTo("Maria Clara"));
            Assert.That(service.CurrentProfile.GamesPlayed, Is.Zero);
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("bad\u0001name")]
        [TestCase("1234567890123456789012345")]
        public async Task FirstProfile_RejectsInvalidNames(string requestedName)
        {
            var service = CreateNewService("auth-player-1", new MemoryCloudStore());
            await service.InitializeAsync();

            CloudProfileCommandStatus result = await service.CreateProfileAsync(requestedName);

            Assert.That(result, Is.EqualTo(CloudProfileCommandStatus.InvalidPlayerName));
            Assert.That(service.CurrentProfile, Is.Null);
        }

        [Test]
        public async Task ExistingProfile_LoadsWithoutRequestingAnotherName()
        {
            var store = new MemoryCloudStore();
            store.Seed(Record("auth-player-1", "Existing Player", 7, "old-match"));
            var service = CreateNewService("auth-player-1", store);
            int nameRequests = 0;
            service.ProfileNeedsName += _ => nameRequests++;

            CloudProfileCommandStatus result = await service.InitializeAsync();

            Assert.That(result, Is.EqualTo(CloudProfileCommandStatus.Success));
            Assert.That(nameRequests, Is.Zero);
            Assert.That(service.CurrentProfile.PlayerName, Is.EqualTo("Existing Player"));
            Assert.That(service.CurrentProfile.GamesPlayed, Is.EqualTo(7));
        }

        [Test]
        public async Task NamePersistsAcrossCoordinatorRestartAndCannotBeRenamed()
        {
            var store = new MemoryCloudStore();
            CloudPlayerProfileCoordinator first = CreateNewService("auth-player-1", store);
            await first.InitializeAsync();
            await first.CreateProfileAsync("Permanent Name");

            CloudPlayerProfileCoordinator second = CreateNewService("auth-player-1", store);
            await second.InitializeAsync();
            MethodInfo rename = typeof(CloudPlayerProfileCoordinator).GetMethods(
                    BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(method =>
                    method.Name.IndexOf("Rename", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    method.Name.IndexOf("UpdatePlayerName", StringComparison.OrdinalIgnoreCase) >= 0);

            Assert.That(second.CurrentProfile.PlayerName, Is.EqualTo("Permanent Name"));
            Assert.That(await second.CreateProfileAsync("Replacement"),
                Is.EqualTo(CloudProfileCommandStatus.ProfileAlreadyExists));
            Assert.That(rename, Is.Null);
        }

        [Test]
        public async Task CompletedFourPlayerMatch_IncrementsOnlyOnce()
        {
            var store = new MemoryCloudStore();
            CloudPlayerProfileCoordinator service = await CreateReadyService(store);
            CompletedMatchResult result = CompletedMatch("match-1", "auth-player-1");

            CloudProfileCommandStatus first = await service.ProcessCompletedMatchAsync(result);
            CloudProfileCommandStatus duplicate = await service.ProcessCompletedMatchAsync(result);

            Assert.That(first, Is.EqualTo(CloudProfileCommandStatus.Success));
            Assert.That(duplicate, Is.EqualTo(CloudProfileCommandStatus.DuplicateMatch));
            Assert.That(service.CurrentProfile.GamesPlayed, Is.EqualTo(1));
        }

        [Test]
        public async Task IncompleteOrNonParticipantResults_DoNotIncrementGames()
        {
            var store = new MemoryCloudStore();
            CloudPlayerProfileCoordinator service = await CreateReadyService(store);
            var incomplete = new CompletedMatchResult(
                "match-1",
                false,
                CompletedMatch("match-1", "auth-player-1").Placements);

            CloudProfileCommandStatus incompleteStatus =
                await service.ProcessCompletedMatchAsync(incomplete);
            CloudProfileCommandStatus absentStatus =
                await service.ProcessCompletedMatchAsync(CompletedMatch("match-2", "another-player"));

            Assert.That(incompleteStatus, Is.EqualTo(CloudProfileCommandStatus.InvalidMatch));
            Assert.That(absentStatus, Is.EqualTo(CloudProfileCommandStatus.PlayerNotInMatch));
            Assert.That(service.CurrentProfile.GamesPlayed, Is.Zero);
        }

        [Test]
        public async Task DuplicateMatchId_RemainsRejectedAfterRestart()
        {
            var store = new MemoryCloudStore();
            CloudPlayerProfileCoordinator first = await CreateReadyService(store);
            await first.ProcessCompletedMatchAsync(CompletedMatch("match-1", "auth-player-1"));
            var second = CreateNewService("auth-player-1", store);
            await second.InitializeAsync();

            CloudProfileCommandStatus duplicate = await second.ProcessCompletedMatchAsync(
                CompletedMatch("match-1", "auth-player-1"));

            Assert.That(duplicate, Is.EqualTo(CloudProfileCommandStatus.DuplicateMatch));
            Assert.That(second.CurrentProfile.GamesPlayed, Is.EqualTo(1));
        }

        [Test]
        public async Task SaveFailure_DoesNotMutateLoadedProfile()
        {
            var store = new MemoryCloudStore();
            CloudPlayerProfileCoordinator service = await CreateReadyService(store);
            store.FailNextSave = true;

            CloudProfileCommandStatus result = await service.ProcessCompletedMatchAsync(
                CompletedMatch("match-1", "auth-player-1"));

            Assert.That(result, Is.EqualTo(CloudProfileCommandStatus.PersistenceFailed));
            Assert.That(service.CurrentProfile.PlayerName, Is.EqualTo("Player One"));
            Assert.That(service.CurrentProfile.GamesPlayed, Is.Zero);
        }

        [Test]
        public async Task WriteConflict_ReloadsAndRetriesWithoutLosingRemoteState()
        {
            var store = new MemoryCloudStore();
            CloudPlayerProfileCoordinator service = await CreateReadyService(store);
            store.ReplaceOnNextConflict = Record(
                "auth-player-1",
                "Player One",
                2,
                "remote-match-1",
                "remote-match-2");

            CloudProfileCommandStatus result = await service.ProcessCompletedMatchAsync(
                CompletedMatch("local-match", "auth-player-1"));

            Assert.That(result, Is.EqualTo(CloudProfileCommandStatus.Success));
            Assert.That(service.CurrentProfile.GamesPlayed, Is.EqualTo(3));
            Assert.That(store.StoredRecord.ProcessedMatchIds,
                Is.EquivalentTo(new[] { "remote-match-1", "remote-match-2", "local-match" }));
        }

        [Test]
        public async Task RepeatedInitialization_DoesNotReloadOrCreateProfile()
        {
            var store = new MemoryCloudStore();
            CloudPlayerProfileCoordinator service = await CreateReadyService(store);
            int loadsAfterCreation = store.LoadCount;
            int savesAfterCreation = store.SaveCount;

            CloudProfileCommandStatus result = await service.InitializeAsync();

            Assert.That(result, Is.EqualTo(CloudProfileCommandStatus.Unchanged));
            Assert.That(store.LoadCount, Is.EqualTo(loadsAfterCreation));
            Assert.That(store.SaveCount, Is.EqualTo(savesAfterCreation));
        }

        [Test]
        public async Task AccountSwitch_ClearsOldProfileBeforeLoadingNewAccount()
        {
            var identity = new FakeIdentity("account-a");
            var store = new MemoryCloudStore();
            store.Seed(Record("account-a", "First Account", 1));
            store.Seed(Record("account-b", "Second Account", 4));
            var service = new CloudPlayerProfileCoordinator(identity, store);
            int cleared = 0;
            service.ProfileCleared += () => cleared++;
            await service.InitializeAsync();

            identity.PlayerId = "account-b";
            CloudProfileCommandStatus result = await service.InitializeAsync();

            Assert.That(result, Is.EqualTo(CloudProfileCommandStatus.Success));
            Assert.That(cleared, Is.EqualTo(1));
            Assert.That(service.CurrentProfile.PlayerId, Is.EqualTo("account-b"));
            Assert.That(service.CurrentProfile.PlayerName, Is.EqualTo("Second Account"));
            Assert.That(service.CurrentProfile.GamesPlayed, Is.EqualTo(4));
        }

        [Test]
        public async Task MultiplayerClientId_CannotSelectCloudProfileIdentity()
        {
            var store = new MemoryCloudStore();
            var identity = new FakeIdentity("unity-auth-id");
            var service = new CloudPlayerProfileCoordinator(identity, store);
            await service.InitializeAsync();
            await service.CreateProfileAsync("Player");

            Assert.That(service.CurrentProfile.PlayerId, Is.EqualTo("unity-auth-id"));
            Assert.That(typeof(CloudPlayerProfileCoordinator).GetMethods(
                    (BindingFlags.Public | BindingFlags.Instance))
                .Any(method => method.GetParameters().Any(parameter =>
                    parameter.Name != null &&
                    parameter.Name.IndexOf("clientId", StringComparison.OrdinalIgnoreCase) >= 0)),
                Is.False);
        }

        [Test]
        public async Task CorruptCloudProfile_IsNotReplacedWithNewData()
        {
            var store = new MemoryCloudStore();
            store.Seed(new CloudPlayerProfileRecord(
                CloudPlayerProfileRecord.CurrentSchemaVersion,
                "different-account",
                "Valid Name",
                5,
                Array.Empty<string>()), "auth-player-1");
            var service = CreateNewService("auth-player-1", store);

            CloudProfileCommandStatus result = await service.InitializeAsync();

            Assert.That(result, Is.EqualTo(CloudProfileCommandStatus.CorruptOrUnsupported));
            Assert.That(service.CurrentProfile, Is.Null);
            Assert.That(store.SaveCount, Is.Zero);
        }

        private static CloudPlayerProfileCoordinator CreateNewService(
            string playerId,
            MemoryCloudStore store)
        {
            return new CloudPlayerProfileCoordinator(new FakeIdentity(playerId), store);
        }

        private static async Task<CloudPlayerProfileCoordinator> CreateReadyService(
            MemoryCloudStore store)
        {
            CloudPlayerProfileCoordinator service = CreateNewService("auth-player-1", store);
            await service.InitializeAsync();
            await service.CreateProfileAsync("Player One");
            return service;
        }

        private static CloudPlayerProfileRecord Record(
            string playerId,
            string playerName,
            int gamesPlayed,
            params string[] matchIds)
        {
            return new CloudPlayerProfileRecord(
                CloudPlayerProfileRecord.CurrentSchemaVersion,
                playerId,
                playerName,
                gamesPlayed,
                matchIds);
        }

        private static CompletedMatchResult CompletedMatch(string matchId, string firstPlayerId)
        {
            return new CompletedMatchResult(matchId, true, new[]
            {
                new PlayerFinalPlacement(firstPlayerId, FinalMatchPlacement.First),
                new PlayerFinalPlacement("player-2", FinalMatchPlacement.Second),
                new PlayerFinalPlacement("player-3", FinalMatchPlacement.Third),
                new PlayerFinalPlacement("player-4", FinalMatchPlacement.Fourth)
            });
        }

        private sealed class FakeIdentity : ICloudPlayerIdentity
        {
            public FakeIdentity(string playerId)
            {
                PlayerId = playerId;
            }

            public string PlayerId { get; set; }
            public string SignedInPlayerId => PlayerId;

            public Task<CloudAuthenticationResult> EnsureAuthenticatedAsync(
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult(new CloudAuthenticationResult(
                    CloudAuthenticationStatus.Success,
                    PlayerId));
            }
        }

        private sealed class MemoryCloudStore : ICloudPlayerProfileStore
        {
            private readonly Dictionary<string, CloudPlayerProfileRecord> records =
                new Dictionary<string, CloudPlayerProfileRecord>(StringComparer.Ordinal);
            private readonly Dictionary<string, int> revisions =
                new Dictionary<string, int>(StringComparer.Ordinal);

            public bool FailNextSave { get; set; }
            public CloudPlayerProfileRecord ReplaceOnNextConflict { get; set; }
            public int LoadCount { get; private set; }
            public int SaveCount { get; private set; }
            public CloudPlayerProfileRecord StoredRecord =>
                records.TryGetValue("auth-player-1", out CloudPlayerProfileRecord value)
                    ? Copy(value)
                    : null;

            public void Seed(CloudPlayerProfileRecord record, string storagePlayerId = null)
            {
                string key = storagePlayerId ?? record.PlayerId;
                records[key] = Copy(record);
                revisions[key] = revisions.TryGetValue(key, out int revision) ? revision + 1 : 1;
            }

            public Task<CloudProfileStoreLoadResult> LoadAsync(
                string authenticatedPlayerId,
                CancellationToken cancellationToken = default)
            {
                LoadCount++;
                if (!records.TryGetValue(authenticatedPlayerId, out CloudPlayerProfileRecord record))
                {
                    return Task.FromResult(new CloudProfileStoreLoadResult(
                        CloudProfileStoreLoadStatus.NotFound));
                }

                return Task.FromResult(new CloudProfileStoreLoadResult(
                    CloudProfileStoreLoadStatus.Success,
                    Copy(record),
                    revisions[authenticatedPlayerId].ToString()));
            }

            public Task<CloudProfileStoreSaveResult> SaveAsync(
                CloudPlayerProfileRecord record,
                string expectedWriteLock,
                CancellationToken cancellationToken = default)
            {
                SaveCount++;
                if (FailNextSave)
                {
                    FailNextSave = false;
                    return Task.FromResult(new CloudProfileStoreSaveResult(
                        CloudProfileStoreSaveStatus.Unavailable));
                }

                if (ReplaceOnNextConflict != null)
                {
                    CloudPlayerProfileRecord replacement = ReplaceOnNextConflict;
                    ReplaceOnNextConflict = null;
                    Seed(replacement, record.PlayerId);
                    return Task.FromResult(new CloudProfileStoreSaveResult(
                        CloudProfileStoreSaveStatus.Conflict));
                }

                if (records.TryGetValue(record.PlayerId, out _) &&
                    !string.Equals(
                        expectedWriteLock,
                        revisions[record.PlayerId].ToString(),
                        StringComparison.Ordinal))
                {
                    return Task.FromResult(new CloudProfileStoreSaveResult(
                        CloudProfileStoreSaveStatus.Conflict));
                }

                records[record.PlayerId] = Copy(record);
                revisions[record.PlayerId] = revisions.TryGetValue(record.PlayerId, out int revision)
                    ? revision + 1
                    : 1;
                return Task.FromResult(new CloudProfileStoreSaveResult(
                    CloudProfileStoreSaveStatus.Success,
                    revisions[record.PlayerId].ToString()));
            }

            private static CloudPlayerProfileRecord Copy(CloudPlayerProfileRecord source)
            {
                return new CloudPlayerProfileRecord(
                    source.SchemaVersion,
                    source.PlayerId,
                    source.PlayerName,
                    source.GamesPlayed,
                    source.ProcessedMatchIds);
            }
        }
    }
}
