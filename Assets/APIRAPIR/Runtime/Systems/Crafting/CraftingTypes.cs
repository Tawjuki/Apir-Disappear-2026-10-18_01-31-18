using System;

namespace ApirApir.Crafting
{
    [Flags]
    public enum PartEquipmentRegion : byte
    {
        None = 0,
        Head = 1 << 0,
        Body = 1 << 1,
        Legs = 1 << 2
    }

    public enum MaterialRarity : byte
    {
        Common,
        Rare,
        Epic
    }

    public enum CraftingFailure : byte
    {
        None,
        NotAuthoritative,
        CraftingNotAllowed,
        DuplicateCommand,
        RecipeNotFound,
        RecipeDisabled,
        InventoryNotFound,
        MissingMaterialDefinition,
        InsufficientMaterials,
        InvalidRequest,
        InventoryTransactionFailed
    }

    public enum CraftingEventType : byte
    {
        CraftingRequested,
        CraftingSucceeded,
        CraftingFailed,
        MaterialsConsumed,
        CardPartCreated,
        InventoryUpdated,
        CraftableRecipeAvailabilityChanged,
        MaterialRewarded,
        MaterialRewardRejected
    }

    public readonly struct CraftingRequest
    {
        public CraftingRequest(string matchId, string recipeId, string commandId)
        {
            MatchId = matchId ?? string.Empty;
            RecipeId = recipeId ?? string.Empty;
            CommandId = commandId ?? string.Empty;
        }

        public string MatchId { get; }
        public string RecipeId { get; }
        public string CommandId { get; }
    }

    public readonly struct MaterialRewardRequest
    {
        public MaterialRewardRequest(
            string matchId,
            string materialId,
            int quantity,
            string commandId,
            string sourceId = null)
        {
            MatchId = matchId ?? string.Empty;
            MaterialId = materialId ?? string.Empty;
            Quantity = quantity;
            CommandId = commandId ?? string.Empty;
            SourceId = sourceId ?? string.Empty;
        }

        public string MatchId { get; }
        public string MaterialId { get; }
        public int Quantity { get; }
        public string CommandId { get; }
        public string SourceId { get; }
    }

    public readonly struct MaterialBalanceChange
    {
        public MaterialBalanceChange(string materialId, int quantityConsumed, int remainingQuantity)
        {
            MaterialId = materialId ?? string.Empty;
            QuantityConsumed = quantityConsumed;
            RemainingQuantity = remainingQuantity;
        }

        public string MaterialId { get; }
        public int QuantityConsumed { get; }
        public int RemainingQuantity { get; }
    }

    public sealed class CraftingResult
    {
        private CraftingResult(
            bool succeeded,
            CraftingFailure failure,
            string playerId,
            string recipeId,
            OwnedCardPart createdPart)
        {
            Succeeded = succeeded;
            Failure = failure;
            PlayerId = playerId ?? string.Empty;
            RecipeId = recipeId ?? string.Empty;
            CreatedPart = createdPart;
        }

        public bool Succeeded { get; }
        public CraftingFailure Failure { get; }
        public string PlayerId { get; }
        public string RecipeId { get; }
        public OwnedCardPart CreatedPart { get; }

        internal static CraftingResult Success(string playerId, string recipeId, OwnedCardPart part)
        {
            return new CraftingResult(true, CraftingFailure.None, playerId, recipeId, part);
        }

        internal static CraftingResult Rejected(
            CraftingFailure failure,
            string playerId = null,
            string recipeId = null)
        {
            return new CraftingResult(false, failure, playerId, recipeId, null);
        }
    }

    public sealed class MaterialRewardResult
    {
        private MaterialRewardResult(
            bool succeeded,
            CraftingFailure failure,
            string materialId,
            int quantity,
            int resultingQuantity)
        {
            Succeeded = succeeded;
            Failure = failure;
            MaterialId = materialId ?? string.Empty;
            Quantity = quantity;
            ResultingQuantity = resultingQuantity;
        }

        public bool Succeeded { get; }
        public CraftingFailure Failure { get; }
        public string MaterialId { get; }
        public int Quantity { get; }
        public int ResultingQuantity { get; }

        internal static MaterialRewardResult Success(string materialId, int quantity, int resultingQuantity)
        {
            return new MaterialRewardResult(true, CraftingFailure.None, materialId, quantity, resultingQuantity);
        }

        internal static MaterialRewardResult Rejected(CraftingFailure failure, string materialId)
        {
            return new MaterialRewardResult(false, failure, materialId, 0, 0);
        }
    }

    public sealed class CraftingEvent
    {
        public CraftingEvent(
            CraftingEventType type,
            string matchId,
            string playerId,
            string subjectId,
            int value = 0,
            int secondaryValue = 0,
            CraftingFailure failure = CraftingFailure.None)
        {
            Type = type;
            MatchId = matchId ?? string.Empty;
            PlayerId = playerId ?? string.Empty;
            SubjectId = subjectId ?? string.Empty;
            Value = value;
            SecondaryValue = secondaryValue;
            Failure = failure;
        }

        public CraftingEventType Type { get; }
        public string MatchId { get; }
        public string PlayerId { get; }
        public string SubjectId { get; }
        public int Value { get; }
        public int SecondaryValue { get; }
        public CraftingFailure Failure { get; }
    }

    public sealed class CraftingConfigurationException : Exception
    {
        public CraftingConfigurationException(string message) : base(message)
        {
        }
    }
}
