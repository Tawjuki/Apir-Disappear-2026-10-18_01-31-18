using System;
using System.Collections.Generic;

namespace ApirApir.MatchFlow
{
    public enum MatchPhase : byte
    {
        StandardCompetition,
        ChampionshipMinigames,
        FinalTyangge,
        FinalPreparation,
        ChampionshipBattle,
        ConsolationBattle,
        Completed
    }

    public enum MatchFlowFailure : byte
    {
        None,
        NotAuthoritative,
        InvalidRequest,
        PlayerNotFound,
        PlayerAlreadyEliminated,
        InvalidPhase,
        DuplicateCommand,
        FinalMinigameAlreadyRunning,
        FinalMinigameLimitReached,
        WrongFinalMinigame,
        InvalidPlacement
    }

    public sealed class MatchParticipantSnapshot
    {
        public MatchParticipantSnapshot(string playerId, int championshipLives, bool isConnected)
        {
            PlayerId = playerId ?? string.Empty;
            ChampionshipLives = Math.Max(0, championshipLives);
            IsConnected = isConnected;
        }

        public string PlayerId { get; }
        public int ChampionshipLives { get; }
        public bool IsConnected { get; }
        public bool IsContender => ChampionshipLives > 0;
        public bool IsEliminated => ChampionshipLives == 0;
    }

    public readonly struct MatchFlowOperationResult
    {
        public MatchFlowOperationResult(bool succeeded, MatchFlowFailure failure)
        {
            Succeeded = succeeded;
            Failure = failure;
        }

        public bool Succeeded { get; }
        public MatchFlowFailure Failure { get; }

        public static MatchFlowOperationResult Success() =>
            new MatchFlowOperationResult(true, MatchFlowFailure.None);

        public static MatchFlowOperationResult Reject(MatchFlowFailure failure) =>
            new MatchFlowOperationResult(false, failure);
    }

    public sealed class MatchFlowSnapshot
    {
        public MatchFlowSnapshot(
            string matchId,
            MatchPhase phase,
            IReadOnlyList<MatchParticipantSnapshot> participants,
            int completedFinalMinigames,
            string runningFinalMinigameEventId,
            bool finalRecoveryApplied)
        {
            MatchId = matchId ?? string.Empty;
            Phase = phase;
            Participants = participants ?? Array.Empty<MatchParticipantSnapshot>();
            CompletedFinalMinigames = completedFinalMinigames;
            RunningFinalMinigameEventId = runningFinalMinigameEventId ?? string.Empty;
            FinalRecoveryApplied = finalRecoveryApplied;
        }

        public string MatchId { get; }
        public MatchPhase Phase { get; }
        public IReadOnlyList<MatchParticipantSnapshot> Participants { get; }
        public int CompletedFinalMinigames { get; }
        public string RunningFinalMinigameEventId { get; }
        public bool FinalRecoveryApplied { get; }
    }
}
