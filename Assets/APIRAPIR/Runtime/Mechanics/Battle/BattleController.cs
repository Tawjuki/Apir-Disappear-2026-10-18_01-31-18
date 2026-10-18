using System;
using System.Collections.Generic;

namespace ApirApir.Battle
{
    public interface IBattleAuthority
    {
        bool IsAuthoritative { get; }
    }

    public sealed class FixedBattleAuthority : IBattleAuthority
    {
        public FixedBattleAuthority(bool isAuthoritative)
        {
            IsAuthoritative = isAuthoritative;
        }

        public bool IsAuthoritative { get; }
    }

    public sealed class BattleController
    {
        private readonly BattleSettings settings;
        private readonly IBattleAuthority authority;
        private readonly CardStatCalculator statCalculator;
        private readonly KayaKulobResolver kayaKulobResolver;
        private readonly SkillCooldownSystem cooldownSystem;
        private readonly BattleResolver resolver;
        private readonly Dictionary<string, HashSet<string>> usedSkillsByPlayer;
        private bool resultPublished;

        public BattleController(
            string battleId,
            CardBattleLoadout firstLoadout,
            CardBattleLoadout secondLoadout,
            BattleSettings settings,
            IBattleAuthority authority,
            IRandomNumberProvider random,
            IDamageCalculationStrategy damageStrategy = null)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.settings.Validate();
            this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
            statCalculator = new CardStatCalculator();

            var first = new CardCombatState(firstLoadout, statCalculator);
            var second = new CardCombatState(secondLoadout, statCalculator);
            State = new BattleState(battleId, first, second);

            random = random ?? throw new ArgumentNullException(nameof(random));
            kayaKulobResolver = new KayaKulobResolver(random);
            cooldownSystem = new SkillCooldownSystem();
            var damageCalculator = new DamageCalculator(
                statCalculator,
                damageStrategy ?? new ProvisionalDamageCalculationStrategy());
            resolver = new BattleResolver(
                State,
                damageCalculator,
                new PartMaintenanceSystem(random),
                cooldownSystem,
                Publish);

            usedSkillsByPlayer = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
            {
                { State.FirstPlayerId, new HashSet<string>(StringComparer.Ordinal) },
                { State.SecondPlayerId, new HashSet<string>(StringComparer.Ordinal) }
            };
        }

        public BattleState State { get; }
        public event Action<BattleEvent> EventPublished;
        public event Action<BattleResult> BattleCompleted;

        public bool Start(DateTimeOffset now)
        {
            if (!authority.IsAuthoritative || State.Phase != BattlePhase.Initializing)
            {
                return false;
            }

            State.ExchangeNumber = 1;
            Publish(new BattleEvent(BattleEventType.BattleStarted, State.BattleId, 0));
            DetermineAttackOrder();
            BeginActionSelection(now);
            return true;
        }

        public ActionSubmissionResult SubmitAction(BattleAction action, DateTimeOffset now)
        {
            if (!authority.IsAuthoritative)
            {
                return ActionSubmissionResult.Rejected(ActionRejectionReason.NotAuthoritative);
            }

            ActionRejectionReason rejection = resolver.Validate(action);
            if (rejection != ActionRejectionReason.None)
            {
                return Reject(action, rejection);
            }

            AcceptAction(action);
            if (State.SubmittedActions.Count == 2)
            {
                ResolveExchange(now);
            }

            return ActionSubmissionResult.Success();
        }

        public bool AdvanceClock(DateTimeOffset now)
        {
            if (!authority.IsAuthoritative || State.Phase != BattlePhase.SelectingActions || now < State.SelectionDeadline)
            {
                return false;
            }

            if (settings.SelectionTimeoutPolicy == SelectionTimeoutPolicy.CancelBattle)
            {
                Finish(BattleEndReason.SelectionTimedOut, string.Empty, string.Empty);
                return true;
            }

            AutoPassMissingPlayers("timeout");
            ResolveExchange(now);
            return true;
        }

        public bool Cancel()
        {
            if (!authority.IsAuthoritative || State.Phase == BattlePhase.BattleFinished)
            {
                return false;
            }

            Finish(BattleEndReason.Cancelled, string.Empty, string.Empty);
            return true;
        }

        private void DetermineAttackOrder()
        {
            State.Phase = BattlePhase.KayaKulob;
            State.KayaPlayerId = kayaKulobResolver.ChooseKaya(State.FirstPlayerId, State.SecondPlayerId);
            State.KulobPlayerId = State.GetOpponentId(State.KayaPlayerId);
            Publish(new BattleEvent(
                BattleEventType.KayaKulobDetermined,
                State.BattleId,
                State.ExchangeNumber,
                State.KayaPlayerId,
                State.KulobPlayerId));
        }

