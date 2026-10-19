#if APIRAPIR_ARFOUNDATION
using System;
using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using FoundationSessionState = UnityEngine.XR.ARFoundation.ARSessionState;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace ApirApir.AugmentedReality
{
    /// <summary>
    /// Official AR Foundation implementation of the platform session boundary.
    /// Assign the ARSession, XROrigin, and AR camera references in the Inspector.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class ARFoundationSessionAdapter : MonoBehaviour,
        IARPlatformSession,
        IARAsynchronousPlatformSession
    {
        [SerializeField] private ARSession arSession;
        [SerializeField] private XROrigin xrOrigin;
        [SerializeField] private ARCameraManager cameraManager;
        [SerializeField] private bool resetSessionOnStop = true;

        private Coroutine initializationRoutine;
        private bool initializationAttempted;
        private bool initialized;
        private bool disposed;

        public ARAvailability Availability
        {
            get
            {
                switch (ARSession.state)
                {
                    case FoundationSessionState.Ready:
                    case FoundationSessionState.SessionInitializing:
                    case FoundationSessionState.SessionTracking:
                        return ARAvailability.Available;
                    case FoundationSessionState.Unsupported:
                        return ARAvailability.Unsupported;
                    case FoundationSessionState.None:
                    case FoundationSessionState.CheckingAvailability:
                    case FoundationSessionState.NeedsInstall:
                    case FoundationSessionState.Installing:
                        return ARAvailability.Unknown;
                    default:
                        return ARAvailability.Unavailable;
                }
            }
        }

        public CameraPermissionState CameraPermission
        {
            get
            {
                if (HasCameraPermission())
                {
                    return CameraPermissionState.Granted;
                }

                return initializationAttempted
                    ? CameraPermissionState.Denied
                    : CameraPermissionState.Unknown;
            }
        }

        public void BeginInitialize(Action<bool, string> completion)
        {
            if (completion == null)
            {
                throw new ArgumentNullException(nameof(completion));
            }

            if (disposed)
            {
                completion(false, "The AR Foundation session adapter has been disposed.");
                return;
            }

            if (initializationRoutine != null)
            {
                completion(false, "AR Foundation initialization is already in progress.");
                return;
            }

            if (!TryValidateReferences(out string failureReason))
            {
                initializationAttempted = true;
                completion(false, failureReason);
                return;
            }

            initializationRoutine = StartCoroutine(InitializeRoutine(completion));
        }

        public bool TryInitialize(out string failureReason)
        {
            initializationAttempted = true;
            if (!TryValidateReferences(out failureReason))
            {
                return false;
            }

            if (Availability != ARAvailability.Available)
            {
                failureReason = "AR Foundation is not ready. Run the asynchronous availability check first.";
                return false;
            }

            if (!HasCameraPermission())
            {
                failureReason = "Camera permission has not been granted.";
                return false;
            }

            initialized = true;
            failureReason = string.Empty;
            return true;
        }

        public bool TryStart(out string failureReason)
        {
            if (!initialized && !TryInitialize(out failureReason))
            {
                return false;
            }

            if (!TryValidateReferences(out failureReason))
            {
                return false;
            }

            xrOrigin.enabled = true;
            cameraManager.enabled = true;
            arSession.enabled = true;
            failureReason = string.Empty;
            return true;
        }

        public void Pause()
        {
            if (disposed)
            {
                return;
            }

            if (cameraManager != null)
            {
                cameraManager.enabled = false;
            }

            if (arSession != null)
            {
                arSession.enabled = false;
            }
        }

        public void Resume()
        {
            if (disposed || !initialized)
            {
                return;
            }

            if (xrOrigin != null)
            {
                xrOrigin.enabled = true;
            }

            if (cameraManager != null)
            {
                cameraManager.enabled = true;
            }

            if (arSession != null)
            {
                arSession.enabled = true;
            }
        }

        public void Stop()
        {
            if (disposed)
            {
                return;
            }

            if (initializationRoutine != null)
            {
                StopCoroutine(initializationRoutine);
                initializationRoutine = null;
            }

            if (resetSessionOnStop && arSession != null && arSession.enabled)
            {
                arSession.Reset();
            }

            Pause();
            initialized = false;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            Stop();
            disposed = true;
        }

        private IEnumerator InitializeRoutine(Action<bool, string> completion)
        {
            initializationAttempted = true;
            yield return ARSession.CheckAvailability();

            if (ARSession.state == FoundationSessionState.NeedsInstall && arSession.attemptUpdate)
            {
                yield return ARSession.Install();
            }

            if (Availability != ARAvailability.Available)
            {
                CompleteInitialization(completion, false,
                    ARSession.state == FoundationSessionState.Unsupported
                        ? "This device does not support the configured AR Foundation provider."
                        : "The configured AR Foundation provider is unavailable or requires installation.");
                yield break;
            }

            yield return RequestCameraPermission();
            if (!HasCameraPermission())
            {
                CompleteInitialization(completion, false, "Camera permission was denied.");
                yield break;
            }

            initialized = true;
            CompleteInitialization(completion, true, string.Empty);
        }

        private IEnumerator RequestCameraPermission()
        {
            if (HasCameraPermission())
            {
                yield break;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            bool requestFinished = false;
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => requestFinished = true;
            callbacks.PermissionDenied += _ => requestFinished = true;
            callbacks.PermissionDeniedAndDontAskAgain += _ => requestFinished = true;
            Permission.RequestUserPermission(Permission.Camera, callbacks);
            while (!requestFinished)
            {
                yield return null;
            }
#else
            yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
#endif
        }

        private bool HasCameraPermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return Permission.HasUserAuthorizedPermission(Permission.Camera);
#else
            return Application.HasUserAuthorization(UserAuthorization.WebCam) ||
                   (cameraManager != null && cameraManager.permissionGranted);
#endif
        }

        private bool TryValidateReferences(out string failureReason)
        {
            if (arSession == null)
            {
                failureReason = "ARSession reference is not assigned.";
                return false;
            }

            if (xrOrigin == null)
            {
                failureReason = "XROrigin reference is not assigned.";
                return false;
            }

            if (cameraManager == null)
            {
                failureReason = "ARCameraManager reference is not assigned.";
                return false;
            }

            failureReason = string.Empty;
            return true;
        }

        private void CompleteInitialization(
            Action<bool, string> completion,
            bool succeeded,
            string failureReason)
        {
            initializationRoutine = null;
            if (!disposed)
            {
                completion(succeeded, failureReason);
            }
        }

        private void OnDestroy()
        {
            Dispose();
        }
    }
}
#endif
