using System;
using System.Threading;
using System.Threading.Tasks;
using ApirApir.Multiplayer;
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
                await UnityPlayerAccountRuntime.InitializeAuthenticationAsync();
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
                EnsureCoordinator();

                if (initializationTask == null || initializationTask.IsCompleted)
                {
                    initializationTask = coordinator.InitializeAsync(cancellationToken);
                }

                return initializationTask;
            }
        }

        public static Task ClearSessionAsync(
            CancellationToken cancellationToken = default)
        {
            CloudPlayerProfileCoordinator current = Coordinator;
            return current == null
                ? Task.CompletedTask
                : current.ClearSessionAsync(cancellationToken);
        }

        internal static CloudPlayerProfileCoordinator GetOrCreateCoordinator()
        {
            lock (Sync)
            {
                EnsureCoordinator();
                return coordinator;
            }
        }

        private static void EnsureCoordinator()
        {
            if (coordinator == null)
            {
                coordinator = new CloudPlayerProfileCoordinator(
                    new UnityAuthenticationPlayerIdentity(),
                    new UnityCloudPlayerProfileStore());
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

    /// <summary>
    /// Public backend API for future login UI. It is the only production composition point for
    /// username/password authentication and the existing Cloud Profile session.
    /// </summary>
    public static class UnityPlayerAccountRuntime
    {
        private static readonly object Sync = new object();
        private static PlayerAccountCoordinator coordinator;

        public static PlayerAccountCoordinator Coordinator
        {
            get
            {
                lock (Sync)
                {
                    if (coordinator == null)
                    {
                        coordinator = new PlayerAccountCoordinator(
                            UnityAccountAuthenticationRuntime.Service,
                            UnityCloudPlayerProfileRuntime.GetOrCreateCoordinator());
                    }

                    return coordinator;
                }
            }
        }

        public static Task<PlayerAccountResult> InitializeAuthenticationAsync(
            CancellationToken cancellationToken = default)
        {
            return Coordinator.InitializeAuthenticationAsync(cancellationToken);
        }

        public static Task<PlayerAccountResult> RegisterAsync(
            string username,
            string password,
            CancellationToken cancellationToken = default)
        {
            return Coordinator.RegisterAsync(username, password, cancellationToken);
        }

        public static Task<PlayerAccountResult> LoginAsync(
            string username,
            string password,
            CancellationToken cancellationToken = default)
        {
            return Coordinator.LoginAsync(username, password, cancellationToken);
        }

        public static Task<PlayerAccountResult> LinkUsernamePasswordAsync(
            string username,
            string password,
            CancellationToken cancellationToken = default)
        {
            return Coordinator.LinkUsernamePasswordAsync(
                username,
                password,
                cancellationToken);
        }

        public static Task<PlayerAccountResult> ResumeAnonymousAsync(
            CancellationToken cancellationToken = default)
        {
            return Coordinator.ResumeAnonymousAsync(cancellationToken);
        }

        public static Task<PlayerAccountResult> LogoutAsync(
            bool clearCachedCredentials = false,
            bool confirmAnonymousCredentialLoss = false,
            CancellationToken cancellationToken = default)
        {
            return Coordinator.LogoutAsync(
                clearCachedCredentials,
                confirmAnonymousCredentialLoss,
                cancellationToken);
        }
    }
}
