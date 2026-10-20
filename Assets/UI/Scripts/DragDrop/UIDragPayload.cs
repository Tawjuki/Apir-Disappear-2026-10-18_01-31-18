using UnityEngine;

namespace ApirApir.UI.DragDrop
{
    /// <summary>
    /// Data payload carried during an ongoing drag-and-drop operation.
    /// Does NOT mutate authoritative inventory state until a drop is confirmed.
    /// </summary>
    public sealed class UIDragPayload
    {
        public object SourceContext { get; }
        public string ItemId { get; }
        public string DisplayName { get; }
        public Sprite Icon { get; }
        public int Quantity { get; }
        public string Category { get; }

        public UIDragPayload(
            object sourceContext,
            string itemId,
            string displayName,
            Sprite icon,
            int quantity = 1,
            string category = "Material")
        {
            SourceContext = sourceContext;
            ItemId = itemId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Icon = icon;
            Quantity = Mathf.Max(1, quantity);
            Category = category ?? string.Empty;
        }
    }
}
