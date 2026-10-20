using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace ApirApir.UI.DragDrop
{
    /// <summary>
    /// Receptor for dropped items. Attached to Crafting Ingredient Slots,
    /// Equipped Skill Slots, and Inventory drop areas.
    /// </summary>
    public sealed class UIDropTarget : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private string acceptedCategory = "Material";
        [SerializeField] private Outline targetOutline;
        [SerializeField] private Color normalColor = new Color(0.4f, 0.4f, 0.6f, 0.5f);
        [SerializeField] private Color highlightColor = new Color(1f, 0.85f, 0.2f, 0.9f);

        public Action<UIDragPayload> OnPayloadDropped { get; set; }

        public string AcceptedCategory
        {
            get => acceptedCategory;
            set => acceptedCategory = value;
        }

        private void Awake()
        {
            if (targetOutline == null) targetOutline = GetComponent<Outline>();
            SetHighlight(false);
        }

        public bool Accepts(UIDragPayload payload)
        {
            if (payload == null) return false;
            if (string.Equals(acceptedCategory, "Any", StringComparison.OrdinalIgnoreCase)) return true;
            return string.Equals(acceptedCategory, payload.Category, StringComparison.OrdinalIgnoreCase);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (UIDragGhost.Instance != null && UIDragGhost.Instance.CurrentPayload != null)
            {
                if (Accepts(UIDragGhost.Instance.CurrentPayload))
                {
                    SetHighlight(true);
                }
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            SetHighlight(false);
        }

        public void OnDrop(PointerEventData eventData)
        {
            SetHighlight(false);

            UIDragPayload payload = null;
            if (UIDragGhost.Instance != null && UIDragGhost.Instance.CurrentPayload != null)
            {
                payload = UIDragGhost.Instance.CurrentPayload;
            }

            if (payload != null && Accepts(payload))
            {
                OnPayloadDropped?.Invoke(payload);
            }
        }

        private void SetHighlight(bool active)
        {
            if (targetOutline != null)
            {
                targetOutline.effectColor = active ? highlightColor : normalColor;
            }
        }
    }
}
