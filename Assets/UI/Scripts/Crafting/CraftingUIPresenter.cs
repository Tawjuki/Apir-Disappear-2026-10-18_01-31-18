using System;
using System.Collections.Generic;
using ApirApir.Cards;
using ApirApir.Crafting;
using ApirApir.UI.DragDrop;
using ApirApir.UI.Inventory;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ApirApir.UI
{
    /// <summary>
    /// Connects the notebook HUD to authoritative crafting. Dragging only reserves
    /// materials locally; inventory is mutated atomically when Craft is confirmed.
    /// </summary>
    public sealed class CraftingUIPresenter : MonoBehaviour
    {
        [SerializeField] private UICraftingView view;
        [SerializeField] private bool useLocalDefenseDemo = true;
        [SerializeField] private UIItemSpriteBinding[] itemSprites = Array.Empty<UIItemSpriteBinding>();

        [Header("Preparation Flow")]
        [SerializeField] private GameObject cardBuilderPanelPrefab;
        [SerializeField] private Button openCardBuilderButton;

        private readonly string[] slotItemIds = new string[3];
        private readonly int[] slotQuantities = new int[3];
        private readonly Dictionary<string, Sprite> sprites =
            new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private CraftingCatalog catalog;
        private CraftingController controller;
        private PlayerCraftingInventory inventory;
        private string playerId = string.Empty;
        private string matchId = string.Empty;
        private CraftingRecipe matchedRecipe;
        private bool bound;
        private GameObject cardBuilderInstance;
        private Button closeCardBuilderButton;

        public Action<CraftingRecipe> OnCraftExecuted { get; set; }

        private void Awake()
        {
            if (view == null) view = GetComponent<UICraftingView>();
            if (view == null) view = gameObject.AddComponent<UICraftingView>();
            view.AutoConfigureFromHierarchy();
            BuildSpriteLookup();
        }

        private void OnEnable()
        {
            BindEvents();
            SubscribeInventory();
        }

        private void Start()
        {
            if (useLocalDefenseDemo && controller == null)
            {
                PreparationDemoSession.EnsureInitialized();
                Configure(
                    PreparationDemoSession.Catalog,
                    PreparationDemoSession.CraftingController,
                    PreparationDemoSession.Inventory,
                    PreparationDemoSession.PlayerId,
                    PreparationDemoSession.MatchId);
            }

            RefreshMaterialInventory();
            EvaluateRecipe();
            EnsureCardBuilderNavigation();
        }

        private void OnDisable()
        {
            UnbindEvents();
            UnsubscribeInventory();
        }

        private void OnDestroy()
        {
            if (openCardBuilderButton != null)
                openCardBuilderButton.onClick.RemoveListener(OpenCardBuilder);
            if (closeCardBuilderButton != null)
                closeCardBuilderButton.onClick.RemoveListener(CloseCardBuilder);
        }

        public void Configure(
            CraftingCatalog craftingCatalog,
            CraftingController craftingController,
            PlayerCraftingInventory playerInventory,
            string authenticatedPlayerId,
            string activeMatchId)
        {
            UnsubscribeInventory();
            catalog = craftingCatalog ?? throw new ArgumentNullException(nameof(craftingCatalog));
            controller = craftingController ?? throw new ArgumentNullException(nameof(craftingController));
            inventory = playerInventory ?? throw new ArgumentNullException(nameof(playerInventory));
            playerId = authenticatedPlayerId == null ? string.Empty : authenticatedPlayerId.Trim();
            matchId = activeMatchId == null ? string.Empty : activeMatchId.Trim();
            if (playerId.Length == 0 || matchId.Length == 0 ||
                !string.Equals(inventory.PlayerId, playerId, StringComparison.Ordinal))
            {
                throw new ArgumentException("Crafting UI identity does not match its player inventory.");
            }

            SubscribeInventory();
            RefreshMaterialInventory();
            EvaluateRecipe();
        }

        private void BindEvents()
        {
            if (bound || view == null) return;
            int count = Math.Min(slotItemIds.Length, view.ingredientSlots.Length);
            for (int i = 0; i < count; i++)
            {
                UICraftingIngredientSlot slot = view.ingredientSlots[i];
                if (slot == null) continue;
                slot.slotIndex = i;
                slot.OnIngredientAssigned = HandleIngredientAssigned;
                slot.OnIngredientRemoved = HandleIngredientRemoved;
            }

            if (view.materialsDropArea != null)
            {
                view.materialsDropArea.AcceptedCategory = "Material";
                view.materialsDropArea.OnPayloadDropped = HandleDroppedBackToInventory;
            }

            if (view.btnCraft != null) view.btnCraft.onClick.AddListener(HandleCraftClicked);
            bound = true;
        }

        private void UnbindEvents()
        {
            if (!bound || view == null) return;
            if (view.btnCraft != null) view.btnCraft.onClick.RemoveListener(HandleCraftClicked);
            if (view.materialsDropArea != null) view.materialsDropArea.OnPayloadDropped = null;
            for (int i = 0; i < view.ingredientSlots.Length; i++)
            {
                if (view.ingredientSlots[i] == null) continue;
                view.ingredientSlots[i].OnIngredientAssigned = null;
                view.ingredientSlots[i].OnIngredientRemoved = null;
            }

            bound = false;
        }

        private void SubscribeInventory()
        {
            if (!isActiveAndEnabled || inventory == null) return;
            inventory.Changed -= HandleInventoryChanged;
            inventory.Changed += HandleInventoryChanged;
        }

        private void UnsubscribeInventory()
        {
            if (inventory != null) inventory.Changed -= HandleInventoryChanged;
        }

        private void HandleInventoryChanged()
        {
            RefreshMaterialInventory();
            EvaluateRecipe();
        }

        private void HandleIngredientAssigned(int slotIndex, UIDragPayload payload)
        {
            if (!IsValidSlot(slotIndex) || payload == null || inventory == null || catalog == null ||
                !catalog.TryGetMaterial(payload.ItemId, out MaterialDefinition material))
            {
                return;
            }

            if (payload.SourceContext is UICraftingIngredientSlot sourceSlot)
            {
                int sourceIndex = sourceSlot.slotIndex;
                if (!IsValidSlot(sourceIndex) || sourceIndex == slotIndex) return;
                string movingId = slotItemIds[sourceIndex];
                int movingQuantity = slotQuantities[sourceIndex];
                slotItemIds[sourceIndex] = null;
                slotQuantities[sourceIndex] = 0;
                sourceSlot.Clear();
                SetSlot(slotIndex, movingId, material.DisplayName, payload.Icon, movingQuantity);
            }
            else
            {
                int available = inventory.GetMaterialQuantity(material.Id) - GetReservedQuantity(material.Id);
                if (available <= 0) return;
                int quantity = string.Equals(slotItemIds[slotIndex], material.Id, StringComparison.Ordinal)
                    ? slotQuantities[slotIndex] + 1
                    : 1;
                SetSlot(slotIndex, material.Id, material.DisplayName, ResolveSprite(material.Id), quantity);
            }

            RefreshMaterialInventory();
            EvaluateRecipe();
        }

        private void SetSlot(int index, string itemId, string displayName, Sprite sprite, int quantity)
        {
            slotItemIds[index] = itemId;
            slotQuantities[index] = Math.Max(1, quantity);
            view.ingredientSlots[index]?.AssignIngredient(
                itemId, displayName, sprite, slotQuantities[index]);
        }

        private void HandleIngredientRemoved(int slotIndex, string removedItemId, int removedQuantity)
        {
            if (!IsValidSlot(slotIndex)) return;
            slotItemIds[slotIndex] = null;
            slotQuantities[slotIndex] = 0;
            RefreshMaterialInventory();
            EvaluateRecipe();
        }

        private void HandleDroppedBackToInventory(UIDragPayload payload)
        {
            if (!(payload?.SourceContext is UICraftingIngredientSlot sourceSlot) ||
                !IsValidSlot(sourceSlot.slotIndex)) return;
            int index = sourceSlot.slotIndex;
            sourceSlot.Clear();
            slotItemIds[index] = null;
            slotQuantities[index] = 0;
            RefreshMaterialInventory();
            EvaluateRecipe();
        }

        private void EvaluateRecipe()
        {
            matchedRecipe = null;
            if (catalog == null || view == null) return;
            var present = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < slotItemIds.Length; i++)
            {
                if (string.IsNullOrEmpty(slotItemIds[i]) || slotQuantities[i] <= 0) continue;
                present.TryGetValue(slotItemIds[i], out int quantity);
                present[slotItemIds[i]] = quantity + slotQuantities[i];
            }

            foreach (CraftingRecipe recipe in catalog.Recipes.Values)
            {
                if (recipe.IsEnabled && IsExactMatch(recipe, present))
                {
                    matchedRecipe = recipe;
                    break;
                }
            }

            if (matchedRecipe == null)
            {
                view.SetPreview(string.Empty, string.Empty, null, false);
                return;
            }

            CraftablePartDefinition part = matchedRecipe.OutputPart;
            view.SetPreview(part.DisplayName, BuildPartDetails(part),
                ResolveSprite(part.VisualAssetId), true);
        }

        private void HandleCraftClicked()
        {
            if (matchedRecipe == null || controller == null || inventory == null) return;
            CraftingResult result = controller.Craft(
                new CraftingRequest(matchId, matchedRecipe.Id, Guid.NewGuid().ToString("N")), playerId);
            if (!result.Succeeded)
            {
                Debug.LogWarning($"[Crafting] {matchedRecipe.Id} rejected: {result.Failure}");
                RefreshMaterialInventory();
                EvaluateRecipe();
                return;
            }

            CraftingRecipe completedRecipe = matchedRecipe;
            ClearIngredientSlots();
            RefreshMaterialInventory();
            EvaluateRecipe();
            OnCraftExecuted?.Invoke(completedRecipe);
        }

        private void ClearIngredientSlots()
        {
            for (int i = 0; i < slotItemIds.Length; i++)
            {
                slotItemIds[i] = null;
                slotQuantities[i] = 0;
                if (view != null && i < view.ingredientSlots.Length)
                    view.ingredientSlots[i]?.Clear();
            }
        }

        private void RefreshMaterialInventory()
        {
            if (view == null || view.materialsGrid == null || catalog == null || inventory == null) return;
            var items = new List<UIInventoryItemData>(catalog.Materials.Count);
            foreach (MaterialDefinition material in catalog.Materials.Values)
            {
                items.Add(new UIInventoryItemData(
                    material.Id, material.DisplayName,
                    Math.Max(0, inventory.GetMaterialQuantity(material.Id) - GetReservedQuantity(material.Id)),
                    -1, ResolveSprite(material.Id), "Material", material.Rarity.ToString()));
            }

            view.materialsGrid.Populate(items);
        }

        private int GetReservedQuantity(string itemId)
        {
            int total = 0;
            for (int i = 0; i < slotItemIds.Length; i++)
                if (string.Equals(slotItemIds[i], itemId, StringComparison.Ordinal))
                    total += slotQuantities[i];
            return total;
        }

        private static bool IsExactMatch(CraftingRecipe recipe, IReadOnlyDictionary<string, int> present)
        {
            if (present.Count != recipe.Requirements.Count) return false;
            for (int i = 0; i < recipe.Requirements.Count; i++)
            {
                MaterialRequirement requirement = recipe.Requirements[i];
                if (!present.TryGetValue(requirement.MaterialId, out int quantity) ||
                    quantity != requirement.Quantity) return false;
            }

            return true;
        }

        private static string BuildPartDetails(CraftablePartDefinition part)
        {
            string category = part.IsHybrid ? "Body & Legs Hybrid" : part.Slot.ToString();
            return $"{part.Rarity} {category}\n" +
                   $"HP +{part.HitPointContribution} / +{part.HitPointBonusPercent:0.#}%  " +
                   $"ATK +{part.AttackBonusPercent:0.#}%\n" +
                   $"SPD +{part.SpeedBonusPercent:0.#}%  " +
                   $"CRIT +{part.CriticalChanceBonusPercent:0.#}%  " +
                   $"DODGE +{part.BattleDefinition.DodgeChanceBonusPercent:0.#}%\n" +
                   $"Maintenance {part.InitialMaintenance}" +
                   (part.PassiveAbilityReferences.Count == 0
                       ? string.Empty
                       : "\nPassives: " + string.Join(", ", part.PassiveAbilityReferences));
        }

        private void BuildSpriteLookup()
        {
            sprites.Clear();
            for (int i = 0; i < itemSprites.Length; i++)
            {
                UIItemSpriteBinding binding = itemSprites[i];
                if (binding != null && !string.IsNullOrWhiteSpace(binding.ItemId) && binding.Sprite != null)
                    sprites[binding.ItemId.Trim()] = binding.Sprite;
            }
        }

        private Sprite ResolveSprite(string itemId) =>
            itemId != null && sprites.TryGetValue(itemId, out Sprite sprite) ? sprite : null;

        private bool IsValidSlot(int index) => view != null && index >= 0 &&
            index < slotItemIds.Length && index < view.ingredientSlots.Length;

        private void EnsureCardBuilderNavigation()
        {
            if (view == null || cardBuilderPanelPrefab == null) return;

            Transform canvas = GetComponentInParent<Canvas>()?.transform;
            if (canvas == null) return;

            ApirApir.UI.CardBuilder.CardBuilderPresenter[] existing =
                canvas.GetComponentsInChildren<ApirApir.UI.CardBuilder.CardBuilderPresenter>(true);
            if (existing.Length > 0)
            {
                cardBuilderInstance = existing[0].gameObject;
            }
            else
            {
                cardBuilderInstance = Instantiate(cardBuilderPanelPrefab, canvas, false);
                cardBuilderInstance.name = cardBuilderPanelPrefab.name;
            }

            ApirApir.UI.CardBuilder.CardBuilderPresenter builderPresenter =
                cardBuilderInstance.GetComponent<ApirApir.UI.CardBuilder.CardBuilderPresenter>();
            if (builderPresenter != null) builderPresenter.ConfigureSpriteBindings(itemSprites);

            if (openCardBuilderButton == null)
            {
                openCardBuilderButton = view.CreateNavigationButton(
                    "CardAssemblyButton", "CARD ASSEMBLY",
                    new Vector2(0.76f, 0.07f), new Vector2(0.94f, 0.14f));
            }

            if (closeCardBuilderButton == null)
            {
                closeCardBuilderButton = CreateOverlayButton(
                    cardBuilderInstance.transform as RectTransform,
                    "BackToCraftingButton", "BACK TO CRAFTING");
            }

            if (openCardBuilderButton != null)
            {
                openCardBuilderButton.onClick.RemoveListener(OpenCardBuilder);
                openCardBuilderButton.onClick.AddListener(OpenCardBuilder);
            }
            if (closeCardBuilderButton != null)
            {
                closeCardBuilderButton.onClick.RemoveListener(CloseCardBuilder);
                closeCardBuilderButton.onClick.AddListener(CloseCardBuilder);
            }

            cardBuilderInstance.SetActive(false);
        }

        private void OpenCardBuilder()
        {
            if (cardBuilderInstance == null) return;
            cardBuilderInstance.SetActive(true);
            gameObject.SetActive(false);
        }

        private void CloseCardBuilder()
        {
            gameObject.SetActive(true);
            if (cardBuilderInstance != null) cardBuilderInstance.SetActive(false);
        }

        private static Button CreateOverlayButton(RectTransform parent, string objectName, string label)
        {
            if (parent == null) return null;
            Transform existing = parent.Find(objectName);
            if (existing != null && existing.TryGetComponent(out Button found)) return found;

            var buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = (RectTransform)buttonObject.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.76f, 0.91f);
            rect.anchorMax = new Vector2(0.96f, 0.98f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            buttonObject.GetComponent<Image>().color = new Color(0.34f, 0.24f, 0.14f, 0.95f);

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            var labelRect = (RectTransform)labelObject.transform;
            labelRect.SetParent(rect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(8f, 4f);
            labelRect.offsetMax = new Vector2(-8f, -4f);
            var text = labelObject.GetComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = 22f;
            text.enableAutoSizing = true;
            text.fontSizeMin = 11f;
            text.fontSizeMax = 24f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;
            return buttonObject.GetComponent<Button>();
        }
    }

    /// <summary>
    /// Offline composition root for a defense-day Play Mode demonstration. A multiplayer
    /// bootstrap can inject the same contracts through Configure and leave this unused.
    /// </summary>
    public static class PreparationDemoSession
    {
        public const string PlayerId = "local-player";
        public const string MatchId = "local-preparation";
        public const string CardId = "local-card";

        public static CraftingCatalog Catalog { get; private set; }
        public static PlayerCraftingInventory Inventory { get; private set; }
        public static CraftingController CraftingController { get; private set; }
        public static AuthoritativeCardAssemblyService CardAssembly { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession()
        {
            Catalog = null;
            Inventory = null;
            CraftingController = null;
            CardAssembly = null;
        }

        public static void EnsureInitialized()
        {
            if (Catalog != null) return;
            Catalog = DefaultCraftingCatalog.CreateAvailableAssetCatalog();
            Inventory = new PlayerCraftingInventory(PlayerId);
            var inventories = new PlayerCraftingInventoryCollection();
            inventories.Register(Inventory);
            CraftingController = new CraftingController(
                MatchId, Catalog, new FixedCraftingAuthority(true),
                new FixedCraftingAccessPolicy(true), inventories, new GuidPartInstanceIdProvider());
            CardAssembly = new AuthoritativeCardAssemblyService(
                inventories, new FixedCardAssemblyAccessPolicy(true),
                new FixedCardAssemblyAuthority(true));
            CardAssembly.InitializeCard(PlayerId, CardId);
            foreach (MaterialDefinition material in Catalog.Materials.Values)
                Inventory.TryAddMaterial(material, 6, out _);
        }
    }
}
