using System;
using System.Collections.Generic;
using ApirApir.Battle;
using UnityEngine;

namespace ApirApir.AugmentedReality
{
    [Serializable]
    public sealed class ARCardSpriteBinding
    {
        public string visualAssetId;
        public Sprite sprite;
    }

    [DisallowMultipleComponent]
    public sealed class ARCardSpriteResolver : MonoBehaviour
    {
        [SerializeField] private ARCardSpriteBinding[] bindings = Array.Empty<ARCardSpriteBinding>();
        private Dictionary<string, Sprite> lookup;

        public Sprite Resolve(string visualAssetId)
        {
            if (lookup == null)
            {
                lookup = new Dictionary<string, Sprite>(StringComparer.Ordinal);
                for (int i = 0; i < bindings.Length; i++)
                {
                    ARCardSpriteBinding value = bindings[i];
                    if (value != null && value.sprite != null && !string.IsNullOrWhiteSpace(value.visualAssetId))
                        lookup[value.visualAssetId.Trim()] = value.sprite;
                }
            }

            string canonicalId = DefaultPartDefinitionCatalog.NormalizeId(visualAssetId);
            return canonicalId.Length > 0 && lookup.TryGetValue(canonicalId, out Sprite sprite)
                ? sprite
                : null;
        }
    }
}
