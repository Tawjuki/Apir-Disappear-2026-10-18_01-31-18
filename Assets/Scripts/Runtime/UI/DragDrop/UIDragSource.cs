using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ApirApir.UI.Inventory;

namespace ApirApir.UI.DragDrop
{
    /// <summary>
    /// Attached to draggable UI cards and slots.
    /// Manages drag lifecycle and communicates with UIDragGhost.
    /// Does NOT mutate authoritative gameplay data during drag.
    /// </summary>
    public sealed class UIDragSource : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerDownHandler
    {
        [Header("Gesture Recognition")]
        [SerializeField, Min(1f)] private float gestureThreshold = 10f;
        [SerializeField, Range(1f, 3f)] private float verticalScrollBias = 1.35f;

        public Func<UIDragPayload> PayloadProvider { get; set; }
        public Action<bool> DragEndedCallback { get; set; }

        private bool isDragging;
        private bool isScrolling;
        private bool isChoosingGesture;
        private Vector2 gestureStartPosition;
        private UIDragPayload activePayload;
        private ScrollRect parentScrollRect;
        private UIInventoryRowSnapScroller rowSnapScroller;

        public void ConfigureVerticalScroll(ScrollRect scrollRect)
        {
            parentScrollRect = scrollRect;
            rowSnapScroller = parentScrollRect != null
                ? parentScrollRect.GetComponent<UIInventoryRowSnapScroller>()
                : null;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            // Ready for drag initiation
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            isDragging = false;
            isScrolling = false;
            activePayload = null;

            if (parentScrollRect == null)
            {
                BeginItemDrag(eventData != null ? eventData.position : Vector2.zero);
                return;
            }

            // A scrollable inventory cannot reliably choose from the first delta:
            // small touch jitter frequently made intended item drags become scrolling.
            // Wait for a short deliberate gesture, then lock to one behavior.
            isChoosingGesture = true;
            gestureStartPosition = eventData != null
                ? eventData.pressPosition
                : Vector2.zero;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (isChoosingGesture)
            {
                Vector2 displacement = eventData.position - gestureStartPosition;
                if (displacement.sqrMagnitude < gestureThreshold * gestureThreshold)
                {
                    return;
                }

                isChoosingGesture = false;
                bool isDeliberateVerticalSwipe =
                    Mathf.Abs(displacement.y) > Mathf.Abs(displacement.x) * verticalScrollBias;
                if (isDeliberateVerticalSwipe)
                {
                    isScrolling = true;
                    parentScrollRect.OnBeginDrag(eventData);
                    rowSnapScroller?.NotifyForwardedBeginDrag(eventData);
                }
                else
                {
                    BeginItemDrag(eventData.position);
                }
            }

            if (isScrolling)
            {
                parentScrollRect.OnDrag(eventData);
                return;
            }

            if (!isDragging) return;
            if (UIDragGhost.Instance != null)
            {
                UIDragGhost.Instance.UpdatePosition(eventData.position);
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (isChoosingGesture)
            {
                isChoosingGesture = false;
                return;
            }

            if (isScrolling)
            {
                isScrolling = false;
                parentScrollRect.OnEndDrag(eventData);
                rowSnapScroller?.NotifyForwardedEndDrag(eventData);
                return;
            }

            if (!isDragging) return;
            isDragging = false;

            bool wasDroppedOnValidTarget = false;
            // Check if dropped onto a UIDropTarget
            if (eventData.pointerEnter != null)
            {
                var dropTarget = eventData.pointerEnter.GetComponentInParent<UIDropTarget>();
                if (dropTarget != null && dropTarget.Accepts(activePayload))
                {
                    wasDroppedOnValidTarget = true;
                }
            }

            if (UIDragGhost.Instance != null)
            {
                UIDragGhost.Instance.Hide();
            }

            DragEndedCallback?.Invoke(wasDroppedOnValidTarget);
            activePayload = null;
        }

        private void BeginItemDrag(Vector2 pointerPosition)
        {
            if (PayloadProvider == null)
            {
                return;
            }

            activePayload = PayloadProvider();
            if (activePayload == null)
            {
                return;
            }

            isDragging = true;
            if (UIDragGhost.Instance != null)
            {
                UIDragGhost.Instance.Show(activePayload, pointerPosition);
            }
        }
    }
}
