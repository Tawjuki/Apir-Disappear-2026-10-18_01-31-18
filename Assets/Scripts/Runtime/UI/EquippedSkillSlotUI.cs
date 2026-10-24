using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

namespace ApirApir.UI
{
    /// <summary>
    /// Equipped skill slot on the left side of the Skill screen.
    /// Implements IDropHandler to accept dropped SkillCardUI cards.
    /// </summary>
    public class EquippedSkillSlotUI : MonoBehaviour, IDropHandler
    {
        [Header("UI References")]
        public Image slotIcon;
        public TextMeshProUGUI slotLabel;
        public TextMeshProUGUI skillNameLabel;

        [Header("Slot Data")]
        public int slotIndex = 1;
        public string equippedSkillName = "";

        public void OnDrop(PointerEventData eventData)
        {
            var draggedSkill = eventData.pointerDrag != null ? eventData.pointerDrag.GetComponent<SkillCardUI>() : null;
            if (draggedSkill != null)
            {
                EquipSkill(draggedSkill.skillName, draggedSkill.skillIcon);
                Debug.Log($"[SkillSlot {slotIndex}] Equipped: {draggedSkill.skillName}");
            }
        }

        public void EquipSkill(string skillName, Sprite icon = null)
        {
            equippedSkillName = skillName;
            if (skillNameLabel != null)
            {
                skillNameLabel.text = skillName;
                skillNameLabel.gameObject.SetActive(true);
            }
            if (slotIcon != null && icon != null)
            {
                slotIcon.sprite = icon;
                slotIcon.color = Color.white;
            }
        }

        public void ClearSlot()
        {
            equippedSkillName = "";
            if (skillNameLabel != null)
            {
                skillNameLabel.text = "";
                skillNameLabel.gameObject.SetActive(false);
            }
        }
    }
}
