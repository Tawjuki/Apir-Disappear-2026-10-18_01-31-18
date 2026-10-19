using System;
using System.Collections.Generic;
using ApirApir.EventDirector.Formats;
using ApirApir.MatchFlow;

namespace ApirApir.EventDirector
{
    /// <summary>
    /// Filters registered minigames by declared capabilities and publishes authoritative,
    /// immutable assignments/results for networking and future read-only consumers.
    /// </summary>
    public sealed class AuthoritativeEventDirector
    {
        private readonly AuthoritativeMatchFlow matchFlow;
        private readonly List<MinigameDefinition> definitions;
        private readonly IEventDirectorRandom random;
        private readonly bool isAuthoritative;
        private readonly HashSet<string> processedEventIds =
            new HashSet<string>(StringComparer.Ordinal);
        private long lastSequence;

        public AuthoritativeEventDirector(
            AuthoritativeMatchFlow matchFlow,
            IEnumerable<MinigameDefinition> definitions,
            bool isAuthoritative,
            IEventDirectorRandom random = null)
        {
            this.matchFlow = matchFlow ?? throw new ArgumentNullException(nameof(matchFlow));
            this.definitions = definitions == null
                ? throw new ArgumentNullException(nameof(definitions))
                : new List<MinigameDefinition>(definitions);
            if (this.definitions.Count == 0)
            {
                throw new ArgumentException("At least one minigame must be registered.", nameof(definitions));
            }

            var minigameIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < this.definitions.Count; i++)
            {
                if (this.definitions[i] == null || !minigameIds.Add(this.definitions[i].MinigameId))
                {
                    throw new ArgumentException(
                        "Registered minigames must be non-null and have unique IDs.",
                        nameof(definitions));
                }
            }

            this.isAuthoritative = isAuthoritative;
            this.random = random ?? new SystemEventDirectorRandom();
        }

        public event Action<MinigameEventPlan> PlanPublished;
        public event Action<MinigameResult> ResultPublished;

        public MinigameEventPlan CurrentPlan { get; private set; }
        public MinigameResult LastResult { get; private set; }
        public string MatchId => matchFlow.MatchId;

        public IReadOnlyList<MinigameDefinition> GetEligibleMinigames()
        {
            MatchFlowSnapshot snapshot = matchFlow.CreateSnapshot();
            var formats = GetAllowedFormats(snapshot);
            var result = new List<MinigameDefinition>();
            for (int i = 0; i < definitions.Count; i++)
            {
                if (SupportsAny(definitions[i], snapshot, formats))
                {
                    result.Add(definitions[i]);
                }
            }

            return result.AsReadOnly();
        }

        public EventDirectorOperationResult TryCreateNextPlan(
            string eventId,
            long sequence,
            out MinigameEventPlan plan)
        {
            plan = null;
            if (!isAuthoritative)
            {
                return EventDirectorOperationResult.Reject(EventDirectorFailure.NotAuthoritative);
            }

            MatchFlowSnapshot snapshot = matchFlow.CreateSnapshot();
            string normalizedEventId = Normalize(eventId);
            if (normalizedEventId.Length == 0 || sequence <= 0)
            {
                return EventDirectorOperationResult.Reject(EventDirectorFailure.InvalidRequest);
            }

            if (CurrentPlan != null)
            {
                return EventDirectorOperationResult.Reject(EventDirectorFailure.EventAlreadyRunning);
            }

            if (sequence <= lastSequence || processedEventIds.Contains(normalizedEventId))
            {
                return EventDirectorOperationResult.Reject(EventDirectorFailure.DuplicateOrStaleEvent);
            }

            if (snapshot.Participants.Count != AuthoritativeMatchFlow.RequiredHumanPlayers ||
                CountConnected(snapshot) != AuthoritativeMatchFlow.RequiredHumanPlayers)
            {
                return EventDirectorOperationResult.Reject(EventDirectorFailure.ParticipantsDisconnected);
            }

            IReadOnlyList<MinigameFormat> allowedFormats = GetAllowedFormats(snapshot);
            if (allowedFormats.Count == 0)
            {
                return EventDirectorOperationResult.Reject(EventDirectorFailure.InvalidMatchStage);
            }

            var eligible = new List<MinigameDefinition>();
            for (int i = 0; i < definitions.Count; i++)
            {
                if (SupportsAny(definitions[i], snapshot, allowedFormats))
                {
                    eligible.Add(definitions[i]);
                }
            }

            if (eligible.Count == 0)
            {
                return EventDirectorOperationResult.Reject(EventDirectorFailure.NoEligibleMinigame);
            }

            MinigameDefinition selected = eligible[random.Next(eligible.Count)];
            var selectedFormats = new List<MinigameFormat>();
            for (int i = 0; i < allowedFormats.Count; i++)
            {
                if (selected.Supports(matchFlow.ActiveContenderCount, allowedFormats[i]))
                {
                    selectedFormats.Add(allowedFormats[i]);
                }
            }

            MinigameFormat format = selectedFormats[random.Next(selectedFormats.Count)];
            int seed = random.Next(int.MaxValue);
            if (!selected.AssignmentPolicy.TryCreateAssignments(
                    snapshot, format, seed, out IReadOnlyList<MinigameParticipantAssignment> assignments) ||
                !ValidateAssignments(snapshot, format, assignments))
            {
                return EventDirectorOperationResult.Reject(EventDirectorFailure.AssignmentRejected);
            }

            var candidate = new MinigameEventPlan(
                snapshot.MatchId,
                normalizedEventId,
                sequence,
                selected.MinigameId,
                format,
                CopyAssignments(assignments));

            if (snapshot.Phase == MatchPhase.ChampionshipMinigames)
            {
                MatchFlowOperationResult flowResult = matchFlow.BeginFinalMinigame(normalizedEventId);
                if (!flowResult.Succeeded)
                {
                    return EventDirectorOperationResult.Reject(EventDirectorFailure.InvalidMatchStage);
                }
            }

            CurrentPlan = candidate;
            lastSequence = sequence;
            processedEventIds.Add(normalizedEventId);
            plan = candidate;
            PlanPublished?.Invoke(candidate);
            return EventDirectorOperationResult.Success();
        }

