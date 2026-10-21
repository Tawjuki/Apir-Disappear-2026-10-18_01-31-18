using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ApirApir.UI.Inventory;
using ApirApir.UI.DragDrop;
using System;

namespace ApirApir.UI
{
    /// <summary>
    /// View component for the Crafting screen matching Sketch 1.
    /// Left side: Result preview box, 3 horizontal ingredient slots, Craft button.
    /// Right side: Scrollable Materials inventory grid.
    /// </summary>
    public sealed class UICraftingView : MonoBehaviour
    {
        [Header("Ingredient Slots")]
        public UICraftingIngredientSlot[] ingredientSlots = new UICraftingIngredientSlot[3];

        [Header("Preview Area")]
        public GameObject previewContainer;
        public Image resultIcon;
        public TextMeshProUGUI resultNameLabel;
        public TextMeshProUGUI resultDetailsLabel;
        public GameObject invalidPlaceholder;

        [Header("Craft Button")]
        public Button btnCraft;
        public TextMeshProUGUI craftButtonLabel;

        [Header("Materials Inventory")]
        public UIInventoryGrid materialsGrid;
        public UIDropTarget materialsDropArea;

        [Header("Inventory Popup")]
        [SerializeField] private GameObject inventoryHud;
        [SerializeField] private Button inventoryButton;
        [SerializeField] private bool inventoryStartsOpen = true;

        private void Awake()
        {
            AutoConfigureFromHierarchy();
            if (inventoryHud != null && inventoryButton != null &&
                inventoryButton.transform.IsChildOf(inventoryHud.transform))
            {
                Transform popupParent = inventoryHud.transform.parent;
                if (popupParent != null)
                {
                    inventoryButton.transform.SetParent(popupParent, true);
                    inventoryButton.transform.SetAsLastSibling();
                }
            }

            SetInventoryVisible(inventoryStartsOpen);
        }

        private void OnEnable()
        {
            if (inventoryButton != null)
            {
                inventoryButton.onClick.AddListener(ToggleInventory);
            }
        }

        private void OnDisable()
        {
            if (inventoryButton != null)
            {
                inventoryButton.onClick.RemoveListener(ToggleInventory);
            }
        }

        public void ToggleInventory()
        {
            SetInventoryVisible(inventoryHud == null || !inventoryHud.activeSelf);
        }

        public void SetInventoryVisible(bool visible)
        {
            if (inventoryHud != null)
            {
                inventoryHud.SetActive(visible);
            }
        }

        public void SetPreview(string itemName, string details, Sprite icon, bool isValidRecipe)
        {
            if (invalidPlaceholder != null)
            {
                invalidPlaceholder.SetActive(!isValidRecipe);
            }

            if (resultIcon != null)
            {
                resultIcon.gameObject.SetActive(isValidRecipe && icon != null);
                if (icon != null) resultIcon.sprite = icon;
            }

            if (resultNameLabel != null)
            {
                resultNameLabel.gameObject.SetActive(isValidRecipe);
                resultNameLabel.text = isValidRecipe ? itemName : string.Empty;
            }

            if (resultDetailsLabel != null)
            {
                resultDetailsLabel.gameObject.SetActive(isValidRecipe);
                resultDetailsLabel.text = isValidRecipe
                    ? (resultNameLabel == null ? itemName + "\n" + details : details)
                    : string.Empty;
            }

            SetCraftButtonInteractable(isValidRecipe);
        }

        public void SetCraftButtonInteractable(bool interactable)
        {
            if (btnCraft != null)
            {
                btnCraft.interactable = interactable;
                var img = btnCraft.GetComponent<Image>();
                if (img != null)
                {
                    img.color = interactable ? new Color(0.25f, 0.75f, 0.40f) : new Color(0.4f, 0.4f, 0.4f, 0.7f);
                }
            }
        }

        /// <summary>
        /// Binds the saved notebook hierarchy and supplies only the controls that the
        /// authored scene does not contain. It is idempotent so an editor-authored
        /// reference always wins over the runtime fallback.
        /// </summary>
        public void AutoConfigureFromHierarchy()
        {
            Transform root = transform;
            EnsureDragGhost();
            if (ingredientSlots == null || ingredientSlots.Length != 3)
                ingredientSlots = new UICraftingIngredientSlot[3];
            if (inventoryHud == null)
            {
                Transform found = FindDescendant(root, "InventoryHUD");
                if (found != null) inventoryHud = found.gameObject;
            }

            if (inventoryButton == null)
            {
                Transform found = FindDescendant(root, "InventoryButton");
                if (found != null) inventoryButton = found.GetComponent<Button>();
            }

            Transform itemSlot = FindDescendant(root, "ItemSlot");
            for (int i = 0; i < ingredientSlots.Length; i++)
            {
                if (ingredientSlots[i] != null) continue;
                Transform found = itemSlot == null
                    ? null
                    : FindDescendant(itemSlot, $"ItemSlotSpawnPoint {i + 1}");
                if (found == null) continue;
                ingredientSlots[i] = found.GetComponent<UICraftingIngredientSlot>() ??
                                     found.gameObject.AddComponent<UICraftingIngredientSlot>();
            }

            Transform partSpawn = FindDescendant(root, "PartSpawn");
            if (previewContainer == null && partSpawn != null) previewContainer = partSpawn.gameObject;
            if (resultIcon == null && partSpawn != null)
            {
                Transform icon = FindDescendant(partSpawn, "Icon");
                if (icon != null) resultIcon = icon.GetComponent<Image>();
            }

            if (resultDetailsLabel == null)
            {
                Transform details = FindDescendant(root, "PartsInfo");
                if (details != null) resultDetailsLabel = details.GetComponent<TextMeshProUGUI>();
            }

            if (btnCraft == null)
            {
                btnCraft = CreateButton(root as RectTransform, "CraftButton", "CRAFT",
                    new Vector2(0.58f, 0.07f), new Vector2(0.74f, 0.14f));
                craftButtonLabel = btnCraft == null
                    ? null
                    : btnCraft.GetComponentInChildren<TextMeshProUGUI>();
            }

            EnsureMaterialsGrid();
        }

