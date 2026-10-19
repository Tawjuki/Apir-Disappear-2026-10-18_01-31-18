using System;
using System.Collections.Generic;
using ApirApir.EventDirector;
using ApirApir.MatchFlow;

namespace ApirApir.TaguTaguan
{
    /// <summary>Tagu-taguan owns only its four-contender 1v3 Taya/Hider policy.</summary>
    public sealed class TaguTaguanRoleAssignmentPolicy : IMinigameAssignmentPolicy
    {
        public bool TryCreateAssignments(
            MatchFlowSnapshot match,
            MinigameFormat format,
            int randomSeed,
            out IReadOnlyList<MinigameParticipantAssignment> assignments)
        {
            assignments = Array.Empty<MinigameParticipantAssignment>();
            if (match == null || format != MinigameFormat.OneVsThree ||
                match.Participants.Count != AuthoritativeMatchFlow.RequiredHumanPlayers)
            {
                return false;
            }

            var contenders = new List<MatchParticipantSnapshot>(4);
            for (int i = 0; i < match.Participants.Count; i++)
            {
                if (!match.Participants[i].IsContender)
                {
                    return false;
                }

                contenders.Add(match.Participants[i]);
            }

            int tayaIndex = new Random(randomSeed).Next(contenders.Count);
            var result = new List<MinigameParticipantAssignment>(4);
            for (int i = 0; i < contenders.Count; i++)
            {
                bool isTaya = i == tayaIndex;
                result.Add(new MinigameParticipantAssignment(
                    contenders[i].PlayerId,
                    ChampionshipMembership.ActiveContender,
                    isTaya ? MinigameTeam.TeamB : MinigameTeam.TeamA,
                    isTaya ? MinigameParticipantRole.Taya : MinigameParticipantRole.Hider));
            }

            assignments = result.AsReadOnly();
            return true;
        }
    }
}
