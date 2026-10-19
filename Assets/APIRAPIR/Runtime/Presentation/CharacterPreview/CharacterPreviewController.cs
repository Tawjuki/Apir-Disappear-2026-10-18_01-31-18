using System;
using UnityEngine;

namespace ApirApir.PlayerProfiles
{
    /// <summary>
    /// Applies local-only floating and rotation to an explicitly assigned preview Transform.
    /// Assign a dedicated preview model, never the live gameplay character.
    /// </summary>
    public sealed class CharacterPreviewController : MonoBehaviour
    {
        [SerializeField] private Transform previewTransform;
        [SerializeField] private bool previewActive = true;
        [SerializeField, Min(0f)] private float floatingSpeed = 0.5f;
        [SerializeField, Min(0f)] private float floatingHeight = 0.15f;
        [SerializeField] private float rotationSpeed = 25f;

        private Vector3 originalLocalPosition;
        private Quaternion originalLocalRotation;
        private float animationStartedAt;
        private bool hasOriginalPose;

        public event Action<Transform> CharacterPreviewChanged;

        public Transform PreviewTransform => previewTransform;
        public bool IsPreviewActive => previewActive;
        public float FloatingSpeed => floatingSpeed;
        public float FloatingHeight => floatingHeight;
        public float RotationSpeed => rotationSpeed;

        private void Awake()
        {
            if (previewTransform != null)
            {
                CaptureOriginalPose();
                animationStartedAt = Time.unscaledTime;
            }

            enabled = previewActive && previewTransform != null;
        }

        private void Update()
        {
            ApplyPreviewPose(Time.unscaledTime - animationStartedAt);
        }

        private void OnEnable()
        {
            if (previewActive && previewTransform != null && !hasOriginalPose)
            {
                CaptureOriginalPose();
                animationStartedAt = Time.unscaledTime;
            }
        }

        private void OnDisable()
        {
            RestoreOriginalPose();
        }

        private void OnDestroy()
        {
            RestoreOriginalPose();
        }

        public void SetPreview(Transform newPreviewTransform)
        {
            if (ReferenceEquals(previewTransform, newPreviewTransform))
            {
                ResetPreviewPose();
                return;
            }

            RestoreOriginalPose();
            previewTransform = newPreviewTransform;
            hasOriginalPose = false;
            if (previewTransform != null)
            {
                CaptureOriginalPose();
                animationStartedAt = Time.unscaledTime;
            }

            enabled = previewActive && previewTransform != null;
            CharacterPreviewChanged?.Invoke(previewTransform);
        }

        public void ClearPreview()
        {
            SetPreview(null);
        }

        public void SetPreviewActive(bool active)
        {
            if (previewActive == active)
            {
                return;
            }

            previewActive = active;
            if (!previewActive)
            {
                RestoreOriginalPose();
            }
            else if (previewTransform != null)
            {
                CaptureOriginalPose();
                animationStartedAt = Time.unscaledTime;
            }

            enabled = previewActive && previewTransform != null;
        }

        public void ConfigureMotion(float speed, float height, float degreesPerSecond)
        {
            floatingSpeed = Mathf.Max(0f, speed);
            floatingHeight = Mathf.Max(0f, height);
            rotationSpeed = degreesPerSecond;
            ResetPreviewPose();
        }

        public void ResetPreviewPose()
        {
            RestoreOriginalPose();
            if (previewTransform != null)
            {
                CaptureOriginalPose();
                animationStartedAt = Time.unscaledTime;
            }
        }

        /// <summary>Deterministic pose evaluation used by Update and Edit Mode tests.</summary>
        public void ApplyPreviewPose(float elapsedSeconds)
        {
            if (!previewActive || previewTransform == null || !hasOriginalPose)
            {
                return;
            }

            float safeElapsed = Mathf.Max(0f, elapsedSeconds);
            float offset = Mathf.Sin(safeElapsed * floatingSpeed * Mathf.PI * 2f) * floatingHeight;
            previewTransform.localPosition = originalLocalPosition + Vector3.up * offset;
            previewTransform.localRotation = originalLocalRotation *
                                             Quaternion.AngleAxis(rotationSpeed * safeElapsed, Vector3.up);
        }

        private void CaptureOriginalPose()
        {
            if (previewTransform == null || hasOriginalPose)
            {
                return;
            }

            originalLocalPosition = previewTransform.localPosition;
            originalLocalRotation = previewTransform.localRotation;
            hasOriginalPose = true;
        }

        private void RestoreOriginalPose()
        {
            if (previewTransform == null || !hasOriginalPose)
            {
                return;
            }

            previewTransform.localPosition = originalLocalPosition;
            previewTransform.localRotation = originalLocalRotation;
            hasOriginalPose = false;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            floatingSpeed = Mathf.Max(0f, floatingSpeed);
            floatingHeight = Mathf.Max(0f, floatingHeight);
        }
#endif
    }
}
