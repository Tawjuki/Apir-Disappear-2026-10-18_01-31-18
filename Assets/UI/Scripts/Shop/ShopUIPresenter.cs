using System;
using System.Collections.Generic;
using UnityEngine;
using ApirApir.Shop;
using ApirApir.UI.Inventory;

namespace ApirApir.UI
{
    /// <summary>
    /// Presenter managing Shop (Tyangge) buying and selling.
    /// Follows GDD 100% buy price and 75% sell payout rule.
    /// </summary>
    public sealed class ShopUIPresenter : MonoBehaviour
    {
        [SerializeField] private UIShopView view;

        private int playerCoins = 350;
        private readonly MarketValueShopPricingPolicy pricingPolicy = new MarketValueShopPricingPolicy();
        private UIInventoryItemData selectedShopItem;
        private UIInventoryItemData selectedBagItem;

        public int PlayerCoins => playerCoins;

        private void Start()
        {
            if (view == null) view = GetComponent<UIShopView>();
            BindEvents();
            LoadShopCatalog();
            LoadPlayerBag();
            view.UpdateCurrency(playerCoins);
            view.SetShopSelection(null, false);
            view.SetBagSelection(null, 0);
        }

        private void BindEvents()
        {
            if (view == null) return;

            if (view.itemsGrid != null)
            {
                view.itemsGrid.OnItemSelected = HandleShopItemSelected;
            }

            if (view.maintenanceGrid != null)
            {
                view.maintenanceGrid.OnItemSelected = HandleShopItemSelected;
            }

            if (view.bagGrid != null)
            {
                view.bagGrid.OnItemSelected = HandleBagItemSelected;
            }

            if (view.btnBuy != null)
            {
                view.btnBuy.onClick.AddListener(HandleBuyClicked);
            }

            if (view.btnSell != null)
            {
                view.btnSell.onClick.AddListener(HandleSellClicked);
            }
        }

        public void LoadShopCatalog()
        {
            if (view == null) return;

            if (view.itemsGrid != null)
            {
                var generalItems = new List<UIInventoryItemData>
                {
                    new UIInventoryItemData("common_tela",   "Tela",   10, 40, null, "Material", "Common"),
                    new UIInventoryItemData("common_tali",   "Tali",   8,  30, null, "Material", "Common"),
                    new UIInventoryItemData("common_kahoy",  "Kahoy",  6,  50, null, "Material", "Common"),
                    new UIInventoryItemData("common_bakal",  "Bakal",  4,  80, null, "Material", "Common"),
                    new UIInventoryItemData("common_goma",   "Goma",   5,  35, null, "Material", "Common"),
                    new UIInventoryItemData("rare_salamin",  "Salamin",3, 120, null, "Material", "Rare")
                };
                view.itemsGrid.Populate(generalItems);
            }

            if (view.maintenanceGrid != null)
            {
                var maintenanceItems = new List<UIInventoryItemData>
                {
                    new UIInventoryItemData("maint_langis",  "Langis Panlinis", 12, 45, null, "Maintenance", "Common"),
                    new UIInventoryItemData("maint_pantapal","Pantapal",        15, 25, null, "Maintenance", "Common"),
                    new UIInventoryItemData("maint_liha",    "Liha",            10, 30, null, "Maintenance", "Common")
                };
                view.maintenanceGrid.Populate(maintenanceItems);
            }
        }

        public void LoadPlayerBag()
        {
            if (view == null || view.bagGrid == null) return;

            var bagItems = new List<UIInventoryItemData>
            {
                new UIInventoryItemData("common_tela",  "Tela",  3, 40, null, "Material", "Common"),
                new UIInventoryItemData("common_tali",  "Tali",  2, 30, null, "Material", "Common"),
                new UIInventoryItemData("common_kahoy", "Kahoy", 5, 50, null, "Material", "Common"),
                new UIInventoryItemData("common_dayami","Dayami",4, 20, null, "Material", "Common")
            };
            view.bagGrid.Populate(bagItems);
        }

        private void HandleShopItemSelected(UIInventoryItemData item)
        {
            selectedShopItem = item;
            bool canAfford = item != null && playerCoins >= item.price && item.quantity > 0;
            view.SetShopSelection(item, canAfford);
        }

        private void HandleBagItemSelected(UIInventoryItemData item)
        {
            selectedBagItem = item;
            int payout = 0;
            if (item != null && item.price > 0)
            {
                // 75% market value payout
                payout = Mathf.RoundToInt(item.price * 0.75f);
            }
            view.SetBagSelection(item, payout);
        }

        private void HandleBuyClicked()
        {
            if (selectedShopItem == null || selectedShopItem.quantity <= 0) return;
            if (playerCoins < selectedShopItem.price) return;

            // Transact
            playerCoins -= selectedShopItem.price;
            selectedShopItem.quantity--;

            // Refresh shop grid
            if (view.itemsGrid.Items.ContainsKey(selectedShopItem.id))
            {
                view.itemsGrid.TryAdjustQuantity(selectedShopItem.id, 0); // triggers visual refresh
            }
            else if (view.maintenanceGrid.Items.ContainsKey(selectedShopItem.id))
            {
                view.maintenanceGrid.TryAdjustQuantity(selectedShopItem.id, 0);
            }

            // Add to Bag
            if (view.bagGrid.Items.ContainsKey(selectedShopItem.id))
            {
                view.bagGrid.TryAdjustQuantity(selectedShopItem.id, 1);
            }
            else
            {
                var newBagItem = new UIInventoryItemData(
                    selectedShopItem.id,
                    selectedShopItem.displayName,
                    1,
                    selectedShopItem.price,
                    selectedShopItem.icon,
                    selectedShopItem.category,
                    selectedShopItem.rarity);
                var currentBag = new List<UIInventoryItemData>(view.bagGrid.Items.Values) { newBagItem };
                view.bagGrid.Populate(currentBag);
            }

            view.UpdateCurrency(playerCoins);
            HandleShopItemSelected(selectedShopItem);
            Debug.Log($"[Shop] Purchased {selectedShopItem.displayName} for ₱{selectedShopItem.price}. Remaining: ₱{playerCoins}");
        }

        private void HandleSellClicked()
        {
            if (selectedBagItem == null || selectedBagItem.quantity <= 0) return;

            int payout = Mathf.RoundToInt(selectedBagItem.price * 0.75f);
            if (payout <= 0) return;

            // Transact
            playerCoins += payout;
            view.bagGrid.TryAdjustQuantity(selectedBagItem.id, -1);

            view.UpdateCurrency(playerCoins);
            HandleBagItemSelected(selectedBagItem);
            Debug.Log($"[Shop] Sold {selectedBagItem.displayName} for ₱{payout}. Total: ₱{playerCoins}");
        }
    }
}
