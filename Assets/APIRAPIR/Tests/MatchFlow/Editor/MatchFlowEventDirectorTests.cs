using System;
using System.Collections.Generic;
using System.Linq;
using ApirApir.EventDirector;
using ApirApir.MatchFlow;
using NUnit.Framework;

namespace ApirApir.MatchFlowTests
{
    public sealed class MatchFlowEventDirectorTests
    {
        private static readonly string[] Players = { "A", "B", "C", "D" };

        [Test]
        public void MatchStartsWithFourConnectedContendersAndFiveLives()
        {
            var flow = new AuthoritativeMatchFlow("match", Players, true);

            Assert.That(flow.ActiveContenderCount, Is.EqualTo(4));
            Assert.That(flow.ConnectedParticipantCount, Is.EqualTo(4));
            Assert.That(flow.CreateSnapshot().Participants.All(player =>
                player.ChampionshipLives == AuthoritativeMatchFlow.DefaultStartingLives), Is.True);
        }

        [Test]
        public void FourContendersExposeDeclaredOneVsThreeAndTwoVsTwoGames()
        {
            AuthoritativeEventDirector director = CreateDirector(CreateFlow());

            string[] ids = director.GetEligibleMinigames().Select(game => game.MinigameId).ToArray();

            Assert.That(ids, Is.EquivalentTo(new[]
            {
                DefaultMinigameCatalog.PatinteroId,
                DefaultMinigameCatalog.TaguTaguanId
            }));
        }

        [Test]
        public void CatalogDeclaresOnlyApprovedFormatsPerMinigame()
        {
            MinigameDefinition patintero = DefaultMinigameCatalog.Create()
                .Single(value => value.MinigameId == DefaultMinigameCatalog.PatinteroId);
            MinigameDefinition taguTaguan = DefaultMinigameCatalog.Create()
                .Single(value => value.MinigameId == DefaultMinigameCatalog.TaguTaguanId);

            Assert.That(patintero.Eligibility.Select(value => value.Format).Distinct(),
                Is.EqualTo(new[] { MinigameFormat.TwoVsTwo }));
            Assert.That(taguTaguan.Eligibility.Select(value => value.Format).Distinct(),
                Is.EqualTo(new[] { MinigameFormat.OneVsThree }));
        }

        [Test]
        public void InvalidFormatDeclarationIsRejected()
        {
            Assert.Throws<ArgumentException>(() => new MinigameDefinition(
                "invalid",
                new[] { new MinigameEligibilityDeclaration(4, (MinigameFormat)255) },
                new InvalidMixedTeamPolicy()));
        }

        [Test]
        public void FourPlayerTaguTaguanAssignsExactlyOneRandomTaya()
        {
            AuthoritativeMatchFlow flow = CreateFlow();
            AuthoritativeEventDirector director = CreateDirector(flow, 1, 0, 1234);

            Assert.That(director.TryCreateNextPlan("event", 1, out MinigameEventPlan plan).Succeeded, Is.True);
            Assert.That(plan.MinigameId, Is.EqualTo(DefaultMinigameCatalog.TaguTaguanId));
            Assert.That(plan.Format, Is.EqualTo(MinigameFormat.OneVsThree));
            Assert.That(plan.Assignments.Count(role => role.Role == MinigameParticipantRole.Taya), Is.EqualTo(1));
            Assert.That(plan.Assignments.Count(role => role.Role == MinigameParticipantRole.Hider), Is.EqualTo(3));
        }

        [Test]
        public void FourPlayerPatinteroCreatesTwoTeamsOfTwo()
        {
            AuthoritativeEventDirector director = CreateDirector(CreateFlow(), 0, 0, 1234);

            Assert.That(director.TryCreateNextPlan("event", 1, out MinigameEventPlan plan).Succeeded, Is.True);
            Assert.That(plan.MinigameId, Is.EqualTo(DefaultMinigameCatalog.PatinteroId));
            Assert.That(plan.Assignments.Count(value => value.Team == MinigameTeam.TeamA), Is.EqualTo(2));
            Assert.That(plan.Assignments.Count(value => value.Team == MinigameTeam.TeamB), Is.EqualTo(2));
        }

