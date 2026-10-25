using ApirApir.Minigames;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public sealed class LobbyPlayerMovement : MonoBehaviour
{
    private const float InputEpsilon = 0.001f;
    private const float WalkAnimationSpeed = 0.5f;
    private const float RunAnimationSpeed = 1f;

    [Header("Movement")]
    [SerializeField] private float walkSpeed = 4f;
    [SerializeField] private float runSpeed = 7f;
    [SerializeField] private float rotationSpeed = 540f;
    [SerializeField] private float gravity = -20f;

    [Header("Mobile Controls")]
    [SerializeField] private MinigameVirtualJoystick joystick;
    [SerializeField] private UniversalPlayerInput controls;
    [SerializeField, Range(0.1f, 0.99f)]
    private float runThreshold = 0.75f;

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private float animationSmoothTime = 0.1f;

    private CharacterController controller;
    private float verticalVelocity;
    private bool movementEnabled = true;

    private static readonly int SpeedHash =
        Animator.StringToHash("Speed");

    public UniversalPlayerInput Controls => controls;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (controls == null)
        {
            controls = GetComponent<UniversalPlayerInput>();
        }

        if (controls == null)
        {
            controls = gameObject.AddComponent<UniversalPlayerInput>();
        }

        controls.SetVirtualJoystick(joystick);

        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        if (animator != null)
            animator.applyRootMotion = false;
    }

    private void Update()
    {
        if (!movementEnabled)
        {
            return;
        }

        MinigameInputFrame inputFrame = controls.ReadInput();
        if (!movementEnabled)
        {
            return;
        }

        Vector2 input = inputFrame.Movement;
        bool keyboardIsActive = controls.LastMovementSource == PlayerInputSource.Keyboard;
        bool keyboardRunning = keyboardIsActive && inputFrame.IsHeld(MinigameActionSlot.Modifier);
        float inputMagnitude = input.magnitude;
        bool isMoving = inputMagnitude > InputEpsilon;
        float movementSpeed = GetMovementSpeed(
            inputMagnitude,
            keyboardIsActive,
            keyboardRunning
        );
        float animationSpeed = GetAnimationSpeed(
            inputMagnitude,
            keyboardIsActive,
            keyboardRunning
        );

        Vector3 direction = new Vector3(
            input.x,
            0f,
            input.y
        );

        if (isMoving)
        {
            direction /= inputMagnitude;

            Quaternion targetRotation =
                Quaternion.LookRotation(direction);

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime
            );
        }

        if (controller.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }
        else
        {
            verticalVelocity += gravity * Time.deltaTime;
        }

        Vector3 velocity = direction * movementSpeed;
        velocity.y = verticalVelocity;

        controller.Move(velocity * Time.deltaTime);

        UpdateAnimation(animationSpeed);
    }

    public void SetMovementEnabled(bool value)
    {
        movementEnabled = value;
        controls?.SetInputEnabled(value);
        verticalVelocity = 0f;
        if (!value)
        {
            UpdateAnimation(0f);
        }
    }

    private float GetMovementSpeed(
        float inputMagnitude,
        bool keyboardIsActive,
        bool keyboardRunning)
    {
        if (inputMagnitude <= InputEpsilon)
            return 0f;

        if (keyboardIsActive)
            return keyboardRunning ? runSpeed : walkSpeed;

        if (inputMagnitude < runThreshold)
            return walkSpeed * (inputMagnitude / runThreshold);

        float runBlend = Mathf.InverseLerp(runThreshold, 1f, inputMagnitude);
        return Mathf.Lerp(walkSpeed, runSpeed, runBlend);
    }

    private float GetAnimationSpeed(
        float inputMagnitude,
        bool keyboardIsActive,
        bool keyboardRunning)
    {
        if (inputMagnitude <= InputEpsilon)
            return 0f;

        if (keyboardIsActive)
            return keyboardRunning ? RunAnimationSpeed : WalkAnimationSpeed;

        if (inputMagnitude < runThreshold)
        {
            float walkBlend = inputMagnitude / runThreshold;
            return Mathf.Lerp(0f, WalkAnimationSpeed, walkBlend);
        }

        float runBlend = Mathf.InverseLerp(runThreshold, 1f, inputMagnitude);
        return Mathf.Lerp(WalkAnimationSpeed, RunAnimationSpeed, runBlend);
    }

    private void UpdateAnimation(float targetSpeed)
    {
        if (animator == null)
            return;

        animator.SetFloat(
            SpeedHash,
            targetSpeed,
            animationSmoothTime,
            Time.deltaTime
        );
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        walkSpeed = Mathf.Max(0f, walkSpeed);
        runSpeed = Mathf.Max(walkSpeed, runSpeed);
        rotationSpeed = Mathf.Max(0f, rotationSpeed);
        gravity = Mathf.Min(0f, gravity);
        runThreshold = Mathf.Clamp(runThreshold, 0.1f, 0.99f);
        animationSmoothTime = Mathf.Max(0f, animationSmoothTime);
    }
#endif
}
