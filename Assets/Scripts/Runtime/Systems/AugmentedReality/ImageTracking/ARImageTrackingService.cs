using System;
using System.Collections.Generic;
using UnityEngine;

namespace ApirApir.AugmentedReality
{
    public enum ARImageTrackingState : byte
    {
        Found,
        Tracking,
        Lost
    }

    public readonly struct ARTrackedPose
    {
        public ARTrackedPose(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }

        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
    }

    public readonly struct ARTrackedImageObservation
    {
        public ARTrackedImageObservation(
            string trackingTargetId,
            ARImageTrackingState state,
            ARTrackedPose pose)
        {
            TrackingTargetId = trackingTargetId == null ? string.Empty : trackingTargetId.Trim();
            State = state;
            Pose = pose;
        }

        public string TrackingTargetId { get; }
        public ARImageTrackingState State { get; }
        public ARTrackedPose Pose { get; }
    }

    /// <summary>
    /// Adapter for an image-tracking provider. A future AR Foundation adapter should translate
    /// tracked-image callbacks into these observations; QR detections must not be emitted here.
    /// </summary>
    public interface IARImageTrackingProvider : IDisposable
    {
        event Action<ARTrackedImageObservation> TrackingChanged;
        bool IsRunning { get; }
        bool TryStart(out string failureReason);
        void Pause();
        void Resume();
        void Stop();
    }

    /// <summary>Logic-test provider only. It performs no physical image recognition.</summary>
    public sealed class MockARImageTrackingProvider : IARImageTrackingProvider
    {
        public event Action<ARTrackedImageObservation> TrackingChanged;
        public bool IsRunning { get; private set; }

        public bool TryStart(out string failureReason)
        {
            IsRunning = true;
            failureReason = string.Empty;
            return true;
        }

        public void Submit(ARTrackedImageObservation observation)
        {
            if (IsRunning)
            {
                TrackingChanged?.Invoke(observation);
            }
        }

        public void Pause() { IsRunning = false; }
        public void Resume() { IsRunning = true; }
        public void Stop() { IsRunning = false; }
        public void Dispose() { Stop(); }
    }

    public sealed class ARImageTrackingService : IDisposable
    {
        private readonly IARImageTrackingProvider provider;
        private readonly Dictionary<string, RegisteredARCard> cardsByTarget =
            new Dictionary<string, RegisteredARCard>(StringComparer.Ordinal);
        private readonly HashSet<string> currentlyTrackedCards =
            new HashSet<string>(StringComparer.Ordinal);
        private bool subscribed;

        public ARImageTrackingService(IARImageTrackingProvider provider)
        {
            this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public event Action<RegisteredARCard, ARTrackedPose> CardTrackingFound;
        public event Action<RegisteredARCard, ARTrackedPose> CardPoseUpdated;
        public event Action<RegisteredARCard> CardTrackingLost;

        public bool TryTrackCard(RegisteredARCard card)
        {
            if (card == null || !card.IsActive || string.IsNullOrWhiteSpace(card.TrackingTargetId))
            {
                return false;
            }

            if (cardsByTarget.TryGetValue(card.TrackingTargetId, out RegisteredARCard previous) &&
                !string.Equals(previous.CardId, card.CardId, StringComparison.Ordinal) &&
                currentlyTrackedCards.Remove(previous.CardId))
            {
                CardTrackingLost?.Invoke(previous);
            }

            cardsByTarget[card.TrackingTargetId] = card;
            return true;
        }

        public void StopTrackingCard(string cardId)
        {
            string normalized = cardId == null ? string.Empty : cardId.Trim();
            string targetToRemove = null;
            foreach (KeyValuePair<string, RegisteredARCard> pair in cardsByTarget)
            {
                if (string.Equals(pair.Value.CardId, normalized, StringComparison.Ordinal))
                {
                    targetToRemove = pair.Key;
                    break;
                }
            }

            if (targetToRemove != null)
            {
                RegisteredARCard card = cardsByTarget[targetToRemove];
                cardsByTarget.Remove(targetToRemove);
                if (currentlyTrackedCards.Remove(card.CardId))
                {
                    CardTrackingLost?.Invoke(card);
                }
            }
        }

        public bool Start(out string failureReason)
        {
            if (!subscribed)
            {
                provider.TrackingChanged += HandleTrackingChanged;
                subscribed = true;
            }

            return provider.TryStart(out failureReason);
        }

        public void Pause() { provider.Pause(); }
        public void Resume() { provider.Resume(); }

        public void Stop()
        {
            provider.Stop();
            foreach (RegisteredARCard card in cardsByTarget.Values)
            {
                if (currentlyTrackedCards.Contains(card.CardId))
                {
                    CardTrackingLost?.Invoke(card);
                }
            }

            currentlyTrackedCards.Clear();
        }

        public void Dispose()
        {
            Stop();
            if (subscribed)
            {
                provider.TrackingChanged -= HandleTrackingChanged;
                subscribed = false;
            }

            provider.Dispose();
        }

        private void HandleTrackingChanged(ARTrackedImageObservation observation)
        {
            if (observation.TrackingTargetId.Length == 0 ||
                !cardsByTarget.TryGetValue(observation.TrackingTargetId, out RegisteredARCard card))
            {
                return;
            }

            if (observation.State == ARImageTrackingState.Lost)
            {
                if (currentlyTrackedCards.Remove(card.CardId))
                {
                    CardTrackingLost?.Invoke(card);
                }

                return;
            }

            if (currentlyTrackedCards.Add(card.CardId))
            {
                CardTrackingFound?.Invoke(card, observation.Pose);
            }
            else
            {
                CardPoseUpdated?.Invoke(card, observation.Pose);
            }
        }
    }
}