        private void BeginActionSelection(DateTimeOffset now)
        {
            State.Phase = BattlePhase.SelectingActions;
            State.SubmittedActions.Clear();
            ClearUsedSkills();
            State.SelectionDeadline = now + settings.SelectionDuration;
            Publish(new BattleEvent(
                BattleEventType.ActionSelectionStarted,
                State.BattleId,
                State.ExchangeNumber,
                value: (int)Math.Ceiling(settings.SelectionDuration.TotalSeconds)));

            int forcedPasses = AutoPassUnavailablePlayers();
            if (forcedPasses == 2)
            {
                bool noFutureActionExists =
                    State.Fighters[State.FirstPlayerId].EquippedSkillCount == 0 &&
                    State.Fighters[State.SecondPlayerId].EquippedSkillCount == 0;
                if (noFutureActionExists)
                {
                    Publish(new BattleEvent(
                        BattleEventType.ActionSkipped,
                        State.BattleId,
                        State.ExchangeNumber,
                        State.FirstPlayerId,
                        subjectId: "no-equipped-skills"));
                    Publish(new BattleEvent(
                        BattleEventType.ActionSkipped,
                        State.BattleId,
                        State.ExchangeNumber,
                        State.SecondPlayerId,
                        subjectId: "no-equipped-skills"));
                    State.Phase = BattlePhase.ExchangeComplete;
                    Publish(new BattleEvent(
                        BattleEventType.ExchangeCompleted,
                        State.BattleId,
                        State.ExchangeNumber));
                    Finish(BattleEndReason.Stalemate, string.Empty, string.Empty);
                }
                else
                {
                    ResolveExchange(now);
                }
            }
        }

        private int AutoPassUnavailablePlayers()
        {
            int count = 0;
            count += AutoPassIfUnavailable(State.FirstPlayerId) ? 1 : 0;
            count += AutoPassIfUnavailable(State.SecondPlayerId) ? 1 : 0;
            return count;
        }

        private bool AutoPassIfUnavailable(string playerId)
        {
            CardCombatState fighter = State.Fighters[playerId];
            if (fighter.HasAnyAvailableSkill())
            {
                return false;
            }

            State.SubmittedActions[playerId] = BattleAction.Pass(playerId, "auto-unavailable-" + State.ExchangeNumber);
            return true;
        }

        private void AutoPassMissingPlayers(string reason)
        {
            AutoPassMissingPlayer(State.FirstPlayerId, reason);
            AutoPassMissingPlayer(State.SecondPlayerId, reason);
        }

        private void AutoPassMissingPlayer(string playerId, string reason)
        {
            if (State.SubmittedActions.ContainsKey(playerId))
            {
                return;
            }

            State.SubmittedActions[playerId] = BattleAction.Pass(playerId, "auto-" + reason + "-" + State.ExchangeNumber);
            Publish(new BattleEvent(
                BattleEventType.ActionSkipped,
                State.BattleId,
                State.ExchangeNumber,
                playerId,
                subjectId: reason));
        }

        private void AcceptAction(BattleAction action)
        {
            State.SubmittedActions.Add(action.PlayerId, action);
            BattleEventType type = action.Kind == BattleActionKind.UseSkill
                ? BattleEventType.SkillSelected
                : BattleEventType.ActionSkipped;
            Publish(new BattleEvent(
                type,
                State.BattleId,
                State.ExchangeNumber,
                action.PlayerId,
                subjectId: action.SkillId));
        }

        private ActionSubmissionResult Reject(BattleAction action, ActionRejectionReason reason)
        {
            Publish(new BattleEvent(
                BattleEventType.ActionRejected,
                State.BattleId,
                State.ExchangeNumber,
                action.PlayerId,
                subjectId: action.SkillId,
                rejectionReason: reason));
            return ActionSubmissionResult.Rejected(reason);
        }

        private void ResolveExchange(DateTimeOffset now)
        {
            if (State.Phase != BattlePhase.SelectingActions || State.SubmittedActions.Count != 2)
            {
                return;
            }

            State.Phase = BattlePhase.ResolvingActions;
            ResolvePlayerAction(State.KayaPlayerId);

            bool someoneDefeated = AnyFighterDefeated();
            if (!someoneDefeated || settings.DefeatInterruptionPolicy == DefeatInterruptionPolicy.ResolveCommittedActions)
            {
                ResolvePlayerAction(State.KulobPlayerId);
            }
            else
            {
                Publish(new BattleEvent(
                    BattleEventType.ActionSkipped,
                    State.BattleId,
                    State.ExchangeNumber,
                    State.KulobPlayerId,
                    subjectId: "interrupted-by-defeat"));
            }

            CompleteExchange(now);
        }

