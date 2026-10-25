using System;
using System.Collections.Generic;
using ApirApir.Battle;
using ApirApir.Cards;
using ApirApir.Crafting;
using ApirApir.UI.Inventory;
using UnityEngine;

namespace ApirApir.UI.CardBuilder
{
    [DisallowMultipleComponent]
    public sealed class CardBuilderPresenter : MonoBehaviour
    {
        [SerializeField] private UICardBuilderView view;
        [SerializeField] private bool useLocalDefenseDemo = true;
        [SerializeField] private CardBuilderPartOptionDefinition[] headOptions =
            Array.Empty<CardBuilderPartOptionDefinition>();
        [SerializeField] private CardBuilderPartOptionDefinition[] bodyOptions =
            Array.Empty<CardBuilderPartOptionDefinition>();
        [SerializeField] private CardBuilderPartOptionDefinition[] legsOptions =
            Array.Empty<CardBuilderPartOptionDefinition>();
        [SerializeField] private UIItemSpriteBinding[] partSprites = Array.Empty<UIItemSpriteBinding>();

        private readonly CardBuilderSelectionState selection = new CardBuilderSelectionState();
        private readonly Dictionary<string, Sprite> spriteByDefinition =
            new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private PlayerCraftingInventory inventory;
        private AuthoritativeCardAssemblyService assembly;
        private string playerId = string.Empty;
        private string cardId = string.Empty;
        private CardBuildSnapshot snapshot;
        private bool initialized;
        private bool bound;

        public CardBuilderSelectionState Selection => selection;

        private void Awake()
        {
            if (view == null) view = GetComponent<UICardBuilderView>();
            BuildSpriteLookup();
        }

        private void OnEnable()
        {
            Bind();
            SubscribeRuntime();

            // Inventory changes can occur while this panel is inactive (for example, when
            // the player returns to Crafting and creates another part). Rebuild the view
            // whenever an already-initialized Card Builder is reopened so those parts are
            // not omitted simply because the presenter was unsubscribed at craft time.
            if (initialized && inventory != null && assembly != null)
            {
                RefreshRuntimeState();
            }
        }

        private void Start()
        {
            if (useLocalDefenseDemo && assembly == null)
            {
                PreparationDemoSession.EnsureInitialized();
                ConfigureRuntime(
                    PreparationDemoSession.Inventory,
                    PreparationDemoSession.CardAssembly,
                    PreparationDemoSession.PlayerId,
                    PreparationDemoSession.CardId);
            }
            else
            {
                Initialize();
            }
        }

        private void OnDisable()
        {
            Unbind();
            UnsubscribeRuntime();
        }

        public void Configure(
            UICardBuilderView builderView,
            CardBuilderPartOptionDefinition[] heads,
            CardBuilderPartOptionDefinition[] bodies,
            CardBuilderPartOptionDefinition[] legs)
        {
            view = builderView;
            headOptions = heads ?? Array.Empty<CardBuilderPartOptionDefinition>();
            bodyOptions = bodies ?? Array.Empty<CardBuilderPartOptionDefinition>();
            legsOptions = legs ?? Array.Empty<CardBuilderPartOptionDefinition>();
            BuildSpriteLookup();
        }

        public void ConfigureRuntime(
            PlayerCraftingInventory playerInventory,
            AuthoritativeCardAssemblyService assemblyService,
            string authenticatedPlayerId,
            string assembledCardId)
        {
            UnsubscribeRuntime();
            inventory = playerInventory ?? throw new ArgumentNullException(nameof(playerInventory));
            assembly = assemblyService ?? throw new ArgumentNullException(nameof(assemblyService));
            playerId = authenticatedPlayerId == null ? string.Empty : authenticatedPlayerId.Trim();
            cardId = assembledCardId == null ? string.Empty : assembledCardId.Trim();
            if (playerId.Length == 0 || cardId.Length == 0 ||
                !string.Equals(playerId, inventory.PlayerId, StringComparison.Ordinal))
            {
                throw new ArgumentException("Card Builder identity does not match its player inventory.");
            }

            if (!assembly.TryGetCardBuild(playerId, cardId, out snapshot))
                snapshot = assembly.InitializeCard(playerId, cardId).Snapshot;
            SubscribeRuntime();
            RefreshOwnedParts();
            DisplaySnapshot();
            initialized = true;
        }

        public void ConfigureSpriteBindings(UIItemSpriteBinding[] bindings)
        {
            partSprites = bindings ?? Array.Empty<UIItemSpriteBinding>();
            BuildSpriteLookup();
            if (snapshot != null) DisplaySnapshot();
        }

