using System;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;

namespace ApirApir.Patintero
{
    public sealed class PatinteroNetworkUI : MonoBehaviour
    {
        [Header("Connection")]
        [SerializeField] private InputField addressInput;
        [SerializeField] private InputField portInput;
        [SerializeField] private Button hostButton;
        [SerializeField] private Button joinButton;
        [SerializeField] private Button disconnectButton;
        [SerializeField] private Button restartButton;

        [Header("Status")]
        [SerializeField] private Text connectionText;
        [SerializeField] private Text timerText;
        [SerializeField] private Text roleText;
        [SerializeField] private Text roundText;
        [SerializeField] private Text resultText;

        private NetworkManager manager;

        private void Awake()
        {
            manager = NetworkManager.Singleton != null
                ? NetworkManager.Singleton
                : FindFirstObjectByType<NetworkManager>();

            hostButton.onClick.AddListener(StartHost);
            joinButton.onClick.AddListener(StartClient);
            disconnectButton.onClick.AddListener(Disconnect);
            restartButton.onClick.AddListener(RestartRound);

            if (string.IsNullOrWhiteSpace(addressInput.text)) addressInput.text = "127.0.0.1";
            if (string.IsNullOrWhiteSpace(portInput.text)) portInput.text = "7777";
        }

        private void Start()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            if (Array.IndexOf(arguments, "--patintero-host") >= 0)
            {
                StartHost();
            }
            else if (Array.IndexOf(arguments, "--patintero-client") >= 0)
            {
                StartClient();
            }
        }

        private void OnDestroy()
        {
            hostButton.onClick.RemoveListener(StartHost);
            joinButton.onClick.RemoveListener(StartClient);
            disconnectButton.onClick.RemoveListener(Disconnect);
            restartButton.onClick.RemoveListener(RestartRound);
        }

        private void Update()
        {
            if (manager == null)
            {
                return;
            }

            bool listening = manager.IsListening;
            hostButton.interactable = !listening;
            joinButton.interactable = !listening;
            disconnectButton.interactable = listening;
            addressInput.interactable = !listening;
            portInput.interactable = !listening;

            PatinteroGameManager game = PatinteroGameManager.Instance;
            restartButton.interactable = manager.IsHost && game != null &&
                                         game.RoundState.Value == PatinteroRoundState.Finished;

            UpdateConnectionText();
            UpdateGameText(game);
        }

        private void StartHost()
        {
            if (!PrepareTransport(true))
            {
                return;
            }

            SetUniqueConnectionToken();
            if (!manager.StartHost())
            {
                connectionText.text = "Host failed to start.";
            }
            else
            {
                Debug.Log("Patintero host started on the configured Unity Transport endpoint.");
            }
        }

        private void StartClient()
        {
            if (!PrepareTransport(false))
            {
                return;
            }

            SetUniqueConnectionToken();
            if (!manager.StartClient())
            {
                connectionText.text = "Client failed to start.";
            }
            else
            {
                Debug.Log("Patintero client started and is connecting to the configured Unity Transport endpoint.");
            }
        }

        private void Disconnect()
        {
            if (manager != null && manager.IsListening)
            {
                manager.Shutdown();
            }
        }

        private void RestartRound()
        {
            PatinteroGameManager.Instance?.RequestRestart();
        }

        private bool PrepareTransport(bool isHost)
        {
            if (!ushort.TryParse(portInput.text, out ushort port) || port == 0)
            {
                connectionText.text = "Port must be between 1 and 65535.";
                return false;
            }

            string address = string.IsNullOrWhiteSpace(addressInput.text) ? "127.0.0.1" : addressInput.text.Trim();
            UnityTransport transport = manager.GetComponent<UnityTransport>();
            if (transport == null)
            {
                connectionText.text = "Unity Transport is missing.";
                return false;
            }

            if (isHost)
            {
                transport.SetConnectionData(address, port, "0.0.0.0");
            }
            else
            {
                transport.SetConnectionData(address, port);
            }

            return true;
        }

        private void SetUniqueConnectionToken()
        {
            manager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("N"));
        }

        private void UpdateConnectionText()
        {
            if (!manager.IsListening)
            {
                connectionText.text = "Offline - host or join a LAN game";
                return;
            }

            string mode = manager.IsHost ? "HOST" : "CLIENT";
            int count = manager.IsServer ? manager.ConnectedClientsIds.Count : PatinteroPlayer.SpawnedPlayers.Count;
            connectionText.text = $"{mode} | Players: {count}/4";
        }

        private void UpdateGameText(PatinteroGameManager game)
        {
            if (game == null || !manager.IsListening)
            {
                timerText.text = "Time: --";
                roleText.text = "Role: --";
                roundText.text = "Waiting for network";
                resultText.text = string.Empty;
                return;
            }

            timerText.text = game.RoundState.Value == PatinteroRoundState.Playing
                ? $"Time: {Mathf.CeilToInt(game.RemainingSeconds):00}"
                : "Time: --";

            PatinteroPlayer local = null;
            foreach (PatinteroPlayer player in PatinteroPlayer.SpawnedPlayers)
            {
                if (player != null && player.IsOwner)
                {
                    local = player;
                    break;
                }
            }

            roleText.text = local == null ? "Role: --" : BuildRoleText(local);
            roundText.text = game.RoundState.Value switch
            {
                PatinteroRoundState.WaitingForPlayers => "Waiting for four human players...",
                PatinteroRoundState.Playing => $"Round {game.RoundNumber.Value} - cross, then return!",
                PatinteroRoundState.Finished => $"Round {game.RoundNumber.Value} complete",
                _ => string.Empty
            };

            resultText.text = game.Result.Value switch
            {
                PatinteroResult.RunnersWin => "RUNNERS WIN!",
                PatinteroResult.TayaWins => "TAYA WINS!",
                _ => string.Empty
            };
        }

        private static string BuildRoleText(PatinteroPlayer player)
        {
            if (player.Role.Value == PatinteroRole.Unassigned)
            {
                return "Role: waiting for assignment";
            }

            if (player.Role.Value == PatinteroRole.Taya)
            {
                return "Role: TAYA - guard the red line";
            }

            string objective = player.Progress.Value switch
            {
                RunnerProgress.Outbound => "reach the far yellow zone",
                RunnerProgress.Returning => "return to the blue start zone",
                RunnerProgress.Caught => "caught - spectating this round",
                RunnerProgress.Completed => "round trip complete!",
                _ => string.Empty
            };
            return $"Role: RUNNER - {objective}";
        }
    }
}
