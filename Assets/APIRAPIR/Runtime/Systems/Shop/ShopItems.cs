using System;
using System.Collections.Generic;
using ApirApir.Battle;

namespace ApirApir.Shop
{
    public enum ShopMaterialRarity : byte
    {
        Common,
        Rare,
        Epic
    }

    public interface IShopItemDefinition
    {
        string Id { get; }
        string DisplayName { get; }
        ShopItemCategory Category { get; }
        ShopMaterialRarity? MaterialRarity { get; }
        PartSlot? CompletedPartSlot { get; }
        object InventoryDefinition { get; }
        int MarketValue { get; }
        bool IsSellable { get; }
    }

    /// <summary>
    /// Lightweight shop adapter around an authoritative inventory/crafting definition.
    /// InventoryDefinition is the original definition reference; the shop does not copy it.
    /// </summary>
    public sealed class ShopItemDefinition : IShopItemDefinition
    {
        public ShopItemDefinition(
            string id,
            string displayName,
            ShopItemCategory category,
            object inventoryDefinition,
            int marketValue,
            bool isSellable = false,
            ShopMaterialRarity? materialRarity = null,
            PartSlot? completedPartSlot = null)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A shop item requires a stable ID.", nameof(id));
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException("A shop item requires a display name.", nameof(displayName));
            }

            if (inventoryDefinition == null)
            {
                throw new ArgumentNullException(nameof(inventoryDefinition));
            }

