using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ApirApir.UI.DragDrop
{
    /// <summary>
    /// Visual ghost icon rendered during drag-and-drop operations.
    /// Resides on top of the root Canvas, with blocksRaycasts = false so drop targets receive events.
    /// </summary>
    public sealed class UIDragGhost : MonoBehaviour
    {
        public static UIDragGhost Instance { get; private set; }

        [SerializeField] private Image iconImage;
        [SerializeField] private TextMeshProUGUI quantityLabel;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private RectTransform rectTransform;

        private Canvas rootCanvas;
        public UIDragPayload CurrentPayload { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (rectTransform == null) rectTransform = GetComponent<RectTransform>();
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
            rootCanvas = GetComponentInParent<Canvas>();

            Hide();
        }

        public void Initialize(Image icon, TextMeshProUGUI qty, CanvasGroup group, Canvas canvas)
        {
            iconImage = icon;
            quantityLabel = qty;
            canvasGroup = group;
            rootCanvas = canvas;
            rectTransform = GetComponent<RectTransform>();
            Hide();
        }

        public void Show(UIDragPayload payload, Vector2 screenPosition)
        {
            CurrentPayload = payload;
            if (payload == null)
            {
                Hide();
                return;
            }

            gameObject.SetActive(true);
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0.85f;
                canvasGroup.blocksRaycasts = false;
            }

            if (iconImage != null)
            {
                iconImage.sprite = payload.Icon;
                iconImage.color = payload.Icon != null ? Color.white : new Color(0.7f, 0.7f, 0.9f, 0.9f);
            }

            if (quantityLabel != null)
            {
                quantityLabel.text = payload.Quantity > 1 ? $"x{payload.Quantity}" : string.Empty;
            }

            UpdatePosition(screenPosition);
        }

        public void UpdatePosition(Vector2 screenPosition)
        {
            if (rectTransform == null) return;

            if (rootCanvas != null && rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                rectTransform.position = screenPosition;
            }
            else if (rootCanvas != null)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rootCanvas.transform as RectTransform,
                    screenPosition,
                    rootCanvas.worldCamera,
                    out Vector2 localPoint);
                rectTransform.localPosition = localPoint;
            }
            else
            {
                rectTransform.position = screenPosition;
            }
        }

        public void Hide()
        {
            CurrentPayload = null;
            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }
        }
    }
}
