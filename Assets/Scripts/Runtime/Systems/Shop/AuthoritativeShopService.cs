using System;
using System.Collections.Generic;

namespace ApirApir.Shop
{
    public sealed class AuthoritativeShopService
    {
        private readonly Dictionary<string, ShopController> shops;

        public AuthoritativeShopService()
        {
            shops = new Dictionary<string, ShopController>(StringComparer.Ordinal);
        }

        public event Action<ShopEvent> EventPublished;

        public bool Register(ShopController controller)
        {
            if (controller == null)
            {
                throw new ArgumentNullException(nameof(controller));
            }

            if (shops.ContainsKey(controller.MatchId))
            {
                return false;
            }

            shops.Add(controller.MatchId, controller);
            controller.EventPublished += ForwardEvent;
            return true;
        }

        public bool Unregister(string matchId)
        {
            if (!shops.TryGetValue(matchId ?? string.Empty, out ShopController controller))
            {
                return false;
            }

            controller.EventPublished -= ForwardEvent;
            shops.Remove(matchId);
            return true;
        }

        public bool TryGetShop(string matchId, out ShopController controller)
        {
            return shops.TryGetValue(matchId ?? string.Empty, out controller);
        }

        public ShopTransactionResult Purchase(ShopPurchaseRequest request, string authenticatedPlayerId)
        {
            if (!shops.TryGetValue(request.MatchId, out ShopController controller))
            {
                return ShopTransactionResult.Rejected(
                    ShopTransactionKind.Purchase,
                    ShopTransactionFailure.InvalidRequest,
                    authenticatedPlayerId,
                    offerId: request.OfferId);
            }

            return controller.Purchase(request, authenticatedPlayerId);
        }

        public ShopTransactionResult Sell(ShopSaleRequest request, string authenticatedPlayerId)
        {
            if (!shops.TryGetValue(request.MatchId, out ShopController controller))
            {
                return ShopTransactionResult.Rejected(
                    ShopTransactionKind.Sale,
                    ShopTransactionFailure.InvalidRequest,
                    authenticatedPlayerId,
                    request.ItemId);
            }

            return controller.Sell(request, authenticatedPlayerId);
        }

        public bool TryCreateSnapshot(
            string matchId,
            string requestingPlayerId,
            out ShopSnapshot snapshot)
        {
            if (!shops.TryGetValue(matchId ?? string.Empty, out ShopController controller))
            {
                snapshot = null;
                return false;
            }

            snapshot = controller.CreateSnapshot(requestingPlayerId);
            return true;
        }

        private void ForwardEvent(ShopEvent shopEvent)
        {
            EventPublished?.Invoke(shopEvent);
        }
    }
}
