using System;
using System.Collections.Generic;
using ApirApir.PlayerProfiles;

namespace ApirApir.MatchFlow
{
    /// <summary>
    /// Owns championship lives and phase transitions. Eliminated players remain participants;
    /// their connection state is independent from championship eligibility.
    /// </summary>
    public sealed class AuthoritativeMatchFlow : ICompletedMatchResultSource
    {
        public const int RequiredHumanPlayers = 4;
        public const int DefaultStartingLives = 5;
        public const int RequiredFinalMinigames = 2;

        private sealed class ParticipantState
        {
            public ParticipantState(string playerId, int lives)
            {
                PlayerId = playerId;
                Lives = lives;
                IsConnected = true;
            }

            public string PlayerId;
            public int Lives;
            public bool IsConnected;
        }

        private readonly Dictionary<string, ParticipantState> participants =
            new Dictionary<string, ParticipantState>(StringComparer.Ordinal);
        private readonly List<string> participantOrder = new List<string>(RequiredHumanPlayers);
        private readonly HashSet<string> processedCommands =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> completedFinalMinigameEvents =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly bool isAuthoritative;
        private string runningFinalMinigameEventId = string.Empty;
        private bool finalRecoveryApplied;

        public AuthoritativeMatchFlow(
            string matchId,
            IEnumerable<string> playerIds,
            bool isAuthoritative,
            int startingLives = DefaultStartingLives)
        {
            MatchId = NormalizeRequired(matchId, nameof(matchId));
            if (startingLives <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(startingLives));
            }

            if (playerIds == null)
            {
                throw new ArgumentNullException(nameof(playerIds));
            }

            foreach (string value in playerIds)
            {
                string playerId = NormalizeRequired(value, nameof(playerIds));
                if (participants.ContainsKey(playerId))
                {
                    throw new ArgumentException("Player IDs must be unique.", nameof(playerIds));
                }

                participants.Add(playerId, new ParticipantState(playerId, startingLives));
                participantOrder.Add(playerId);
            }

            if (participantOrder.Count != RequiredHumanPlayers)
            {
                throw new ArgumentException("APIR APIR matches require exactly four human players.", nameof(playerIds));
            }

            this.isAuthoritative = isAuthoritative;
        }

        public event Action<MatchFlowSnapshot> StateChanged;
        public event Action<CompletedMatchResult> MatchCompleted;

        public string MatchId { get; }
        public MatchPhase Phase { get; private set; } = MatchPhase.StandardCompetition;
        public int CompletedFinalMinigames { get; private set; }
        public int ActiveContenderCount => CountParticipants(true);
        public int EliminatedPlayerCount => RequiredHumanPlayers - ActiveContenderCount;
        public int ConnectedParticipantCount => CountConnectedParticipants();

        public MatchFlowSnapshot CreateSnapshot()
        {
            var snapshots = new List<MatchParticipantSnapshot>(RequiredHumanPlayers);
            for (int i = 0; i < participantOrder.Count; i++)
            {
                ParticipantState participant = participants[participantOrder[i]];
                snapshots.Add(new MatchParticipantSnapshot(
                    participant.PlayerId,
                    participant.Lives,
                    participant.IsConnected));
            }

            return new MatchFlowSnapshot(
                MatchId,
                Phase,
                snapshots.AsReadOnly(),
                CompletedFinalMinigames,
                runningFinalMinigameEventId,
                finalRecoveryApplied);
        }

        public MatchFlowOperationResult SetConnectionState(string playerId, bool connected)
        {
            if (!isAuthoritative)
            {
                return MatchFlowOperationResult.Reject(MatchFlowFailure.NotAuthoritative);
            }

            if (!TryGetParticipant(playerId, out ParticipantState participant))
            {
                return MatchFlowOperationResult.Reject(MatchFlowFailure.PlayerNotFound);
            }

            participant.IsConnected = connected;
            PublishState();
            return MatchFlowOperationResult.Success();
        }

