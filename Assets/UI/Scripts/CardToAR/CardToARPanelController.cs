using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ApirApir.AugmentedReality;
using ApirApir.Battle;
using ApirApir.Cards;
using ApirApir.Crafting;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ApirApir.UI.CardToAR
{
    /// <summary>
    /// Lightweight defense-ready authoring panel. It consumes the existing immutable assembly
    /// snapshot and never owns inventory, crafting, or combat state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CardToARPanelController : MonoBehaviour
    {
        private readonly List<string> selectedSkillIds = new List<string>(4);
        private readonly Dictionary<string, Toggle> skillToggles = new Dictionary<string, Toggle>();
        private CardBuildSnapshot snapshot;
        private Func<string, Sprite> spriteResolver;
        private UICardPreview preview;
        private TMP_InputField nameInput;
        private TMP_Text statsText;
        private TMP_Text idText;
        private TMP_Text statusText;
        private RawImage qrImage;
        private RectTransform printableCardRect;
        private Button generateButton;
        private Button exportButton;
        private Button openARButton;
        private Texture2D qrTexture;
        private ARCardRecord generatedRecord;
        private CancellationTokenSource lifetime;

        public static CardToARPanelController Create(Transform canvas)
        {
            if (canvas == null) throw new ArgumentNullException(nameof(canvas));
            var root = UI("CardToARPanel", canvas, typeof(Image));
            Stretch(root.GetComponent<RectTransform>());
            root.GetComponent<Image>().color = new Color(0.05f, 0.045f, 0.035f, 0.98f);
            var controller = root.AddComponent<CardToARPanelController>();
            controller.BuildInterface();
            return controller;
        }

        public void Show(CardBuildSnapshot confirmedSnapshot, Func<string, Sprite> resolver)
        {
            snapshot = confirmedSnapshot;
            spriteResolver = resolver;
            selectedSkillIds.Clear();
            foreach (Toggle toggle in skillToggles.Values)
                if (toggle != null) toggle.SetIsOnWithoutNotify(false);
            generatedRecord = null;
            SetQrTexture(null);
            idText.text = "ID: Generated after cloud confirmation";
            statusText.text = "Name the confirmed card and choose up to 4 available skills.";
            openARButton.interactable = true;
            RefreshPreview();
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
        }

        private void OnEnable()
        {
            lifetime?.Cancel();
            lifetime?.Dispose();
            lifetime = new CancellationTokenSource();
        }

        private void OnDisable()
        {
            lifetime?.Cancel();
            lifetime?.Dispose();
            lifetime = null;
        }

        private void OnDestroy()
        {
            if (qrTexture != null) Destroy(qrTexture);
        }

        private void BuildInterface()
        {
            var title = Text("Title", transform, "CARD TO AR", 30, TextAlignmentOptions.Center);
            Anchor(title.rectTransform, new Vector2(0.05f, 0.91f), new Vector2(0.95f, 0.98f));

            RectTransform cardRoot = UI("PrintableCard", transform, typeof(Image)).GetComponent<RectTransform>();
            printableCardRect = cardRoot;
            Anchor(cardRoot, new Vector2(0.05f, 0.16f), new Vector2(0.46f, 0.89f));
            cardRoot.GetComponent<Image>().color = new Color(0.90f, 0.78f, 0.55f, 1f);

            Image background = UI("CardBackground", cardRoot, typeof(Image)).GetComponent<Image>();
            Stretch(background.rectTransform);
            background.color = Color.white;
            Image legs = CreatePartSlot("LegsSlot", cardRoot);
            Image body = CreatePartSlot("BodySlot", cardRoot);
            Image head = CreatePartSlot("HeadSlot", cardRoot);
            preview = cardRoot.gameObject.AddComponent<UICardPreview>();
            preview.BindSlots(background, legs, body, head);

            idText = Text("UniqueID", cardRoot, "ID: —", 16, TextAlignmentOptions.Center);
            Anchor(idText.rectTransform, new Vector2(0.05f, 0.10f), new Vector2(0.95f, 0.18f));
            qrImage = UI("QRArea", cardRoot, typeof(RawImage)).GetComponent<RawImage>();
            Anchor(qrImage.rectTransform, new Vector2(0.70f, 0.20f), new Vector2(0.95f, 0.45f));
            qrImage.color = Color.white;

            nameInput = Input("CardName", transform, "Customized card name");
            Anchor(nameInput.GetComponent<RectTransform>(), new Vector2(0.51f, 0.80f), new Vector2(0.95f, 0.88f));
            nameInput.characterLimit = ARCardRecordFactory.MaximumCardNameLength;

            statsText = Text("Statistics", transform, string.Empty, 18, TextAlignmentOptions.TopLeft);
            Anchor(statsText.rectTransform, new Vector2(0.51f, 0.65f), new Vector2(0.95f, 0.78f));

            TMP_Text skillHeader = Text("SkillHeader", transform,
                "AVAILABLE NORMAL SKILLS — select up to 4", 17, TextAlignmentOptions.Left);
            Anchor(skillHeader.rectTransform, new Vector2(0.51f, 0.59f), new Vector2(0.95f, 0.64f));

            RectTransform skillList = UI("SkillList", transform, typeof(VerticalLayoutGroup)).GetComponent<RectTransform>();
            Anchor(skillList, new Vector2(0.51f, 0.33f), new Vector2(0.95f, 0.59f));
            var layout = skillList.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 4f;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = true;
            SkillCatalog catalog = DefaultSkillCatalog.Create();
            for (int i = 0; i < catalog.NormalSkills.Count; i++)
                CreateSkillToggle(skillList, catalog.NormalSkills[i]);

            statusText = Text("Status", transform, string.Empty, 16, TextAlignmentOptions.TopLeft);
            Anchor(statusText.rectTransform, new Vector2(0.51f, 0.20f), new Vector2(0.95f, 0.31f));

            Button close = Button("Close", transform, "BACK", new Color(0.42f, 0.28f, 0.18f));
            Anchor(close.GetComponent<RectTransform>(), new Vector2(0.51f, 0.08f), new Vector2(0.60f, 0.16f));
            close.onClick.AddListener(() => gameObject.SetActive(false));
            generateButton = Button("Generate", transform, "GENERATE + UPLOAD", new Color(0.17f, 0.53f, 0.30f));
            Anchor(generateButton.GetComponent<RectTransform>(), new Vector2(0.61f, 0.08f), new Vector2(0.76f, 0.16f));
            generateButton.onClick.AddListener(() => _ = GenerateAsync());
            exportButton = Button("Export", transform, "EXPORT PNG", new Color(0.43f, 0.32f, 0.16f));
            Anchor(exportButton.GetComponent<RectTransform>(), new Vector2(0.77f, 0.08f), new Vector2(0.86f, 0.16f));
            exportButton.onClick.AddListener(() => StartCoroutine(ExportPrintableCard()));
            openARButton = Button("OpenAR", transform, "OPEN AR", new Color(0.18f, 0.40f, 0.62f));
            Anchor(openARButton.GetComponent<RectTransform>(), new Vector2(0.87f, 0.08f), new Vector2(0.95f, 0.16f));
            openARButton.onClick.AddListener(OpenAR);
            gameObject.SetActive(false);
        }

        private void CreateSkillToggle(RectTransform parent, SkillDefinition skill)
        {
            GameObject row = UI(skill.Id, parent, typeof(Image), typeof(Toggle), typeof(LayoutElement));
            row.GetComponent<Image>().color = new Color(0.16f, 0.14f, 0.11f, 0.95f);
            row.GetComponent<LayoutElement>().preferredHeight = 36f;
            Toggle toggle = row.GetComponent<Toggle>();
            TMP_Text label = Text("Label", row.transform,
                $"{skill.DisplayName}  (CD {skill.CooldownExchanges})", 15, TextAlignmentOptions.Left);
            Stretch(label.rectTransform, 12f);
            toggle.targetGraphic = row.GetComponent<Image>();
            toggle.graphic = label;
            toggle.onValueChanged.AddListener(on => HandleSkillToggle(skill.Id, toggle, on));
            skillToggles.Add(skill.Id, toggle);
        }

        private void HandleSkillToggle(string skillId, Toggle toggle, bool selected)
        {
            if (selected)
            {
                if (selectedSkillIds.Count >= CardBattleLoadout.MaximumEquippedSkills)
                {
                    toggle.SetIsOnWithoutNotify(false);
                    statusText.text = $"Only {CardBattleLoadout.MaximumEquippedSkills} skills may be selected.";
                    return;
                }
                if (!selectedSkillIds.Contains(skillId)) selectedSkillIds.Add(skillId);
            }
            else
            {
                selectedSkillIds.Remove(skillId);
            }
        }

        private async Task GenerateAsync()
        {
            if (snapshot == null || generateButton == null) return;
            generateButton.interactable = false;
            statusText.text = "Validating confirmed card…";
            try
            {
                var factory = new ARCardRecordFactory(
                    DefaultCraftingCatalog.CreateAvailableAssetCatalog(), DefaultSkillCatalog.Create());
                if (!factory.TryCreate(snapshot, snapshot.OwnerPlayerId, nameInput.text,
                        selectedSkillIds, out ARCardRecord record, out string error))
                {
                    statusText.text = error;
                    return;
                }

                generatedRecord = record;
                SetQrTexture(new ZXingQRCodeCodec().CreateCardIdTexture(record.cardId));
                idText.text = "ID: " + record.cardId;
                statusText.text = "Uploading to shared cloud storage…";
                var store = new UnitySharedARCardStore();
                SharedARCardStoreResult result = await store.SaveAsync(record,
                    lifetime == null ? CancellationToken.None : lifetime.Token);
                if (this == null) return;
                statusText.text = result.IsCloudSuccess
                    ? "CLOUD UPLOAD SUCCEEDED — ready for another player to scan."
                    : result.Status == SharedARCardStoreStatus.LocalCacheOnly
                        ? "LOCAL CACHE ONLY — NOT uploaded for cross-player scanning.\n" + result.Message
                        : result.Status + ": " + result.Message;
            }
            finally
            {
                if (this != null && generateButton != null) generateButton.interactable = true;
            }
        }

        private void RefreshPreview()
        {
            if (snapshot == null || preview == null) return;
            preview.SetParts(
                snapshot.Head.IsEmpty ? null : spriteResolver?.Invoke(snapshot.Head.DefinitionId),
                snapshot.Body.IsEmpty ? null : spriteResolver?.Invoke(snapshot.Body.DefinitionId),
                snapshot.Legs.IsEmpty ? null : spriteResolver?.Invoke(snapshot.Legs.DefinitionId));
            preview.ApplyLayout();
            statsText.text = $"HP {snapshot.Stats.MaximumHitPoints}   ATK {snapshot.Stats.EffectiveAttack}\n" +
                             $"ATK +{snapshot.Stats.AttackBonusPercent:0.#}%   " +
                             $"SPD +{snapshot.Stats.SpeedBonusPercent:0.#}%   " +
                             $"CRIT +{snapshot.Stats.CriticalChanceBonusPercent:0.#}%";
        }

        private void OpenAR()
        {
            if (string.Equals(SceneManager.GetActiveScene().name, "CardAR", StringComparison.Ordinal))
            {
                gameObject.SetActive(false);
                return;
            }
            if (Application.CanStreamedLevelBeLoaded("CardAR")) SceneManager.LoadScene("CardAR");
            else statusText.text = "CardAR is not available in Build Settings yet.";
        }

        private IEnumerator ExportPrintableCard()
        {
            if (generatedRecord == null || printableCardRect == null || qrTexture == null)
            {
                statusText.text = "Generate the card and QR before exporting.";
                yield break;
            }

            exportButton.interactable = false;
            yield return new WaitForEndOfFrame();
            Texture2D screenshot = null;
            Texture2D crop = null;
            try
            {
                screenshot = ScreenCapture.CaptureScreenshotAsTexture();
                var corners = new Vector3[4];
                printableCardRect.GetWorldCorners(corners);
                int x = Mathf.Clamp(Mathf.FloorToInt(corners[0].x), 0, screenshot.width - 1);
                int y = Mathf.Clamp(Mathf.FloorToInt(corners[0].y), 0, screenshot.height - 1);
                int width = Mathf.Clamp(Mathf.CeilToInt(corners[2].x) - x, 1, screenshot.width - x);
                int height = Mathf.Clamp(Mathf.CeilToInt(corners[2].y) - y, 1, screenshot.height - y);
                crop = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
                crop.SetPixels(screenshot.GetPixels(x, y, width, height));
                crop.Apply(false, false);
                string directory = Path.Combine(Application.persistentDataPath, "ARCards");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, generatedRecord.cardId + ".png");
                File.WriteAllBytes(path, crop.EncodeToPNG());
                statusText.text = "Printable card exported to: " + path;
            }
            catch (Exception exception)
            {
                statusText.text = "Card export failed: " + exception.Message;
            }
            finally
            {
                if (screenshot != null) Destroy(screenshot);
                if (crop != null) Destroy(crop);
                if (exportButton != null) exportButton.interactable = true;
            }
        }

        private void SetQrTexture(Texture2D texture)
        {
            if (qrTexture != null) Destroy(qrTexture);
            qrTexture = texture;
            if (qrImage != null)
            {
                qrImage.texture = texture;
                qrImage.enabled = texture != null;
            }
        }

        private static Image CreatePartSlot(string name, Transform parent)
        {
            Image image = UI(name, parent, typeof(Image)).GetComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        private static GameObject UI(string name, Transform parent, params Type[] components)
        {
            var types = new Type[components.Length + 1];
            types[0] = typeof(RectTransform);
            Array.Copy(components, 0, types, 1, components.Length);
            var value = new GameObject(name, types);
            value.transform.SetParent(parent, false);
            return value;
        }

        private static TextMeshProUGUI Text(
            string name, Transform parent, string value, float size, TextAlignmentOptions alignment)
        {
            var text = UI(name, parent, typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = Color.white;
            text.textWrappingMode = TextWrappingModes.Normal;
            return text;
        }

        private static TMP_InputField Input(string name, Transform parent, string placeholder)
        {
            GameObject root = UI(name, parent, typeof(Image), typeof(TMP_InputField));
            root.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);
            TextMeshProUGUI text = Text("Text", root.transform, string.Empty, 20, TextAlignmentOptions.Left);
            Stretch(text.rectTransform, 12f);
            TextMeshProUGUI hint = Text("Placeholder", root.transform, placeholder, 20, TextAlignmentOptions.Left);
            hint.color = new Color(1f, 1f, 1f, 0.45f);
            Stretch(hint.rectTransform, 12f);
            TMP_InputField field = root.GetComponent<TMP_InputField>();
            field.textComponent = text;
            field.placeholder = hint;
            return field;
        }

        private static Button Button(string name, Transform parent, string label, Color color)
        {
            GameObject root = UI(name, parent, typeof(Image), typeof(Button));
            root.GetComponent<Image>().color = color;
            TMP_Text text = Text("Label", root.transform, label, 16, TextAlignmentOptions.Center);
            Stretch(text.rectTransform, 5f);
            return root.GetComponent<Button>();
        }

        private static void Anchor(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static void Stretch(RectTransform rect, float padding = 0f)
        {
            Anchor(rect, Vector2.zero, Vector2.one);
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
        }
    }
}
