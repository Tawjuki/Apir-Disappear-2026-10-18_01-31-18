using System;
using System.Collections.Generic;
using ApirApir.AugmentedReality;
using ApirApir.Battle;
using ApirApir.Crafting;

namespace ApirApir.Cards
{
    /// <summary>
    /// Owns only equipment associations. Owned part instances and their Maintenance remain in
    /// the authoritative inventory, while battle runtime HP/effects remain in Battle.
    /// </summary>
    public sealed class AuthoritativeCardAssemblyService : ICardBattleLoadoutSource
    {
        private sealed class CardBuild
        {
            public CardBuild(string ownerPlayerId, string cardId, CardEffectiveStats stats)
            {
                OwnerPlayerId = ownerPlayerId;
                CardId = cardId;
                Stats = stats;
            }

            public string OwnerPlayerId { get; }
            public string CardId { get; }
            public long Revision { get; set; }
            public bool IsLockedForBattle { get; set; }
            public Dictionary<PartSlot, OwnedCardPart> Parts { get; } =
                new Dictionary<PartSlot, OwnedCardPart>();
            public HashSet<string> BattleHitPointInstances { get; } =
                new HashSet<string>(StringComparer.Ordinal);
            public CardEffectiveStats Stats { get; set; }
        }

        private readonly IOwnedCardPartInventoryProvider inventories;
        private readonly ICardAssemblyAccessPolicy accessPolicy;
        private readonly ICardAssemblyAuthority authority;
        private readonly ICardSkillLoadoutSource skills;
        private readonly AssemblyCardStatCalculator statCalculator;
        private readonly Dictionary<string, CardBuild> builds =
            new Dictionary<string, CardBuild>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> partLocations =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> processedCommands =
            new HashSet<string>(StringComparer.Ordinal);

        public AuthoritativeCardAssemblyService(
            IOwnedCardPartInventoryProvider inventories,
            ICardAssemblyAccessPolicy accessPolicy,
            ICardAssemblyAuthority authority,
            ICardSkillLoadoutSource skills = null,
            AssemblyCardStatCalculator statCalculator = null)
        {
            this.inventories = inventories ?? throw new ArgumentNullException(nameof(inventories));
            this.accessPolicy = accessPolicy ?? throw new ArgumentNullException(nameof(accessPolicy));
            this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
            this.skills = skills ?? new EmptyCardSkillLoadoutSource();
            this.statCalculator = statCalculator ?? new AssemblyCardStatCalculator();
        }

        public event Action<CardAssemblyEvent> EventPublished;
        public event Action<CardBuildSnapshot> CardConfigurationChanged;

        public CardAssemblyOperationResult InitializeCard(string ownerPlayerId, string cardId)
        {
            string owner = Normalize(ownerPlayerId);
            string card = Normalize(cardId);
            if (!authority.IsAuthoritative)
            {
                return Reject(CardAssemblyFailure.NotAuthoritative, owner, card, null);
            }

            if (owner.Length == 0 || card.Length == 0)
            {
                return Reject(CardAssemblyFailure.InvalidRequest, owner, card, null);
            }

            string key = GetBuildKey(owner, card);
            if (builds.TryGetValue(key, out CardBuild existing))
            {
                return CardAssemblyOperationResult.Success(CreateSnapshot(existing));
            }

            var build = new CardBuild(owner, card, statCalculator.Calculate(Array.Empty<OwnedCardPart>()));
            builds.Add(key, build);
            Publish(build, CardAssemblyEventType.CardInitialized);
            CardBuildSnapshot snapshot = CreateSnapshot(build);
            CardConfigurationChanged?.Invoke(snapshot);
            return CardAssemblyOperationResult.Success(snapshot);
        }

        public CardAssemblyOperationResult EquipHead(
            string playerId, string cardId, string partInstanceId, long expectedRevision, string commandId) =>
            EquipPart(playerId, cardId, PartSlot.Head, partInstanceId, expectedRevision, commandId);