        public Button CreateNavigationButton(
            string objectName,
            string label,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            return CreateButton(transform as RectTransform, objectName, label, anchorMin, anchorMax);
        }

        private void EnsureMaterialsGrid()
        {
            if (inventoryHud == null) return;
            RectTransform popup = inventoryHud.transform as RectTransform;
            if (popup == null) return;

            if (materialsGrid == null)
            {
                Transform existing = FindDescendant(popup, "MaterialsGrid");
                RectTransform gridRect;
                if (existing != null)
                {
                    gridRect = existing as RectTransform;
                }
                else
                {
                    var gridObject = new GameObject("MaterialsGrid", typeof(RectTransform));
                    gridRect = (RectTransform)gridObject.transform;
                    gridRect.SetParent(popup, false);
                    gridRect.anchorMin = new Vector2(0.08f, 0.08f);
                    gridRect.anchorMax = new Vector2(0.92f, 0.92f);
                    gridRect.offsetMin = gridRect.offsetMax = Vector2.zero;
                    // The saved popup is rotated 180 degrees around Y. Counter-rotate
                    // generated contents so text and drag targets remain readable.
                    if (Vector3.Dot(popup.forward, Vector3.forward) < 0f)
                        gridRect.localRotation = Quaternion.Euler(0f, 180f, 0f);
                }

                if (gridRect != null)
                {
                    var layout = gridRect.GetComponent<GridLayoutGroup>() ??
                                 gridRect.gameObject.AddComponent<GridLayoutGroup>();
                    layout.cellSize = new Vector2(150f, 150f);
                    layout.spacing = new Vector2(18f, 18f);
                    layout.padding = new RectOffset(20, 20, 20, 20);
                    layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                    layout.constraintCount = 4;
                    layout.childAlignment = TextAnchor.UpperCenter;
                    materialsGrid = gridRect.GetComponent<UIInventoryGrid>() ??
                                    gridRect.gameObject.AddComponent<UIInventoryGrid>();
                    materialsGrid.Initialize(gridRect, null);
                }
            }

            if (materialsDropArea == null)
            {
                materialsDropArea = inventoryHud.GetComponent<UIDropTarget>() ??
                                    inventoryHud.AddComponent<UIDropTarget>();
            }
        }

        private void EnsureDragGhost()
        {
            if (UIDragGhost.Instance != null) return;
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;

            var ghostObject = new GameObject(
                "DragGhost", typeof(RectTransform), typeof(CanvasGroup));
            var rect = (RectTransform)ghostObject.transform;
            rect.SetParent(canvas.transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(96f, 96f);
            rect.SetAsLastSibling();

            var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            var iconRect = (RectTransform)iconObject.transform;
            iconRect.SetParent(rect, false);
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = iconRect.offsetMax = Vector2.zero;
            Image icon = iconObject.GetComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            var quantityObject = new GameObject(
                "Quantity", typeof(RectTransform), typeof(TextMeshProUGUI));
            var quantityRect = (RectTransform)quantityObject.transform;
            quantityRect.SetParent(rect, false);
            quantityRect.anchorMin = new Vector2(0.55f, 0f);
            quantityRect.anchorMax = Vector2.one;
            quantityRect.offsetMin = quantityRect.offsetMax = Vector2.zero;
            var quantity = quantityObject.GetComponent<TextMeshProUGUI>();
            quantity.fontSize = 24f;
            quantity.alignment = TextAlignmentOptions.BottomRight;
            quantity.color = Color.white;
            quantity.raycastTarget = false;

            CanvasGroup group = ghostObject.GetComponent<CanvasGroup>();
            UIDragGhost ghost = ghostObject.AddComponent<UIDragGhost>();
            ghost.Initialize(icon, quantity, group, canvas);
        }

        private static Button CreateButton(
            RectTransform parent,
            string objectName,
            string label,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            if (parent == null) return null;
            Transform existing = FindDescendant(parent, objectName);
            if (existing != null)
            {
                Button existingButton = existing.GetComponent<Button>();
                if (existingButton != null) return existingButton;
            }

            var buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = (RectTransform)buttonObject.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.25f, 0.63f, 0.34f, 0.95f);

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            var labelRect = (RectTransform)labelObject.transform;
            labelRect.SetParent(rect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(8f, 4f);
            labelRect.offsetMax = new Vector2(-8f, -4f);
            var text = labelObject.GetComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = 25f;
            text.enableAutoSizing = true;
            text.fontSizeMin = 12f;
            text.fontSizeMax = 30f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;
            return buttonObject.GetComponent<Button>();
        }

        private static Transform FindDescendant(Transform parent, string objectName)
        {
            if (parent == null || string.IsNullOrEmpty(objectName)) return null;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (string.Equals(child.name, objectName, StringComparison.Ordinal)) return child;
                Transform nested = FindDescendant(child, objectName);
                if (nested != null) return nested;
            }
            return null;
        }
    }
}
