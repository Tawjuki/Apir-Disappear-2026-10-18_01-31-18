using UnityEngine;

namespace ApirApir.TaguTaguan
{
    /// <summary>
    /// Smoothly follows only the local Tagu-Taguan player while retaining the camera's
    /// authored rotation, lens settings, culling mask, and visibility behavior.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class TaguTaguanCameraFollow : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Transform target;
        [SerializeField] private bool findLocallyOwnedTarget = true;
        [SerializeField] private bool snapWhenTargetChanges = true;

        [Header("Framing")]
        [Tooltip("World-space offset from the player's follow point.")]
        [SerializeField] private Vector3 targetOffset = new Vector3(0f, 1.5f, 0f);
        [Tooltip("Distance behind the camera's fixed forward direction. Reduce this to zoom closer.")]
        [SerializeField, Min(0.1f)] private float followDistance = 12f;
        [SerializeField, Min(0f)] private float smoothingTime = 0.18f;
        [SerializeField, Min(0.01f)] private float maximumFollowSpeed = 100f;
        [SerializeField] private bool useUnscaledTime;

        [Header("World-space Camera Boundaries")]
        [SerializeField] private bool constrainToBounds;
        [SerializeField] private Vector2 minimumCameraXZ = new Vector2(-40f, -40f);
        [SerializeField] private Vector2 maximumCameraXZ = new Vector2(40f, 40f);

        private Quaternion fixedRotation;
        private Vector3 followVelocity;
        private TaguTaguanCameraTarget ownedTarget;

        public Transform Target => target;
        public float FollowDistance => followDistance;

        private void Awake()
        {
            CaptureCurrentRotation();
        }

        private void OnEnable()
        {
            CaptureCurrentRotation();
            TaguTaguanCameraTarget.LocalTargetAvailable += HandleLocalTargetAvailable;
            TaguTaguanCameraTarget.LocalTargetUnavailable += HandleLocalTargetUnavailable;

            if (target == null && findLocallyOwnedTarget)
            {
                TryBindExistingLocalTarget();
            }
        }

        private void OnDisable()
        {
            TaguTaguanCameraTarget.LocalTargetAvailable -= HandleLocalTargetAvailable;
            TaguTaguanCameraTarget.LocalTargetUnavailable -= HandleLocalTargetUnavailable;
            followVelocity = Vector3.zero;
        }

        private void LateUpdate()
        {
            // Never inherit player facing. The scene-authored view remains fixed for the match.
            transform.rotation = fixedRotation;
            if (target == null)
            {
                ownedTarget = null;
                return;
            }

            Vector3 desiredPosition = CalculateDesiredPosition(
                target.position,
                fixedRotation,
                targetOffset,
                followDistance,
                constrainToBounds,
                minimumCameraXZ,
                maximumCameraXZ);

            float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            if (smoothingTime <= 0f || deltaTime <= 0f)
            {
                transform.position = desiredPosition;
                followVelocity = Vector3.zero;
                return;
            }

            transform.position = Vector3.SmoothDamp(
                transform.position,
                desiredPosition,
                ref followVelocity,
                smoothingTime,
                maximumFollowSpeed,
                deltaTime);
        }

        public void SetTarget(Transform newTarget)
        {
            ownedTarget = null;
            AssignTarget(newTarget);
        }

        public void ClearTarget()
        {
            ownedTarget = null;
            target = null;
            followVelocity = Vector3.zero;
        }

        public void SetFollowDistance(float distance)
        {
            followDistance = Mathf.Max(0.1f, distance);
        }

        public void CaptureCurrentRotation()
        {
            fixedRotation = transform.rotation;
        }

        public static Vector3 CalculateDesiredPosition(
            Vector3 targetPosition,
            Quaternion cameraRotation,
            Vector3 offset,
            float distance,
            bool useBounds,
            Vector2 minimumXZ,
            Vector2 maximumXZ)
        {
            float safeDistance = Mathf.Max(0.1f, distance);
            Vector3 desired = targetPosition + offset - cameraRotation * Vector3.forward * safeDistance;
            if (!useBounds)
            {
                return desired;
            }

            float minimumX = Mathf.Min(minimumXZ.x, maximumXZ.x);
            float maximumX = Mathf.Max(minimumXZ.x, maximumXZ.x);
            float minimumZ = Mathf.Min(minimumXZ.y, maximumXZ.y);
            float maximumZ = Mathf.Max(minimumXZ.y, maximumXZ.y);
            desired.x = Mathf.Clamp(desired.x, minimumX, maximumX);
            desired.z = Mathf.Clamp(desired.z, minimumZ, maximumZ);
            return desired;
        }

        private void HandleLocalTargetAvailable(TaguTaguanCameraTarget candidate)
        {
            if (!findLocallyOwnedTarget || candidate == null || !candidate.IsLocalTarget)
            {
                return;
            }

            ownedTarget = candidate;
            AssignTarget(candidate.FollowPoint);
        }

        private void HandleLocalTargetUnavailable(TaguTaguanCameraTarget candidate)
        {
            if (candidate == null || candidate != ownedTarget)
            {
                return;
            }

            ClearTarget();
            if (findLocallyOwnedTarget)
            {
                TryBindExistingLocalTarget();
            }
        }

        private void TryBindExistingLocalTarget()
        {
            TaguTaguanCameraTarget[] candidates = FindObjectsByType<TaguTaguanCameraTarget>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            for (int i = 0; i < candidates.Length; i++)
            {
                if (candidates[i] != null && candidates[i].IsLocalTarget)
                {
                    HandleLocalTargetAvailable(candidates[i]);
                    return;
                }
            }
        }

        private void AssignTarget(Transform newTarget)
        {
            target = newTarget;
            followVelocity = Vector3.zero;
            if (!snapWhenTargetChanges || target == null)
            {
                return;
            }

            transform.position = CalculateDesiredPosition(
                target.position,
                fixedRotation,
                targetOffset,
                followDistance,
                constrainToBounds,
                minimumCameraXZ,
                maximumCameraXZ);
        }

        private void OnValidate()
        {
            followDistance = Mathf.Max(0.1f, followDistance);
            smoothingTime = Mathf.Max(0f, smoothingTime);
            maximumFollowSpeed = Mathf.Max(0.01f, maximumFollowSpeed);
        }

        private void OnDrawGizmosSelected()
        {
            if (!constrainToBounds)
            {
                return;
            }

            float minimumX = Mathf.Min(minimumCameraXZ.x, maximumCameraXZ.x);
            float maximumX = Mathf.Max(minimumCameraXZ.x, maximumCameraXZ.x);
            float minimumZ = Mathf.Min(minimumCameraXZ.y, maximumCameraXZ.y);
            float maximumZ = Mathf.Max(minimumCameraXZ.y, maximumCameraXZ.y);
            Vector3 center = new Vector3(
                (minimumX + maximumX) * 0.5f,
                transform.position.y,
                (minimumZ + maximumZ) * 0.5f);
            Vector3 size = new Vector3(maximumX - minimumX, 0.1f, maximumZ - minimumZ);

            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.8f);
            Gizmos.DrawWireCube(center, size);
        }
    }
}