        public CardAssemblyOperationResult EquipBody(
            string playerId, string cardId, string partInstanceId, long expectedRevision, string commandId) =>
            EquipPart(playerId, cardId, PartSlot.Body, partInstanceId, expectedRevision, commandId);

        public CardAssemblyOperationResult EquipLegs(
            string playerId, string cardId, string partInstanceId, long expectedRevision, string commandId) =>
            EquipPart(playerId, cardId, PartSlot.Legs, partInstanceId, expectedRevision, commandId);

        public CardAssemblyOperationResult ReplacePart(
            string playerId,
            string cardId,
            PartSlot slot,
            string partInstanceId,
            long expectedRevision,
            string commandId) =>
            EquipPart(playerId, cardId, slot, partInstanceId, expectedRevision, commandId);

        public CardAssemblyOperationResult EquipPart(
            string playerId,
            string cardId,
            PartSlot slot,
            string partInstanceId,
            long expectedRevision,
            string commandId)
        {
            string owner = Normalize(playerId);
            string card = Normalize(cardId);
            string instanceId = Normalize(partInstanceId);
            string command = Normalize(commandId);
            if (!TryStartCommand(owner, card, command, out CardBuild build, out CardAssemblyOperationResult rejected))
            {
                return rejected;
            }

            if (!Enum.IsDefined(typeof(PartSlot), slot) || instanceId.Length == 0)
            {
                return Reject(CardAssemblyFailure.InvalidRequest, owner, card, build);
            }

            if (!CanMutate(build, owner, expectedRevision, out CardAssemblyFailure failure))
            {
                return Reject(failure, owner, card, build, slot, instanceId);
            }

            if (!inventories.TryGetInventory(owner, out IOwnedCardPartInventory inventory))
            {
                return Reject(CardAssemblyFailure.InventoryNotFound, owner, card, build, slot, instanceId);
            }

            if (!inventory.TryGetOwnedPart(instanceId, out OwnedCardPart part) || part == null)
            {
                return Reject(CardAssemblyFailure.PartNotFound, owner, card, build, slot, instanceId);
            }

            if (!string.Equals(part.OwnerPlayerId, owner, StringComparison.Ordinal))
            {
                return Reject(CardAssemblyFailure.PartNotOwned, owner, card, build, slot, instanceId);
            }

            if (part.Definition.Slot != slot)
            {
                return Reject(CardAssemblyFailure.WrongSlot, owner, card, build, slot, instanceId);
            }

            string key = GetBuildKey(owner, card);
            if (partLocations.TryGetValue(instanceId, out string occupiedAt))
            {
                return Reject(CardAssemblyFailure.AlreadyEquipped, owner, card, build, slot, instanceId);
            }

            if (part.IsEquipped)
            {
                return Reject(CardAssemblyFailure.AlreadyEquipped, owner, card, build, slot, instanceId);
            }

            var replaced = new List<OwnedCardPart>();
            var keysToRemove = new List<PartSlot>();
            foreach (KeyValuePair<PartSlot, OwnedCardPart> entry in build.Parts)
            {
                if ((entry.Value.Definition.OccupiedRegions & part.Definition.OccupiedRegions) != 0)
                {
                    keysToRemove.Add(entry.Key);
                    replaced.Add(entry.Value);
                }
            }

            for (int i = 0; i < keysToRemove.Count; i++)
            {
                OwnedCardPart previous = build.Parts[keysToRemove[i]];
                build.Parts.Remove(keysToRemove[i]);
                previous.SetEquipped(false);
                partLocations.Remove(previous.InstanceId);
            }

            build.Parts[slot] = part;
            part.SetEquipped(true);
            partLocations.Add(instanceId, key);
            build.Revision++;
            Recalculate(build);

            Publish(build, replaced.Count == 0 ? GetEquippedEvent(slot) : CardAssemblyEventType.PartReplaced,
                slot, instanceId);
            PublishChanged(build);
            return CardAssemblyOperationResult.Success(CreateSnapshot(build));
        }

