using System;
using UnityEngine;

namespace ApirApir.Minigames
{
    public enum MinigameActionSlot : byte
    {
        Primary,
        Secondary,
        Modifier,
        Utility
    }

    [Flags]
    public enum MinigameActionMask : byte
    {
        None = 0,
        Primary = 1 << 0,
        Secondary = 1 << 1,
        Modifier = 1 << 2,
        Utility = 1 << 3
    }

    /// <summary>
    /// Local player intent only. A minigame must still validate every movement and
    /// action against its own authoritative rules before changing gameplay state.
    /// </summary>
    public readonly struct MinigameInputFrame
    {
        public MinigameInputFrame(
            Vector2 movement,
            MinigameActionMask heldActions,
            MinigameActionMask pressedActions,
            MinigameActionMask releasedActions)
        {
            Movement = Vector2.ClampMagnitude(movement, 1f);
            HeldActions = heldActions;
            PressedActions = pressedActions;
            ReleasedActions = releasedActions;
        }

        public Vector2 Movement { get; }
        public MinigameActionMask HeldActions { get; }
        public MinigameActionMask PressedActions { get; }
        public MinigameActionMask ReleasedActions { get; }

        public bool IsHeld(MinigameActionSlot slot) =>
            (HeldActions & ToMask(slot)) != MinigameActionMask.None;

        public bool WasPressed(MinigameActionSlot slot) =>
            (PressedActions & ToMask(slot)) != MinigameActionMask.None;

        public bool WasReleased(MinigameActionSlot slot) =>
            (ReleasedActions & ToMask(slot)) != MinigameActionMask.None;

        public static MinigameActionMask ToMask(MinigameActionSlot slot)
        {
            int index = (int)slot;
            return index >= 0 && index < 4
                ? (MinigameActionMask)(1 << index)
                : MinigameActionMask.None;
        }
    }
}
