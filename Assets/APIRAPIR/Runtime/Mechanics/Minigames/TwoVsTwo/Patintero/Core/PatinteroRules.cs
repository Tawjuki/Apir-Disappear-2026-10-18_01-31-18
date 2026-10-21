using UnityEngine;

namespace ApirApir.Patintero
{
    public static class PatinteroRules
    {
        public static Vector3 ConstrainToCourt(
            Vector3 position,
            PatinteroRole role,
            float guardLineZ,
            PatinteroSettings settings)
        {
            if (settings == null)
            {
                return position;
            }

            float minX = settings.MinimumX + settings.playerRadius;
            float maxX = settings.MaximumX - settings.playerRadius;
            position.x = Mathf.Clamp(position.x, minX, maxX);

            if (role == PatinteroRole.Taya)
            {
                position.z = guardLineZ;
            }
            else
            {
                float minZ = settings.MinimumZ + settings.playerRadius;
                float maxZ = settings.MaximumZ - settings.playerRadius;
                position.z = Mathf.Clamp(position.z, minZ, maxZ);
            }

            return position;
        }

        public static Vector3 MoveAndConstrain(
            Vector3 current,
            Vector2 input,
            float deltaTime,
            PatinteroRole role,
            float guardLineZ,
            PatinteroSettings settings)
        {
            input = Vector2.ClampMagnitude(input, 1f);
            float speed = role == PatinteroRole.Taya ? settings.tayaSpeed : settings.runnerSpeed;
            Vector3 result = current + new Vector3(input.x, 0f, input.y) * (speed * Mathf.Max(0f, deltaTime));

            result = ConstrainToCourt(result, role, guardLineZ, settings);
            result.y = current.y;
            return result;
        }

        public static RunnerProgress AdvanceRunner(RunnerProgress current, float z, PatinteroSettings settings)
        {
            if (current == RunnerProgress.Outbound && z >= settings.FarThresholdZ)
            {
                return RunnerProgress.Returning;
            }

            if (current == RunnerProgress.Returning && z <= settings.StartThresholdZ)
            {
                return RunnerProgress.Completed;
            }

            return current;
        }

        public static bool IsTag(Vector3 tayaPosition, Vector3 runnerPosition, float tagRadius)
        {
            tayaPosition.y = 0f;
            runnerPosition.y = 0f;
            return (tayaPosition - runnerPosition).sqrMagnitude <= tagRadius * tagRadius;
        }

        public static PatinteroResult EvaluateResult(
            int activeRunners,
            int completedRunners,
            int requiredCompletions,
            bool timeExpired)
        {
            if (completedRunners >= Mathf.Max(1, requiredCompletions))
            {
                return PatinteroResult.RunnersWin;
            }

            return activeRunners <= 0 || timeExpired ? PatinteroResult.TayaWins : PatinteroResult.None;
        }

        public static PatinteroResult EvaluateResult(int activeRunners, bool anyCompleted, bool timeExpired) =>
            EvaluateResult(activeRunners, anyCompleted ? 1 : 0, 1, timeExpired);
    }
}