        private void ResolvePlayerAction(string playerId)
        {
            resolver.ResolveAction(State.SubmittedActions[playerId], usedSkillsByPlayer[playerId]);
        }

        private void CompleteExchange(DateTimeOffset now)
        {
            State.Phase = BattlePhase.ExchangeComplete;
            AdvanceCooldowns(State.FirstPlayerId);
            AdvanceCooldowns(State.SecondPlayerId);
            State.Fighters[State.FirstPlayerId].AdvanceTimedEffects();
            State.Fighters[State.SecondPlayerId].AdvanceTimedEffects();
            Publish(new BattleEvent(
                BattleEventType.ExchangeCompleted,
                State.BattleId,
                State.ExchangeNumber));

            if (TryFinishDefeat())
            {
                return;
            }

            State.ExchangeNumber++;
            if (settings.KayaKulobFrequency == KayaKulobFrequency.EveryExchange)
            {
                DetermineAttackOrder();
            }

            BeginActionSelection(now);
        }

        private void AdvanceCooldowns(string playerId)
        {
            CardCombatState fighter = State.Fighters[playerId];
            var changedSkills = new HashSet<string>(StringComparer.Ordinal);
            cooldownSystem.AdvanceAfterExchange(
                fighter,
                usedSkillsByPlayer[playerId],
                settings.CooldownConvention,
                (skillId, remaining) =>
                {
                    changedSkills.Add(skillId);
                    Publish(new BattleEvent(
                        BattleEventType.CooldownChanged,
                        State.BattleId,
                        State.ExchangeNumber,
                        playerId,
                        subjectId: skillId,
                        value: remaining));
                });

            foreach (string skillId in usedSkillsByPlayer[playerId])
            {
                int remaining = fighter.GetCooldown(skillId);
                if (remaining > 0 && !changedSkills.Contains(skillId))
                {
                    Publish(new BattleEvent(
                        BattleEventType.CooldownChanged,
                        State.BattleId,
                        State.ExchangeNumber,
                        playerId,
                        subjectId: skillId,
                        value: remaining));
                }
            }
        }

        private bool TryFinishDefeat()
        {
            CardCombatState first = State.Fighters[State.FirstPlayerId];
            CardCombatState second = State.Fighters[State.SecondPlayerId];
            if (!first.IsDefeated && !second.IsDefeated)
            {
                return false;
            }

            if (first.IsDefeated && second.IsDefeated)
            {
                Finish(BattleEndReason.MutualDefeat, string.Empty, string.Empty);
            }
            else if (first.IsDefeated)
            {
                Finish(BattleEndReason.Defeat, second.PlayerId, first.PlayerId);
            }
            else
            {
                Finish(BattleEndReason.Defeat, first.PlayerId, second.PlayerId);
            }

            return true;
        }

        private bool AnyFighterDefeated()
        {
            return State.Fighters[State.FirstPlayerId].IsDefeated ||
                   State.Fighters[State.SecondPlayerId].IsDefeated;
        }

        private void Finish(BattleEndReason reason, string winnerId, string loserId)
        {
            if (resultPublished)
            {
                return;
            }

            resultPublished = true;
            CardCombatState first = State.Fighters[State.FirstPlayerId];
            CardCombatState second = State.Fighters[State.SecondPlayerId];
            first.FinalizeAfterBattle(statCalculator);
            second.FinalizeAfterBattle(statCalculator);
            State.Phase = BattlePhase.BattleFinished;
            State.Result = new BattleResult(
                State.BattleId,
                reason,
                winnerId,
                loserId,
                State.ExchangeNumber,
                first,
                second);
            Publish(new BattleEvent(
                BattleEventType.BattleFinished,
                State.BattleId,
                State.ExchangeNumber,
                winnerId,
                loserId,
                reason.ToString()));
            BattleCompleted?.Invoke(State.Result);
        }

        private void ClearUsedSkills()
        {
            usedSkillsByPlayer[State.FirstPlayerId].Clear();
            usedSkillsByPlayer[State.SecondPlayerId].Clear();
        }

        private void Publish(BattleEvent battleEvent)
        {
            EventPublished?.Invoke(battleEvent);
        }
    }
}
