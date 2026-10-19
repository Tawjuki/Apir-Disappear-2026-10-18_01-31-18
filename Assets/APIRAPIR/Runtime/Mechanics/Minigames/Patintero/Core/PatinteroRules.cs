using UnityEngine;

namespace ApirApir.Patintero
{
    public static class PatinteroRules
    {
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

            float minX = -settings.HalfWidth + settings.playerRadius;
            float maxX = settings.HalfWidth - settings.playerRadius;
            result.x = Mathf.Clamp(result.x, minX, maxX);

            if (role == PatinteroRole.Taya)
            {
                result.z = guardLineZ;
            }
            else
            {
                float minZ = -settings.HalfLength + settings.playerRadius;
                float maxZ = settings.HalfLength - settings.playerRadius;
                result.z = Mathf.Clamp(result.z, minZ, maxZ);
            }

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

        public static PatinteroResult EvaluateResult(int activeRunners, bool anyCompleted, bool timeExpired)
        {
            if (anyCompleted)
            {
                return PatinteroResult.RunnersWin;
            }

            return activeRunners <= 0 || timeExpired ? PatinteroResult.TayaWins : PatinteroResult.None;
        }
    }
}
