using UnityEngine;
using UnityEngine.UI;

namespace ApirApir.UI
{
    /// <summary>
    /// Controls top-level navigation between the main lobby panels.
    /// </summary>
    public class LobbyNavigator : MonoBehaviour
    {
        [Header("Panels")]
        public GameObject craftingPanel;
        public GameObject shopPanel;
        public GameObject skillPanel;
        public GameObject cardBuilderPanel;

        [Header("Nav Buttons")]
        public Button btnCrafting;
        public Button btnShop;
        public Button btnSkill;
        public Button btnCardBuilder;

        [Header("Active Button Highlight")]
        public Color activeColor = new Color(1f, 0.85f, 0.2f);
        public Color inactiveColor = new Color(0.8f, 0.8f, 0.8f);

        private void Start()
        {
            if (btnCrafting) btnCrafting.onClick.AddListener(() => ShowPanel(0));
            if (btnShop)     btnShop.onClick.AddListener(() => ShowPanel(1));
            if (btnSkill)    btnSkill.onClick.AddListener(() => ShowPanel(2));
            if (btnCardBuilder) btnCardBuilder.onClick.AddListener(() => ShowPanel(3));
            ShowPanel(0);
        }

        public void ShowPanel(int index)
        {
            if (craftingPanel) craftingPanel.SetActive(index == 0);
            if (shopPanel)     shopPanel.SetActive(index == 1);
            if (skillPanel)    skillPanel.SetActive(index == 2);
            if (cardBuilderPanel) cardBuilderPanel.SetActive(index == 3);

            SetButtonHighlight(btnCrafting, index == 0);
            SetButtonHighlight(btnShop,     index == 1);
            SetButtonHighlight(btnSkill,    index == 2);
            SetButtonHighlight(btnCardBuilder, index == 3);
        }

        private void SetButtonHighlight(Button btn, bool active)
        {
            if (btn == null) return;
            var img = btn.GetComponent<Image>();
            if (img) img.color = active ? activeColor : inactiveColor;
        }
    }
}
