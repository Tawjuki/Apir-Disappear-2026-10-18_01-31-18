using System;
using System.Collections.Generic;

namespace ApirApir.Crafting
{
    /// <summary>
    /// Adapter implemented by the authoritative player inventory. TryCraft must consume
    /// every requirement and add the supplied owned part atomically, or do nothing.
    /// </summary>
    public interface ICraftingPlayerInventory
    {
        string PlayerId { get; }
        int GetMaterialQuantity(string materialId);
        bool TryCraft(
            CraftingRecipe recipe,
            OwnedCardPart newPart,
            out IReadOnlyList<MaterialBalanceChange> materialChanges);
        bool TryAddMaterial(MaterialDefinition material, int quantity, out int resultingQuantity);
    }

    public interface ICraftingPlayerInventoryProvider
    {
        bool TryGetInventory(string playerId, out ICraftingPlayerInventory inventory);
    }

    /// <summary>
    /// Read-through extension used by card assembly. Implementations expose the same owned
    /// instances created by crafting or purchases; assembly never mirrors the inventory.
    /// </summary>
    public interface IOwnedCardPartInventory : ICraftingPlayerInventory
    {
        bool TryGetOwnedPart(string instanceId, out OwnedCardPart part);
        IReadOnlyList<OwnedCardPart> GetOwnedParts();
    }

    public interface IOwnedCardPartInventoryProvider
    {
        bool TryGetInventory(string playerId, out IOwnedCardPartInventory inventory);
    }

    public interface ICraftingAccessPolicy
    {
        bool CanCraft(string playerId);
    }

    public sealed class FixedCraftingAccessPolicy : ICraftingAccessPolicy
    {
        private readonly bool allowed;

        public FixedCraftingAccessPolicy(bool allowed)
        {
            this.allowed = allowed;
        }

        public bool CanCraft(string playerId)
        {
            return allowed;
        }
    }

    public interface ICraftingAuthority
    {
        bool IsAuthoritative { get; }
    }

    public sealed class FixedCraftingAuthority : ICraftingAuthority
    {
        public FixedCraftingAuthority(bool isAuthoritative)
        {
            IsAuthoritative = isAuthoritative;
        }

        public bool IsAuthoritative { get; }
    }

    public interface IPartInstanceIdProvider
    {
        string CreateInstanceId(string playerId, CraftablePartDefinition definition);
    }

    public sealed class GuidPartInstanceIdProvider : IPartInstanceIdProvider
    {
        public string CreateInstanceId(string playerId, CraftablePartDefinition definition)
        {
            return Guid.NewGuid().ToString("N");
        }
    }

    /// <summary>
    /// Thread-safe match inventory shared by crafting and card assembly. Materials and
    /// crafted part instances live in one player-owned store, so UI filtering never creates
    /// a second source of truth.
    /// </summary>
    public sealed class PlayerCraftingInventory : IOwnedCardPartInventory
    {
        private readonly object transactionLock = new object();
        private readonly Dictionary<string, int> materials =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, OwnedCardPart> parts =
            new Dictionary<string, OwnedCardPart>(StringComparer.Ordinal);

        public PlayerCraftingInventory(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId))
            {
                throw new ArgumentException("An inventory requires a player ID.", nameof(playerId));
            }

            PlayerId = playerId.Trim();
        }

        public string PlayerId { get; }
        public event Action Changed;

        public int GetMaterialQuantity(string materialId)
        {
            lock (transactionLock)
            {
                return materials.TryGetValue(materialId ?? string.Empty, out int quantity)
                    ? quantity
                    : 0;
            }
        }

        public IReadOnlyDictionary<string, int> GetMaterialBalances()
        {
            lock (transactionLock)
            {
                return new Dictionary<string, int>(materials, StringComparer.Ordinal);
            }
        }

        public IReadOnlyList<OwnedCardPart> GetOwnedParts()
        {
            lock (transactionLock)
            {
                return new List<OwnedCardPart>(parts.Values).AsReadOnly();
            }
        }

        public bool TryGetOwnedPart(string instanceId, out OwnedCardPart part)
        {
            lock (transactionLock)
            {
                return parts.TryGetValue(instanceId ?? string.Empty, out part);
            }
        }

        public bool TryCraft(
            CraftingRecipe recipe,
            OwnedCardPart newPart,
            out IReadOnlyList<MaterialBalanceChange> materialChanges)
        {
            if (recipe == null || newPart == null ||
                !string.Equals(PlayerId, newPart.OwnerPlayerId, StringComparison.Ordinal))
            {
                materialChanges = Array.Empty<MaterialBalanceChange>();
                return false;
            }

            lock (transactionLock)
            {
                if (parts.ContainsKey(newPart.InstanceId))
                {
                    materialChanges = Array.Empty<MaterialBalanceChange>();
                    return false;
                }

                for (int i = 0; i < recipe.Requirements.Count; i++)
                {
                    MaterialRequirement requirement = recipe.Requirements[i];
                    if (!materials.TryGetValue(requirement.MaterialId, out int quantity) ||
                        quantity < requirement.Quantity)
                    {
                        materialChanges = Array.Empty<MaterialBalanceChange>();
                        return false;
                    }
                }

                var changes = new MaterialBalanceChange[recipe.Requirements.Count];
                for (int i = 0; i < recipe.Requirements.Count; i++)
                {
                    MaterialRequirement requirement = recipe.Requirements[i];
                    int remaining = materials[requirement.MaterialId] - requirement.Quantity;
                    materials[requirement.MaterialId] = remaining;
                    changes[i] = new MaterialBalanceChange(
                        requirement.MaterialId, requirement.Quantity, remaining);
                }

                parts.Add(newPart.InstanceId, newPart);
                materialChanges = changes;
            }

            Changed?.Invoke();
            return true;
        }

        public bool TryAddMaterial(
            MaterialDefinition material,
            int quantity,
            out int resultingQuantity)
        {
            if (material == null || quantity <= 0)
            {
                resultingQuantity = 0;
                return false;
            }

            lock (transactionLock)
            {
                materials.TryGetValue(material.Id, out int current);
                if (current > int.MaxValue - quantity)
                {
                    resultingQuantity = current;
                    return false;
                }

                resultingQuantity = current + quantity;
                materials[material.Id] = resultingQuantity;
            }

            Changed?.Invoke();
            return true;
        }
    }

    /// <summary>Player-ID keyed provider used by both authoritative services.</summary>
    public sealed class PlayerCraftingInventoryCollection :
        ICraftingPlayerInventoryProvider, IOwnedCardPartInventoryProvider
    {
        private readonly Dictionary<string, PlayerCraftingInventory> inventories =
            new Dictionary<string, PlayerCraftingInventory>(StringComparer.Ordinal);

        public bool Register(PlayerCraftingInventory inventory)
        {
            if (inventory == null || inventories.ContainsKey(inventory.PlayerId))
            {
                return false;
            }

            inventories.Add(inventory.PlayerId, inventory);
            return true;
        }

        public bool TryGetInventory(string playerId, out ICraftingPlayerInventory inventory)
        {
            bool found = inventories.TryGetValue(
                playerId == null ? string.Empty : playerId.Trim(), out PlayerCraftingInventory concrete);
            inventory = concrete;
            return found;
        }

        bool IOwnedCardPartInventoryProvider.TryGetInventory(
            string playerId,
            out IOwnedCardPartInventory inventory)
        {
            bool found = inventories.TryGetValue(
                playerId == null ? string.Empty : playerId.Trim(), out PlayerCraftingInventory concrete);
            inventory = concrete;
            return found;
        }
    }
}