        public MatchFlowOperationResult ApplyStandardBattleLoss(string commandId, string losingPlayerId)
        {
            MatchFlowOperationResult commandValidation = ValidateNewCommand(commandId);
            if (!commandValidation.Succeeded)
            {
                return commandValidation;
            }

            if (Phase != MatchPhase.StandardCompetition || ActiveContenderCount <= 2)
            {
                return MatchFlowOperationResult.Reject(MatchFlowFailure.InvalidPhase);
            }

            if (!TryGetParticipant(losingPlayerId, out ParticipantState participant))
            {
                return MatchFlowOperationResult.Reject(MatchFlowFailure.PlayerNotFound);
            }

            if (participant.Lives <= 0)
            {
                return MatchFlowOperationResult.Reject(MatchFlowFailure.PlayerAlreadyEliminated);
            }

            processedCommands.Add(commandId.Trim());
            participant.Lives--;
            if (ActiveContenderCount == 2)
            {
                Phase = MatchPhase.ChampionshipMinigames;
            }

            PublishState();
            return MatchFlowOperationResult.Success();
        }

        public MatchFlowOperationResult BeginFinalMinigame(string eventId)
        {
            MatchFlowOperationResult commandValidation = ValidateNewCommand(eventId);
            if (!commandValidation.Succeeded)
            {
                return commandValidation;
            }

            if (Phase != MatchPhase.ChampionshipMinigames || ActiveContenderCount != 2)
            {
                return MatchFlowOperationResult.Reject(MatchFlowFailure.InvalidPhase);
            }

            if (CompletedFinalMinigames >= RequiredFinalMinigames)
            {
                return MatchFlowOperationResult.Reject(MatchFlowFailure.FinalMinigameLimitReached);
            }

            if (runningFinalMinigameEventId.Length > 0)
            {
                return MatchFlowOperationResult.Reject(MatchFlowFailure.FinalMinigameAlreadyRunning);
            }

            runningFinalMinigameEventId = eventId.Trim();
            processedCommands.Add(runningFinalMinigameEventId);
            PublishState();
            return MatchFlowOperationResult.Success();
        }

        public MatchFlowOperationResult CompleteFinalMinigame(string eventId)
        {
            if (!isAuthoritative)
            {
                return MatchFlowOperationResult.Reject(MatchFlowFailure.NotAuthoritative);
            }

            string normalized = Normalize(eventId);
            if (normalized.Length == 0 ||
                !string.Equals(runningFinalMinigameEventId, normalized, StringComparison.Ordinal))
            {
                return MatchFlowOperationResult.Reject(MatchFlowFailure.WrongFinalMinigame);
            }

            if (!completedFinalMinigameEvents.Add(normalized))
            {
                return MatchFlowOperationResult.Reject(MatchFlowFailure.DuplicateCommand);
            }

            runningFinalMinigameEventId = string.Empty;
            CompletedFinalMinigames++;
            if (CompletedFinalMinigames == RequiredFinalMinigames)
            {
                Phase = MatchPhase.FinalTyangge;
            }

            PublishState();
            return MatchFlowOperationResult.Success();
        }

        public MatchFlowOperationResult CompleteFinalTyangge(string commandId)
        {
            return AdvancePhase(commandId, MatchPhase.FinalTyangge, MatchPhase.FinalPreparation);
        }

        public MatchFlowOperationResult ApplyFinalRoundRecovery(string commandId)
        {
            MatchFlowOperationResult validation = ValidateNewCommand(commandId);
            if (!validation.Succeeded)
            {
                return validation;
            }

            if (Phase != MatchPhase.FinalPreparation)
            {
                return MatchFlowOperationResult.Reject(MatchFlowFailure.InvalidPhase);
            }

            processedCommands.Add(commandId.Trim());
            finalRecoveryApplied = true;
            PublishState();
            return MatchFlowOperationResult.Success();
        }

        public MatchFlowOperationResult CompleteFinalPreparation(string commandId)
        {
            if (!finalRecoveryApplied)
            {
                return MatchFlowOperationResult.Reject(MatchFlowFailure.InvalidPhase);
            }

            return AdvancePhase(commandId, MatchPhase.FinalPreparation, MatchPhase.ChampionshipBattle);
        }

        public MatchFlowOperationResult CompleteChampionshipBattle(
            string commandId,
            string winnerPlayerId,
            string loserPlayerId)
        {
            MatchFlowOperationResult validation = ValidatePlacementCommand(
                commandId, winnerPlayerId, loserPlayerId, true, MatchPhase.ChampionshipBattle);
            if (!validation.Succeeded)
            {
                return validation;
            }

            processedCommands.Add(commandId.Trim());
            FirstPlacePlayerId = winnerPlayerId.Trim();
            SecondPlacePlayerId = loserPlayerId.Trim();
            Phase = MatchPhase.ConsolationBattle;
            PublishState();
            return MatchFlowOperationResult.Success();
        }

