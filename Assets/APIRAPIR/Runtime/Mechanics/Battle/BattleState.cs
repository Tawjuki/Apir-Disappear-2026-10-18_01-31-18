using System;
using System.Collections.Generic;

namespace ApirApir.Battle
{
    public sealed class BattleState
    {
        private readonly Dictionary<string, CardCombatState> fighters;
        private readonly Dictionary<string, BattleAction> submittedActions;
        private readonly HashSet<string> processedCommands;

        internal BattleState(string battleId, CardCombatState first, CardCombatState second)
        {
            if (string.IsNullOrWhiteSpace(battleId))
            {
                throw new ArgumentException("A battle requires a unique ID.", nameof(battleId));
            }

            if (first == null || second == null)
            {
                throw new ArgumentNullException(first == null ? nameof(first) : nameof(second));
            }

            if (string.Equals(first.PlayerId, second.PlayerId, StringComparison.Ordinal))
            {
                throw new ArgumentException("A battle requires two distinct players.");
            }

            BattleId = battleId;
            FirstPlayerId = first.PlayerId;
            SecondPlayerId = second.PlayerId;
            fighters = new Dictionary<string, CardCombatState>(StringComparer.Ordinal)
            {
                { first.PlayerId, first },
                { second.PlayerId, second }
            };
            submittedActions = new Dictionary<string, BattleAction>(StringComparer.Ordinal);
            processedCommands = new HashSet<string>(StringComparer.Ordinal);
            Phase = BattlePhase.Initializing;
        }

        public string BattleId { get; }
        public string FirstPlayerId { get; }
        public string SecondPlayerId { get; }
        public string KayaPlayerId { get; internal set; }
        public string KulobPlayerId { get; internal set; }
        public BattlePhase Phase { get; internal set; }
        public int ExchangeNumber { get; internal set; }
        public DateTimeOffset SelectionDeadline { get; internal set; }
        public BattleResult Result { get; internal set; }
        public IReadOnlyDictionary<string, CardCombatState> Fighters => fighters;

        internal Dictionary<string, BattleAction> SubmittedActions => submittedActions;
        internal HashSet<string> ProcessedCommands => processedCommands;

        public bool TryGetFighter(string playerId, out CardCombatState fighter)
        {
            return fighters.TryGetValue(playerId ?? string.Empty, out fighter);
        }

        public string GetOpponentId(string playerId)
        {
            if (string.Equals(playerId, FirstPlayerId, StringComparison.Ordinal))
            {
                return SecondPlayerId;
            }

            if (string.Equals(playerId, SecondPlayerId, StringComparison.Ordinal))
            {
                return FirstPlayerId;
            }

            return string.Empty;
        }
    }

    public sealed class PartConditionResult
    {
        public PartConditionResult(string partId, PartSlot slot, int maintenance, bool isFunctional)
        {
            PartId = partId ?? string.Empty;
            Slot = slot;
            Maintenance = maintenance;
            IsFunctional = isFunctional;
        }

        public string PartId { get; }
        public PartSlot Slot { get; }
        public int Maintenance { get; }
        public bool IsFunctional { get; }
    }

    public sealed class FighterBattleResult
    {
        internal FighterBattleResult(CardCombatState fighter)
        {
            PlayerId = fighter.PlayerId;
            CardId = fighter.CardId;
            FinalHitPoints = fighter.CurrentHitPoints;
            FinalMaximumHitPoints = fighter.MaximumHitPoints;
            var conditions = new PartConditionResult[fighter.Parts.Count];
            for (int i = 0; i < conditions.Length; i++)
            {
                PartCombatState part = fighter.Parts[i];
                conditions[i] = new PartConditionResult(
                    part.Definition.Id,
                    part.Definition.Slot,
                    part.CurrentMaintenance,
                    part.IsFunctional);
            }

            PartConditions = conditions;
        }

        public string PlayerId { get; }
        public string CardId { get; }
        public int FinalHitPoints { get; }
        public int FinalMaximumHitPoints { get; }
        public IReadOnlyList<PartConditionResult> PartConditions { get; }
    }

    public sealed class BattleResult
    {
        internal BattleResult(
            string battleId,
            BattleEndReason endReason,
            string winningPlayerId,
            string losingPlayerId,
            int completedExchanges,
            CardCombatState first,
            CardCombatState second)
        {
            BattleId = battleId;
            EndReason = endReason;
            WinningPlayerId = winningPlayerId ?? string.Empty;
            LosingPlayerId = losingPlayerId ?? string.Empty;
            CompletedExchanges = completedExchanges;
            FirstFighter = new FighterBattleResult(first);
            SecondFighter = new FighterBattleResult(second);
        }

        public string BattleId { get; }
        public BattleEndReason EndReason { get; }
        public string WinningPlayerId { get; }
        public string LosingPlayerId { get; }
        public bool IsDraw => string.IsNullOrEmpty(WinningPlayerId);
        public int CompletedExchanges { get; }
        public FighterBattleResult FirstFighter { get; }
        public FighterBattleResult SecondFighter { get; }
    }
}
