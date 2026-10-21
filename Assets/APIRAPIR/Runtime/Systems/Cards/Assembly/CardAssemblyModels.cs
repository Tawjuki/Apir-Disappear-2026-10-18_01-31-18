using System;
using System.Collections.Generic;
using ApirApir.Battle;
using ApirApir.Crafting;

namespace ApirApir.Cards
{
    public enum CardAssemblyFailure : byte
    {
        None,
        NotAuthoritative,
        InvalidRequest,
        CardNotFound,
        InventoryNotFound,
        PartNotFound,
        PartNotOwned,
        WrongSlot,
        EquipmentLocked,
        AlreadyEquipped,
        DuplicateCommand,
        StaleRevision,
        InvalidBuild,
        BattleResultMismatch
    }

    public enum CardAssemblyEventType : byte
    {
        CardInitialized,
        HeadEquipped,
        BodyEquipped,
        LegsEquipped,
        PartUnequipped,
        PartReplaced,
        CardConfigurationChanged,
        CardStatisticsRecalculated,
        InvalidEquipmentRequestRejected,
        CardBuildLockedForBattle,
        CardBuildUnlockedForPreparation,
        BattleConditionSynchronized
    }

    public sealed class CardEffectiveStats
    {
        public CardEffectiveStats(
            int maximumHitPoints,
            int effectiveAttack,
            float attackBonusPercent,
            IReadOnlyDictionary<string, float> additionalModifiers,
            IReadOnlyList<string> activePassiveReferences,
            float speedBonusPercent = 0f,
            float criticalChanceBonusPercent = 0f)
        {
            MaximumHitPoints = maximumHitPoints;
            EffectiveAttack = effectiveAttack;
            AttackBonusPercent = attackBonusPercent;
            AdditionalModifiers = additionalModifiers ??
                                  new Dictionary<string, float>(StringComparer.Ordinal);
            ActivePassiveReferences = activePassiveReferences ?? Array.Empty<string>();
            SpeedBonusPercent = speedBonusPercent;
            CriticalChanceBonusPercent = criticalChanceBonusPercent;
        }

        public int MaximumHitPoints { get; }
        public int EffectiveAttack { get; }
        public float AttackBonusPercent { get; }
        public IReadOnlyDictionary<string, float> AdditionalModifiers { get; }
        public IReadOnlyList<string> ActivePassiveReferences { get; }
        public float SpeedBonusPercent { get; }
        public float CriticalChanceBonusPercent { get; }
    }

    public sealed class CardPartSlotSnapshot
    {
        public CardPartSlotSnapshot(PartSlot slot, OwnedCardPart part)
        {
            Slot = slot;
            if (part == null)
            {
                return;
            }

            InstanceId = part.InstanceId;
            DefinitionId = part.Definition.Id;
            VisualAssetId = part.Definition.VisualAssetId;
            CurrentMaintenance = part.CurrentMaintenance;
            MaximumMaintenance = part.Definition.MaximumMaintenance;
            VisualOffsetX = part.Definition.VisualOffsetX;
            VisualOffsetY = part.Definition.VisualOffsetY;
            VisualScale = part.Definition.VisualScale;
            VisualLayerOrder = part.Definition.VisualLayerOrder;
            OccupiedRegions = part.Definition.OccupiedRegions;
        }

        public PartSlot Slot { get; }
        public string InstanceId { get; } = string.Empty;
        public string DefinitionId { get; } = string.Empty;
        public string VisualAssetId { get; } = string.Empty;
        public int CurrentMaintenance { get; }
        public int MaximumMaintenance { get; }
        public float VisualOffsetX { get; }
        public float VisualOffsetY { get; }
        public float VisualScale { get; } = 1f;
        public int VisualLayerOrder { get; }
        public PartEquipmentRegion OccupiedRegions { get; }
        public bool IsEmpty => InstanceId.Length == 0;

        internal static CardPartSlotSnapshot FromSerialized(
            PartSlot slot,
            string instanceId,
            string definitionId,
            string visualAssetId,
            int currentMaintenance,
            int maximumMaintenance,
            float visualOffsetX,
            float visualOffsetY,
            float visualScale,
            int visualLayerOrder,
            PartEquipmentRegion occupiedRegions = PartEquipmentRegion.None)
        {
            return new CardPartSlotSnapshot(
                slot,
                instanceId ?? string.Empty,
                definitionId ?? string.Empty,
                visualAssetId ?? string.Empty,
                currentMaintenance,
                maximumMaintenance,
                visualOffsetX,
                visualOffsetY,
                visualScale,
                visualLayerOrder,
                occupiedRegions == PartEquipmentRegion.None
                    ? RegionFor(slot)
                    : occupiedRegions);
        }

