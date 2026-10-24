using System.Collections.Generic;
using ApirApir.Crafting;
using ApirApir.UI;
using UnityEngine;
using UnityEngine.UI;

namespace ApirApir.AugmentedReality
{
    /// <summary>Applies AR-record offsets, scale, and layer order after the base preview layout.</summary>
    public static class ARCardVisualLayout
    {
        private readonly struct Layer
        {
            public Layer(Image image, int order)
            {
                Image = image;
                Order = order;
            }

            public Image Image { get; }
            public int Order { get; }
        }

        public static void Apply(
            UICardPreview preview,
            ARCardPartRecord head,
            ARCardPartRecord body,
            ARCardPartRecord legs)
        {
            if (preview == null) return;
            ApplyTransform(preview.HeadSlot, head?.offsetX ?? 0f, head?.offsetY ?? 0f, head?.scale ?? 1f);
            ApplyTransform(preview.BodySlot, body?.offsetX ?? 0f, body?.offsetY ?? 0f, body?.scale ?? 1f);
            ApplyTransform(preview.LegsSlot, legs?.offsetX ?? 0f, legs?.offsetY ?? 0f, legs?.scale ?? 1f);
            ApplyLayers(preview,
                head == null ? null : new Layer(preview.HeadSlot, head.layerOrder),
                body == null ? null : new Layer(preview.BodySlot, body.layerOrder),
                legs == null ? null : new Layer(preview.LegsSlot, legs.layerOrder));
        }

        public static void Apply(
            UICardPreview preview,
            CraftablePartDefinition head,
            CraftablePartDefinition body,
            CraftablePartDefinition legs)
        {
            if (preview == null) return;
            ApplyTransform(preview.HeadSlot, head?.VisualOffsetX ?? 0f, head?.VisualOffsetY ?? 0f, head?.VisualScale ?? 1f);
            ApplyTransform(preview.BodySlot, body?.VisualOffsetX ?? 0f, body?.VisualOffsetY ?? 0f, body?.VisualScale ?? 1f);
            ApplyTransform(preview.LegsSlot, legs?.VisualOffsetX ?? 0f, legs?.VisualOffsetY ?? 0f, legs?.VisualScale ?? 1f);
            ApplyLayers(preview,
                head == null ? null : new Layer(preview.HeadSlot, head.VisualLayerOrder),
                body == null ? null : new Layer(preview.BodySlot, body.VisualLayerOrder),
                legs == null ? null : new Layer(preview.LegsSlot, legs.VisualLayerOrder));
        }

        private static void ApplyTransform(Image image, float offsetX, float offsetY, float scale)
        {
            if (image == null) return;
            RectTransform rect = image.rectTransform;
            rect.anchoredPosition += new Vector2(offsetX, offsetY);
            rect.localScale *= Mathf.Max(0.01f, scale);
        }

        private static void ApplyLayers(UICardPreview preview, params Layer?[] source)
        {
            var layers = new List<Layer>(3);
            for (int i = 0; i < source.Length; i++)
                if (source[i].HasValue && source[i].Value.Image != null) layers.Add(source[i].Value);
            layers.Sort((left, right) => left.Order.CompareTo(right.Order));
            if (preview.CardBackground != null) preview.CardBackground.transform.SetAsFirstSibling();
            for (int i = 0; i < layers.Count; i++) layers[i].Image.transform.SetAsLastSibling();
        }
    }
}
