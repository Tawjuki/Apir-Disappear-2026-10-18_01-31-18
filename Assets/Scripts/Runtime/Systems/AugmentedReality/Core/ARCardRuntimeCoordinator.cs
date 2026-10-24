using System;

namespace ApirApir.AugmentedReality
{
    /// <summary>Wires identification, physical tracking, visualization, and revocation events.</summary>
    public sealed class ARCardRuntimeCoordinator : IDisposable
    {
        private readonly ARCardIdentificationService identification;
        private readonly ARImageTrackingService tracking;
        private readonly ARCardVisualizer visualizer;
        private readonly CardRegistrationService registrations;
        private bool disposed;

        public ARCardRuntimeCoordinator(
            ARCardIdentificationService identification,
            ARImageTrackingService tracking,
            ARCardVisualizer visualizer,
            CardRegistrationService registrations)
        {
            this.identification = identification ?? throw new ArgumentNullException(nameof(identification));
            this.tracking = tracking ?? throw new ArgumentNullException(nameof(tracking));
            this.visualizer = visualizer ?? throw new ArgumentNullException(nameof(visualizer));
            this.registrations = registrations ?? throw new ArgumentNullException(nameof(registrations));

            identification.CardIdentificationSucceeded += HandleIdentificationSucceeded;
            tracking.CardTrackingFound += HandleTrackingFound;
            tracking.CardPoseUpdated += visualizer.UpdateTrackedPose;
            tracking.CardTrackingLost += visualizer.HandleTrackingLost;
            registrations.CardRevoked += HandleCardRevoked;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            identification.CardIdentificationSucceeded -= HandleIdentificationSucceeded;
            tracking.CardTrackingFound -= HandleTrackingFound;
            tracking.CardPoseUpdated -= visualizer.UpdateTrackedPose;
            tracking.CardTrackingLost -= visualizer.HandleTrackingLost;
            registrations.CardRevoked -= HandleCardRevoked;
            visualizer.Clear();
            disposed = true;
        }

        private void HandleIdentificationSucceeded(RegisteredARCard card)
        {
            tracking.TryTrackCard(card);
        }

        private void HandleTrackingFound(RegisteredARCard card, ARTrackedPose pose)
        {
            visualizer.HandleTrackingFound(card, pose);
        }

        private void HandleCardRevoked(RegisteredARCard card)
        {
            if (card != null)
            {
                tracking.StopTrackingCard(card.CardId);
            }
        }
    }
}
