using System;

namespace ApirApir.AugmentedReality
{
    public enum ARAvailability : byte
    {
        Unknown,
        Available,
        Unavailable,
        Unsupported
    }

    public enum CameraPermissionState : byte
    {
        Unknown,
        Granted,
        Denied
    }

    public enum ARSessionState : byte
    {
        Idle,
        Initializing,
        Initialized,
        Running,
        Paused,
        Stopped,
        Failed
    }

    /// <summary>Platform adapter. The production implementation is backed by Unity AR Foundation.</summary>
    public interface IARPlatformSession : IDisposable
    {
        ARAvailability Availability { get; }
        CameraPermissionState CameraPermission { get; }
        bool TryInitialize(out string failureReason);
        bool TryStart(out string failureReason);
        void Pause();
        void Resume();
        void Stop();
    }

    /// <summary>
    /// Optional extension for providers whose availability and camera permission checks are asynchronous.
    /// Unity AR Foundation uses this path because its availability check is a coroutine.
    /// </summary>
    public interface IARAsynchronousPlatformSession
    {
        void BeginInitialize(Action<bool, string> completion);
    }

    public sealed class ARSessionController : IDisposable
    {
        private readonly IARPlatformSession platform;
        private bool disposed;

        public ARSessionController(IARPlatformSession platform)
        {
            this.platform = platform ?? throw new ArgumentNullException(nameof(platform));
        }

        public event Action ARInitialized;
        public event Action<string> ARInitializationFailed;
        public event Action ARSessionStarted;
        public event Action ARSessionPaused;
        public event Action ARSessionResumed;
        public event Action ARSessionStopped;

        public ARSessionState State { get; private set; } = ARSessionState.Idle;

        public bool Initialize()
        {
            ThrowIfDisposed();
            if (State == ARSessionState.Initializing || State == ARSessionState.Initialized ||
                State == ARSessionState.Running ||
                State == ARSessionState.Paused)
            {
                return true;
            }

            if (platform.Availability == ARAvailability.Unsupported ||
                platform.Availability == ARAvailability.Unavailable)
            {
                return Fail("AR is not available on this platform or its provider is not configured.");
            }

            if (platform.CameraPermission == CameraPermissionState.Denied)
            {
                return Fail("Camera permission has not been granted.");
            }

            if ((platform.Availability == ARAvailability.Unknown ||
                 platform.CameraPermission == CameraPermissionState.Unknown) &&
                platform is IARAsynchronousPlatformSession asynchronousPlatform)
            {
                State = ARSessionState.Initializing;
                asynchronousPlatform.BeginInitialize(CompleteAsynchronousInitialization);
                return true;
            }

            if (platform.Availability != ARAvailability.Available)
            {
                return Fail("AR availability could not be determined.");
            }

            if (platform.CameraPermission != CameraPermissionState.Granted)
            {
                return Fail("Camera permission status could not be determined.");
            }

            if (!platform.TryInitialize(out string reason))
            {
                return Fail(string.IsNullOrWhiteSpace(reason) ? "AR initialization failed." : reason);
            }

            State = ARSessionState.Initialized;
            ARInitialized?.Invoke();
            return true;
        }

        public bool Start()
        {
            ThrowIfDisposed();
            if (State == ARSessionState.Running)
            {
                return true;
            }

            if (State == ARSessionState.Initializing)
            {
                return false;
            }

            if (State != ARSessionState.Initialized && !Initialize())
            {
                return false;
            }

            if (State != ARSessionState.Initialized)
            {
                return false;
            }

            if (!platform.TryStart(out string reason))
            {
                return Fail(string.IsNullOrWhiteSpace(reason) ? "AR session failed to start." : reason);
            }

            State = ARSessionState.Running;
            ARSessionStarted?.Invoke();
            return true;
        }

        public void Pause()
        {
            ThrowIfDisposed();
            if (State != ARSessionState.Running)
            {
                return;
            }

            platform.Pause();
            State = ARSessionState.Paused;
            ARSessionPaused?.Invoke();
        }

        public void Resume()
        {
            ThrowIfDisposed();
            if (State != ARSessionState.Paused)
            {
                return;
            }

            platform.Resume();
            State = ARSessionState.Running;
            ARSessionResumed?.Invoke();
        }

        public void Stop()
        {
            if (disposed || State == ARSessionState.Stopped || State == ARSessionState.Idle)
            {
                return;
            }

            platform.Stop();
            State = ARSessionState.Stopped;
            ARSessionStopped?.Invoke();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            Stop();
            platform.Dispose();
            disposed = true;
        }

        private bool Fail(string reason)
        {
            State = ARSessionState.Failed;
            ARInitializationFailed?.Invoke(reason);
            return false;
        }

        private void CompleteAsynchronousInitialization(bool succeeded, string failureReason)
        {
            if (disposed || State != ARSessionState.Initializing)
            {
                return;
            }

            if (!succeeded)
            {
                Fail(string.IsNullOrWhiteSpace(failureReason)
                    ? "AR initialization failed."
                    : failureReason);
                return;
            }

            State = ARSessionState.Initialized;
            ARInitialized?.Invoke();
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(ARSessionController));
            }
        }
    }

    /// <summary>Logic-test provider only. It does not access a camera or perform physical AR.</summary>
    public sealed class MockARPlatformSession : IARPlatformSession
    {
        public MockARPlatformSession(
            ARAvailability availability = ARAvailability.Available,
            CameraPermissionState permission = CameraPermissionState.Granted)
        {
            Availability = availability;
            CameraPermission = permission;
        }

        public ARAvailability Availability { get; set; }
        public CameraPermissionState CameraPermission { get; set; }
        public bool InitializationSucceeds { get; set; } = true;
        public bool StartSucceeds { get; set; } = true;
        public bool IsRunning { get; private set; }

        public bool TryInitialize(out string failureReason)
        {
            failureReason = InitializationSucceeds ? string.Empty : "Simulated initialization failure.";
            return InitializationSucceeds;
        }

        public bool TryStart(out string failureReason)
        {
            IsRunning = StartSucceeds;
            failureReason = StartSucceeds ? string.Empty : "Simulated start failure.";
            return StartSucceeds;
        }

        public void Pause() { IsRunning = false; }
        public void Resume() { IsRunning = true; }
        public void Stop() { IsRunning = false; }
        public void Dispose() { IsRunning = false; }
    }
}
