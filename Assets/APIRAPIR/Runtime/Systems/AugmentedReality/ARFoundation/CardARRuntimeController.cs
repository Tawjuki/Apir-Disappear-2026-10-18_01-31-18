#if APIRAPIR_ARFOUNDATION
using System;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ApirApir.AugmentedReality
{
    [DisallowMultipleComponent]
    public sealed class CardARSceneController : MonoBehaviour
    {
        [SerializeField] private ARFoundationSessionAdapter sessionAdapter;
        [SerializeField] private ARFoundationQRCodeScanner scanner;
        [SerializeField] private ARCardDisplayView displayView;
        [SerializeField] private ARCardSpriteResolver spriteResolver;
        [SerializeField] private TMP_Text trackingStatusText;
        [SerializeField] private Button backButton;
        [SerializeField, Min(0.5f)] private float trackingLostTimeoutSeconds = 1.5f;
        [SerializeField] private string returnSceneName = "Lobby";

        private readonly QRCodeService payloads = new QRCodeService();
        private readonly UnitySharedARCardStore store = new UnitySharedARCardStore();
        private CancellationTokenSource lifetime;
        private string currentCardId = string.Empty;
        private float lastDetectionTime = float.NegativeInfinity;
        private bool loading;
        private bool visible;

        public void Configure(
            ARFoundationSessionAdapter session,
            ARFoundationQRCodeScanner qrScanner,
            ARCardDisplayView view,
            ARCardSpriteResolver sprites,
            TMP_Text status,
            Button back)
        {
            sessionAdapter = session;
            scanner = qrScanner;
            displayView = view;
            spriteResolver = sprites;
            trackingStatusText = status;
            backButton = back;
        }

        private void OnEnable()
        {
            lifetime = new CancellationTokenSource();
            if (backButton != null) backButton.onClick.AddListener(ReturnToLobby);
            if (scanner != null) scanner.QRCodeDetected += HandleDetected;
            displayView?.Hide();
            SetStatus("Starting AR camera…");
            if (sessionAdapter == null)
            {
                SetStatus("AR session is not configured.");
                return;
            }
            sessionAdapter.BeginInitialize(HandleInitialized);
        }

        private void OnDisable()
        {
            if (backButton != null) backButton.onClick.RemoveListener(ReturnToLobby);
            if (scanner != null) scanner.QRCodeDetected -= HandleDetected;
            scanner?.Stop();
            sessionAdapter?.Stop();
            lifetime?.Cancel();
            lifetime?.Dispose();
            lifetime = null;
        }

        private void Update()
        {
            if (!visible || Time.unscaledTime - lastDetectionTime <= trackingLostTimeoutSeconds) return;
            visible = false;
            displayView?.Hide();
            SetStatus("Tracking Lost — Scan Again");
        }

        private void HandleInitialized(bool succeeded, string failure)
        {
            if (!succeeded || !sessionAdapter.TryStart(out string startFailure))
            {
                SetStatus("AR unavailable: " + (failure.Length > 0 ? failure : startFailure));
                return;
            }
            if (scanner == null || !scanner.TryStart(out string scanFailure))
            {
                SetStatus("QR scanner unavailable: " + scanFailure);
                return;
            }
            SetStatus("Point the camera at an APIR APIR card QR code.");
        }

        private void HandleDetected(string payload)
        {
            if (!payloads.TryParsePayload(payload, out string cardId)) return;
            lastDetectionTime = Time.unscaledTime;
            if (visible && string.Equals(currentCardId, cardId, StringComparison.Ordinal)) return;
            if (!loading) _ = LoadCardAsync(cardId);
        }

        private async Task LoadCardAsync(string cardId)
        {
            loading = true;
            SetStatus("Card recognized. Loading shared record…");
            try
            {
                SharedARCardStoreResult result = await store.LoadAsync(
                    cardId, lifetime == null ? CancellationToken.None : lifetime.Token);
                if (this == null || lifetime == null || lifetime.IsCancellationRequested) return;
                if (result.Record == null)
                {
                    SetStatus(result.Status == SharedARCardStoreStatus.NotFound
                        ? "Card ID was not found in shared storage."
                        : "Card lookup failed: " + result.Message);
                    return;
                }

                currentCardId = cardId;
                lastDetectionTime = Time.unscaledTime;
                visible = true;
                displayView.Display(result.Record, spriteResolver);
                SetStatus(result.IsCloudSuccess
                    ? "Tracking card"
                    : "Tracking cached card — cloud is unavailable");
            }
            finally
            {
                loading = false;
            }
        }

        private void SetStatus(string message)
        {
            if (trackingStatusText != null) trackingStatusText.text = message ?? string.Empty;
        }

        private void ReturnToLobby()
        {
            if (Application.CanStreamedLevelBeLoaded(returnSceneName)) SceneManager.LoadScene(returnSceneName);
        }
    }
}
#endif
