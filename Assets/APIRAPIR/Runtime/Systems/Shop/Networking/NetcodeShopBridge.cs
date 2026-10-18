using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace ApirApir.Shop
{
    public interface INetcodeShopPlayerResolver
    {
        bool TryGetPlayerId(ulong clientId, out string playerId);
        bool TryGetClientId(string playerId, out ulong clientId);
        IReadOnlyList<ulong> GetMatchClientIds(string matchId);
    }

    public sealed class ShopNetworkSnapshot
    {
        internal ShopNetworkSnapshot(
            string matchId,
            string roundId,
            long rotationVersion,
            ShopLifecycleState state,
            long closesAtUnixMilliseconds,
            string[] offerIds,
            string[] itemIds,
            byte[] categories,
            byte[] listingKinds,
            int[] purchasePrices,
            int[] remainingStock,
            bool[] unlimitedStock)
        {
            MatchId = matchId;
            RoundId = roundId;
            RotationVersion = rotationVersion;
            State = state;
            ClosesAtUnixMilliseconds = closesAtUnixMilliseconds;
            OfferIds = offerIds;
            ItemIds = itemIds;
            Categories = categories;
            ListingKinds = listingKinds;
            PurchasePrices = purchasePrices;
            RemainingStock = remainingStock;
            UnlimitedStock = unlimitedStock;
        }

        public string MatchId { get; }
        public string RoundId { get; }
        public long RotationVersion { get; }
        public ShopLifecycleState State { get; }
        public long ClosesAtUnixMilliseconds { get; }
        public IReadOnlyList<string> OfferIds { get; }
        public IReadOnlyList<string> ItemIds { get; }
        public IReadOnlyList<byte> Categories { get; }
        public IReadOnlyList<byte> ListingKinds { get; }
        public IReadOnlyList<int> PurchasePrices { get; }
        public IReadOnlyList<int> RemainingStock { get; }
        public IReadOnlyList<bool> UnlimitedStock { get; }
    }

    /// <summary>
    /// Netcode for GameObjects transport adapter. It authenticates the sending connection;
    /// only stable IDs and compact state fields cross the network.
    /// </summary>
    public sealed class NetcodeShopBridge : NetworkBehaviour
    {
        private const int MaxSerializedSnapshotCharacters = 16384;

        [Serializable]
        private sealed class ShopSnapshotPayload
        {
            public string MatchId;
            public string RoundId;
            public long RotationVersion;
            public byte State;
            public long ClosesAtUnixMilliseconds;
            public string[] OfferIds;
            public string[] ItemIds;
            public byte[] Categories;
            public byte[] ListingKinds;
            public int[] PurchasePrices;
            public int[] RemainingStock;
            public bool[] UnlimitedStock;
        }

        private AuthoritativeShopService service;
        private INetcodeShopPlayerResolver playerResolver;
        private bool subscribed;

        public event Action<ShopEvent> RemoteEventReceived;
        public event Action<ShopNetworkSnapshot> RemoteSnapshotReceived;

        public void Initialize(
            AuthoritativeShopService authoritativeService,
            INetcodeShopPlayerResolver identityResolver)
        {
            Unsubscribe();
            service = authoritativeService ?? throw new ArgumentNullException(nameof(authoritativeService));
            playerResolver = identityResolver ?? throw new ArgumentNullException(nameof(identityResolver));
            SubscribeIfServer();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            SubscribeIfServer();
        }

        public override void OnNetworkDespawn()
        {
            Unsubscribe();
            base.OnNetworkDespawn();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void PurchaseServerRpc(
            string matchId,
            long rotationVersion,
            string offerId,
            int quantity,
            string commandId,
            RpcParams rpcParams = default)
        {
            if (TryAuthenticate(rpcParams.Receive.SenderClientId, out string playerId))
            {
                service.Purchase(
                    new ShopPurchaseRequest(matchId, rotationVersion, offerId, quantity, commandId),
                    playerId);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void SellServerRpc(
            string matchId,
            long rotationVersion,
            string itemId,
            int quantity,
            string commandId,
            RpcParams rpcParams = default)
        {
            if (TryAuthenticate(rpcParams.Receive.SenderClientId, out string playerId))
            {
                service.Sell(
                    new ShopSaleRequest(matchId, rotationVersion, itemId, quantity, commandId),
                    playerId);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestSnapshotServerRpc(string matchId, RpcParams rpcParams = default)
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;
            if (!TryAuthenticate(senderClientId, out string playerId) ||
                !service.TryCreateSnapshot(matchId, playerId, out ShopSnapshot snapshot))
            {
                return;
            }

            SendSnapshot(snapshot, senderClientId);
        }

        private bool TryAuthenticate(ulong senderClientId, out string playerId)
        {
            playerId = string.Empty;
            return service != null && playerResolver != null &&
                   playerResolver.TryGetPlayerId(senderClientId, out playerId);
        }

        private void SubscribeIfServer()
        {
            if (subscribed || !IsServer || service == null)
            {
                return;
            }

            service.EventPublished += ForwardEvent;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || service == null)
            {
                return;
            }

            service.EventPublished -= ForwardEvent;
            subscribed = false;
        }

        private void ForwardEvent(ShopEvent shopEvent)
        {
            if (shopEvent.Type == ShopEventType.RotationChanged &&
                service.TryCreateSnapshot(shopEvent.MatchId, string.Empty, out ShopSnapshot snapshot))
            {
                SendSnapshot(snapshot, playerResolver.GetMatchClientIds(shopEvent.MatchId));
            }

            ulong[] targets = ResolveEventTargets(shopEvent);
            if (targets.Length == 0)
            {
                return;
            }

            var rpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = targets }
            };
            PublishEventClientRpc(
                (byte)shopEvent.Type,
                shopEvent.MatchId,
                shopEvent.RotationVersion,
                shopEvent.PlayerId,
                shopEvent.SubjectId,
                shopEvent.Value,
                shopEvent.SecondaryValue,
                (byte)shopEvent.Failure,
                rpcParams);
        }

        private ulong[] ResolveEventTargets(ShopEvent shopEvent)
        {
            bool playerOnly = shopEvent.Type == ShopEventType.PurchaseSucceeded ||
                              shopEvent.Type == ShopEventType.PurchaseRejected ||
                              shopEvent.Type == ShopEventType.SaleSucceeded ||
                              shopEvent.Type == ShopEventType.SaleRejected ||
                              shopEvent.Type == ShopEventType.CurrencyChanged ||
                              shopEvent.Type == ShopEventType.InventorySynchronized;
            if (playerOnly)
            {
                return playerResolver.TryGetClientId(shopEvent.PlayerId, out ulong clientId)
                    ? new[] { clientId }
                    : Array.Empty<ulong>();
            }

            IReadOnlyList<ulong> recipients = playerResolver.GetMatchClientIds(shopEvent.MatchId);
            if (recipients == null)
            {
                return Array.Empty<ulong>();
            }

            var result = new ulong[recipients.Count];
            for (int i = 0; i < recipients.Count; i++)
            {
                result[i] = recipients[i];
            }

            return result;
        }

        private void SendSnapshot(ShopSnapshot snapshot, ulong targetClientId)
        {
            SendSnapshot(snapshot, new[] { targetClientId });
        }

        private void SendSnapshot(ShopSnapshot snapshot, IReadOnlyList<ulong> targetClientIds)
        {
            if (snapshot == null || targetClientIds == null || targetClientIds.Count == 0 ||
                !TrySerializeSnapshot(snapshot, out string serializedSnapshot))
            {
                return;
            }

            var targets = new ulong[targetClientIds.Count];
            for (int i = 0; i < targets.Length; i++)
            {
                targets[i] = targetClientIds[i];
            }

            var rpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = targets }
            };
            PublishSnapshotClientRpc(
                serializedSnapshot,
                rpcParams);
        }

        private static bool TrySerializeSnapshot(ShopSnapshot snapshot, out string serializedSnapshot)
        {
            serializedSnapshot = string.Empty;
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.MatchId) ||
                snapshot.Offers == null)
            {
                return false;
            }

            int count = snapshot.Offers.Count;
            if (count != 0 && count != ShopInventory.TotalListingCount)
            {
                return false;
            }

            var payload = new ShopSnapshotPayload
            {
                MatchId = snapshot.MatchId,
                RoundId = snapshot.RoundId ?? string.Empty,
                RotationVersion = snapshot.RotationVersion,
                State = (byte)snapshot.State,
                ClosesAtUnixMilliseconds = snapshot.ClosesAt.ToUnixTimeMilliseconds(),
                OfferIds = new string[count],
                ItemIds = new string[count],
                Categories = new byte[count],
                ListingKinds = new byte[count],
                PurchasePrices = new int[count],
                RemainingStock = new int[count],
                UnlimitedStock = new bool[count]
            };

            for (int i = 0; i < count; i++)
            {
                ShopOfferSnapshot offer = snapshot.Offers[i];
                if (offer == null || string.IsNullOrWhiteSpace(offer.OfferId) ||
                    string.IsNullOrWhiteSpace(offer.ItemId))
                {
                    return false;
                }

                payload.OfferIds[i] = offer.OfferId;
                payload.ItemIds[i] = offer.ItemId;
                payload.Categories[i] = (byte)offer.Category;
                payload.ListingKinds[i] = (byte)offer.ListingKind;
                payload.PurchasePrices[i] = offer.PurchasePrice;
                payload.RemainingStock[i] = offer.RemainingStock;
                payload.UnlimitedStock[i] = offer.IsUnlimitedStock;
            }

            try
            {
                serializedSnapshot = JsonUtility.ToJson(payload, false);
                return !string.IsNullOrEmpty(serializedSnapshot) &&
                       serializedSnapshot.Length <= MaxSerializedSnapshotCharacters;
            }
            catch (Exception)
            {
                serializedSnapshot = string.Empty;
                return false;
            }
        }

        private static bool TryDeserializeSnapshot(
            string serializedSnapshot,
            out ShopNetworkSnapshot snapshot)
        {
            snapshot = null;
            if (string.IsNullOrWhiteSpace(serializedSnapshot) ||
                serializedSnapshot.Length > MaxSerializedSnapshotCharacters)
            {
                return false;
            }

            ShopSnapshotPayload payload;
            try
            {
                payload = JsonUtility.FromJson<ShopSnapshotPayload>(serializedSnapshot);
            }
            catch (Exception)
            {
                return false;
            }

            if (!IsValidPayload(payload))
            {
                return false;
            }

            snapshot = new ShopNetworkSnapshot(
                payload.MatchId,
                payload.RoundId,
                payload.RotationVersion,
                (ShopLifecycleState)payload.State,
                payload.ClosesAtUnixMilliseconds,
                payload.OfferIds,
                payload.ItemIds,
                payload.Categories,
                payload.ListingKinds,
                payload.PurchasePrices,
                payload.RemainingStock,
                payload.UnlimitedStock);
            return true;
        }

        private static bool IsValidPayload(ShopSnapshotPayload payload)
        {
            if (payload == null || string.IsNullOrWhiteSpace(payload.MatchId) ||
                payload.RoundId == null || payload.RotationVersion < 0 ||
                !Enum.IsDefined(typeof(ShopLifecycleState), payload.State) ||
                payload.OfferIds == null)
            {
                return false;
            }

            int count = payload.OfferIds.Length;
            if ((count != 0 && count != ShopInventory.TotalListingCount) ||
                !HasLength(payload.ItemIds, count) ||
                !HasLength(payload.Categories, count) ||
                !HasLength(payload.ListingKinds, count) ||
                !HasLength(payload.PurchasePrices, count) ||
                !HasLength(payload.RemainingStock, count) ||
                !HasLength(payload.UnlimitedStock, count))
            {
                return false;
            }

            if (count == 0)
            {
                return payload.RotationVersion == 0 && string.IsNullOrEmpty(payload.RoundId);
            }

            if (payload.RotationVersion <= 0 || string.IsNullOrWhiteSpace(payload.RoundId))
            {
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                ShopListingKind expectedKind = i < ShopInventory.RandomizedListingCount
                    ? ShopListingKind.Randomized
                    : ShopListingKind.Permanent;
                if (string.IsNullOrWhiteSpace(payload.OfferIds[i]) ||
                    string.IsNullOrWhiteSpace(payload.ItemIds[i]) ||
                    !Enum.IsDefined(typeof(ShopItemCategory), payload.Categories[i]) ||
                    !Enum.IsDefined(typeof(ShopListingKind), payload.ListingKinds[i]) ||
                    (ShopListingKind)payload.ListingKinds[i] != expectedKind ||
                    payload.PurchasePrices[i] < 0 || payload.RemainingStock[i] < 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasLength(Array values, int expectedLength)
        {
            return values != null && values.Length == expectedLength;
        }

        [ClientRpc]
        private void PublishEventClientRpc(
            byte eventType,
            string matchId,
            long rotationVersion,
            string playerId,
            string subjectId,
            int value,
            int secondaryValue,
            byte failure,
            ClientRpcParams rpcParams = default)
        {
            RemoteEventReceived?.Invoke(new ShopEvent(
                (ShopEventType)eventType,
                matchId,
                rotationVersion,
                playerId,
                subjectId,
                value,
                secondaryValue,
                (ShopTransactionFailure)failure));
        }

        [ClientRpc]
        private void PublishSnapshotClientRpc(
            string serializedSnapshot,
            ClientRpcParams rpcParams = default)
        {
            if (TryDeserializeSnapshot(serializedSnapshot, out ShopNetworkSnapshot snapshot))
            {
                RemoteSnapshotReceived?.Invoke(snapshot);
            }
        }
    }
}