        public CardAssemblyOperationResult UnequipHead(
            string playerId, string cardId, long expectedRevision, string commandId) =>
            UnequipPart(playerId, cardId, PartSlot.Head, expectedRevision, commandId);

        public CardAssemblyOperationResult UnequipBody(
            string playerId, string cardId, long expectedRevision, string commandId) =>
            UnequipPart(playerId, cardId, PartSlot.Body, expectedRevision, commandId);

        public CardAssemblyOperationResult UnequipLegs(
            string playerId, string cardId, long expectedRevision, string commandId) =>
            UnequipPart(playerId, cardId, PartSlot.Legs, expectedRevision, commandId);

        public CardAssemblyOperationResult UnequipPart(
            string playerId,
            string cardId,
            PartSlot slot,
            long expectedRevision,
            string commandId)
        {
            string owner = Normalize(playerId);
            string card = Normalize(cardId);
            string command = Normalize(commandId);
            if (!TryStartCommand(owner, card, command, out CardBuild build, out CardAssemblyOperationResult rejected))
            {
                return rejected;
            }

            if (!Enum.IsDefined(typeof(PartSlot), slot))
            {
                return Reject(CardAssemblyFailure.InvalidRequest, owner, card, build);
            }

            if (!CanMutate(build, owner, expectedRevision, out CardAssemblyFailure failure))
            {
                return Reject(failure, owner, card, build, slot);
            }

            PartSlot primarySlot = slot;
            if (!build.Parts.TryGetValue(primarySlot, out OwnedCardPart part))
            {
                part = null;
                foreach (KeyValuePair<PartSlot, OwnedCardPart> entry in build.Parts)
                {
                    if (entry.Value.Definition.Occupies(slot))
                    {
                        primarySlot = entry.Key;
                        part = entry.Value;
                        break;
                    }
                }

                if (part == null)
                {
                    return CardAssemblyOperationResult.Success(CreateSnapshot(build));
                }
            }

            build.Parts.Remove(primarySlot);
            partLocations.Remove(part.InstanceId);
            part.SetEquipped(false);
            build.Revision++;
            Recalculate(build);
            Publish(build, CardAssemblyEventType.PartUnequipped, slot, part.InstanceId);
            PublishChanged(build);
            return CardAssemblyOperationResult.Success(CreateSnapshot(build));
        }

        public bool TryGetCardBuild(string ownerPlayerId, string cardId, out CardBuildSnapshot snapshot)
        {
            if (TryFindBuild(ownerPlayerId, cardId, out CardBuild build))
            {
                snapshot = CreateSnapshot(build);
                return true;
            }

            snapshot = null;
            return false;
        }

        public IReadOnlyList<OwnedCardPart> GetEquippedParts(string ownerPlayerId, string cardId)
        {
            return TryFindBuild(ownerPlayerId, cardId, out CardBuild build)
                ? CopyParts(build)
                : Array.Empty<OwnedCardPart>();
        }

        public bool ValidateCardBuild(string ownerPlayerId, string cardId)
        {
            if (!TryFindBuild(ownerPlayerId, cardId, out CardBuild build) || build.Parts.Count > 3)
            {
                return false;
            }

            var instances = new HashSet<string>(StringComparer.Ordinal);
            PartEquipmentRegion occupiedRegions = PartEquipmentRegion.None;
            foreach (KeyValuePair<PartSlot, OwnedCardPart> entry in build.Parts)
            {
                OwnedCardPart part = entry.Value;
                if (part == null || part.Definition == null || part.Definition.Slot != entry.Key ||
                    !string.Equals(part.OwnerPlayerId, build.OwnerPlayerId, StringComparison.Ordinal) ||
                    !instances.Add(part.InstanceId) || !part.IsEquipped ||
                    (occupiedRegions & part.Definition.OccupiedRegions) != 0)
                {
                    return false;
                }

                occupiedRegions |= part.Definition.OccupiedRegions;
            }

            return true;
        }

