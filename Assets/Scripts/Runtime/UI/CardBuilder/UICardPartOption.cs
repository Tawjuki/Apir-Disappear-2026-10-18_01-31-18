using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using ApirApir.UI.DragDrop;

namespace ApirApir.UI.CardBuilder
{
    [DisallowMultipleComponent]
    public sealed class UICardPartOption : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField] private Image icon;
        [SerializeField] private TextMeshProUGUI label;
        [SerializeField] private Color normalColor = new Color(0.2f, 0.22f, 0.3f, 1f);
        [SerializeField] private Color selectedColor = new Color(0.95f, 0.72f, 0.2f, 1f);
        [SerializeField] private UIDragSource dragSource;

        private CardBuilderPartOptionDefinition definition;
        private UnityAction clickHandler;

        public CardBuilderPartOptionDefinition Definition => definition;

        private void Awake()
        {
            if (dragSource == null) dragSource = GetComponent<UIDragSource>();
            if (dragSource == null) dragSource = gameObject.AddComponent<UIDragSource>();
            dragSource.PayloadProvider = GetDragPayload;
        }

        public void BindVisuals(Button optionButton, Image optionIcon, TextMeshProUGUI optionLabel)
        {
            button = optionButton;
            icon = optionIcon;
            label = optionLabel;
        }

        public void Configure(
            CardBuilderPartOptionDefinition option,
            Action<CardBuilderPartOptionDefinition> selected)
        {
            RemoveClickHandler();
            definition = option;

            if (icon != null)
            {
                icon.sprite = option?.Sprite;
                icon.enabled = option?.Sprite != null;
                icon.raycastTarget = false;
            }

            if (label != null)
            {
                label.text = option?.DisplayName ?? string.Empty;
            }

            if (button != null)
            {
                button.interactable = option?.IsSelectable == true;
                clickHandler = () => selected?.Invoke(definition);
                button.onClick.AddListener(clickHandler);
            }

            SetSelected(false);
        }

        public void SetSelected(bool selected)
        {
            if (button != null && button.targetGraphic != null)
            {
                button.targetGraphic.color = selected ? selectedColor : normalColor;
            }
        }

        private void OnDestroy()
        {
            RemoveClickHandler();
        }

        private UIDragPayload GetDragPayload()
        {
            return definition == null || !definition.IsSelectable
                ? null
                : new UIDragPayload(
                    this,
                    definition.Id,
                    definition.DisplayName,
                    definition.Sprite,
                    1,
                    "Part");
        }

        private void RemoveClickHandler()
        {
            if (button != null && clickHandler != null)
            {
                button.onClick.RemoveListener(clickHandler);
            }

            clickHandler = null;
        }
    }
}
