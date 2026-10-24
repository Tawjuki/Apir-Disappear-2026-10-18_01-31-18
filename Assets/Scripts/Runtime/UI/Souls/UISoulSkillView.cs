using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ApirApir.UI
{
    /// <summary>
    /// View component for Souls & Skills matching Sketch 3 and User specifications:
    /// - Left: 4 equipped skill slots (accepts dragged skills).
    /// - Center: 2-column vertically scrollable skill grid + Craft/Unlock button.
    /// - Right: 5 soul selection buttons.
    /// - Centered popup modal for skill details.
    /// </summary>
    public sealed class UISoulSkillView : MonoBehaviour
    {
        [Header("Talino Currency")]
        public TextMeshProUGUI talinoLabel;

        [Header("Left Side - 4 Equipped Slots")]
        public EquippedSkillSlotUI[] equippedSlots = new EquippedSkillSlotUI[4];

        [Header("Center - Skill Catalog")]
        public Transform skillCatalogContent;
        public GameObject skillCardPrefab;
        public Button btnCraftSkill;
        public TextMeshProUGUI craftSkillButtonLabel;

        [Header("Right Side - 5 Soul Buttons")]
        public Button btnKanayunan;
        public Button btnDambana;
        public Button btnKatubigan;
        public Button btnPamayanan;
        public Button btnAliwanan;

        [Header("Centered Detail Modal")]
        public SkillDetailModal detailModal;

        public void UpdateTalino(int talino)
        {
            if (talinoLabel != null)
            {
                talinoLabel.text = $"Talino: {talino}✦";
            }
        }

        public void SetCraftButtonState(string skillName, int cost, bool canUnlock, bool isAlreadyUnlocked)
        {
            if (craftSkillButtonLabel != null)
            {
                if (isAlreadyUnlocked)
                {
                    craftSkillButtonLabel.text = "UNLOCKED";
                }
                else if (string.IsNullOrEmpty(skillName))
                {
                    craftSkillButtonLabel.text = "CRAFT / UNLOCK";
                }
                else
                {
                    craftSkillButtonLabel.text = $"UNLOCK ({cost}✦)";
                }
            }

            if (btnCraftSkill != null)
            {
                bool interactable = canUnlock && !isAlreadyUnlocked;
                btnCraftSkill.interactable = interactable;
                var img = btnCraftSkill.GetComponent<Image>();
                if (img != null)
                {
                    img.color = interactable ? new Color(0.3f, 0.75f, 0.45f) : new Color(0.4f, 0.4f, 0.4f, 0.7f);
                }
            }
        }
    }
}
