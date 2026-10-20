using UnityEngine;
using UnityEngine.UI;

namespace ApirApir.UI
{
    /// <summary>
    /// Manages shop category tab switching (Materials / Parts / Maintenance).
    /// </summary>
    public class ShopCategoryController : MonoBehaviour
    {
        public enum ShopCategory { Materials, Parts, Maintenance }

        [System.Serializable]
        public class CategoryTab
        {
            public ShopCategory category;
            public Button button;
        }

        [Header("Category Tabs")]
        public CategoryTab[] tabs;

        [Header("Shop Item Content")]
        public Transform shopItemContent; // VerticalLayoutGroup content parent

        [Header("Colors")]
        public Color activeColor   = new Color(1f, 0.85f, 0.2f);
        public Color inactiveColor = new Color(0.7f, 0.7f, 0.7f);

        private ShopCategory _activeCategory = ShopCategory.Materials;

        private void Start()
        {
            for (int i = 0; i < tabs.Length; i++)
            {
                int idx = i;
                if (tabs[i].button)
                    tabs[i].button.onClick.AddListener(() => SelectCategory(tabs[idx].category));
            }
            SelectCategory(ShopCategory.Materials);
        }

        public void SelectCategory(ShopCategory cat)
        {
            _activeCategory = cat;
            UpdateTabColors();
            FilterItemsByCategory();
        }

        private void UpdateTabColors()
        {
            foreach (var tab in tabs)
            {
                if (tab.button == null) continue;
                var img = tab.button.GetComponent<Image>();
                if (img) img.color = tab.category == _activeCategory ? activeColor : inactiveColor;
            }
        }

        private void FilterItemsByCategory()
        {
            if (shopItemContent == null) return;
            foreach (Transform child in shopItemContent)
            {
                // Future: filter by ItemCardUI.category field
                child.gameObject.SetActive(true);
            }
        }
    }
}
