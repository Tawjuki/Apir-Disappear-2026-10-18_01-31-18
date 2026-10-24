using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ApirApir.Battle;
using ApirApir.UI.DragDrop;

namespace ApirApir.UI
{
    /// <summary>
    /// Presenter managing Souls & Skills logic:
    /// Soul switching, 2-column catalog filtering, drag-and-drop equipping to 4 slots,
    /// detail modal popups, and skill unlocking/crafting with Talino currency.
    /// </summary>
    public sealed class SoulSkillUIPresenter : MonoBehaviour
    {
        [SerializeField] private UISoulSkillView view;

        private int playerTalino = 60;
        private string activeSoul = "Kanayunan";
        private SkillCatalog catalog;
        private readonly HashSet<string> unlockedSkills = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<SkillCardUI> spawnedCards = new List<SkillCardUI>();
        private SkillCardUI selectedSkillCard;

        public static readonly string[] SoulNames = { "Kanayunan", "Dambana", "Katubigan", "Pamayanan", "Aliwanan" };

        private void Start()
        {
            if (view == null) view = GetComponent<UISoulSkillView>();
            catalog = DefaultSkillCatalog.Create();

            // Default unlock basic skills
            unlockedSkills.Add("sibat_ng_kawayan");
            unlockedSkills.Add("gumagapang_na_ugat");

            BindEvents();
            SelectSoul("Kanayunan");
            view.UpdateTalino(playerTalino);
            view.SetCraftButtonState(string.Empty, 0, false, false);
        }

        private void BindEvents()
        {
            if (view == null) return;

            if (view.btnKanayunan) view.btnKanayunan.onClick.AddListener(() => SelectSoul("Kanayunan"));
            if (view.btnDambana)   view.btnDambana.onClick.AddListener(() => SelectSoul("Dambana"));
            if (view.btnKatubigan) view.btnKatubigan.onClick.AddListener(() => SelectSoul("Katubigan"));
            if (view.btnPamayanan) view.btnPamayanan.onClick.AddListener(() => SelectSoul("Pamayanan"));
            if (view.btnAliwanan)  view.btnAliwanan.onClick.AddListener(() => SelectSoul("Aliwanan"));

            if (view.btnCraftSkill != null)
            {
                view.btnCraftSkill.onClick.AddListener(HandleUnlockClicked);
            }

            // Setup equipped slots drop targets
            for (int i = 0; i < view.equippedSlots.Length; i++)
            {
                int index = i;
                var slot = view.equippedSlots[i];
                if (slot != null)
                {
                    slot.slotIndex = index + 1;
                    var dropTarget = slot.GetComponent<UIDropTarget>();
                    if (dropTarget == null) dropTarget = slot.gameObject.AddComponent<UIDropTarget>();
                    dropTarget.AcceptedCategory = "Skill";
                    dropTarget.OnPayloadDropped = (payload) => HandleSkillDroppedOnSlot(index, payload);
                }
            }
        }

        public void SelectSoul(string soulName)
        {
            activeSoul = soulName;
            HighlightSoulButtons();
            PopulateSkillsForActiveSoul();
        }

        private void HighlightSoulButtons()
        {
            if (view == null) return;
            SetButtonColor(view.btnKanayunan, activeSoul == "Kanayunan", new Color(0.35f, 0.70f, 0.35f));
            SetButtonColor(view.btnDambana,   activeSoul == "Dambana",   new Color(0.85f, 0.55f, 0.22f));
            SetButtonColor(view.btnKatubigan, activeSoul == "Katubigan", new Color(0.25f, 0.55f, 0.85f));
            SetButtonColor(view.btnPamayanan, activeSoul == "Pamayanan", new Color(0.70f, 0.30f, 0.70f));
            SetButtonColor(view.btnAliwanan,  activeSoul == "Aliwanan",  new Color(0.90f, 0.80f, 0.25f));
        }

        private void SetButtonColor(Button btn, bool isActive, Color soulColor)
        {
            if (btn == null) return;
            var img = btn.GetComponent<Image>();
            if (img != null)
            {
                img.color = isActive ? new Color(1f, 0.9f, 0.3f) : soulColor;
            }
        }

