using System;
using Unity.Netcode;

namespace ApirApir.Crafting
{
    public interface INetcodeCraftingPlayerResolver
    {
        bool TryGetPlayerId(ulong clientId, out string playerId);
        bool TryGetClientId(string playerId, out ulong clientId);
    }

    /// <summary>
    /// Netcode for GameObjects adapter for authenticated craft requests and private
    /// inventory/crafting events. Material rewards remain server-only.
    /// </summary>
    public sealed class NetcodeCraftingBridge : NetworkBehaviour
    {
        private AuthoritativeCraftingService service;
        private INetcodeCraftingPlayerResolver playerResolver;
        private bool subscribed;

        public event Action<CraftingEvent> RemoteEventReceived;

        public void Initialize(
            AuthoritativeCraftingService authoritativeService,
            INetcodeCraftingPlayerResolver identityResolver)
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
        public void CraftServerRpc(
            string matchId,
            string recipeId,
            string commandId,
            RpcParams rpcParams = default)
        {
            if (TryAuthenticate(rpcParams.Receive.SenderClientId, out string playerId))
            {
                service.Craft(new CraftingRequest(matchId, recipeId, commandId), playerId);
            }
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

        private void ForwardEvent(CraftingEvent craftingEvent)
        {
            if (!playerResolver.TryGetClientId(craftingEvent.PlayerId, out ulong clientId))
            {
                return;
            }

            var rpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            };
            PublishEventClientRpc(
                (byte)craftingEvent.Type,
                craftingEvent.MatchId,
                craftingEvent.PlayerId,
                craftingEvent.SubjectId,
                craftingEvent.Value,
                craftingEvent.SecondaryValue,
                (byte)craftingEvent.Failure,
                rpcParams);
        }

        [ClientRpc]
        private void PublishEventClientRpc(
            byte eventType,
            string matchId,
            string playerId,
            string subjectId,
            int value,
            int secondaryValue,
            byte failure,
            ClientRpcParams rpcParams = default)
        {
            RemoteEventReceived?.Invoke(new CraftingEvent(
                (CraftingEventType)eventType,
                matchId,
                playerId,
                subjectId,
                value,
                secondaryValue,
                (CraftingFailure)failure));
        }
    }
}
