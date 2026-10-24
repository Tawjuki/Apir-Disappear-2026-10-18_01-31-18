using System;

namespace ApirApir.Shop
{
    public interface IShopPricingPolicy
    {
        int CalculatePurchasePrice(IShopItemDefinition item, int quantity);
        int CalculateSalePayout(IShopItemDefinition item, int quantity);
    }

    /// <summary>
    /// Purchases cost 100% of market value. Sales pay 75%, rounded once after quantity
    /// multiplication using the injected midpoint rule.
    /// </summary>
    public sealed class MarketValueShopPricingPolicy : IShopPricingPolicy
    {
        private readonly decimal saleRate;
        private readonly MidpointRounding saleRounding;

        public MarketValueShopPricingPolicy(
            decimal saleRate = 0.75m,
            MidpointRounding saleRounding = MidpointRounding.AwayFromZero)
        {
            if (saleRate < 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(saleRate));
            }

            this.saleRate = saleRate;
            this.saleRounding = saleRounding;
        }

        public int CalculatePurchasePrice(IShopItemDefinition item, int quantity)
        {
            Validate(item, quantity);
            return checked(item.MarketValue * quantity);
        }

        public int CalculateSalePayout(IShopItemDefinition item, int quantity)
        {
            Validate(item, quantity);
            decimal unrounded = item.MarketValue * (decimal)quantity * saleRate;
            return checked((int)Math.Round(unrounded, 0, saleRounding));
        }

        private static void Validate(IShopItemDefinition item, int quantity)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            if (quantity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(quantity));
            }
        }
    }

    /// <summary>
    /// Adapter implemented by the authoritative inventory/currency system. TryPurchase
    /// and TrySell must update balance and inventory atomically, or make no changes.
    /// </summary>
    public interface IShopPlayerAccount
    {
        string PlayerId { get; }
        int CurrencyBalance { get; }
        int GetItemQuantity(string itemId);
        bool TryPurchase(
            IShopItemDefinition item,
            int quantity,
            int totalCost,
            out int resultingBalance,
            out int resultingItemQuantity);
        bool TrySell(
            IShopItemDefinition item,
            int quantity,
            int payout,
            out int resultingBalance,
            out int resultingItemQuantity);
    }

    public interface IShopPlayerAccountProvider
    {
        bool TryGetAccount(string playerId, out IShopPlayerAccount account);
    }

    public interface IShopAccessPolicy
    {
        bool CanShop(string playerId);
    }

    public sealed class AllowRegisteredShopPlayers : IShopAccessPolicy
    {
        private readonly IShopPlayerAccountProvider accounts;

        public AllowRegisteredShopPlayers(IShopPlayerAccountProvider accounts)
        {
            this.accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        }

        public bool CanShop(string playerId)
        {
            return accounts.TryGetAccount(playerId, out _);
        }
    }

    public interface IShopAuthority
    {
        bool IsAuthoritative { get; }
    }

    public sealed class FixedShopAuthority : IShopAuthority
    {
        public FixedShopAuthority(bool isAuthoritative)
        {
            IsAuthoritative = isAuthoritative;
        }

        public bool IsAuthoritative { get; }
    }
}
