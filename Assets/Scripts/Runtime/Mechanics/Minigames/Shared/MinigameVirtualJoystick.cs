using UnityEngine;
using UnityEngine.EventSystems;

namespace ApirApir.Minigames
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public class MinigameVirtualJoystick : MonoBehaviour,
        IPointerDownHandler,
        IDragHandler,
        IPointerUpHandler
    {
        private const int NoPointer = int.MinValue;

        [Header("Visuals")]
        [SerializeField] private RectTransform background;
        [SerializeField] private RectTransform handle;

        [Header("Response")]
        [SerializeField, Range(0f, 0.95f)] private float deadZone = 0.12f;
        [SerializeField, Min(0.01f)] private float sensitivity = 1f;
        [SerializeField, Range(0.1f, 1f)] private float handleRange = 0.72f;

        private int activePointerId = NoPointer;
        private Vector2 value;

        public static MinigameVirtualJoystick Active { get; private set; }
        public Vector2 Value => value;
        public bool IsEngaged => activePointerId != NoPointer;

        private void Reset()
        {
            background = transform as RectTransform;
        }

        protected virtual void Awake()
        {
            if (background == null)
            {
                background = transform as RectTransform;
            }

            ResetInput();
        }

        protected virtual void OnEnable()
        {
            Active = this;
        }

        protected virtual void OnDisable()
        {
            if (Active == this)
            {
                Active = null;
            }

            ResetInput();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData == null || activePointerId != NoPointer)
            {
                return;
            }

            activePointerId = eventData.pointerId;
            UpdateInput(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData == null || eventData.pointerId != activePointerId)
            {
                return;
            }

            UpdateInput(eventData);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData != null && eventData.pointerId == activePointerId)
            {
                ResetInput();
            }
        }

        public void CancelInput()
        {
            ResetInput();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                ResetInput();
            }
        }

        private void UpdateInput(PointerEventData eventData)
        {
            if (background == null ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    background,
                    eventData.position,
                    eventData.pressEventCamera,
                    out Vector2 localPoint))
            {
                ResetInput();
                return;
            }

            Rect rect = background.rect;
            float radius = Mathf.Max(1f, Mathf.Min(rect.width, rect.height) * 0.5f);
            Vector2 raw = Vector2.ClampMagnitude(localPoint / radius, 1f);
            float magnitude = raw.magnitude;

            if (handle != null)
            {
                handle.anchoredPosition = raw * (radius * handleRange);
            }

            if (magnitude <= deadZone)
            {
                value = Vector2.zero;
                return;
            }

            float scaledMagnitude = Mathf.Clamp01(
                ((magnitude - deadZone) / Mathf.Max(0.0001f, 1f - deadZone)) * sensitivity);
            value = raw.normalized * scaledMagnitude;
        }

        private void ResetInput()
        {
            activePointerId = NoPointer;
            value = Vector2.zero;
            if (handle != null)
            {
                handle.anchoredPosition = Vector2.zero;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            deadZone = Mathf.Clamp(deadZone, 0f, 0.95f);
            sensitivity = Mathf.Max(0.01f, sensitivity);
            handleRange = Mathf.Clamp(handleRange, 0.1f, 1f);
        }
#endif
    }
}
