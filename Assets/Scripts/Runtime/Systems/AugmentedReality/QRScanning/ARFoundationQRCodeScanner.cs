#if APIRAPIR_ARFOUNDATION
using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ApirApir.AugmentedReality
{
    /// <summary>
    /// Throttled CPU-image scanner. It shares AR Foundation's camera stream and never starts a
    /// competing WebCamTexture. XRCpuImage and temporary native memory are always disposed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ARFoundationQRCodeScanner : MonoBehaviour, IQRCodeScanner
    {
        [SerializeField] private ARCameraManager cameraManager;
        [SerializeField, Min(0.1f)] private float decodeIntervalSeconds = 0.35f;
        [SerializeField, Range(0.1f, 1f)] private float imageScale = 0.5f;

        private readonly ZXingQRCodeCodec codec = new ZXingQRCodeCodec();
        private byte[] rgbaBuffer = Array.Empty<byte>();
        private float nextDecodeTime;

        public event Action<string> QRCodeDetected;
        public bool IsRunning { get; private set; }

        public void Configure(ARCameraManager manager, float intervalSeconds = 0.35f)
        {
            if (IsRunning) Stop();
            cameraManager = manager;
            decodeIntervalSeconds = Mathf.Max(0.1f, intervalSeconds);
        }

        public bool TryStart(out string failureReason)
        {
            if (cameraManager == null)
            {
                failureReason = "ARCameraManager reference is not assigned.";
                return false;
            }

            if (!IsRunning)
            {
                cameraManager.frameReceived += HandleCameraFrame;
                IsRunning = true;
                nextDecodeTime = 0f;
            }
            failureReason = string.Empty;
            return true;
        }

        public void Stop()
        {
            if (cameraManager != null) cameraManager.frameReceived -= HandleCameraFrame;
            IsRunning = false;
        }

        public void Dispose()
        {
            Stop();
            rgbaBuffer = Array.Empty<byte>();
        }

        private void HandleCameraFrame(ARCameraFrameEventArgs _)
        {
            if (!IsRunning || Time.unscaledTime < nextDecodeTime) return;
            nextDecodeTime = Time.unscaledTime + decodeIntervalSeconds;
            if (!cameraManager.TryAcquireLatestCpuImage(out XRCpuImage image)) return;

            try
            {
                int width = Mathf.Max(96, Mathf.RoundToInt(image.width * imageScale));
                int height = Mathf.Max(96, Mathf.RoundToInt(image.height * imageScale));
                var conversion = new XRCpuImage.ConversionParams
                {
                    inputRect = new RectInt(0, 0, image.width, image.height),
                    outputDimensions = new Vector2Int(width, height),
                    outputFormat = TextureFormat.RGBA32,
                    transformation = XRCpuImage.Transformation.MirrorY
                };
                int required = conversion.outputDimensions.x * conversion.outputDimensions.y * 4;
                if (rgbaBuffer.Length != required) rgbaBuffer = new byte[required];
                using (var native = new NativeArray<byte>(required, Allocator.Temp, NativeArrayOptions.UninitializedMemory))
                {
                    image.Convert(conversion, native);
                    native.CopyTo(rgbaBuffer);
                }

                if (codec.TryDecodeRgba32(rgbaBuffer,
                        conversion.outputDimensions.x, conversion.outputDimensions.y, out string cardId))
                {
                    QRCodeDetected?.Invoke(new QRCodeService().CreatePayload(cardId));
                }
            }
            catch (InvalidOperationException)
            {
                // A frame can become unavailable during Android pause/resume; the next frame retries.
            }
            finally
            {
                image.Dispose();
            }
        }

        private void OnDisable() => Stop();
        private void OnDestroy() => Dispose();
    }
}
#endif