        [ContextMenu("Initialize Card Builder")]
        public void Initialize()
        {
            if (view == null) return;
            if (assembly != null)
            {
                RefreshOwnedParts();
                DisplaySnapshot();
            }
            else
            {
                view.PopulateOptions(headOptions, bodyOptions, legsOptions);
                view.Display(selection);
            }
            initialized = true;
        }

        [ContextMenu("Clear Card")]
        public void ClearCard()
        {
            if (assembly == null)
            {
                bool wasBlank = selection.IsBlank;
                selection.Clear();
                if (wasBlank) view?.Display(selection);
                return;
            }

            UnequipIfPresent(PartSlot.Head);
            UnequipIfPresent(PartSlot.Body);
            UnequipIfPresent(PartSlot.Legs);
        }

        public void RemovePart(CardBuilderPartSlot slot)
        {
            if (assembly == null)
            {
                selection.Remove(slot);
                return;
            }

            UnequipIfPresent(ToPartSlot(slot));
        }

        public void SelectPart(CardBuilderPartOptionDefinition option)
        {
            if (option == null) return;
            if (assembly == null)
            {
                selection.Select(option);
                return;
            }

            EquipOwnedPart(option.Id);
        }

        private void EquipOwnedPart(string instanceId)
        {
            if (assembly == null || inventory == null || snapshot == null ||
                !inventory.TryGetOwnedPart(instanceId, out OwnedCardPart part) || part == null)
            {
                return;
            }

            CardAssemblyOperationResult result = assembly.EquipPart(
                playerId, cardId, part.Definition.Slot, part.InstanceId, snapshot.Revision,
                Guid.NewGuid().ToString("N"));
            if (!result.Succeeded)
            {
                Debug.LogWarning($"[Card Builder] Equip rejected: {result.Failure}");
                if (result.Snapshot != null) snapshot = result.Snapshot;
                DisplaySnapshot();
                return;
            }

            snapshot = result.Snapshot;
            RefreshOwnedParts();
            DisplaySnapshot();
        }

        private void UnequipIfPresent(PartSlot slot)
        {
            if (assembly == null || snapshot == null || !SnapshotOccupies(snapshot, slot)) return;
            CardAssemblyOperationResult result = assembly.UnequipPart(
                playerId, cardId, slot, snapshot.Revision, Guid.NewGuid().ToString("N"));
            if (result.Snapshot != null) snapshot = result.Snapshot;
            if (!result.Succeeded)
                Debug.LogWarning($"[Card Builder] Unequip rejected: {result.Failure}");
            RefreshOwnedParts();
            DisplaySnapshot();
        }

        private void Bind()
        {
            if (bound) return;
            selection.Changed += HandleSelectionChanged;
            if (view != null)
            {
                view.PartSelected += SelectPart;
                view.PartRemovalRequested += RemovePart;
                view.ClearRequested += ClearCard;
                view.PartDropped += EquipOwnedPart;
            }
            bound = true;
        }

        private void Unbind()
        {
            if (!bound) return;
            selection.Changed -= HandleSelectionChanged;
            if (view != null)
            {
                view.PartSelected -= SelectPart;
                view.PartRemovalRequested -= RemovePart;
                view.ClearRequested -= ClearCard;
                view.PartDropped -= EquipOwnedPart;
            }
            bound = false;
        }

        private void SubscribeRuntime()
        {
            if (!isActiveAndEnabled) return;
            if (inventory != null)
            {
                inventory.Changed -= HandleInventoryChanged;
                inventory.Changed += HandleInventoryChanged;
            }
            if (assembly != null)
            {
                assembly.CardConfigurationChanged -= HandleCardChanged;
                assembly.CardConfigurationChanged += HandleCardChanged;
            }
        }

        private void UnsubscribeRuntime()
        {
            if (inventory != null) inventory.Changed -= HandleInventoryChanged;
            if (assembly != null) assembly.CardConfigurationChanged -= HandleCardChanged;
        }

        private void HandleInventoryChanged()
        {
            RefreshOwnedParts();
            DisplaySnapshot();
        }

        private void HandleCardChanged(CardBuildSnapshot changed)
        {
            if (changed == null || !string.Equals(changed.OwnerPlayerId, playerId, StringComparison.Ordinal) ||
                !string.Equals(changed.CardId, cardId, StringComparison.Ordinal)) return;
            snapshot = changed;
            RefreshOwnedParts();
            DisplaySnapshot();
        }

