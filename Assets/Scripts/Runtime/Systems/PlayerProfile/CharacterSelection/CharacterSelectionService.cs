using System;
using System.Collections.Generic;

namespace ApirApir.PlayerProfiles
{
    /// <summary>Validates stable character IDs without owning or instantiating visual assets.</summary>
    public sealed class CharacterSelectionService
    {
        public const int MaximumCharacterIdLength = 128;

        private readonly ICharacterCatalog catalog;

        public CharacterSelectionService(ICharacterCatalog catalog)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public bool TryValidate(string requestedCharacterId, out string characterId)
        {
            characterId = requestedCharacterId == null ? string.Empty : requestedCharacterId.Trim();
            return characterId.Length > 0 &&
                   characterId.Length <= MaximumCharacterIdLength &&
                   catalog.Contains(characterId);
        }
    }

    /// <summary>
    /// Lightweight catalog for projects that already expose stable character IDs in code.
    /// It intentionally contains no built-in character choices.
    /// </summary>
    public sealed class CharacterIdCatalog : ICharacterCatalog
    {
        private readonly HashSet<string> characterIds;

        public CharacterIdCatalog(IEnumerable<string> characterIds)
        {
            this.characterIds = new HashSet<string>(StringComparer.Ordinal);
            if (characterIds == null)
            {
                return;
            }

            foreach (string characterId in characterIds)
            {
                if (!string.IsNullOrWhiteSpace(characterId))
                {
                    this.characterIds.Add(characterId.Trim());
                }
            }
        }

        public bool Contains(string characterId)
        {
            return !string.IsNullOrWhiteSpace(characterId) && characterIds.Contains(characterId);
        }
    }
}
