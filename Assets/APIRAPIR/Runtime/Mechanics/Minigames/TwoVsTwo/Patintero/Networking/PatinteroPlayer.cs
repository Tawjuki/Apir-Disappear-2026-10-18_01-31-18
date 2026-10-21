using System.Collections.Generic;
using ApirApir.Minigames;
using Unity.Netcode;
using UnityEngine;

namespace ApirApir.Patintero
{
    [RequireComponent(typeof(NetworkObject), typeof(PatinteroKeyboardInput), typeof(MinigameGroundMotor))]
    public sealed class PatinteroPlayer : NetworkBehaviour
    {
        private static readonly List<PatinteroPlayer> SpawnedPlayersInternal = new(4);

        [Header("Presentation")]
        [SerializeField] private Renderer bodyRenderer;
        [SerializeField] private Animator movementAnimator;
        [SerializeField] private string animationSpeedParameter = "Speed";

        [Header("Movement")]
        [SerializeField] private Camera movementCamera;
        [SerializeField] private MinigameGroundMotor groundMotor;
        [SerializeField] private float standingY = 1f;

        [Header("Offline Preview")]
        [SerializeField] private bool enableOfflinePreview = true;
        [SerializeField] private PatinteroSettings offlinePreviewSettings;

        private MinigamePlayerInput input;
        private Vector2 serverInput;
        private Vector2 lastSentInput;
        private MinigameActionMask serverHeldActions;
        private MinigameActionMask lastSentHeldActions;
        private float nextInputSendTime;
        private float nextInputKeepAliveTime;
        private float guardLineZ;
        private PatinteroGuardPath guardPath;
        private int activeGuardSegment = -1;
        private MaterialPropertyBlock materialProperties;
        private int animationSpeedHash;
        private bool hasAnimationSpeedParameter;
        private float lastAnimationAmount = -1f;
        private PatinteroSettings generatedPreviewSettings;

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

