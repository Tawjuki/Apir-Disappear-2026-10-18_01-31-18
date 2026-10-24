using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace ApirApir.TaguTaguan
{
    public interface ITaguTaguanNetworkPlayerResolver : ITaguTaguanWorldStateResolver
    {
        bool TryGetPlayerId(ulong clientId, out string playerId);
        IReadOnlyList<ulong> GetMatchClientIds(string matchId);
    }

    /// <summary>
    /// Accepts client intent and publishes compact JSON snapshots through one Netcode-supported
    /// string parameter. All positions, roles, timers, catches, and cooldowns remain server-owned.
    /// </summary>
    public sealed class TaguTaguanNetworkBridge : NetworkBehaviour
    {
        private const int MaximumPayloadCharacters = 16384;
        private const int MaximumIdentifierCharacters = 128;
        private const int ParticipantCount = 4;
        private const int MaximumNoiseCount = 16;

        [Serializable]
        private sealed class SnapshotPayload
        {
            public string MatchId;
            public string EventId;
            public long EventSequence;
            public long Revision;
            public byte Phase;
            public byte CompletionReason;
            public double StartedAt;
            public double EndsAt;
            public double CapturedAt;
            public string[] PlayerIds;
            public byte[] Roles;
            public bool[] Caught;
            public bool[] Detected;
            public double[] TurutotAvailableAt;
            public string[] NoiseIds;
            public string[] NoiseSourcePlayerIds;
            public float[] NoiseX;
            public float[] NoiseY;
            public float[] NoiseZ;
            public double[] NoiseActivatedAt;
            public double[] NoiseExpiresAt;
            public float[] NoiseRanges;
        }

        private TaguTaguanController controller;
        private ITaguTaguanNetworkPlayerResolver players;
        private ITaguTaguanMovementAdapter movement;
        private bool subscribed;
        private double nextDetectionRefreshAt;
        private readonly Dictionary<string, long> remoteRevisions =
            new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> remoteNoiseIds =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        public event Action<TaguTaguanSnapshot> RemoteSnapshotReceived;
        public event Action<TurutotNoiseEvent> RemoteNoiseCreated;
        public event Action<string> RemoteNoiseExpired;
        public event Action<string, TaguTaguanFailure> ServerRequestRejected;

        public void Initialize(
            TaguTaguanController authoritativeController,
            ITaguTaguanNetworkPlayerResolver playerResolver,
            ITaguTaguanMovementAdapter movementAdapter = null)
        {
            Unsubscribe();
            controller = authoritativeController ??
                throw new ArgumentNullException(nameof(authoritativeController));
            players = playerResolver ?? throw new ArgumentNullException(nameof(playerResolver));
            movement = movementAdapter;
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

        private void Update()
        {
            if (!IsServer || controller == null || controller.Phase != TaguTaguanPhase.Playing)
            {
                return;
            }

            double serverTime = GetServerTime();
            controller.Advance(serverTime);
            if (controller.Phase == TaguTaguanPhase.Playing && serverTime >= nextDetectionRefreshAt)
            {
                nextDetectionRefreshAt = serverTime + controller.Settings.detectionRefreshSeconds;
                controller.RefreshDetections(players);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestCatchServerRpc(
            string targetPlayerId,
            ulong requestId,
            RpcParams rpcParams = default)
        {
            if (!TryAuthenticate(rpcParams.Receive.SenderClientId, out string playerId))
            {
                return;
            }

            TaguTaguanOperationResult result = controller.TryCatch(
                playerId,
                targetPlayerId,
                requestId,
                GetServerTime(),
                players);
            if (!result.Succeeded)
            {
                ServerRequestRejected?.Invoke(playerId, result.Failure);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestTurutotServerRpc(ulong requestId, RpcParams rpcParams = default)
        {
            if (!TryAuthenticate(rpcParams.Receive.SenderClientId, out string playerId))
            {
                return;
            }

            TaguTaguanOperationResult result = controller.TryUseTurutot(
                playerId,
                requestId,
                GetServerTime(),
                players,
                out _);
            if (!result.Succeeded)
            {
                ServerRequestRejected?.Invoke(playerId, result.Failure);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestTaguTaguanSnapshotServerRpc(
            string eventId,
            RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (!TryAuthenticate(sender, out _) || !IsBounded(eventId) || controller.Plan == null ||
                !string.Equals(controller.Plan.EventId, eventId, StringComparison.Ordinal))
            {
                return;
            }

            SendSnapshot(controller.CreateSnapshot(GetServerTime()), new[] { sender });
        }

        private void SubscribeIfServer()
        {
            if (subscribed || !IsServer || controller == null)
            {
                return;
            }

            controller.StateChanged += ForwardSnapshot;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || controller == null)
            {
                return;
            }

            controller.StateChanged -= ForwardSnapshot;
            subscribed = false;
        }

        private bool TryAuthenticate(ulong clientId, out string playerId)
        {
            playerId = string.Empty;
            return IsServer && controller != null && players != null &&
                   players.TryGetPlayerId(clientId, out playerId) && IsBounded(playerId);
        }

        private void ForwardSnapshot(TaguTaguanSnapshot snapshot)
        {
            ApplyMovement(snapshot);
            IReadOnlyList<ulong> recipients = players?.GetMatchClientIds(snapshot.MatchId);
            SendSnapshot(snapshot, recipients);
        }

        private void SendSnapshot(TaguTaguanSnapshot snapshot, IReadOnlyList<ulong> recipients)
        {
            if (snapshot == null || recipients == null || recipients.Count == 0 ||
                !TrySerialize(snapshot, out string payload))
            {
                return;
            }

            var ids = new ulong[recipients.Count];
            for (int i = 0; i < recipients.Count; i++)
            {
                ids[i] = recipients[i];
            }

            PublishTaguTaguanSnapshotClientRpc(payload, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = ids }
            });
        }

        [ClientRpc]
        private void PublishTaguTaguanSnapshotClientRpc(
            string serializedSnapshot,
            ClientRpcParams rpcParams = default)
        {
            if (!TryDeserialize(serializedSnapshot, out TaguTaguanSnapshot snapshot))
            {
                return;
            }

            if (remoteRevisions.TryGetValue(snapshot.EventId, out long revision) &&
                snapshot.Revision <= revision)
            {
                return;
            }

            remoteRevisions[snapshot.EventId] = snapshot.Revision;
            PublishRemoteNoiseChanges(snapshot);
            ApplyMovement(snapshot);
            RemoteSnapshotReceived?.Invoke(snapshot);
        }

        private void ApplyMovement(TaguTaguanSnapshot snapshot)
        {
            if (movement == null || snapshot == null)
            {
                return;
            }

            if (snapshot.Phase == TaguTaguanPhase.Completed)
            {
                for (int i = 0; i < snapshot.Participants.Count; i++)
                {
                    movement.ClearTaguTaguanMovement(snapshot.Participants[i].PlayerId);
                }

                return;
            }

            for (int i = 0; i < snapshot.Participants.Count; i++)
            {
                TaguTaguanParticipantSnapshot participant = snapshot.Participants[i];
                bool movementAllowed = snapshot.Phase == TaguTaguanPhase.Playing &&
                                       (!participant.IsCaught || controller == null ||
                                        controller.Settings.caughtHidersMayMove);
                float speedMultiplier = participant.Role == TaguTaguanRole.Taya
                    ? controller?.Settings.tayaMovementSpeedMultiplier ?? 1.15f
                    : controller?.Settings.hiderMovementSpeedMultiplier ?? 1f;
                movement.ApplyTaguTaguanMovement(
                    participant.PlayerId,
                    participant.Role,
                    speedMultiplier,
                    movementAllowed);
            }
        }

        private void PublishRemoteNoiseChanges(TaguTaguanSnapshot snapshot)
        {
            if (!remoteNoiseIds.TryGetValue(snapshot.EventId, out HashSet<string> previous))
            {
                previous = new HashSet<string>(StringComparer.Ordinal);
                remoteNoiseIds.Add(snapshot.EventId, previous);
            }

            var current = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < snapshot.ActiveNoises.Count; i++)
            {
                TurutotNoiseEvent noise = snapshot.ActiveNoises[i];
                current.Add(noise.NoiseEventId);
                if (!previous.Contains(noise.NoiseEventId))
                {
                    RemoteNoiseCreated?.Invoke(noise);
                }
            }

            foreach (string noiseId in previous)
            {
                if (!current.Contains(noiseId))
                {
                    RemoteNoiseExpired?.Invoke(noiseId);
                }
            }

            remoteNoiseIds[snapshot.EventId] = current;
        }

        private static bool TrySerialize(TaguTaguanSnapshot snapshot, out string serialized)
        {
            serialized = string.Empty;
            if (snapshot.Participants == null || snapshot.Participants.Count != ParticipantCount ||
                snapshot.ActiveNoises == null || snapshot.ActiveNoises.Count > MaximumNoiseCount)
            {
                return false;
            }

            var payload = new SnapshotPayload
            {
                MatchId = snapshot.MatchId,
                EventId = snapshot.EventId,
                EventSequence = snapshot.EventSequence,
                Revision = snapshot.Revision,
                Phase = (byte)snapshot.Phase,
                CompletionReason = (byte)snapshot.CompletionReason,
                StartedAt = snapshot.StartedAt,
                EndsAt = snapshot.EndsAt,
                CapturedAt = snapshot.CapturedAt,
                PlayerIds = new string[ParticipantCount],
                Roles = new byte[ParticipantCount],
                Caught = new bool[ParticipantCount],
                Detected = new bool[ParticipantCount],
                TurutotAvailableAt = new double[ParticipantCount],
                NoiseIds = new string[snapshot.ActiveNoises.Count],
                NoiseSourcePlayerIds = new string[snapshot.ActiveNoises.Count],
                NoiseX = new float[snapshot.ActiveNoises.Count],
                NoiseY = new float[snapshot.ActiveNoises.Count],
                NoiseZ = new float[snapshot.ActiveNoises.Count],
                NoiseActivatedAt = new double[snapshot.ActiveNoises.Count],
                NoiseExpiresAt = new double[snapshot.ActiveNoises.Count],
                NoiseRanges = new float[snapshot.ActiveNoises.Count]
            };

            for (int i = 0; i < ParticipantCount; i++)
            {
                TaguTaguanParticipantSnapshot participant = snapshot.Participants[i];
                payload.PlayerIds[i] = participant.PlayerId;
                payload.Roles[i] = (byte)participant.Role;
                payload.Caught[i] = participant.IsCaught;
                payload.Detected[i] = participant.IsDetected;
                payload.TurutotAvailableAt[i] = participant.TurutotAvailableAt;
            }

            for (int i = 0; i < snapshot.ActiveNoises.Count; i++)
            {
                TurutotNoiseEvent noise = snapshot.ActiveNoises[i];
                payload.NoiseIds[i] = noise.NoiseEventId;
                payload.NoiseSourcePlayerIds[i] = noise.SourcePlayerId;
                payload.NoiseX[i] = noise.Position.x;
                payload.NoiseY[i] = noise.Position.y;
                payload.NoiseZ[i] = noise.Position.z;
                payload.NoiseActivatedAt[i] = noise.ActivatedAt;
                payload.NoiseExpiresAt[i] = noise.ExpiresAt;
                payload.NoiseRanges[i] = noise.NotificationRange;
            }

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

        private static bool TryDeserialize(string serialized, out TaguTaguanSnapshot snapshot)
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

            var participants = new List<TaguTaguanParticipantSnapshot>(ParticipantCount);
            for (int i = 0; i < ParticipantCount; i++)
            {
                participants.Add(new TaguTaguanParticipantSnapshot(
                    payload.PlayerIds[i],
                    (TaguTaguanRole)payload.Roles[i],
                    payload.Caught[i],
                    payload.Detected[i],
                    payload.TurutotAvailableAt[i]));
            }

            var noises = new List<TurutotNoiseEvent>(payload.NoiseIds.Length);
            for (int i = 0; i < payload.NoiseIds.Length; i++)
            {
                noises.Add(new TurutotNoiseEvent(
                    payload.NoiseIds[i],
                    payload.NoiseSourcePlayerIds[i],
                    new Vector3(payload.NoiseX[i], payload.NoiseY[i], payload.NoiseZ[i]),
                    payload.NoiseActivatedAt[i],
                    payload.NoiseExpiresAt[i],
                    payload.NoiseRanges[i]));
            }

            snapshot = new TaguTaguanSnapshot(
                payload.MatchId,
                payload.EventId,
                payload.EventSequence,
                payload.Revision,
                (TaguTaguanPhase)payload.Phase,
                payload.StartedAt,
                payload.EndsAt,
                payload.CapturedAt,
                (TaguTaguanCompletionReason)payload.CompletionReason,
                participants.AsReadOnly(),
                noises.AsReadOnly());
            return true;
        }

        private static bool ValidatePayload(SnapshotPayload payload)
        {
            if (payload == null || !IsBounded(payload.MatchId) || !IsBounded(payload.EventId) ||
                payload.EventSequence <= 0 || payload.Revision <= 0 ||
                !Enum.IsDefined(typeof(TaguTaguanPhase), payload.Phase) ||
                !Enum.IsDefined(typeof(TaguTaguanCompletionReason), payload.CompletionReason) ||
                !IsFinite(payload.StartedAt) || !IsFinite(payload.EndsAt) ||
                !IsFinite(payload.CapturedAt) || !HasLength(payload.PlayerIds, ParticipantCount) ||
                !HasLength(payload.Roles, ParticipantCount) || !HasLength(payload.Caught, ParticipantCount) ||
                !HasLength(payload.Detected, ParticipantCount) ||
                !HasLength(payload.TurutotAvailableAt, ParticipantCount) || payload.NoiseIds == null ||
                payload.NoiseIds.Length > MaximumNoiseCount)
            {
                return false;
            }

            int noiseCount = payload.NoiseIds.Length;
            if (!HasLength(payload.NoiseSourcePlayerIds, noiseCount) ||
                !HasLength(payload.NoiseX, noiseCount) || !HasLength(payload.NoiseY, noiseCount) ||
                !HasLength(payload.NoiseZ, noiseCount) || !HasLength(payload.NoiseActivatedAt, noiseCount) ||
                !HasLength(payload.NoiseExpiresAt, noiseCount) || !HasLength(payload.NoiseRanges, noiseCount))
            {
                return false;
            }

            var players = new HashSet<string>(StringComparer.Ordinal);
            int taya = 0;
            int hiders = 0;
            for (int i = 0; i < ParticipantCount; i++)
            {
                if (!IsBounded(payload.PlayerIds[i]) || !players.Add(payload.PlayerIds[i]) ||
                    !Enum.IsDefined(typeof(TaguTaguanRole), payload.Roles[i]) ||
                    !IsFinite(payload.TurutotAvailableAt[i]))
                {
                    return false;
                }

                switch ((TaguTaguanRole)payload.Roles[i])
                {
                    case TaguTaguanRole.Taya: taya++; break;
                    case TaguTaguanRole.Hider: hiders++; break;
                    default: return false;
                }
            }

            if (taya != 1 || hiders != 3)
            {
                return false;
            }

            var noiseIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < noiseCount; i++)
            {
                if (!IsBounded(payload.NoiseIds[i]) || !noiseIds.Add(payload.NoiseIds[i]) ||
                    !players.Contains(payload.NoiseSourcePlayerIds[i]) ||
                    !IsFinite(payload.NoiseX[i]) || !IsFinite(payload.NoiseY[i]) ||
                    !IsFinite(payload.NoiseZ[i]) || !IsFinite(payload.NoiseActivatedAt[i]) ||
                    !IsFinite(payload.NoiseExpiresAt[i]) || !IsFinite(payload.NoiseRanges[i]) ||
                    payload.NoiseExpiresAt[i] < payload.NoiseActivatedAt[i] || payload.NoiseRanges[i] <= 0f)
                {
                    return false;
                }
            }

            return true;
        }

        private double GetServerTime()
        {
            return NetworkManager != null && NetworkManager.IsListening
                ? NetworkManager.ServerTime.Time
                : Time.unscaledTimeAsDouble;
        }

        private static bool HasLength(Array values, int length) => values != null && values.Length == length;

        private static bool IsBounded(string value) =>
            value != null && value.Length > 0 && value.Length <= MaximumIdentifierCharacters &&
            string.Equals(value, value.Trim(), StringComparison.Ordinal);

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