        private void RefreshOwnedParts()
        {
            if (view == null || inventory == null) return;
            var heads = new List<CardBuilderPartOptionDefinition>();
            var bodies = new List<CardBuilderPartOptionDefinition>();
            var legs = new List<CardBuilderPartOptionDefinition>();
            IReadOnlyList<OwnedCardPart> parts = inventory.GetOwnedParts();
            for (int i = 0; i < parts.Count; i++)
            {
                OwnedCardPart part = parts[i];
                if (part?.Definition == null) continue;
                var option = new CardBuilderPartOptionDefinition(
                    part.InstanceId,
                    part.Definition.Id,
                    part.Definition.DisplayName,
                    ToBuilderSlot(part.Definition.Slot),
                    ResolvePartSprite(part.Definition));
                switch (part.Definition.Slot)
                {
                    case PartSlot.Head: heads.Add(option); break;
                    case PartSlot.Body: bodies.Add(option); break;
                    case PartSlot.Legs: legs.Add(option); break;
                }
            }
            view.PopulateOptions(heads, bodies, legs);
        }

        private void RefreshRuntimeState()
        {
            if (assembly.TryGetCardBuild(playerId, cardId, out CardBuildSnapshot latest))
            {
                snapshot = latest;
            }

            RefreshOwnedParts();
            DisplaySnapshot();
        }

        private void DisplaySnapshot()
        {
            if (snapshot != null) view?.Display(snapshot, ResolveSprite);
        }

        private void HandleSelectionChanged()
        {
            if (assembly == null && (initialized || view != null)) view?.Display(selection);
        }

        private void BuildSpriteLookup()
        {
            spriteByDefinition.Clear();
            for (int i = 0; i < partSprites.Length; i++)
            {
                UIItemSpriteBinding binding = partSprites[i];
                if (binding != null && !string.IsNullOrWhiteSpace(binding.ItemId) && binding.Sprite != null)
                    spriteByDefinition[binding.ItemId.Trim()] = binding.Sprite;
            }
            AddLegacySprites(headOptions);
            AddLegacySprites(bodyOptions);
            AddLegacySprites(legsOptions);
        }

        private void AddLegacySprites(IReadOnlyList<CardBuilderPartOptionDefinition> options)
        {
            if (options == null) return;
            for (int i = 0; i < options.Count; i++)
            {
                CardBuilderPartOptionDefinition option = options[i];
                if (option == null || option.Sprite == null) continue;
                string id = option.Id;
                string prefix = id.StartsWith("magsasaka_", StringComparison.Ordinal) ? "part_common_" :
                    id.StartsWith("abogado_", StringComparison.Ordinal) ? "part_rare_" :
                    id.StartsWith("pari_", StringComparison.Ordinal) ? "part_rare_" : string.Empty;
                if (prefix.Length > 0) spriteByDefinition[prefix + id] = option.Sprite;
                if (!string.IsNullOrWhiteSpace(option.PartDefinitionId))
                    spriteByDefinition[option.PartDefinitionId] = option.Sprite;
            }
        }

        private Sprite ResolveSprite(string definitionId)
        {
            string canonicalId = DefaultPartDefinitionCatalog.NormalizeId(definitionId);
            return canonicalId.Length > 0 && spriteByDefinition.TryGetValue(canonicalId, out Sprite sprite)
                ? sprite
                : null;
        }

        private Sprite ResolvePartSprite(CraftablePartDefinition definition)
        {
            if (definition == null) return null;
            Sprite sprite = ResolveSprite(definition.VisualAssetId);
            return sprite != null ? sprite : ResolveSprite(definition.Id);
        }

        private static CardBuilderPartSlot ToBuilderSlot(PartSlot slot) => slot switch
        {
            PartSlot.Head => CardBuilderPartSlot.Head,
            PartSlot.Body => CardBuilderPartSlot.Body,
            _ => CardBuilderPartSlot.Legs
        };

        private static PartSlot ToPartSlot(CardBuilderPartSlot slot) => slot switch
        {
            CardBuilderPartSlot.Head => PartSlot.Head,
            CardBuilderPartSlot.Body => PartSlot.Body,
            _ => PartSlot.Legs
        };

        private static bool SnapshotOccupies(CardBuildSnapshot value, PartSlot slot)
        {
            PartEquipmentRegion region = slot == PartSlot.Head
                ? PartEquipmentRegion.Head
                : slot == PartSlot.Body ? PartEquipmentRegion.Body : PartEquipmentRegion.Legs;
            return (!value.Head.IsEmpty && (value.Head.OccupiedRegions & region) != 0) ||
                   (!value.Body.IsEmpty && (value.Body.OccupiedRegions & region) != 0) ||
                   (!value.Legs.IsEmpty && (value.Legs.OccupiedRegions & region) != 0);
        }
    }
}