        public readonly NetworkVariable<byte> MovementAmount = new(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public static IReadOnlyList<PatinteroPlayer> SpawnedPlayers => SpawnedPlayersInternal;
        public bool IsCaught => Progress.Value == RunnerProgress.Caught;
        public bool CanMove => IsSpawned && PatinteroGameManager.Instance != null &&
                               PatinteroGameManager.Instance.RoundState.Value == PatinteroRoundState.Playing &&
                               Role.Value != PatinteroRole.Unassigned && !IsCaught &&
                               Progress.Value != RunnerProgress.Completed;
        private bool IsOfflinePreview => enableOfflinePreview && !IsSpawned &&
                                         (NetworkManager.Singleton == null ||
                                          !NetworkManager.Singleton.IsListening);

        private void Awake()
        {
            input = GetComponent<MinigamePlayerInput>();
            if (bodyRenderer == null)
            {
                bodyRenderer = GetComponentInChildren<Renderer>();
            }

            if (groundMotor == null)
            {
                groundMotor = GetComponent<MinigameGroundMotor>();
            }

            if (groundMotor == null)
            {
                groundMotor = gameObject.AddComponent<MinigameGroundMotor>();
            }

            if (movementAnimator == null)
            {
                movementAnimator = GetComponentInChildren<Animator>();
            }

            CacheAnimationParameter();
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
            MovementAmount.OnValueChanged += OnMovementAmountChanged;

            if (IsOwner)
            {
                ResolveMovementCamera();
            }

            ApplyPresentation();
            ApplyMovementAnimation(MovementAmount.Value);
        }

        public override void OnNetworkDespawn()
        {
            Role.OnValueChanged -= OnPresentationChanged;
            Progress.OnValueChanged -= OnPresentationChanged;
            PlayerNumber.OnValueChanged -= OnNumberChanged;
            MovementAmount.OnValueChanged -= OnMovementAmountChanged;
            SpawnedPlayersInternal.Remove(this);
        }

        public override void OnDestroy()
        {
            if (generatedPreviewSettings != null)
            {
                Destroy(generatedPreviewSettings);
                generatedPreviewSettings = null;
            }

            base.OnDestroy();
        }

        private void Update()
        {
            bool offlinePreview = IsOfflinePreview;
            if ((!IsOwner || !CanMove) && !offlinePreview)
            {
                return;
            }

            ResolveMovementCamera();

            MinigameInputFrame inputFrame = input.ReadInput();
            Vector2 movement = MinigameMovementMath.ToCameraRelative(
                inputFrame.Movement,
                movementCamera != null ? movementCamera.transform : null);

            if (!MinigameMovementMath.IsFinite(movement))
            {
                movement = Vector2.zero;
            }

            PatinteroSettings settings = offlinePreview
                ? GetOfflinePreviewSettings()
                : PatinteroGameManager.Instance.Settings;
            if (settings == null)
            {
                return;
            }

            if (offlinePreview)
            {
                serverInput = movement;
                serverHeldActions = inputFrame.HeldActions & MinigameActionMask.Primary;
                return;
            }

            int sendRate = settings.inputSendRate;
            MinigameActionMask heldActions = inputFrame.HeldActions & MinigameActionMask.Primary;
            MinigameActionMask pressedActions = inputFrame.PressedActions & MinigameActionMask.Primary;
            bool movementChanged = (movement - lastSentInput).sqrMagnitude > 0.0001f;
            bool actionStateChanged = heldActions != lastSentHeldActions;
            bool keepAliveDue = Time.unscaledTime >= nextInputKeepAliveTime;
            if (!movementChanged && !actionStateChanged && pressedActions == MinigameActionMask.None &&
                !keepAliveDue)
            {
                return;
            }

            bool stopped = movement.sqrMagnitude <= 0.0001f && lastSentInput.sqrMagnitude > 0.0001f;
            if (!stopped && !actionStateChanged && pressedActions == MinigameActionMask.None &&
                Time.unscaledTime < nextInputSendTime)
            {
                return;
            }

            lastSentInput = movement;
            lastSentHeldActions = heldActions;
            nextInputSendTime = Time.unscaledTime + 1f / Mathf.Max(10, sendRate);
            nextInputKeepAliveTime = Time.unscaledTime + settings.InputKeepAliveSeconds;
            SubmitInputRpc(movement, (byte)heldActions, (byte)pressedActions);
        }

        private void FixedUpdate()
        {
            if (IsOfflinePreview)
            {
                MoveOnServer(GetOfflinePreviewSettings(), Time.fixedDeltaTime);
                return;
            }

            if (!IsServer)
            {
                return;
            }

            if (!CanMove)
            {
                StopServerMovement();
                return;
            }

            MoveOnServer(PatinteroGameManager.Instance.Settings, Time.fixedDeltaTime);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitInputRpc(
            Vector2 movement,
            byte heldActions,
            byte pressedActions,
            RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || !CanMove)
            {
                return;
            }

            if (!MinigameMovementMath.IsFinite(movement))
            {
                serverInput = Vector2.zero;
                return;
            }

            serverInput = Vector2.ClampMagnitude(movement, 1f);
            MinigameActionMask allowedActions = MinigameActionMask.Primary;
            serverHeldActions = (MinigameActionMask)heldActions & allowedActions;

            if (Role.Value == PatinteroRole.Taya &&
                ((MinigameActionMask)pressedActions & MinigameActionMask.Primary) != 0)
            {
                PatinteroGameManager.Instance?.TryTagOnServer(this);
            }
        }

        public void InitializeOnServer(
            int playerNumber,
            PatinteroRole role,
            RunnerProgress progress,
            Vector3 position,
            float assignedGuardLineZ,
            PatinteroGuardPath assignedGuardPath = null)
        {
            if (!IsServer)
            {
                return;
            }

            PlayerNumber.Value = playerNumber;
            Role.Value = role;
            Progress.Value = progress;
            guardLineZ = assignedGuardLineZ;
            guardPath = role == PatinteroRole.Taya ? assignedGuardPath : null;
            activeGuardSegment = -1;
            serverInput = Vector2.zero;
            serverHeldActions = MinigameActionMask.None;
            lastSentHeldActions = MinigameActionMask.None;
            groundMotor.Stop();
            MovementAmount.Value = 0;
            groundMotor.Teleport(new Vector3(position.x, standingY, position.z));

            if (guardPath != null && guardPath.HasUsableSegments)
            {
                groundMotor.Teleport(guardPath.SnapToPath(transform.position, ref activeGuardSegment));
            }
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
                serverHeldActions = MinigameActionMask.None;
                groundMotor.Stop();
                MovementAmount.Value = 0;
            }
        }

        private void OnPresentationChanged<T>(T previous, T current) => ApplyPresentation();
        private void OnNumberChanged(int previous, int current) => gameObject.name = $"Player {current}";
        private void OnMovementAmountChanged(byte previous, byte current) => ApplyMovementAnimation(current);

