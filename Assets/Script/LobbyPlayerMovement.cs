using ApirApir.Minigames;
using UnityEngine;
using UnityEngine.InputSystem;

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
    [SerializeField, Range(0.1f, 0.99f)]
    private float runThreshold = 0.75f;

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private float animationSmoothTime = 0.1f;

    private CharacterController controller;
    private float verticalVelocity;

    private static readonly int SpeedHash =
        Animator.StringToHash("Speed");

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        if (animator != null)
            animator.applyRootMotion = false;
    }

    private void Update()
    {
        Vector2 input = ReadMovement(
            out bool keyboardIsActive,
            out bool keyboardRunning
        );
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

    private Vector2 ReadMovement(
        out bool keyboardIsActive,
        out bool keyboardRunning)
    {
        Vector2 keyboardInput = Vector2.zero;
        keyboardRunning = false;

        Keyboard keyboard = Keyboard.current;

        if (keyboard != null)
        {
            if (keyboard.wKey.isPressed)
                keyboardInput.y += 1f;

            if (keyboard.sKey.isPressed)
                keyboardInput.y -= 1f;

            if (keyboard.aKey.isPressed)
                keyboardInput.x -= 1f;

            if (keyboard.dKey.isPressed)
                keyboardInput.x += 1f;

            keyboardInput = Vector2.ClampMagnitude(
                keyboardInput, 1f
            );

            keyboardRunning =
                keyboard.leftShiftKey.isPressed &&
                keyboardInput.sqrMagnitude > InputEpsilon;
        }

        keyboardIsActive = keyboardInput.sqrMagnitude > InputEpsilon;

        MinigameVirtualJoystick activeJoystick =
            joystick != null
                ? joystick
                : MinigameVirtualJoystick.Active;

        Vector2 joystickInput = activeJoystick != null
            ? activeJoystick.Value
            : Vector2.zero;

        // Keyboard input is digital and deliberately takes priority while it is
        // being used. This keeps WASD at walk speed (or run speed with Shift)
        // without changing the joystick's analog magnitude.
        return keyboardIsActive
            ? keyboardInput
            : Vector2.ClampMagnitude(joystickInput, 1f);
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
