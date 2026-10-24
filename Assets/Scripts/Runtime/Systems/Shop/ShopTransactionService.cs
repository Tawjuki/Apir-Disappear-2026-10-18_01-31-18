using System;
using System.Collections.Generic;

namespace ApirApir.Shop
{
    public sealed class ShopTransactionService
    {
        private readonly object transactionLock;
        private readonly ShopItemCatalog catalog;
        private readonly IShopPricingPolicy pricing;
        private readonly IShopPlayerAccountProvider accounts;
        private readonly IShopAccessPolicy accessPolicy;
        private readonly HashSet<string> processedCommands;
        private readonly Action<ShopEvent> publish;
        private long commandRotationVersion;

        public ShopTransactionService(
            ShopItemCatalog catalog,
            IShopPricingPolicy pricing,
            IShopPlayerAccountProvider accounts,
            IShopAccessPolicy accessPolicy,
            Action<ShopEvent> publish)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.pricing = pricing ?? throw new ArgumentNullException(nameof(pricing));
            this.accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
            this.accessPolicy = accessPolicy ?? throw new ArgumentNullException(nameof(accessPolicy));
            this.publish = publish ?? throw new ArgumentNullException(nameof(publish));
            transactionLock = new object();
            processedCommands = new HashSet<string>(StringComparer.Ordinal);
        }

        internal void BeginRotation(long rotationVersion)
        {
            lock (transactionLock)
            {
                commandRotationVersion = rotationVersion;
                processedCommands.Clear();
            }
        }

        public ShopTransactionResult Purchase(
            string matchId,
            ShopInventory inventory,
            bool shopIsOpen,
            ShopPurchaseRequest request,
            string authenticatedPlayerId)
        {
            lock (transactionLock)
            {
                ShopTransactionResult rejection = ValidateCommon(
                    matchId,
                    inventory,
                    shopIsOpen,
                    request.MatchId,
                    request.RotationVersion,
                    request.CommandId,
                    authenticatedPlayerId,
                    ShopTransactionKind.Purchase,
                    request.OfferId);
                if (rejection != null)
                {
                    return rejection;
                }

                if (request.Quantity <= 0)
                {
                    return RejectPurchase(matchId, inventory, authenticatedPlayerId, request,
                        ShopTransactionFailure.InvalidQuantity);
                }

                if (!inventory.TryGetOffer(request.OfferId, out ShopOffer offer))
                {
                    return RejectPurchase(matchId, inventory, authenticatedPlayerId, request,
                        ShopTransactionFailure.OfferNotFound);
                }

                if (!offer.CanPurchase(request.Quantity))
                {
                    return RejectPurchase(matchId, inventory, authenticatedPlayerId, request,
                        ShopTransactionFailure.OutOfStock, offer.Item.Id);
                }

                int totalCost;
                try
                {
                    totalCost = checked(offer.PurchasePrice * request.Quantity);
                }
                catch (OverflowException)
                {
                    return RejectPurchase(matchId, inventory, authenticatedPlayerId, request,
                        ShopTransactionFailure.InvalidRequest, offer.Item.Id);
                }

                accounts.TryGetAccount(authenticatedPlayerId, out IShopPlayerAccount account);
                if (account.CurrencyBalance < totalCost)
                {
                    return RejectPurchase(matchId, inventory, authenticatedPlayerId, request,
                        ShopTransactionFailure.InsufficientFunds, offer.Item.Id);
                }

                if (!account.TryPurchase(
                        offer.Item,
                        request.Quantity,
                        totalCost,
                        out int resultingBalance,
                        out int resultingQuantity))
                {
                    return RejectPurchase(matchId, inventory, authenticatedPlayerId, request,
                        ShopTransactionFailure.AccountTransactionFailed, offer.Item.Id);
                }

                offer.RemoveStock(request.Quantity);
                var result = ShopTransactionResult.Success(
                    ShopTransactionKind.Purchase,
                    authenticatedPlayerId,
                    offer.Item.Id,
                    offer.OfferId,
                    request.Quantity,
                    totalCost,
                    resultingBalance);
                publish(new ShopEvent(
                    ShopEventType.PurchaseSucceeded,
                    matchId,
                    inventory.RotationVersion,
                    authenticatedPlayerId,
                    offer.Item.Id,
                    request.Quantity,
                    totalCost));
                publish(new ShopEvent(
                    ShopEventType.CurrencyChanged,
                    matchId,
                    inventory.RotationVersion,
                    authenticatedPlayerId,
                    offer.Item.Id,
                    resultingBalance,
                    resultingQuantity));
                if (!offer.IsUnlimitedStock)
                {
                    publish(new ShopEvent(
                        ShopEventType.StockChanged,
                        matchId,
                        inventory.RotationVersion,
                        authenticatedPlayerId,
                        offer.OfferId,
                        offer.RemainingStock));
                }

                return result;
            }
        }