        private void MoveOnServer(PatinteroSettings settings, float deltaTime)
        {
            if (settings == null)
            {
                StopServerMovement();
                return;
            }

            bool isRunnerRunning = Role.Value == PatinteroRole.Runner &&
                                   (serverHeldActions & MinigameActionMask.Primary) != 0;
            float speed = Role.Value == PatinteroRole.Taya
                ? settings.tayaSpeed
                : isRunnerRunning ? settings.RunnerRunSpeed : settings.RunnerWalkSpeed;
            Vector2 serverVelocity = groundMotor.UpdateVelocity(
                serverInput,
                speed,
                settings.MovementAcceleration,
                settings.MovementDeceleration,
                deltaTime);

            Vector3 previousPosition = transform.position;
            Vector3 targetPosition;
            bool usesGuardPath = Role.Value == PatinteroRole.Taya &&
                                 guardPath != null && guardPath.HasUsableSegments;

            if (usesGuardPath)
            {
                targetPosition = guardPath.MoveAndConstrain(
                    previousPosition,
                    serverVelocity,
                    serverInput,
                    deltaTime,
                    ref activeGuardSegment);
            }
            else
            {
                targetPosition = previousPosition +
                                 new Vector3(serverVelocity.x, 0f, serverVelocity.y) * deltaTime;
                targetPosition = PatinteroRules.ConstrainToCourt(
                    targetPosition,
                    Role.Value,
                    guardLineZ,
                    settings);
                targetPosition.y = previousPosition.y;
            }

            groundMotor.MoveTo(targetPosition, settings.RotationSpeed, deltaTime);

            Vector3 constrainedPosition = usesGuardPath
                ? guardPath.SnapToPath(transform.position, ref activeGuardSegment)
                : PatinteroRules.ConstrainToCourt(transform.position, Role.Value, guardLineZ, settings);
            constrainedPosition.y = previousPosition.y;
            groundMotor.CorrectPosition(constrainedPosition);

            Vector3 actualMovement = transform.position - previousPosition;
            actualMovement.y = 0f;
            float normalizedSpeed = speed > 0.0001f
                ? Mathf.Clamp01(actualMovement.magnitude / Mathf.Max(0.0001f, deltaTime * speed))
                : 0f;
            SetMovementAmountOnServer(normalizedSpeed);
        }

        private void StopServerMovement()
        {
            serverInput = Vector2.zero;
            serverHeldActions = MinigameActionMask.None;
            groundMotor.Stop();
            if (MovementAmount.Value != 0)
            {
                MovementAmount.Value = 0;
            }
        }

        private void SetMovementAmountOnServer(float normalizedSpeed)
        {
            byte amount = (byte)Mathf.RoundToInt(Mathf.Clamp01(normalizedSpeed) * byte.MaxValue);
            if (!IsSpawned)
            {
                ApplyMovementAnimation(amount);
                return;
            }

            if (MovementAmount.Value != amount)
            {
                MovementAmount.Value = amount;
            }
        }

        private void CacheAnimationParameter()
        {
            if (movementAnimator == null || string.IsNullOrWhiteSpace(animationSpeedParameter))
            {
                return;
            }

            movementAnimator.applyRootMotion = false;
            animationSpeedHash = Animator.StringToHash(animationSpeedParameter);
            AnimatorControllerParameter[] parameters = movementAnimator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].nameHash == animationSpeedHash &&
                    parameters[i].type == AnimatorControllerParameterType.Float)
                {
                    hasAnimationSpeedParameter = true;
                    break;
                }
            }
        }

        private void ApplyMovementAnimation(byte amount)
        {
            if (!hasAnimationSpeedParameter || movementAnimator == null)
            {
                return;
            }

            float normalizedAmount = amount / (float)byte.MaxValue;
            if (Mathf.Abs(normalizedAmount - lastAnimationAmount) <= 0.005f)
            {
                return;
            }

            lastAnimationAmount = normalizedAmount;
            movementAnimator.SetFloat(animationSpeedHash, normalizedAmount);
        }

        private void ResolveMovementCamera()
        {
            if (movementCamera == null)
            {
                movementCamera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
            }
        }

        private PatinteroSettings GetOfflinePreviewSettings()
        {
            if (offlinePreviewSettings != null)
            {
                return offlinePreviewSettings;
            }

            if (generatedPreviewSettings == null)
            {
                generatedPreviewSettings = ScriptableObject.CreateInstance<PatinteroSettings>();
                generatedPreviewSettings.hideFlags = HideFlags.HideAndDontSave;
            }

            return generatedPreviewSettings;
        }

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
