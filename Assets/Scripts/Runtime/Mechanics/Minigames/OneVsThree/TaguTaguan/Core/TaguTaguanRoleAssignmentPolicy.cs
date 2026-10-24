using System;
using System.Collections.Generic;
using ApirApir.EventDirector;
using ApirApir.MatchFlow;

namespace ApirApir.TaguTaguan
{
    /// <summary>Creates Tagu-taguan's authoritative 1v3 Taya/Hider assignments.</summary>
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
            MatchParticipantSnapshot eliminated = null;
            for (int i = 0; i < match.Participants.Count; i++)
            {
                MatchParticipantSnapshot participant = match.Participants[i];
                if (!participant.IsConnected)
                {
                    return false;
                }

                if (participant.IsContender)
                {
                    contenders.Add(participant);
                }
                else if (eliminated == null)
                {
                    eliminated = participant;
                }
                else
                {
                    return false;
                }
            }

            if (contenders.Count != 4 && (contenders.Count != 3 || eliminated == null))
            {
                return false;
            }

            string tayaPlayerId = contenders.Count == 4
                ? contenders[new Random(randomSeed).Next(contenders.Count)].PlayerId
                : eliminated.PlayerId;
            var result = new List<MinigameParticipantAssignment>(4);
            for (int i = 0; i < match.Participants.Count; i++)
            {
                MatchParticipantSnapshot participant = match.Participants[i];
                bool isTaya = string.Equals(participant.PlayerId, tayaPlayerId, StringComparison.Ordinal);
                result.Add(new MinigameParticipantAssignment(
                    participant.PlayerId,
                    participant.IsContender
                        ? ChampionshipMembership.ActiveContender
                        : ChampionshipMembership.EliminatedPlayer,
                    isTaya ? MinigameTeam.TeamB : MinigameTeam.TeamA,
                    isTaya ? MinigameParticipantRole.Taya : MinigameParticipantRole.Hider));
            }

            assignments = result.AsReadOnly();
            return true;
        }
    }
}
