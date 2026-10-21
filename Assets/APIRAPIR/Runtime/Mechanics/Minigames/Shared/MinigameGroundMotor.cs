using UnityEngine;

namespace ApirApir.Minigames
{
    /// <summary>
    /// Reusable planar motor. It contains no ownership or minigame-rule decisions;
    /// authoritative gameplay code decides when it may run and constrains its target.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MinigameGroundMotor : MonoBehaviour
    {
        [SerializeField] private CharacterController characterController;
        [SerializeField] private bool createControllerWhenMissing = true;

        private Vector2 velocity;

        public Vector2 Velocity => velocity;
        public bool UsesCharacterController => characterController != null && characterController.enabled;

        private void Awake()
        {
            ResolveCharacterController();
        }

        public Vector2 UpdateVelocity(
            Vector2 input,
            float speed,
            float acceleration,
            float deceleration,
            float deltaTime)
        {
            velocity = MinigameMovementMath.MoveVelocityTowards(
                velocity,
                input,
                speed,
                acceleration,
                deceleration,
                deltaTime);
            return velocity;
        }

        public Vector3 MoveTo(Vector3 targetPosition, float rotationSpeedDegrees, float deltaTime)
        {
            Vector3 previousPosition = transform.position;
            targetPosition.y = previousPosition.y;
            MoveDisplacement(targetPosition - previousPosition);

            Vector3 actualMovement = transform.position - previousPosition;
            actualMovement.y = 0f;
            if (actualMovement.sqrMagnitude > 0.000001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(actualMovement.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    targetRotation,
                    Mathf.Max(0f, rotationSpeedDegrees) * Mathf.Max(0f, deltaTime));
            }

            return actualMovement;
        }

        public void CorrectPosition(Vector3 constrainedPosition)
        {
            constrainedPosition.y = transform.position.y;
            MoveDisplacement(constrainedPosition - transform.position);
        }

        public void Teleport(Vector3 position)
        {
            Stop();
            if (characterController == null || !characterController.enabled)
            {
                transform.position = position;
                return;
            }

            characterController.enabled = false;
            transform.position = position;
            characterController.enabled = true;
        }

        public void Stop()
        {
            velocity = Vector2.zero;
        }

        private void ResolveCharacterController()
        {
            if (characterController == null)
            {
                characterController = GetComponent<CharacterController>();
            }

            if (characterController != null || !createControllerWhenMissing || GetComponent<Rigidbody>() != null)
            {
                return;
            }

            CapsuleCollider capsule = GetComponent<CapsuleCollider>();
            characterController = gameObject.AddComponent<CharacterController>();
            if (capsule == null)
            {
                return;
            }

            characterController.center = capsule.center;
            characterController.radius = capsule.radius;
            characterController.height = capsule.height;
            capsule.enabled = false;
        }

        private void MoveDisplacement(Vector3 displacement)
        {
            displacement.y = 0f;
            if (displacement.sqrMagnitude <= 0.0000001f)
            {
                return;
            }

            if (characterController != null && characterController.enabled)
            {
                characterController.Move(displacement);
            }
            else
            {
                transform.position += displacement;
            }
        }
    }
}
