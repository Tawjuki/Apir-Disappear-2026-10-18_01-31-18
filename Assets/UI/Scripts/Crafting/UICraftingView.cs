using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ApirApir.UI.Inventory;
using ApirApir.UI.DragDrop;

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
                resultDetailsLabel.text = isValidRecipe ? details : string.Empty;
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
    }
}
