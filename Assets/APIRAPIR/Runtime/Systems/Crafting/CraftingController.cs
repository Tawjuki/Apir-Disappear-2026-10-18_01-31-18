using System;
using System.Collections.Generic;

namespace ApirApir.Crafting
{
    public sealed class CraftingController
    {
        private readonly object commandLock;
        private readonly CraftingCatalog catalog;
        private readonly ICraftingAuthority authority;
        private readonly ICraftingAccessPolicy accessPolicy;
        private readonly ICraftingPlayerInventoryProvider inventories;
        private readonly IPartInstanceIdProvider instanceIds;
        private readonly HashSet<string> processedCommands;

        public CraftingController(
            string matchId,
            CraftingCatalog catalog,
            ICraftingAuthority authority,
            ICraftingAccessPolicy accessPolicy,
            ICraftingPlayerInventoryProvider inventories,
            IPartInstanceIdProvider instanceIds)
        {
            if (string.IsNullOrWhiteSpace(matchId))
            {
                throw new ArgumentException("Crafting requires its match ID.", nameof(matchId));
            }

            MatchId = matchId;
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
            this.accessPolicy = accessPolicy ?? throw new ArgumentNullException(nameof(accessPolicy));
            this.inventories = inventories ?? throw new ArgumentNullException(nameof(inventories));
            this.instanceIds = instanceIds ?? throw new ArgumentNullException(nameof(instanceIds));
            commandLock = new object();
            processedCommands = new HashSet<string>(StringComparer.Ordinal);
        }

        public string MatchId { get; }
        public event Action<CraftingEvent> EventPublished;

        public CraftingResult Craft(CraftingRequest request, string authenticatedPlayerId)
        {
            if (!authority.IsAuthoritative)
            {
                return CraftingResult.Rejected(
                    CraftingFailure.NotAuthoritative,
                    authenticatedPlayerId,
                    request.RecipeId);
            }

            Publish(CraftingEventType.CraftingRequested, authenticatedPlayerId, request.RecipeId);
            CraftingFailure validation = ValidateCraftRequest(request, authenticatedPlayerId);
            if (validation != CraftingFailure.None)
            {
                return Reject(validation, authenticatedPlayerId, request.RecipeId);
            }

            catalog.TryGetRecipe(request.RecipeId, out CraftingRecipe recipe);
            inventories.TryGetInventory(authenticatedPlayerId, out ICraftingPlayerInventory inventory);
            for (int i = 0; i < recipe.Requirements.Count; i++)
            {
                MaterialRequirement requirement = recipe.Requirements[i];
                if (!catalog.TryGetMaterial(requirement.MaterialId, out _))
                {
                    return Reject(
                        CraftingFailure.MissingMaterialDefinition,
                        authenticatedPlayerId,
                        recipe.Id);
                }

                if (inventory.GetMaterialQuantity(requirement.MaterialId) < requirement.Quantity)
                {
                    return Reject(
                        CraftingFailure.InsufficientMaterials,
                        authenticatedPlayerId,
                        recipe.Id);
                }
            }

            string instanceId = instanceIds.CreateInstanceId(authenticatedPlayerId, recipe.OutputPart);
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                return Reject(CraftingFailure.InvalidRequest, authenticatedPlayerId, recipe.Id);
            }

            var ownedPart = new OwnedCardPart(instanceId, authenticatedPlayerId, recipe.OutputPart);
            if (!inventory.TryCraft(recipe, ownedPart, out IReadOnlyList<MaterialBalanceChange> changes))
            {
                return Reject(
                    CraftingFailure.InventoryTransactionFailed,
                    authenticatedPlayerId,
                    recipe.Id);
            }

