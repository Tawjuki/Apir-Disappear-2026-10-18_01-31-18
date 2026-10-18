using System;

namespace ApirApir.Battle
{
    public enum BattlePhase : byte
    {
        Initializing,
        KayaKulob,
        SelectingActions,
        ResolvingActions,
        ExchangeComplete,
        BattleFinished
    }

    public enum PartSlot : byte
    {
        Head,
        Body,
        Legs
    }

    public enum PartRarity : byte
    {
        Common,
        Rare,
        Epic
    }

    public enum SkillCategory : byte
    {
        Normal,
        Soul,
        Fusion
    }

    public enum BattleActionKind : byte
    {
        Pass,
        UseSkill
    }

    public enum ActionRejectionReason : byte
    {
        None,
        NotAuthoritative,
        BattleNotSelecting,
        UnknownPlayer,
        DuplicateCommand,
        ActionAlreadySubmitted,
        MissingSkill,
        SkillNotEquipped,
        SkillOnCooldown,
        FighterDefeated
    }

    public enum KayaKulobFrequency : byte
    {
        /// <summary>The opening result remains in effect for the whole battle.</summary>
        OncePerBattle,
        /// <summary>A new authoritative result is generated before each exchange.</summary>
        EveryExchange
    }

    public enum DefeatInterruptionPolicy : byte
    {
        /// <summary>A lethal first action prevents the queued second action.</summary>
        StopRemainingActions,
        /// <summary>Both already-selected actions resolve, allowing a mutual defeat.</summary>
        ResolveCommittedActions
    }

    public enum CooldownConvention : byte
    {
        /// <summary>
        /// Provisional default: an N cooldown blocks the next N complete exchanges;
        /// the exchange that activated the skill does not decrement it.
        /// </summary>
        FullExchangesAfterUse,
        /// <summary>The activation exchange is the first exchange counted toward N.</summary>
        IncludeActivationExchange
    }

    public enum SelectionTimeoutPolicy : byte
    {
        /// <summary>Missing actions become passes when the authoritative clock expires.</summary>
        AutoPass,
        /// <summary>The battle ends without a winner when the authoritative clock expires.</summary>
        CancelBattle
    }

    public enum BattleEndReason : byte
    {
        Defeat,
        MutualDefeat,
        Stalemate,
        Cancelled,
        SelectionTimedOut
    }

    public enum BattleEventType : byte
    {
        BattleStarted,
        KayaKulobDetermined,
        ActionSelectionStarted,
        SkillSelected,
        ActionRejected,
        SkillActivated,
        DamageReceived,
        HitPointsChanged,
        PartMaintenanceChanged,
        PartDisabled,
        HealingApplied,
        StatusEffectApplied,
        TemporaryModifierApplied,
        CooldownChanged,
        ActionSkipped,
        ExchangeCompleted,
        CardDefeated,
        BattleFinished
    }

    public readonly struct BattleActionRequest
    {
        public BattleActionRequest(
            string battleId,
            string commandId,
            BattleActionKind kind,
            string skillId = null)
        {
            BattleId = battleId ?? string.Empty;
            CommandId = commandId ?? string.Empty;
            Kind = kind;
            SkillId = skillId ?? string.Empty;
        }

        public string BattleId { get; }
        public string CommandId { get; }
        public BattleActionKind Kind { get; }
        public string SkillId { get; }
    }

    public readonly struct BattleAction
    {
        private BattleAction(string playerId, string commandId, BattleActionKind kind, string skillId)
        {
            PlayerId = playerId ?? string.Empty;
            CommandId = commandId ?? string.Empty;
            Kind = kind;
            SkillId = skillId ?? string.Empty;
        }

        public string PlayerId { get; }
        public string CommandId { get; }
        public BattleActionKind Kind { get; }
        public string SkillId { get; }

        public static BattleAction Pass(string playerId, string commandId)
        {
            return new BattleAction(playerId, commandId, BattleActionKind.Pass, string.Empty);
        }

        public static BattleAction UseSkill(string playerId, string commandId, string skillId)
        {
            return new BattleAction(playerId, commandId, BattleActionKind.UseSkill, skillId);
        }
    }

    public readonly struct ActionSubmissionResult
    {
        public ActionSubmissionResult(bool accepted, ActionRejectionReason rejectionReason)
        {
            Accepted = accepted;
            RejectionReason = rejectionReason;
        }

        public bool Accepted { get; }
        public ActionRejectionReason RejectionReason { get; }

        public static ActionSubmissionResult Success()
        {
            return new ActionSubmissionResult(true, ActionRejectionReason.None);
        }

        public static ActionSubmissionResult Rejected(ActionRejectionReason reason)
        {
            return new ActionSubmissionResult(false, reason);
        }
    }

    public sealed class BattleSettings
    {
        public BattleSettings()
        {
            SelectionDuration = TimeSpan.FromSeconds(10);
            KayaKulobFrequency = KayaKulobFrequency.OncePerBattle;
            DefeatInterruptionPolicy = DefeatInterruptionPolicy.StopRemainingActions;
            CooldownConvention = CooldownConvention.FullExchangesAfterUse;
            SelectionTimeoutPolicy = SelectionTimeoutPolicy.AutoPass;
        }

        public TimeSpan SelectionDuration { get; set; }
        public KayaKulobFrequency KayaKulobFrequency { get; set; }
        public DefeatInterruptionPolicy DefeatInterruptionPolicy { get; set; }
        public CooldownConvention CooldownConvention { get; set; }
        public SelectionTimeoutPolicy SelectionTimeoutPolicy { get; set; }

        internal void Validate()
        {
            if (SelectionDuration < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(SelectionDuration));
            }
        }
    }

    public sealed class BattleEvent
    {
        public BattleEvent(
            BattleEventType type,
            string battleId,
            int exchangeNumber,
            string actorPlayerId = null,
            string targetPlayerId = null,
            string subjectId = null,
            int value = 0,
            int secondaryValue = 0,
            ActionRejectionReason rejectionReason = ActionRejectionReason.None)
        {
            Type = type;
            BattleId = battleId ?? string.Empty;
            ExchangeNumber = exchangeNumber;
            ActorPlayerId = actorPlayerId ?? string.Empty;
            TargetPlayerId = targetPlayerId ?? string.Empty;
            SubjectId = subjectId ?? string.Empty;
            Value = value;
            SecondaryValue = secondaryValue;
            RejectionReason = rejectionReason;
        }

        public BattleEventType Type { get; }
        public string BattleId { get; }
        public int ExchangeNumber { get; }
        public string ActorPlayerId { get; }
        public string TargetPlayerId { get; }
        public string SubjectId { get; }
        public int Value { get; }
        public int SecondaryValue { get; }
        public ActionRejectionReason RejectionReason { get; }
    }
}
