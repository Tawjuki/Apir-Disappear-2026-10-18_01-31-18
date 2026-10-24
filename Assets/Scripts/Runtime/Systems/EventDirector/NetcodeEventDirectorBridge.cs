using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace ApirApir.EventDirector
{
    public interface INetcodeEventDirectorPlayerResolver
    {
        bool TryGetPlayerId(ulong clientId, out string playerId);
        IReadOnlyList<ulong> GetMatchClientIds(string matchId);
    }

    /// <summary>
    /// Synchronizes authoritative minigame plans and results. Arrays are wrapped in a compact
    /// JSON DTO and sent as one supported string RPC parameter for Netcode compatibility.
    /// </summary>
    public sealed class NetcodeEventDirectorBridge : NetworkBehaviour
    {
        private const int MaximumPayloadCharacters = 8192;
        private const int MaximumIdentifierCharacters = 128;

        [Serializable]
        private sealed class EventPayload
        {
            public byte Kind;
            public string MatchId;
            public string EventId;
            public long Sequence;
            public string MinigameId;
            public byte Format;
            public string[] PlayerIds;
            public byte[] Memberships;
            public byte[] Teams;
            public byte[] Roles;
            public byte WinningTeam;
            public byte LosingTeam;
            public byte CompletionStatus;
            public string[] ResultPlayerIds;
            public bool[] PlayerSucceeded;
            public int[] PlayerScores;
        }

        private AuthoritativeEventDirector director;
        private INetcodeEventDirectorPlayerResolver playerResolver;
        private bool subscribed;
        private long lastRemoteSequence;
        private string lastRemotePlanEventId = string.Empty;
        private readonly HashSet<string> receivedResultEventIds =
            new HashSet<string>(StringComparer.Ordinal);

        public event Action<MinigameEventPlan> RemotePlanReceived;
        public event Action<MinigameResult> RemoteResultReceived;

        public void Initialize(
            AuthoritativeEventDirector authoritativeDirector,
            INetcodeEventDirectorPlayerResolver resolver)
        {
            Unsubscribe();
            director = authoritativeDirector ?? throw new ArgumentNullException(nameof(authoritativeDirector));
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
        public void RequestEventSnapshotServerRpc(string matchId, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (!IsBounded(matchId) || director == null || playerResolver == null ||
                !string.Equals(director.MatchId, matchId.Trim(), StringComparison.Ordinal) ||
                !playerResolver.TryGetPlayerId(sender, out string playerId) || !IsBounded(playerId))
            {
                return;
            }

            if (director.CurrentPlan != null)
            {
                SendPlan(director.CurrentPlan, new[] { sender });
            }
            else if (director.LastResult != null)
            {
                SendResult(director.LastResult, new[] { sender });
            }
        }

        private void SubscribeIfServer()
        {
            if (subscribed || !IsServer || director == null)
            {
                return;
            }

            director.PlanPublished += ForwardPlan;
            director.ResultPublished += ForwardResult;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || director == null)
            {
                return;
            }

            director.PlanPublished -= ForwardPlan;
            director.ResultPublished -= ForwardResult;
            subscribed = false;
        }

        private void ForwardPlan(MinigameEventPlan plan)
        {
            SendPlan(plan, ResolveRecipients(plan.MatchId));
        }

        private void ForwardResult(MinigameResult result)
        {
            SendResult(result, ResolveRecipients(result.MatchId));
        }

        private ulong[] ResolveRecipients(string matchId)
        {
            IReadOnlyList<ulong> source = playerResolver == null
                ? null
                : playerResolver.GetMatchClientIds(matchId);
            if (source == null || source.Count == 0)
            {
                return Array.Empty<ulong>();
            }

            var result = new ulong[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                result[i] = source[i];
            }

            return result;
        }

        private void SendPlan(MinigameEventPlan plan, IReadOnlyList<ulong> recipients)
        {
            if (plan == null || recipients == null || recipients.Count == 0 ||
                !TrySerializePlan(plan, out string payload))
            {
                return;
            }

            PublishEventClientRpc(payload, CreateRpcParams(recipients));
        }

        private void SendResult(MinigameResult result, IReadOnlyList<ulong> recipients)
        {
            if (result == null || recipients == null || recipients.Count == 0 ||
                !TrySerializeResult(result, out string payload))
            {
                return;
            }

            PublishEventClientRpc(payload, CreateRpcParams(recipients));
        }

        [ClientRpc]
        private void PublishEventClientRpc(string serializedPayload, ClientRpcParams rpcParams = default)
        {
            if (!TryDeserialize(serializedPayload, out EventPayload payload, out MinigameEventPlan plan))
            {
                return;
            }

            if (payload.Kind == 0)
            {
                if (plan.Sequence <= lastRemoteSequence)
                {
                    return;
                }

                lastRemoteSequence = plan.Sequence;
                lastRemotePlanEventId = plan.EventId;
                RemotePlanReceived?.Invoke(plan);
                return;
            }

            if (plan.Sequence < lastRemoteSequence ||
                (plan.Sequence == lastRemoteSequence && lastRemotePlanEventId.Length > 0 &&
                 !string.Equals(lastRemotePlanEventId, plan.EventId, StringComparison.Ordinal)) ||
                !receivedResultEventIds.Add(plan.EventId))
            {
                return;
            }

            lastRemoteSequence = plan.Sequence;
            lastRemotePlanEventId = plan.EventId;
            var individualResults = new List<MinigameIndividualResult>(payload.ResultPlayerIds.Length);
            for (int i = 0; i < payload.ResultPlayerIds.Length; i++)
            {
                individualResults.Add(new MinigameIndividualResult(
                    payload.ResultPlayerIds[i], payload.PlayerSucceeded[i], payload.PlayerScores[i]));
            }

            var result = new MinigameResult(
                plan,
                (MinigameTeam)payload.WinningTeam,
                (MinigameTeam)payload.LosingTeam,
                (MinigameCompletionStatus)payload.CompletionStatus,
                individualResults.AsReadOnly());
            RemoteResultReceived?.Invoke(result);
        }

        private static bool TrySerializePlan(MinigameEventPlan plan, out string serialized)
        {
            return TrySerialize(CreatePayload(plan, null), out serialized);
        }

        private static bool TrySerializeResult(MinigameResult result, out string serialized)
        {
            return TrySerialize(CreatePayload(result.Plan, result), out serialized);
        }

        private static EventPayload CreatePayload(MinigameEventPlan plan, MinigameResult result)
        {
            int assignmentCount = plan.Assignments.Count;
            int resultCount = result == null ? 0 : result.IndividualResults.Count;
            var payload = new EventPayload
            {
                Kind = result == null ? (byte)0 : (byte)1,
                MatchId = plan.MatchId,
                EventId = plan.EventId,
                Sequence = plan.Sequence,
                MinigameId = plan.MinigameId,
                Format = (byte)plan.Format,
                PlayerIds = new string[assignmentCount],
                Memberships = new byte[assignmentCount],
                Teams = new byte[assignmentCount],
                Roles = new byte[assignmentCount],
                WinningTeam = result == null ? (byte)0 : (byte)result.WinningTeam,
                LosingTeam = result == null ? (byte)0 : (byte)result.LosingTeam,
                CompletionStatus = result == null ? (byte)0 : (byte)result.Status,
                ResultPlayerIds = new string[resultCount],
                PlayerSucceeded = new bool[resultCount],
                PlayerScores = new int[resultCount]
            };

            for (int i = 0; i < assignmentCount; i++)
            {
                MinigameParticipantAssignment assignment = plan.Assignments[i];
                payload.PlayerIds[i] = assignment.PlayerId;
                payload.Memberships[i] = (byte)assignment.Membership;
                payload.Teams[i] = (byte)assignment.Team;
                payload.Roles[i] = (byte)assignment.Role;
            }

            if (result != null)
            {
                for (int i = 0; i < resultCount; i++)
                {
                    MinigameIndividualResult individual = result.IndividualResults[i];
                    payload.ResultPlayerIds[i] = individual.PlayerId;
                    payload.PlayerSucceeded[i] = individual.Succeeded;
                    payload.PlayerScores[i] = individual.Score;
                }
            }

            return payload;
        }

        private static bool TrySerialize(EventPayload payload, out string serialized)
        {
            serialized = string.Empty;
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

        private static bool TryDeserialize(
            string serialized,
            out EventPayload payload,
            out MinigameEventPlan plan)
        {
            payload = null;
            plan = null;
            if (string.IsNullOrWhiteSpace(serialized) || serialized.Length > MaximumPayloadCharacters)
            {
                return false;
            }

            try
            {
                payload = JsonUtility.FromJson<EventPayload>(serialized);
            }
            catch (Exception)
            {
                return false;
            }

            if (!ValidatePayload(payload))
            {
                return false;
            }

            var assignments = new List<MinigameParticipantAssignment>(payload.PlayerIds.Length);
            for (int i = 0; i < payload.PlayerIds.Length; i++)
            {
                assignments.Add(new MinigameParticipantAssignment(
                    payload.PlayerIds[i],
                    (ChampionshipMembership)payload.Memberships[i],
                    (MinigameTeam)payload.Teams[i],
                    (MinigameParticipantRole)payload.Roles[i]));
            }

            plan = new MinigameEventPlan(
                payload.MatchId,
                payload.EventId,
                payload.Sequence,
                payload.MinigameId,
                (MinigameFormat)payload.Format,
                assignments.AsReadOnly());
            return true;
        }

        private static bool ValidatePayload(EventPayload payload)
        {
            if (payload == null || payload.Kind > 1 || !IsBounded(payload.MatchId) ||
                !IsBounded(payload.EventId) || !IsBounded(payload.MinigameId) ||
                payload.Sequence <= 0 || !Enum.IsDefined(typeof(MinigameFormat), payload.Format) ||
                payload.PlayerIds == null || payload.PlayerIds.Length != 4 ||
                !HasLength(payload.Memberships, 4) || !HasLength(payload.Teams, 4) ||
                !HasLength(payload.Roles, 4))
            {
                return false;
            }

            var players = new HashSet<string>(StringComparer.Ordinal);
            int teamA = 0;
            int teamB = 0;
            int activeA = 0;
            int activeB = 0;
            int activeCount = 0;
            int tayaCount = 0;
            int hiderCount = 0;
            int runnerCount = 0;
            int guardCount = 0;
            for (int i = 0; i < 4; i++)
            {
                if (!IsBounded(payload.PlayerIds[i]) || !players.Add(payload.PlayerIds[i]) ||
                    !Enum.IsDefined(typeof(ChampionshipMembership), payload.Memberships[i]) ||
                    !Enum.IsDefined(typeof(MinigameTeam), payload.Teams[i]) ||
                    !Enum.IsDefined(typeof(MinigameParticipantRole), payload.Roles[i]))
                {
                    return false;
                }

                bool active = (ChampionshipMembership)payload.Memberships[i] ==
                              ChampionshipMembership.ActiveContender;
                if ((MinigameTeam)payload.Teams[i] == MinigameTeam.TeamA)
                {
                    teamA++;
                    if (active) activeA++;
                }
                else
                {
                    teamB++;
                    if (active) activeB++;
                }

                if (active) activeCount++;
                switch ((MinigameParticipantRole)payload.Roles[i])
                {
                    case MinigameParticipantRole.Taya: tayaCount++; break;
                    case MinigameParticipantRole.Hider: hiderCount++; break;
                    case MinigameParticipantRole.Runner: runnerCount++; break;
                    case MinigameParticipantRole.Guard: guardCount++; break;
                }
            }

            MinigameFormat format = (MinigameFormat)payload.Format;
            bool validAssignment = format switch
            {
                MinigameFormat.OneVsThree =>
                    teamA == 3 && teamB == 1 &&
                    tayaCount == 1 && hiderCount == 3 &&
                    runnerCount == 0 && guardCount == 0 &&
                    (activeCount == 4 ||
                     (activeCount == 3 && activeA == 3 && activeB == 0)),
                MinigameFormat.TwoVsTwo =>
                    teamA == 2 && teamB == 2 && runnerCount == 2 && guardCount == 2 &&
                    (activeCount == 4 || (activeCount == 2 && activeA == 1 && activeB == 1)),
                _ => false
            };
            if (!validAssignment)
            {
                return false;
            }

            int resultCount = payload.ResultPlayerIds == null ? -1 : payload.ResultPlayerIds.Length;
            if (payload.Kind == 0)
            {
                return resultCount == 0 && HasLength(payload.PlayerSucceeded, 0) &&
                       HasLength(payload.PlayerScores, 0);
            }

            if (!Enum.IsDefined(typeof(MinigameTeam), payload.WinningTeam) ||
                !Enum.IsDefined(typeof(MinigameTeam), payload.LosingTeam) ||
                payload.WinningTeam == payload.LosingTeam ||
                !Enum.IsDefined(typeof(MinigameCompletionStatus), payload.CompletionStatus) ||
                resultCount < 0 || resultCount > 4 ||
                !HasLength(payload.PlayerSucceeded, resultCount) ||
                !HasLength(payload.PlayerScores, resultCount))
            {
                return false;
            }

            var resultPlayers = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < resultCount; i++)
            {
                if (!players.Contains(payload.ResultPlayerIds[i]) ||
                    !resultPlayers.Add(payload.ResultPlayerIds[i]))
                {
                    return false;
                }
            }

            return true;
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

        private static bool HasLength(Array values, int expected) =>
            values != null && values.Length == expected;

        private static bool IsBounded(string value) =>
            value != null && value.Length > 0 && value.Length <= MaximumIdentifierCharacters &&
            string.Equals(value, value.Trim(), StringComparison.Ordinal);
    }
}
