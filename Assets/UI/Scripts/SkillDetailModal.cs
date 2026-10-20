using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ApirApir.UI
{
    /// <summary>
    /// Shows a centered modal with detailed information about a selected skill.
    /// </summary>
    public class SkillDetailModal : MonoBehaviour
    {
        [Header("Modal Root")]
        public GameObject modalRoot;

        [Header("UI References")]
        public Image skillIcon;
        public TextMeshProUGUI skillNameLabel;
        public TextMeshProUGUI descriptionLabel;
        public TextMeshProUGUI talinoCostLabel;
        public Button closeButton;

        private void Awake()
        {
            if (closeButton) closeButton.onClick.AddListener(Hide);
            if (modalRoot)   modalRoot.SetActive(false);
        }

        public void Show(SkillCardUI card)
        {
            if (modalRoot)       modalRoot.SetActive(true);
            if (skillNameLabel)  skillNameLabel.text  = card.skillName;
            if (descriptionLabel) descriptionLabel.text = card.skillDescription;
            if (talinoCostLabel) talinoCostLabel.text  = $"{card.talinoCost} Talino";
            if (skillIcon && card.skillIcon != null)   skillIcon.sprite = card.skillIcon;
        }

        public void Hide()
        {
            if (modalRoot) modalRoot.SetActive(false);
        }
    }
}
