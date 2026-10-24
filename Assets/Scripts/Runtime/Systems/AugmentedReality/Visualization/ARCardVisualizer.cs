using System;
using System.Collections.Generic;
using UnityEngine;

namespace ApirApir.AugmentedReality
{
    public sealed class ARVisualizationSettings
    {
        public ARVisualizationSettings(Vector3 positionOffset, float scale = 1f, bool hideWhenTrackingLost = true)
        {
            if (scale <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(scale));
            }

            PositionOffset = positionOffset;
            Scale = scale;
            HideWhenTrackingLost = hideWhenTrackingLost;
        }

        public Vector3 PositionOffset { get; }
        public float Scale { get; }
        public bool HideWhenTrackingLost { get; }
    }

    /// <summary>
    /// Resolves locally installed layered 2D assets or an optional 3D prefab from stable IDs.
    /// Implementations should cache loaded assets and return false when any required layer is absent.
    /// </summary>
    public interface IARCardAssetResolver
    {
        bool TryCreateVisual(RegisteredARCard card, out IARCardVisualInstance visual);
    }

    public enum ARVisualAssetKind : byte
    {
        Layered2D,
        Prefab3D
    }

    public sealed class ARVisualAssetReference
    {
        public ARVisualAssetReference(string visualAssetId, string contentKey, ARVisualAssetKind kind)
        {
            if (string.IsNullOrWhiteSpace(visualAssetId) || string.IsNullOrWhiteSpace(contentKey))
            {
                throw new ArgumentException("Visual asset and content keys are required.");
            }

            VisualAssetId = visualAssetId.Trim();
            ContentKey = contentKey.Trim();
            Kind = kind;
        }

        public string VisualAssetId { get; }
        public string ContentKey { get; }
        public ARVisualAssetKind Kind { get; }
    }

    /// <summary>
    /// Unity-specific factories may interpret layered references as Sprite layers or a 3D
    /// reference as a prefab. This interface keeps those choices out of registry records.
    /// </summary>
    public interface IARCardVisualFactory
    {
        bool TryCreate(
            RegisteredARCard card,
            ARVisualAssetReference head,
            ARVisualAssetReference body,
            ARVisualAssetReference legs,
            out IARCardVisualInstance visual);
    }

    /// <summary>
    /// In-memory content catalog for stable visual IDs. References are resolved once when a
    /// visual is created; the supplied factory owns actual Unity asset loading and caching.
    /// </summary>
    public sealed class ConfigurableARCardAssetResolver : IARCardAssetResolver
    {
        private readonly Dictionary<string, ARVisualAssetReference> references =
            new Dictionary<string, ARVisualAssetReference>(StringComparer.Ordinal);
        private readonly IARCardVisualFactory factory;

        public ConfigurableARCardAssetResolver(IARCardVisualFactory factory)
        {
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public void Register(ARVisualAssetReference reference)
        {
            if (reference == null)
            {
                throw new ArgumentNullException(nameof(reference));
            }

            if (references.ContainsKey(reference.VisualAssetId))
            {
                throw new InvalidOperationException(
                    "Duplicate AR visual asset ID: " + reference.VisualAssetId);
            }

            references.Add(reference.VisualAssetId, reference);
        }

        public bool TryCreateVisual(RegisteredARCard card, out IARCardVisualInstance visual)
        {
            visual = null;
            if (card == null || card.Configuration == null ||
                !TryResolve(card.Configuration.HeadVisualAssetId, out ARVisualAssetReference head) ||
                !TryResolve(card.Configuration.BodyVisualAssetId, out ARVisualAssetReference body) ||
                !TryResolve(card.Configuration.LegsVisualAssetId, out ARVisualAssetReference legs))
            {
                return false;
            }

            return factory.TryCreate(card, head, body, legs, out visual) && visual != null;
        }

        private bool TryResolve(string id, out ARVisualAssetReference reference)
        {
            reference = null;
            return string.IsNullOrEmpty(id) || references.TryGetValue(id, out reference);
        }
    }

    public interface IARCardVisualInstance : IDisposable
    {
        string CardId { get; }
        void ApplyPose(Vector3 position, Quaternion rotation, float scale);
        void Show();
        void Hide();
        void OnTrackingAcquired();
        void OnTrackingLost();
    }

    public sealed class ARCardVisualizer : IDisposable
    {
        private readonly CardRegistrationService registrations;
        private readonly IARCardAssetResolver assets;
        private readonly ARVisualizationSettings settings;
        private readonly Dictionary<string, IARCardVisualInstance> visuals =
            new Dictionary<string, IARCardVisualInstance>(StringComparer.Ordinal);
        private bool disposed;

        public ARCardVisualizer(
            CardRegistrationService registrations,
            IARCardAssetResolver assets,
            ARVisualizationSettings settings = null)
        {
            this.registrations = registrations ?? throw new ArgumentNullException(nameof(registrations));
            this.assets = assets ?? throw new ArgumentNullException(nameof(assets));
            this.settings = settings ?? new ARVisualizationSettings(Vector3.zero);
            this.registrations.CardRevoked += HandleCardRevoked;
        }

        public event Action<RegisteredARCard> ARCharacterCreated;
        public event Action<string> ARCharacterRemoved;
        public event Action<string> ARCharacterCreationFailed;

        public int ActiveVisualCount => visuals.Count;

        public bool HandleTrackingFound(RegisteredARCard observedCard, ARTrackedPose pose)
        {
            if (disposed || observedCard == null)
            {
                return false;
            }

            // Always revalidate on acquisition/recovery. Pose updates do not perform network lookups.
            ARCardOperationResult validation = registrations.Validate(observedCard.CardId);
            if (!validation.Succeeded || validation.Card == null)
            {
                Remove(observedCard.CardId);
                return false;
            }

            RegisteredARCard current = validation.Card;
            if (!visuals.TryGetValue(current.CardId, out IARCardVisualInstance visual))
            {
                if (!assets.TryCreateVisual(current, out visual) || visual == null ||
                    !string.Equals(visual.CardId, current.CardId, StringComparison.Ordinal))
                {
                    visual?.Dispose();
                    ARCharacterCreationFailed?.Invoke(current.CardId);
                    return false;
                }

                visuals.Add(current.CardId, visual);
                ARCharacterCreated?.Invoke(current);
            }

            ApplyPose(visual, pose);
            visual.Show();
            visual.OnTrackingAcquired();
            return true;
        }

        public void UpdateTrackedPose(RegisteredARCard card, ARTrackedPose pose)
        {
            if (!disposed && card != null && visuals.TryGetValue(card.CardId, out IARCardVisualInstance visual))
            {
                ApplyPose(visual, pose);
            }
        }

        public void HandleTrackingLost(RegisteredARCard card)
        {
            if (disposed || card == null || !visuals.TryGetValue(card.CardId, out IARCardVisualInstance visual))
            {
                return;
            }

            visual.OnTrackingLost();
            if (settings.HideWhenTrackingLost)
            {
                visual.Hide();
            }
            else
            {
                Remove(card.CardId);
            }
        }

        public void RevalidateActiveCards()
        {
            if (disposed || visuals.Count == 0)
            {
                return;
            }

            var ids = new List<string>(visuals.Keys);
            for (int i = 0; i < ids.Count; i++)
            {
                if (!registrations.Validate(ids[i]).Succeeded)
                {
                    Remove(ids[i]);
                }
            }
        }

        public void Clear()
        {
            if (visuals.Count == 0)
            {
                return;
            }

            var ids = new List<string>(visuals.Keys);
            for (int i = 0; i < ids.Count; i++)
            {
                Remove(ids[i]);
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            registrations.CardRevoked -= HandleCardRevoked;
            Clear();
            disposed = true;
        }

        private void ApplyPose(IARCardVisualInstance visual, ARTrackedPose pose)
        {
            Vector3 position = pose.Position + pose.Rotation * settings.PositionOffset;
            visual.ApplyPose(position, pose.Rotation, settings.Scale);
        }

        private void HandleCardRevoked(RegisteredARCard card)
        {
            if (card != null)
            {
                Remove(card.CardId);
            }
        }

        private void Remove(string cardId)
        {
            if (!visuals.TryGetValue(cardId ?? string.Empty, out IARCardVisualInstance visual))
            {
                return;
            }

            visuals.Remove(cardId);
            visual.Dispose();
            ARCharacterRemoved?.Invoke(cardId);
        }
    }
}
