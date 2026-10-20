using System;
using UnityEngine;
using UnityEngine.UI;

namespace ApirApir.Multiplayer
{
    /// <summary>Thin event-driven uGUI adapter for the reusable session service.</summary>
    public sealed class NetworkSessionUI : MonoBehaviour
    {
        [Header("Service")]
        [SerializeField] private NetworkSessionService sessionService;

        [Header("LAN")]
        [SerializeField] private InputField addressInput;
        [SerializeField] private InputField portInput;
        [SerializeField] private Button hostLanButton;
        [SerializeField] private Button joinLanButton;

        [Header("Online")]
        [SerializeField] private InputField joinCodeInput;
        [SerializeField] private Button createOnlineButton;
        [SerializeField] private Button joinOnlineButton;

        [Header("Lobby")]
        [SerializeField] private Button readyButton;
        [SerializeField] private Button startMatchButton;
        [SerializeField] private Button leaveButton;
        [SerializeField] private Text statusText;
        [SerializeField] private Text joinCodeText;
        [SerializeField] private Text playerCountText;
        [SerializeField] private Text readyStatusText;

#if UNITY_EDITOR
        public void ConfigureForEditor(
            NetworkSessionService service,
            InputField address,
            InputField port,
            Button hostLan,
            Button joinLan,
            InputField code,
            Button createOnline,
            Button joinOnline,
            Button ready,
            Button startMatch,
            Button leave,
            Text status,
            Text displayedCode,
            Text playerCount,
            Text readyStatus)
        {
            sessionService = service;
            addressInput = address;
            portInput = port;
            hostLanButton = hostLan;
            joinLanButton = joinLan;
            joinCodeInput = code;
            createOnlineButton = createOnline;
            joinOnlineButton = joinOnline;
            readyButton = ready;
            startMatchButton = startMatch;
            leaveButton = leave;
            statusText = status;
            joinCodeText = displayedCode;
            playerCountText = playerCount;
            readyStatusText = readyStatus;
        }
#endif

        private void Awake()
        {
            if (sessionService == null)
            {
                sessionService = NetworkSessionService.Instance != null
                    ? NetworkSessionService.Instance
                    : FindFirstObjectByType<NetworkSessionService>();
            }

            AddListeners();
            if (addressInput != null && string.IsNullOrWhiteSpace(addressInput.text))
            {
                addressInput.text = "127.0.0.1";
            }

            if (portInput != null && string.IsNullOrWhiteSpace(portInput.text))
            {
                portInput.text = "7777";
            }
        }

        private void OnEnable()
        {
            if (sessionService != null)
            {
                sessionService.SessionChanged += Refresh;
                sessionService.ErrorRaised += OnError;
            }

            Refresh();
        }

        private void Start()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            if (Array.IndexOf(arguments, "--patintero-host") >= 0)
            {
                HostLan();
            }
            else if (Array.IndexOf(arguments, "--patintero-client") >= 0)
            {
                JoinLan();
            }
        }

        private void OnDisable()
        {
            if (sessionService != null)
            {
                sessionService.SessionChanged -= Refresh;
                sessionService.ErrorRaised -= OnError;
            }
        }

        private void OnDestroy()
        {
            RemoveListeners();
        }

        private void AddListeners()
        {
            hostLanButton?.onClick.AddListener(HostLan);
            joinLanButton?.onClick.AddListener(JoinLan);
            createOnlineButton?.onClick.AddListener(CreateOnline);
            joinOnlineButton?.onClick.AddListener(JoinOnline);
            readyButton?.onClick.AddListener(ToggleReady);
            startMatchButton?.onClick.AddListener(StartMatch);
            leaveButton?.onClick.AddListener(Leave);
        }

        private void RemoveListeners()
        {
            hostLanButton?.onClick.RemoveListener(HostLan);
            joinLanButton?.onClick.RemoveListener(JoinLan);
            createOnlineButton?.onClick.RemoveListener(CreateOnline);
            joinOnlineButton?.onClick.RemoveListener(JoinOnline);
            readyButton?.onClick.RemoveListener(ToggleReady);
            startMatchButton?.onClick.RemoveListener(StartMatch);
            leaveButton?.onClick.RemoveListener(Leave);
        }

        private async void HostLan()
        {
            if (TryReadLanEndpoint(out string address, out ushort port))
            {
                await sessionService.HostLanAsync(address, port);
            }
        }

        private async void JoinLan()
        {
            if (TryReadLanEndpoint(out string address, out ushort port))
            {
                await sessionService.JoinLanAsync(address, port);
            }
        }

