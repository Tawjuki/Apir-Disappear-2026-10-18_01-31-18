using UnityEngine;

namespace ApirApir.Minigames
{
    public static class MinigameMovementMath
    {
        private const float DirectionEpsilon = 0.0001f;

        public static Vector2 ToCameraRelative(Vector2 input, Transform cameraTransform)
        {
            input = Vector2.ClampMagnitude(input, 1f);
            if (cameraTransform == null || input.sqrMagnitude <= DirectionEpsilon)
            {
                return input;
            }

            Vector3 forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);
            if (forward.sqrMagnitude <= DirectionEpsilon)
            {
                forward = Vector3.ProjectOnPlane(cameraTransform.up, Vector3.up);
            }

            Vector3 right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up);
            if (forward.sqrMagnitude <= DirectionEpsilon || right.sqrMagnitude <= DirectionEpsilon)
            {
                return input;
            }

            forward.Normalize();
            right.Normalize();
            Vector3 worldDirection = right * input.x + forward * input.y;
            worldDirection = Vector3.ClampMagnitude(worldDirection, 1f);
            return new Vector2(worldDirection.x, worldDirection.z);
        }

        public static Vector2 MoveVelocityTowards(
            Vector2 currentVelocity,
            Vector2 input,
            float speed,
            float acceleration,
            float deceleration,
            float deltaTime)
        {
            input = Vector2.ClampMagnitude(input, 1f);
            Vector2 targetVelocity = input * Mathf.Max(0f, speed);
            float response = input.sqrMagnitude > DirectionEpsilon ? acceleration : deceleration;
            return Vector2.MoveTowards(
                currentVelocity,
                targetVelocity,
                Mathf.Max(0f, response) * Mathf.Max(0f, deltaTime));
        }

        public static bool IsFinite(Vector2 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y);
        }
    }
}
