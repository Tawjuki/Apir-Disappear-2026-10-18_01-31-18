using System;
using System.Collections.Generic;
using ApirApir.Battle;
using ApirApir.Cards;
using ApirApir.Crafting;
using UnityEngine;

namespace ApirApir.AugmentedReality
{
    [Serializable]
    public sealed class ARCardPartRecord
    {
        public string slot;
        public string definitionId;
        public string visualAssetId;
        public string rarity;
        public int occupiedRegions;
        public float offsetX;
        public float offsetY;
        public float scale = 1f;
        public int layerOrder;
    }

    [Serializable]
    public sealed class ARCardStatisticsRecord
    {
        public int maximumHitPoints;
        public int effectiveAttack;
        public float attackBonusPercent;
        public float speedBonusPercent;
        public float criticalChanceBonusPercent;
        public string[] passiveIds = Array.Empty<string>();
        public string[] modifierIds = Array.Empty<string>();
        public float[] modifierValues = Array.Empty<float>();
    }

    /// <summary>
    /// Stable, compact DTO saved to shared Cloud Save Game Data. It stores asset IDs rather than
    /// artwork bytes so every client reconstructs the same two-dimensional card from its catalog.
    /// </summary>
    [Serializable]
    public sealed class ARCardRecord
    {
        public const int CurrentSchemaVersion = 1;

        public int schemaVersion = CurrentSchemaVersion;
        public string cardId;
        public string ownerPlayerId;
        public string cardName;
        public string sourceAssembledCardId;
        public long sourceRevision;
        public long createdUnixMilliseconds;
        public ARCardPartRecord head;
        public ARCardPartRecord body;
        public ARCardPartRecord legs;
        public string[] selectedSkillIds = Array.Empty<string>();
        public ARCardStatisticsRecord statistics;

        public bool IsValidBasicShape()
        {
            if (schemaVersion != CurrentSchemaVersion ||
                !IsLowerHexCardId(cardId) ||
                !IsValidIdentifier(ownerPlayerId) ||
                string.IsNullOrWhiteSpace(cardName) || cardName.Length > ARCardRecordFactory.MaximumCardNameLength ||
                !IsValidIdentifier(sourceAssembledCardId) || sourceRevision < 0 ||
                createdUnixMilliseconds <= 0 || !IsValidStatistics(statistics) ||
                !AreValidSkills(selectedSkillIds))
            {
                return false;
            }

            return IsValidPart(head) && IsValidPart(body) && IsValidPart(legs);
        }

        private static bool IsValidPart(ARCardPartRecord part)
        {
            return part == null ||
                   (Enum.TryParse(part.slot, false, out PartSlot _) &&
                    IsValidIdentifier(part.definitionId) &&
                    IsValidIdentifier(part.visualAssetId) &&
                    (part.occupiedRegions & ~(int)(PartEquipmentRegion.Head | PartEquipmentRegion.Body |
                                                    PartEquipmentRegion.Legs)) == 0 &&
                    part.scale > 0f && !float.IsNaN(part.scale) && !float.IsInfinity(part.scale));
        }

        private static bool IsValidStatistics(ARCardStatisticsRecord value)
        {
            if (value == null || value.maximumHitPoints <= 0 || value.effectiveAttack < 0 ||
                !IsFinite(value.attackBonusPercent) || !IsFinite(value.speedBonusPercent) ||
                !IsFinite(value.criticalChanceBonusPercent) || value.passiveIds == null ||
                value.modifierIds == null || value.modifierValues == null ||
                value.modifierIds.Length != value.modifierValues.Length)
            {
                return false;
            }

            for (int i = 0; i < value.passiveIds.Length; i++)
                if (!IsValidIdentifier(value.passiveIds[i])) return false;
            for (int i = 0; i < value.modifierIds.Length; i++)
                if (!IsValidIdentifier(value.modifierIds[i]) || !IsFinite(value.modifierValues[i])) return false;
            return true;
        }

