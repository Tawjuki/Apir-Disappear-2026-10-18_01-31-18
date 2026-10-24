using System;
using ApirApir.AugmentedReality;

namespace ApirApir.Cards
{
    /// <summary>
    /// Read-only AR registration adapter. Registered Card IDs remain owned by the AR registry;
    /// this adapter exports only the authenticated owner's assembled definition/visual IDs.
    /// </summary>
    public sealed class CardAssemblyARSnapshotProvider : IAssembledCardSnapshotProvider
    {
        private readonly AuthoritativeCardAssemblyService cards;

        public CardAssemblyARSnapshotProvider(AuthoritativeCardAssemblyService cards)
        {
            this.cards = cards ?? throw new ArgumentNullException(nameof(cards));
        }

        public bool TryGetSnapshot(
            string authenticatedOwnerPlayerId,
            string assembledCardId,
            out ARCardConfigurationSnapshot configuration)
        {
            configuration = null;
            if (!cards.TryGetCardBuild(
                    authenticatedOwnerPlayerId, assembledCardId, out CardBuildSnapshot build) ||
                build == null ||
                !string.Equals(
                    build.OwnerPlayerId, authenticatedOwnerPlayerId, StringComparison.Ordinal))
            {
                return false;
            }

            configuration = new ARCardConfigurationSnapshot(
                build.Head.DefinitionId,
                build.Head.VisualAssetId,
                build.Body.DefinitionId,
                build.Body.VisualAssetId,
                build.Legs.DefinitionId,
                build.Legs.VisualAssetId);
            return configuration.HasAnyPart && configuration.HasConsistentVisualReferences;
        }
    }
}
