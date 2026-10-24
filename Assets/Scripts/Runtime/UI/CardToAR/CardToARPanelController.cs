using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ApirApir.AugmentedReality;
using ApirApir.Battle;
using ApirApir.Crafting;
using ApirApir.Multiplayer;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ApirApir.UI.CardToAR
{
    /// <summary>
    /// Standalone authoring panel used only by CardAR. It reads validated static definitions but
    /// never owns or mutates gameplay inventory, crafting, card-builder, battle, or network state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CardToARPanelController : MonoBehaviour
    {
        private readonly List<string> selectedSkillIds = new List<string>(4);
        private readonly Dictionary<string, Toggle> skillToggles = new Dictionary<string, Toggle>();
        private StandaloneARCardCatalog catalog;
        private ARCardSpriteResolver spriteResolver;
        private readonly string draftId = Guid.NewGuid().ToString("N");
        private int headIndex;
        private int bodyIndex;
        private int legsIndex;
        private long revision;
        private UICardPreview preview;
        private TMP_InputField nameInput;
        private TMP_Text statsText;
        private TMP_Text idText;
        private TMP_Text statusText;
        private TMP_Text headSelectionText;
        private TMP_Text bodySelectionText;
        private TMP_Text legsSelectionText;
        private TMP_Text printableNameText;
        private TMP_Text printableStatsText;
        private TMP_Text printableSkillsText;
        private RawImage qrImage;
        private RectTransform printableCardRect;
        private Button generateButton;
        private Button exportButton;
        private Button openARButton;
        private Texture2D qrTexture;
        private ARCardRecord generatedRecord;
        private bool uploadedToCloud;
        private CancellationTokenSource lifetime;

        public event Action Closed;

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

        public void Show(ARCardSpriteResolver resolver)
        {
            spriteResolver = resolver;
            catalog ??= StandaloneARCardCatalog.CreateDefault();
            idText.text = generatedRecord == null ? "ID: Generated after authentication" : "ID: " + generatedRecord.cardId;
            statusText.text = generatedRecord == null
                ? "Choose parts, name the card, and select up to 4 skills."
                : uploadedToCloud
                    ? "CLOUD UPLOAD SUCCEEDED — ready for another player to scan."
                    : "Card generated locally. Upload is still required for cross-player scanning.";
            openARButton.interactable = true;
            RefreshSelectionLabels();
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
            catalog = StandaloneARCardCatalog.CreateDefault();
            var title = Text("Title", transform, "CARD TO AR", 30, TextAlignmentOptions.Center);
            Anchor(title.rectTransform, new Vector2(0.05f, 0.91f), new Vector2(0.95f, 0.98f));

            RectTransform cardRoot = UI("PrintableCard", transform, typeof(Image)).GetComponent<RectTransform>();
            printableCardRect = cardRoot;
            Anchor(cardRoot, new Vector2(0.05f, 0.16f), new Vector2(0.46f, 0.89f));
            cardRoot.GetComponent<Image>().color = new Color(0.90f, 0.78f, 0.55f, 1f);

            RectTransform artworkRoot = UI("Artwork", cardRoot).GetComponent<RectTransform>();
            Stretch(artworkRoot);
            Image background = UI("CardBackground", artworkRoot, typeof(Image)).GetComponent<Image>();
            Stretch(background.rectTransform);
            background.color = Color.white;
            Image legs = CreatePartSlot("LegsSlot", artworkRoot);
            Image body = CreatePartSlot("BodySlot", artworkRoot);
            Image head = CreatePartSlot("HeadSlot", artworkRoot);
            preview = artworkRoot.gameObject.AddComponent<UICardPreview>();
            preview.BindSlots(background, legs, body, head);

            printableNameText = Text("PrintableName", cardRoot, "UNTITLED CARD", 20, TextAlignmentOptions.Center);
            Anchor(printableNameText.rectTransform, new Vector2(0.05f, 0.90f), new Vector2(0.95f, 0.98f));
            printableSkillsText = Text("PrintableSkills", cardRoot, "Skills: None", 12, TextAlignmentOptions.Center);
            Anchor(printableSkillsText.rectTransform, new Vector2(0.05f, 0.82f), new Vector2(0.95f, 0.90f));

            idText = Text("UniqueID", cardRoot, "ID: —", 16, TextAlignmentOptions.Center);
            Anchor(idText.rectTransform, new Vector2(0.05f, 0.10f), new Vector2(0.95f, 0.18f));
            printableStatsText = Text("PrintableStats", cardRoot, string.Empty, 12, TextAlignmentOptions.Center);
            Anchor(printableStatsText.rectTransform, new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.10f));
            RectTransform qrArea = UI("QRArea", cardRoot).GetComponent<RectTransform>();
            Anchor(qrArea, new Vector2(0.70f, 0.20f), new Vector2(0.95f, 0.45f));
            qrImage = UI("QRImage", qrArea, typeof(RawImage), typeof(AspectRatioFitter))
                .GetComponent<RawImage>();
            Stretch(qrImage.rectTransform);
            qrImage.color = Color.white;
            AspectRatioFitter qrAspect = qrImage.GetComponent<AspectRatioFitter>();
            qrAspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            qrAspect.aspectRatio = 1f;

            nameInput = Input("CardName", transform, "Customized card name");
            Anchor(nameInput.GetComponent<RectTransform>(), new Vector2(0.51f, 0.80f), new Vector2(0.95f, 0.88f));
            nameInput.characterLimit = ARCardRecordFactory.MaximumCardNameLength;
            nameInput.onValueChanged.AddListener(_ => MarkContentChanged());

            headSelectionText = CreatePartSelector(
                "HeadSelector", "HEAD", new Vector2(0.51f, 0.73f), new Vector2(0.95f, 0.79f), CycleHead);
            bodySelectionText = CreatePartSelector(
                "BodySelector", "BODY", new Vector2(0.51f, 0.66f), new Vector2(0.95f, 0.72f), CycleBody);
            legsSelectionText = CreatePartSelector(
                "LegsSelector", "LEGS", new Vector2(0.51f, 0.59f), new Vector2(0.95f, 0.65f), CycleLegs);

            statsText = Text("Statistics", transform, string.Empty, 18, TextAlignmentOptions.TopLeft);
            Anchor(statsText.rectTransform, new Vector2(0.51f, 0.49f), new Vector2(0.95f, 0.58f));

            TMP_Text skillHeader = Text("SkillHeader", transform,
                "AVAILABLE NORMAL SKILLS — select up to 4", 17, TextAlignmentOptions.Left);
            Anchor(skillHeader.rectTransform, new Vector2(0.51f, 0.44f), new Vector2(0.95f, 0.48f));

            RectTransform skillList = UI("SkillList", transform, typeof(VerticalLayoutGroup)).GetComponent<RectTransform>();
            Anchor(skillList, new Vector2(0.51f, 0.22f), new Vector2(0.95f, 0.44f));
            var layout = skillList.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 4f;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = true;
            SkillCatalog skillCatalog = DefaultSkillCatalog.Create();
            for (int i = 0; i < skillCatalog.NormalSkills.Count; i++)
                CreateSkillToggle(skillList, skillCatalog.NormalSkills[i]);

            statusText = Text("Status", transform, string.Empty, 16, TextAlignmentOptions.TopLeft);
            Anchor(statusText.rectTransform, new Vector2(0.51f, 0.15f), new Vector2(0.95f, 0.21f));

            Button close = Button("Close", transform, "BACK", new Color(0.42f, 0.28f, 0.18f));
            Anchor(close.GetComponent<RectTransform>(), new Vector2(0.51f, 0.08f), new Vector2(0.60f, 0.16f));
            close.onClick.AddListener(Close);
            generateButton = Button("Generate", transform, "GENERATE + UPLOAD", new Color(0.17f, 0.53f, 0.30f));
            Anchor(generateButton.GetComponent<RectTransform>(), new Vector2(0.61f, 0.08f), new Vector2(0.76f, 0.16f));
            generateButton.onClick.AddListener(() => _ = GenerateAsync());
            exportButton = Button("Export", transform, "EXPORT PNG", new Color(0.43f, 0.32f, 0.16f));
            Anchor(exportButton.GetComponent<RectTransform>(), new Vector2(0.77f, 0.08f), new Vector2(0.86f, 0.16f));
            exportButton.onClick.AddListener(() => StartCoroutine(ExportPrintableCard()));
            openARButton = Button("OpenAR", transform, "SCAN CARD", new Color(0.18f, 0.40f, 0.62f));
            Anchor(openARButton.GetComponent<RectTransform>(), new Vector2(0.87f, 0.08f), new Vector2(0.95f, 0.16f));
            openARButton.onClick.AddListener(OpenAR);
            RefreshSelectionLabels();
            RefreshPreview();
            gameObject.SetActive(false);
        }

        private void CreateSkillToggle(RectTransform parent, SkillDefinition skill)
        {
            GameObject row = UI(skill.Id, parent, typeof(Image), typeof(Toggle), typeof(LayoutElement));
            row.GetComponent<Image>().color = new Color(0.16f, 0.14f, 0.11f, 0.95f);
            row.GetComponent<LayoutElement>().preferredHeight = 36f;
            Toggle toggle = row.GetComponent<Toggle>();
            Image checkmark = UI("Checkmark", row.transform, typeof(Image)).GetComponent<Image>();
            Anchor(checkmark.rectTransform, new Vector2(0.02f, 0.18f), new Vector2(0.08f, 0.82f));
            checkmark.color = new Color(0.32f, 0.82f, 0.44f, 1f);
            checkmark.raycastTarget = false;
            TMP_Text label = Text("Label", row.transform,
                $"{skill.DisplayName}  (CD {skill.CooldownExchanges})", 15, TextAlignmentOptions.Left);
            Anchor(label.rectTransform, new Vector2(0.10f, 0f), Vector2.one);
            label.rectTransform.offsetMin = new Vector2(0f, 4f);
            label.rectTransform.offsetMax = new Vector2(-8f, -4f);
            label.raycastTarget = false;
            toggle.targetGraphic = row.GetComponent<Image>();
            toggle.graphic = checkmark;
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
            MarkContentChanged();
        }

        private TMP_Text CreatePartSelector(
            string objectName,
            string category,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Action<int> cycle)
        {
            RectTransform row = UI(objectName, transform).GetComponent<RectTransform>();
            Anchor(row, anchorMin, anchorMax);
            TMP_Text caption = Text("Caption", row, category, 15, TextAlignmentOptions.Left);
            Anchor(caption.rectTransform, new Vector2(0f, 0f), new Vector2(0.22f, 1f));
            Button previous = Button("Previous", row, "<", new Color(0.20f, 0.22f, 0.28f));
            Anchor(previous.GetComponent<RectTransform>(), new Vector2(0.23f, 0f), new Vector2(0.33f, 1f));
            previous.onClick.AddListener(() => cycle(-1));
            TMP_Text value = Text("Value", row, "None", 15, TextAlignmentOptions.Center);
            Anchor(value.rectTransform, new Vector2(0.34f, 0f), new Vector2(0.88f, 1f));
            Button next = Button("Next", row, ">", new Color(0.20f, 0.22f, 0.28f));
            Anchor(next.GetComponent<RectTransform>(), new Vector2(0.89f, 0f), new Vector2(1f, 1f));
            next.onClick.AddListener(() => cycle(1));
            return value;
        }

        private void CycleHead(int direction)
        {
            headIndex = Wrap(headIndex + direction, catalog.Heads.Count + 1);
            MarkContentChanged();
        }

        private void CycleBody(int direction)
        {
            bodyIndex = Wrap(bodyIndex + direction, catalog.Bodies.Count + 1);
            CraftablePartDefinition body = Selected(catalog.Bodies, bodyIndex);
            if (body != null && body.IsHybrid) legsIndex = 0;
            MarkContentChanged();
        }

        private void CycleLegs(int direction)
        {
            legsIndex = Wrap(legsIndex + direction, catalog.Legs.Count + 1);
            CraftablePartDefinition body = Selected(catalog.Bodies, bodyIndex);
            if (legsIndex > 0 && body != null && body.IsHybrid) bodyIndex = 0;
            MarkContentChanged();
        }

        private void MarkContentChanged()
        {
            revision++;
            generatedRecord = null;
            uploadedToCloud = false;
            SetQrTexture(null);
            if (idText != null) idText.text = "ID: Generated after authentication";
            if (statusText != null)
                statusText.text = "Changes are ready. Generate and upload to share this card.";
            RefreshSelectionLabels();
            RefreshPreview();
        }

        private void RefreshSelectionLabels()
        {
            if (catalog == null) return;
            SetSelectionText(headSelectionText, Selected(catalog.Heads, headIndex));
            SetSelectionText(bodySelectionText, Selected(catalog.Bodies, bodyIndex));
            SetSelectionText(legsSelectionText, Selected(catalog.Legs, legsIndex));
        }

        private bool TryCompose(out StandaloneARCardComposition composition, out string error)
        {
            CraftablePartDefinition head = Selected(catalog.Heads, headIndex);
            CraftablePartDefinition body = Selected(catalog.Bodies, bodyIndex);
            CraftablePartDefinition legs = Selected(catalog.Legs, legsIndex);
            return catalog.TryCompose(
                draftId, revision, head?.Id, body?.Id, legs?.Id, out composition, out error);
        }

        private Sprite Resolve(CraftablePartDefinition part) =>
            part == null || spriteResolver == null ? null : spriteResolver.Resolve(part.VisualAssetId);

        private string BuildSelectedSkillNames()
        {
            if (selectedSkillIds.Count == 0) return "Skills: None";
            SkillCatalog skills = DefaultSkillCatalog.Create();
            var names = new string[selectedSkillIds.Count];
            for (int i = 0; i < names.Length; i++)
                names[i] = skills.TryGet(selectedSkillIds[i], out SkillDefinition skill)
                    ? skill.DisplayName : selectedSkillIds[i];
            return "Skills: " + string.Join(", ", names);
        }

        private static string BuildStoreStatus(SharedARCardStoreResult result)
        {
            if (result == null) return "CLOUD UNAVAILABLE — no response was received.";
            switch (result.Status)
            {
                case SharedARCardStoreStatus.Success:
                    return "CLOUD UPLOAD SUCCEEDED — ready for another player to scan.";
                case SharedARCardStoreStatus.LocalCacheOnly:
                    return "LOCAL CACHE ONLY — NOT uploaded for cross-player scanning.\n" + result.Message;
                case SharedARCardStoreStatus.Unauthorized:
                    return "AUTHENTICATION REQUIRED — sign in before uploading.";
                case SharedARCardStoreStatus.Conflict:
                    return "UPLOAD CONFLICT — the card ID already belongs to different data.";
                case SharedARCardStoreStatus.Cancelled:
                    return "Upload cancelled.";
                default:
                    return "CLOUD UNAVAILABLE — " + (result.Message.Length == 0
                        ? result.Status.ToString() : result.Message);
            }
        }

        private void Close()
        {
            gameObject.SetActive(false);
            Closed?.Invoke();
        }

        private static int Wrap(int value, int count)
        {
            if (count <= 0) return 0;
            value %= count;
            return value < 0 ? value + count : value;
        }

        private static CraftablePartDefinition Selected(
            IReadOnlyList<CraftablePartDefinition> values,
            int index) => index <= 0 || values == null || index > values.Count ? null : values[index - 1];

        private static void SetSelectionText(TMP_Text target, CraftablePartDefinition part)
        {
            if (target != null) target.text = part == null ? "None" : part.DisplayName;
        }

        private async Task GenerateAsync()
        {
            if (generateButton == null || catalog == null) return;
            generateButton.interactable = false;
            statusText.text = "Checking Unity Authentication…";
            try
            {
                CancellationToken token = lifetime == null ? CancellationToken.None : lifetime.Token;
                AccountAuthenticationResult authentication =
                    await UnityAccountAuthenticationRuntime.Service.RequireAuthenticatedAsync(token);
                if (this == null || lifetime == null || lifetime.IsCancellationRequested) return;
                if (!authentication.Succeeded || string.IsNullOrWhiteSpace(authentication.PlayerId))
                {
                    statusText.text = "AUTHENTICATION REQUIRED — " + authentication.ErrorMessage;
                    return;
                }

                if (!TryCompose(out StandaloneARCardComposition composition, out string compositionError))
                {
                    statusText.text = compositionError;
                    return;
                }

                var factory = new ARCardRecordFactory(
                    DefaultCraftingCatalog.CreateAvailableAssetCatalog(), DefaultSkillCatalog.Create());
                if (generatedRecord == null && !factory.TryCreateStandalone(
                        composition, authentication.PlayerId, nameInput.text,
                        selectedSkillIds, out generatedRecord, out string error))
                {
                    statusText.text = error;
                    return;
                }

                SetQrTexture(new ZXingQRCodeCodec().CreateCardIdTexture(generatedRecord.cardId));
                idText.text = "ID: " + generatedRecord.cardId;
                statusText.text = "Uploading to shared cloud storage…";
                var store = new UnitySharedARCardStore();
                SharedARCardStoreResult result = await store.SaveAsync(generatedRecord, token);
                if (this == null || lifetime == null || lifetime.IsCancellationRequested) return;
                uploadedToCloud = result.IsCloudSuccess;
                statusText.text = BuildStoreStatus(result);
            }
            catch (OperationCanceledException)
            {
                if (this != null && statusText != null) statusText.text = "Upload cancelled.";
            }
            catch (Exception exception)
            {
                if (this != null && statusText != null)
                    statusText.text = "Card generation failed: " + exception.Message;
            }
            finally
            {
                if (this != null && generateButton != null) generateButton.interactable = true;
            }
        }

        private void RefreshPreview()
        {
            if (preview == null || catalog == null ||
                !TryCompose(out StandaloneARCardComposition composition, out _)) return;
            preview.SetParts(
                Resolve(composition.Head), Resolve(composition.Body), Resolve(composition.Legs));
            preview.ApplyLayout();
            ARCardVisualLayout.Apply(preview, composition.Head, composition.Body, composition.Legs);
            string stats = $"HP {composition.Statistics.MaximumHitPoints}   ATK {composition.Statistics.EffectiveAttack}\n" +
                           $"ATK +{composition.Statistics.AttackBonusPercent:0.#}%   " +
                           $"SPD +{composition.Statistics.SpeedBonusPercent:0.#}%   " +
                           $"CRIT +{composition.Statistics.CriticalChanceBonusPercent:0.#}%";
            statsText.text = stats;
            printableNameText.text = string.IsNullOrWhiteSpace(nameInput?.text)
                ? "UNTITLED CARD" : nameInput.text.Trim();
            printableStatsText.text = stats.Replace("\n", "   ");
            printableSkillsText.text = BuildSelectedSkillNames();
        }

        private void OpenAR()
        {
            Close();
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
