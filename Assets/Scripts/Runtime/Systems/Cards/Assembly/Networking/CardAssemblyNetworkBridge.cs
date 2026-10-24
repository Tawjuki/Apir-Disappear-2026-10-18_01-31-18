using System;
using System.Collections.Generic;
using ApirApir.Battle;
using ApirApir.Crafting;
using Unity.Netcode;
using UnityEngine;

namespace ApirApir.Cards
{
    public interface INetcodeCardAssemblyPlayerResolver
    {
        bool TryGetPlayerId(ulong clientId, out string playerId);
        /// <summary>Returns the owner and any match clients authorized to observe this build.</summary>
        IReadOnlyList<ulong> GetCardObserverClientIds(string ownerPlayerId);
    }

    /// <summary>
    /// NGO transport adapter. RPC signatures use scalar supported types; the fixed three-slot
    /// snapshot is wrapped in a compact JSON object rather than passed as managed arrays.
    /// </summary>
    public sealed class CardAssemblyNetworkBridge : NetworkBehaviour
    {
        private const int MaximumIdentifierCharacters = 128;
        private const int MaximumPayloadCharacters = 4096;

        [Serializable]
        private sealed class SnapshotPayload
        {
            public string Owner;
            public string Card;
            public long Revision;
            public bool Locked;
            public string HeadInstance;
            public string HeadDefinition;
            public string HeadVisual;
            public int HeadMaintenance;
            public int HeadMaximum;
            public float HeadX;
            public float HeadY;
            public float HeadScale;
            public int HeadOrder;
            public byte HeadRegions;
            public string BodyInstance;
            public string BodyDefinition;
            public string BodyVisual;
            public int BodyMaintenance;
            public int BodyMaximum;
            public float BodyX;
            public float BodyY;
            public float BodyScale;
            public int BodyOrder;
            public byte BodyRegions;
            public string LegsInstance;
            public string LegsDefinition;
            public string LegsVisual;
            public int LegsMaintenance;
            public int LegsMaximum;
            public float LegsX;
            public float LegsY;
            public float LegsScale;
            public int LegsOrder;
            public byte LegsRegions;
            public int HitPoints;
            public int Attack;
            public float AttackPercent;
            public float SpeedPercent;
            public float CriticalPercent;
        }

        private AuthoritativeCardAssemblyService service;
        private INetcodeCardAssemblyPlayerResolver playerResolver;
        private bool subscribed;
        private readonly Dictionary<string, long> remoteRevisions =
            new Dictionary<string, long>(StringComparer.Ordinal);

        public event Action<CardBuildSnapshot> RemoteSnapshotReceived;
        public event Action<string, CardAssemblyFailure, long> RemoteRequestRejected;