            PublishMaterialChanges(authenticatedPlayerId, changes);
            Publish(
                CraftingEventType.CardPartCreated,
                authenticatedPlayerId,
                ownedPart.InstanceId,
                ownedPart.CurrentMaintenance,
                (int)ownedPart.Definition.Slot);
            Publish(CraftingEventType.InventoryUpdated, authenticatedPlayerId, ownedPart.Definition.Id);
            Publish(CraftingEventType.CraftingSucceeded, authenticatedPlayerId, recipe.Id);
            PublishAffectedAvailability(authenticatedPlayerId, recipe, inventory);
            return CraftingResult.Success(authenticatedPlayerId, recipe.Id, ownedPart);
        }

        public MaterialRewardResult AwardMaterial(
            MaterialRewardRequest request,
            string authoritativePlayerId)
        {
            if (!authority.IsAuthoritative)
            {
                return MaterialRewardResult.Rejected(
                    CraftingFailure.NotAuthoritative,
                    request.MaterialId);
            }

            if (!string.Equals(request.MatchId, MatchId, StringComparison.Ordinal) ||
                request.Quantity <= 0 ||
                string.IsNullOrWhiteSpace(request.MaterialId) ||
                string.IsNullOrWhiteSpace(authoritativePlayerId) ||
                string.IsNullOrWhiteSpace(request.CommandId))
            {
                return RejectReward(
                    request,
                    authoritativePlayerId,
                    CraftingFailure.InvalidRequest);
            }

            if (!TryAcceptCommand(authoritativePlayerId, "reward", request.CommandId))
            {
                return RejectReward(
                    request,
                    authoritativePlayerId,
                    CraftingFailure.DuplicateCommand);
            }

            if (!catalog.TryGetMaterial(request.MaterialId, out MaterialDefinition material))
            {
                return RejectReward(
                    request,
                    authoritativePlayerId,
                    CraftingFailure.MissingMaterialDefinition);
            }

            if (!inventories.TryGetInventory(authoritativePlayerId, out ICraftingPlayerInventory inventory))
            {
                return RejectReward(
                    request,
                    authoritativePlayerId,
                    CraftingFailure.InventoryNotFound);
            }

            if (!inventory.TryAddMaterial(material, request.Quantity, out int resultingQuantity))
            {
                return RejectReward(
                    request,
                    authoritativePlayerId,
                    CraftingFailure.InventoryTransactionFailed);
            }

            Publish(
                CraftingEventType.MaterialRewarded,
                authoritativePlayerId,
                material.Id,
                request.Quantity,
                resultingQuantity);
            Publish(CraftingEventType.InventoryUpdated, authoritativePlayerId, material.Id);
            PublishAvailabilityForMaterial(authoritativePlayerId, material.Id, inventory);
            return MaterialRewardResult.Success(material.Id, request.Quantity, resultingQuantity);
        }

        public IReadOnlyList<CraftingRecipe> GetCraftableRecipes(string playerId)
        {
            if (!inventories.TryGetInventory(playerId, out ICraftingPlayerInventory inventory))
            {
                return Array.Empty<CraftingRecipe>();
            }

            var result = new List<CraftingRecipe>();
            foreach (CraftingRecipe recipe in catalog.Recipes.Values)
            {
                if (recipe.IsEnabled && CanCraft(recipe, inventory))
                {
                    result.Add(recipe);
                }
            }

            return result;
        }

        private CraftingFailure ValidateCraftRequest(CraftingRequest request, string playerId)
        {
            if (!string.Equals(request.MatchId, MatchId, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(request.RecipeId) ||
                string.IsNullOrWhiteSpace(request.CommandId) ||
                string.IsNullOrWhiteSpace(playerId))
            {
                return CraftingFailure.InvalidRequest;
            }

            if (!accessPolicy.CanCraft(playerId))
            {
                return CraftingFailure.CraftingNotAllowed;
            }

            if (!catalog.TryGetRecipe(request.RecipeId, out CraftingRecipe recipe))
            {
                return CraftingFailure.RecipeNotFound;
            }

            if (!recipe.IsEnabled)
            {
                return CraftingFailure.RecipeDisabled;
            }

            if (!inventories.TryGetInventory(playerId, out _))
            {
                return CraftingFailure.InventoryNotFound;
            }

            return TryAcceptCommand(playerId, "craft", request.CommandId)
                ? CraftingFailure.None
                : CraftingFailure.DuplicateCommand;
        }

        private bool TryAcceptCommand(string playerId, string kind, string commandId)
        {
            if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(commandId))
            {
                return false;
            }

            lock (commandLock)
            {
                return processedCommands.Add(playerId + "\n" + kind + "\n" + commandId);
            }
        }

