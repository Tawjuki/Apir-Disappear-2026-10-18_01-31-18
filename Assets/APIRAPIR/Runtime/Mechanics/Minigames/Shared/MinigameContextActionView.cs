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
        [SerializeField] private MinigameActionButton actionInput;
        [SerializeField] private TMP_Text label;
        [SerializeField] private Graphic buttonGraphic;
        [SerializeField] private CanvasGroup canvasGroup;

        private string currentLabel = string.Empty;
        private bool currentAvailability;
        private bool hasContext;

        public MinigameActionButton ActionInput => actionInput;

        private void Awake()
        {
            if (actionInput == null)
            {
                actionInput = GetComponent<MinigameActionButton>();
            }

            if (buttonGraphic == null)
            {
                buttonGraphic = GetComponent<Graphic>();
            }
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
    }
}
