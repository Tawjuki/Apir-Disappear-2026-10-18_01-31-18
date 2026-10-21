using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using ApirApir.UI.DragDrop;

namespace ApirApir.UI.Inventory
{
    /// <summary>
    /// Reusable card/slot inside UIInventoryGrid.
    /// Supports tapping to select and dragging 1 unit into recipe/drop slots.
    /// </summary>
    public sealed class UIInventorySlot : MonoBehaviour, IPointerClickHandler
    {
        [Header("UI References")]
        [SerializeField] private Image iconImage;
        [SerializeField] private TextMeshProUGUI nameLabel;
        [SerializeField] private TextMeshProUGUI quantityLabel;
        [SerializeField] private TextMeshProUGUI priceLabel;
        [SerializeField] private Outline selectionOutline;
        [SerializeField] private UIDragSource dragSource;

        [Header("Colors")]
        [SerializeField] private Color normalBorder = new Color(0.45f, 0.45f, 0.65f, 0.5f);
        [SerializeField] private Color selectedBorder = new Color(1f, 0.85f, 0.2f, 0.95f);

        public UIInventoryItemData ItemData { get; private set; }
        public Action<UIInventorySlot> OnSlotClicked { get; set; }

        private void Awake()
        {
            EnsureRuntimeVisuals();
            if (dragSource == null) dragSource = GetComponent<UIDragSource>();
            if (dragSource == null) dragSource = gameObject.AddComponent<UIDragSource>();
            if (selectionOutline == null) selectionOutline = GetComponent<Outline>();

            dragSource.PayloadProvider = GetDragPayload;
        }

        public void Bind(UIInventoryItemData data)
        {
            ItemData = data;
            if (data == null)
            {
                gameObject.SetActive(false);
                return;
            }

            gameObject.SetActive(true);
            if (nameLabel != null) nameLabel.text = data.displayName;
            if (iconImage != null)
            {
                iconImage.sprite = data.icon;
                iconImage.color = data.icon != null
                    ? Color.white
                    : RarityColor(data.rarity);
            }
            RefreshDisplay();
            SetSelected(false);
        }

        public void RefreshDisplay()
        {
            if (ItemData == null) return;
            if (quantityLabel != null)
            {
                quantityLabel.text = ItemData.quantity > 0 ? $"x{ItemData.quantity}" : "0";
                quantityLabel.gameObject.SetActive(ItemData.quantity >= 0);
            }
            if (priceLabel != null)
            {
                priceLabel.text = ItemData.price >= 0 ? $"₱{ItemData.price}" : string.Empty;
                priceLabel.gameObject.SetActive(ItemData.price >= 0);
            }
        }

        public void SetSelected(bool isSelected)
        {
            if (selectionOutline != null)
            {
                selectionOutline.effectColor = isSelected ? selectedBorder : normalBorder;
                selectionOutline.effectDistance = isSelected ? new Vector2(2, -2) : new Vector2(1, -1);
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            OnSlotClicked?.Invoke(this);
        }

        private UIDragPayload GetDragPayload()
        {
            if (ItemData == null || ItemData.quantity <= 0) return null;
            // Drag exactly 1 unit
            return new UIDragPayload(
                this,
                ItemData.id,
                ItemData.displayName,
                ItemData.icon,
                1,
                ItemData.category);
        }

        /// <summary>
        /// Builds a small, usable slot only when an authored prefab has not supplied
        /// visual references. This keeps the inventory functional in prototype scenes
        /// while allowing a proper prefab to replace every generated visual later.
        /// </summary>
        private void EnsureRuntimeVisuals()
        {
            RectTransform root = transform as RectTransform;
            if (root == null) return;

            Image background = GetComponent<Image>();
            if (background == null)
            {
                background = gameObject.AddComponent<Image>();
                background.color = new Color(0.93f, 0.88f, 0.72f, 0.95f);
            }

            if (selectionOutline == null)
            {
                selectionOutline = GetComponent<Outline>();
                if (selectionOutline == null) selectionOutline = gameObject.AddComponent<Outline>();
                selectionOutline.effectColor = normalBorder;
                selectionOutline.effectDistance = new Vector2(1f, -1f);
            }

            if (iconImage == null)
            {
                RectTransform icon = CreateRect("Icon", root);
                icon.anchorMin = new Vector2(0.08f, 0.32f);
                icon.anchorMax = new Vector2(0.92f, 0.95f);
                icon.offsetMin = icon.offsetMax = Vector2.zero;
                iconImage = icon.gameObject.AddComponent<Image>();
                iconImage.preserveAspect = true;
                iconImage.raycastTarget = false;
            }

            if (nameLabel == null)
            {
                nameLabel = CreateLabel("Name", root, new Vector2(0.04f, 0.08f),
                    new Vector2(0.96f, 0.34f), 17f, TextAlignmentOptions.Center);
            }

            if (quantityLabel == null)
            {
                quantityLabel = CreateLabel("Quantity", root, new Vector2(0.58f, 0.70f),
                    new Vector2(0.98f, 0.98f), 18f, TextAlignmentOptions.TopRight);
            }
        }

        private static RectTransform CreateRect(string objectName, RectTransform parent)
        {
            var child = new GameObject(objectName, typeof(RectTransform));
            var rect = (RectTransform)child.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static TextMeshProUGUI CreateLabel(
            string objectName,
            RectTransform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            float fontSize,
            TextAlignmentOptions alignment)
        {
            RectTransform rect = CreateRect(objectName, parent);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.fontSize = fontSize;
            label.color = new Color(0.18f, 0.11f, 0.07f, 1f);
            label.alignment = alignment;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;
            return label;
        }

        private static Color RarityColor(string rarity)
        {
            if (string.Equals(rarity, "Epic", StringComparison.OrdinalIgnoreCase))
                return new Color(0.62f, 0.32f, 0.78f, 1f);
            if (string.Equals(rarity, "Rare", StringComparison.OrdinalIgnoreCase))
                return new Color(0.28f, 0.52f, 0.78f, 1f);
            return new Color(0.68f, 0.62f, 0.48f, 1f);
        }
    }
}
