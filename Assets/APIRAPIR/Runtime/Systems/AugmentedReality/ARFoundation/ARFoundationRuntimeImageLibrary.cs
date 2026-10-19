#if APIRAPIR_ARFOUNDATION
using System;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ApirApir.AugmentedReality
{
    /// <summary>
    /// Owns AR Foundation runtime image-library creation and guarded image registration.
    /// Runtime mutability depends on the active platform provider and device capabilities.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ARFoundationRuntimeImageLibrary : MonoBehaviour
    {
        [SerializeField] private ARTrackedImageManager trackedImageManager;
        [SerializeField] private XRReferenceImageLibrary initialReferenceLibrary;

        private RuntimeReferenceImageLibrary runtimeLibrary;
        private MutableRuntimeReferenceImageLibrary mutableLibrary;

        public bool IsInitialized => runtimeLibrary != null;
        public bool SupportsRuntimeImageRegistration => mutableLibrary != null;

        public bool TryInitialize(out string failureReason)
        {
            if (trackedImageManager == null)
            {
                failureReason = "ARTrackedImageManager reference is not assigned.";
                return false;
            }

            if (runtimeLibrary != null)
            {
                failureReason = string.Empty;
                return true;
            }

            try
            {
                runtimeLibrary = trackedImageManager.CreateRuntimeLibrary(initialReferenceLibrary);
                if (runtimeLibrary == null)
                {
                    failureReason = "The active image-tracking subsystem did not create a runtime library.";
                    return false;
                }

                trackedImageManager.referenceLibrary = runtimeLibrary;
                mutableLibrary = runtimeLibrary as MutableRuntimeReferenceImageLibrary;
                failureReason = string.Empty;
                return true;
            }
            catch (NotSupportedException exception)
            {
                runtimeLibrary = null;
                mutableLibrary = null;
                failureReason = "The active AR provider does not support image tracking: " + exception.Message;
                return false;
            }
            catch (InvalidOperationException exception)
            {
                runtimeLibrary = null;
                mutableLibrary = null;
                failureReason = "The AR image-tracking subsystem is not ready: " + exception.Message;
                return false;
            }
        }

        public bool TryScheduleImageRegistration(
            Texture2D sourceImage,
            string trackingTargetId,
            float physicalWidthMeters,
            out AddReferenceImageJobState jobState,
            out string failureReason)
        {
            jobState = default;
            string normalizedTargetId = trackingTargetId == null
                ? string.Empty
                : trackingTargetId.Trim();

            if (sourceImage == null)
            {
                failureReason = "A source image is required.";
                return false;
            }

            if (normalizedTargetId.Length == 0)
            {
                failureReason = "A non-empty tracking target ID is required.";
                return false;
            }

            if (physicalWidthMeters <= 0f || float.IsNaN(physicalWidthMeters) ||
                float.IsInfinity(physicalWidthMeters))
            {
                failureReason = "Physical image width must be a finite value greater than zero.";
                return false;
            }

            if (!sourceImage.isReadable)
            {
                failureReason = "The source image texture must have Read/Write enabled.";
                return false;
            }

            if (runtimeLibrary == null && !TryInitialize(out failureReason))
            {
                return false;
            }

            if (mutableLibrary == null)
            {
                failureReason =
                    "The active AR image-tracking subsystem does not support mutable runtime libraries.";
                return false;
            }

            if (!mutableLibrary.IsTextureFormatSupported(sourceImage.format))
            {
                failureReason = "The active AR provider does not support this texture format.";
                return false;
            }

            try
            {
                jobState = mutableLibrary.ScheduleAddImageWithValidationJob(
                    sourceImage,
                    normalizedTargetId,
                    physicalWidthMeters);
                failureReason = string.Empty;
                return true;
            }
            catch (ArgumentException exception)
            {
                failureReason = "The image could not be scheduled for registration: " + exception.Message;
                return false;
            }
            catch (InvalidOperationException exception)
            {
                failureReason = "Runtime image registration is unavailable: " + exception.Message;
                return false;
            }
        }
    }
}
#endif
