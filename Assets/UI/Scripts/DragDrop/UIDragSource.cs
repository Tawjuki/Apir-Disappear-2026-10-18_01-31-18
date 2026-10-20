using System;
using UnityEngine;
using UnityEngine.EventSystems;

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
        private UIDragPayload activePayload;

        public void OnPointerDown(PointerEventData eventData)
        {
            // Ready for drag initiation
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
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
            if (!isDragging) return;
            if (UIDragGhost.Instance != null)
            {
                UIDragGhost.Instance.UpdatePosition(eventData.position);
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
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
