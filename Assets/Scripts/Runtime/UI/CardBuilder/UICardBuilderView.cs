using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ApirApir.Cards;
using ApirApir.UI.DragDrop;

namespace ApirApir.UI.CardBuilder
{
    [DisallowMultipleComponent]
    public sealed class UICardBuilderView : MonoBehaviour
    {
        [Header("Preview")]
        [SerializeField] private UICardPreview preview;

        [Header("Option Lists")]
        [SerializeField] private UICardPartOption optionPrefab;
        [SerializeField] private RectTransform headOptionsContainer;
        [SerializeField] private RectTransform bodyOptionsContainer;
        [SerializeField] private RectTransform legsOptionsContainer;

        [Header("Remove Actions")]
        [SerializeField] private Button removeHeadButton;
        [SerializeField] private Button removeBodyButton;
        [SerializeField] private Button removeLegsButton;
        [SerializeField] private Button clearCardButton;
        [SerializeField] private TextMeshProUGUI selectionSummary;
        [SerializeField] private UIDropTarget cardDropTarget;

        private readonly List<UICardPartOption> generatedOptions = new List<UICardPartOption>();

        public event Action<CardBuilderPartOptionDefinition> PartSelected;
        public event Action<CardBuilderPartSlot> PartRemovalRequested;
        public event Action ClearRequested;
        public event Action<string> PartDropped;

        public UICardPreview Preview => preview;

        public void Configure(
            UICardPreview cardPreview,
            UICardPartOption partOptionPrefab,
            RectTransform headContainer,
            RectTransform bodyContainer,
            RectTransform legsContainer,
            Button removeHead,
            Button removeBody,
            Button removeLegs,
            Button clearCard,
            TextMeshProUGUI summary)
        {
            preview = cardPreview;
            optionPrefab = partOptionPrefab;
            headOptionsContainer = headContainer;
            bodyOptionsContainer = bodyContainer;
            legsOptionsContainer = legsContainer;
            removeHeadButton = removeHead;
            removeBodyButton = removeBody;
            removeLegsButton = removeLegs;
            clearCardButton = clearCard;
            selectionSummary = summary;
        }

        private void OnEnable()
        {
            AddButtonListeners();
            BindCardDropTarget();
        }

        private void OnDisable()
        {
            RemoveButtonListeners();
            if (cardDropTarget != null) cardDropTarget.OnPayloadDropped = null;
        }

        public void PopulateOptions(
            IReadOnlyList<CardBuilderPartOptionDefinition> headOptions,
            IReadOnlyList<CardBuilderPartOptionDefinition> bodyOptions,
            IReadOnlyList<CardBuilderPartOptionDefinition> legsOptions)
        {
            ClearGeneratedOptions();
            CreateOptions(headOptionsContainer, headOptions);
            CreateOptions(bodyOptionsContainer, bodyOptions);
            CreateOptions(legsOptionsContainer, legsOptions);
        }

        public void Display(CardBuilderSelectionState selection)
        {
            if (selection == null)
            {
                return;
            }

            if (preview != null)
            {
                preview.SetParts(selection.Head.Sprite, selection.Body.Sprite, selection.Legs.Sprite);
            }

            for (int i = 0; i < generatedOptions.Count; i++)
            {
                UICardPartOption option = generatedOptions[i];
                CardBuilderPartOptionDefinition definition = option.Definition;
                bool selected = definition != null &&
                                selection.Get(definition.Slot).OptionId == definition.Id &&
                                selection.Get(definition.Slot).Sprite == definition.Sprite;
                option.SetSelected(selected);
            }

            if (selectionSummary != null)
            {
                selectionSummary.text = selection.IsBlank
                    ? "Blank card — choose any Head, Body, or Legs."
                    : BuildSummary(selection);
            }
        }

