using System;
using System.Collections.Generic;
using UnityEngine;
using ApirApir.Crafting;
using ApirApir.UI.Inventory;
using ApirApir.UI.DragDrop;

namespace ApirApir.UI
{
    /// <summary>
    /// Presenter managing crafting workflow:
    /// Validates recipes against CraftingCatalog, manages inventory counts,
    /// and triggers crafting execution.
    /// </summary>
    public sealed class CraftingUIPresenter : MonoBehaviour
    {
        [SerializeField] private UICraftingView view;

        private CraftingCatalog catalog;
        private readonly List<string> slotItemIds = new List<string> { null, null, null };
        private CraftingRecipe matchedRecipe;

        public Action<CraftingRecipe> OnCraftExecuted { get; set; }

        private void Start()
        {
            if (view == null) view = GetComponent<UICraftingView>();
            InitializeCatalog();
            BindEvents();
            LoadMockPlayerMaterials();
            EvaluateRecipe();
        }

        public void InitializeCatalog(CraftingCatalog customCatalog = null)
        {
            catalog = customCatalog ?? DefaultCraftingCatalog.Create();
        }

        private void BindEvents()
        {
            if (view == null) return;

            for (int i = 0; i < view.ingredientSlots.Length; i++)
            {
                int index = i;
                if (view.ingredientSlots[i] != null)
                {
                    view.ingredientSlots[i].slotIndex = index;
                    view.ingredientSlots[i].OnIngredientAssigned = HandleIngredientAssigned;
                    view.ingredientSlots[i].OnIngredientRemoved = HandleIngredientRemoved;
                }
            }

            if (view.materialsDropArea != null)
            {
                view.materialsDropArea.AcceptedCategory = "Material";
                view.materialsDropArea.OnPayloadDropped = HandleDroppedBackToInventory;
            }

            if (view.btnCraft != null)
            {
                view.btnCraft.onClick.AddListener(HandleCraftClicked);
            }
        }

        public void LoadMockPlayerMaterials()
        {
            if (view == null || view.materialsGrid == null) return;

            var items = new List<UIInventoryItemData>
            {
                new UIInventoryItemData("common_tela",   "Tela",   5, -1, null, "Material", "Common"),
                new UIInventoryItemData("common_tali",   "Tali",   4, -1, null, "Material", "Common"),
                new UIInventoryItemData("common_dayami", "Dayami", 4, -1, null, "Material", "Common"),
                new UIInventoryItemData("common_kahoy",  "Kahoy",  6, -1, null, "Material", "Common"),
                new UIInventoryItemData("common_goma",   "Goma",   3, -1, null, "Material", "Common"),
                new UIInventoryItemData("common_bakal",  "Bakal",  3, -1, null, "Material", "Common"),
                new UIInventoryItemData("rare_salamin",  "Salamin",2, -1, null, "Material", "Rare"),
                new UIInventoryItemData("rare_tinta",    "Tinta",  2, -1, null, "Material", "Rare"),
                new UIInventoryItemData("rare_tsapa",    "Tsapa",  2, -1, null, "Material", "Rare")
            };

            view.materialsGrid.Populate(items);
        }

        private void HandleIngredientAssigned(int slotIndex, UIDragPayload payload)
        {
            if (payload == null || string.IsNullOrEmpty(payload.ItemId)) return;

            // If item is being moved from another crafting slot
            if (payload.SourceContext is UICraftingIngredientSlot sourceSlot)
            {
                int sourceIdx = sourceSlot.slotIndex;
                if (sourceIdx >= 0 && sourceIdx < slotItemIds.Count)
                {
                    slotItemIds[sourceIdx] = null;
                }
            }
            else
            {
                // Coming from inventory: check quantity and decrement 1
                if (view.materialsGrid.GetQuantity(payload.ItemId) <= 0) return;
                view.materialsGrid.TryAdjustQuantity(payload.ItemId, -1);
            }

            // If target slot already held an ingredient, return old ingredient to materials
            if (slotIndex >= 0 && slotIndex < slotItemIds.Count && !string.IsNullOrEmpty(slotItemIds[slotIndex]))
            {
                view.materialsGrid.TryAdjustQuantity(slotItemIds[slotIndex], 1);
            }

            slotItemIds[slotIndex] = payload.ItemId;
            view.ingredientSlots[slotIndex].AssignIngredient(payload.ItemId, payload.DisplayName, payload.Icon);

            EvaluateRecipe();
        }

        private void HandleIngredientRemoved(int slotIndex, string removedItemId)
        {
            if (slotIndex >= 0 && slotIndex < slotItemIds.Count)
            {
                slotItemIds[slotIndex] = null;
            }

            // Decision 1: Returning an ingredient restores available quantity
            if (!string.IsNullOrEmpty(removedItemId))
            {
                view.materialsGrid.TryAdjustQuantity(removedItemId, 1);
            }

            EvaluateRecipe();
        }

        private void HandleDroppedBackToInventory(UIDragPayload payload)
        {
            if (payload != null && payload.SourceContext is UICraftingIngredientSlot sourceSlot)
            {
                int idx = sourceSlot.slotIndex;
                sourceSlot.Clear();
                HandleIngredientRemoved(idx, payload.ItemId);
            }
        }

        private void EvaluateRecipe()
        {
            matchedRecipe = null;
            if (catalog == null) return;

            // Count materials in the 3 slots
            var presentCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < slotItemIds.Count; i++)
            {
                string id = slotItemIds[i];
                if (!string.IsNullOrEmpty(id))
                {
                    presentCounts[id] = presentCounts.TryGetValue(id, out int c) ? c + 1 : 1;
                }
            }

            // Find matching recipe
            foreach (var kvp in catalog.Recipes)
            {
                CraftingRecipe recipe = kvp.Value;
                if (!recipe.IsEnabled) continue;

                bool match = true;
                int totalReqCount = 0;
                foreach (var req in recipe.Requirements)
                {
                    totalReqCount += req.Quantity;
                    if (!presentCounts.TryGetValue(req.MaterialId, out int count) || count < req.Quantity)
                    {
                        match = false;
                        break;
                    }
                }

                // If all requirements are satisfied
                if (match && totalReqCount > 0)
                {
                    matchedRecipe = recipe;
                    break;
                }
            }

            if (matchedRecipe != null)
            {
                var part = matchedRecipe.OutputPart;
                string details = $"{part.Slot} Part • {part.Rarity} • {part.CharacterDisplayName}";
                view.SetPreview(part.DisplayName, details, null, true);
            }
            else
            {
                view.SetPreview(string.Empty, string.Empty, null, false);
            }
        }

        private void HandleCraftClicked()
        {
            if (matchedRecipe == null) return;

            Debug.Log($"[Crafting] Successfully crafted: {matchedRecipe.OutputPart.DisplayName}!");

            // Consumed permanently: clear slots
            for (int i = 0; i < slotItemIds.Count; i++)
            {
                slotItemIds[i] = null;
                if (view.ingredientSlots[i] != null)
                {
                    view.ingredientSlots[i].Clear();
                }
            }

            OnCraftExecuted?.Invoke(matchedRecipe);
            EvaluateRecipe();
        }
    }
}
