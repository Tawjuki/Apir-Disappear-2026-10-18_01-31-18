using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace ApirApir.Battle
{
    public interface INetcodeBattlePlayerResolver
    {
        bool TryGetPlayerId(ulong clientId, out string playerId);
        IReadOnlyList<ulong> GetBattleClientIds(string battleId);
    }

    /// <summary>
    /// Thin Netcode for GameObjects adapter. It authenticates player identity from the
    /// sending connection and forwards requests to the transport-independent service.
    /// Match/network bootstrap code must inject the service and identity resolver.
    /// </summary>
    public sealed class NetcodeBattleBridge : NetworkBehaviour
    {
        private const int MaximumIdentifierCharacters = 128;
        private AuthoritativeBattleService service;
        private INetcodeBattlePlayerResolver playerResolver;
        private bool subscribed;

        public event Action<BattleEvent> RemoteEventReceived;

        public void Initialize(
            AuthoritativeBattleService authoritativeService,
            INetcodeBattlePlayerResolver identityResolver)
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
        public void SubmitSkillServerRpc(
            string battleId,
            string commandId,
            string skillId,
            RpcParams rpcParams = default)
        {
            SubmitFromClient(
                new BattleActionRequest(battleId, commandId, BattleActionKind.UseSkill, skillId),
                rpcParams.Receive.SenderClientId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void SubmitPassServerRpc(
            string battleId,
            string commandId,
            RpcParams rpcParams = default)
        {
            SubmitFromClient(
                new BattleActionRequest(battleId, commandId, BattleActionKind.Pass),
                rpcParams.Receive.SenderClientId);
        }

        private void SubmitFromClient(BattleActionRequest request, ulong senderClientId)
        {
            if (service == null || playerResolver == null ||
                !playerResolver.TryGetPlayerId(senderClientId, out string playerId) ||
                !IsBounded(playerId) || !IsBounded(request.BattleId) ||
                !IsBounded(request.CommandId) ||
                (request.Kind == BattleActionKind.UseSkill && !IsBounded(request.SkillId)))
            {
                return;
            }

            service.Submit(request, playerId, DateTimeOffset.UtcNow);
        }

        private void SubscribeIfServer()
        {
            if (subscribed || !IsServer || service == null)
            {
                return;
            }

            service.EventPublished += ForwardEventToBattleClients;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || service == null)
            {
                return;
            }

            service.EventPublished -= ForwardEventToBattleClients;
            subscribed = false;
        }

        private void ForwardEventToBattleClients(BattleEvent battleEvent)
        {
            IReadOnlyList<ulong> recipients = playerResolver.GetBattleClientIds(battleEvent.BattleId);
            if (recipients == null || recipients.Count == 0)
            {
                return;
            }

            var targetIds = new ulong[recipients.Count];
            for (int i = 0; i < recipients.Count; i++)
            {
                targetIds[i] = recipients[i];
            }

            var rpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = targetIds }
            };
            PublishEventClientRpc(
                (byte)battleEvent.Type,
                battleEvent.BattleId,
                battleEvent.ExchangeNumber,
                battleEvent.ActorPlayerId,
                battleEvent.TargetPlayerId,
                battleEvent.SubjectId,
                battleEvent.Value,
                battleEvent.SecondaryValue,
                (byte)battleEvent.RejectionReason,
                rpcParams);
        }

        [ClientRpc]
        private void PublishEventClientRpc(
            byte eventType,
            string battleId,
            int exchangeNumber,
            string actorPlayerId,
            string targetPlayerId,
            string subjectId,
            int value,
            int secondaryValue,
            byte rejectionReason,
            ClientRpcParams rpcParams = default)
        {
            if (!Enum.IsDefined(typeof(BattleEventType), eventType) ||
                !Enum.IsDefined(typeof(ActionRejectionReason), rejectionReason) ||
                !IsBounded(battleId) || !IsOptionalBounded(actorPlayerId) ||
                !IsOptionalBounded(targetPlayerId) || !IsOptionalBounded(subjectId) ||
                exchangeNumber < 0)
            {
                return;
            }

            RemoteEventReceived?.Invoke(new BattleEvent(
                (BattleEventType)eventType,
                battleId,
                exchangeNumber,
                actorPlayerId,
                targetPlayerId,
                subjectId,
                value,
                secondaryValue,
                (ActionRejectionReason)rejectionReason));
        }

        private static bool IsBounded(string value) =>
            !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumIdentifierCharacters;

        private static bool IsOptionalBounded(string value) =>
            string.IsNullOrEmpty(value) || value.Length <= MaximumIdentifierCharacters;
    }
}