        public void Display(
            CardBuildSnapshot snapshot,
            Func<string, Sprite> spriteResolver)
        {
            if (snapshot == null) return;
            Sprite head = snapshot.Head.IsEmpty ? null : spriteResolver?.Invoke(snapshot.Head.DefinitionId);
            Sprite body = snapshot.Body.IsEmpty ? null : spriteResolver?.Invoke(snapshot.Body.DefinitionId);
            Sprite legs = snapshot.Legs.IsEmpty ? null : spriteResolver?.Invoke(snapshot.Legs.DefinitionId);
            if (preview != null)
            {
                preview.SetParts(head, body, legs);
                preview.ApplyLayout();
            }

            for (int i = 0; i < generatedOptions.Count; i++)
            {
                CardBuilderPartOptionDefinition definition = generatedOptions[i].Definition;
                bool selected = definition != null &&
                    (definition.Id == snapshot.Head.InstanceId ||
                     definition.Id == snapshot.Body.InstanceId ||
                     definition.Id == snapshot.Legs.InstanceId);
                generatedOptions[i].SetSelected(selected);
            }

            if (selectionSummary != null)
            {
                selectionSummary.text =
                    $"HP {snapshot.Stats.MaximumHitPoints}   ATK {snapshot.Stats.EffectiveAttack}   " +
                    $"ATK +{snapshot.Stats.AttackBonusPercent:0.#}%   " +
                    $"SPD +{snapshot.Stats.SpeedBonusPercent:0.#}%   " +
                    $"CRIT +{snapshot.Stats.CriticalChanceBonusPercent:0.#}%";
            }
        }

        private void CreateOptions(
            RectTransform container,
            IReadOnlyList<CardBuilderPartOptionDefinition> definitions)
        {
            if (container == null || optionPrefab == null || definitions == null)
            {
                return;
            }

            for (int i = 0; i < definitions.Count; i++)
            {
                CardBuilderPartOptionDefinition definition = definitions[i];
                if (definition == null || !definition.IsSelectable)
                {
                    continue;
                }

                UICardPartOption option = Instantiate(optionPrefab, container, false);
                option.gameObject.SetActive(true);
                option.Configure(definition, NotifyPartSelected);
                generatedOptions.Add(option);
            }
        }

        private void ClearGeneratedOptions()
        {
            for (int i = 0; i < generatedOptions.Count; i++)
            {
                if (generatedOptions[i] != null)
                {
                    Destroy(generatedOptions[i].gameObject);
                }
            }

            generatedOptions.Clear();
        }

        private void NotifyPartSelected(CardBuilderPartOptionDefinition definition)
        {
            PartSelected?.Invoke(definition);
        }

        private void AddButtonListeners()
        {
            if (removeHeadButton != null) removeHeadButton.onClick.AddListener(RemoveHead);
            if (removeBodyButton != null) removeBodyButton.onClick.AddListener(RemoveBody);
            if (removeLegsButton != null) removeLegsButton.onClick.AddListener(RemoveLegs);
            if (clearCardButton != null) clearCardButton.onClick.AddListener(RequestClear);
        }

        private void BindCardDropTarget()
        {
            if (cardDropTarget == null && preview != null)
            {
                Image hitArea = preview.GetComponent<Image>();
                if (hitArea == null)
                {
                    hitArea = preview.gameObject.AddComponent<Image>();
                    hitArea.color = new Color(1f, 1f, 1f, 0f);
                }
                hitArea.raycastTarget = true;
                cardDropTarget = preview.GetComponent<UIDropTarget>();
                if (cardDropTarget == null) cardDropTarget = preview.gameObject.AddComponent<UIDropTarget>();
            }

            if (cardDropTarget != null)
            {
                cardDropTarget.AcceptedCategory = "Part";
                cardDropTarget.OnPayloadDropped = payload =>
                {
                    if (payload != null && !string.IsNullOrWhiteSpace(payload.ItemId))
                        PartDropped?.Invoke(payload.ItemId);
                };
            }
        }

        private void RemoveButtonListeners()
        {
            if (removeHeadButton != null) removeHeadButton.onClick.RemoveListener(RemoveHead);
            if (removeBodyButton != null) removeBodyButton.onClick.RemoveListener(RemoveBody);
            if (removeLegsButton != null) removeLegsButton.onClick.RemoveListener(RemoveLegs);
            if (clearCardButton != null) clearCardButton.onClick.RemoveListener(RequestClear);
        }

        private void RemoveHead() => PartRemovalRequested?.Invoke(CardBuilderPartSlot.Head);
        private void RemoveBody() => PartRemovalRequested?.Invoke(CardBuilderPartSlot.Body);
        private void RemoveLegs() => PartRemovalRequested?.Invoke(CardBuilderPartSlot.Legs);
        private void RequestClear() => ClearRequested?.Invoke();

        private static string BuildSummary(CardBuilderSelectionState selection)
        {
            string head = selection.Head.IsEmpty ? "None" : selection.Head.DisplayName;
            string body = selection.Body.IsEmpty ? "None" : selection.Body.DisplayName;
            string legs = selection.Legs.IsEmpty ? "None" : selection.Legs.DisplayName;
            return $"Head: {head}   Body: {body}   Legs: {legs}";
        }
    }
}