        public ShopTransactionResult Sell(
            string matchId,
            ShopInventory inventory,
            bool shopIsOpen,
            ShopSaleRequest request,
            string authenticatedPlayerId)
        {
            lock (transactionLock)
            {
                ShopTransactionResult rejection = ValidateCommon(
                    matchId,
                    inventory,
                    shopIsOpen,
                    request.MatchId,
                    request.RotationVersion,
                    request.CommandId,
                    authenticatedPlayerId,
                    ShopTransactionKind.Sale,
                    request.ItemId);
                if (rejection != null)
                {
                    return rejection;
                }

                if (request.Quantity <= 0)
                {
                    return RejectSale(matchId, inventory, authenticatedPlayerId, request,
                        ShopTransactionFailure.InvalidQuantity);
                }

                if (!catalog.TryGet(request.ItemId, out IShopItemDefinition item))
                {
                    return RejectSale(matchId, inventory, authenticatedPlayerId, request,
                        ShopTransactionFailure.ItemNotFound);
                }

                if (!item.IsSellable || !IsMaterial(item.Category))
                {
                    return RejectSale(matchId, inventory, authenticatedPlayerId, request,
                        ShopTransactionFailure.ItemNotSellable);
                }

                accounts.TryGetAccount(authenticatedPlayerId, out IShopPlayerAccount account);
                if (account.GetItemQuantity(item.Id) < request.Quantity)
                {
                    return RejectSale(matchId, inventory, authenticatedPlayerId, request,
                        ShopTransactionFailure.InsufficientItems);
                }

                int payout;
                try
                {
                    payout = pricing.CalculateSalePayout(item, request.Quantity);
                }
                catch (OverflowException)
                {
                    return RejectSale(matchId, inventory, authenticatedPlayerId, request,
                        ShopTransactionFailure.InvalidRequest);
                }

                if (!account.TrySell(
                        item,
                        request.Quantity,
                        payout,
                        out int resultingBalance,
                        out int resultingQuantity))
                {
                    return RejectSale(matchId, inventory, authenticatedPlayerId, request,
                        ShopTransactionFailure.AccountTransactionFailed);
                }

                var result = ShopTransactionResult.Success(
                    ShopTransactionKind.Sale,
                    authenticatedPlayerId,
                    item.Id,
                    string.Empty,
                    request.Quantity,
                    payout,
                    resultingBalance);
                publish(new ShopEvent(
                    ShopEventType.SaleSucceeded,
                    matchId,
                    inventory.RotationVersion,
                    authenticatedPlayerId,
                    item.Id,
                    request.Quantity,
                    payout));
                publish(new ShopEvent(
                    ShopEventType.CurrencyChanged,
                    matchId,
                    inventory.RotationVersion,
                    authenticatedPlayerId,
                    item.Id,
                    resultingBalance,
                    resultingQuantity));
                return result;
            }
        }

        private ShopTransactionResult ValidateCommon(
            string matchId,
            ShopInventory inventory,
            bool shopIsOpen,
            string requestMatchId,
            long requestRotationVersion,
            string commandId,
            string playerId,
            ShopTransactionKind kind,
            string subjectId)
        {
            ShopTransactionFailure failure = ShopTransactionFailure.None;
            if (!shopIsOpen || inventory == null)
            {
                failure = ShopTransactionFailure.ShopClosed;
            }
            else if (!string.Equals(matchId, requestMatchId, StringComparison.Ordinal))
            {
                failure = ShopTransactionFailure.InvalidRequest;
            }
            else if (!accessPolicy.CanShop(playerId) || !accounts.TryGetAccount(playerId, out _))
            {
                failure = ShopTransactionFailure.PlayerNotAllowed;
            }
            else if (requestRotationVersion != inventory.RotationVersion ||
                     requestRotationVersion != commandRotationVersion)
            {
                failure = ShopTransactionFailure.StaleRotation;
            }
            else
            {
                string commandKey = playerId + "\n" + commandId;
                if (string.IsNullOrWhiteSpace(commandId) || !processedCommands.Add(commandKey))
                {
                    failure = ShopTransactionFailure.DuplicateCommand;
                }
            }

            if (failure == ShopTransactionFailure.None)
            {
                return null;
            }

            ShopEventType eventType = kind == ShopTransactionKind.Purchase
                ? ShopEventType.PurchaseRejected
                : ShopEventType.SaleRejected;
            publish(new ShopEvent(
                eventType,
                matchId,
                inventory?.RotationVersion ?? 0,
                playerId,
                subjectId,
                failure: failure));
            return ShopTransactionResult.Rejected(kind, failure, playerId, subjectId, subjectId);
        }

        private ShopTransactionResult RejectPurchase(
            string matchId,
            ShopInventory inventory,
            string playerId,
            ShopPurchaseRequest request,
            ShopTransactionFailure failure,
            string itemId = null)
        {
            publish(new ShopEvent(
                ShopEventType.PurchaseRejected,
                matchId,
                inventory.RotationVersion,
                playerId,
                request.OfferId,
                failure: failure));
            return ShopTransactionResult.Rejected(
                ShopTransactionKind.Purchase,
                failure,
                playerId,
                itemId,
                request.OfferId);
        }

        private ShopTransactionResult RejectSale(
            string matchId,
            ShopInventory inventory,
            string playerId,
            ShopSaleRequest request,
            ShopTransactionFailure failure)
        {
            publish(new ShopEvent(
                ShopEventType.SaleRejected,
                matchId,
                inventory.RotationVersion,
                playerId,
                request.ItemId,
                failure: failure));
            return ShopTransactionResult.Rejected(
                ShopTransactionKind.Sale,
                failure,
                playerId,
                request.ItemId);
        }

        private static bool IsMaterial(ShopItemCategory category)
        {
            return category == ShopItemCategory.CommonMaterial ||
                   category == ShopItemCategory.RareMaterial ||
                   category == ShopItemCategory.EpicMaterial;
        }
    }
}