        private void PublishMaterialChanges(
            string playerId,
            IReadOnlyList<MaterialBalanceChange> changes)
        {
            if (changes == null)
            {
                return;
            }

            for (int i = 0; i < changes.Count; i++)
            {
                MaterialBalanceChange change = changes[i];
                Publish(
                    CraftingEventType.MaterialsConsumed,
                    playerId,
                    change.MaterialId,
                    change.QuantityConsumed,
                    change.RemainingQuantity);
            }
        }

        private void PublishAffectedAvailability(
            string playerId,
            CraftingRecipe craftedRecipe,
            ICraftingPlayerInventory inventory)
        {
            var affected = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < craftedRecipe.Requirements.Count; i++)
            {
                IReadOnlyList<CraftingRecipe> recipes =
                    catalog.GetRecipesUsingMaterial(craftedRecipe.Requirements[i].MaterialId);
                for (int j = 0; j < recipes.Count; j++)
                {
                    if (affected.Add(recipes[j].Id))
                    {
                        PublishAvailability(playerId, recipes[j], inventory);
                    }
                }
            }
        }

        private void PublishAvailabilityForMaterial(
            string playerId,
            string materialId,
            ICraftingPlayerInventory inventory)
        {
            IReadOnlyList<CraftingRecipe> recipes = catalog.GetRecipesUsingMaterial(materialId);
            for (int i = 0; i < recipes.Count; i++)
            {
                PublishAvailability(playerId, recipes[i], inventory);
            }
        }

        private void PublishAvailability(
            string playerId,
            CraftingRecipe recipe,
            ICraftingPlayerInventory inventory)
        {
            Publish(
                CraftingEventType.CraftableRecipeAvailabilityChanged,
                playerId,
                recipe.Id,
                CanCraft(recipe, inventory) ? 1 : 0);
        }

        private static bool CanCraft(CraftingRecipe recipe, ICraftingPlayerInventory inventory)
        {
            for (int i = 0; i < recipe.Requirements.Count; i++)
            {
                MaterialRequirement requirement = recipe.Requirements[i];
                if (inventory.GetMaterialQuantity(requirement.MaterialId) < requirement.Quantity)
                {
                    return false;
                }
            }

            return true;
        }

        private CraftingResult Reject(CraftingFailure failure, string playerId, string recipeId)
        {
            Publish(
                CraftingEventType.CraftingFailed,
                playerId,
                recipeId,
                failure: failure);
            return CraftingResult.Rejected(failure, playerId, recipeId);
        }

        private MaterialRewardResult RejectReward(
            MaterialRewardRequest request,
            string playerId,
            CraftingFailure failure)
        {
            Publish(
                CraftingEventType.MaterialRewardRejected,
                playerId,
                request.MaterialId,
                request.Quantity,
                failure: failure);
            return MaterialRewardResult.Rejected(failure, request.MaterialId);
        }

        private void Publish(
            CraftingEventType type,
            string playerId,
            string subjectId,
            int value = 0,
            int secondaryValue = 0,
            CraftingFailure failure = CraftingFailure.None)
        {
            EventPublished?.Invoke(new CraftingEvent(
                type,
                MatchId,
                playerId,
                subjectId,
                value,
                secondaryValue,
                failure));
        }
    }
}
