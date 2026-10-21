using System;
using UnityEngine;

namespace ApirApir.UI.CardBuilder
{
    public enum CardBuilderPartSlot : byte
    {
        Head,
        Body,
        Legs
    }

    [Serializable]
    public sealed class CardBuilderPartOptionDefinition
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private CardBuilderPartSlot slot;
        [SerializeField] private Sprite sprite;
        [SerializeField] private string partDefinitionId = string.Empty;

        public CardBuilderPartOptionDefinition(
            string id,
            string displayName,
            CardBuilderPartSlot slot,
            Sprite sprite)
        {
            this.id = id ?? string.Empty;
            this.displayName = displayName ?? string.Empty;
            this.slot = slot;
            this.sprite = sprite;
            partDefinitionId = this.id;
        }

        public CardBuilderPartOptionDefinition(
            string instanceId,
            string definitionId,
            string displayName,
            CardBuilderPartSlot slot,
            Sprite sprite)
        {
            id = instanceId ?? string.Empty;
            partDefinitionId = definitionId ?? string.Empty;
            this.displayName = displayName ?? string.Empty;
            this.slot = slot;
            this.sprite = sprite;
        }

        public string Id => id;
        public string DisplayName => displayName;
        public CardBuilderPartSlot Slot => slot;
        public Sprite Sprite => sprite;
        public string PartDefinitionId => string.IsNullOrEmpty(partDefinitionId) ? id : partDefinitionId;
        public bool IsSelectable => !string.IsNullOrWhiteSpace(id);
    }

    public readonly struct CardBuilderPartSelection
    {
        public CardBuilderPartSelection(string optionId, string displayName, Sprite sprite)
        {
            OptionId = optionId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Sprite = sprite;
        }

        public string OptionId { get; }
        public string DisplayName { get; }
        public Sprite Sprite { get; }
        public bool IsEmpty => string.IsNullOrEmpty(OptionId);
    }

    /// <summary>
    /// Session-only card-builder selection data. It deliberately has no inventory,
    /// crafting, combat, persistence, or networking responsibilities.
    /// </summary>
    public sealed class CardBuilderSelectionState
    {
        public event Action Changed;

        public CardBuilderPartSelection Head { get; private set; }
        public CardBuilderPartSelection Body { get; private set; }
        public CardBuilderPartSelection Legs { get; private set; }
        public bool IsBlank => Head.IsEmpty && Body.IsEmpty && Legs.IsEmpty;

        public CardBuilderPartSelection Get(CardBuilderPartSlot slot)
        {
            return slot switch
            {
                CardBuilderPartSlot.Head => Head,
                CardBuilderPartSlot.Body => Body,
                CardBuilderPartSlot.Legs => Legs,
                _ => default
            };
        }

        public void Select(CardBuilderPartOptionDefinition option)
        {
            if (option == null || !option.IsSelectable)
            {
                return;
            }

            Set(option.Slot, new CardBuilderPartSelection(option.Id, option.DisplayName, option.Sprite));
        }

        public void Remove(CardBuilderPartSlot slot)
        {
            Set(slot, default);
        }

        public void Clear()
        {
            if (IsBlank)
            {
                return;
            }

            Head = default;
            Body = default;
            Legs = default;
            Changed?.Invoke();
        }

        private void Set(CardBuilderPartSlot slot, CardBuilderPartSelection selection)
        {
            CardBuilderPartSelection current = Get(slot);
            if (current.OptionId == selection.OptionId && current.Sprite == selection.Sprite)
            {
                return;
            }

            switch (slot)
            {
                case CardBuilderPartSlot.Head:
                    Head = selection;
                    break;
                case CardBuilderPartSlot.Body:
                    Body = selection;
                    break;
                case CardBuilderPartSlot.Legs:
                    Legs = selection;
                    break;
                default:
                    return;
            }

            Changed?.Invoke();
        }
    }
}
