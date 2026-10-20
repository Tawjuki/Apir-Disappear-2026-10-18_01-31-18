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
            if (iconImage != null && data.icon != null)
            {
                iconImage.sprite = data.icon;
                iconImage.color = Color.white;
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
    }
}
