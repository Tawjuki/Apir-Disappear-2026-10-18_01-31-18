using System;

namespace ApirApir.Shop
{
    public enum ShopItemCategory : byte
    {
        CommonMaterial,
        RareMaterial,
        EpicMaterial,
        CompletedPart,
        MaintenanceSupply
    }

    public enum ShopListingKind : byte
    {
        Randomized,
        Permanent
    }

    public enum ShopLifecycleState : byte
    {
        NotGenerated,
        Closed,
        Open,
        Closing
    }

    public enum ShopTransactionKind : byte
    {
        Purchase,
        Sale
    }

    public enum ShopTransactionFailure : byte
    {
        None,
        NotAuthoritative,
        ShopClosed,
        PlayerNotAllowed,
        DuplicateCommand,
        StaleRotation,
        OfferNotFound,
        ItemNotFound,
        InvalidQuantity,
        OutOfStock,
        InsufficientFunds,
        InsufficientItems,
        ItemNotSellable,
        AccountTransactionFailed,
        InvalidRequest
    }

    public enum ShopEventType : byte
    {
        InventoryGenerated,
        RotationChanged,
        ShopOpened,
        ShopClosing,
        ShopClosed,
        PurchaseSucceeded,
        PurchaseRejected,
        SaleSucceeded,
        SaleRejected,
        CurrencyChanged,
        StockChanged,
        InventorySynchronized
    }

    public enum ShopDuplicatePolicy : byte
    {
        AllowRepeatedItems,
        DisallowRepeatedItems
    }

    public readonly struct ShopPurchaseRequest
    {
        public ShopPurchaseRequest(
            string matchId,
            long rotationVersion,
            string offerId,
            int quantity,
            string commandId)
        {
            MatchId = matchId ?? string.Empty;
            RotationVersion = rotationVersion;
            OfferId = offerId ?? string.Empty;
            Quantity = quantity;
            CommandId = commandId ?? string.Empty;
        }

        public string MatchId { get; }
        public long RotationVersion { get; }
        public string OfferId { get; }
        public int Quantity { get; }
        public string CommandId { get; }
    }

    public readonly struct ShopSaleRequest
    {
        public ShopSaleRequest(
            string matchId,
            long rotationVersion,
            string itemId,
            int quantity,
            string commandId)
        {
            MatchId = matchId ?? string.Empty;
            RotationVersion = rotationVersion;
            ItemId = itemId ?? string.Empty;
            Quantity = quantity;
            CommandId = commandId ?? string.Empty;
        }

        public string MatchId { get; }
        public long RotationVersion { get; }
        public string ItemId { get; }
        public int Quantity { get; }
        public string CommandId { get; }
    }

    public sealed class ShopTransactionResult
    {
        private ShopTransactionResult(
            bool succeeded,
            ShopTransactionFailure failure,
            ShopTransactionKind kind,
            string playerId,
            string itemId,
            string offerId,
            int quantity,
            int currencyAmount,
            int resultingBalance)
        {
            Succeeded = succeeded;
            Failure = failure;
            Kind = kind;
            PlayerId = playerId ?? string.Empty;
            ItemId = itemId ?? string.Empty;
            OfferId = offerId ?? string.Empty;
            Quantity = quantity;
            CurrencyAmount = currencyAmount;
            ResultingBalance = resultingBalance;
        }

        public bool Succeeded { get; }
        public ShopTransactionFailure Failure { get; }
        public ShopTransactionKind Kind { get; }
        public string PlayerId { get; }
        public string ItemId { get; }
        public string OfferId { get; }
        public int Quantity { get; }
        public int CurrencyAmount { get; }
        public int ResultingBalance { get; }

        internal static ShopTransactionResult Success(
            ShopTransactionKind kind,
            string playerId,
            string itemId,
            string offerId,
            int quantity,
            int currencyAmount,
            int resultingBalance)
        {
            return new ShopTransactionResult(
                true,
                ShopTransactionFailure.None,
                kind,
                playerId,
                itemId,
                offerId,
                quantity,
                currencyAmount,
                resultingBalance);
        }

        internal static ShopTransactionResult Rejected(
            ShopTransactionKind kind,
            ShopTransactionFailure failure,
            string playerId = null,
            string itemId = null,
            string offerId = null)
        {
            return new ShopTransactionResult(
                false,
                failure,
                kind,
                playerId,
                itemId,
                offerId,
                0,
                0,
                0);
        }
    }

    public sealed class ShopEvent
    {
        public ShopEvent(
            ShopEventType type,
            string matchId,
            long rotationVersion,
            string playerId = null,
            string subjectId = null,
            int value = 0,
            int secondaryValue = 0,
            ShopTransactionFailure failure = ShopTransactionFailure.None)
        {
            Type = type;
            MatchId = matchId ?? string.Empty;
            RotationVersion = rotationVersion;
            PlayerId = playerId ?? string.Empty;
            SubjectId = subjectId ?? string.Empty;
            Value = value;
            SecondaryValue = secondaryValue;
            Failure = failure;
        }

        public ShopEventType Type { get; }
        public string MatchId { get; }
        public long RotationVersion { get; }
        public string PlayerId { get; }
        public string SubjectId { get; }
        public int Value { get; }
        public int SecondaryValue { get; }
        public ShopTransactionFailure Failure { get; }
    }

    public sealed class ShopConfigurationException : Exception
    {
        public ShopConfigurationException(string message) : base(message)
        {
        }
    }

    public sealed class ShopSettings
    {
        public ShopSettings()
        {
            PhaseDuration = TimeSpan.FromSeconds(30);
            DuplicatePolicy = ShopDuplicatePolicy.AllowRepeatedItems;
        }

        public TimeSpan PhaseDuration { get; set; }
        public ShopDuplicatePolicy DuplicatePolicy { get; set; }

        internal void Validate()
        {
            if (PhaseDuration < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(PhaseDuration));
            }
        }
    }
}