        public void Initialize(
            AuthoritativeCardAssemblyService authoritativeService,
            INetcodeCardAssemblyPlayerResolver resolver)
        {
            Unsubscribe();
            service = authoritativeService ?? throw new ArgumentNullException(nameof(authoritativeService));
            playerResolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
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
        public void RequestEquipPartServerRpc(
            string cardId,
            byte slot,
            string partInstanceId,
            long expectedRevision,
            string commandId,
            RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (!TryResolveSender(sender, out string playerId) ||
                !IsBounded(cardId) || !IsBounded(partInstanceId) || !IsBounded(commandId) ||
                !Enum.IsDefined(typeof(PartSlot), slot) || expectedRevision < 0)
            {
                return;
            }

            CardAssemblyOperationResult result = service.EquipPart(
                playerId, cardId.Trim(), (PartSlot)slot, partInstanceId.Trim(), expectedRevision,
                commandId.Trim());
            ReplyIfRejected(sender, cardId, result);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestUnequipPartServerRpc(
            string cardId,
            byte slot,
            long expectedRevision,
            string commandId,
            RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (!TryResolveSender(sender, out string playerId) ||
                !IsBounded(cardId) || !IsBounded(commandId) ||
                !Enum.IsDefined(typeof(PartSlot), slot) || expectedRevision < 0)
            {
                return;
            }

            CardAssemblyOperationResult result = service.UnequipPart(
                playerId, cardId.Trim(), (PartSlot)slot, expectedRevision, commandId.Trim());
            ReplyIfRejected(sender, cardId, result);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestCardBuildSnapshotServerRpc(string cardId, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (!TryResolveSender(sender, out string playerId) || !IsBounded(cardId) ||
                !service.TryGetCardBuild(playerId, cardId.Trim(), out CardBuildSnapshot snapshot))
            {
                return;
            }

            SendSnapshot(snapshot, new[] { sender });
        }

        private bool TryResolveSender(ulong sender, out string playerId)
        {
            playerId = string.Empty;
            return service != null && playerResolver != null &&
                   playerResolver.TryGetPlayerId(sender, out playerId) && IsBounded(playerId);
        }

        private void SubscribeIfServer()
        {
            if (subscribed || !IsServer || service == null)
            {
                return;
            }

            service.CardConfigurationChanged += ForwardSnapshot;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || service == null)
            {
                return;
            }

            service.CardConfigurationChanged -= ForwardSnapshot;
            subscribed = false;
        }

        private void ForwardSnapshot(CardBuildSnapshot snapshot)
        {
            IReadOnlyList<ulong> recipients =
                playerResolver?.GetCardObserverClientIds(snapshot.OwnerPlayerId);
            if (recipients != null && recipients.Count > 0)
            {
                SendSnapshot(snapshot, recipients);
            }
        }

        private void SendSnapshot(CardBuildSnapshot snapshot, IReadOnlyList<ulong> recipients)
        {
            if (!TrySerialize(snapshot, out string payload))
            {
                return;
            }

            PublishCardBuildClientRpc(payload, CreateRpcParams(recipients));
        }

        [ClientRpc]
        private void PublishCardBuildClientRpc(
            string serializedSnapshot,
            ClientRpcParams rpcParams = default)
        {
            if (!TryDeserialize(serializedSnapshot, out CardBuildSnapshot snapshot))
            {
                return;
            }

            string key = snapshot.OwnerPlayerId + "\n" + snapshot.CardId;
            if (remoteRevisions.TryGetValue(key, out long revision) && snapshot.Revision <= revision)
            {
                return;
            }

            remoteRevisions[key] = snapshot.Revision;
            RemoteSnapshotReceived?.Invoke(snapshot);
        }

        [ClientRpc]
        private void PublishRejectionClientRpc(
            string cardId,
            byte failure,
            long revision,
            ClientRpcParams rpcParams = default)
        {
            if (!IsBounded(cardId) || !Enum.IsDefined(typeof(CardAssemblyFailure), failure) ||
                failure == (byte)CardAssemblyFailure.None || revision < 0)
            {
                return;
            }

            RemoteRequestRejected?.Invoke(cardId, (CardAssemblyFailure)failure, revision);
        }

        private void ReplyIfRejected(
            ulong sender,
            string cardId,
            CardAssemblyOperationResult result)
        {
            if (result.Succeeded)
            {
                return;
            }

            long revision = result.Snapshot == null ? 0 : result.Snapshot.Revision;
            ClientRpcParams target = CreateRpcParams(new[] { sender });
            PublishRejectionClientRpc(cardId.Trim(), (byte)result.Failure, revision, target);
            if (result.Snapshot != null)
            {
                SendSnapshot(result.Snapshot, new[] { sender });
            }
        }

        private static bool TrySerialize(CardBuildSnapshot snapshot, out string serialized)
        {
            serialized = string.Empty;
            if (snapshot == null || !IsBounded(snapshot.OwnerPlayerId) || !IsBounded(snapshot.CardId))
            {
                return false;
            }

            var payload = new SnapshotPayload
            {
                Owner = snapshot.OwnerPlayerId,
                Card = snapshot.CardId,
                Revision = snapshot.Revision,
                Locked = snapshot.IsLockedForBattle,
                HeadInstance = snapshot.Head.InstanceId,
                HeadDefinition = snapshot.Head.DefinitionId,
                HeadVisual = snapshot.Head.VisualAssetId,
                HeadMaintenance = snapshot.Head.CurrentMaintenance,
                HeadMaximum = snapshot.Head.MaximumMaintenance,
                HeadX = snapshot.Head.VisualOffsetX,
                HeadY = snapshot.Head.VisualOffsetY,
                HeadScale = snapshot.Head.VisualScale,
                HeadOrder = snapshot.Head.VisualLayerOrder,
                HeadRegions = (byte)snapshot.Head.OccupiedRegions,
                BodyInstance = snapshot.Body.InstanceId,
                BodyDefinition = snapshot.Body.DefinitionId,
                BodyVisual = snapshot.Body.VisualAssetId,
                BodyMaintenance = snapshot.Body.CurrentMaintenance,
                BodyMaximum = snapshot.Body.MaximumMaintenance,
                BodyX = snapshot.Body.VisualOffsetX,
                BodyY = snapshot.Body.VisualOffsetY,
                BodyScale = snapshot.Body.VisualScale,
                BodyOrder = snapshot.Body.VisualLayerOrder,
                BodyRegions = (byte)snapshot.Body.OccupiedRegions,
                LegsInstance = snapshot.Legs.InstanceId,
                LegsDefinition = snapshot.Legs.DefinitionId,
                LegsVisual = snapshot.Legs.VisualAssetId,
                LegsMaintenance = snapshot.Legs.CurrentMaintenance,
                LegsMaximum = snapshot.Legs.MaximumMaintenance,
                LegsX = snapshot.Legs.VisualOffsetX,
                LegsY = snapshot.Legs.VisualOffsetY,
                LegsScale = snapshot.Legs.VisualScale,
                LegsOrder = snapshot.Legs.VisualLayerOrder,
                LegsRegions = (byte)snapshot.Legs.OccupiedRegions,
                HitPoints = snapshot.Stats.MaximumHitPoints,
                Attack = snapshot.Stats.EffectiveAttack,
                AttackPercent = snapshot.Stats.AttackBonusPercent,
                SpeedPercent = snapshot.Stats.SpeedBonusPercent,
                CriticalPercent = snapshot.Stats.CriticalChanceBonusPercent
            };

            try
            {
                serialized = JsonUtility.ToJson(payload, false);
                return serialized.Length > 0 && serialized.Length <= MaximumPayloadCharacters;
            }
            catch (Exception)
            {
                serialized = string.Empty;
                return false;
            }
        }

        private static bool TryDeserialize(string serialized, out CardBuildSnapshot snapshot)
        {
            snapshot = null;
            if (string.IsNullOrWhiteSpace(serialized) || serialized.Length > MaximumPayloadCharacters)
            {
                return false;
            }

            SnapshotPayload payload;
            try
            {
                payload = JsonUtility.FromJson<SnapshotPayload>(serialized);
            }
            catch (Exception)
            {
                return false;
            }

            if (!ValidatePayload(payload))
            {
                return false;
            }

            snapshot = new CardBuildSnapshot(
                payload.Owner,
                payload.Card,
                payload.Revision,
                payload.Locked,
                CreateSlot(PartSlot.Head, payload.HeadInstance, payload.HeadDefinition,
                    payload.HeadVisual, payload.HeadMaintenance, payload.HeadMaximum,
                    payload.HeadX, payload.HeadY, payload.HeadScale, payload.HeadOrder,
                    (PartEquipmentRegion)payload.HeadRegions),
                CreateSlot(PartSlot.Body, payload.BodyInstance, payload.BodyDefinition,
                    payload.BodyVisual, payload.BodyMaintenance, payload.BodyMaximum,
                    payload.BodyX, payload.BodyY, payload.BodyScale, payload.BodyOrder,
                    (PartEquipmentRegion)payload.BodyRegions),
                CreateSlot(PartSlot.Legs, payload.LegsInstance, payload.LegsDefinition,
                    payload.LegsVisual, payload.LegsMaintenance, payload.LegsMaximum,
                    payload.LegsX, payload.LegsY, payload.LegsScale, payload.LegsOrder,
                    (PartEquipmentRegion)payload.LegsRegions),
                new CardEffectiveStats(
                    payload.HitPoints,
                    payload.Attack,
                    payload.AttackPercent,
                    new Dictionary<string, float>(StringComparer.Ordinal),
                    Array.Empty<string>(),
                    payload.SpeedPercent,
                    payload.CriticalPercent));
            return true;
        }

        private static CardPartSlotSnapshot CreateSlot(
            PartSlot slot,
            string instanceId,
            string definitionId,
            string visualId,
            int maintenance,
            int maximum,
            float offsetX,
            float offsetY,
            float scale,
            int order,
            PartEquipmentRegion occupiedRegions)
        {
            return CardPartSlotSnapshot.FromSerialized(
                slot, instanceId, definitionId, visualId, maintenance, maximum,
                offsetX, offsetY, scale, order, occupiedRegions);
        }

        private static bool ValidatePayload(SnapshotPayload value)
        {
            return value != null && IsBounded(value.Owner) && IsBounded(value.Card) &&
                   value.Revision >= 0 && value.HitPoints > 0 && value.Attack >= 0 &&
                   IsValidSlot(PartSlot.Head, value.HeadInstance, value.HeadDefinition, value.HeadVisual,
                       value.HeadMaintenance, value.HeadMaximum, value.HeadScale, value.HeadRegions) &&
                   IsValidSlot(PartSlot.Body, value.BodyInstance, value.BodyDefinition, value.BodyVisual,
                       value.BodyMaintenance, value.BodyMaximum, value.BodyScale, value.BodyRegions) &&
                   IsValidSlot(PartSlot.Legs, value.LegsInstance, value.LegsDefinition, value.LegsVisual,
                       value.LegsMaintenance, value.LegsMaximum, value.LegsScale, value.LegsRegions);
        }

        private static bool IsValidSlot(
            PartSlot slot,
            string instanceId,
            string definitionId,
            string visualId,
            int maintenance,
            int maximum,
            float scale,
            byte regionsValue)
        {
            bool empty = string.IsNullOrEmpty(instanceId) && string.IsNullOrEmpty(definitionId) &&
                         string.IsNullOrEmpty(visualId);
            if (empty)
            {
                return maintenance == 0 && maximum == 0 && regionsValue == 0;
            }

            var regions = (PartEquipmentRegion)regionsValue;
            const PartEquipmentRegion all =
                PartEquipmentRegion.Head | PartEquipmentRegion.Body | PartEquipmentRegion.Legs;
            PartEquipmentRegion primary = slot == PartSlot.Head
                ? PartEquipmentRegion.Head
                : slot == PartSlot.Body ? PartEquipmentRegion.Body : PartEquipmentRegion.Legs;
            return IsBounded(instanceId) && IsBounded(definitionId) && IsBounded(visualId) &&
                   maximum > 0 && maintenance >= 0 && maintenance <= maximum && scale > 0f &&
                   (regions & primary) != 0 && (regions & ~all) == 0;
        }

        private static ClientRpcParams CreateRpcParams(IReadOnlyList<ulong> recipients)
        {
            var targetIds = new ulong[recipients.Count];
            for (int i = 0; i < recipients.Count; i++)
            {
                targetIds[i] = recipients[i];
            }

            return new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = targetIds }
            };
        }

        private static bool IsBounded(string value)
        {
            return value != null && value.Length > 0 && value.Length <= MaximumIdentifierCharacters &&
                   string.Equals(value, value.Trim(), StringComparison.Ordinal);
        }
    }
}
