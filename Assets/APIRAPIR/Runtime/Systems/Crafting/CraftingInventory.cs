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
}
