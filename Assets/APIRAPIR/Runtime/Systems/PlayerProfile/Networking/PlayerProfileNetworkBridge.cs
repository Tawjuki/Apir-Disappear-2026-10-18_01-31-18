using System;
using System.Collections.Generic;
using Unity.Netcode;

namespace ApirApir.PlayerProfiles
{
    public interface IPlayerProfileConnectionResolver
    {
        bool TryGetPlayerId(ulong clientId, out string playerId);
        IReadOnlyList<ulong> GetConnectedClientIds();
    }

    /// <summary>
    /// Netcode adapter for compact public profile replication. Persistent identity is supplied
    /// by the existing connection resolver; a temporary Netcode ClientId is never stored as identity.
    /// </summary>
    public sealed class PlayerProfileNetworkBridge : NetworkBehaviour
    {
        private const int MaximumIncomingNameLength = 128;
        private const int MaximumIncomingCharacterIdLength = 128;
        private const int MaximumIncomingPlayerIdLength = 128;

        private PlayerProfileService service;
        private IPlayerProfileConnectionResolver connectionResolver;
        private CloudPlayerProfileCoordinator authenticatedProfileCoordinator;
        private readonly Dictionary<string, PlayerProfileSnapshot> authenticatedVisibleProfiles =
            new Dictionary<string, PlayerProfileSnapshot>(StringComparer.Ordinal);
        private readonly PlayerNamePolicy playerNamePolicy = new PlayerNamePolicy();
        private bool subscribed;
        private string localAuthenticatedPlayerId = string.Empty;

        public event Action<PlayerProfileSnapshot> RemoteProfileReceived;
        public event Action<string> RemoteProfileCleared;

        public void Initialize(
            PlayerProfileService profileService,
            IPlayerProfileConnectionResolver resolver)
        {
            Unsubscribe();
            service = profileService ?? throw new ArgumentNullException(nameof(profileService));
            connectionResolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            SubscribeIfServer();
        }

        /// <summary>
        /// Binds the local authenticated account for public lobby presentation. The RPC sends no
        /// Player ID and never writes Cloud Save; the server associates the snapshot with the
        /// connection resolver's identity. This is not used as authorization for account changes.
        /// </summary>
        public void BindAuthenticatedProfile(CloudPlayerProfileCoordinator coordinator)
        {
            UnbindAuthenticatedProfile();
            authenticatedProfileCoordinator = coordinator ??
                throw new ArgumentNullException(nameof(coordinator));
            authenticatedProfileCoordinator.ProfileReady += ForwardLocalAuthenticatedProfile;
            authenticatedProfileCoordinator.ProfileCleared += ClearLocalAuthenticatedProfile;
            if (IsSpawned && authenticatedProfileCoordinator.IsReady)
            {
                ForwardLocalAuthenticatedProfile(authenticatedProfileCoordinator.CurrentProfile);
            }
        }

        public void UnbindAuthenticatedProfile()
        {
            if (authenticatedProfileCoordinator == null)
            {
                return;
            }

            authenticatedProfileCoordinator.ProfileReady -= ForwardLocalAuthenticatedProfile;
            authenticatedProfileCoordinator.ProfileCleared -= ClearLocalAuthenticatedProfile;
            authenticatedProfileCoordinator = null;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            SubscribeIfServer();
            if (authenticatedProfileCoordinator != null && authenticatedProfileCoordinator.IsReady)
            {
                ForwardLocalAuthenticatedProfile(authenticatedProfileCoordinator.CurrentProfile);
            }
        }