        public MatchFlowOperationResult CompleteConsolationBattle(
            string commandId,
            string winnerPlayerId,
            string loserPlayerId)
        {
            MatchFlowOperationResult validation = ValidatePlacementCommand(
                commandId, winnerPlayerId, loserPlayerId, false, MatchPhase.ConsolationBattle);
            if (!validation.Succeeded)
            {
                return validation;
            }

            processedCommands.Add(commandId.Trim());
            ThirdPlacePlayerId = winnerPlayerId.Trim();
            FourthPlacePlayerId = loserPlayerId.Trim();
            Phase = MatchPhase.Completed;
            PublishState();
            MatchCompleted?.Invoke(new CompletedMatchResult(
                MatchId,
                true,
                new[]
                {
                    new PlayerFinalPlacement(FirstPlacePlayerId, FinalMatchPlacement.First),
                    new PlayerFinalPlacement(SecondPlacePlayerId, FinalMatchPlacement.Second),
                    new PlayerFinalPlacement(ThirdPlacePlayerId, FinalMatchPlacement.Third),
                    new PlayerFinalPlacement(FourthPlacePlayerId, FinalMatchPlacement.Fourth)
                }));
            return MatchFlowOperationResult.Success();
        }

        public string FirstPlacePlayerId { get; private set; } = string.Empty;
        public string SecondPlacePlayerId { get; private set; } = string.Empty;
        public string ThirdPlacePlayerId { get; private set; } = string.Empty;
        public string FourthPlacePlayerId { get; private set; } = string.Empty;

        private MatchFlowOperationResult AdvancePhase(
            string commandId,
            MatchPhase expected,
            MatchPhase next)
        {
            MatchFlowOperationResult validation = ValidateNewCommand(commandId);
            if (!validation.Succeeded)
            {
                return validation;
            }

            if (Phase != expected)
            {
                return MatchFlowOperationResult.Reject(MatchFlowFailure.InvalidPhase);
            }

            processedCommands.Add(commandId.Trim());
            Phase = next;
            PublishState();
            return MatchFlowOperationResult.Success();
        }

        private MatchFlowOperationResult ValidatePlacementCommand(
            string commandId,
            string winnerPlayerId,
            string loserPlayerId,
            bool requireContenders,
            MatchPhase expectedPhase)
        {
            MatchFlowOperationResult validation = ValidateNewCommand(commandId);
            if (!validation.Succeeded)
            {
                return validation;
            }

            if (Phase != expectedPhase ||
                !TryGetParticipant(winnerPlayerId, out ParticipantState winner) ||
                !TryGetParticipant(loserPlayerId, out ParticipantState loser) ||
                ReferenceEquals(winner, loser) ||
                (winner.Lives > 0) != requireContenders ||
                (loser.Lives > 0) != requireContenders)
            {
                return MatchFlowOperationResult.Reject(MatchFlowFailure.InvalidPlacement);
            }

            return MatchFlowOperationResult.Success();
        }

        private MatchFlowOperationResult ValidateNewCommand(string commandId)
        {
            if (!isAuthoritative)
            {
                return MatchFlowOperationResult.Reject(MatchFlowFailure.NotAuthoritative);
            }

            string normalized = Normalize(commandId);
            if (normalized.Length == 0)
            {
                return MatchFlowOperationResult.Reject(MatchFlowFailure.InvalidRequest);
            }

            return processedCommands.Contains(normalized)
                ? MatchFlowOperationResult.Reject(MatchFlowFailure.DuplicateCommand)
                : MatchFlowOperationResult.Success();
        }

        private bool TryGetParticipant(string playerId, out ParticipantState participant)
        {
            return participants.TryGetValue(Normalize(playerId), out participant);
        }

        private int CountParticipants(bool contenders)
        {
            int count = 0;
            foreach (ParticipantState participant in participants.Values)
            {
                if ((participant.Lives > 0) == contenders)
                {
                    count++;
                }
            }

            return count;
        }

        private int CountConnectedParticipants()
        {
            int count = 0;
            foreach (ParticipantState participant in participants.Values)
            {
                if (participant.IsConnected)
                {
                    count++;
                }
            }

            return count;
        }

        private void PublishState()
        {
            StateChanged?.Invoke(CreateSnapshot());
        }

        private static string Normalize(string value) => value == null ? string.Empty : value.Trim();

        private static string NormalizeRequired(string value, string parameterName)
        {
            string normalized = Normalize(value);
            return normalized.Length > 0
                ? normalized
                : throw new ArgumentException("A non-empty identifier is required.", parameterName);
        }
    }
}
