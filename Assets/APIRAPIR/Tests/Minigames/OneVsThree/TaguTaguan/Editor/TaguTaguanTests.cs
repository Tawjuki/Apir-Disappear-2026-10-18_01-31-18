using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ApirApir.EventDirector;
using ApirApir.MatchFlow;
using ApirApir.TaguTaguan;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

namespace ApirApir.Tests.Minigames.OneVsThree
{
    public sealed class TaguTaguanTests
    {
        [Test]
        public void FourContendersAlwaysProduceOneTayaAndThreeHiders()
        {
            var policy = new TaguTaguanRoleAssignmentPolicy();
            MatchFlowSnapshot match = CreateMatch(4);

            Assert.That(policy.TryCreateAssignments(
                match, MinigameFormat.OneVsThree, 17, out var assignments), Is.True);
            Assert.That(assignments.Count(value => value.Role == MinigameParticipantRole.Taya), Is.EqualTo(1));
            Assert.That(assignments.Count(value => value.Role == MinigameParticipantRole.Hider), Is.EqualTo(3));
            Assert.That(assignments.All(value => value.Membership == ChampionshipMembership.ActiveContender),
                Is.True);
        }

        [Test]
        public void SeededRandomSelectionCanChooseEveryHumanContender()
        {
            var policy = new TaguTaguanRoleAssignmentPolicy();
            MatchFlowSnapshot match = CreateMatch(4);
            var selected = new HashSet<string>(StringComparer.Ordinal);

            for (int seed = 0; seed < 1000 && selected.Count < 4; seed++)
            {
                policy.TryCreateAssignments(match, MinigameFormat.OneVsThree, seed, out var assignments);
                selected.Add(assignments.Single(value => value.Role == MinigameParticipantRole.Taya).PlayerId);
            }

            Assert.That(selected, Is.EquivalentTo(new[] { "A", "B", "C", "D" }));
        }

        [Test]
        public void ThreeContendersForceEliminatedHumanToBeTaya()
        {
            var policy = new TaguTaguanRoleAssignmentPolicy();

            Assert.That(policy.TryCreateAssignments(
                CreateMatch(3), MinigameFormat.OneVsThree, 99, out var assignments), Is.True);

            MinigameParticipantAssignment taya = assignments.Single(value =>
                value.Role == MinigameParticipantRole.Taya);
            Assert.That(taya.PlayerId, Is.EqualTo("D"));
            Assert.That(taya.Membership, Is.EqualTo(ChampionshipMembership.EliminatedPlayer));
            Assert.That(assignments.Where(value => value.Role == MinigameParticipantRole.Hider)
                .All(value => value.Membership == ChampionshipMembership.ActiveContender), Is.True);
        }

        [Test]
        public void ReinitializingSamePlanDoesNotRerollRoles()
        {
            MinigameEventPlan plan = CreatePlan();
            var controller = new TaguTaguanController(true, detectionService: new FakeDetection());

            Assert.That(controller.TryInitialize(plan).Succeeded, Is.True);
            string original = controller.TayaPlayerId;
            Assert.That(controller.TryInitialize(plan).Succeeded, Is.True);
            Assert.That(controller.TayaPlayerId, Is.EqualTo(original));
        }

        [Test]
        public void ValidCatchIsAcceptedOnlyOnceAndDoesNotTouchMatchLives()
        {
            var detection = new FakeDetection { Visible = true, WithinRange = true };
            TaguTaguanController controller = CreatePlayingController(detection);
            var world = CreateWorld();
            MatchFlowSnapshot matchBefore = CreateMatch(4);

            TaguTaguanOperationResult first = controller.TryCatch("D", "A", 1, 1d, world);
            TaguTaguanOperationResult duplicate = controller.TryCatch("D", "A", 1, 1.1d, world);

            Assert.That(first.Succeeded, Is.True);
            Assert.That(duplicate.Failure, Is.EqualTo(TaguTaguanFailure.DuplicateOrStaleRequest));
            Assert.That(controller.RemainingHiderCount, Is.EqualTo(2));
            Assert.That(CreateMatch(4).Participants.Select(value => value.ChampionshipLives),
                Is.EqualTo(matchBefore.Participants.Select(value => value.ChampionshipLives)));
        }

        [TestCase(false, true, TaguTaguanFailure.NotVisible)]
        [TestCase(true, false, TaguTaguanFailure.OutOfRange)]
        public void CatchRequiresVisibilityAndRange(
            bool visible,
            bool withinRange,
            TaguTaguanFailure expected)
        {
            var detection = new FakeDetection { Visible = visible, WithinRange = withinRange };
            TaguTaguanController controller = CreatePlayingController(detection);

            TaguTaguanOperationResult result = controller.TryCatch("D", "A", 1, 1d, CreateWorld());

            Assert.That(result.Failure, Is.EqualTo(expected));
            Assert.That(controller.RemainingHiderCount, Is.EqualTo(3));
        }