        public CardAssemblyOperationResult RecalculateCardStats(string ownerPlayerId, string cardId)
        {
            if (!authority.IsAuthoritative)
            {
                return CardAssemblyOperationResult.Reject(CardAssemblyFailure.NotAuthoritative);
            }

            if (!TryFindBuild(ownerPlayerId, cardId, out CardBuild build))
            {
                return CardAssemblyOperationResult.Reject(CardAssemblyFailure.CardNotFound);
            }

            build.Revision++;
            Recalculate(build);
            Publish(build, CardAssemblyEventType.CardStatisticsRecalculated);
            CardBuildSnapshot snapshot = CreateSnapshot(build);
            CardConfigurationChanged?.Invoke(snapshot);
            return CardAssemblyOperationResult.Success(snapshot);
        }

        public CardAssemblyOperationResult LockForBattle(string ownerPlayerId, string cardId)
        {
            if (!authority.IsAuthoritative)
            {
                return CardAssemblyOperationResult.Reject(CardAssemblyFailure.NotAuthoritative);
            }

            if (!TryFindBuild(ownerPlayerId, cardId, out CardBuild build))
            {
                return CardAssemblyOperationResult.Reject(CardAssemblyFailure.CardNotFound);
            }

            if (!ValidateCardBuild(ownerPlayerId, cardId))
            {
                return Reject(CardAssemblyFailure.InvalidBuild, build.OwnerPlayerId, build.CardId, build);
            }

            if (build.IsLockedForBattle)
            {
                return CardAssemblyOperationResult.Success(CreateSnapshot(build));
            }

            build.BattleHitPointInstances.Clear();
            foreach (OwnedCardPart part in build.Parts.Values)
            {
                if (part.CurrentMaintenance > 0)
                {
                    build.BattleHitPointInstances.Add(part.InstanceId);
                }
            }

            build.IsLockedForBattle = true;
            build.Revision++;
            Recalculate(build);
            Publish(build, CardAssemblyEventType.CardBuildLockedForBattle);
            PublishChanged(build);
            return CardAssemblyOperationResult.Success(CreateSnapshot(build));
        }

        public CardAssemblyOperationResult UnlockForPreparation(string ownerPlayerId, string cardId)
        {
            if (!authority.IsAuthoritative)
            {
                return CardAssemblyOperationResult.Reject(CardAssemblyFailure.NotAuthoritative);
            }

            if (!TryFindBuild(ownerPlayerId, cardId, out CardBuild build))
            {
                return CardAssemblyOperationResult.Reject(CardAssemblyFailure.CardNotFound);
            }

            if (!build.IsLockedForBattle)
            {
                return CardAssemblyOperationResult.Success(CreateSnapshot(build));
            }

            build.IsLockedForBattle = false;
            build.BattleHitPointInstances.Clear();
            build.Revision++;
            Recalculate(build);
            Publish(build, CardAssemblyEventType.CardBuildUnlockedForPreparation);
            PublishChanged(build);
            return CardAssemblyOperationResult.Success(CreateSnapshot(build));
        }

