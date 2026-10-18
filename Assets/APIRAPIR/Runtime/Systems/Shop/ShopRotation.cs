using System;
using System.Collections.Generic;

namespace ApirApir.Shop
{
    public interface IShopRandomProvider
    {
        double NextUnit();
        int NextIndex(int exclusiveMaximum);
    }

    public sealed class SystemShopRandomProvider : IShopRandomProvider
    {
        private readonly Random random;

        public SystemShopRandomProvider(int? seed = null)
        {
            random = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        public double NextUnit()
        {
            return random.NextDouble();
        }

        public int NextIndex(int exclusiveMaximum)
        {
            if (exclusiveMaximum <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
            }

            return random.Next(exclusiveMaximum);
        }
    }

    public readonly struct ShopStock
    {
        private ShopStock(bool isUnlimited, int quantity)
        {
            IsUnlimited = isUnlimited;
            Quantity = quantity;
        }

        public bool IsUnlimited { get; }
        public int Quantity { get; }

        public static ShopStock Unlimited()
        {
            return new ShopStock(true, 0);
        }

        public static ShopStock Limited(int quantity)
        {
            if (quantity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(quantity));
            }

            return new ShopStock(false, quantity);
        }
    }

    public interface IShopStockPolicy
    {
        ShopStock CreateInitialStock(IShopItemDefinition item, ShopListingKind listingKind, int listingIndex);
    }

    public sealed class UnlimitedShopStockPolicy : IShopStockPolicy
    {
        public ShopStock CreateInitialStock(
            IShopItemDefinition item,
            ShopListingKind listingKind,
            int listingIndex)
        {
            return ShopStock.Unlimited();
        }
    }

    public sealed class ShopOffer
    {
        internal ShopOffer(
            string offerId,
            ShopListingKind listingKind,
            IShopItemDefinition item,
            int purchasePrice,
            int sellBackValue,
            ShopStock stock)
        {
            OfferId = offerId ?? throw new ArgumentNullException(nameof(offerId));
            ListingKind = listingKind;
            Item = item ?? throw new ArgumentNullException(nameof(item));
            PurchasePrice = purchasePrice;
            SellBackValue = sellBackValue;
            IsUnlimitedStock = stock.IsUnlimited;
            RemainingStock = stock.Quantity;
        }

        public string OfferId { get; }
        public ShopListingKind ListingKind { get; }
        public IShopItemDefinition Item { get; }
        public int PurchasePrice { get; }
        public int SellBackValue { get; }
        public bool IsUnlimitedStock { get; }
        public int RemainingStock { get; private set; }
        public bool IsAvailable => IsUnlimitedStock || RemainingStock > 0;

        internal bool CanPurchase(int quantity)
        {
            return quantity > 0 && (IsUnlimitedStock || RemainingStock >= quantity);
        }

        internal void RemoveStock(int quantity)
        {
            if (!IsUnlimitedStock)
            {
                RemainingStock -= quantity;
            }
        }
    }

    public sealed class ShopInventory
    {
        public const int RandomizedListingCount = 6;
        public const int PermanentListingCount = 3;
        public const int TotalListingCount = 9;

        private readonly ShopOffer[] offers;
        private readonly Dictionary<string, ShopOffer> byOfferId;

        internal ShopInventory(string roundId, long rotationVersion, ShopOffer[] offers)
        {
            if (offers == null || offers.Length != TotalListingCount)
            {
                throw new ShopConfigurationException("Every shop rotation must contain exactly nine offers.");
            }

            RoundId = roundId ?? string.Empty;
            RotationVersion = rotationVersion;
            this.offers = (ShopOffer[])offers.Clone();
            byOfferId = new Dictionary<string, ShopOffer>(TotalListingCount, StringComparer.Ordinal);
            for (int i = 0; i < this.offers.Length; i++)
            {
                byOfferId.Add(this.offers[i].OfferId, this.offers[i]);
            }
        }

        public string RoundId { get; }
        public long RotationVersion { get; }
        public IReadOnlyList<ShopOffer> Offers => offers;

        public bool TryGetOffer(string offerId, out ShopOffer offer)
        {
            return byOfferId.TryGetValue(offerId ?? string.Empty, out offer);
        }
    }

    public sealed class ShopRotationGenerator
    {
        private readonly ShopItemCatalog catalog;
        private readonly PermanentShopItems permanentItems;
        private readonly IShopRandomProvider random;
        private readonly IShopPricingPolicy pricing;
        private readonly IShopStockPolicy stockPolicy;
        private readonly ShopDuplicatePolicy duplicatePolicy;

