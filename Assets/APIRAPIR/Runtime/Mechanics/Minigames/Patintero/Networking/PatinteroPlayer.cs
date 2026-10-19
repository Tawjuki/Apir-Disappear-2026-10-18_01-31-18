using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace ApirApir.Patintero
{
    [RequireComponent(typeof(NetworkObject), typeof(PatinteroKeyboardInput))]
    public sealed class PatinteroPlayer : NetworkBehaviour
    {
        private static readonly List<PatinteroPlayer> SpawnedPlayersInternal = new(4);

        [SerializeField] private Renderer bodyRenderer;
        [SerializeField] private float standingY = 1f;

        private PatinteroKeyboardInput input;
        private Vector2 serverInput;
        private Vector2 lastSentInput;
        private float nextInputSendTime;
        private float guardLineZ;
        private MaterialPropertyBlock materialProperties;

        public readonly NetworkVariable<PatinteroRole> Role = new(
            PatinteroRole.Unassigned,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<RunnerProgress> Progress = new(
            RunnerProgress.Outbound,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<int> PlayerNumber = new(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public static IReadOnlyList<PatinteroPlayer> SpawnedPlayers => SpawnedPlayersInternal;
        public bool IsCaught => Progress.Value == RunnerProgress.Caught;
        public bool CanMove => IsSpawned && PatinteroGameManager.Instance != null &&
                               PatinteroGameManager.Instance.RoundState.Value == PatinteroRoundState.Playing &&
                               Role.Value != PatinteroRole.Unassigned && !IsCaught &&
                               Progress.Value != RunnerProgress.Completed;

        private void Awake()
        {
            input = GetComponent<PatinteroKeyboardInput>();
            if (bodyRenderer == null)
            {
                bodyRenderer = GetComponentInChildren<Renderer>();
            }

            materialProperties = new MaterialPropertyBlock();
        }

        public override void OnNetworkSpawn()
        {
            if (!SpawnedPlayersInternal.Contains(this))
            {
                SpawnedPlayersInternal.Add(this);
            }

            Role.OnValueChanged += OnPresentationChanged;
            Progress.OnValueChanged += OnPresentationChanged;
            PlayerNumber.OnValueChanged += OnNumberChanged;
            ApplyPresentation();
        }

        public override void OnNetworkDespawn()
        {
            Role.OnValueChanged -= OnPresentationChanged;
            Progress.OnValueChanged -= OnPresentationChanged;
            PlayerNumber.OnValueChanged -= OnNumberChanged;
            SpawnedPlayersInternal.Remove(this);
        }

        private void Update()
        {
            if (!IsOwner || !CanMove)
            {
                return;
            }

            Vector2 movement = input.ReadMovement();
            int sendRate = PatinteroGameManager.Instance.Settings.inputSendRate;
            bool changed = (movement - lastSentInput).sqrMagnitude > 0.0001f;
            if (!changed && Time.unscaledTime < nextInputSendTime)
            {
                return;
            }

            lastSentInput = movement;
            nextInputSendTime = Time.unscaledTime + 1f / Mathf.Max(10, sendRate);
            SubmitInputRpc(movement);
        }

        private void FixedUpdate()
        {
            if (!IsServer || !CanMove)
            {
                return;
            }

            transform.position = PatinteroRules.MoveAndConstrain(
                transform.position,
                serverInput,
                Time.fixedDeltaTime,
                Role.Value,
                guardLineZ,
                PatinteroGameManager.Instance.Settings);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitInputRpc(Vector2 movement, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || !CanMove)
            {
                return;
            }

            serverInput = Vector2.ClampMagnitude(movement, 1f);
        }

        public void InitializeOnServer(
            int playerNumber,
            PatinteroRole role,
            RunnerProgress progress,
            Vector3 position,
            float assignedGuardLineZ)
        {
            if (!IsServer)
            {
                return;
            }

            PlayerNumber.Value = playerNumber;
            Role.Value = role;
            Progress.Value = progress;
            guardLineZ = assignedGuardLineZ;
            serverInput = Vector2.zero;
            transform.position = new Vector3(position.x, standingY, position.z);
        }

        public void SetProgressOnServer(RunnerProgress progress)
        {
            if (!IsServer || Role.Value != PatinteroRole.Runner)
            {
                return;
            }

            Progress.Value = progress;
            if (progress is RunnerProgress.Caught or RunnerProgress.Completed)
            {
                serverInput = Vector2.zero;
            }
        }

        private void OnPresentationChanged<T>(T previous, T current) => ApplyPresentation();
        private void OnNumberChanged(int previous, int current) => gameObject.name = $"Player {current}";

        private void ApplyPresentation()
        {
            if (bodyRenderer == null)
            {
                return;
            }

            Color color = Role.Value == PatinteroRole.Taya
                ? new Color(0.92f, 0.25f, 0.2f)
                : new Color(0.1f, 0.55f, 0.95f);

            if (IsCaught)
            {
                color = new Color(0.35f, 0.35f, 0.35f);
            }
            else if (Progress.Value == RunnerProgress.Returning)
            {
                color = new Color(0.15f, 0.85f, 0.45f);
            }

            bodyRenderer.GetPropertyBlock(materialProperties);
            materialProperties.SetColor("_BaseColor", color);
            materialProperties.SetColor("_Color", color);
            bodyRenderer.SetPropertyBlock(materialProperties);
        }
    }
}