        [Test]
        public void ThreeContendersAllowOnlyTaguTaguanOneVsThree()
        {
            AuthoritativeMatchFlow flow = CreateFlow();
            Eliminate(flow, "D", "drop-d");
            AuthoritativeEventDirector director = CreateDirector(flow);

            Assert.That(director.GetEligibleMinigames().Select(game => game.MinigameId),
                Is.EqualTo(new[] { DefaultMinigameCatalog.TaguTaguanId }));
            Assert.That(director.TryCreateNextPlan("event", 1, out MinigameEventPlan plan).Succeeded, Is.True);
            Assert.That(plan.MinigameId, Is.EqualTo(DefaultMinigameCatalog.TaguTaguanId));
            Assert.That(plan.Format, Is.EqualTo(MinigameFormat.OneVsThree));
        }

        [Test]
        public void ThreeContenderTaguTaguanMakesEliminatedPlayerTaya()
        {
            AuthoritativeMatchFlow flow = CreateFlow();
            Eliminate(flow, "D", "drop-d");
            AuthoritativeEventDirector director = CreateDirector(flow);

            director.TryCreateNextPlan("event", 1, out MinigameEventPlan plan);

            MinigameParticipantAssignment taya = plan.Assignments.Single(value =>
                value.Role == MinigameParticipantRole.Taya);
            Assert.That(taya.PlayerId, Is.EqualTo("D"));
            Assert.That(taya.Membership, Is.EqualTo(ChampionshipMembership.EliminatedPlayer));
            Assert.That(plan.Assignments.Where(value => value.Role == MinigameParticipantRole.Hider)
                .All(value => value.Membership == ChampionshipMembership.ActiveContender), Is.True);
        }

        [Test]
        public void SecondEliminationStartsChampionshipWithoutDisconnectingPlayers()
        {
            AuthoritativeMatchFlow flow = CreateFlow();
            Eliminate(flow, "D", "drop-d");
            Eliminate(flow, "C", "drop-c");

            Assert.That(flow.ActiveContenderCount, Is.EqualTo(2));
            Assert.That(flow.EliminatedPlayerCount, Is.EqualTo(2));
            Assert.That(flow.ConnectedParticipantCount, Is.EqualTo(4));
            Assert.That(flow.Phase, Is.EqualTo(MatchPhase.ChampionshipMinigames));
        }

        [Test]
        public void ChampionshipTeamsAlwaysMixFinalistsAndEliminatedPlayers()
        {
            AuthoritativeMatchFlow flow = CreateChampionshipFlow();
            AuthoritativeEventDirector director = CreateDirector(flow, 0, 0, 9876);

            Assert.That(director.TryCreateNextPlan("final-1", 1, out MinigameEventPlan plan).Succeeded, Is.True);
            Assert.That(plan.Format, Is.EqualTo(MinigameFormat.TwoVsTwo));
            foreach (MinigameTeam team in new[] { MinigameTeam.TeamA, MinigameTeam.TeamB })
            {
                MinigameParticipantAssignment[] members = plan.Assignments
                    .Where(value => value.Team == team).ToArray();
                Assert.That(members.Length, Is.EqualTo(2));
                Assert.That(members.Count(value =>
                    value.Membership == ChampionshipMembership.ActiveContender), Is.EqualTo(1));
                Assert.That(members.Count(value =>
                    value.Membership == ChampionshipMembership.EliminatedPlayer), Is.EqualTo(1));
            }
        }

        [Test]
        public void ChampionshipRunsExactlyTwoMinigameOpportunitiesThenTyangge()
        {
            AuthoritativeMatchFlow flow = CreateChampionshipFlow();
            AuthoritativeEventDirector director = CreateDirector(flow);

            CompleteMinigame(director, "final-1", 1);
            Assert.That(flow.CompletedFinalMinigames, Is.EqualTo(1));
            Assert.That(flow.Phase, Is.EqualTo(MatchPhase.ChampionshipMinigames));

            CompleteMinigame(director, "final-2", 2);
            Assert.That(flow.CompletedFinalMinigames, Is.EqualTo(2));
            Assert.That(flow.Phase, Is.EqualTo(MatchPhase.FinalTyangge));
            Assert.That(director.TryCreateNextPlan("final-3", 3, out _).Failure,
                Is.EqualTo(EventDirectorFailure.InvalidMatchStage));
        }