        public CardAssemblyOperationResult SynchronizeBattleResult(BattleResult result, string ownerPlayerId)
        {
            if (!authority.IsAuthoritative || result == null)
            {
                return CardAssemblyOperationResult.Reject(
                    result == null ? CardAssemblyFailure.InvalidRequest : CardAssemblyFailure.NotAuthoritative);
            }

            string owner = Normalize(ownerPlayerId);
            FighterBattleResult fighter = string.Equals(result.FirstFighter.PlayerId, owner, StringComparison.Ordinal)
                ? result.FirstFighter
                : string.Equals(result.SecondFighter.PlayerId, owner, StringComparison.Ordinal)
                    ? result.SecondFighter
                    : null;
            CardBuild build = null;
            if (fighter == null || !TryFindBuild(owner, fighter.CardId, out build) ||
                !build.IsLockedForBattle || fighter.PartConditions.Count != build.Parts.Count)
            {
                return Reject(CardAssemblyFailure.BattleResultMismatch, owner,
                    fighter == null ? string.Empty : fighter.CardId, build);
            }

            var updates = new List<KeyValuePair<OwnedCardPart, int>>(fighter.PartConditions.Count);
            for (int i = 0; i < fighter.PartConditions.Count; i++)
            {
                PartConditionResult condition = fighter.PartConditions[i];
                if (!build.Parts.TryGetValue(condition.Slot, out OwnedCardPart part) ||
                    !string.Equals(part.Definition.Id, condition.PartId, StringComparison.Ordinal) ||
                    condition.Maintenance < 0 || condition.Maintenance > part.Definition.MaximumMaintenance)
                {
                    return Reject(CardAssemblyFailure.BattleResultMismatch, owner, fighter.CardId, build);
                }

                updates.Add(new KeyValuePair<OwnedCardPart, int>(part, condition.Maintenance));
            }

            for (int i = 0; i < updates.Count; i++)
            {
                updates[i].Key.TrySetMaintenance(updates[i].Value);
            }

            build.Revision++;
            Recalculate(build);
            Publish(build, CardAssemblyEventType.BattleConditionSynchronized);
            PublishChanged(build);
            return CardAssemblyOperationResult.Success(CreateSnapshot(build));
        }

        public bool TryGetLoadout(
            string ownerPlayerId,
            string assembledCardId,
            out CardBattleLoadout loadout)
        {
            loadout = null;
            if (!TryFindBuild(ownerPlayerId, assembledCardId, out CardBuild build) ||
                !ValidateCardBuild(ownerPlayerId, assembledCardId) ||
                !skills.TryGetSkills(build.OwnerPlayerId, out IReadOnlyList<SkillDefinition> selectedSkills) ||
                selectedSkills == null || selectedSkills.Count > CardBattleLoadout.MaximumEquippedSkills)
            {
                return false;
            }

            IReadOnlyList<OwnedCardPart> ownedParts = CopyParts(build);
            var battleParts = new EquippedPart[ownedParts.Count];
            for (int i = 0; i < battleParts.Length; i++)
            {
                battleParts[i] = ownedParts[i].ToBattlePart();
            }

            try
            {
                loadout = new CardBattleLoadout(
                    build.OwnerPlayerId,
                    build.CardId,
                    battleParts,
                    selectedSkills,
                    AssemblyCardStatCalculator.BlankCardHitPoints,
                    AssemblyCardStatCalculator.BlankCardAttack);
                return true;
            }
            catch (ArgumentException)
            {
                loadout = null;
                return false;
            }
        }

        private bool TryStartCommand(
            string owner,
            string card,
            string command,
            out CardBuild build,
            out CardAssemblyOperationResult rejected)
        {
            build = null;
            rejected = default;
            if (!authority.IsAuthoritative)
            {
                rejected = Reject(CardAssemblyFailure.NotAuthoritative, owner, card, null);
                return false;
            }

            if (owner.Length == 0 || card.Length == 0 || command.Length == 0)
            {
                rejected = Reject(CardAssemblyFailure.InvalidRequest, owner, card, null);
                return false;
            }

            if (!TryFindBuild(owner, card, out build))
            {
                rejected = Reject(CardAssemblyFailure.CardNotFound, owner, card, null);
                return false;
            }

            if (!processedCommands.Add(owner + "\n" + command))
            {
                rejected = Reject(CardAssemblyFailure.DuplicateCommand, owner, card, build);
                return false;
            }

            return true;
        }