        [Test]
        public void CatchingAllHidersCompletesWithTayaSuccess()
        {
            var detection = new FakeDetection { Visible = true, WithinRange = true };
            TaguTaguanController controller = CreatePlayingController(detection);
            TaguTaguanResult result = null;
            controller.ResultsPublished += value => result = value;
            FakeWorldResolver world = CreateWorld();

            controller.TryCatch("D", "A", 1, 1d, world);
            controller.TryCatch("D", "B", 2, 2d, world);
            controller.TryCatch("D", "C", 3, 3d, world);

            Assert.That(controller.Phase, Is.EqualTo(TaguTaguanPhase.Completed));
            Assert.That(result, Is.Not.Null);
            Assert.That(result.TayaSucceeded, Is.True);
            Assert.That(result.CaughtHiderIds, Has.Count.EqualTo(3));
            Assert.That(result.SurvivingHiderIds, Is.Empty);
        }

        [Test]
        public void TimerExpiryCompletesWithRemainingHidersSuccessful()
        {
            TaguTaguanController controller = CreatePlayingController(new FakeDetection());
            TaguTaguanResult result = null;
            controller.ResultsPublished += value => result = value;

            controller.Advance(30d);

            Assert.That(controller.Phase, Is.EqualTo(TaguTaguanPhase.Completed));
            Assert.That(result.Reason, Is.EqualTo(TaguTaguanCompletionReason.TimeExpired));
            Assert.That(result.TayaSucceeded, Is.False);
            Assert.That(result.SurvivingHiderIds, Has.Count.EqualTo(3));
        }

        [Test]
        public void TurutotStartsUnavailableThenUsesIndependentCooldowns()
        {
            TaguTaguanController controller = CreatePlayingController(new FakeDetection());
            FakeWorldResolver world = CreateWorld();

            Assert.That(controller.TryUseTurutot("A", 1, 9.9d, world, out _).Failure,
                Is.EqualTo(TaguTaguanFailure.TurutotUnavailable));
            Assert.That(controller.TryUseTurutot("A", 2, 10d, world, out _).Succeeded, Is.True);
            Assert.That(controller.TryUseTurutot("B", 1, 10d, world, out _).Succeeded, Is.True);
            Assert.That(controller.TryUseTurutot("A", 3, 19.9d, world, out _).Failure,
                Is.EqualTo(TaguTaguanFailure.TurutotUnavailable));
            Assert.That(controller.TryUseTurutot("A", 4, 20d, world, out _).Succeeded, Is.True);
        }

        [Test]
        public void TurutotNoiseKeepsItsAuthoritativeActivationPosition()
        {
            TaguTaguanController controller = CreatePlayingController(new FakeDetection());
            FakeWorldResolver world = CreateWorld();

            controller.TryUseTurutot("A", 1, 10d, world, out TurutotNoiseEvent noise);
            world.States["A"] = new TaguTaguanWorldState(new Vector3(50f, 0f, 50f), Vector3.forward);

            Assert.That(noise.Position, Is.EqualTo(new Vector3(1f, 0f, 0f)));
            Assert.That(controller.CreateSnapshot(11d).ActiveNoises.Single().Position,
                Is.EqualTo(new Vector3(1f, 0f, 0f)));
        }

        [Test]
        public void TayaAndCaughtHiderCannotUseTurutot()
        {
            var detection = new FakeDetection { Visible = true, WithinRange = true };
            TaguTaguanController controller = CreatePlayingController(detection);
            FakeWorldResolver world = CreateWorld();

            Assert.That(controller.TryUseTurutot("D", 1, 10d, world, out _).Failure,
                Is.EqualTo(TaguTaguanFailure.UnauthorizedPlayer));
            controller.TryCatch("D", "A", 1, 10d, world);
            Assert.That(controller.TryUseTurutot("A", 1, 10d, world, out _).Failure,
                Is.EqualTo(TaguTaguanFailure.AlreadyCaught));
        }

        [Test]
        public void PhysicsDetectionRespectsObstacleOcclusion()
        {
            var settings = new TaguTaguanSettings
            {
                detectionDistance = 10f,
                fieldOfViewDegrees = 120f,
                obstacleLayers = 1 << 0
            }.ValidatedCopy();
            var detection = new TaguTaguanPhysicsDetectionService();
            var taya = new TaguTaguanWorldState(Vector3.zero, Vector3.forward);
            var hider = new TaguTaguanWorldState(new Vector3(0f, 0f, 5f), Vector3.back);
            GameObject obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstacle.transform.position = new Vector3(0f, 0f, 2.5f);
            obstacle.transform.localScale = new Vector3(2f, 2f, 0.5f);
            Physics.SyncTransforms();

            try
            {
                Assert.That(detection.IsVisible(taya, hider, settings), Is.False);
                UnityEngine.Object.DestroyImmediate(obstacle);
                obstacle = null;
                Physics.SyncTransforms();
                Assert.That(detection.IsVisible(taya, hider, settings), Is.True);
            }
            finally
            {
                if (obstacle != null) UnityEngine.Object.DestroyImmediate(obstacle);
            }
        }