        private static bool AreValidSkills(IReadOnlyList<string> values)
        {
            if (values == null || values.Count > CardBattleLoadout.MaximumEquippedSkills) return false;
            var unique = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < values.Count; i++)
                if (!IsValidIdentifier(values[i]) || !unique.Add(values[i])) return false;
            return true;
        }

        private static bool IsValidIdentifier(string value) =>
            !string.IsNullOrWhiteSpace(value) &&
            value.Length <= CardRegistrationService.MaximumIdentifierLength;

        private static bool IsLowerHexCardId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 32) return false;
            for (int i = 0; i < value.Length; i++)
                if (!((value[i] >= '0' && value[i] <= '9') || (value[i] >= 'a' && value[i] <= 'f')))
                    return false;
            return true;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public sealed class ARCardRecordFactory
    {
        public const int MaximumCardNameLength = 32;

        private readonly CraftingCatalog craftingCatalog;
        private readonly SkillCatalog skillCatalog;

        public ARCardRecordFactory(CraftingCatalog craftingCatalog, SkillCatalog skillCatalog)
        {
            this.craftingCatalog = craftingCatalog ?? throw new ArgumentNullException(nameof(craftingCatalog));
            this.skillCatalog = skillCatalog ?? throw new ArgumentNullException(nameof(skillCatalog));
        }

        public bool TryCreate(
            CardBuildSnapshot assembledCard,
            string authenticatedPlayerId,
            string requestedName,
            IReadOnlyList<string> selectedSkillIds,
            out ARCardRecord record,
            out string error)
        {
            record = null;
            error = string.Empty;
            string owner = Normalize(authenticatedPlayerId);
            string cardName = Normalize(requestedName);
            if (assembledCard == null || owner.Length == 0 ||
                !string.Equals(owner, assembledCard.OwnerPlayerId, StringComparison.Ordinal) ||
                !assembledCard.IsUsable)
            {
                error = "The confirmed assembled card does not belong to the authenticated player.";
                return false;
            }

            if (cardName.Length == 0 || cardName.Length > MaximumCardNameLength)
            {
                error = $"Card name must contain 1-{MaximumCardNameLength} characters.";
                return false;
            }

            string[] skills = ValidateAndCopySkills(selectedSkillIds, out error);
            if (skills == null) return false;

            var created = new ARCardRecord
            {
                cardId = Guid.NewGuid().ToString("N"),
                ownerPlayerId = owner,
                cardName = cardName,
                sourceAssembledCardId = assembledCard.CardId,
                sourceRevision = assembledCard.Revision,
                createdUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                head = CreatePart(assembledCard.Head),
                body = CreatePart(assembledCard.Body),
                legs = CreatePart(assembledCard.Legs),
                selectedSkillIds = skills,
                statistics = CreateStatistics(assembledCard.Stats)
            };

            if (!created.IsValidBasicShape())
            {
                error = "The assembled card contains invalid or unresolved part data.";
                return false;
            }

            record = created;
            return true;
        }

        public bool TryCreateStandalone(
            StandaloneARCardComposition composition,
            string authenticatedPlayerId,
            string requestedName,
            IReadOnlyList<string> selectedSkillIds,
            out ARCardRecord record,
            out string error)
        {
            record = null;
            error = string.Empty;
            string owner = Normalize(authenticatedPlayerId);
            string cardName = Normalize(requestedName);
            if (composition == null || owner.Length == 0 ||
                !IsValidSourceId(composition.SourceId) || composition.Revision < 0)
            {
                error = "Authentication and a valid standalone AR card are required.";
                return false;
            }

            if (cardName.Length == 0 || cardName.Length > MaximumCardNameLength)
            {
                error = $"Card name must contain 1-{MaximumCardNameLength} characters.";
                return false;
            }

            string[] skills = ValidateAndCopySkills(selectedSkillIds, out error);
            if (skills == null) return false;

            var created = new ARCardRecord
            {
                cardId = Guid.NewGuid().ToString("N"),
                ownerPlayerId = owner,
                cardName = cardName,
                sourceAssembledCardId = composition.SourceId,
                sourceRevision = composition.Revision,
                createdUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                head = CreatePart(composition.Head),
                body = CreatePart(composition.Body),
                legs = CreatePart(composition.Legs),
                selectedSkillIds = skills,
                statistics = CreateStatistics(composition.Statistics)
            };

            if (!created.IsValidBasicShape())
            {
                error = "The standalone AR card contains invalid or unresolved content.";
                return false;
            }

            record = created;
            return true;
        }

        private string[] ValidateAndCopySkills(IReadOnlyList<string> source, out string error)
        {
            error = string.Empty;
            if (source == null || source.Count == 0) return Array.Empty<string>();
            if (source.Count > CardBattleLoadout.MaximumEquippedSkills)
            {
                error = $"A card supports at most {CardBattleLoadout.MaximumEquippedSkills} skills.";
                return null;
            }

            var result = new string[source.Count];
            var unique = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < result.Length; i++)
            {
                string id = Normalize(source[i]);
                if (!unique.Add(id) || !skillCatalog.TryGet(id, out _))
                {
                    error = "The selected skill list contains an unknown or duplicate skill.";
                    return null;
                }
                result[i] = id;
            }
            return result;
        }

        private ARCardPartRecord CreatePart(CardPartSlotSnapshot slot)
        {
            if (slot == null || slot.IsEmpty) return null;
            string rarity = string.Empty;
            if (craftingCatalog.TryGetPart(slot.DefinitionId, out CraftablePartDefinition definition))
                rarity = definition.Rarity.ToString();
            return new ARCardPartRecord
            {
                slot = slot.Slot.ToString(),
                definitionId = slot.DefinitionId,
                visualAssetId = slot.VisualAssetId,
                rarity = rarity,
                occupiedRegions = (int)slot.OccupiedRegions,
                offsetX = slot.VisualOffsetX,
                offsetY = slot.VisualOffsetY,
                scale = slot.VisualScale,
                layerOrder = slot.VisualLayerOrder
            };
        }

        private static ARCardPartRecord CreatePart(CraftablePartDefinition part)
        {
            if (part == null) return null;
            return new ARCardPartRecord
            {
                slot = part.Slot.ToString(),
                definitionId = part.Id,
                visualAssetId = part.VisualAssetId,
                rarity = part.Rarity.ToString(),
                occupiedRegions = (int)part.OccupiedRegions,
                offsetX = part.VisualOffsetX,
                offsetY = part.VisualOffsetY,
                scale = part.VisualScale,
                layerOrder = part.VisualLayerOrder
            };
        }

        private static ARCardStatisticsRecord CreateStatistics(CardEffectiveStats stats)
        {
            var modifierIds = new string[stats.AdditionalModifiers.Count];
            var modifierValues = new float[stats.AdditionalModifiers.Count];
            int index = 0;
            foreach (KeyValuePair<string, float> modifier in stats.AdditionalModifiers)
            {
                modifierIds[index] = modifier.Key;
                modifierValues[index] = modifier.Value;
                index++;
            }

            var passiveIds = new string[stats.ActivePassiveReferences.Count];
            for (int i = 0; i < passiveIds.Length; i++)
                passiveIds[i] = stats.ActivePassiveReferences[i];

            return new ARCardStatisticsRecord
            {
                maximumHitPoints = stats.MaximumHitPoints,
                effectiveAttack = stats.EffectiveAttack,
                attackBonusPercent = stats.AttackBonusPercent,
                speedBonusPercent = stats.SpeedBonusPercent,
                criticalChanceBonusPercent = stats.CriticalChanceBonusPercent,
                passiveIds = passiveIds,
                modifierIds = modifierIds,
                modifierValues = modifierValues
            };
        }

        private static bool IsValidSourceId(string value) =>
            !string.IsNullOrWhiteSpace(value) && value.Length <= CardRegistrationService.MaximumIdentifierLength;

        private static string Normalize(string value) => value == null ? string.Empty : value.Trim();
    }
}
