using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ApirApir.PlayerProfiles
{
    /// <summary>
    /// Process-wide entry point for the local authenticated account. It creates no scene objects
    /// and exposes state/events for a future first-time-name UI.
    /// </summary>
    public static class UnityCloudPlayerProfileRuntime
    {
        private static readonly object Sync = new object();
        private static CloudPlayerProfileCoordinator coordinator;
        private static Task<CloudProfileCommandStatus> initializationTask;

        public static CloudPlayerProfileCoordinator Coordinator
        {
            get
            {
                lock (Sync)
                {
                    return coordinator;
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static async void InitializeOnApplicationStart()
        {
            try
            {
                await InitializeAsync();
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "APIR APIR cloud profile initialization failed: " + exception.Message);
            }
        }

        public static Task<CloudProfileCommandStatus> InitializeAsync(
            CancellationToken cancellationToken = default)
        {
            lock (Sync)
            {
                if (coordinator == null)
                {
                    coordinator = new CloudPlayerProfileCoordinator(
                        new UnityAuthenticationPlayerIdentity(),
                        new UnityCloudPlayerProfileStore());
                }

                if (initializationTask == null || initializationTask.IsCompleted)
                {
                    initializationTask = coordinator.InitializeAsync(cancellationToken);
                }

                return initializationTask;
            }
        }

        public static Task<CloudProfileCommandStatus> CreateFirstProfileAsync(
            string requestedName,
            CancellationToken cancellationToken = default)
        {
            CloudPlayerProfileCoordinator current = Coordinator;
            return current == null
                ? Task.FromResult(CloudProfileCommandStatus.NotReady)
                : current.CreateProfileAsync(requestedName, cancellationToken);
        }

        public static void AttachCompletedMatchSource(ICompletedMatchResultSource source)
        {
            CloudPlayerProfileCoordinator current = Coordinator;
            if (current == null)
            {
                throw new InvalidOperationException(
                    "Initialize the cloud profile runtime before attaching match results.");
            }

            current.Attach(source);
        }

        public static void DetachCompletedMatchSource()
        {
            Coordinator?.Detach();
        }
    }
}
