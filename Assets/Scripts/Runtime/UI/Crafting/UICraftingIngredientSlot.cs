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
        public int CurrentQuantity { get; private set; }
        public bool IsOccupied => !string.IsNullOrEmpty(CurrentItemId);

        public Action<int, UIDragPayload> OnIngredientAssigned { get; set; }
        public Action<int, string, int> OnIngredientRemoved { get; set; }

        private void Awake()
        {
            EnsureRuntimeVisuals();
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

        public void AssignIngredient(string itemId, string displayName, Sprite icon, int quantity = 1)
        {
            CurrentItemId = itemId;
            CurrentDisplayName = displayName;
            CurrentIcon = icon;
            CurrentQuantity = Mathf.Max(1, quantity);

            if (iconImage != null)
            {
                iconImage.sprite = icon;
                iconImage.color = icon != null ? Color.white : new Color(0.4f, 0.4f, 0.6f);
                iconImage.gameObject.SetActive(true);
            }

            if (labelText != null)
            {
                labelText.text = string.Empty;
                labelText.gameObject.SetActive(false);
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
            CurrentQuantity = 0;

            if (iconImage != null)
            {
                iconImage.sprite = null;
                iconImage.gameObject.SetActive(false);
            }

            if (labelText != null)
            {
                labelText.text = string.Empty;
                labelText.gameObject.SetActive(false);
            }

            if (emptyPlaceholder != null)
            {
                emptyPlaceholder.SetActive(false);
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            // Decision 1: Tapping an occupied slot removes the ingredient and returns it
            if (IsOccupied)
            {
                string removedId = CurrentItemId;
                int removedQuantity = CurrentQuantity;
                Clear();
                OnIngredientRemoved?.Invoke(slotIndex, removedId, removedQuantity);
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
            return new UIDragPayload(
                this, CurrentItemId, CurrentDisplayName, CurrentIcon, CurrentQuantity, "Material");
        }

        private void HandleDragEnded(bool droppedOnValidTarget)
        {
            // If dropped back on materials grid or cleared, slot is emptied
            if (droppedOnValidTarget && IsOccupied)
            {
                string removedId = CurrentItemId;
                int removedQuantity = CurrentQuantity;
                Clear();
                OnIngredientRemoved?.Invoke(slotIndex, removedId, removedQuantity);
            }
        }

        private void EnsureRuntimeVisuals()
        {
            RectTransform root = transform as RectTransform;
            if (root == null) return;

            Image background = GetComponent<Image>();
            if (background == null)
            {
                background = gameObject.AddComponent<Image>();
                background.color = new Color(1f, 1f, 1f, 0.04f);
            }

            if (slotOutline == null)
            {
                slotOutline = GetComponent<Outline>();
                if (slotOutline == null) slotOutline = gameObject.AddComponent<Outline>();
                slotOutline.effectColor = new Color(0.28f, 0.18f, 0.08f, 0.75f);
                slotOutline.effectDistance = new Vector2(1f, -1f);
            }

            if (iconImage == null)
            {
                RectTransform icon = CreateRect("IngredientIcon", root);
                icon.anchorMin = new Vector2(0.08f, 0.25f);
                icon.anchorMax = new Vector2(0.92f, 0.92f);
                icon.offsetMin = icon.offsetMax = Vector2.zero;
                iconImage = icon.gameObject.AddComponent<Image>();
                iconImage.preserveAspect = true;
                iconImage.raycastTarget = false;
            }

            if (labelText == null)
            {
                RectTransform labelRect = CreateRect("IngredientLabel", root);
                labelRect.anchorMin = new Vector2(0f, 0f);
                labelRect.anchorMax = new Vector2(1f, 0.30f);
                labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
                labelText = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
                labelText.fontSize = 12f;
                labelText.enableAutoSizing = true;
                labelText.fontSizeMin = 7f;
                labelText.fontSizeMax = 14f;
                labelText.color = new Color(0.18f, 0.11f, 0.07f, 1f);
                labelText.alignment = TextAlignmentOptions.Center;
                labelText.raycastTarget = false;
                labelText.gameObject.SetActive(false);
            }
        }

        private static RectTransform CreateRect(string objectName, RectTransform parent)
        {
            var child = new GameObject(objectName, typeof(RectTransform));
            var rect = (RectTransform)child.transform;
            rect.SetParent(parent, false);
            return rect;
        }
    }
}
