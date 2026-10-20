using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using ApirApir.UI.DragDrop;

namespace ApirApir.UI
{
    /// <summary>
    /// Represents one of the 3 horizontal ingredient slots.
    /// Supports dropping materials in, dragging ingredients back out, and tapping to remove.
    /// </summary>
    public sealed class UICraftingIngredientSlot : MonoBehaviour, IPointerClickHandler
    {
        [Header("Configuration")]
        public int slotIndex;

        [Header("UI References")]
        [SerializeField] private Image iconImage;
        [SerializeField] private TextMeshProUGUI labelText;
        [SerializeField] private GameObject emptyPlaceholder;
        [SerializeField] private Outline slotOutline;

        [Header("Components")]
        [SerializeField] private UIDropTarget dropTarget;
        [SerializeField] private UIDragSource dragSource;

        public string CurrentItemId { get; private set; }
        public Sprite CurrentIcon { get; private set; }
        public string CurrentDisplayName { get; private set; }
        public bool IsOccupied => !string.IsNullOrEmpty(CurrentItemId);

        public Action<int, UIDragPayload> OnIngredientAssigned { get; set; }
        public Action<int, string> OnIngredientRemoved { get; set; }

        private void Awake()
        {
            if (dropTarget == null) dropTarget = GetComponent<UIDropTarget>();
            if (dropTarget == null) dropTarget = gameObject.AddComponent<UIDropTarget>();

            if (dragSource == null) dragSource = GetComponent<UIDragSource>();
            if (dragSource == null) dragSource = gameObject.AddComponent<UIDragSource>();

            if (slotOutline == null) slotOutline = GetComponent<Outline>();

            dropTarget.AcceptedCategory = "Material";
            dropTarget.OnPayloadDropped = HandlePayloadDropped;

            dragSource.PayloadProvider = GetDragPayload;
            dragSource.DragEndedCallback = HandleDragEnded;

            Clear();
        }

        public void AssignIngredient(string itemId, string displayName, Sprite icon)
        {
            CurrentItemId = itemId;
            CurrentDisplayName = displayName;
            CurrentIcon = icon;

            if (iconImage != null)
            {
                iconImage.sprite = icon;
                iconImage.color = icon != null ? Color.white : new Color(0.4f, 0.4f, 0.6f);
                iconImage.gameObject.SetActive(true);
            }

            if (labelText != null)
            {
                labelText.text = displayName;
                labelText.gameObject.SetActive(true);
            }

            if (emptyPlaceholder != null)
            {
                emptyPlaceholder.SetActive(false);
            }
        }

        public void Clear()
        {
            CurrentItemId = null;
            CurrentDisplayName = null;
            CurrentIcon = null;

            if (iconImage != null)
            {
                iconImage.sprite = null;
                iconImage.gameObject.SetActive(false);
            }

            if (labelText != null)
            {
                labelText.text = $"Slot {slotIndex + 1}";
            }

            if (emptyPlaceholder != null)
            {
                emptyPlaceholder.SetActive(true);
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            // Decision 1: Tapping an occupied slot removes the ingredient and returns it
            if (IsOccupied)
            {
                string removedId = CurrentItemId;
                Clear();
                OnIngredientRemoved?.Invoke(slotIndex, removedId);
            }
        }

        private void HandlePayloadDropped(UIDragPayload payload)
        {
            if (payload == null || string.IsNullOrEmpty(payload.ItemId)) return;
            OnIngredientAssigned?.Invoke(slotIndex, payload);
        }

        private UIDragPayload GetDragPayload()
        {
            if (!IsOccupied) return null;
            // Drag out of slot to return to materials
            return new UIDragPayload(this, CurrentItemId, CurrentDisplayName, CurrentIcon, 1, "Material");
        }

        private void HandleDragEnded(bool droppedOnValidTarget)
        {
            // If dropped back on materials grid or cleared, slot is emptied
            if (droppedOnValidTarget && IsOccupied)
            {
                string removedId = CurrentItemId;
                Clear();
                OnIngredientRemoved?.Invoke(slotIndex, removedId);
            }
        }
    }
}
