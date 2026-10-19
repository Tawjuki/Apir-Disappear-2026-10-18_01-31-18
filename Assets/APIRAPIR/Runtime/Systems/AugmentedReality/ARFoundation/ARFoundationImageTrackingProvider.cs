#if APIRAPIR_ARFOUNDATION
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ApirApir.AugmentedReality
{
    /// <summary>
    /// Translates AR Foundation tracked-image changes into provider-neutral card observations.
    /// Reference image names must match RegisteredARCard.TrackingTargetId values.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ARFoundationImageTrackingProvider : MonoBehaviour, IARImageTrackingProvider
    {
        [SerializeField] private ARTrackedImageManager trackedImageManager;

        private readonly Dictionary<TrackableId, string> targetIdsByTrackable =
            new Dictionary<TrackableId, string>();
        private bool subscribed;
        private bool disposed;

        public event Action<ARTrackedImageObservation> TrackingChanged;

        public bool IsRunning =>
            !disposed && trackedImageManager != null && trackedImageManager.enabled;

        public bool TryStart(out string failureReason)
        {
            if (disposed)
            {
                failureReason = "The AR Foundation image tracking provider has been disposed.";
                return false;
            }

            if (trackedImageManager == null)
            {
                failureReason = "ARTrackedImageManager reference is not assigned.";
                return false;
            }

            if (trackedImageManager.referenceLibrary == null)
            {
                failureReason = "ARTrackedImageManager has no reference image library.";
                return false;
            }

            Subscribe();
            trackedImageManager.enabled = true;
            failureReason = string.Empty;
            return true;
        }

        public void Pause()
        {
            if (!disposed && trackedImageManager != null)
            {
                trackedImageManager.enabled = false;
            }
        }

        public void Resume()
        {
            if (disposed || trackedImageManager == null ||
                trackedImageManager.referenceLibrary == null)
            {
                return;
            }

            Subscribe();
            trackedImageManager.enabled = true;
        }

        public void Stop()
        {
            if (trackedImageManager != null)
            {
                trackedImageManager.enabled = false;
            }

            Unsubscribe();
            targetIdsByTrackable.Clear();
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

        private void Subscribe()
        {
            if (subscribed)
            {
                return;
            }

            trackedImageManager.trackablesChanged += HandleTrackablesChanged;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || trackedImageManager == null)
            {
                subscribed = false;
                return;
            }

            trackedImageManager.trackablesChanged -= HandleTrackablesChanged;
            subscribed = false;
        }

        private void HandleTrackablesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> changes)
        {
            foreach (ARTrackedImage trackedImage in changes.added)
            {
                Emit(trackedImage, true);
            }

            foreach (ARTrackedImage trackedImage in changes.updated)
            {
                Emit(trackedImage, false);
            }

            foreach (KeyValuePair<TrackableId, ARTrackedImage> removed in changes.removed)
            {
                string targetId = ResolveTargetId(removed.Key, removed.Value);
                targetIdsByTrackable.Remove(removed.Key);
                if (targetId.Length > 0)
                {
                    TrackingChanged?.Invoke(new ARTrackedImageObservation(
                        targetId,
                        ARImageTrackingState.Lost,
                        ToPose(removed.Value)));
                }
            }
        }

        private void Emit(ARTrackedImage trackedImage, bool wasAdded)
        {
            if (trackedImage == null)
            {
                return;
            }

            string targetId = NormalizeTargetId(trackedImage.referenceImage.name);
            if (targetId.Length == 0)
            {
                return;
            }

            targetIdsByTrackable[trackedImage.trackableId] = targetId;
            ARImageTrackingState state;
            if (trackedImage.trackingState == TrackingState.None)
            {
                state = ARImageTrackingState.Lost;
            }
            else
            {
                state = wasAdded ? ARImageTrackingState.Found : ARImageTrackingState.Tracking;
            }

            TrackingChanged?.Invoke(new ARTrackedImageObservation(targetId, state, ToPose(trackedImage)));
        }

        private string ResolveTargetId(TrackableId trackableId, ARTrackedImage trackedImage)
        {
            if (trackedImage != null)
            {
                string targetId = NormalizeTargetId(trackedImage.referenceImage.name);
                if (targetId.Length > 0)
                {
                    return targetId;
                }
            }

            return targetIdsByTrackable.TryGetValue(trackableId, out string cachedTargetId)
                ? cachedTargetId
                : string.Empty;
        }

        private static ARTrackedPose ToPose(ARTrackedImage trackedImage)
        {
            return trackedImage == null
                ? new ARTrackedPose(Vector3.zero, Quaternion.identity)
                : new ARTrackedPose(trackedImage.transform.position, trackedImage.transform.rotation);
        }

        private static string NormalizeTargetId(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }

        private void OnDestroy()
        {
            Dispose();
        }
    }
}
#endif