        public ShopRotationGenerator(
            ShopItemCatalog catalog,
            PermanentShopItems permanentItems,
            IShopRandomProvider random,
            IShopPricingPolicy pricing,
            IShopStockPolicy stockPolicy,
            ShopDuplicatePolicy duplicatePolicy = ShopDuplicatePolicy.AllowRepeatedItems)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.permanentItems = permanentItems ?? throw new ArgumentNullException(nameof(permanentItems));
            this.random = random ?? throw new ArgumentNullException(nameof(random));
            this.pricing = pricing ?? throw new ArgumentNullException(nameof(pricing));
            this.stockPolicy = stockPolicy ?? throw new ArgumentNullException(nameof(stockPolicy));
            this.duplicatePolicy = duplicatePolicy;
        }

        public ShopInventory Generate(string roundId, long rotationVersion)
        {
            if (string.IsNullOrWhiteSpace(roundId))
            {
                throw new ArgumentException("A rotation requires a round ID.", nameof(roundId));
            }

            if (rotationVersion <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(rotationVersion));
            }

            catalog.ValidateRandomPools();
            var offers = new ShopOffer[ShopInventory.TotalListingCount];
            HashSet<string> selectedIds = duplicatePolicy == ShopDuplicatePolicy.DisallowRepeatedItems
                ? new HashSet<string>(StringComparer.Ordinal)
                : null;

            for (int i = 0; i < ShopInventory.RandomizedListingCount; i++)
            {
                ShopItemCategory category = RollCategory(random.NextUnit());
                IShopItemDefinition item = SelectItem(category, selectedIds);
                offers[i] = CreateOffer(rotationVersion, i, ShopListingKind.Randomized, item);
            }

            for (int i = 0; i < ShopInventory.PermanentListingCount; i++)
            {
                int listingIndex = ShopInventory.RandomizedListingCount + i;
                offers[listingIndex] = CreateOffer(
                    rotationVersion,
                    listingIndex,
                    ShopListingKind.Permanent,
                    permanentItems.GetAt(i));
            }

            return new ShopInventory(roundId, rotationVersion, offers);
        }

        public static ShopItemCategory RollCategory(double roll)
        {
            if (double.IsNaN(roll) || roll < 0d || roll >= 1d)
            {
                throw new ArgumentOutOfRangeException(nameof(roll), "Category rolls must be in [0, 1).");
            }

            if (roll < 0.50d)
            {
                return ShopItemCategory.CommonMaterial;
            }

            if (roll < 0.75d)
            {
                return ShopItemCategory.RareMaterial;
            }

            if (roll < 0.90d)
            {
                return ShopItemCategory.EpicMaterial;
            }

            return ShopItemCategory.CompletedPart;
        }

        private IShopItemDefinition SelectItem(ShopItemCategory category, HashSet<string> selectedIds)
        {
            IReadOnlyList<IShopItemDefinition> pool = catalog.GetCategory(category);
            if (selectedIds == null)
            {
                return pool[GetValidatedIndex(pool.Count)];
            }

            int availableCount = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                if (!selectedIds.Contains(pool[i].Id))
                {
                    availableCount++;
                }
            }

            if (availableCount == 0)
            {
                throw new ShopConfigurationException(
                    "Duplicate prevention exhausted the " + category + " catalog without rerolling its category.");
            }

            int selectedAvailableIndex = GetValidatedIndex(availableCount);
            for (int i = 0; i < pool.Count; i++)
            {
                IShopItemDefinition candidate = pool[i];
                if (selectedIds.Contains(candidate.Id))
                {
                    continue;
                }

                if (selectedAvailableIndex-- == 0)
                {
                    selectedIds.Add(candidate.Id);
                    return candidate;
                }
            }

            throw new InvalidOperationException("No eligible item could be selected.");
        }

        private int GetValidatedIndex(int exclusiveMaximum)
        {
            int index = random.NextIndex(exclusiveMaximum);
            if (index < 0 || index >= exclusiveMaximum)
            {
                throw new ShopConfigurationException(
                    "The shop random provider returned an item index outside its requested range.");
            }

            return index;
        }

        private ShopOffer CreateOffer(
            long rotationVersion,
            int listingIndex,
            ShopListingKind listingKind,
            IShopItemDefinition item)
        {
            ShopStock stock = stockPolicy.CreateInitialStock(item, listingKind, listingIndex);
            return new ShopOffer(
                rotationVersion + ":" + listingIndex,
                listingKind,
                item,
                pricing.CalculatePurchasePrice(item, 1),
                pricing.CalculateSalePayout(item, 1),
                stock);
        }
    }
}
