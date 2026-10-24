using System;
using System.Collections.Generic;

namespace ApirApir.Battle
{
    /// <summary>
    /// Host-side battle registry. Match flow owns controller construction and life-cycle;
    /// transports authenticate a caller and submit compact requests through this service.
    /// </summary>
    public sealed class AuthoritativeBattleService
    {
        private readonly Dictionary<string, BattleController> battles;
        private readonly HashSet<string> completedBattleIds;

        public AuthoritativeBattleService()
        {
            battles = new Dictionary<string, BattleController>(StringComparer.Ordinal);
            completedBattleIds = new HashSet<string>(StringComparer.Ordinal);
        }

        public event Action<BattleEvent> EventPublished;
        public event Action<BattleResult> BattleCompleted;

        public bool Register(BattleController controller)
        {
            if (controller == null)
            {
                throw new ArgumentNullException(nameof(controller));
            }

            if (completedBattleIds.Contains(controller.State.BattleId) ||
                !battles.TryAdd(controller.State.BattleId, controller))
            {
                return false;
            }

            controller.EventPublished += ForwardEvent;
            controller.BattleCompleted += ForwardResultOnce;
            return true;
        }

        public bool TryGetBattle(string battleId, out BattleController controller)
        {
            return battles.TryGetValue(battleId ?? string.Empty, out controller);
        }

        public ActionSubmissionResult Submit(
            BattleActionRequest request,
            string authenticatedPlayerId,
            DateTimeOffset now)
        {
            if (!battles.TryGetValue(request.BattleId, out BattleController controller))
            {
                return ActionSubmissionResult.Rejected(ActionRejectionReason.BattleNotSelecting);
            }

            BattleAction action = request.Kind == BattleActionKind.Pass
                ? BattleAction.Pass(authenticatedPlayerId, request.CommandId)
                : BattleAction.UseSkill(authenticatedPlayerId, request.CommandId, request.SkillId);
            return controller.SubmitAction(action, now);
        }

        public bool AdvanceClock(string battleId, DateTimeOffset now)
        {
            return battles.TryGetValue(battleId ?? string.Empty, out BattleController controller) &&
                   controller.AdvanceClock(now);
        }

        public bool Unregister(string battleId)
        {
            if (!battles.TryGetValue(battleId ?? string.Empty, out BattleController controller))
            {
                return false;
            }

            controller.EventPublished -= ForwardEvent;
            controller.BattleCompleted -= ForwardResultOnce;
            battles.Remove(battleId);
            return true;
        }

        private void ForwardEvent(BattleEvent battleEvent)
        {
            EventPublished?.Invoke(battleEvent);
        }

        private void ForwardResultOnce(BattleResult result)
        {
            if (completedBattleIds.Add(result.BattleId))
            {
                BattleCompleted?.Invoke(result);
            }
        }
    }
}