        private void PopulateSkillsForActiveSoul()
        {
            if (view == null || view.skillCatalogContent == null) return;

            // Fetch skills from catalog or fallback data
            var soulSkills = catalog != null ? catalog.GetSoulSkills(activeSoul) : null;

            int count = 0;
            if (soulSkills != null && soulSkills.Count > 0)
            {
                foreach (var sk in soulSkills)
                {
                    SkillCardUI card = GetOrCreateSkillCard(count);
                    int talinoCost = sk.TalinoUnlockCost > 0 ? sk.TalinoUnlockCost : Mathf.Max(10, sk.CooldownExchanges * 6);
                    string desc = !string.IsNullOrEmpty(sk.Description) ? sk.Description : $"Cooldown: {sk.CooldownExchanges} exchanges.";
                    card.SetData(sk.DisplayName, desc, talinoCost, activeSoul);
                    ConfigureSkillCard(card, sk.Id, sk.DisplayName, talinoCost);
                    count++;
                }
            }
            else
            {
                // Fallback sample skills for souls
                string[] names = { $"{activeSoul} Atake", $"{activeSoul} Depensa", $"{activeSoul} Bilis", $"{activeSoul} Suporta" };
                for (int i = 0; i < names.Length; i++)
                {
                    SkillCardUI card = GetOrCreateSkillCard(count);
                    int cost = (i + 1) * 8;
                    card.SetData(names[i], $"Espesyal na galing mula sa {activeSoul}.", cost, activeSoul);
                    ConfigureSkillCard(card, $"{activeSoul}_{i}", names[i], cost);
                    count++;
                }
            }

            // Hide unused
            for (int i = count; i < spawnedCards.Count; i++)
            {
                spawnedCards[i].gameObject.SetActive(false);
            }
        }

        private void ConfigureSkillCard(SkillCardUI card, string skillId, string skillName, int cost)
        {
            card.gameObject.SetActive(true);

            // Wire drag source
            var dragSource = card.GetComponent<UIDragSource>();
            if (dragSource == null) dragSource = card.gameObject.AddComponent<UIDragSource>();
            dragSource.PayloadProvider = () => new UIDragPayload(card, skillId, skillName, card.skillIcon, 1, "Skill");

            // Wire click
            card.OnCardTapped = (tappedCard) =>
            {
                selectedSkillCard = tappedCard;
                bool isUnlocked = unlockedSkills.Contains(skillId);
                bool canUnlock = playerTalino >= cost;
                view.SetCraftButtonState(skillName, cost, canUnlock, isUnlocked);

                if (view.detailModal != null)
                {
                    view.detailModal.Show(tappedCard);
                }
            };
        }

        private SkillCardUI GetOrCreateSkillCard(int index)
        {
            if (index < spawnedCards.Count)
            {
                return spawnedCards[index];
            }

            GameObject instance = null;
            if (view.skillCardPrefab != null)
            {
                instance = Instantiate(view.skillCardPrefab, view.skillCatalogContent);
            }
            else
            {
                instance = new GameObject($"SkillCard_{index}");
                instance.transform.SetParent(view.skillCatalogContent, false);
            }

            var card = instance.GetComponent<SkillCardUI>();
            if (card == null) card = instance.AddComponent<SkillCardUI>();
            spawnedCards.Add(card);
            return card;
        }

        private void HandleSkillDroppedOnSlot(int slotIndex, UIDragPayload payload)
        {
            if (payload == null || slotIndex < 0 || slotIndex >= view.equippedSlots.Length) return;

            var targetSlot = view.equippedSlots[slotIndex];
            if (targetSlot != null)
            {
                targetSlot.EquipSkill(payload.DisplayName, payload.Icon);
                Debug.Log($"[Souls/Skills] Equipped {payload.DisplayName} into Slot {slotIndex + 1}!");
            }
        }

        private void HandleUnlockClicked()
        {
            if (selectedSkillCard == null) return;
            if (playerTalino < selectedSkillCard.talinoCost) return;

            playerTalino -= selectedSkillCard.talinoCost;
            unlockedSkills.Add(selectedSkillCard.skillName);
            view.UpdateTalino(playerTalino);
            view.SetCraftButtonState(selectedSkillCard.skillName, selectedSkillCard.talinoCost, false, true);

            Debug.Log($"[Souls/Skills] Unlocked/Crafted skill {selectedSkillCard.skillName} for {selectedSkillCard.talinoCost} Talino!");
        }
    }
}
