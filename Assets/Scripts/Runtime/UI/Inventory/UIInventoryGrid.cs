using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ApirApir.UI.Inventory
{
    /// <summary>
    /// Reusable scrollable grid of inventory items.
    /// Used by Crafting Materials panel and Shop Bag panel.
    /// </summary>
    public sealed class UIInventoryGrid : MonoBehaviour
    {
        [Header("Hierarchy")]
        [SerializeField] private Transform contentContainer;
        [SerializeField] private GameObject slotPrefab;

        private readonly List<UIInventorySlot> activeSlots = new List<UIInventorySlot>();
        private readonly Dictionary<string, UIInventoryItemData> itemMap = new Dictionary<string, UIInventoryItemData>(StringComparer.Ordinal);
        private UIInventorySlot selectedSlot;
        private TMP_FontAsset itemFont;
        private Vector2 itemIconSize = new Vector2(100f, 100f);
        private ScrollRect verticalScrollRect;
        private bool showItemNames = true;

        public Action<UIInventoryItemData> OnItemSelected { get; set; }
        public IReadOnlyDictionary<string, UIInventoryItemData> Items => itemMap;
        public UIInventoryItemData SelectedItem => selectedSlot != null ? selectedSlot.ItemData : null;

        public void Initialize(Transform container, GameObject prefab)
        {
            contentContainer = container;
            slotPrefab = prefab;
            CacheAuthoredSlots();
        }

        public void ConfigurePresentation(
            TMP_FontAsset font,
            Vector2 iconSize,
            ScrollRect scrollRect = null,
            bool displayItemNames = true)
        {
            itemFont = font;
            itemIconSize = iconSize;
            verticalScrollRect = scrollRect;
            showItemNames = displayItemNames;
            for (int i = 0; i < activeSlots.Count; i++)
            {
                activeSlots[i].ConfigurePresentation(
                    itemFont, itemIconSize, verticalScrollRect, showItemNames);
            }
        }

        public void Populate(IEnumerable<UIInventoryItemData> items)
        {
            itemMap.Clear();
            selectedSlot = null;

            int index = 0;
            if (items != null)
            {
                foreach (var item in items)
                {
                    itemMap[item.id] = item;
                    UIInventorySlot slot = GetOrCreateSlot(index);
                    slot.ConfigurePresentation(
                        itemFont, itemIconSize, verticalScrollRect, showItemNames);
                    slot.Bind(item);
                    slot.OnSlotClicked = HandleSlotClicked;
                    index++;
                }
            }

            // Deactivate remaining unused pooled slots
            for (int i = index; i < activeSlots.Count; i++)
            {
                activeSlots[i].gameObject.SetActive(false);
            }
        }

        public bool TryAdjustQuantity(string itemId, int delta)
        {
            if (string.IsNullOrEmpty(itemId) || !itemMap.TryGetValue(itemId, out var item))
            {
                return false;
            }

            if (item.quantity + delta < 0) return false;
            item.quantity += delta;

            foreach (var slot in activeSlots)
            {
                if (slot.gameObject.activeSelf && slot.ItemData != null && slot.ItemData.id == itemId)
                {
                    slot.RefreshDisplay();
                    break;
                }
            }
            return true;
        }

        public int GetQuantity(string itemId)
        {
            if (string.IsNullOrEmpty(itemId) || !itemMap.TryGetValue(itemId, out var item))
            {
                return 0;
            }
            return item.quantity;
        }

        private void HandleSlotClicked(UIInventorySlot slot)
        {
            if (selectedSlot != null) selectedSlot.SetSelected(false);
            selectedSlot = slot;
            if (selectedSlot != null) selectedSlot.SetSelected(true);

            OnItemSelected?.Invoke(slot != null ? slot.ItemData : null);
        }

        private UIInventorySlot GetOrCreateSlot(int index)
        {
            if (index < activeSlots.Count)
            {
                return activeSlots[index];
            }

            GameObject instance = null;
            if (slotPrefab != null)
            {
                instance = Instantiate(slotPrefab, contentContainer);
            }
            else
            {
                // Functional fallback for prototype scenes that have not yet been
                // assigned an authored inventory-slot prefab.
                instance = new GameObject(
                    $"MaterialInventorySlot_{index + 1:00}", typeof(RectTransform));
                instance.transform.SetParent(contentContainer, false);
            }

            var slot = instance.GetComponent<UIInventorySlot>();
            if (slot == null) slot = instance.AddComponent<UIInventorySlot>();
            activeSlots.Add(slot);
            return slot;
        }

        private void CacheAuthoredSlots()
        {
            activeSlots.Clear();
            if (contentContainer == null)
            {
                return;
            }

            for (int i = 0; i < contentContainer.childCount; i++)
            {
                Transform child = contentContainer.GetChild(i);
                if (!child.name.StartsWith("MaterialInventorySlot_", StringComparison.Ordinal))
                {
                    continue;
                }

                UIInventorySlot slot = child.GetComponent<UIInventorySlot>() ??
                                       child.gameObject.AddComponent<UIInventorySlot>();
                activeSlots.Add(slot);
            }
        }
    }
}