        public override void OnNetworkDespawn()
        {
            Unsubscribe();
            base.OnNetworkDespawn();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestPlayerNameChangeServerRpc(
            string requestedName,
            RpcParams rpcParams = default)
        {
            if (string.IsNullOrEmpty(requestedName) ||
                requestedName.Length > MaximumIncomingNameLength ||
                !TryAuthenticate(rpcParams.Receive.SenderClientId, out string playerId))
            {
                return;
            }

            if (authenticatedVisibleProfiles.ContainsKey(playerId))
            {
                return;
            }

            service.TryUpdatePlayerName(playerId, playerId, requestedName);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void SubmitAuthenticatedProfileServerRpc(
            string playerName,
            int gamesPlayed,
            RpcParams rpcParams = default)
        {
            if (gamesPlayed < 0 ||
                !playerNamePolicy.TryNormalize(playerName, out string normalizedName) ||
                !string.Equals(playerName, normalizedName, StringComparison.Ordinal) ||
                !TryAuthenticate(rpcParams.Receive.SenderClientId, out string playerId))
            {
                return;
            }

            var snapshot = new PlayerProfileSnapshot(
                playerId,
                normalizedName,
                string.Empty,
                gamesPlayed,
                0,
                0);
            if (authenticatedVisibleProfiles.TryGetValue(playerId, out PlayerProfileSnapshot existing) &&
                existing.PlayerName == snapshot.PlayerName &&
                existing.GamesPlayed == snapshot.GamesPlayed)
            {
                return;
            }

            authenticatedVisibleProfiles[playerId] = snapshot;
            IReadOnlyList<ulong> clients = connectionResolver.GetConnectedClientIds();
            if (clients == null || clients.Count == 0)
            {
                return;
            }

            var targets = new ulong[clients.Count];
            for (int i = 0; i < clients.Count; i++)
            {
                targets[i] = clients[i];
            }

            SendProfile(snapshot, targets);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestCharacterChangeServerRpc(
            string requestedCharacterId,
            RpcParams rpcParams = default)
        {
            if (string.IsNullOrEmpty(requestedCharacterId) ||
                requestedCharacterId.Length > MaximumIncomingCharacterIdLength ||
                !TryAuthenticate(rpcParams.Receive.SenderClientId, out string playerId))
            {
                return;
            }

            service.TryUpdateCharacter(playerId, playerId, requestedCharacterId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestVisibleProfilesServerRpc(RpcParams rpcParams = default)
        {
            ulong requestingClientId = rpcParams.Receive.SenderClientId;
            if (!TryAuthenticate(requestingClientId, out _))
            {
                return;
            }

            SendVisibleProfiles(requestingClientId);
        }

        /// <summary>Called by authoritative connection flow after a client joins or reconnects.</summary>
        public void SendVisibleProfiles(ulong targetClientId)
        {
            if (!IsServer || service == null || connectionResolver == null)
            {
                return;
            }

            IReadOnlyList<ulong> connectedClients = connectionResolver.GetConnectedClientIds();
            if (connectedClients == null)
            {
                return;
            }

            var sentPlayerIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < connectedClients.Count; i++)
            {
                if (!connectionResolver.TryGetPlayerId(connectedClients[i], out string playerId) ||
                    !sentPlayerIds.Add(playerId))
                {
                    continue;
                }

                if (authenticatedVisibleProfiles.TryGetValue(playerId, out PlayerProfileSnapshot cloudProfile))
                {
                    SendProfile(cloudProfile, new[] { targetClientId });
                }
                else if (service.TryGetProfile(playerId, out PlayerProfileSnapshot localProfile))
                {
                    SendProfile(localProfile, new[] { targetClientId });
                }
            }
        }

        private void ForwardLocalAuthenticatedProfile(PlayerProfileSnapshot profile)
        {
            if (!IsSpawned || profile == null)
            {
                return;
            }

            localAuthenticatedPlayerId = profile.PlayerId ?? string.Empty;
            SubmitAuthenticatedProfileServerRpc(profile.PlayerName, profile.GamesPlayed);
        }

        private void ClearLocalAuthenticatedProfile()
        {
            string clearedPlayerId = localAuthenticatedPlayerId;
            localAuthenticatedPlayerId = string.Empty;
            if (clearedPlayerId.Length == 0)
            {
                return;
            }

            authenticatedVisibleProfiles.Remove(clearedPlayerId);
            RemoteProfileCleared?.Invoke(clearedPlayerId);
        }

        private bool TryAuthenticate(ulong senderClientId, out string playerId)
        {
            playerId = string.Empty;
            return IsServer && service != null && connectionResolver != null &&
                   connectionResolver.TryGetPlayerId(senderClientId, out playerId) &&
                   !string.IsNullOrWhiteSpace(playerId);
        }

        private void SubscribeIfServer()
        {
            if (subscribed || !IsServer || service == null)
            {
                return;
            }

            service.ProfileCreated += ForwardProfile;
            service.ProfileLoaded += ForwardProfile;
            service.ProfileUpdated += ForwardProfile;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || service == null)
            {
                return;
            }

            service.ProfileCreated -= ForwardProfile;
            service.ProfileLoaded -= ForwardProfile;
            service.ProfileUpdated -= ForwardProfile;
            subscribed = false;
        }

        private void ForwardProfile(PlayerProfileSnapshot profile)
        {
            IReadOnlyList<ulong> clients = connectionResolver.GetConnectedClientIds();
            if (clients == null || clients.Count == 0)
            {
                return;
            }

            var targets = new ulong[clients.Count];
            for (int i = 0; i < clients.Count; i++)
            {
                targets[i] = clients[i];
            }

            SendProfile(profile, targets);
        }

        private void SendProfile(PlayerProfileSnapshot profile, ulong[] targets)
        {
            if (profile == null || targets == null || targets.Length == 0)
            {
                return;
            }

            var rpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = targets }
            };
            PublishProfileClientRpc(
                profile.PlayerId,
                profile.PlayerName,
                profile.SelectedCharacterId,
                profile.GamesPlayed,
                profile.GamesWon,
                profile.ThirdPlaceFinishes,
                rpcParams);
        }

        [ClientRpc]
        private void PublishProfileClientRpc(
            string playerId,
            string playerName,
            string selectedCharacterId,
            int gamesPlayed,
            int gamesWon,
            int thirdPlaceFinishes,
            ClientRpcParams rpcParams = default)
        {
            if (string.IsNullOrWhiteSpace(playerId) ||
                playerId.Length > MaximumIncomingPlayerIdLength ||
                string.IsNullOrWhiteSpace(playerName) ||
                playerName.Length > MaximumIncomingNameLength ||
                selectedCharacterId == null ||
                selectedCharacterId.Length > MaximumIncomingCharacterIdLength ||
                gamesPlayed < 0 || gamesWon < 0 || thirdPlaceFinishes < 0 ||
                gamesWon > gamesPlayed || thirdPlaceFinishes > gamesPlayed)
            {
                return;
            }

            RemoteProfileReceived?.Invoke(new PlayerProfileSnapshot(
                playerId,
                playerName,
                selectedCharacterId,
                gamesPlayed,
                gamesWon,
                thirdPlaceFinishes));
        }
    }
}
