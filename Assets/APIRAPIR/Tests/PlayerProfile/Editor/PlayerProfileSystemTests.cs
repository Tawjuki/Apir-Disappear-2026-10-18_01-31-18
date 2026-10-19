using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ApirApir.PlayerProfiles;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

namespace ApirApir.Tests.PlayerProfiles
{
    public sealed class PlayerProfileSystemTests
    {
        private readonly List<string> temporaryDirectories = new List<string>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < temporaryDirectories.Count; i++)
            {
                if (Directory.Exists(temporaryDirectories[i]))
                {
                    Directory.Delete(temporaryDirectories[i], true);
                }
            }

            temporaryDirectories.Clear();
        }

        [Test]
        public void NewProfileCreation_StoresExactlyTheRequestedPrimaryValues()
        {
            PlayerProfileService service = CreateService(new MemoryRepository());

            ProfileOperationStatus status = service.TryCreateProfile(
                "  Ada   Lovelace  ", "child_a", out PlayerProfileSnapshot profile);

            Assert.That(status, Is.EqualTo(ProfileOperationStatus.Success));
            Assert.That(profile.PlayerId, Is.Not.Empty);
            Assert.That(profile.PlayerName, Is.EqualTo("Ada Lovelace"));
            Assert.That(profile.SelectedCharacterId, Is.EqualTo("child_a"));
            Assert.That(profile.GamesPlayed, Is.Zero);
            Assert.That(profile.GamesWon, Is.Zero);
            Assert.That(profile.ThirdPlaceFinishes, Is.Zero);
        }

        [Test]
        public void GeneratedPlayerId_RemainsStableAfterReload()
        {
            var repository = new MemoryRepository();
            PlayerProfileService firstSession = CreateService(repository);
            firstSession.TryCreateProfile("Player", "child_a", out PlayerProfileSnapshot created);

            PlayerProfileService secondSession = CreateService(repository);
            Assert.That(secondSession.LoadProfiles(), Is.EqualTo(ProfileRepositoryLoadStatus.Success));
            Assert.That(secondSession.TryGetProfile(created.PlayerId, out PlayerProfileSnapshot loaded), Is.True);
            Assert.That(loaded.PlayerId, Is.EqualTo(created.PlayerId));
        }

        [Test]
        public void ExistingProfileLoading_RaisesLoadedEvent()
        {
            var repository = new MemoryRepository();
            CreateService(repository).TryCreateProfile("Player", "child_a", out _, "account-42");
            PlayerProfileService service = CreateService(repository);
            PlayerProfileSnapshot eventProfile = null;
            service.ProfileLoaded += profile => eventProfile = profile;

            ProfileRepositoryLoadStatus status = service.LoadProfiles();

            Assert.That(status, Is.EqualTo(ProfileRepositoryLoadStatus.Success));
            Assert.That(eventProfile, Is.Not.Null);
            Assert.That(eventProfile.PlayerId, Is.EqualTo("account-42"));
        }

        [Test]
        public void NameValidation_RejectsEmptyControlAndOverlongNames()
        {
            PlayerProfileService service = CreateService(new MemoryRepository(), 12);

            Assert.That(service.TryCreateProfile("   ", "child_a", out _),
                Is.EqualTo(ProfileOperationStatus.InvalidPlayerName));
            Assert.That(service.TryCreateProfile("bad\u0001name", "child_a", out _),
                Is.EqualTo(ProfileOperationStatus.InvalidPlayerName));
            Assert.That(service.TryCreateProfile("1234567890123", "child_a", out _),
                Is.EqualTo(ProfileOperationStatus.InvalidPlayerName));
        }

        [Test]
        public void NameChange_DoesNotResetStatistics()
        {
            var repository = new MemoryRepository();
            PlayerProfileService service = CreateService(repository);
            string[] ids = CreateFourProfiles(service);
            ProcessMatch(service, "match-1", ids);

            ProfileOperationStatus status = service.TryUpdatePlayerName(ids[0], ids[0], "Champion");

            Assert.That(status, Is.EqualTo(ProfileOperationStatus.Success));
            service.TryGetProfile(ids[0], out PlayerProfileSnapshot profile);
            Assert.That(profile.PlayerName, Is.EqualTo("Champion"));
            Assert.That(profile.GamesPlayed, Is.EqualTo(1));
            Assert.That(profile.GamesWon, Is.EqualTo(1));
        }

        [Test]
        public void CharacterSelectionChange_UsesStableCatalogIdAndRaisesEvent()
        {
            PlayerProfileService service = CreateService(new MemoryRepository());
            service.TryCreateProfile("Player", "child_a", out PlayerProfileSnapshot created);
            PlayerProfileSnapshot changed = null;
            service.CharacterSelectionChanged += profile => changed = profile;

            ProfileOperationStatus status = service.TryUpdateCharacter(
                created.PlayerId, created.PlayerId, " child_b ");

            Assert.That(status, Is.EqualTo(ProfileOperationStatus.Success));
            Assert.That(changed.SelectedCharacterId, Is.EqualTo("child_b"));
        }

        [Test]
        public void InvalidCharacterId_IsRejectedWithoutChangingSelection()
        {
            PlayerProfileService service = CreateService(new MemoryRepository());
            service.TryCreateProfile("Player", "child_a", out PlayerProfileSnapshot created);

            ProfileOperationStatus status = service.TryUpdateCharacter(
                created.PlayerId, created.PlayerId, "not-in-catalog");

            Assert.That(status, Is.EqualTo(ProfileOperationStatus.InvalidCharacterId));
            service.TryGetProfile(created.PlayerId, out PlayerProfileSnapshot unchanged);
            Assert.That(unchanged.SelectedCharacterId, Is.EqualTo("child_a"));
        }

        [Test]
        public void CompletedMatch_IncreasesGamesPlayedExactlyOnceForEveryParticipant()
        {
            PlayerProfileService service = CreateService(new MemoryRepository());
            string[] ids = CreateFourProfiles(service);

            StatisticsProcessStatus status = ProcessMatch(service, "match-1", ids);

            Assert.That(status, Is.EqualTo(StatisticsProcessStatus.Success));
            foreach (string id in ids)
            {
                service.TryGetProfile(id, out PlayerProfileSnapshot profile);
                Assert.That(profile.GamesPlayed, Is.EqualTo(1));
            }
        }

        [Test]
        public void FirstPlace_IncreasesGamesWon()
        {
            PlayerProfileService service = CreateService(new MemoryRepository());
            string[] ids = CreateFourProfiles(service);
            ProcessMatch(service, "match-1", ids);

            service.TryGetProfile(ids[0], out PlayerProfileSnapshot first);

            Assert.That(first.GamesPlayed, Is.EqualTo(1));
            Assert.That(first.GamesWon, Is.EqualTo(1));
            Assert.That(first.ThirdPlaceFinishes, Is.Zero);
        }

        [Test]
        public void SecondPlace_DoesNotIncreaseWinsOrThirdPlaceFinishes()
        {
            PlayerProfileService service = CreateService(new MemoryRepository());
            string[] ids = CreateFourProfiles(service);
            ProcessMatch(service, "match-1", ids);

            service.TryGetProfile(ids[1], out PlayerProfileSnapshot second);

            Assert.That(second.GamesPlayed, Is.EqualTo(1));
            Assert.That(second.GamesWon, Is.Zero);
            Assert.That(second.ThirdPlaceFinishes, Is.Zero);
        }

        [Test]
        public void ThirdPlace_IncreasesOnlyThirdPlaceFinishes()
        {
            PlayerProfileService service = CreateService(new MemoryRepository());
            string[] ids = CreateFourProfiles(service);
            ProcessMatch(service, "match-1", ids);

            service.TryGetProfile(ids[2], out PlayerProfileSnapshot third);

            Assert.That(third.GamesPlayed, Is.EqualTo(1));
            Assert.That(third.GamesWon, Is.Zero);
            Assert.That(third.ThirdPlaceFinishes, Is.EqualTo(1));
        }

        [Test]
        public void FourthPlace_IncreasesOnlyGamesPlayed()
        {
            PlayerProfileService service = CreateService(new MemoryRepository());
            string[] ids = CreateFourProfiles(service);
            ProcessMatch(service, "match-1", ids);

            service.TryGetProfile(ids[3], out PlayerProfileSnapshot fourth);

            Assert.That(fourth.GamesPlayed, Is.EqualTo(1));
            Assert.That(fourth.GamesWon, Is.Zero);
            Assert.That(fourth.ThirdPlaceFinishes, Is.Zero);
        }

        [Test]
        public void IndividualBattleVictory_HasNoLifetimeStatisticsEntryPoint()
        {
            PlayerProfileService service = CreateService(new MemoryRepository());
            service.TryCreateProfile("Player", "child_a", out PlayerProfileSnapshot created);
            int publicStatisticMutators = typeof(PlayerProfileService)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Count(method => method.Name.IndexOf("Win", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 method.Name.IndexOf("Placement", StringComparison.OrdinalIgnoreCase) >= 0);

            service.TryGetProfile(created.PlayerId, out PlayerProfileSnapshot profile);

            Assert.That(publicStatisticMutators, Is.Zero);
            Assert.That(profile.GamesPlayed, Is.Zero);
            Assert.That(profile.GamesWon, Is.Zero);
        }

        [Test]
        public void DuplicateMatchResult_DoesNotAwardStatisticsAgainEvenAfterReload()
        {
            var repository = new MemoryRepository();
            PlayerProfileService firstSession = CreateService(repository);
            string[] ids = CreateFourProfiles(firstSession);
            Assert.That(ProcessMatch(firstSession, "match-1", ids),
                Is.EqualTo(StatisticsProcessStatus.Success));
            PlayerProfileService secondSession = CreateService(repository);
            secondSession.LoadProfiles();

            StatisticsProcessStatus duplicate = ProcessMatch(secondSession, "match-1", ids);

            Assert.That(duplicate, Is.EqualTo(StatisticsProcessStatus.DuplicateMatch));
            secondSession.TryGetProfile(ids[0], out PlayerProfileSnapshot profile);
            Assert.That(profile.GamesPlayed, Is.EqualTo(1));
            Assert.That(profile.GamesWon, Is.EqualTo(1));
        }

        [Test]
        public void IncompleteMatch_DoesNotAwardPermanentStatistics()
        {
            PlayerProfileService service = CreateService(new MemoryRepository());
            string[] ids = CreateFourProfiles(service);
            var statistics = new PlayerStatisticsService(service, new Authority(true));
            CompletedMatchResult incomplete = CreateResult("match-1", ids, false);

            StatisticsProcessStatus status = statistics.ProcessCompletedMatch(incomplete);

            Assert.That(status, Is.EqualTo(StatisticsProcessStatus.IncompleteMatch));
            service.TryGetProfile(ids[0], out PlayerProfileSnapshot profile);
            Assert.That(profile.GamesPlayed, Is.Zero);
        }

        [Test]
        public void JsonRepository_PersistsProfileAndStatisticsAcrossRestarts()
        {
            string path = CreateTemporarySavePath();
            var firstRepository = new JsonPlayerProfileRepository(path);
            PlayerProfileService firstSession = CreateService(firstRepository);
            string[] ids = CreateFourProfiles(firstSession);
            ProcessMatch(firstSession, "match-1", ids);

            PlayerProfileService secondSession = CreateService(new JsonPlayerProfileRepository(path));
            ProfileRepositoryLoadStatus loadStatus = secondSession.LoadProfiles();

            Assert.That(loadStatus, Is.EqualTo(ProfileRepositoryLoadStatus.Success));
            secondSession.TryGetProfile(ids[0], out PlayerProfileSnapshot profile);
            Assert.That(profile.GamesPlayed, Is.EqualTo(1));
            Assert.That(profile.GamesWon, Is.EqualTo(1));
            Assert.That(profile.PlayerId, Is.EqualTo(ids[0]));
        }

        [Test]
        public void JsonRepository_HandlesMissingAndCorruptDataWithoutThrowing()
        {
            string path = CreateTemporarySavePath();
            var missing = new JsonPlayerProfileRepository(path);
            Assert.That(missing.Load().Status, Is.EqualTo(ProfileRepositoryLoadStatus.NotFound));
            File.WriteAllText(path, "{ definitely-not-valid-json");

            ProfileRepositoryLoadResult corrupt = missing.Load();

            Assert.That(corrupt.Status, Is.EqualTo(ProfileRepositoryLoadStatus.CorruptOrUnsupported));
        }

        [Test]
        public void OwnershipValidation_PreventsChangingAnotherPlayersProfile()
        {
            PlayerProfileService service = CreateService(new MemoryRepository());
            string[] ids = CreateFourProfiles(service);

            ProfileOperationStatus nameStatus = service.TryUpdatePlayerName(ids[0], ids[1], "Stolen");
            ProfileOperationStatus characterStatus = service.TryUpdateCharacter(ids[0], ids[1], "child_b");

            Assert.That(nameStatus, Is.EqualTo(ProfileOperationStatus.Unauthorized));
            Assert.That(characterStatus, Is.EqualTo(ProfileOperationStatus.Unauthorized));
            service.TryGetProfile(ids[1], out PlayerProfileSnapshot unchanged);
            Assert.That(unchanged.PlayerName, Is.EqualTo("Player 2"));
            Assert.That(unchanged.SelectedCharacterId, Is.EqualTo("child_a"));
        }

        [Test]
        public void ProfileRpc_ReplicatesCharacterIdUsingOnlySupportedScalarParameters()
        {
            MethodInfo rpc = typeof(PlayerProfileNetworkBridge).GetMethod(
                "PublishProfileClientRpc",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Type[] parameterTypes = rpc.GetParameters().Select(parameter => parameter.ParameterType).ToArray();

            Assert.That(parameterTypes, Is.EqualTo(new[]
            {
                typeof(string), typeof(string), typeof(string),
                typeof(int), typeof(int), typeof(int), typeof(ClientRpcParams)
            }));
            Assert.That(rpc.GetParameters()[2].Name, Is.EqualTo("selectedCharacterId"));
            Assert.That(parameterTypes.Any(type => type.IsArray), Is.False);
        }

        [Test]
        public void CharacterPreviewChange_DoesNotModifyGameplayCharacterAndRestoresOldPreview()
        {
            var host = new GameObject("Preview Controller");
            var gameplayCharacter = new GameObject("Gameplay Character");
            var firstPreview = new GameObject("First Preview");
            var secondPreview = new GameObject("Second Preview");
            try
            {
                gameplayCharacter.transform.localPosition = new Vector3(10f, 2f, -3f);
                firstPreview.transform.localPosition = new Vector3(0f, 1f, 0f);
                secondPreview.transform.localPosition = new Vector3(2f, 3f, 4f);
                Vector3 gameplayStart = gameplayCharacter.transform.localPosition;
                Vector3 firstStart = firstPreview.transform.localPosition;
                Vector3 secondStart = secondPreview.transform.localPosition;
                CharacterPreviewController controller = host.AddComponent<CharacterPreviewController>();
                controller.ConfigureMotion(0.5f, 0.2f, 90f);
                controller.SetPreview(firstPreview.transform);
                controller.ApplyPreviewPose(0.5f);
                Assert.That(firstPreview.transform.localPosition, Is.Not.EqualTo(firstStart));

                controller.SetPreview(secondPreview.transform);
                controller.ApplyPreviewPose(0.5f);

                Assert.That(firstPreview.transform.localPosition, Is.EqualTo(firstStart));
                Assert.That(secondPreview.transform.localPosition, Is.Not.EqualTo(secondStart));
                Assert.That(gameplayCharacter.transform.localPosition, Is.EqualTo(gameplayStart));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(gameplayCharacter);
                UnityEngine.Object.DestroyImmediate(firstPreview);
                UnityEngine.Object.DestroyImmediate(secondPreview);
            }
        }

        [Test]
        public void NonAuthoritativeStatisticsService_CannotUpdateProfiles()
        {
            PlayerProfileService service = CreateService(new MemoryRepository());
            string[] ids = CreateFourProfiles(service);
            var statistics = new PlayerStatisticsService(service, new Authority(false));

            StatisticsProcessStatus status = statistics.ProcessCompletedMatch(
                CreateResult("match-1", ids, true));

            Assert.That(status, Is.EqualTo(StatisticsProcessStatus.NotAuthoritative));
            service.TryGetProfile(ids[0], out PlayerProfileSnapshot profile);
            Assert.That(profile.GamesPlayed, Is.Zero);
        }

        private string CreateTemporarySavePath()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "apir-profile-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            temporaryDirectories.Add(directory);
            return Path.Combine(directory, "profiles.json");
        }

        private static PlayerProfileService CreateService(
            IPlayerProfileRepository repository,
            int maximumNameLength = PlayerNamePolicy.DefaultMaximumLength)
        {
            return new PlayerProfileService(
                repository,
                new CharacterIdCatalog(new[] { "child_a", "child_b" }),
                maximumPlayerNameLength: maximumNameLength);
        }

        private static string[] CreateFourProfiles(PlayerProfileService service)
        {
            var ids = new string[4];
            for (int i = 0; i < ids.Length; i++)
            {
                string id = "player-" + (i + 1);
                ProfileOperationStatus status = service.TryCreateProfile(
                    "Player " + (i + 1), "child_a", out PlayerProfileSnapshot profile, id);
                Assert.That(status, Is.EqualTo(ProfileOperationStatus.Success));
                ids[i] = profile.PlayerId;
            }

            return ids;
        }

        private static StatisticsProcessStatus ProcessMatch(
            PlayerProfileService service,
            string matchId,
            string[] ids)
        {
            var statistics = new PlayerStatisticsService(service, new Authority(true));
            return statistics.ProcessCompletedMatch(CreateResult(matchId, ids, true));
        }

        private static CompletedMatchResult CreateResult(string matchId, string[] ids, bool completed)
        {
            return new CompletedMatchResult(matchId, completed, new[]
            {
                new PlayerFinalPlacement(ids[0], FinalMatchPlacement.First),
                new PlayerFinalPlacement(ids[1], FinalMatchPlacement.Second),
                new PlayerFinalPlacement(ids[2], FinalMatchPlacement.Third),
                new PlayerFinalPlacement(ids[3], FinalMatchPlacement.Fourth)
            });
        }

        private sealed class Authority : IPlayerProfileAuthority
        {
            public Authority(bool isAuthoritative)
            {
                IsAuthoritative = isAuthoritative;
            }

            public bool IsAuthoritative { get; }
        }

        private sealed class MemoryRepository : IPlayerProfileRepository
        {
            private PlayerProfileRepositoryState state;

            public ProfileRepositoryLoadResult Load()
            {
                return state == null
                    ? new ProfileRepositoryLoadResult(ProfileRepositoryLoadStatus.NotFound)
                    : new ProfileRepositoryLoadResult(ProfileRepositoryLoadStatus.Success, state);
            }

            public bool TrySave(PlayerProfileRepositoryState newState)
            {
                state = new PlayerProfileRepositoryState(
                    newState.Profiles,
                    newState.ProcessedMatchIds);
                return true;
            }
        }
    }
}
