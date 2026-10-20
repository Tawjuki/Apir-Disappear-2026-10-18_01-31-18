using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ApirApir.UI
{
    /// <summary>
    /// Manages the five soul tab buttons and filters the skill catalog.
    /// </summary>
    public class SoulTabController : MonoBehaviour
    {
        [System.Serializable]
        public class SoulTab
        {
            public string soulName;
            public Button button;
        }

        [Header("Soul Tabs")]
        public SoulTab[] soulTabs;

        [Header("Skill Catalog")]
        public Transform skillCatalogContent; // GridLayoutGroup content parent

        [Header("Colors")]
        public Color activeTabColor   = new Color(1f, 0.85f, 0.2f);
        public Color inactiveTabColor = new Color(0.6f, 0.6f, 0.8f);

        private string _activeSoul = "";

        private void Start()
        {
            for (int i = 0; i < soulTabs.Length; i++)
            {
                int idx = i;
                if (soulTabs[i].button)
                    soulTabs[i].button.onClick.AddListener(() => SelectSoul(soulTabs[idx].soulName));
            }
            if (soulTabs.Length > 0) SelectSoul(soulTabs[0].soulName);
        }

        public void SelectSoul(string soulName)
        {
            _activeSoul = soulName;
            UpdateTabColors();
            FilterSkillsByActiveSoul();
        }

        private void UpdateTabColors()
        {
            foreach (var tab in soulTabs)
            {
                if (tab.button == null) continue;
                var img = tab.button.GetComponent<Image>();
                if (img) img.color = tab.soulName == _activeSoul ? activeTabColor : inactiveTabColor;
            }
        }

        private void FilterSkillsByActiveSoul()
        {
            if (skillCatalogContent == null) return;
            foreach (Transform child in skillCatalogContent)
            {
                var card = child.GetComponent<SkillCardUI>();
                if (card == null) continue;
                bool match = string.IsNullOrEmpty(_activeSoul) || card.soulCategory == _activeSoul;
                child.gameObject.SetActive(match);
            }
        }
    }
}
