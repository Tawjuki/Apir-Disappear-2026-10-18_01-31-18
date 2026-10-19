using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace ApirApir.Souls
{
    public interface INetcodeSoulPlayerResolver
    {
        bool TryGetPlayerId(ulong clientId, out string playerId);
        IReadOnlyList<ulong> GetPlayerObserverClientIds(string playerId);
    }

    /// <summary>
    /// Server-authoritative NGO adapter. RPCs use only NGO-supported scalar values. Variable
    /// ID collections are contained in one bounded JsonUtility wrapper string.
    /// </summary>
    public sealed class SoulProgressionNetworkBridge : NetworkBehaviour
    {
        private const int MaximumIdentifierCharacters = 128;
        private const int MaximumSnapshotCharacters = 8192;

        [Serializable]
        private sealed class SnapshotPayload
        {
            public string PlayerId;
            public long Revision;
            public int Talino;
            public string SelectedSoulId;
            public string[] OwnedSoulIds;
            public string[] UnlockedSkillIds;
            public string[] EquippedSkillIds;
        }

        private SoulProgressionService service;
        private INetcodeSoulPlayerResolver identities;
        private bool subscribed;
        private readonly Dictionary<string, long> remoteRevisions =
            new Dictionary<string, long>(StringComparer.Ordinal);

        public event Action<SoulProgressionSnapshot> RemoteSnapshotReceived;
        public event Action<SoulProgressionFailure, long> RemoteRequestRejected;

        public void Initialize(
            SoulProgressionService authoritativeService,
            INetcodeSoulPlayerResolver playerResolver)
        {
            Unsubscribe();
            service = authoritativeService ?? throw new ArgumentNullException(nameof(authoritativeService));
            identities = playerResolver ?? throw new ArgumentNullException(nameof(playerResolver));
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
        public void RequestSelectSoulServerRpc(
            string soulId,
            long expectedRevision,
            string commandId,
            RpcParams rpcParams = default)
        {
            if (!IsBounded(soulId)) return;
            Execute(rpcParams.Receive.SenderClientId, expectedRevision, commandId,
                playerId => service.SelectSoul(playerId, soulId, expectedRevision, commandId));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestUnlockSkillServerRpc(
            string skillId,
            long expectedRevision,
            string commandId,
            RpcParams rpcParams = default)
        {
            if (!IsBounded(skillId)) return;
            Execute(rpcParams.Receive.SenderClientId, expectedRevision, commandId,
                playerId => service.UnlockSkill(playerId, skillId, expectedRevision, commandId));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestEquipSkillServerRpc(
            string skillId,
            long expectedRevision,
            string commandId,
            RpcParams rpcParams = default)
        {
            if (!IsBounded(skillId)) return;
            Execute(rpcParams.Receive.SenderClientId, expectedRevision, commandId,
                playerId => service.EquipSkill(playerId, skillId, expectedRevision, commandId));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestUnequipSkillServerRpc(
            string skillId,
            long expectedRevision,
            string commandId,
            RpcParams rpcParams = default)
        {
            if (!IsBounded(skillId)) return;
            Execute(rpcParams.Receive.SenderClientId, expectedRevision, commandId,
                playerId => service.UnequipSkill(playerId, skillId, expectedRevision, commandId));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestSnapshotServerRpc(RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (TryResolve(sender, out string playerId) &&
                service.TryGetSnapshot(playerId, out SoulProgressionSnapshot snapshot))
            {
                SendSnapshot(snapshot, new[] { sender });
            }
        }

        private void Execute(
            ulong sender,
            long expectedRevision,
            string commandId,
            Func<string, SoulProgressionResult> operation)
        {
            if (!TryResolve(sender, out string playerId) || expectedRevision < 0 ||
                !IsBounded(commandId))
            {
                return;
            }

            SoulProgressionResult result = operation(playerId);
            if (!result.Succeeded)
            {
                PublishRejectionClientRpc(
                    (byte)result.Failure,
                    result.Snapshot == null ? 0 : result.Snapshot.Revision,
                    CreateRpcParams(new[] { sender }));
                if (result.Snapshot != null)
                {
                    SendSnapshot(result.Snapshot, new[] { sender });
                }
            }
        }

        private bool TryResolve(ulong sender, out string playerId)
        {
            playerId = string.Empty;
            return service != null && identities != null &&
                   identities.TryGetPlayerId(sender, out playerId) && IsBounded(playerId);
        }

        private void SubscribeIfServer()
        {
            if (subscribed || !IsServer || service == null) return;
            service.SnapshotChanged += ForwardSnapshot;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || service == null) return;
            service.SnapshotChanged -= ForwardSnapshot;
            subscribed = false;
        }

        private void ForwardSnapshot(SoulProgressionSnapshot snapshot)
        {
            IReadOnlyList<ulong> recipients = identities?.GetPlayerObserverClientIds(snapshot.PlayerId);
            if (recipients != null && recipients.Count > 0) SendSnapshot(snapshot, recipients);
        }

        private void SendSnapshot(
            SoulProgressionSnapshot snapshot,
            IReadOnlyList<ulong> recipients)
        {
            if (TrySerialize(snapshot, out string serialized))
            {
                PublishSnapshotClientRpc(serialized, CreateRpcParams(recipients));
            }
        }

        [ClientRpc]
        private void PublishSnapshotClientRpc(
            string serializedSnapshot,
            ClientRpcParams rpcParams = default)
        {
            if (!TryDeserialize(serializedSnapshot, out SoulProgressionSnapshot snapshot)) return;
            if (remoteRevisions.TryGetValue(snapshot.PlayerId, out long revision) &&
                snapshot.Revision <= revision) return;
            remoteRevisions[snapshot.PlayerId] = snapshot.Revision;
            RemoteSnapshotReceived?.Invoke(snapshot);
        }

        [ClientRpc]
        private void PublishRejectionClientRpc(
            byte failure,
            long revision,
            ClientRpcParams rpcParams = default)
        {
            if (!Enum.IsDefined(typeof(SoulProgressionFailure), failure) ||
                failure == (byte)SoulProgressionFailure.None || revision < 0) return;
            RemoteRequestRejected?.Invoke((SoulProgressionFailure)failure, revision);
        }

        private static bool TrySerialize(
            SoulProgressionSnapshot snapshot,
            out string serialized)
        {
            serialized = string.Empty;
            if (!ValidateSnapshot(snapshot)) return false;
            var payload = new SnapshotPayload
            {
                PlayerId = snapshot.PlayerId,
                Revision = snapshot.Revision,
                Talino = snapshot.Talino,
                SelectedSoulId = snapshot.SelectedSoulId,
                OwnedSoulIds = Copy(snapshot.OwnedSoulIds),
                UnlockedSkillIds = Copy(snapshot.UnlockedSkillIds),
                EquippedSkillIds = Copy(snapshot.EquippedSkillIds)
            };
            try
            {
                serialized = JsonUtility.ToJson(payload, false);
                return serialized.Length > 0 && serialized.Length <= MaximumSnapshotCharacters;
            }
            catch (Exception)
            {
                serialized = string.Empty;
                return false;
            }
        }

        private static bool TryDeserialize(
            string serialized,
            out SoulProgressionSnapshot snapshot)
        {
            snapshot = null;
            if (string.IsNullOrWhiteSpace(serialized) ||
                serialized.Length > MaximumSnapshotCharacters) return false;
            SnapshotPayload payload;
            try
            {
                payload = JsonUtility.FromJson<SnapshotPayload>(serialized);
            }
            catch (Exception)
            {
                return false;
            }

            if (payload == null || !IsBounded(payload.PlayerId) || payload.Revision < 0 ||
                payload.Talino < 0 || !IsOptionalBounded(payload.SelectedSoulId) ||
                !ValidateIds(payload.OwnedSoulIds, 5) ||
                !ValidateIds(payload.UnlockedSkillIds, 30) ||
                !ValidateIds(payload.EquippedSkillIds, 4)) return false;
            snapshot = new SoulProgressionSnapshot(
                payload.PlayerId,
                payload.Revision,
                payload.Talino,
                payload.SelectedSoulId,
                payload.OwnedSoulIds,
                payload.UnlockedSkillIds,
                payload.EquippedSkillIds);
            return ValidateSnapshot(snapshot);
        }

        private static bool ValidateSnapshot(SoulProgressionSnapshot snapshot)
        {
            return snapshot != null && IsBounded(snapshot.PlayerId) && snapshot.Revision >= 0 &&
                   snapshot.Talino >= 0 && IsOptionalBounded(snapshot.SelectedSoulId) &&
                   ValidateIds(snapshot.OwnedSoulIds, 5) &&
                   ValidateIds(snapshot.UnlockedSkillIds, 30) &&
                   ValidateIds(snapshot.EquippedSkillIds, 4);
        }

        private static bool ValidateIds(IReadOnlyList<string> values, int maximum)
        {
            if (values == null || values.Count > maximum) return false;
            var unique = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < values.Count; i++)
                if (!IsBounded(values[i]) || !unique.Add(values[i])) return false;
            return true;
        }

        private static string[] Copy(IReadOnlyList<string> source)
        {
            var result = new string[source.Count];
            for (int i = 0; i < result.Length; i++) result[i] = source[i];
            return result;
        }

        private static bool IsBounded(string value) =>
            !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumIdentifierCharacters;

        private static bool IsOptionalBounded(string value) =>
            string.IsNullOrEmpty(value) || value.Length <= MaximumIdentifierCharacters;

        private static ClientRpcParams CreateRpcParams(IReadOnlyList<ulong> recipients)
        {
            var ids = new ulong[recipients.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = recipients[i];
            return new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = ids }
            };
        }
    }
}