        private bool CanMutate(
            CardBuild build,
            string owner,
            long expectedRevision,
            out CardAssemblyFailure failure)
        {
            if (!string.Equals(build.OwnerPlayerId, owner, StringComparison.Ordinal))
            {
                failure = CardAssemblyFailure.PartNotOwned;
                return false;
            }

            if (build.IsLockedForBattle || !accessPolicy.CanModifyEquipment(owner))
            {
                failure = CardAssemblyFailure.EquipmentLocked;
                return false;
            }

            if (expectedRevision != build.Revision)
            {
                failure = CardAssemblyFailure.StaleRevision;
                return false;
            }

            failure = CardAssemblyFailure.None;
            return true;
        }

        private bool TryFindBuild(string ownerPlayerId, string cardId, out CardBuild build)
        {
            return builds.TryGetValue(
                GetBuildKey(Normalize(ownerPlayerId), Normalize(cardId)), out build);
        }

        private void Recalculate(CardBuild build)
        {
            build.Stats = statCalculator.Calculate(
                CopyParts(build),
                build.IsLockedForBattle ? build.BattleHitPointInstances : null);
        }

        private static IReadOnlyList<OwnedCardPart> CopyParts(CardBuild build)
        {
            var result = new List<OwnedCardPart>(build.Parts.Count);
            AddIfPresent(build, PartSlot.Head, result);
            AddIfPresent(build, PartSlot.Body, result);
            AddIfPresent(build, PartSlot.Legs, result);
            return result.AsReadOnly();
        }

        private static void AddIfPresent(
            CardBuild build,
            PartSlot slot,
            ICollection<OwnedCardPart> destination)
        {
            if (build.Parts.TryGetValue(slot, out OwnedCardPart part))
            {
                destination.Add(part);
            }
        }

        private static CardBuildSnapshot CreateSnapshot(CardBuild build)
        {
            build.Parts.TryGetValue(PartSlot.Head, out OwnedCardPart head);
            build.Parts.TryGetValue(PartSlot.Body, out OwnedCardPart body);
            build.Parts.TryGetValue(PartSlot.Legs, out OwnedCardPart legs);
            return new CardBuildSnapshot(
                build.OwnerPlayerId,
                build.CardId,
                build.Revision,
                build.IsLockedForBattle,
                new CardPartSlotSnapshot(PartSlot.Head, head),
                new CardPartSlotSnapshot(PartSlot.Body, body),
                new CardPartSlotSnapshot(PartSlot.Legs, legs),
                build.Stats);
        }

        private void PublishChanged(CardBuild build)
        {
            Publish(build, CardAssemblyEventType.CardStatisticsRecalculated);
            Publish(build, CardAssemblyEventType.CardConfigurationChanged);
            CardConfigurationChanged?.Invoke(CreateSnapshot(build));
        }

        private void Publish(
            CardBuild build,
            CardAssemblyEventType type,
            PartSlot? slot = null,
            string instanceId = null)
        {
            EventPublished?.Invoke(new CardAssemblyEvent(
                type, build.OwnerPlayerId, build.CardId, build.Revision, slot, instanceId));
        }

        private CardAssemblyOperationResult Reject(
            CardAssemblyFailure failure,
            string owner,
            string card,
            CardBuild build,
            PartSlot? slot = null,
            string instanceId = null)
        {
            EventPublished?.Invoke(new CardAssemblyEvent(
                CardAssemblyEventType.InvalidEquipmentRequestRejected,
                owner,
                card,
                build == null ? 0 : build.Revision,
                slot,
                instanceId,
                failure));
            return CardAssemblyOperationResult.Reject(
                failure, build == null ? null : CreateSnapshot(build));
        }

        private static CardAssemblyEventType GetEquippedEvent(PartSlot slot)
        {
            switch (slot)
            {
                case PartSlot.Head: return CardAssemblyEventType.HeadEquipped;
                case PartSlot.Body: return CardAssemblyEventType.BodyEquipped;
                case PartSlot.Legs: return CardAssemblyEventType.LegsEquipped;
                default: throw new ArgumentOutOfRangeException(nameof(slot));
            }
        }

        private static string GetBuildKey(string owner, string card) => owner + "\n" + card;
        private static string Normalize(string value) => value == null ? string.Empty : value.Trim();
    }
}