        [Test]
        public void NetworkRpcSignaturesContainNoArrayParameters()
        {
            MethodInfo[] rpcMethods = typeof(TaguTaguanNetworkBridge)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => method.GetCustomAttributes(false)
                    .Any(attribute => attribute is RpcAttribute || attribute is ClientRpcAttribute))
                .ToArray();

            Assert.That(rpcMethods, Is.Not.Empty);
            Assert.That(rpcMethods.SelectMany(method => method.GetParameters())
                .Any(parameter => parameter.ParameterType.IsArray), Is.False);
            Assert.That(rpcMethods.SelectMany(method => method.GetParameters())
                .All(parameter => parameter.ParameterType == typeof(string) ||
                                  parameter.ParameterType == typeof(ulong) ||
                                  parameter.ParameterType == typeof(RpcParams) ||
                                  parameter.ParameterType == typeof(ClientRpcParams)), Is.True);
        }

        private static TaguTaguanController CreatePlayingController(ITaguTaguanDetectionService detection)
        {
            var controller = new TaguTaguanController(true, new TaguTaguanSettings(), detection);
            Assert.That(controller.TryInitialize(CreatePlan()).Succeeded, Is.True);
            Assert.That(controller.TryBeginPreparation().Succeeded, Is.True);
            Assert.That(controller.TryStart(0d).Succeeded, Is.True);
            return controller;
        }

        private static MinigameEventPlan CreatePlan()
        {
            var assignments = new[]
            {
                new MinigameParticipantAssignment("A", ChampionshipMembership.ActiveContender,
                    MinigameTeam.TeamA, MinigameParticipantRole.Hider),
                new MinigameParticipantAssignment("B", ChampionshipMembership.ActiveContender,
                    MinigameTeam.TeamA, MinigameParticipantRole.Hider),
                new MinigameParticipantAssignment("C", ChampionshipMembership.ActiveContender,
                    MinigameTeam.TeamA, MinigameParticipantRole.Hider),
                new MinigameParticipantAssignment("D", ChampionshipMembership.ActiveContender,
                    MinigameTeam.TeamB, MinigameParticipantRole.Taya)
            };
            return new MinigameEventPlan(
                "match", "event", 1, DefaultMinigameCatalog.TaguTaguanId,
                MinigameFormat.OneVsThree, assignments);
        }

        private static MatchFlowSnapshot CreateMatch(int contenderCount)
        {
            var players = new List<MatchParticipantSnapshot>(4);
            string[] ids = { "A", "B", "C", "D" };
            for (int i = 0; i < ids.Length; i++)
            {
                players.Add(new MatchParticipantSnapshot(ids[i], i < contenderCount ? 1 : 0, true));
            }

            return new MatchFlowSnapshot(
                "match",
                contenderCount == 2 ? MatchPhase.ChampionshipMinigames : MatchPhase.StandardCompetition,
                players.AsReadOnly(),
                0,
                string.Empty,
                false);
        }

        private static FakeWorldResolver CreateWorld()
        {
            var world = new FakeWorldResolver();
            world.States["A"] = new TaguTaguanWorldState(new Vector3(1f, 0f, 0f), Vector3.forward);
            world.States["B"] = new TaguTaguanWorldState(new Vector3(0f, 0f, 1f), Vector3.forward);
            world.States["C"] = new TaguTaguanWorldState(new Vector3(-1f, 0f, 0f), Vector3.forward);
            world.States["D"] = new TaguTaguanWorldState(Vector3.zero, Vector3.forward);
            return world;
        }

        private sealed class FakeDetection : ITaguTaguanDetectionService
        {
            public bool Visible { get; set; }
            public bool WithinRange { get; set; }

            public bool IsVisible(
                TaguTaguanWorldState taya,
                TaguTaguanWorldState hider,
                TaguTaguanSettings settings) => Visible;

            public bool IsWithinCatchingDistance(
                TaguTaguanWorldState taya,
                TaguTaguanWorldState hider,
                TaguTaguanSettings settings) => WithinRange;
        }

        private sealed class FakeWorldResolver : ITaguTaguanWorldStateResolver
        {
            public Dictionary<string, TaguTaguanWorldState> States { get; } =
                new Dictionary<string, TaguTaguanWorldState>(StringComparer.Ordinal);

            public bool TryGetWorldState(string playerId, out TaguTaguanWorldState state) =>
                States.TryGetValue(playerId, out state);
        }
    }
}