        private async void CreateOnline()
        {
            if (sessionService != null)
            {
                await sessionService.CreateOnlineSessionAsync();
            }
        }

        private async void JoinOnline()
        {
            if (sessionService != null)
            {
                await sessionService.JoinOnlineSessionAsync(joinCodeInput == null ? string.Empty : joinCodeInput.text);
            }
        }

        private void ToggleReady()
        {
            SessionPlayerState localPlayer = sessionService?.GetLocalPlayer();
            if (localPlayer != null)
            {
                sessionService.RequestReady(!localPlayer.isReady);
            }
        }

        private void StartMatch()
        {
            sessionService?.RequestStartMatch();
        }

        private async void Leave()
        {
            if (sessionService != null)
            {
                await sessionService.LeaveAsync();
            }
        }

        private bool TryReadLanEndpoint(out string address, out ushort port)
        {
            port = 0;
            address = addressInput == null || string.IsNullOrWhiteSpace(addressInput.text)
                ? "127.0.0.1"
                : addressInput.text.Trim();
            if (sessionService == null || portInput == null ||
                !ushort.TryParse(portInput.text, out port) || port == 0)
            {
                OnError("Port must be between 1 and 65535.");
                return false;
            }

            return true;
        }

        private void Refresh()
        {
            if (sessionService == null)
            {
                SetText(statusText, "Multiplayer service is not configured.");
                SetConnectionControls(false, false);
                return;
            }

            bool offline = sessionService.State == MultiplayerSessionState.Offline;
            bool waiting = sessionService.State == MultiplayerSessionState.WaitingRoom;
            SetConnectionControls(offline, sessionService.IsConnected);

            SessionPlayerState localPlayer = sessionService.GetLocalPlayer();
            if (readyButton != null)
            {
                readyButton.interactable = waiting && localPlayer != null;
                SetButtonText(readyButton, localPlayer != null && localPlayer.isReady ? "Not Ready" : "Ready");
            }

            if (startMatchButton != null)
            {
                startMatchButton.interactable = sessionService.CanStartMatch;
            }

            SetText(playerCountText, $"Players: {sessionService.PlayerCount}/{NetworkSessionService.MaximumPlayers}");
            SetText(joinCodeText, string.IsNullOrWhiteSpace(sessionService.JoinCode)
                ? "Join code: --"
                : $"Join code: {sessionService.JoinCode}");
            SetText(readyStatusText, BuildReadyStatus(sessionService.Players));
            SetText(statusText, BuildStatus());
        }

        private void SetConnectionControls(bool offline, bool connected)
        {
            if (hostLanButton != null) hostLanButton.interactable = offline;
            if (joinLanButton != null) joinLanButton.interactable = offline;
            if (createOnlineButton != null) createOnlineButton.interactable = offline;
            if (joinOnlineButton != null) joinOnlineButton.interactable = offline;
            if (addressInput != null) addressInput.interactable = offline;
            if (portInput != null) portInput.interactable = offline;
            if (joinCodeInput != null) joinCodeInput.interactable = offline;
            if (leaveButton != null) leaveButton.interactable = connected;
        }

        private string BuildStatus()
        {
            if (!string.IsNullOrWhiteSpace(sessionService.LastError))
            {
                return sessionService.LastError;
            }

            return sessionService.State switch
            {
                MultiplayerSessionState.Offline => "Offline - host or join a game",
                MultiplayerSessionState.Connecting => "Connecting...",
                MultiplayerSessionState.WaitingRoom => sessionService.IsSessionHost
                    ? "Lobby open - all four players must ready up"
                    : "Connected - ready up and wait for the host",
                MultiplayerSessionState.InMatch => "Match in progress",
                MultiplayerSessionState.Disconnecting => "Leaving session...",
                _ => string.Empty
            };
        }

        private static string BuildReadyStatus(System.Collections.Generic.IReadOnlyList<SessionPlayerState> players)
        {
            if (players == null || players.Count == 0)
            {
                return "Ready: --";
            }

            int readyCount = 0;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].isReady)
                {
                    readyCount++;
                }
            }

            return $"Ready: {readyCount}/{players.Count}";
        }

        private void OnError(string message)
        {
            SetText(statusText, message);
        }

        private static void SetButtonText(Button button, string value)
        {
            Text label = button == null ? null : button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.text = value;
            }
        }

        private static void SetText(Text label, string value)
        {
            if (label != null)
            {
                label.text = value;
            }
        }
    }
}
