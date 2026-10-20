using UnityEngine;

namespace ApirApir.UI.CardBuilder
{
    [DisallowMultipleComponent]
    public sealed class CardBuilderPresenter : MonoBehaviour
    {
        [SerializeField] private UICardBuilderView view;
        [SerializeField] private CardBuilderPartOptionDefinition[] headOptions =
            System.Array.Empty<CardBuilderPartOptionDefinition>();
        [SerializeField] private CardBuilderPartOptionDefinition[] bodyOptions =
            System.Array.Empty<CardBuilderPartOptionDefinition>();
        [SerializeField] private CardBuilderPartOptionDefinition[] legsOptions =
            System.Array.Empty<CardBuilderPartOptionDefinition>();

        private readonly CardBuilderSelectionState selection = new CardBuilderSelectionState();
        private bool initialized;

        public CardBuilderSelectionState Selection => selection;

        private void Awake()
        {
            if (view == null)
            {
                view = GetComponent<UICardBuilderView>();
            }
        }

        private void OnEnable()
        {
            Bind();
        }

        private void Start()
        {
            Initialize();
        }

        private void OnDisable()
        {
            Unbind();
        }

        public void Configure(
            UICardBuilderView builderView,
            CardBuilderPartOptionDefinition[] heads,
            CardBuilderPartOptionDefinition[] bodies,
            CardBuilderPartOptionDefinition[] legs)
        {
            view = builderView;
            headOptions = heads ?? System.Array.Empty<CardBuilderPartOptionDefinition>();
            bodyOptions = bodies ?? System.Array.Empty<CardBuilderPartOptionDefinition>();
            legsOptions = legs ?? System.Array.Empty<CardBuilderPartOptionDefinition>();
        }

        [ContextMenu("Initialize Card Builder")]
        public void Initialize()
        {
            if (view == null)
            {
                return;
            }

            view.PopulateOptions(headOptions, bodyOptions, legsOptions);
            view.Display(selection);
            initialized = true;
        }

        [ContextMenu("Clear Card")]
        public void ClearCard()
        {
            bool wasBlank = selection.IsBlank;
            selection.Clear();
            if (wasBlank)
            {
                view?.Display(selection);
            }
        }

        public void RemovePart(CardBuilderPartSlot slot)
        {
            selection.Remove(slot);
        }

        public void SelectPart(CardBuilderPartOptionDefinition option)
        {
            selection.Select(option);
        }

        private void Bind()
        {
            selection.Changed -= HandleSelectionChanged;
            selection.Changed += HandleSelectionChanged;

            if (view == null)
            {
                return;
            }

            view.PartSelected -= SelectPart;
            view.PartSelected += SelectPart;
            view.PartRemovalRequested -= RemovePart;
            view.PartRemovalRequested += RemovePart;
            view.ClearRequested -= ClearCard;
            view.ClearRequested += ClearCard;
        }

        private void Unbind()
        {
            selection.Changed -= HandleSelectionChanged;
            if (view == null)
            {
                return;
            }

            view.PartSelected -= SelectPart;
            view.PartRemovalRequested -= RemovePart;
            view.ClearRequested -= ClearCard;
        }

        private void HandleSelectionChanged()
        {
            if (initialized || view != null)
            {
                view?.Display(selection);
            }
        }
    }
}
