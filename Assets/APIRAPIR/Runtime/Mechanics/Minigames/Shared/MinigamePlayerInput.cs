using UnityEngine;
using UnityEngine.InputSystem;

namespace ApirApir.Minigames
{
    /// <summary>
    /// Reads platform input without applying gameplay rules or changing world state.
    /// Each minigame decides what the generic action slots mean and whether they are allowed.
    /// </summary>
    public class MinigamePlayerInput : MonoBehaviour
    {
        [Header("Mobile")]
        [SerializeField] private MinigameVirtualJoystick virtualJoystick;
        [SerializeField] private MinigameActionButton primaryButton;
        [SerializeField] private MinigameActionButton secondaryButton;
        [SerializeField] private MinigameActionButton modifierButton;
        [SerializeField] private MinigameActionButton utilityButton;

        [Header("Editor keyboard actions")]
        [SerializeField] private Key primaryKey = Key.Space;
        [SerializeField] private Key secondaryKey = Key.E;
        [SerializeField] private Key modifierKey = Key.LeftShift;
        [SerializeField] private Key utilityKey = Key.Q;

        public Vector2 ReadMovement()
        {
            Vector2 movement = ReadKeyboardMovement();
            MinigameVirtualJoystick joystick = virtualJoystick != null
                ? virtualJoystick
                : MinigameVirtualJoystick.Active;

            if (joystick != null)
            {
                movement += joystick.Value;
            }

            return Vector2.ClampMagnitude(movement, 1f);
        }

        public MinigameInputFrame ReadInput()
        {
            MinigameActionMask held = MinigameActionMask.None;
            MinigameActionMask pressed = MinigameActionMask.None;
            MinigameActionMask released = MinigameActionMask.None;
            Keyboard keyboard = Keyboard.current;

            ReadAction(MinigameActionSlot.Primary, primaryButton, primaryKey, keyboard,
                ref held, ref pressed, ref released);
            ReadAction(MinigameActionSlot.Secondary, secondaryButton, secondaryKey, keyboard,
                ref held, ref pressed, ref released);
            ReadAction(MinigameActionSlot.Modifier, modifierButton, modifierKey, keyboard,
                ref held, ref pressed, ref released);
            ReadAction(MinigameActionSlot.Utility, utilityButton, utilityKey, keyboard,
                ref held, ref pressed, ref released);

            return new MinigameInputFrame(ReadMovement(), held, pressed, released);
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

        private void ReadAction(
            MinigameActionSlot slot,
            MinigameActionButton configuredButton,
            Key key,
            Keyboard keyboard,
            ref MinigameActionMask held,
            ref MinigameActionMask pressed,
            ref MinigameActionMask released)
        {
            MinigameActionButton button = configuredButton != null
                ? configuredButton
                : MinigameActionButton.GetActive(slot);
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
    }
}
