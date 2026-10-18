using System;
using System.Collections.Generic;

namespace ApirApir.Shop
{
    public sealed class ShopOfferSnapshot
    {
        internal ShopOfferSnapshot(ShopOffer offer)
        {
            OfferId = offer.OfferId;
            ItemId = offer.Item.Id;
            DisplayName = offer.Item.DisplayName;
            Category = offer.Item.Category;
            ListingKind = offer.ListingKind;
            PurchasePrice = offer.PurchasePrice;
            SellBackValue = offer.SellBackValue;
            IsUnlimitedStock = offer.IsUnlimitedStock;
            RemainingStock = offer.RemainingStock;
        }

        public string OfferId { get; }
        public string ItemId { get; }
        public string DisplayName { get; }
        public ShopItemCategory Category { get; }
        public ShopListingKind ListingKind { get; }
        public int PurchasePrice { get; }
        public int SellBackValue { get; }
        public bool IsUnlimitedStock { get; }
        public int RemainingStock { get; }
    }

    public sealed class ShopSnapshot
    {
        internal ShopSnapshot(
            string matchId,
            ShopLifecycleState state,
            DateTimeOffset closesAt,
            ShopInventory inventory)
        {
            MatchId = matchId;
            State = state;
            ClosesAt = closesAt;
            RoundId = inventory?.RoundId ?? string.Empty;
            RotationVersion = inventory?.RotationVersion ?? 0;
            if (inventory == null)
            {
                Offers = Array.Empty<ShopOfferSnapshot>();
                return;
            }

            var offers = new ShopOfferSnapshot[inventory.Offers.Count];
            for (int i = 0; i < offers.Length; i++)
            {
                offers[i] = new ShopOfferSnapshot(inventory.Offers[i]);
            }

            Offers = offers;
        }

        public string MatchId { get; }
        public string RoundId { get; }
        public long RotationVersion { get; }
        public ShopLifecycleState State { get; }
        public DateTimeOffset ClosesAt { get; }
        public IReadOnlyList<ShopOfferSnapshot> Offers { get; }
    }

    public sealed class ShopController
    {
        private readonly ShopSettings settings;
        private readonly IShopAuthority authority;
        private readonly ShopRotationGenerator rotationGenerator;
        private readonly ShopTransactionService transactions;
        private readonly HashSet<string> generatedRoundIds;
        private long nextRotationVersion;

        public ShopController(
            string matchId,
            ShopSettings settings,
            IShopAuthority authority,
            ShopRotationGenerator rotationGenerator,
            ShopItemCatalog catalog,
            IShopPricingPolicy pricing,
            IShopPlayerAccountProvider accounts,
            IShopAccessPolicy accessPolicy)
        {
            if (string.IsNullOrWhiteSpace(matchId))
            {
                throw new ArgumentException("A shop requires its match ID.", nameof(matchId));
            }

            MatchId = matchId;
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.settings.Validate();
            this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
            this.rotationGenerator = rotationGenerator ?? throw new ArgumentNullException(nameof(rotationGenerator));
            transactions = new ShopTransactionService(
                catalog,
                pricing,
                accounts,
                accessPolicy,
                Publish);
            generatedRoundIds = new HashSet<string>(StringComparer.Ordinal);
            State = ShopLifecycleState.NotGenerated;
        }

        public string MatchId { get; }
        public ShopLifecycleState State { get; private set; }
        public DateTimeOffset ClosesAt { get; private set; }
        public ShopInventory CurrentInventory { get; private set; }
        public event Action<ShopEvent> EventPublished;

        /// <summary>
        /// Called by MatchFlow upon entering Tyangge. Reusing the same round ID reopens
        /// the current rotation and never consumes more authoritative randomness.
        /// </summary>
        public bool BeginShopPhase(string roundId, DateTimeOffset now)
        {
            if (!authority.IsAuthoritative || string.IsNullOrWhiteSpace(roundId))
            {
                return false;
            }

            bool isNewRotation = CurrentInventory == null ||
                                 !string.Equals(CurrentInventory.RoundId, roundId, StringComparison.Ordinal);
            if (isNewRotation)
            {
                if (generatedRoundIds.Contains(roundId))
                {
                    return false;
                }

                long version = checked(nextRotationVersion + 1);
                ShopInventory generated = rotationGenerator.Generate(roundId, version);
                nextRotationVersion = version;
                generatedRoundIds.Add(roundId);
                CurrentInventory = generated;
                transactions.BeginRotation(version);
                Publish(new ShopEvent(
                    ShopEventType.InventoryGenerated,
                    MatchId,
                    version,
                    subjectId: roundId,
                    value: generated.Offers.Count));
                Publish(new ShopEvent(
                    ShopEventType.RotationChanged,
                    MatchId,
                    version,
                    subjectId: roundId));
            }

            State = ShopLifecycleState.Open;
            ClosesAt = now + settings.PhaseDuration;
            Publish(new ShopEvent(
                ShopEventType.ShopOpened,
                MatchId,
                CurrentInventory.RotationVersion,
                subjectId: CurrentInventory.RoundId,
                value: (int)Math.Ceiling(settings.PhaseDuration.TotalSeconds)));
            return true;
        }

        public bool BeginClosing()
        {
            if (!authority.IsAuthoritative || State != ShopLifecycleState.Open)
            {
                return false;
            }

            State = ShopLifecycleState.Closing;
            Publish(new ShopEvent(
                ShopEventType.ShopClosing,
                MatchId,
                CurrentInventory.RotationVersion));
            return true;
        }

        public bool EndShopPhase()
        {
            if (!authority.IsAuthoritative ||
                (State != ShopLifecycleState.Open && State != ShopLifecycleState.Closing))
            {
                return false;
            }

            State = ShopLifecycleState.Closed;
            Publish(new ShopEvent(
                ShopEventType.ShopClosed,
                MatchId,
                CurrentInventory.RotationVersion));
            return true;
        }

        public ShopTransactionResult Purchase(
            ShopPurchaseRequest request,
            string authenticatedPlayerId)
        {
            if (!authority.IsAuthoritative)
            {
                return ShopTransactionResult.Rejected(
                    ShopTransactionKind.Purchase,
                    ShopTransactionFailure.NotAuthoritative,
                    authenticatedPlayerId,
                    offerId: request.OfferId);
            }

            return transactions.Purchase(
                MatchId,
                CurrentInventory,
                State == ShopLifecycleState.Open,
                request,
                authenticatedPlayerId);
        }

        public ShopTransactionResult Sell(
            ShopSaleRequest request,
            string authenticatedPlayerId)
        {
            if (!authority.IsAuthoritative)
            {
                return ShopTransactionResult.Rejected(
                    ShopTransactionKind.Sale,
                    ShopTransactionFailure.NotAuthoritative,
                    authenticatedPlayerId,
                    request.ItemId);
            }

            return transactions.Sell(
                MatchId,
                CurrentInventory,
                State == ShopLifecycleState.Open,
                request,
                authenticatedPlayerId);
        }

        public ShopSnapshot CreateSnapshot(string requestingPlayerId = null)
        {
            ShopSnapshot snapshot = new ShopSnapshot(MatchId, State, ClosesAt, CurrentInventory);
            Publish(new ShopEvent(
                ShopEventType.InventorySynchronized,
                MatchId,
                CurrentInventory?.RotationVersion ?? 0,
                requestingPlayerId,
                value: snapshot.Offers.Count));
            return snapshot;
        }

        private void Publish(ShopEvent shopEvent)
        {
            EventPublished?.Invoke(shopEvent);
        }
    }
}
