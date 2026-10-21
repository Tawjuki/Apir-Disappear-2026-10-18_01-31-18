#if APIRAPIR_ARFOUNDATION
using System;
using System.Threading;
using System.Threading.Tasks;
using ApirApir.UI;
using ApirApir.UI.CardToAR;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ApirApir.AugmentedReality
{
    /// <summary>Owns scanning, presentation, and authoring for the isolated CardAR scene.</summary>
    [DisallowMultipleComponent]
    public sealed class CardARSceneController : MonoBehaviour
    {
        [SerializeField] private ARFoundationSessionAdapter sessionAdapter;
        [SerializeField] private ARFoundationQRCodeScanner scanner;
        [SerializeField] private ARCardDisplayView displayView;
        [SerializeField] private ARCardSpriteResolver spriteResolver;
        [SerializeField] private TMP_Text trackingStatusText;
        [SerializeField] private Button backButton;
        [SerializeField] private Button authorCardButton;
        [SerializeField, Min(0.5f)] private float trackingLostTimeoutSeconds = 1.5f;
        [SerializeField] private string returnSceneName = "Lobby";

        private readonly QRCodeService payloads = new QRCodeService();
        private readonly UnitySharedARCardStore store = new UnitySharedARCardStore();
        private CancellationTokenSource lifetime;
        private string currentCardId = string.Empty;
        private float lastDetectionTime = float.NegativeInfinity;
        private bool loading;
        private bool visible;
        private CardToARPanelController authoringPanel;

        public void Configure(
            ARFoundationSessionAdapter session,
            ARFoundationQRCodeScanner qrScanner,
            ARCardDisplayView view,
            ARCardSpriteResolver sprites,
            TMP_Text status,
            Button back,
            Button authorCard)
        {
            sessionAdapter = session;
            scanner = qrScanner;
            displayView = view;
            spriteResolver = sprites;
            trackingStatusText = status;
            backButton = back;
            authorCardButton = authorCard;
        }

        private void OnEnable()
        {
            lifetime = new CancellationTokenSource();
            EnsureAuthorCardButton();
            if (backButton != null) backButton.onClick.AddListener(ReturnToLobby);
            if (authorCardButton != null) authorCardButton.onClick.AddListener(OpenAuthoring);
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
            if (authorCardButton != null) authorCardButton.onClick.RemoveListener(OpenAuthoring);
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
            string startFailure = string.Empty;
            if (!succeeded || !sessionAdapter.TryStart(out startFailure))
            {
                SetStatus("AR unavailable: " + (failure.Length > 0 ? failure : startFailure));
                return;
            }
            string scanFailure = string.Empty;
            if (scanner == null || !scanner.TryStart(out scanFailure))
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

        private void OpenAuthoring()
        {
            Canvas canvas = backButton == null ? null : backButton.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                SetStatus("Card authoring UI is not configured.");
                return;
            }

            PreparationDemoSession.EnsureInitialized();
            if (PreparationDemoSession.CardAssembly == null ||
                !PreparationDemoSession.CardAssembly.TryGetCardBuild(
                    PreparationDemoSession.PlayerId,
                    PreparationDemoSession.CardId,
                    out ApirApir.Cards.CardBuildSnapshot snapshot))
            {
                SetStatus("No confirmed assembled card is available.");
                return;
            }

            if (authoringPanel == null)
                authoringPanel = CardToARPanelController.Create(canvas.transform);
            authoringPanel.Show(snapshot, spriteResolver == null ? null : spriteResolver.Resolve);
        }

        private void EnsureAuthorCardButton()
        {
            if (authorCardButton != null) return;
            Canvas canvas = backButton == null ? null : backButton.GetComponentInParent<Canvas>();
            if (canvas == null) return;

            Transform existing = canvas.transform.Find("AuthorCardButton");
            if (existing != null) authorCardButton = existing.GetComponent<Button>();
            if (authorCardButton != null) return;

            var root = new GameObject("AuthorCardButton", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(Button));
            root.transform.SetParent(canvas.transform, false);
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.27f, 0.03f);
            rect.anchorMax = new Vector2(0.51f, 0.11f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            root.GetComponent<Image>().color = new Color(0.18f, 0.25f, 0.34f, 0.95f);

            var label = new GameObject("Label", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            label.transform.SetParent(root.transform, false);
            RectTransform labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            TextMeshProUGUI text = label.GetComponent<TextMeshProUGUI>();
            text.text = "CREATE / VIEW CARD";
            text.fontSize = 20f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            authorCardButton = root.GetComponent<Button>();
        }

        private void ReturnToLobby()
        {
            if (Application.CanStreamedLevelBeLoaded(returnSceneName)) SceneManager.LoadScene(returnSceneName);
        }
    }
}
#endif
