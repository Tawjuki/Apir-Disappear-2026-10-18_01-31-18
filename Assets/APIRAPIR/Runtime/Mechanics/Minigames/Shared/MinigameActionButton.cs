using UnityEngine;
using UnityEngine.EventSystems;

namespace ApirApir.Minigames
{
    [DisallowMultipleComponent]
    public sealed class MinigameActionButton : MonoBehaviour,
        IPointerDownHandler,
        IPointerUpHandler
    {
        private const int NoPointer = int.MinValue;
        private static readonly MinigameActionButton[] ActiveButtons = new MinigameActionButton[4];

        [SerializeField] private MinigameActionSlot actionSlot = MinigameActionSlot.Primary;

        private int activePointerId = NoPointer;
        private bool isHeld;
        private bool pressed;
        private bool released;

        public MinigameActionSlot ActionSlot => actionSlot;
        public bool IsHeld => isHeld;

        public static MinigameActionButton GetActive(MinigameActionSlot slot)
        {
            int index = (int)slot;
            return index >= 0 && index < ActiveButtons.Length ? ActiveButtons[index] : null;
        }

        private void OnEnable()
        {
            int index = GetSlotIndex(actionSlot);
            if (index >= 0)
            {
                ActiveButtons[index] = this;
            }

            ResetInput();
        }

        private void OnDisable()
        {
            int index = GetSlotIndex(actionSlot);
            if (index >= 0 && ActiveButtons[index] == this)
            {
                ActiveButtons[index] = null;
            }

            ResetInput();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData == null || activePointerId != NoPointer)
            {
                return;
            }

            activePointerId = eventData.pointerId;
            isHeld = true;
            pressed = true;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData == null || eventData.pointerId != activePointerId)
            {
                return;
            }

            activePointerId = NoPointer;
            isHeld = false;
            released = true;
        }

        public bool ConsumePressed()
        {
            bool result = pressed;
            pressed = false;
            return result;
        }

        public bool ConsumeReleased()
        {
            bool result = released;
            released = false;
            return result;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && isHeld)
            {
                released = true;
                activePointerId = NoPointer;
                isHeld = false;
            }
        }

        private void ResetInput()
        {
            activePointerId = NoPointer;
            isHeld = false;
            pressed = false;
            released = false;
        }

        private static int GetSlotIndex(MinigameActionSlot slot)
        {
            int index = (int)slot;
            return index >= 0 && index < ActiveButtons.Length ? index : -1;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (GetSlotIndex(actionSlot) < 0)
            {
                actionSlot = MinigameActionSlot.Primary;
            }
        }
#endif
    }
}
