using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ApirApir.UI.Inventory
{
    /// <summary>
    /// Snaps a vertical inventory grid to complete rows after a swipe. The drag remains
    /// responsive, but no inertia or animated settling can leave partially visible rows.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ScrollRect))]
    public sealed class UIInventoryRowSnapScroller : MonoBehaviour,
        IBeginDragHandler,
        IEndDragHandler,
        IScrollHandler
    {
        [SerializeField, Min(1)] private int columnCount = 3;
        [SerializeField, Min(1)] private int visibleRowCount = 4;
        [SerializeField, Min(1f)] private float minimumSwipeDistance = 30f;

        private ScrollRect scrollRect;
        private GridLayoutGroup gridLayout;
        private int rowAtDragStart;
        private float pointerYAtDragStart;

        public void Initialize(
            ScrollRect targetScrollRect,
            GridLayoutGroup targetGridLayout,
            int columns,
            int visibleRows)
        {
            scrollRect = targetScrollRect;
            gridLayout = targetGridLayout;
            columnCount = Mathf.Max(1, columns);
            visibleRowCount = Mathf.Max(1, visibleRows);

            if (scrollRect != null)
            {
                scrollRect.horizontal = false;
                scrollRect.vertical = true;
                scrollRect.inertia = false;
                scrollRect.movementType = ScrollRect.MovementType.Clamped;
            }

            SnapToNearestRow();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            rowAtDragStart = GetNearestRow();
            pointerYAtDragStart = eventData != null ? eventData.position.y : 0f;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            float pointerY = eventData != null ? eventData.position.y : pointerYAtDragStart;
            float dragDistance = pointerY - pointerYAtDragStart;
            int direction = Mathf.Abs(dragDistance) >= minimumSwipeDistance
                ? (dragDistance > 0f ? 1 : -1)
                : 0;
            SnapToRow(rowAtDragStart + direction);
        }

        public void OnScroll(PointerEventData eventData)
        {
            float scrollDelta = eventData != null ? eventData.scrollDelta.y : 0f;
            int direction = scrollDelta < 0f ? 1 : scrollDelta > 0f ? -1 : 0;
            SnapToRow(GetNearestRow() + direction);
        }

        public void NotifyForwardedBeginDrag(PointerEventData eventData)
        {
            OnBeginDrag(eventData);
        }

        public void NotifyForwardedEndDrag(PointerEventData eventData)
        {
            OnEndDrag(eventData);
        }

        public void SnapToNearestRow()
        {
            SnapToRow(GetNearestRow());
        }

        private void SnapToRow(int row)
        {
            if (!TryGetContent(out RectTransform content))
            {
                return;
            }

            int maxRow = GetMaximumTopRow(content);
            int targetRow = Mathf.Clamp(row, 0, maxRow);
            Vector2 position = content.anchoredPosition;
            position.y = targetRow * GetRowStride();
            content.anchoredPosition = position;
            rowAtDragStart = targetRow;
        }

        private int GetNearestRow()
        {
            if (!TryGetContent(out RectTransform content))
            {
                return rowAtDragStart;
            }

            return Mathf.RoundToInt(Mathf.Max(0f, content.anchoredPosition.y) / GetRowStride());
        }

        private int GetMaximumTopRow(RectTransform content)
        {
            int activeChildren = 0;
            for (int i = 0; i < content.childCount; i++)
            {
                if (content.GetChild(i).gameObject.activeSelf)
                {
                    activeChildren++;
                }
            }

            int totalRows = Mathf.CeilToInt(activeChildren / (float)columnCount);
            return Mathf.Max(0, totalRows - visibleRowCount);
        }

        private float GetRowStride()
        {
            return gridLayout != null
                ? Mathf.Max(1f, gridLayout.cellSize.y + gridLayout.spacing.y)
                : 1f;
        }

        private bool TryGetContent(out RectTransform content)
        {
            if (scrollRect == null)
            {
                scrollRect = GetComponent<ScrollRect>();
            }

            content = scrollRect != null ? scrollRect.content : null;
            if (content != null && gridLayout == null)
            {
                gridLayout = content.GetComponent<GridLayoutGroup>();
            }

            return content != null;
        }
    }
}
