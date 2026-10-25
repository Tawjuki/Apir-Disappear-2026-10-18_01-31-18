using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ApirApir.Minigames
{
    /// <summary>
    /// Reusable presentation for the primary contextual action. Minigame code owns
    /// the label and availability; MinigameActionButton owns only pointer input.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MinigameContextActionView : MonoBehaviour
    {
        private static readonly MinigameContextActionView[] ActiveViews =
            new MinigameContextActionView[4];

        [SerializeField] private MinigameActionButton actionInput;
        [SerializeField] private TMP_Text label;
        [SerializeField] private Graphic buttonGraphic;
        [SerializeField] private CanvasGroup canvasGroup;

        private string currentLabel = string.Empty;
        private bool currentAvailability;
        private bool hasContext;

        public MinigameActionButton ActionInput => actionInput;

        public static MinigameContextActionView GetActive(MinigameActionSlot slot)
        {
            int index = (int)slot;
            return index >= 0 && index < ActiveViews.Length ? ActiveViews[index] : null;
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            int index = GetSlotIndex();
            if (index >= 0) ActiveViews[index] = this;
        }

        private void OnDisable()
        {
            int index = GetSlotIndex();
            if (index >= 0 && ActiveViews[index] == this) ActiveViews[index] = null;
        }

        public void SetContext(string actionLabel, bool available)
        {
            string normalized = actionLabel ?? string.Empty;
            if (!hasContext || !string.Equals(currentLabel, normalized, System.StringComparison.Ordinal))
            {
                currentLabel = normalized;
                if (label != null)
                {
                    label.text = normalized;
                }
            }

            if (hasContext && currentAvailability == available)
            {
                return;
            }

            hasContext = true;
            currentAvailability = available;
            if (actionInput != null)
            {
                actionInput.enabled = available;
            }

            if (buttonGraphic != null)
            {
                buttonGraphic.raycastTarget = available;
            }

            if (canvasGroup != null)
            {
                canvasGroup.interactable = available;
                canvasGroup.blocksRaycasts = available;
                canvasGroup.alpha = available ? 1f : 0.45f;
            }
        }

        private void ResolveReferences()
        {
            if (actionInput == null) actionInput = GetComponent<MinigameActionButton>();
            if (label == null) label = GetComponentInChildren<TMP_Text>(true);
            if (buttonGraphic == null) buttonGraphic = GetComponent<Graphic>();
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        }

        private int GetSlotIndex()
        {
            int index = actionInput == null ? -1 : (int)actionInput.ActionSlot;
            return index >= 0 && index < ActiveViews.Length ? index : -1;
        }
    }
}
