using System;
using UnityEngine;

namespace ApirApir.UI.Inventory
{
    [Serializable]
    public sealed class UIItemSpriteBinding
    {
        [SerializeField] private string itemId = string.Empty;
        [SerializeField] private Sprite sprite;

        public UIItemSpriteBinding(string itemId, Sprite sprite)
        {
            this.itemId = itemId == null ? string.Empty : itemId.Trim();
            this.sprite = sprite;
        }

        public string ItemId => itemId;
        public Sprite Sprite => sprite;
    }

    [Serializable]
    public sealed class UIInventoryItemData
    {
        public string id;
        public string displayName;
        public int quantity;
        public int price;
        public Sprite icon;
        public string category;
        public string rarity;

        public UIInventoryItemData(
            string id,
            string displayName,
            int quantity,
            int price = -1,
            Sprite icon = null,
            string category = "Material",
            string rarity = "Common")
        {
            this.id = id ?? string.Empty;
            this.displayName = displayName ?? string.Empty;
            this.quantity = quantity;
            this.price = price;
            this.icon = icon;
            this.category = category ?? "Material";
            this.rarity = rarity ?? "Common";
        }
    }
}
