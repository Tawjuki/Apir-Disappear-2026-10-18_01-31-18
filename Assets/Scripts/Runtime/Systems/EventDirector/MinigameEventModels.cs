using System;
using System.Collections.Generic;

namespace ApirApir.EventDirector
{
    public enum MinigameFormat : byte
    {
        OneVsThree,
        TwoVsTwo
    }

    public enum MinigameTeam : byte
    {
        TeamA,
        TeamB
    }

    public enum MinigameParticipantRole : byte
    {
        Taya,
        Hider,
        Runner,
        Guard
    }

    public enum ChampionshipMembership : byte
    {
        ActiveContender,
        EliminatedPlayer
    }

    public enum MinigameCompletionStatus : byte
    {
        Completed,
        Cancelled
    }

    public enum EventDirectorFailure : byte
    {
        None,
        NotAuthoritative,
        InvalidRequest,
        InvalidMatchStage,
        ParticipantsDisconnected,
        NoEligibleMinigame,
        AssignmentRejected,
        EventAlreadyRunning,
        DuplicateOrStaleEvent,
        WrongEvent,
        InvalidResult
    }

    public readonly struct MinigameEligibilityDeclaration
    {
        public MinigameEligibilityDeclaration(int activeContenders, MinigameFormat format)
        {
            ActiveContenders = activeContenders;
            Format = format;
        }

        public int ActiveContenders { get; }
        public MinigameFormat Format { get; }
    }

    public sealed class MinigameParticipantAssignment
    {
        public MinigameParticipantAssignment(
            string playerId,
            ChampionshipMembership membership,
            MinigameTeam team,
            MinigameParticipantRole role)
        {
            PlayerId = playerId ?? string.Empty;
            Membership = membership;
            Team = team;
            Role = role;
        }

        public string PlayerId { get; }
        public ChampionshipMembership Membership { get; }
        public MinigameTeam Team { get; }
        public MinigameParticipantRole Role { get; }
    }

    public sealed class MinigameEventPlan
    {
        public MinigameEventPlan(
            string matchId,
            string eventId,
            long sequence,
            string minigameId,
            MinigameFormat format,
            IReadOnlyList<MinigameParticipantAssignment> assignments)
        {
            MatchId = matchId ?? string.Empty;
            EventId = eventId ?? string.Empty;
            Sequence = sequence;
            MinigameId = minigameId ?? string.Empty;
            Format = format;
            Assignments = assignments ?? Array.Empty<MinigameParticipantAssignment>();
        }

        public string MatchId { get; }
        public string EventId { get; }
        public long Sequence { get; }
        public string MinigameId { get; }
        public MinigameFormat Format { get; }
        public IReadOnlyList<MinigameParticipantAssignment> Assignments { get; }
    }

    public readonly struct MinigameIndividualResult
    {
        public MinigameIndividualResult(string playerId, bool succeeded, int score)
        {
            PlayerId = playerId ?? string.Empty;
            Succeeded = succeeded;
            Score = score;
        }

        public string PlayerId { get; }
        public bool Succeeded { get; }
        public int Score { get; }
    }

    public sealed class MinigameResult
    {
        public MinigameResult(
            MinigameEventPlan plan,
            MinigameTeam winningTeam,
            MinigameTeam losingTeam,
            MinigameCompletionStatus status,
            IReadOnlyList<MinigameIndividualResult> individualResults)
        {
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            WinningTeam = winningTeam;
            LosingTeam = losingTeam;
            Status = status;
            IndividualResults = individualResults ?? Array.Empty<MinigameIndividualResult>();
        }

        public MinigameEventPlan Plan { get; }
        public string MatchId => Plan.MatchId;
        public string EventId => Plan.EventId;
        public string MinigameId => Plan.MinigameId;
        public MinigameTeam WinningTeam { get; }
        public MinigameTeam LosingTeam { get; }
        public MinigameCompletionStatus Status { get; }
        public IReadOnlyList<MinigameParticipantAssignment> TeamAssignments => Plan.Assignments;
        public IReadOnlyList<MinigameIndividualResult> IndividualResults { get; }
    }

    public readonly struct EventDirectorOperationResult
    {
        public EventDirectorOperationResult(bool succeeded, EventDirectorFailure failure)
        {
            Succeeded = succeeded;
            Failure = failure;
        }

        public bool Succeeded { get; }
        public EventDirectorFailure Failure { get; }

        public static EventDirectorOperationResult Success() =>
            new EventDirectorOperationResult(true, EventDirectorFailure.None);

        public static EventDirectorOperationResult Reject(EventDirectorFailure failure) =>
            new EventDirectorOperationResult(false, failure);
    }
}