        public EventDirectorOperationResult TryCompleteCurrentPlan(
            string eventId,
            long sequence,
            MinigameTeam winningTeam,
            MinigameTeam losingTeam,
            IReadOnlyList<MinigameIndividualResult> individualResults,
            out MinigameResult result)
        {
            result = null;
            if (!isAuthoritative)
            {
                return EventDirectorOperationResult.Reject(EventDirectorFailure.NotAuthoritative);
            }

            if (CurrentPlan == null ||
                !string.Equals(CurrentPlan.EventId, Normalize(eventId), StringComparison.Ordinal) ||
                CurrentPlan.Sequence != sequence)
            {
                return EventDirectorOperationResult.Reject(EventDirectorFailure.WrongEvent);
            }

            if (winningTeam == losingTeam ||
                !ValidateIndividualResults(CurrentPlan, individualResults))
            {
                return EventDirectorOperationResult.Reject(EventDirectorFailure.InvalidResult);
            }

            if (matchFlow.Phase == MatchPhase.ChampionshipMinigames)
            {
                MatchFlowOperationResult flowResult = matchFlow.CompleteFinalMinigame(CurrentPlan.EventId);
                if (!flowResult.Succeeded)
                {
                    return EventDirectorOperationResult.Reject(EventDirectorFailure.InvalidMatchStage);
                }
            }

            result = new MinigameResult(
                CurrentPlan,
                winningTeam,
                losingTeam,
                MinigameCompletionStatus.Completed,
                CopyIndividualResults(individualResults));
            LastResult = result;
            CurrentPlan = null;
            ResultPublished?.Invoke(result);
            return EventDirectorOperationResult.Success();
        }

        private static IReadOnlyList<MinigameFormat> GetAllowedFormats(MatchFlowSnapshot snapshot)
        {
            int active = CountContenders(snapshot);
            if (snapshot.Phase == MatchPhase.StandardCompetition && active == 4)
            {
                return new[] { MinigameFormat.OneVsThree, MinigameFormat.TwoVsTwo };
            }

            if (snapshot.Phase == MatchPhase.StandardCompetition && active == 3)
            {
                return new[] { MinigameFormat.OneVsThree };
            }

            if (snapshot.Phase == MatchPhase.ChampionshipMinigames && active == 2)
            {
                return new[] { MinigameFormat.TwoVsTwo };
            }

            return Array.Empty<MinigameFormat>();
        }

