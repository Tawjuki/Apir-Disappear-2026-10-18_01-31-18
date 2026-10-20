using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace ApirApir.Multiplayer
{
    /// <summary>
    /// Owns connection lifecycle, approval, lobby identity and ready state. Gameplay systems only consume
    /// its state/events and remain independent of LAN or Relay.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkSessionService : NetworkBehaviour
    {
        public const int MaximumPlayers = 4;
        private const string PersistentPlayerIdKey = "APIRAPIR.Multiplayer.PlayerId";
        private const int MaximumSnapshotCharacters = 8192;

        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private UnityTransport unityTransport;
        [SerializeField] private string onlineSessionName = "APIR APIR";

        private readonly AuthoritativeSessionRoster roster = new(MaximumPlayers);
        private SessionPlayerState[] visiblePlayers = Array.Empty<SessionPlayerState>();
        private ISession onlineSession;
        private bool operationInProgress;
        private bool callbacksRegistered;
        private ulong readyRequestSequence;
        private long snapshotRevision;
        private long lastAppliedSnapshotRevision;

        public static NetworkSessionService Instance { get; private set; }

        public MultiplayerConnectionMode ConnectionMode { get; private set; } = MultiplayerConnectionMode.Offline;
        public MultiplayerSessionState State { get; private set; } = MultiplayerSessionState.Offline;
        public IReadOnlyList<SessionPlayerState> Players => visiblePlayers;
        public string JoinCode { get; private set; } = string.Empty;
        public string LastError { get; private set; } = string.Empty;
        public int PlayerCount => visiblePlayers.Length;
        public bool IsSessionHost => networkManager != null && networkManager.IsHost;
        public bool IsConnected => networkManager != null && networkManager.IsListening;
        public bool CanStartMatch => IsSessionHost && State == MultiplayerSessionState.WaitingRoom &&
                                     visiblePlayers.Length == MaximumPlayers &&
                                     AllVisiblePlayersReady();

        public event Action SessionChanged;
        public event Action<SessionPlayerState> PlayerConnected;
        public event Action<ulong> PlayerDisconnected;
        public event Action MatchStarted;
        public event Action<string> ErrorRaised;

#if UNITY_EDITOR
        public void ConfigureForEditor(NetworkManager manager, UnityTransport transport)
        {
            networkManager = manager;
            unityTransport = transport;
        }
#endif

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError("Only one NetworkSessionService may exist in a scene.", this);
                enabled = false;
                return;
            }

            Instance = this;
            ResolveDependencies();
            RegisterCallbacks();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer)
            {
                PublishSnapshot();
            }
        }

        public override void OnDestroy()
        {
            UnregisterCallbacks();
            if (Instance == this)
            {
                Instance = null;
            }

            base.OnDestroy();
        }

        public Task<bool> HostLanAsync(string address, ushort port)
        {
            if (!ValidateEndpoint(address, port) || !TryBeginOperation())
            {
                return Task.FromResult(false);
            }

            try
            {
                PrepareConnection(MultiplayerConnectionMode.Lan, GetOrCreatePersistentPlayerId());
                unityTransport.SetConnectionData(address.Trim(), port, "0.0.0.0");
                if (!networkManager.StartHost())
                {
                    return Task.FromResult(FailAndReset("Unable to start the LAN host."));
                }

                return Task.FromResult(true);
            }
            catch (Exception exception)
            {
                return Task.FromResult(FailAndReset($"LAN host failed: {exception.Message}"));
            }
            finally
            {
                operationInProgress = false;
            }
        }

        public Task<bool> JoinLanAsync(string address, ushort port)
        {
            if (!ValidateEndpoint(address, port) || !TryBeginOperation())
            {
                return Task.FromResult(false);
            }

            try
            {
                PrepareConnection(MultiplayerConnectionMode.Lan, GetOrCreatePersistentPlayerId());
                unityTransport.SetConnectionData(address.Trim(), port);
                if (!networkManager.StartClient())
                {
                    return Task.FromResult(FailAndReset("Unable to start the LAN client."));
                }

                return Task.FromResult(true);
            }
            catch (Exception exception)
            {
                return Task.FromResult(FailAndReset($"LAN join failed: {exception.Message}"));
            }
            finally
            {
                operationInProgress = false;
            }
        }

        public async Task<bool> CreateOnlineSessionAsync()
        {
            if (!TryBeginOperation())
            {
                return false;
            }

            try
            {
                SetLocalState(MultiplayerSessionState.Connecting);
                await EnsureUnityServicesAsync();
                PrepareConnection(MultiplayerConnectionMode.OnlineRelay, AuthenticationService.Instance.PlayerId);

                var options = new SessionOptions
                {
                    MaxPlayers = MaximumPlayers,
                    Name = string.IsNullOrWhiteSpace(onlineSessionName) ? "APIR APIR" : onlineSessionName.Trim(),
                    IsPrivate = true
                }.WithRelayNetwork();

                onlineSession = await MultiplayerService.Instance.CreateSessionAsync(options);
                JoinCode = onlineSession.Code ?? string.Empty;
                NotifyChanged();
                return true;
            }
            catch (Exception exception)
            {
                return FailAndReset($"Online host failed: {exception.Message}");
            }
            finally
            {
                operationInProgress = false;
            }
        }

        public async Task<bool> JoinOnlineSessionAsync(string joinCode)
        {
            string normalizedCode = string.IsNullOrWhiteSpace(joinCode)
                ? string.Empty
                : joinCode.Trim().ToUpperInvariant();
            if (normalizedCode.Length == 0)
            {
                RaiseError("Enter a valid online join code.");
                return false;
            }

            if (!TryBeginOperation())
            {
                return false;
            }

            try
            {
                SetLocalState(MultiplayerSessionState.Connecting);
                await EnsureUnityServicesAsync();
                PrepareConnection(MultiplayerConnectionMode.OnlineRelay, AuthenticationService.Instance.PlayerId);
                onlineSession = await MultiplayerService.Instance.JoinSessionByCodeAsync(normalizedCode);
                JoinCode = onlineSession.Code ?? normalizedCode;
                NotifyChanged();
                return true;
            }
            catch (Exception exception)
            {
                return FailAndReset($"Online join failed: {exception.Message}");
            }
            finally
            {
                operationInProgress = false;
            }
        }

        public async Task LeaveAsync()
        {
            if (operationInProgress)
            {
                return;
            }

            operationInProgress = true;
            SetLocalState(MultiplayerSessionState.Disconnecting);
            try
            {
                if (onlineSession != null)
                {
                    await onlineSession.LeaveAsync();
                }
            }
            catch (Exception exception)
            {
                RaiseError($"Session cleanup warning: {exception.Message}");
            }
            finally
            {
                onlineSession = null;
                if (networkManager != null && networkManager.IsListening)
                {
                    networkManager.Shutdown();
                }

                ResetLocalSession();
                operationInProgress = false;
            }
        }

        public bool RequestReady(bool ready)
        {
            if (!IsSpawned || !IsClient || State != MultiplayerSessionState.WaitingRoom)
            {
                return false;
            }

            readyRequestSequence++;
            SetReadyRpc(ready, readyRequestSequence);
            return true;
        }

        public bool RequestStartMatch()
        {
            if (!IsSpawned || !IsClient || !IsSessionHost || !CanStartMatch)
            {
                return false;
            }

            StartMatchRpc();
            return true;
        }

        public SessionPlayerState GetLocalPlayer()
        {
            if (networkManager == null)
            {
                return null;
            }

            ulong localClientId = networkManager.LocalClientId;
            for (int i = 0; i < visiblePlayers.Length; i++)
            {
                if (visiblePlayers[i].clientId == localClientId)
                {
                    return visiblePlayers[i];
                }
            }

            return null;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SetReadyRpc(bool ready, ulong requestId, RpcParams rpcParams = default)
        {
            if (!roster.TrySetReady(rpcParams.Receive.SenderClientId, ready, requestId))
            {
                return;
            }

            PublishSnapshot();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void StartMatchRpc(RpcParams rpcParams = default)
        {
            if (!roster.TryBeginMatch(rpcParams.Receive.SenderClientId, NetworkManager.ServerClientId))
            {
                return;
            }

            PublishSnapshot();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PublishSnapshotRpc(string json)
        {
            ApplySnapshot(json);
        }

        private void ResolveDependencies()
        {
            if (networkManager == null)
            {
                networkManager = NetworkManager.Singleton != null
                    ? NetworkManager.Singleton
                    : FindFirstObjectByType<NetworkManager>();
            }

            if (unityTransport == null && networkManager != null)
            {
                unityTransport = networkManager.NetworkConfig.NetworkTransport as UnityTransport;
                if (unityTransport == null)
                {
                    unityTransport = networkManager.GetComponent<UnityTransport>();
                }
            }
        }

        private void RegisterCallbacks()
        {
            if (callbacksRegistered || networkManager == null)
            {
                return;
            }

            networkManager.NetworkConfig.ConnectionApproval = true;
            networkManager.ConnectionApprovalCallback += ApproveConnection;
            networkManager.OnClientConnectedCallback += OnClientConnected;
            networkManager.OnClientDisconnectCallback += OnClientDisconnected;
            callbacksRegistered = true;
        }

        private void UnregisterCallbacks()
        {
            if (!callbacksRegistered || networkManager == null)
            {
                return;
            }

            networkManager.ConnectionApprovalCallback -= ApproveConnection;
            networkManager.OnClientConnectedCallback -= OnClientConnected;
            networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            callbacksRegistered = false;
        }

        private void ApproveConnection(
            NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            ConnectionIdentity identity = DeserializeIdentity(request.Payload);
            string rejectionReason = string.Empty;
            bool approved = identity != null && roster.TryAdd(
                request.ClientNetworkId,
                identity.playerId,
                identity.sessionPlayerId,
                NetworkManager.ServerClientId,
                out rejectionReason);

            response.Approved = approved;
            response.CreatePlayerObject = approved;
            response.Pending = false;
            response.Reason = approved
                ? string.Empty
                : identity == null
                    ? "The client supplied an invalid APIR APIR connection identity."
                    : rejectionReason;
        }

        private void OnClientConnected(ulong clientId)
        {
            if (!networkManager.IsServer)
            {
                return;
            }

            SessionPlayerState player = roster.Find(clientId);
            if (player != null)
            {
                PlayerConnected?.Invoke(player.Clone());
            }

            PublishSnapshot();
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (networkManager != null && networkManager.IsServer)
            {
                if (roster.Remove(clientId))
                {
                    PlayerDisconnected?.Invoke(clientId);
                    PublishSnapshot();
                }

                return;
            }

            if (networkManager != null && clientId == networkManager.LocalClientId &&
                State != MultiplayerSessionState.Disconnecting)
            {
                string reason = string.IsNullOrWhiteSpace(networkManager.DisconnectReason)
                    ? "Disconnected from the host."
                    : networkManager.DisconnectReason;
                ResetLocalSession();
                RaiseError(reason);
            }
        }

        private void PrepareConnection(MultiplayerConnectionMode mode, string playerId)
        {
            ResolveDependencies();
            if (networkManager == null || unityTransport == null)
            {
                throw new InvalidOperationException("NetworkManager and UnityTransport must be configured.");
            }

            if (networkManager.IsListening)
            {
                throw new InvalidOperationException("Leave the current session before starting another one.");
            }

            roster.Clear();
            roster.BeginWaitingRoom();
            ConnectionMode = mode;
            JoinCode = string.Empty;
            LastError = string.Empty;
            visiblePlayers = Array.Empty<SessionPlayerState>();
            SetLocalState(MultiplayerSessionState.Connecting);

            var identity = new ConnectionIdentity
            {
                playerId = string.IsNullOrWhiteSpace(playerId) ? GetOrCreatePersistentPlayerId() : playerId,
                sessionPlayerId = Guid.NewGuid().ToString("N")
            };
            networkManager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(JsonUtility.ToJson(identity));
        }

        private bool ValidateEndpoint(string address, ushort port)
        {
            ResolveDependencies();
            if (networkManager == null || unityTransport == null)
            {
                RaiseError("NetworkManager and UnityTransport must be configured.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(address) || port == 0)
            {
                RaiseError("Enter a valid address and port from 1 to 65535.");
                return false;
            }

            return true;
        }

        private bool TryBeginOperation()
        {
            ResolveDependencies();
            if (operationInProgress)
            {
                RaiseError("A connection operation is already in progress.");
                return false;
            }

            if (networkManager == null || unityTransport == null)
            {
                RaiseError("NetworkManager and UnityTransport must be configured.");
                return false;
            }

            if (networkManager.IsListening)
            {
                RaiseError("Leave the current session before starting another one.");
                return false;
            }

            operationInProgress = true;
            return true;
        }

        private static async Task EnsureUnityServicesAsync()
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
            {
                await UnityServices.InitializeAsync();
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
        }

        private void PublishSnapshot()
        {
            if (!IsServer)
            {
                return;
            }

            var snapshot = new SessionSnapshot
            {
                revision = ++snapshotRevision,
                state = roster.State,
                players = roster.CreateSnapshot()
            };
            PublishSnapshotRpc(JsonUtility.ToJson(snapshot));
        }

        private void ApplySnapshot(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumSnapshotCharacters)
            {
                RaiseError("Received an invalid multiplayer session snapshot.");
                return;
            }

            SessionSnapshot snapshot;
            try
            {
                snapshot = JsonUtility.FromJson<SessionSnapshot>(json);
            }
            catch (Exception)
            {
                RaiseError("Received a malformed multiplayer session snapshot.");
                return;
            }

            if (!IsValidSnapshot(snapshot))
            {
                RaiseError("Received an unsupported multiplayer session snapshot.");
                return;
            }

            if (snapshot.revision <= lastAppliedSnapshotRevision)
            {
                return;
            }

            MultiplayerSessionState previousState = State;
            lastAppliedSnapshotRevision = snapshot.revision;
            visiblePlayers = snapshot.players;
            State = snapshot.state;
            if (State == MultiplayerSessionState.InMatch && previousState != MultiplayerSessionState.InMatch)
            {
                MatchStarted?.Invoke();
            }

            NotifyChanged();
        }

        private static bool IsValidSnapshot(SessionSnapshot snapshot)
        {
            if (snapshot == null || snapshot.version != SessionSnapshot.CurrentVersion ||
                snapshot.players == null || snapshot.players.Length > MaximumPlayers ||
                snapshot.state is not MultiplayerSessionState.WaitingRoom and not MultiplayerSessionState.InMatch)
            {
                return false;
            }

            var clientIds = new HashSet<ulong>();
            var playerIds = new HashSet<string>(StringComparer.Ordinal);
            var sessionIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < snapshot.players.Length; i++)
            {
                SessionPlayerState player = snapshot.players[i];
                if (player == null || string.IsNullOrWhiteSpace(player.playerId) ||
                    string.IsNullOrWhiteSpace(player.sessionPlayerId) ||
                    !clientIds.Add(player.clientId) || !playerIds.Add(player.playerId) ||
                    !sessionIds.Add(player.sessionPlayerId))
                {
                    return false;
                }
            }

            return true;
        }

        private static ConnectionIdentity DeserializeIdentity(byte[] payload)
        {
            if (payload == null || payload.Length == 0 || payload.Length > 1024)
            {
                return null;
            }

            try
            {
                ConnectionIdentity identity = JsonUtility.FromJson<ConnectionIdentity>(Encoding.UTF8.GetString(payload));
                return identity != null && identity.protocol == ConnectionIdentity.CurrentProtocol &&
                       !string.IsNullOrWhiteSpace(identity.playerId) &&
                       !string.IsNullOrWhiteSpace(identity.sessionPlayerId)
                    ? identity
                    : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private bool AllVisiblePlayersReady()
        {
            for (int i = 0; i < visiblePlayers.Length; i++)
            {
                if (!visiblePlayers[i].isReady)
                {
                    return false;
                }
            }

            return true;
        }

        private bool FailAndReset(string message)
        {
            if (networkManager != null && networkManager.IsListening)
            {
                networkManager.Shutdown();
            }

            onlineSession = null;
            ResetLocalSession();
            RaiseError(message);
            return false;
        }

        private void ResetLocalSession()
        {
            roster.Clear();
            visiblePlayers = Array.Empty<SessionPlayerState>();
            ConnectionMode = MultiplayerConnectionMode.Offline;
            JoinCode = string.Empty;
            readyRequestSequence = 0;
            lastAppliedSnapshotRevision = 0;
            SetLocalState(MultiplayerSessionState.Offline);
        }

        private void SetLocalState(MultiplayerSessionState state)
        {
            State = state;
            NotifyChanged();
        }

        private void RaiseError(string message)
        {
            LastError = string.IsNullOrWhiteSpace(message) ? "Unknown multiplayer error." : message;
            ErrorRaised?.Invoke(LastError);
            NotifyChanged();
        }

        private void NotifyChanged()
        {
            SessionChanged?.Invoke();
        }

        private static string GetOrCreatePersistentPlayerId()
        {
            string playerId = PlayerPrefs.GetString(PersistentPlayerIdKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(playerId))
            {
                return playerId;
            }

            playerId = Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(PersistentPlayerIdKey, playerId);
            PlayerPrefs.Save();
            return playerId;
        }
    }
}
