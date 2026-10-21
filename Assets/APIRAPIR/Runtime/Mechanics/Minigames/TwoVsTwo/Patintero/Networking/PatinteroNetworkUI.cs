using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using ApirApir.Minigames;

namespace ApirApir.Patintero
{
    /// <summary>Patintero-only HUD. Connection and lobby controls live in NetworkSessionUI.</summary>
    public sealed class PatinteroNetworkUI : MonoBehaviour
    {
        [SerializeField] private Button restartButton;
        [SerializeField] private Text timerText;
        [SerializeField] private Text roleText;
        [SerializeField] private Text roundText;
        [SerializeField] private Text resultText;
        [SerializeField] private MinigameContextActionView contextAction;

        private NetworkManager manager;

#if UNITY_EDITOR
        public void ConfigureForEditor(
            Button restart,
            Text timer,
            Text role,
            Text round,
            Text result,
            MinigameContextActionView action = null)
        {
            restartButton = restart;
            timerText = timer;
            roleText = role;
            roundText = round;
            resultText = result;
            contextAction = action;
        }
#endif

        private void Awake()
        {
            manager = NetworkManager.Singleton != null
                ? NetworkManager.Singleton
                : FindFirstObjectByType<NetworkManager>();
            restartButton?.onClick.AddListener(RestartRound);
        }

        private void OnDestroy()
        {
            restartButton?.onClick.RemoveListener(RestartRound);
        }

        private void Update()
        {
            PatinteroGameManager game = PatinteroGameManager.Instance;
            if (restartButton != null)
            {
                restartButton.interactable = manager != null && manager.IsHost && game != null &&
                                             game.RoundState.Value == PatinteroRoundState.Finished;
            }

            UpdateGameText(game);
        }

        private static void RestartRound()
        {
            PatinteroGameManager.Instance?.RequestRestart();
        }

        private void UpdateGameText(PatinteroGameManager game)
        {
            if (game == null || manager == null || !manager.IsListening)
            {
                SetText(timerText, "Time: --");
                SetText(roleText, "Role: --");
                SetText(roundText, "Waiting for network");
                SetText(resultText, string.Empty);
                contextAction?.SetContext(string.Empty, false);
                return;
            }

            SetText(timerText, game.RoundState.Value == PatinteroRoundState.Playing
                ? $"Time: {Mathf.CeilToInt(game.RemainingSeconds):00}"
                : "Time: --");

            PatinteroPlayer local = null;
            foreach (PatinteroPlayer player in PatinteroPlayer.SpawnedPlayers)
            {
                if (player != null && player.IsOwner)
                {
                    local = player;
                    break;
                }
            }

            SetText(roleText, local == null ? "Role: --" : BuildRoleText(local));
            UpdateContextAction(game, local);
            SetText(roundText, game.RoundState.Value switch
            {
                PatinteroRoundState.WaitingForPlayers => "Waiting for four ready players...",
                PatinteroRoundState.Playing => $"Round {game.RoundNumber.Value} - cross, then return!",
                PatinteroRoundState.Finished => $"Round {game.RoundNumber.Value} complete",
                _ => string.Empty
            });

            SetText(resultText, game.Result.Value switch
            {
                PatinteroResult.RunnersWin => "RUNNERS WIN!",
                PatinteroResult.TayaWins => "TAYA WINS!",
                _ => string.Empty
            });
        }

        private void UpdateContextAction(PatinteroGameManager game, PatinteroPlayer local)
        {
            if (contextAction == null)
            {
                return;
            }

            if (local == null || local.Role.Value == PatinteroRole.Unassigned)
            {
                contextAction.SetContext(string.Empty, false);
                return;
            }

            string label = local.Role.Value == PatinteroRole.Taya ? "TAG" : "RUN";
            bool available = game != null && game.RoundState.Value == PatinteroRoundState.Playing &&
                             local.CanMove;
            contextAction.SetContext(label, available);
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

        private static void SetText(Text label, string value)
        {
            if (label != null)
            {
                label.text = value;
            }
        }
    }
}
