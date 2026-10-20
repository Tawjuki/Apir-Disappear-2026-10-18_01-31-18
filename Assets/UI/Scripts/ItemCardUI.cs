using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ApirApir.UI
{
    /// <summary>
    /// Reusable data-binding component for an item card (crafting or shop entry).
    /// </summary>
    public class ItemCardUI : MonoBehaviour
    {
        [Header("UI References")]
        public Image iconImage;
        public TextMeshProUGUI itemNameLabel;
        public TextMeshProUGUI quantityLabel;
        public TextMeshProUGUI priceLabel;

        public void SetData(string itemName, Sprite icon = null, int quantity = -1, int price = -1)
        {
            if (itemNameLabel) itemNameLabel.text = itemName;
            if (iconImage && icon != null) iconImage.sprite = icon;
            if (quantityLabel) quantityLabel.text = quantity >= 0 ? $"x{quantity}" : "";
            if (priceLabel)    priceLabel.text    = price >= 0    ? $"\u20b1{price}"    : "";
        }
    }
}