        private CardPartSlotSnapshot(
            PartSlot slot,
            string instanceId,
            string definitionId,
            string visualAssetId,
            int currentMaintenance,
            int maximumMaintenance,
            float visualOffsetX,
            float visualOffsetY,
            float visualScale,
            int visualLayerOrder,
            PartEquipmentRegion occupiedRegions)
        {
            Slot = slot;
            InstanceId = instanceId;
            DefinitionId = definitionId;
            VisualAssetId = visualAssetId;
            CurrentMaintenance = currentMaintenance;
            MaximumMaintenance = maximumMaintenance;
            VisualOffsetX = visualOffsetX;
            VisualOffsetY = visualOffsetY;
            VisualScale = visualScale;
            VisualLayerOrder = visualLayerOrder;
            OccupiedRegions = occupiedRegions;
        }

        private static PartEquipmentRegion RegionFor(PartSlot slot)
        {
            switch (slot)
            {
                case PartSlot.Head: return PartEquipmentRegion.Head;
                case PartSlot.Body: return PartEquipmentRegion.Body;
                case PartSlot.Legs: return PartEquipmentRegion.Legs;
                default: return PartEquipmentRegion.None;
            }
        }
    }

    public sealed class CardBuildSnapshot
    {
        public CardBuildSnapshot(
            string ownerPlayerId,
            string cardId,
            long revision,
            bool isLockedForBattle,
            CardPartSlotSnapshot head,
            CardPartSlotSnapshot body,
            CardPartSlotSnapshot legs,
            CardEffectiveStats stats)
        {
            OwnerPlayerId = ownerPlayerId ?? string.Empty;
            CardId = cardId ?? string.Empty;
            Revision = revision;
            IsLockedForBattle = isLockedForBattle;
            Head = head ?? new CardPartSlotSnapshot(PartSlot.Head, null);
            Body = body ?? new CardPartSlotSnapshot(PartSlot.Body, null);
            Legs = legs ?? new CardPartSlotSnapshot(PartSlot.Legs, null);
            Stats = stats ?? throw new ArgumentNullException(nameof(stats));
        }

        public string OwnerPlayerId { get; }
        public string CardId { get; }
        public long Revision { get; }
        public bool IsLockedForBattle { get; }
        public CardPartSlotSnapshot Head { get; }
        public CardPartSlotSnapshot Body { get; }
        public CardPartSlotSnapshot Legs { get; }
        public CardEffectiveStats Stats { get; }
        public bool IsUsable => Stats.MaximumHitPoints > 0 && Stats.EffectiveAttack >= 0;
    }

    public readonly struct CardAssemblyOperationResult
    {
        public CardAssemblyOperationResult(
            bool succeeded,
            CardAssemblyFailure failure,
            CardBuildSnapshot snapshot)
        {
            Succeeded = succeeded;
            Failure = failure;
            Snapshot = snapshot;
        }

        public bool Succeeded { get; }
        public CardAssemblyFailure Failure { get; }
        public CardBuildSnapshot Snapshot { get; }

        public static CardAssemblyOperationResult Success(CardBuildSnapshot snapshot) =>
            new CardAssemblyOperationResult(true, CardAssemblyFailure.None, snapshot);

        public static CardAssemblyOperationResult Reject(
            CardAssemblyFailure failure,
            CardBuildSnapshot snapshot = null) =>
            new CardAssemblyOperationResult(false, failure, snapshot);
    }

    public sealed class CardAssemblyEvent
    {
        public CardAssemblyEvent(
            CardAssemblyEventType type,
            string ownerPlayerId,
            string cardId,
            long revision,
            PartSlot? slot = null,
            string partInstanceId = null,
            CardAssemblyFailure failure = CardAssemblyFailure.None)
        {
            Type = type;
            OwnerPlayerId = ownerPlayerId ?? string.Empty;
            CardId = cardId ?? string.Empty;
            Revision = revision;
            Slot = slot;
            PartInstanceId = partInstanceId ?? string.Empty;
            Failure = failure;
        }

        public CardAssemblyEventType Type { get; }
        public string OwnerPlayerId { get; }
        public string CardId { get; }
        public long Revision { get; }
        public PartSlot? Slot { get; }
        public string PartInstanceId { get; }
        public CardAssemblyFailure Failure { get; }
    }
}
