using System.Collections.Generic;
using System.Linq;
using ApirApir.Multiplayer;
using Unity.Netcode;
using UnityEngine;

namespace ApirApir.Patintero
{
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PatinteroGameManager : NetworkBehaviour
    {
        [SerializeField] private PatinteroSettings settings;
        [SerializeField] private NetworkSessionService sessionService;
        [SerializeField] private PatinteroGuardPath[] guardPaths = System.Array.Empty<PatinteroGuardPath>();

        private readonly Dictionary<ulong, PatinteroRole> authoritativeRoles = new(4);
        private readonly Dictionary<ulong, double> nextTagTimes = new(2);
        private bool pendingStartCheck;
        private string authoritativeAssignmentId = string.Empty;

        public static PatinteroGameManager Instance { get; private set; }
        public PatinteroSettings Settings => settings;

#if UNITY_EDITOR
        public void SetSettingsForEditor(PatinteroSettings value) => settings = value;
        public void SetSessionServiceForEditor(NetworkSessionService value) => sessionService = value;
#endif

        public readonly NetworkVariable<PatinteroRoundState> RoundState = new(
            PatinteroRoundState.WaitingForPlayers,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<PatinteroResult> Result = new(
            PatinteroResult.None,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<double> RoundEndServerTime = new(
            0d,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<int> RoundNumber = new(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public float RemainingSeconds
        {
            get
            {
                if (RoundState.Value != PatinteroRoundState.Playing || NetworkManager == null)
                {
                    return 0f;
                }

                return Mathf.Max(0f, (float)(RoundEndServerTime.Value - NetworkManager.ServerTime.Time));
            }
        }

        private void Awake()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            ResolveSessionService();
            SubscribeNetworkManager();
        }

        public override void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (sessionService != null)
            {
                sessionService.MatchStarted -= OnSessionMatchStarted;
            }

            UnsubscribeNetworkManager();
            base.OnDestroy();
        }

        private void Update()
        {
            if (!IsServer)
            {
                return;
            }

            bool sessionAllowsStart = sessionService == null ||
                                      sessionService.State == MultiplayerSessionState.InMatch;
            if (pendingStartCheck && sessionAllowsStart &&
                RoundState.Value == PatinteroRoundState.WaitingForPlayers)
            {
                TryStartInitialRound();
            }

            if (RoundState.Value == PatinteroRoundState.Playing && RemainingSeconds <= 0f)
            {
                FinishRound(PatinteroResult.TayaWins);
            }
        }

        private void FixedUpdate()
        {
            if (!IsServer || RoundState.Value != PatinteroRoundState.Playing)
            {
                return;
            }

            ResolveRunnerProgress();
        }

        private void ResolveSessionService()
        {
            if (sessionService == null)
            {
                sessionService = NetworkSessionService.Instance != null
                    ? NetworkSessionService.Instance
                    : FindFirstObjectByType<NetworkSessionService>();
            }

            if (sessionService != null)
            {
                sessionService.MatchStarted -= OnSessionMatchStarted;
                sessionService.MatchStarted += OnSessionMatchStarted;
            }
        }

        private void SubscribeNetworkManager()
        {
            NetworkManager manager = NetworkManager.Singleton != null
                ? NetworkManager.Singleton
                : FindFirstObjectByType<NetworkManager>();

            if (manager == null)
            {
                return;
            }

            manager.OnClientConnectedCallback -= OnClientConnected;
            manager.OnClientConnectedCallback += OnClientConnected;
            manager.OnClientDisconnectCallback -= OnClientDisconnected;
            manager.OnClientDisconnectCallback += OnClientDisconnected;
        }

        private void UnsubscribeNetworkManager()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null)
            {
                return;
            }

            manager.OnClientConnectedCallback -= OnClientConnected;
            manager.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        private void OnClientConnected(ulong clientId)
        {
            if (IsServer && sessionService == null)
            {
                pendingStartCheck = true;
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (!IsServer)
            {
                return;
            }

            if (RoundState.Value == PatinteroRoundState.Playing)
            {
                bool tayaStillPresent = PatinteroPlayer.SpawnedPlayers.Any(
                    player => player.IsSpawned &&
                              player.OwnerClientId != clientId &&
                              player.Role.Value == PatinteroRole.Taya);

                if (!tayaStillPresent)
                {
                    FinishRound(PatinteroResult.RunnersWin);
                    return;
                }

                int activeRunners = CountActiveRunners(clientId);
                if (activeRunners == 0)
                {
                    FinishRound(PatinteroResult.TayaWins);
                }
            }

            pendingStartCheck = false;
        }

        private void OnSessionMatchStarted()
        {
            if (IsServer)
            {
                pendingStartCheck = true;
            }
        }

        private void TryStartInitialRound()
        {
            List<PatinteroPlayer> players = GetServerPlayers();
            if (players.Count < settings.requiredPlayers)
            {
                return;
            }

            pendingStartCheck = false;
            StartRound(players);
        }

        private void StartRound(List<PatinteroPlayer> players)
        {
            if (!IsServer || players.Count != settings.requiredPlayers)
            {
                return;
            }

            players.Sort((left, right) => left.OwnerClientId.CompareTo(right.OwnerClientId));
            bool hasAuthoritativeRoles = HasCompleteAuthoritativeAssignment(players);
            int runnerIndex = 0;
            int tayaAssigned = 0;
            int tayaCount = hasAuthoritativeRoles
                ? authoritativeRoles.Values.Count(role => role == PatinteroRole.Taya)
                : settings.tayaCount;
            int runnerCount = players.Count - tayaCount;
            nextTagTimes.Clear();

            for (int i = 0; i < players.Count; i++)
            {
                bool isTaya = hasAuthoritativeRoles
                    ? authoritativeRoles[players[i].OwnerClientId] == PatinteroRole.Taya
                    : tayaAssigned < settings.tayaCount;
                if (isTaya)
                {
                    float lineZ = settings.GetGuardLineZ(tayaAssigned);
                    players[i].InitializeOnServer(
                        i + 1,
                        PatinteroRole.Taya,
                        RunnerProgress.Outbound,
                        new Vector3(GetTayaSpawnX(tayaAssigned, tayaCount), 1f, lineZ),
                        lineZ,
                        GetGuardPath(tayaAssigned));
                    tayaAssigned++;
                }
                else
                {
                    players[i].InitializeOnServer(
                        i + 1,
                        PatinteroRole.Runner,
                        RunnerProgress.Outbound,
                        GetRunnerSpawn(runnerIndex, runnerCount),
                        0f);
                    runnerIndex++;
                }
            }

            Result.Value = PatinteroResult.None;
            RoundNumber.Value++;
            RoundEndServerTime.Value = NetworkManager.ServerTime.Time + settings.roundDurationSeconds;
            RoundState.Value = PatinteroRoundState.Playing;
            authoritativeRoles.Clear();
            Debug.Log($"Patintero round {RoundNumber.Value} started with {players.Count} players: {tayaCount} taya and {runnerCount} runners.");
        }

        public bool TryApplyAuthoritativeAssignmentOnServer(
            string assignmentId,
            IReadOnlyList<PatinteroNetworkRoleAssignment> assignments)
        {
            string normalizedId = assignmentId == null ? string.Empty : assignmentId.Trim();
            if (!IsServer || RoundState.Value != PatinteroRoundState.WaitingForPlayers ||
                normalizedId.Length == 0 || assignments == null || assignments.Count != 4 ||
                string.Equals(authoritativeAssignmentId, normalizedId, System.StringComparison.Ordinal))
            {
                return false;
            }

            var candidate = new Dictionary<ulong, PatinteroRole>(4);
            int runners = 0;
            int taya = 0;
            for (int i = 0; i < assignments.Count; i++)
            {
                PatinteroNetworkRoleAssignment assignment = assignments[i];
                if ((assignment.Role != PatinteroRole.Runner && assignment.Role != PatinteroRole.Taya) ||
                    !candidate.TryAdd(assignment.ClientId, assignment.Role))
                {
                    return false;
                }

                if (assignment.Role == PatinteroRole.Runner)
                {
                    runners++;
                }
                else
                {
                    taya++;
                }
            }

            if (runners != 2 || taya != 2)
            {
                return false;
            }

            authoritativeRoles.Clear();
            foreach (KeyValuePair<ulong, PatinteroRole> pair in candidate)
            {
                authoritativeRoles.Add(pair.Key, pair.Value);
            }

            authoritativeAssignmentId = normalizedId;
            pendingStartCheck = true;
            return true;
        }

        private bool HasCompleteAuthoritativeAssignment(IReadOnlyList<PatinteroPlayer> players)
        {
            if (authoritativeRoles.Count != players.Count)
            {
                return false;
            }

            for (int i = 0; i < players.Count; i++)
            {
                if (!authoritativeRoles.ContainsKey(players[i].OwnerClientId))
                {
                    return false;
                }
            }

            return true;
        }

        private void ResolveRunnerProgress()
        {
            foreach (PatinteroPlayer runner in PatinteroPlayer.SpawnedPlayers)
            {
                if (!runner.IsSpawned || runner.Role.Value != PatinteroRole.Runner || runner.IsCaught ||
                    runner.Progress.Value == RunnerProgress.Completed)
                {
                    continue;
                }

                RunnerProgress progress = PatinteroRules.AdvanceRunner(
                    runner.Progress.Value,
                    runner.transform.position.z,
                    settings);

                if (progress != runner.Progress.Value)
                {
                    runner.SetProgressOnServer(progress);
                }

            }

            EvaluateRoundResult();
        }

        public bool TryTagOnServer(PatinteroPlayer guard)
        {
            if (!IsServer || guard == null || !guard.IsSpawned ||
                RoundState.Value != PatinteroRoundState.Playing ||
                guard.Role.Value != PatinteroRole.Taya || guard.IsCaught)
            {
                return false;
            }

            double now = NetworkManager.ServerTime.Time;
            if (nextTagTimes.TryGetValue(guard.OwnerClientId, out double availableAt) &&
                now + 0.0001d < availableAt)
            {
                return false;
            }

            nextTagTimes[guard.OwnerClientId] = now + settings.TagCooldown;
            PatinteroPlayer closest = null;
            float closestDistance = float.PositiveInfinity;
            foreach (PatinteroPlayer runner in PatinteroPlayer.SpawnedPlayers)
            {
                if (runner == null || !runner.IsSpawned || runner.Role.Value != PatinteroRole.Runner ||
                    runner.Progress.Value is RunnerProgress.Caught or RunnerProgress.Completed ||
                    !PatinteroRules.IsTag(guard.transform.position, runner.transform.position, settings.tagRadius))
                {
                    continue;
                }

                Vector3 delta = runner.transform.position - guard.transform.position;
                delta.y = 0f;
                float distance = delta.sqrMagnitude;
                if (distance < closestDistance)
                {
                    closest = runner;
                    closestDistance = distance;
                }
            }

            if (closest == null)
            {
                return false;
            }

            closest.SetProgressOnServer(RunnerProgress.Caught);
            EvaluateRoundResult();
            return true;
        }

        private void EvaluateRoundResult()
        {
            int completedRunners = PatinteroPlayer.SpawnedPlayers.Count(player =>
                player != null && player.IsSpawned && player.Role.Value == PatinteroRole.Runner &&
                player.Progress.Value == RunnerProgress.Completed);
            PatinteroResult evaluated = PatinteroRules.EvaluateResult(
                CountActiveRunners(),
                completedRunners,
                settings.requiredRunnerCompletions,
                false);
            if (evaluated != PatinteroResult.None)
            {
                FinishRound(evaluated);
            }
        }

        private int CountActiveRunners(ulong excludedClientId = ulong.MaxValue)
        {
            return PatinteroPlayer.SpawnedPlayers.Count(player =>
                player.IsSpawned &&
                player.OwnerClientId != excludedClientId &&
                player.Role.Value == PatinteroRole.Runner &&
                player.Progress.Value is RunnerProgress.Outbound or RunnerProgress.Returning);
        }

        private void FinishRound(PatinteroResult result)
        {
            if (!IsServer || RoundState.Value != PatinteroRoundState.Playing)
            {
                return;
            }

            Result.Value = result;
            RoundEndServerTime.Value = NetworkManager.ServerTime.Time;
            RoundState.Value = PatinteroRoundState.Finished;
            Debug.Log($"Patintero round {RoundNumber.Value} finished: {result}.");
        }

        public void RequestRestart()
        {
            if (!IsClient)
            {
                return;
            }

            RequestRestartRpc();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestRestartRpc(RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId)
            {
                return;
            }

            List<PatinteroPlayer> players = GetServerPlayers();
            if (players.Count == settings.requiredPlayers)
            {
                StartRound(players);
            }
            else
            {
                Result.Value = PatinteroResult.None;
                RoundState.Value = PatinteroRoundState.WaitingForPlayers;
                pendingStartCheck = true;
            }
        }

        private List<PatinteroPlayer> GetServerPlayers()
        {
            return PatinteroPlayer.SpawnedPlayers
                .Where(player => player != null && player.IsSpawned)
                .Take(settings.requiredPlayers)
                .ToList();
        }

        private Vector3 GetRunnerSpawn(int index, int runnerCount)
        {
            float spacing = settings.courtWidth / (runnerCount + 1f);
            float x = -settings.HalfWidth + spacing * (index + 1f);
            float z = -settings.HalfLength + settings.endZoneDepth * 0.5f;
            return new Vector3(x, 1f, z);
        }

        private float GetTayaSpawnX(int index, int tayaCount)
        {
            if (tayaCount <= 1)
            {
                return 0f;
            }

            return Mathf.Lerp(-settings.HalfWidth * 0.35f, settings.HalfWidth * 0.35f,
                index / (tayaCount - 1f));
        }

        private PatinteroGuardPath GetGuardPath(int index)
        {
            if (guardPaths == null || index < 0 || index >= guardPaths.Length)
            {
                return null;
            }

            PatinteroGuardPath path = guardPaths[index];
            return path != null && path.HasUsableSegments ? path : null;
        }
    }
}
