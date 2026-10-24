using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ApirApir.UI.Inventory;

namespace ApirApir.UI
{
    /// <summary>
    /// View component for the Shop (Tyangge) matching Sketch 2.
    /// Left side: Items grid + separate Maintenance section + Buy button.
    /// Right side: Bag grid + Sell button.
    /// </summary>
    public sealed class UIShopView : MonoBehaviour
    {
        [Header("Currency")]
        public TextMeshProUGUI currencyLabel;

        [Header("Left Side - Shop Items")]
        public UIInventoryGrid itemsGrid;
        public UIInventoryGrid maintenanceGrid;
        public TextMeshProUGUI shopItemDetailsText;
        public Button btnBuy;
        public TextMeshProUGUI buyButtonLabel;

        [Header("Right Side - Bag (Sell)")]
        public UIInventoryGrid bagGrid;
        public TextMeshProUGUI bagItemDetailsText;
        public Button btnSell;
        public TextMeshProUGUI sellButtonLabel;

        public void UpdateCurrency(int coins)
        {
            if (currencyLabel != null)
            {
                currencyLabel.text = $"Pera: ₱{coins}";
            }
        }

        public void SetShopSelection(UIInventoryItemData item, bool canAfford)
        {
            if (item == null)
            {
                if (shopItemDetailsText != null) shopItemDetailsText.text = "Pumili ng bibilhin";
                if (buyButtonLabel != null) buyButtonLabel.text = "BUY";
                SetBuyButtonInteractable(false);
                return;
            }

            if (shopItemDetailsText != null)
            {
                shopItemDetailsText.text = $"{item.displayName} • Presyo: ₱{item.price} (Stock: {item.quantity})";
            }

            if (buyButtonLabel != null)
            {
                buyButtonLabel.text = $"BUY (₱{item.price})";
            }

            SetBuyButtonInteractable(canAfford && item.quantity > 0);
        }

        public void SetBagSelection(UIInventoryItemData item, int sellPayout)
        {
            if (item == null)
            {
                if (bagItemDetailsText != null) bagItemDetailsText.text = "Pumili ng ibebenta";
                if (sellButtonLabel != null) sellButtonLabel.text = "SELL";
                SetSellButtonInteractable(false);
                return;
            }

            if (bagItemDetailsText != null)
            {
                bagItemDetailsText.text = $"{item.displayName} (Hawak: {item.quantity}) • Halaga: ₱{sellPayout}";
            }

            if (sellButtonLabel != null)
            {
                sellButtonLabel.text = $"SELL (+₱{sellPayout})";
            }

            SetSellButtonInteractable(item.quantity > 0 && sellPayout > 0);
        }

        public void SetBuyButtonInteractable(bool interactable)
        {
            if (btnBuy != null)
            {
                btnBuy.interactable = interactable;
                var img = btnBuy.GetComponent<Image>();
                if (img != null)
                {
                    img.color = interactable ? new Color(0.25f, 0.75f, 0.40f) : new Color(0.4f, 0.4f, 0.4f, 0.7f);
                }
            }
        }

        public void SetSellButtonInteractable(bool interactable)
        {
            if (btnSell != null)
            {
                btnSell.interactable = interactable;
                var img = btnSell.GetComponent<Image>();
                if (img != null)
                {
                    img.color = interactable ? new Color(0.85f, 0.50f, 0.25f) : new Color(0.4f, 0.4f, 0.4f, 0.7f);
                }
            }
        }
    }
}
