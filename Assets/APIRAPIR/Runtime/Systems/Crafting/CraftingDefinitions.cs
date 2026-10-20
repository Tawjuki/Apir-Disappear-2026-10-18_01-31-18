using System;
using System.Collections.Generic;
using ApirApir.Battle;
using ApirApir.Shop;

namespace ApirApir.Crafting
{
    public sealed class MaterialDefinition : IShopItemDefinition
    {
        private readonly object inventoryDefinition;

        public MaterialDefinition(
            string id,
            string displayName,
            MaterialRarity rarity,
            int marketValue = 0,
            object inventoryDefinition = null)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A material requires a stable ID.", nameof(id));
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException("A material requires a display name.", nameof(displayName));
            }

            if (marketValue < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(marketValue));
            }

            Id = id;
            DisplayName = displayName;
            Rarity = rarity;
            MarketValue = marketValue;
            this.inventoryDefinition = inventoryDefinition;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public MaterialRarity Rarity { get; }
        public int MarketValue { get; }
        public bool IsSellable => true;
        public object InventoryDefinition => inventoryDefinition ?? this;
        public PartSlot? CompletedPartSlot => null;

        public ShopItemCategory Category
        {
            get
            {
                switch (Rarity)
                {
                    case MaterialRarity.Common:
                        return ShopItemCategory.CommonMaterial;
                    case MaterialRarity.Rare:
                        return ShopItemCategory.RareMaterial;
                    case MaterialRarity.Epic:
                        return ShopItemCategory.EpicMaterial;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        ShopMaterialRarity? IShopItemDefinition.MaterialRarity
        {
            get
            {
                switch (Rarity)
                {
                    case MaterialRarity.Common:
                        return ShopMaterialRarity.Common;
                    case MaterialRarity.Rare:
                        return ShopMaterialRarity.Rare;
                    case MaterialRarity.Epic:
                        return ShopMaterialRarity.Epic;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }
    }

    public sealed class MaterialRequirement
    {
        public MaterialRequirement(string materialId, int quantity)
        {
            if (string.IsNullOrWhiteSpace(materialId))
            {
                throw new ArgumentException("A material requirement needs an item ID.", nameof(materialId));
            }

            if (quantity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(quantity));
            }

            MaterialId = materialId;
            Quantity = quantity;
        }

        public string MaterialId { get; }
        public int Quantity { get; }
    }

    public sealed class CraftablePartDefinition : IShopItemDefinition
    {
        public CraftablePartDefinition(
            string id,
            string displayName,
            string characterId,
            PartSlot slot,
            PartRarity rarity,
            string recipeId,
            PartDefinition battleDefinition,
            int marketValue = 0,
            string characterDisplayName = null,
            IReadOnlyDictionary<string, float> additionalStatModifiers = null,
            IReadOnlyList<string> passiveAbilityReferences = null,
            string visualAssetId = null,
            float visualOffsetX = 0f,
            float visualOffsetY = 0f,
            float visualScale = 1f,
            int? visualLayerOrder = null)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(displayName) ||
                string.IsNullOrWhiteSpace(characterId) || string.IsNullOrWhiteSpace(recipeId))
            {
                throw new ArgumentException("Part IDs, display names, character IDs, and recipe IDs are required.");
            }

            if (battleDefinition == null)
            {
                throw new ArgumentNullException(nameof(battleDefinition));
            }

            if (battleDefinition.Id != id || battleDefinition.Slot != slot || battleDefinition.Rarity != rarity)
            {
                throw new CraftingConfigurationException(
                    "Crafting and battle part metadata must identify the same part.");
            }

            if (marketValue < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(marketValue));
            }

            if (visualScale <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(visualScale));
            }

            Id = id;
            DisplayName = displayName;
            CharacterId = characterId;
            Slot = slot;
            Rarity = rarity;
            RecipeId = recipeId;
            BattleDefinition = battleDefinition;
            MarketValue = marketValue;
            CharacterDisplayName = string.IsNullOrWhiteSpace(characterDisplayName)
                ? displayName
                : characterDisplayName.Trim();
            AdditionalStatModifiers = CopyModifiers(additionalStatModifiers);
            PassiveAbilityReferences = passiveAbilityReferences == null
                ? CopyPassiveIdentifiers(battleDefinition.PassiveAbilities)
                : CopyIdentifiers(passiveAbilityReferences);
            VisualAssetId = string.IsNullOrWhiteSpace(visualAssetId) ? id : visualAssetId.Trim();
            VisualOffsetX = visualOffsetX;
            VisualOffsetY = visualOffsetY;
            VisualScale = visualScale;
            VisualLayerOrder = visualLayerOrder ?? GetDefaultLayerOrder(slot);
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string CharacterId { get; }
        public string CharacterDisplayName { get; }
        public PartSlot Slot { get; }
        public PartRarity Rarity { get; }
        public string RecipeId { get; }
        public PartDefinition BattleDefinition { get; }
        public int InitialMaintenance => BattleDefinition.MaximumMaintenance;
        public int MaximumMaintenance => BattleDefinition.MaximumMaintenance;
        public int HitPointContribution => BattleDefinition.AdditionalHitPoints;
        public float HitPointBonusPercent => BattleDefinition.HitPointBonusPercent;
        public float AttackBonusPercent => BattleDefinition.AttackBonusPercent;
        public float SpeedBonusPercent => BattleDefinition.SpeedBonusPercent;
        public float CriticalChanceBonusPercent => BattleDefinition.CriticalChanceBonusPercent;
        public IReadOnlyDictionary<string, float> AdditionalStatModifiers { get; }
        public IReadOnlyList<string> PassiveAbilityReferences { get; }
        public string VisualAssetId { get; }
        public float VisualOffsetX { get; }
        public float VisualOffsetY { get; }
        public float VisualScale { get; }
        public int VisualLayerOrder { get; }
        public int MarketValue { get; }
        public ShopItemCategory Category => ShopItemCategory.CompletedPart;
        public ShopMaterialRarity? MaterialRarity => null;
        public PartSlot? CompletedPartSlot => Slot;
        public object InventoryDefinition => this;
        public bool IsSellable => false;

        private static IReadOnlyDictionary<string, float> CopyModifiers(
            IReadOnlyDictionary<string, float> source)
        {
            var copy = new Dictionary<string, float>(StringComparer.Ordinal);
            if (source == null)
            {
                return copy;
            }

            foreach (KeyValuePair<string, float> value in source)
            {
                if (string.IsNullOrWhiteSpace(value.Key))
                {
                    throw new ArgumentException("Stat modifier IDs cannot be blank.", nameof(source));
                }

                copy.Add(value.Key.Trim(), value.Value);
            }

            return copy;
        }

        private static IReadOnlyList<string> CopyIdentifiers(IReadOnlyList<string> source)
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<string>();
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var copy = new string[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                string value = source[i] == null ? string.Empty : source[i].Trim();
                if (value.Length == 0 || !seen.Add(value))
                {
                    throw new ArgumentException("Passive references must be unique, non-blank IDs.", nameof(source));
                }

                copy[i] = value;
            }

            return copy;
        }

        private static IReadOnlyList<string> CopyPassiveIdentifiers(
            IReadOnlyList<PartPassiveDefinition> source)
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<string>();
            }

            var result = new string[source.Count];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = source[i].Id;
            }

            return result;
        }

        private static int GetDefaultLayerOrder(PartSlot slot)
        {
            switch (slot)
            {
                case PartSlot.Legs: return 0;
                case PartSlot.Body: return 1;
                case PartSlot.Head: return 2;
                default: throw new ArgumentOutOfRangeException(nameof(slot));
            }
        }
    }

    public sealed class CraftingRecipe
    {
        public CraftingRecipe(
            string id,
            CraftablePartDefinition outputPart,
            IReadOnlyList<MaterialRequirement> requirements,
            bool isEnabled = true)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A recipe requires a stable ID.", nameof(id));
            }

            OutputPart = outputPart ?? throw new ArgumentNullException(nameof(outputPart));
            if (outputPart.RecipeId != id)
            {
                throw new CraftingConfigurationException("The output part references a different recipe ID.");
            }

            if (requirements == null || requirements.Count == 0)
            {
                throw new ArgumentException("A recipe requires at least one material.", nameof(requirements));
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var copy = new MaterialRequirement[requirements.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                MaterialRequirement requirement = requirements[i] ??
                    throw new ArgumentException("Recipe requirements cannot contain null entries.");
                if (!ids.Add(requirement.MaterialId))
                {
                    throw new CraftingConfigurationException(
                        "Recipe " + id + " contains a duplicate material requirement.");
                }

                copy[i] = requirement;
            }

            Id = id;
            Requirements = copy;
            IsEnabled = isEnabled;
        }

        public string Id { get; }
        public CraftablePartDefinition OutputPart { get; }
        public IReadOnlyList<MaterialRequirement> Requirements { get; }
        public bool IsEnabled { get; }
        public string CharacterId => OutputPart.CharacterId;
        public PartSlot Slot => OutputPart.Slot;
        public PartRarity Rarity => OutputPart.Rarity;
    }

    public sealed class OwnedCardPart
    {
        public OwnedCardPart(string instanceId, string ownerPlayerId, CraftablePartDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(ownerPlayerId))
            {
                throw new ArgumentException("Owned parts require instance and owner IDs.");
            }

            InstanceId = instanceId;
            OwnerPlayerId = ownerPlayerId;
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            CurrentMaintenance = definition.InitialMaintenance;
        }

        public string InstanceId { get; }
        public string OwnerPlayerId { get; }
        public CraftablePartDefinition Definition { get; }
        public int CurrentMaintenance { get; private set; }
        public bool IsEquipped { get; private set; }

        public bool TrySetMaintenance(int maintenance)
        {
            if (maintenance < 0 || maintenance > Definition.InitialMaintenance)
            {
                return false;
            }

            CurrentMaintenance = maintenance;
            return true;
        }

        public EquippedPart ToBattlePart()
        {
            return new EquippedPart(Definition.BattleDefinition, CurrentMaintenance);
        }

        /// <summary>
        /// Updates the derived inventory/equipment state. Only the authoritative card assembly
        /// service should call this; ownership and Maintenance remain on this same instance.
        /// </summary>
        public void SetEquipped(bool equipped)
        {
            IsEquipped = equipped;
        }
    }

    public sealed class CraftingCatalog
    {
        private readonly Dictionary<string, MaterialDefinition> materials;
        private readonly Dictionary<string, CraftablePartDefinition> parts;
        private readonly Dictionary<string, CraftingRecipe> recipes;
        private readonly Dictionary<string, List<CraftingRecipe>> recipesByMaterial;

        public CraftingCatalog()
        {
            materials = new Dictionary<string, MaterialDefinition>(StringComparer.Ordinal);
            parts = new Dictionary<string, CraftablePartDefinition>(StringComparer.Ordinal);
            recipes = new Dictionary<string, CraftingRecipe>(StringComparer.Ordinal);
            recipesByMaterial = new Dictionary<string, List<CraftingRecipe>>(StringComparer.Ordinal);
        }

        public IReadOnlyDictionary<string, MaterialDefinition> Materials => materials;
        public IReadOnlyDictionary<string, CraftablePartDefinition> Parts => parts;
        public IReadOnlyDictionary<string, CraftingRecipe> Recipes => recipes;

        public void RegisterMaterial(MaterialDefinition material)
        {
            if (material == null)
            {
                throw new ArgumentNullException(nameof(material));
            }

            if (materials.ContainsKey(material.Id))
            {
                throw new CraftingConfigurationException("Duplicate material ID: " + material.Id);
            }

            materials.Add(material.Id, material);
            recipesByMaterial.Add(material.Id, new List<CraftingRecipe>());
        }

        public void RegisterPartAndRecipe(CraftablePartDefinition part, CraftingRecipe recipe)
        {
            if (part == null || recipe == null)
            {
                throw new ArgumentNullException(part == null ? nameof(part) : nameof(recipe));
            }

            if (!ReferenceEquals(recipe.OutputPart, part))
            {
                throw new CraftingConfigurationException("A recipe must reference its registered output part.");
            }

            if (parts.ContainsKey(part.Id) || recipes.ContainsKey(recipe.Id))
            {
                throw new CraftingConfigurationException("Duplicate part or recipe ID: " + part.Id);
            }

            for (int i = 0; i < recipe.Requirements.Count; i++)
            {
                if (!materials.ContainsKey(recipe.Requirements[i].MaterialId))
                {
                    throw new CraftingConfigurationException(
                        "Recipe " + recipe.Id + " references unknown material " +
                        recipe.Requirements[i].MaterialId + ".");
                }
            }

            parts.Add(part.Id, part);
            recipes.Add(recipe.Id, recipe);
            for (int i = 0; i < recipe.Requirements.Count; i++)
            {
                recipesByMaterial[recipe.Requirements[i].MaterialId].Add(recipe);
            }
        }

        public bool TryGetMaterial(string materialId, out MaterialDefinition material)
        {
            return materials.TryGetValue(materialId ?? string.Empty, out material);
        }

        public bool TryGetRecipe(string recipeId, out CraftingRecipe recipe)
        {
            return recipes.TryGetValue(recipeId ?? string.Empty, out recipe);
        }

        public IReadOnlyList<CraftingRecipe> GetRecipesUsingMaterial(string materialId)
        {
            return recipesByMaterial.TryGetValue(materialId ?? string.Empty, out List<CraftingRecipe> found)
                ? found
                : Array.Empty<CraftingRecipe>();
        }

        public void RegisterWithShop(ShopItemCatalog shopCatalog, bool includeCompletedParts = true)
        {
            if (shopCatalog == null)
            {
                throw new ArgumentNullException(nameof(shopCatalog));
            }

            foreach (MaterialDefinition material in materials.Values)
            {
                shopCatalog.Register(material);
            }

            if (!includeCompletedParts)
            {
                return;
            }

            foreach (CraftablePartDefinition part in parts.Values)
            {
                shopCatalog.Register(part);
            }
        }
    }
}
