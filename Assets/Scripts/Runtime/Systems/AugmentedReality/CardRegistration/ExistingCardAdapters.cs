using System;
using ApirApir.Battle;
using ApirApir.PlayerProfiles;

namespace ApirApir.AugmentedReality
{
    /// <summary>Read-only bridge to whichever service owns assembled battle loadouts.</summary>
    public interface ICardBattleLoadoutSource
    {
        bool TryGetLoadout(string ownerPlayerId, string assembledCardId, out CardBattleLoadout loadout);
    }

    /// <summary>
    /// Maps a stable part definition ID to a locally installed 2D layer or optional 3D asset ID.
    /// The actual asset loading remains the visualization resolver's responsibility.
    /// </summary>
    public interface IARPartVisualIdResolver
    {
        bool TryGetVisualAssetId(string partDefinitionId, out string visualAssetId);
    }

    /// <summary>
    /// Default logical mapping for projects where visual assets use the same stable ID as their
    /// part definition. It does not claim that an asset is currently installed.
    /// </summary>
    public sealed class DefinitionIdARPartVisualResolver : IARPartVisualIdResolver
    {
        public bool TryGetVisualAssetId(string partDefinitionId, out string visualAssetId)
        {
            visualAssetId = partDefinitionId == null ? string.Empty : partDefinitionId.Trim();
            return visualAssetId.Length > 0;
        }
    }

    public sealed class BattleLoadoutARCardSnapshotProvider : IAssembledCardSnapshotProvider
    {
        private readonly ICardBattleLoadoutSource loadouts;
        private readonly IARPartVisualIdResolver visuals;

        public BattleLoadoutARCardSnapshotProvider(
            ICardBattleLoadoutSource loadouts,
            IARPartVisualIdResolver visuals = null)
        {
            this.loadouts = loadouts ?? throw new ArgumentNullException(nameof(loadouts));
            this.visuals = visuals ?? new DefinitionIdARPartVisualResolver();
        }

        public bool TryGetSnapshot(
            string authenticatedOwnerPlayerId,
            string assembledCardId,
            out ARCardConfigurationSnapshot configuration)
        {
            configuration = null;
            if (!loadouts.TryGetLoadout(
                    authenticatedOwnerPlayerId, assembledCardId, out CardBattleLoadout loadout) ||
                loadout == null ||
                !string.Equals(loadout.PlayerId, authenticatedOwnerPlayerId, StringComparison.Ordinal) ||
                !string.Equals(loadout.CardId, assembledCardId, StringComparison.Ordinal))
            {
                return false;
            }

            string headId = string.Empty;
            string headVisualId = string.Empty;
            string bodyId = string.Empty;
            string bodyVisualId = string.Empty;
            string legsId = string.Empty;
            string legsVisualId = string.Empty;

            for (int i = 0; i < loadout.Parts.Count; i++)
            {
                EquippedPart part = loadout.Parts[i];
                if (part == null || part.Definition == null ||
                    !visuals.TryGetVisualAssetId(part.Definition.Id, out string visualId) ||
                    string.IsNullOrWhiteSpace(visualId))
                {
                    return false;
                }

                switch (part.Definition.Slot)
                {
                    case PartSlot.Head:
                        headId = part.Definition.Id;
                        headVisualId = visualId;
                        break;
                    case PartSlot.Body:
                        bodyId = part.Definition.Id;
                        bodyVisualId = visualId;
                        break;
                    case PartSlot.Legs:
                        legsId = part.Definition.Id;
                        legsVisualId = visualId;
                        break;
                    default:
                        return false;
                }
            }

            configuration = new ARCardConfigurationSnapshot(
                headId, headVisualId, bodyId, bodyVisualId, legsId, legsVisualId);
            return configuration.HasAnyPart && configuration.HasConsistentVisualReferences;
        }
    }

    /// <summary>
    /// Reuses persistent profiles. The full stable player ID suffix guarantees uniqueness even
    /// when display names match; applications may replace this with a hosted public-tag service.
    /// </summary>
    public sealed class PlayerProfileARTagProvider : IPlayerTagProvider
    {
        private readonly PlayerProfileService profiles;

        public PlayerProfileARTagProvider(PlayerProfileService profiles)
        {
            this.profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        }

        public bool TryGetPlayerTag(string playerId, out string playerTag)
        {
            playerTag = string.Empty;
            if (!profiles.TryGetProfile(playerId, out PlayerProfileSnapshot profile) || profile == null)
            {
                return false;
            }

            playerTag = profile.PlayerName + "#" + profile.PlayerId;
            return true;
        }
    }
}