        [Test]
        public void InvalidChampionshipTeamCombinationIsRejected()
        {
            AuthoritativeMatchFlow flow = CreateChampionshipFlow();
            var invalid = new MinigameDefinition(
                "invalid-2v2",
                new[] { new MinigameEligibilityDeclaration(2, MinigameFormat.TwoVsTwo) },
                new InvalidMixedTeamPolicy());
            var director = new AuthoritativeEventDirector(
                flow, new[] { invalid }, true, new SequenceRandom(0, 0, 1));

            EventDirectorOperationResult result = director.TryCreateNextPlan("event", 1, out _);

            Assert.That(result.Failure, Is.EqualTo(EventDirectorFailure.AssignmentRejected));
        }

        [Test]
        public void MissingTwoVsTwoImplementationIsReportedWithoutSubstitution()
        {
            AuthoritativeMatchFlow flow = CreateChampionshipFlow();
            MinigameDefinition taguOnly = DefaultMinigameCatalog.Create()
                .Single(value => value.MinigameId == DefaultMinigameCatalog.TaguTaguanId);
            var director = new AuthoritativeEventDirector(flow, new[] { taguOnly }, true);

            Assert.That(director.TryCreateNextPlan("event", 1, out _).Failure,
                Is.EqualTo(EventDirectorFailure.NoEligibleMinigame));
        }

        [Test]
        public void CompletingMinigameDoesNotRestoreEliminatedLives()
        {
            AuthoritativeMatchFlow flow = CreateChampionshipFlow();
            AuthoritativeEventDirector director = CreateDirector(flow);
            int before = flow.CreateSnapshot().Participants.Single(value => value.PlayerId == "C")
                .ChampionshipLives;

            CompleteMinigame(director, "final-1", 1);

            int after = flow.CreateSnapshot().Participants.Single(value => value.PlayerId == "C")
                .ChampionshipLives;
            Assert.That(before, Is.Zero);
            Assert.That(after, Is.Zero);
        }

        [Test]
        public void ResultEventExposesBettingReadyReadOnlyDataWithoutBettingLogic()
        {
            AuthoritativeMatchFlow flow = CreateChampionshipFlow();
            AuthoritativeEventDirector director = CreateDirector(flow);
            MinigameResult observed = null;
            director.ResultPublished += value => observed = value;
            director.TryCreateNextPlan("final-1", 1, out MinigameEventPlan plan);
            var individual = plan.Assignments.Select(value =>
                new MinigameIndividualResult(value.PlayerId, value.Team == MinigameTeam.TeamA, 10))
                .ToArray();

            director.TryCompleteCurrentPlan(
                "final-1", 1, MinigameTeam.TeamA, MinigameTeam.TeamB, individual, out _);

            Assert.That(observed, Is.Not.Null);
            Assert.That(observed.MatchId, Is.EqualTo("match"));
            Assert.That(observed.TeamAssignments.Count, Is.EqualTo(4));
            Assert.That(observed.IndividualResults.Count, Is.EqualTo(4));
            Assert.That(observed.Status, Is.EqualTo(MinigameCompletionStatus.Completed));
        }

        [Test]
        public void DuplicateAndStaleEventsCannotRepeatChampionshipTransition()
        {
            AuthoritativeMatchFlow flow = CreateChampionshipFlow();
            AuthoritativeEventDirector director = CreateDirector(flow);
            CompleteMinigame(director, "final-1", 1);

            Assert.That(director.TryCreateNextPlan("final-1", 2, out _).Failure,
                Is.EqualTo(EventDirectorFailure.DuplicateOrStaleEvent));
            Assert.That(director.TryCreateNextPlan("stale", 1, out _).Failure,
                Is.EqualTo(EventDirectorFailure.DuplicateOrStaleEvent));
            Assert.That(flow.CompletedFinalMinigames, Is.EqualTo(1));
        }

        [Test]
        public void DisconnectedParticipantPreventsArtificialReplacement()
        {
            AuthoritativeMatchFlow flow = CreateChampionshipFlow();
            flow.SetConnectionState("D", false);
            AuthoritativeEventDirector director = CreateDirector(flow);

            Assert.That(director.TryCreateNextPlan("event", 1, out _).Failure,
                Is.EqualTo(EventDirectorFailure.ParticipantsDisconnected));
        }