            if (marketValue < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(marketValue));
            }

            ValidateCategoryMetadata(category, materialRarity, completedPartSlot);
            Id = id;
            DisplayName = displayName;
            Category = category;
            InventoryDefinition = inventoryDefinition;
            MarketValue = marketValue;
            IsSellable = isSellable;
            MaterialRarity = materialRarity;
            CompletedPartSlot = completedPartSlot;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public ShopItemCategory Category { get; }
        public ShopMaterialRarity? MaterialRarity { get; }
        public PartSlot? CompletedPartSlot { get; }
        public object InventoryDefinition { get; }
        public int MarketValue { get; }
        public bool IsSellable { get; }

        private static void ValidateCategoryMetadata(
            ShopItemCategory category,
            ShopMaterialRarity? rarity,
            PartSlot? partSlot)
        {
            ShopMaterialRarity? expectedRarity = null;
            switch (category)
            {
                case ShopItemCategory.CommonMaterial:
                    expectedRarity = ShopMaterialRarity.Common;
                    break;
                case ShopItemCategory.RareMaterial:
                    expectedRarity = ShopMaterialRarity.Rare;
                    break;
                case ShopItemCategory.EpicMaterial:
                    expectedRarity = ShopMaterialRarity.Epic;
                    break;
            }

            if (expectedRarity.HasValue && rarity != expectedRarity)
            {
                throw new ArgumentException("Material rarity must match its shop category.", nameof(rarity));
            }

            if (!expectedRarity.HasValue && rarity.HasValue)
            {
                throw new ArgumentException("Only materials can declare a material rarity.", nameof(rarity));
            }

            if (category == ShopItemCategory.CompletedPart && !partSlot.HasValue)
            {
                throw new ArgumentException("A completed card part requires its usable slot.", nameof(partSlot));
            }

            if (category != ShopItemCategory.CompletedPart && partSlot.HasValue)
            {
                throw new ArgumentException("Only completed card parts can declare a part slot.", nameof(partSlot));
            }
        }
    }

    public sealed class PermanentShopItems
    {
        public PermanentShopItems(
            IShopItemDefinition papel,
            IShopItemDefinition krayola,
            IShopItemDefinition lapis)
        {
            Papel = ValidateSupply(papel, nameof(papel));
            Krayola = ValidateSupply(krayola, nameof(krayola));
            Lapis = ValidateSupply(lapis, nameof(lapis));
            if (Papel.Id == Krayola.Id || Papel.Id == Lapis.Id || Krayola.Id == Lapis.Id)
            {
                throw new ShopConfigurationException("Papel, Krayola, and Lapis require unique item IDs.");
            }
        }

        public IShopItemDefinition Papel { get; }
        public IShopItemDefinition Krayola { get; }
        public IShopItemDefinition Lapis { get; }

        internal IShopItemDefinition GetAt(int index)
        {
            switch (index)
            {
                case 0:
                    return Papel;
                case 1:
                    return Krayola;
                case 2:
                    return Lapis;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        private static IShopItemDefinition ValidateSupply(IShopItemDefinition item, string parameterName)
        {
            if (item == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            if (item.Category != ShopItemCategory.MaintenanceSupply)
            {
                throw new ShopConfigurationException(parameterName + " must be a maintenance supply.");
            }

            return item;
        }
    }

    public sealed class ShopItemCatalog
    {
        private readonly Dictionary<string, IShopItemDefinition> byId;
        private readonly Dictionary<ShopItemCategory, List<IShopItemDefinition>> byCategory;

        public ShopItemCatalog()
        {
            byId = new Dictionary<string, IShopItemDefinition>(StringComparer.Ordinal);
            byCategory = new Dictionary<ShopItemCategory, List<IShopItemDefinition>>();
            foreach (ShopItemCategory category in Enum.GetValues(typeof(ShopItemCategory)))
            {
                byCategory.Add(category, new List<IShopItemDefinition>());
            }
        }

        public int Version { get; private set; }

        public void Register(IShopItemDefinition item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            ValidateItem(item);

            if (byId.ContainsKey(item.Id))
            {
                throw new ShopConfigurationException("Duplicate shop item ID: " + item.Id);
            }

            byId.Add(item.Id, item);
            byCategory[item.Category].Add(item);
            Version++;
        }

        public bool TryGet(string itemId, out IShopItemDefinition item)
        {
            return byId.TryGetValue(itemId ?? string.Empty, out item);
        }

        public IReadOnlyList<IShopItemDefinition> GetCategory(ShopItemCategory category)
        {
            return byCategory[category];
        }

        public void ValidateRandomPools()
        {
            ValidateNotEmpty(ShopItemCategory.CommonMaterial);
            ValidateNotEmpty(ShopItemCategory.RareMaterial);
            ValidateNotEmpty(ShopItemCategory.EpicMaterial);
            ValidateNotEmpty(ShopItemCategory.CompletedPart);
        }

        private void ValidateNotEmpty(ShopItemCategory category)
        {
            if (byCategory[category].Count == 0)
            {
                throw new ShopConfigurationException(
                    "The required " + category + " catalog pool is empty.");
            }
        }

        private static void ValidateItem(IShopItemDefinition item)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.DisplayName))
            {
                throw new ShopConfigurationException("Catalog items require stable IDs and display names.");
            }

            if (!Enum.IsDefined(typeof(ShopItemCategory), item.Category) ||
                item.InventoryDefinition == null || item.MarketValue < 0)
            {
                throw new ShopConfigurationException("Catalog item " + item.Id + " has invalid shop metadata.");
            }

            bool isMaterial = item.Category == ShopItemCategory.CommonMaterial ||
                              item.Category == ShopItemCategory.RareMaterial ||
                              item.Category == ShopItemCategory.EpicMaterial;
            if (isMaterial != item.MaterialRarity.HasValue)
            {
                throw new ShopConfigurationException(
                    "Catalog item " + item.Id + " has inconsistent material rarity metadata.");
            }

            if ((item.Category == ShopItemCategory.CommonMaterial &&
                 item.MaterialRarity != ShopMaterialRarity.Common) ||
                (item.Category == ShopItemCategory.RareMaterial &&
                 item.MaterialRarity != ShopMaterialRarity.Rare) ||
                (item.Category == ShopItemCategory.EpicMaterial &&
                 item.MaterialRarity != ShopMaterialRarity.Epic))
            {
                throw new ShopConfigurationException(
                    "Catalog item " + item.Id + " has a rarity that does not match its category.");
            }

            if ((item.Category == ShopItemCategory.CompletedPart) != item.CompletedPartSlot.HasValue)
            {
                throw new ShopConfigurationException(
                    "Catalog item " + item.Id + " has inconsistent completed-part metadata.");
            }
        }
    }
}
