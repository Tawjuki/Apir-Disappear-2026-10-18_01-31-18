using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System;

namespace ApirApir.UI
{
    /// <summary>
    /// Reusable skill card. Notifies SkillDetailModal when tapped.
    /// Also scaffolds drag-and-drop via IBeginDragHandler / IDragHandler / IEndDragHandler.
    /// </summary>
    public class SkillCardUI : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [Header("UI References")]
        public Image iconImage;
        public TextMeshProUGUI skillNameLabel;
        public TextMeshProUGUI talinoCostLabel;
        [SerializeField] private SkillDetailModal detailModal;

        [Header("Data")]
        public string skillName;
        public string skillDescription;
        public int talinoCost;
        public string soulCategory;
        public Sprite skillIcon;

        public Action<SkillCardUI> OnCardTapped;
        public Action<SkillCardUI> OnDragStarted;

        private RectTransform _rectTransform;
        private CanvasGroup _canvasGroup;
        private Vector2 _originalPosition;
        private Transform _originalParent;
        private Canvas _rootCanvas;

        private void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
            _canvasGroup   = GetComponent<CanvasGroup>();
            _rootCanvas    = GetComponentInParent<Canvas>();
        }

        public void SetData(string name, string description, int cost, string soul, Sprite icon = null)
        {
            skillName        = name;
            skillDescription = description;
            talinoCost       = cost;
            soulCategory     = soul;
            skillIcon        = icon;
            Refresh();
        }

        public void Refresh()
        {
            if (skillNameLabel)  skillNameLabel.text  = skillName;
            if (talinoCostLabel) talinoCostLabel.text = $"{talinoCost} Talino";
            if (iconImage && skillIcon != null) iconImage.sprite = skillIcon;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            OnCardTapped?.Invoke(this);
            ResolveDetailModal();
            if (detailModal != null) detailModal.Show(this);
        }

        // --- Drag scaffold (ready for slot drop implementation) ---
        public void OnBeginDrag(PointerEventData eventData)
        {
            _originalParent   = transform.parent;
            _originalPosition = _rectTransform.anchoredPosition;
            if (_rootCanvas) transform.SetParent(_rootCanvas.transform, true);
            if (_canvasGroup) { _canvasGroup.blocksRaycasts = false; _canvasGroup.alpha = 0.75f; }
            OnDragStarted?.Invoke(this);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_rootCanvas == null) return;
            _rectTransform.anchoredPosition += eventData.delta / _rootCanvas.scaleFactor;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            transform.SetParent(_originalParent, true);
            _rectTransform.anchoredPosition = _originalPosition;
            if (_canvasGroup) { _canvasGroup.blocksRaycasts = true; _canvasGroup.alpha = 1f; }
        }

        private void ResolveDetailModal()
        {
            if (detailModal == null)
            {
                detailModal = UnityEngine.Object.FindFirstObjectByType<SkillDetailModal>(
                    FindObjectsInactive.Include);
            }
        }
    }
}
