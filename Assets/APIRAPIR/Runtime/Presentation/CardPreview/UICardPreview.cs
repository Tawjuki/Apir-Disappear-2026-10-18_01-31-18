using UnityEngine;
using UnityEngine.UI;

namespace ApirApir.UI
{
    /// <summary>
    /// Presents a card background and independently replaceable modular character sprites.
    /// This view intentionally owns no card, crafting, combat, or networking state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UICardPreview : MonoBehaviour
    {
        [Header("Image References")]
        [SerializeField] private Image cardBackground;
        [SerializeField] private Image legsSlot;
        [SerializeField] private Image bodySlot;
        [SerializeField] private Image headSlot;

        [Header("Sprites")]
        [Tooltip("Optional card-base sprite. A sprite can be assigned or replaced at runtime.")]
        [SerializeField] private Sprite cardBackgroundSprite;
        [SerializeField] private Sprite legsSprite;
        [SerializeField] private Sprite bodySprite;
        [SerializeField] private Sprite headSprite;

        [Header("Part Placement")]
        [SerializeField] private Vector2 legsOffset = new Vector2(0f, -112f);
        [SerializeField] private Vector2 legsSize = new Vector2(230f, 160f);
        [SerializeField] private Vector3 legsScale = Vector3.one;
        [SerializeField] private Vector2 bodyOffset = Vector2.zero;
        [SerializeField] private Vector2 bodySize = new Vector2(250f, 210f);
        [SerializeField] private Vector3 bodyScale = Vector3.one;
        [SerializeField] private Vector2 headOffset = new Vector2(0f, 118f);
        [SerializeField] private Vector2 headSize = new Vector2(205f, 170f);
        [SerializeField] private Vector3 headScale = Vector3.one;

        [Header("Part Sibling Order")]
        [Tooltip("Lower values render first. The card background is always forced behind every part.")]
        [SerializeField] private int legsSiblingOrder = 0;
        [SerializeField] private int bodySiblingOrder = 10;
        [SerializeField] private int headSiblingOrder = 20;

        public Image CardBackground => cardBackground;
        public Image LegsSlot => legsSlot;
        public Image BodySlot => bodySlot;
        public Image HeadSlot => headSlot;
        public RectTransform LegsSlotTransform => legsSlot == null ? null : legsSlot.rectTransform;
        public RectTransform BodySlotTransform => bodySlot == null ? null : bodySlot.rectTransform;
        public RectTransform HeadSlotTransform => headSlot == null ? null : headSlot.rectTransform;
        public Sprite CardBackgroundSprite => cardBackgroundSprite;
        public Sprite LegsSprite => legsSprite;
        public Sprite BodySprite => bodySprite;
        public Sprite HeadSprite => headSprite;

        private void Awake()
        {
            Refresh();
        }

        /// <summary>
        /// Assigns an existing UI hierarchy without creating or destroying GameObjects.
        /// </summary>
        public void BindSlots(Image background, Image legs, Image body, Image head)
        {
            cardBackground = background;
            legsSlot = legs;
            bodySlot = body;
            headSlot = head;
            Refresh();
        }

        public void SetCardBackground(Sprite sprite)
        {
            cardBackgroundSprite = sprite;
            ApplyBackground();
        }

        public void SetHead(Sprite sprite)
        {
            headSprite = sprite;
            ApplyPart(headSlot, headSprite);
        }

        public void SetBody(Sprite sprite)
        {
            bodySprite = sprite;
            ApplyPart(bodySlot, bodySprite);
        }

        public void SetLegs(Sprite sprite)
        {
            legsSprite = sprite;
            ApplyPart(legsSlot, legsSprite);
        }

        public void SetParts(Sprite head, Sprite body, Sprite legs)
        {
            headSprite = head;
            bodySprite = body;
            legsSprite = legs;
            ApplyParts();
        }

        public void ConfigureLayout(
            Vector2 headPosition, Vector2 headDimensions,
            Vector2 bodyPosition, Vector2 bodyDimensions,
            Vector2 legsPosition, Vector2 legsDimensions)
        {
            headOffset = headPosition;
            headSize = headDimensions;
            bodyOffset = bodyPosition;
            bodySize = bodyDimensions;
            legsOffset = legsPosition;
            legsSize = legsDimensions;
            ApplyLayout();
        }

        public void ClearParts()
        {
            SetParts(null, null, null);
        }

        /// <summary>Reapplies serialized sprites, placement, and render order.</summary>
        [ContextMenu("Refresh Card Preview")]
        public void Refresh()
        {
            ApplyBackground();
            ApplyParts();
            ApplyLayout();
        }

        /// <summary>Applies Inspector-authored offsets without touching assigned sprites.</summary>
        [ContextMenu("Apply Card Preview Layout")]
        public void ApplyLayout()
        {
            ApplySlotLayout(legsSlot, legsOffset, legsSize, legsScale);
            ApplySlotLayout(bodySlot, bodyOffset, bodySize, bodyScale);
            ApplySlotLayout(headSlot, headOffset, headSize, headScale);
            ApplySiblingOrder();
        }

        private void ApplyBackground()
        {
            if (cardBackground == null)
            {
                return;
            }

            cardBackground.sprite = cardBackgroundSprite;
            cardBackground.enabled = true;
            cardBackground.raycastTarget = false;
        }

        private void ApplyParts()
        {
            ApplyPart(legsSlot, legsSprite);
            ApplyPart(bodySlot, bodySprite);
            ApplyPart(headSlot, headSprite);
        }

        private static void ApplyPart(Image slot, Sprite sprite)
        {
            if (slot == null)
            {
                return;
            }

            slot.sprite = sprite;
            slot.enabled = sprite != null;
            slot.raycastTarget = false;
        }

        private static void ApplySlotLayout(Image slot, Vector2 offset, Vector2 size, Vector3 scale)
        {
            if (slot == null)
            {
                return;
            }

            RectTransform rect = slot.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
            rect.localScale = scale;
        }

        private void ApplySiblingOrder()
        {
            if (cardBackground != null)
            {
                cardBackground.transform.SetAsFirstSibling();
            }

            Image first = legsSlot;
            Image second = bodySlot;
            Image third = headSlot;
            int firstOrder = legsSiblingOrder;
            int secondOrder = bodySiblingOrder;
            int thirdOrder = headSiblingOrder;

            SwapIfOutOfOrder(ref first, ref firstOrder, ref second, ref secondOrder);
            SwapIfOutOfOrder(ref second, ref secondOrder, ref third, ref thirdOrder);
            SwapIfOutOfOrder(ref first, ref firstOrder, ref second, ref secondOrder);

            MoveToFront(first);
            MoveToFront(second);
            MoveToFront(third);
        }

        private static void SwapIfOutOfOrder(
            ref Image left,
            ref int leftOrder,
            ref Image right,
            ref int rightOrder)
        {
            if (leftOrder <= rightOrder)
            {
                return;
            }

            (leftOrder, rightOrder) = (rightOrder, leftOrder);
            (left, right) = (right, left);
        }

        private static void MoveToFront(Image image)
        {
            if (image != null)
            {
                image.transform.SetAsLastSibling();
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            ApplyBackground();
            ApplyParts();
            ApplySlotLayout(legsSlot, legsOffset, legsSize, legsScale);
            ApplySlotLayout(bodySlot, bodyOffset, bodySize, bodyScale);
            ApplySlotLayout(headSlot, headOffset, headSize, headScale);
        }
#endif
    }
}
