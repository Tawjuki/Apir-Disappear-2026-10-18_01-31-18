using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ApirApir.Minigames
{
    public enum PlayerInputSource : byte
    {
        None,
        Keyboard,
        VirtualJoystick
    }

    /// <summary>
    /// Platform input shared by every gameplay scene. It reports player intent only;
    /// scene and minigame code remains responsible for speed, permissions and authority.
    /// </summary>
    [DisallowMultipleComponent]
    public class UniversalPlayerInput : MonoBehaviour
    {
        private const float ActiveInputEpsilon = 0.0001f;

        [Header("Mobile")]
        [SerializeField] private MinigameVirtualJoystick virtualJoystick;
        [SerializeField] private MinigameActionButton primaryButton;
        [SerializeField] private MinigameActionButton secondaryButton;
        [SerializeField] private MinigameActionButton modifierButton;
        [SerializeField] private MinigameActionButton utilityButton;

        [Header("Keyboard testing")]
        [SerializeField] private bool allowKeyboardInput = true;
        [SerializeField] private Key primaryKey = Key.Space;
        [SerializeField] private Key secondaryKey = Key.E;
        [SerializeField] private Key modifierKey = Key.LeftShift;
        [SerializeField] private Key utilityKey = Key.Q;

        private bool inputEnabled = true;

        public bool InputEnabled => inputEnabled;
        public PlayerInputSource LastMovementSource { get; private set; }
        public event Action<MinigameActionSlot> ActionPressed;

        public void SetVirtualJoystick(MinigameVirtualJoystick joystick)
        {
            virtualJoystick = joystick;
        }

        public void SetActionContext(
            MinigameActionSlot slot,
            string actionLabel,
            bool available)
        {
            MinigameContextActionView.GetActive(slot)?.SetContext(actionLabel, available);
        }

        public void SetInputEnabled(bool value)
        {
            if (inputEnabled == value)
            {
                return;
            }

            inputEnabled = value;
            LastMovementSource = PlayerInputSource.None;
            if (!value)
            {
                ResolveJoystick()?.CancelInput();
                ResetActionButtons();
            }
        }

        public Vector2 ReadMovement()
        {
            if (!inputEnabled)
            {
                LastMovementSource = PlayerInputSource.None;
                return Vector2.zero;
            }

            Vector2 keyboardMovement = allowKeyboardInput
                ? ReadKeyboardMovement()
                : Vector2.zero;

            // Keyboard is intentionally prioritized for deterministic editor testing.
            // Touch input remains analog and is never added to a second input source.
            if (keyboardMovement.sqrMagnitude > ActiveInputEpsilon)
            {
                LastMovementSource = PlayerInputSource.Keyboard;
                return keyboardMovement;
            }

            MinigameVirtualJoystick joystick = ResolveJoystick();
            Vector2 joystickMovement = joystick != null ? joystick.Value : Vector2.zero;
            if (joystickMovement.sqrMagnitude > ActiveInputEpsilon)
            {
                LastMovementSource = PlayerInputSource.VirtualJoystick;
                return Vector2.ClampMagnitude(joystickMovement, 1f);
            }

            LastMovementSource = PlayerInputSource.None;
            return Vector2.zero;
        }

        public MinigameInputFrame ReadInput()
        {
            if (!inputEnabled)
            {
                return default;
            }

            MinigameActionMask held = MinigameActionMask.None;
            MinigameActionMask pressed = MinigameActionMask.None;
            MinigameActionMask released = MinigameActionMask.None;
            Keyboard keyboard = allowKeyboardInput ? Keyboard.current : null;

            ReadAction(MinigameActionSlot.Primary, primaryButton, primaryKey, keyboard,
                ref held, ref pressed, ref released);
            ReadAction(MinigameActionSlot.Secondary, secondaryButton, secondaryKey, keyboard,
                ref held, ref pressed, ref released);
            ReadAction(MinigameActionSlot.Modifier, modifierButton, modifierKey, keyboard,
                ref held, ref pressed, ref released);
            ReadAction(MinigameActionSlot.Utility, utilityButton, utilityKey, keyboard,
                ref held, ref pressed, ref released);

            PublishPressedActions(pressed);

            return new MinigameInputFrame(ReadMovement(), held, pressed, released);
        }

        protected virtual void OnDisable()
        {
            LastMovementSource = PlayerInputSource.None;
            ResolveJoystick()?.CancelInput();
            ResetActionButtons();
        }

        private MinigameVirtualJoystick ResolveJoystick()
        {
            return virtualJoystick != null ? virtualJoystick : MinigameVirtualJoystick.Active;
        }

        private static Vector2 ReadKeyboardMovement()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return Vector2.zero;
            }

            float horizontal = 0f;
            float vertical = 0f;

            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) horizontal -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) horizontal += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) vertical -= 1f;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) vertical += 1f;

            return Vector2.ClampMagnitude(new Vector2(horizontal, vertical), 1f);
        }

        private static void ReadAction(
            MinigameActionSlot slot,
            MinigameActionButton configuredButton,
            Key key,
            Keyboard keyboard,
            ref MinigameActionMask held,
            ref MinigameActionMask pressed,
            ref MinigameActionMask released)
        {
            MinigameActionButton button = ResolveActionButton(slot, configuredButton);
            MinigameActionMask mask = MinigameInputFrame.ToMask(slot);

            if ((button != null && button.IsHeld) || (keyboard != null && keyboard[key].isPressed))
            {
                held |= mask;
            }

            if ((button != null && button.ConsumePressed()) ||
                (keyboard != null && keyboard[key].wasPressedThisFrame))
            {
                pressed |= mask;
            }

            if ((button != null && button.ConsumeReleased()) ||
                (keyboard != null && keyboard[key].wasReleasedThisFrame))
            {
                released |= mask;
            }
        }

        private void ResetActionButtons()
        {
            ResolveActionButton(MinigameActionSlot.Primary, primaryButton)?.CancelInput();
            ResolveActionButton(MinigameActionSlot.Secondary, secondaryButton)?.CancelInput();
            ResolveActionButton(MinigameActionSlot.Modifier, modifierButton)?.CancelInput();
            ResolveActionButton(MinigameActionSlot.Utility, utilityButton)?.CancelInput();
        }

        private void PublishPressedActions(MinigameActionMask pressed)
        {
            if (pressed == MinigameActionMask.None || ActionPressed == null) return;
            for (int i = 0; i < 4; i++)
            {
                MinigameActionSlot slot = (MinigameActionSlot)i;
                if ((pressed & MinigameInputFrame.ToMask(slot)) != 0)
                {
                    ActionPressed.Invoke(slot);
                }
            }
        }

        private static MinigameActionButton ResolveActionButton(
            MinigameActionSlot slot,
            MinigameActionButton configuredButton)
        {
            return configuredButton != null
                ? configuredButton
                : MinigameActionButton.GetActive(slot);
        }
    }
}