        [Test]
        public void ChampionshipBattleAndConsolationRemainSeparateFromMinigames()
        {
            AuthoritativeMatchFlow flow = CreateChampionshipFlow();
            AuthoritativeEventDirector director = CreateDirector(flow);
            CompleteMinigame(director, "final-1", 1);
            CompleteMinigame(director, "final-2", 2);

            Assert.That(flow.CompleteFinalTyangge("shop-done").Succeeded, Is.True);
            Assert.That(flow.ApplyFinalRoundRecovery("recover").Succeeded, Is.True);
            Assert.That(flow.CompleteFinalPreparation("prep-done").Succeeded, Is.True);
            Assert.That(flow.Phase, Is.EqualTo(MatchPhase.ChampionshipBattle));
            Assert.That(flow.CompleteChampionshipBattle("champ", "A", "B").Succeeded, Is.True);
            Assert.That(flow.Phase, Is.EqualTo(MatchPhase.ConsolationBattle));
            Assert.That(flow.CompleteConsolationBattle("consolation", "C", "D").Succeeded, Is.True);
            Assert.That(flow.Phase, Is.EqualTo(MatchPhase.Completed));
            Assert.That(flow.FirstPlacePlayerId, Is.EqualTo("A"));
            Assert.That(flow.ThirdPlacePlayerId, Is.EqualTo("C"));
        }

        private static AuthoritativeMatchFlow CreateFlow()
        {
            return new AuthoritativeMatchFlow("match", Players, true, 1);
        }

        private static AuthoritativeMatchFlow CreateChampionshipFlow()
        {
            AuthoritativeMatchFlow flow = CreateFlow();
            Eliminate(flow, "D", "drop-d");
            Eliminate(flow, "C", "drop-c");
            return flow;
        }

        private static AuthoritativeEventDirector CreateDirector(
            AuthoritativeMatchFlow flow,
            params int[] randomValues)
        {
            return new AuthoritativeEventDirector(
                flow,
                DefaultMinigameCatalog.Create(),
                true,
                new SequenceRandom(randomValues));
        }

        private static void Eliminate(AuthoritativeMatchFlow flow, string playerId, string commandId)
        {
            Assert.That(flow.ApplyStandardBattleLoss(commandId, playerId).Succeeded, Is.True);
        }

        private static void CompleteMinigame(
            AuthoritativeEventDirector director,
            string eventId,
            long sequence)
        {
            Assert.That(director.TryCreateNextPlan(eventId, sequence, out _).Succeeded, Is.True);
            Assert.That(director.TryCompleteCurrentPlan(
                eventId,
                sequence,
                MinigameTeam.TeamA,
                MinigameTeam.TeamB,
                Array.Empty<MinigameIndividualResult>(),
                out _).Succeeded, Is.True);
        }

        private sealed class SequenceRandom : IEventDirectorRandom
        {
            private readonly Queue<int> values;

            public SequenceRandom(params int[] values)
            {
                this.values = new Queue<int>(values ?? Array.Empty<int>());
            }

            public int Next(int exclusiveMaximum)
            {
                int value = values.Count == 0 ? 0 : values.Dequeue();
                return Math.Abs(value % exclusiveMaximum);
            }
        }

        private sealed class InvalidMixedTeamPolicy : IMinigameAssignmentPolicy
        {
            public bool TryCreateAssignments(
                MatchFlowSnapshot match,
                MinigameFormat format,
                int randomSeed,
                out IReadOnlyList<MinigameParticipantAssignment> assignments)
            {
                var result = new List<MinigameParticipantAssignment>();
                for (int i = 0; i < match.Participants.Count; i++)
                {
                    MatchParticipantSnapshot participant = match.Participants[i];
                    bool finalist = participant.IsContender;
                    result.Add(new MinigameParticipantAssignment(
                        participant.PlayerId,
                        finalist
                            ? ChampionshipMembership.ActiveContender
                            : ChampionshipMembership.EliminatedPlayer,
                        finalist ? MinigameTeam.TeamA : MinigameTeam.TeamB,
                        finalist ? MinigameParticipantRole.Runner : MinigameParticipantRole.Guard));
                }

                assignments = result.AsReadOnly();
                return true;
            }
        }
    }
}
