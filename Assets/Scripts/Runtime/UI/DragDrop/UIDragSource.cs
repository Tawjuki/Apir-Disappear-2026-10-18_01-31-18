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
        public Func<UIDragPayload> PayloadProvider { get; set; }
        public Action<bool> DragEndedCallback { get; set; }

        private bool isDragging;
        private bool isScrolling;
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
            if (parentScrollRect != null &&
                Mathf.Abs(eventData.delta.y) > Mathf.Abs(eventData.delta.x))
            {
                isScrolling = true;
                parentScrollRect.OnBeginDrag(eventData);
                rowSnapScroller?.NotifyForwardedBeginDrag(eventData);
                return;
            }

            if (PayloadProvider == null) return;
            activePayload = PayloadProvider();
            if (activePayload == null) return;

            isDragging = true;
            if (UIDragGhost.Instance != null)
            {
                UIDragGhost.Instance.Show(activePayload, eventData.position);
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
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
    }
}