        private static bool SupportsAny(
            MinigameDefinition definition,
            MatchFlowSnapshot snapshot,
            IReadOnlyList<MinigameFormat> formats)
        {
            int active = CountContenders(snapshot);
            for (int i = 0; i < formats.Count; i++)
            {
                if (definition.Supports(active, formats[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ValidateAssignments(
            MatchFlowSnapshot snapshot,
            MinigameFormat format,
            IReadOnlyList<MinigameParticipantAssignment> assignments)
        {
            if (assignments == null || assignments.Count != AuthoritativeMatchFlow.RequiredHumanPlayers)
            {
                return false;
            }

            var status = new Dictionary<string, ChampionshipMembership>(StringComparer.Ordinal);
            for (int i = 0; i < snapshot.Participants.Count; i++)
            {
                MatchParticipantSnapshot participant = snapshot.Participants[i];
                status[participant.PlayerId] = participant.IsContender
                    ? ChampionshipMembership.ActiveContender
                    : ChampionshipMembership.EliminatedPlayer;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            int teamA = 0;
            int teamB = 0;
            int activeA = 0;
            int activeB = 0;
            int taya = 0;
            int hiders = 0;
            int runners = 0;
            int guards = 0;
            for (int i = 0; i < assignments.Count; i++)
            {
                MinigameParticipantAssignment assignment = assignments[i];
                if (assignment == null || !seen.Add(assignment.PlayerId) ||
                    !status.TryGetValue(assignment.PlayerId, out ChampionshipMembership expected) ||
                    assignment.Membership != expected)
                {
                    return false;
                }

                if (assignment.Team == MinigameTeam.TeamA)
                {
                    teamA++;
                    if (expected == ChampionshipMembership.ActiveContender) activeA++;
                }
                else
                {
                    teamB++;
                    if (expected == ChampionshipMembership.ActiveContender) activeB++;
                }

                switch (assignment.Role)
                {
                    case MinigameParticipantRole.Taya: taya++; break;
                    case MinigameParticipantRole.Hider: hiders++; break;
                    case MinigameParticipantRole.Runner: runners++; break;
                    case MinigameParticipantRole.Guard: guards++; break;
                }
            }

            int active = CountContenders(snapshot);
            if (format == MinigameFormat.OneVsThree)
            {
                return OneVsThreeFormatRules.IsValidTeamShape(teamA, teamB) &&
                       ((active == 4 && taya == 1 && hiders == 3) ||
                        (active == 3 && activeA == 3 && activeB == 0 &&
                         runners == 3 && guards == 1));
            }

            if (!TwoVsTwoFormatRules.IsValidTeamShape(teamA, teamB))
            {
                return false;
            }

            return TwoVsTwoFormatRules.IsValidFinalistDistribution(active, activeA, activeB);
        }

        private static bool ValidateIndividualResults(
            MinigameEventPlan plan,
            IReadOnlyList<MinigameIndividualResult> results)
        {
            if (results == null || results.Count == 0)
            {
                return true;
            }

            var participants = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < plan.Assignments.Count; i++)
            {
                participants.Add(plan.Assignments[i].PlayerId);
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < results.Count; i++)
            {
                if (!participants.Contains(results[i].PlayerId) || !seen.Add(results[i].PlayerId))
                {
                    return false;
                }
            }

            return true;
        }

        private static IReadOnlyList<MinigameParticipantAssignment> CopyAssignments(
            IReadOnlyList<MinigameParticipantAssignment> source)
        {
            var result = new List<MinigameParticipantAssignment>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                MinigameParticipantAssignment value = source[i];
                result.Add(new MinigameParticipantAssignment(
                    value.PlayerId, value.Membership, value.Team, value.Role));
            }

            return result.AsReadOnly();
        }

        private static IReadOnlyList<MinigameIndividualResult> CopyIndividualResults(
            IReadOnlyList<MinigameIndividualResult> source)
        {
            if (source == null)
            {
                return Array.Empty<MinigameIndividualResult>();
            }

            var result = new List<MinigameIndividualResult>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                result.Add(source[i]);
            }

            return result.AsReadOnly();
        }

        private static int CountContenders(MatchFlowSnapshot snapshot)
        {
            int count = 0;
            for (int i = 0; i < snapshot.Participants.Count; i++)
            {
                if (snapshot.Participants[i].IsContender)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountConnected(MatchFlowSnapshot snapshot)
        {
            int count = 0;
            for (int i = 0; i < snapshot.Participants.Count; i++)
            {
                if (snapshot.Participants[i].IsConnected)
                {
                    count++;
                }
            }

            return count;
        }

        private static string Normalize(string value) => value == null ? string.Empty : value.Trim();
    }
}
