using System;
using System.Collections.Generic;
using ApirApir.MatchFlow;
using ApirApir.Patintero;
using ApirApir.TaguTaguan;

namespace ApirApir.EventDirector
{
    public static class DefaultMinigameCatalog
    {
        public const string PatinteroId = "patintero";
        public const string TaguTaguanId = "tagu-taguan";

        public static IReadOnlyList<MinigameDefinition> Create()
        {
            return new[]
            {
                new MinigameDefinition(
                    PatinteroId,
                    new[]
                    {
                        new MinigameEligibilityDeclaration(4, MinigameFormat.TwoVsTwo),
                        new MinigameEligibilityDeclaration(3, MinigameFormat.OneVsThree),
                        new MinigameEligibilityDeclaration(2, MinigameFormat.TwoVsTwo)
                    },
                    new PatinteroAssignmentPolicyAdapter()),
                new MinigameDefinition(
                    TaguTaguanId,
                    new[]
                    {
                        new MinigameEligibilityDeclaration(4, MinigameFormat.OneVsThree)
                    },
                    new TaguTaguanRoleAssignmentPolicy())
            };
        }
    }

    /// <summary>Maps Patintero-owned role decisions to the shared event contract.</summary>
    public sealed class PatinteroAssignmentPolicyAdapter : IMinigameAssignmentPolicy
    {
        private readonly PatinteroRoleAssignmentPolicy policy = new PatinteroRoleAssignmentPolicy();

        public bool TryCreateAssignments(
            MatchFlowSnapshot match,
            MinigameFormat format,
            int randomSeed,
            out IReadOnlyList<MinigameParticipantAssignment> assignments)
        {
            assignments = Array.Empty<MinigameParticipantAssignment>();
            if (match == null || !IsExpectedFormat(match, format))
            {
                return false;
            }

            var contenders = new List<string>();
            var eliminated = new List<string>();
            var membership = new Dictionary<string, ChampionshipMembership>(StringComparer.Ordinal);
            for (int i = 0; i < match.Participants.Count; i++)
            {
                MatchParticipantSnapshot participant = match.Participants[i];
                if (participant.IsContender)
                {
                    contenders.Add(participant.PlayerId);
                    membership[participant.PlayerId] = ChampionshipMembership.ActiveContender;
                }
                else
                {
                    eliminated.Add(participant.PlayerId);
                    membership[participant.PlayerId] = ChampionshipMembership.EliminatedPlayer;
                }
            }

            if (!policy.TryCreate(contenders, eliminated, randomSeed, out var patinteroAssignments))
            {
                return false;
            }

            var result = new List<MinigameParticipantAssignment>(patinteroAssignments.Count);
            for (int i = 0; i < patinteroAssignments.Count; i++)
            {
                PatinteroAssignmentEntry value = patinteroAssignments[i];
                result.Add(new MinigameParticipantAssignment(
                    value.PlayerId,
                    membership[value.PlayerId],
                    value.Team == 0 ? MinigameTeam.TeamA : MinigameTeam.TeamB,
                    value.Role == PatinteroRole.Runner
                        ? MinigameParticipantRole.Runner
                        : MinigameParticipantRole.Guard));
            }

            assignments = result.AsReadOnly();
            return true;
        }

        private static bool IsExpectedFormat(MatchFlowSnapshot match, MinigameFormat format)
        {
            int active = 0;
            for (int i = 0; i < match.Participants.Count; i++)
            {
                if (match.Participants[i].IsContender) active++;
            }

            return (active == 4 && format == MinigameFormat.TwoVsTwo) ||
                   (active == 3 && format == MinigameFormat.OneVsThree) ||
                   (active == 2 && format == MinigameFormat.TwoVsTwo);
        }
    }
}
