#if UNITY_EDITOR
using System;
using System.Linq;
using ApirApir.UI.CardBuilder;
using ApirApir.UI.Inventory;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ApirApir.UI.Editor
{
    /// <summary>
    /// Creates the reusable Card Builder UI assets and installs the panel into Lobby
    /// without replacing the existing Lobby hierarchy or opening it as the sole scene.
    /// </summary>
    public static class CardBuilderLobbyInstaller
    {
        private const string LobbyScenePath = "Assets/Scenes/Lobby.unity";
        private const string CardPreviewPath = "Assets/UI/Prefabs/CardPreview.prefab";
        private const string OptionPrefabPath = "Assets/UI/Prefabs/CardPartOption.prefab";
        private const string PanelPrefabPath = "Assets/UI/Prefabs/CardBuilderPanel.prefab";

        private static readonly Color PanelColor = new Color(0.10f, 0.11f, 0.16f, 0.98f);
        private static readonly Color SurfaceColor = new Color(0.16f, 0.17f, 0.24f, 1f);
        private static readonly Color AccentColor = new Color(0.95f, 0.72f, 0.20f, 1f);
        private static readonly Color ButtonColor = new Color(0.28f, 0.30f, 0.40f, 1f);

        [MenuItem("APIR APIR/Card Builder/Install Into Lobby")]
        public static void InstallIntoLobby()
        {
            CreateOrUpdatePrefabs();

            Scene originalActiveScene = SceneManager.GetActiveScene();
            Scene lobbyScene = SceneManager.GetSceneByPath(LobbyScenePath);
            bool openedForInstall = !lobbyScene.IsValid() || !lobbyScene.isLoaded;

            if (openedForInstall)
            {
                lobbyScene = EditorSceneManager.OpenScene(LobbyScenePath, OpenSceneMode.Additive);
            }
            else if (lobbyScene.isDirty)
            {
                throw new InvalidOperationException(
                    "Lobby is already open with unsaved changes. Save or close it before installing the Card Builder.");
            }

            try
            {
                LobbyNavigator navigator = FindInScene<LobbyNavigator>(lobbyScene);
                if (navigator == null)
                {
                    throw new InvalidOperationException("LobbyNavigator was not found in the Lobby scene.");
                }

                Transform contentArea = FindDescendant(navigator.transform, "ContentArea");
                Transform navBar = FindDescendant(navigator.transform, "NavBar");
                if (contentArea == null || navBar == null)
                {
                    throw new InvalidOperationException("Lobby ContentArea or NavBar was not found.");
                }

                GameObject panel = InstantiatePanel(contentArea, lobbyScene);
                Button navButton = FindOrCreateNavigationButton(navigator, navBar);
                navigator.cardBuilderPanel = panel;
                navigator.btnCardBuilder = navButton;
                panel.SetActive(false);

                EditorUtility.SetDirty(navigator);
                EditorSceneManager.MarkSceneDirty(lobbyScene);
                if (!EditorSceneManager.SaveScene(lobbyScene))
                {
                    throw new InvalidOperationException("Unity could not save the updated Lobby scene.");
                }

                Debug.Log("[APIR APIR] Card Builder installed into Lobby successfully.");
            }
            finally
            {
                if (openedForInstall && lobbyScene.IsValid() && lobbyScene.isLoaded)
                {
                    EditorSceneManager.CloseScene(lobbyScene, true);
                }

                if (originalActiveScene.IsValid() && originalActiveScene.isLoaded)
                {
                    SceneManager.SetActiveScene(originalActiveScene);
                }
            }
        }

        public static void CreateOrUpdatePrefabs()
        {
            UICardPartOption optionPrefab = CreateOptionPrefab();
            CreatePanelPrefab(optionPrefab);
            AssetDatabase.SaveAssets();
        }

        public static GameObject InstantiatePanel(Transform parent, Scene scene = default)
        {
            Transform existing = FindDirectChild(parent, "CardBuilderPanel");
            if (existing != null)
            {
                return existing.gameObject;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException($"Card Builder prefab is missing at {PanelPrefabPath}.");
            }

            GameObject instance = scene.IsValid()
                ? (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene)
                : (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = "CardBuilderPanel";
            instance.transform.SetParent(parent, false);
            Stretch((RectTransform)instance.transform);
            instance.transform.SetAsLastSibling();
            return instance;
        }

        public static Button ConfigureGeneratedNavigation(
            LobbyNavigator navigator,
            Transform navBar,
            Button generatedButton)
        {
            generatedButton.name = "BtnCardBuilder";
            generatedButton.onClick = new Button.ButtonClickedEvent();
            SetButtonLabel(generatedButton, "Card Builder");
            navigator.btnCardBuilder = generatedButton;
            return generatedButton;
        }

        private static UICardPartOption CreateOptionPrefab()
        {
            GameObject root = CreateUIObject("CardPartOption", null);
            RectTransform rootRect = (RectTransform)root.transform;
            rootRect.sizeDelta = new Vector2(120f, 104f);

            Image background = root.AddComponent<Image>();
            background.color = ButtonColor;
            Button button = root.AddComponent<Button>();
            button.targetGraphic = background;
            LayoutElement layout = root.AddComponent<LayoutElement>();
            layout.preferredWidth = 120f;
            layout.preferredHeight = 104f;

            GameObject iconObject = CreateUIObject("Icon", root.transform);
            RectTransform iconRect = (RectTransform)iconObject.transform;
            iconRect.anchorMin = new Vector2(0.08f, 0.30f);
            iconRect.anchorMax = new Vector2(0.92f, 0.95f);
            iconRect.offsetMin = Vector2.zero;
            iconRect.offsetMax = Vector2.zero;
            Image icon = iconObject.AddComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            TextMeshProUGUI label = CreateLabel("Label", root.transform, "Part", 16f);
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0.04f, 0.02f);
            labelRect.anchorMax = new Vector2(0.96f, 0.28f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            label.alignment = TextAlignmentOptions.Center;

            UICardPartOption option = root.AddComponent<UICardPartOption>();
            option.BindVisuals(button, icon, label);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, OptionPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return saved.GetComponent<UICardPartOption>();
        }

        private static void CreatePanelPrefab(UICardPartOption optionPrefab)
        {
            GameObject previewPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CardPreviewPath);
            if (previewPrefab == null)
            {
                throw new InvalidOperationException($"CardPreview prefab is missing at {CardPreviewPath}.");
            }

            GameObject root = CreateUIObject("CardBuilderPanel", null);
            Stretch((RectTransform)root.transform);
            Image panelBackground = root.AddComponent<Image>();
            panelBackground.color = PanelColor;

            HorizontalLayoutGroup layout = root.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(28, 28, 22, 22);
            layout.spacing = 28f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            GameObject previewColumn = CreateSurface("PreviewColumn", root.transform);
            LayoutElement previewColumnLayout = previewColumn.AddComponent<LayoutElement>();
            previewColumnLayout.preferredWidth = 430f;
            previewColumnLayout.flexibleWidth = 0.75f;
            VerticalLayoutGroup previewLayout = previewColumn.AddComponent<VerticalLayoutGroup>();
            previewLayout.padding = new RectOffset(18, 18, 18, 18);
            previewLayout.spacing = 12f;
            previewLayout.childAlignment = TextAnchor.UpperCenter;
            previewLayout.childControlWidth = true;
            previewLayout.childControlHeight = false;
            previewLayout.childForceExpandWidth = true;
            previewLayout.childForceExpandHeight = false;

            TextMeshProUGUI title = CreateLabel("Title", previewColumn.transform, "CARD BUILDER", 30f);
            title.fontStyle = FontStyles.Bold;
            title.color = AccentColor;
            AddPreferredHeight(title.gameObject, 44f);

            GameObject previewHost = CreateUIObject("CardPreviewHost", previewColumn.transform);
            LayoutElement previewHostLayout = previewHost.AddComponent<LayoutElement>();
            previewHostLayout.preferredWidth = 320f;
            previewHostLayout.preferredHeight = 448f;
            previewHostLayout.flexibleWidth = 0f;
            RectTransform previewHostRect = (RectTransform)previewHost.transform;
            previewHostRect.sizeDelta = new Vector2(320f, 448f);

            GameObject previewInstance = (GameObject)PrefabUtility.InstantiatePrefab(previewPrefab);
            previewInstance.name = "CardPreview";
            previewInstance.transform.SetParent(previewHost.transform, false);
            Stretch((RectTransform)previewInstance.transform);
            UICardPreview preview = previewInstance.GetComponent<UICardPreview>();

            TextMeshProUGUI summary = CreateLabel(
                "SelectionSummary",
                previewColumn.transform,
                "Blank card — choose any Head, Body, or Legs.",
                17f);
            summary.color = new Color(0.86f, 0.87f, 0.92f, 1f);
            summary.textWrappingMode = TextWrappingModes.Normal;
            AddPreferredHeight(summary.gameObject, 54f);

            GameObject choicesColumn = CreateSurface("ChoicesColumn", root.transform);
            choicesColumn.AddComponent<LayoutElement>().flexibleWidth = 1.75f;
            VerticalLayoutGroup choicesLayout = choicesColumn.AddComponent<VerticalLayoutGroup>();
            choicesLayout.padding = new RectOffset(20, 20, 18, 18);
            choicesLayout.spacing = 14f;
            choicesLayout.childControlWidth = true;
            choicesLayout.childControlHeight = false;
            choicesLayout.childForceExpandWidth = true;
            choicesLayout.childForceExpandHeight = false;

            TextMeshProUGUI instructions = CreateLabel(
                "Instructions",
                choicesColumn.transform,
                "Select a part to equip it immediately. Select another to replace it, or use Remove to leave that slot empty.",
                19f);
            instructions.alignment = TextAlignmentOptions.Left;
            instructions.textWrappingMode = TextWrappingModes.Normal;
            AddPreferredHeight(instructions.gameObject, 58f);

            RectTransform headContainer = CreatePartSection(
                choicesColumn.transform, "Head", out Button removeHead);
            RectTransform bodyContainer = CreatePartSection(
                choicesColumn.transform, "Body", out Button removeBody);
            RectTransform legsContainer = CreatePartSection(
                choicesColumn.transform, "Legs", out Button removeLegs);

            Button clearCard = CreateButton(
                "BtnClearCard",
                choicesColumn.transform,
                "CLEAR ALL — BLANK CARD",
                new Color(0.62f, 0.24f, 0.24f, 1f),
                46f);

            UICardBuilderView view = root.AddComponent<UICardBuilderView>();
            view.Configure(
                preview,
                optionPrefab,
                headContainer,
                bodyContainer,
                legsContainer,
                removeHead,
                removeBody,
                removeLegs,
                clearCard,
                summary);

            CardBuilderPresenter presenter = root.AddComponent<CardBuilderPresenter>();
            presenter.Configure(
                view,
                CreateOptions(CardBuilderPartSlot.Head, "head"),
                CreateOptions(CardBuilderPartSlot.Body, "body"),
                CreateOptions(CardBuilderPartSlot.Legs, "legs"));
            presenter.ConfigureSpriteBindings(CreateAvailableAssetSpriteBindings());

            PrefabUtility.SaveAsPrefabAsset(root, PanelPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
        }

        private static CardBuilderPartOptionDefinition[] CreateOptions(
            CardBuilderPartSlot slot,
            string fileSuffix)
        {
            string[] characters = { "abogado", "magsasaka", "pari" };
            string[] rarities = { "Rare", "Common", "Rare" };
            string category = slot == CardBuilderPartSlot.Head
                ? "Head"
                : slot == CardBuilderPartSlot.Body ? "Body" : "Legs";
            return characters
                .Select((character, index) => new CardBuilderPartOptionDefinition(
                    $"{character}_{fileSuffix}",
                    ObjectNames.NicifyVariableName(character),
                    slot,
                    LoadFirstSprite(
                        $"Assets/Art/Parts/{category}/{rarities[index]}/{character} {fileSuffix}.png")))
                .Where(option => option.Sprite != null)
                .ToArray();
        }

        private static Sprite LoadFirstSprite(string path)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null)
            {
                return sprite;
            }

            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
        }

        internal static UIItemSpriteBinding[] CreateAvailableAssetSpriteBindings()
        {
            return new[]
            {
                Bind("common_dayami", "Assets/Art/Materials/Common/Dayami.png"),
                Bind("common_tali", "Assets/Art/Materials/Common/Tali.png"),
                Bind("common_tela", "Assets/Art/Materials/Common/Tela.png"),
                Bind("common_goma", "Assets/Art/Materials/Common/Goma.png"),
                Bind("common_bakal", "Assets/Art/Materials/Common/Bakal.png"),
                Bind("common_kandila", "Assets/Art/Materials/Common/Kandila.png"),
                Bind("rare_salamin", "Assets/Art/Materials/Rare/Salamin.png"),
                Bind("rare_tinta", "Assets/Art/Materials/Rare/Tinta.png"),
                Bind("rare_tsapa", "Assets/Art/Materials/Rare/Tsapa.png"),
                Bind("rare_rosaryo", "Assets/Art/Materials/Rare/Rosaryo.png"),
                Bind("epic_ngipin_dragon", "Assets/Art/Materials/Epic/Ngipin ng Dragon.png"),
                Bind("epic_lawas_dragon", "Assets/Art/Materials/Epic/Lawas ng Dragon.png"),
                Bind("epic_kuko_dragon", "Assets/Art/Materials/Epic/Kuko ng Dragon.png"),
                Bind("part_common_magsasaka_head", "Assets/Art/Parts/Head/Common/magsasaka head.png"),
                Bind("part_common_magsasaka_body", "Assets/Art/Parts/Body/Common/magsasaka body.png"),
                Bind("part_common_magsasaka_legs", "Assets/Art/Parts/Legs/Common/magsasaka legs.png"),
                Bind("part_rare_abogado_head", "Assets/Art/Parts/Head/Rare/abogado head.png"),
                Bind("part_rare_abogado_body", "Assets/Art/Parts/Body/Rare/abogado body.png"),
                Bind("part_rare_abogado_legs", "Assets/Art/Parts/Legs/Rare/abogado legs.png"),
                Bind("part_rare_pari_head", "Assets/Art/Parts/Head/Rare/pari head.png"),
                Bind("part_rare_pari_body", "Assets/Art/Parts/Body/Rare/pari body.png"),
                Bind("part_rare_pari_legs", "Assets/Art/Parts/Legs/Rare/pari legs.png"),
                Bind("part_epic_bakunawa_head", "Assets/Art/Parts/Head/Epic/bakunawa head.png"),
                Bind("part_epic_bakunawa_body_legs",
                    "Assets/Art/Parts/Hybrid/Body&Legs/Epic/bakunawa body&legs.png")
            };
        }

        private static UIItemSpriteBinding Bind(string itemId, string assetPath)
        {
            return new UIItemSpriteBinding(itemId, LoadFirstSprite(assetPath));
        }

        private static RectTransform CreatePartSection(
            Transform parent,
            string title,
            out Button removeButton)
        {
            GameObject section = CreateUIObject($"{title}Section", parent);
            section.AddComponent<LayoutElement>().preferredHeight = 164f;
            HorizontalLayoutGroup sectionLayout = section.AddComponent<HorizontalLayoutGroup>();
            sectionLayout.spacing = 12f;
            sectionLayout.childAlignment = TextAnchor.MiddleLeft;
            sectionLayout.childControlWidth = false;
            sectionLayout.childControlHeight = true;
            sectionLayout.childForceExpandWidth = false;
            sectionLayout.childForceExpandHeight = true;

            TextMeshProUGUI heading = CreateLabel($"{title}Label", section.transform, title.ToUpperInvariant(), 21f);
            heading.fontStyle = FontStyles.Bold;
            heading.color = AccentColor;
            heading.alignment = TextAlignmentOptions.Left;
            LayoutElement headingLayout = heading.gameObject.AddComponent<LayoutElement>();
            headingLayout.preferredWidth = 84f;

            GameObject options = CreateUIObject($"{title}Options", section.transform);
            LayoutElement optionsLayout = options.AddComponent<LayoutElement>();
            optionsLayout.preferredWidth = 384f;
            optionsLayout.flexibleWidth = 1f;
            GridLayoutGroup grid = options.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(120f, 104f);
            grid.spacing = new Vector2(8f, 8f);
            grid.constraint = GridLayoutGroup.Constraint.FixedRowCount;
            grid.constraintCount = 1;
            grid.childAlignment = TextAnchor.MiddleLeft;

            removeButton = CreateButton(
                $"BtnRemove{title}",
                section.transform,
                "Remove",
                ButtonColor,
                42f);
            removeButton.gameObject.GetComponent<LayoutElement>().preferredWidth = 104f;
            return (RectTransform)options.transform;
        }

        private static Button FindOrCreateNavigationButton(LobbyNavigator navigator, Transform navBar)
        {
            Transform existing = FindDirectChild(navBar, "BtnCardBuilder");
            if (existing != null && existing.TryGetComponent(out Button existingButton))
            {
                SetButtonLabel(existingButton, "Card Builder");
                return existingButton;
            }

            GameObject buttonObject;
            if (navigator.btnSkill != null)
            {
                buttonObject = UnityEngine.Object.Instantiate(navigator.btnSkill.gameObject, navBar, false);
                buttonObject.name = "BtnCardBuilder";
                Button copiedButton = buttonObject.GetComponent<Button>();
                copiedButton.onClick = new Button.ButtonClickedEvent();
                SetButtonLabel(copiedButton, "Card Builder");
                return copiedButton;
            }

            return CreateButton("BtnCardBuilder", navBar, "Card Builder", ButtonColor, 56f);
        }

        private static void SetButtonLabel(Button button, string text)
        {
            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                label.text = text;
            }
        }

        private static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>(true);
                if (component != null)
                {
                    return component;
                }
            }

            return null;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDescendant(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static Transform FindDirectChild(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == name)
                {
                    return child;
                }
            }

            return null;
        }

        private static GameObject CreateSurface(string name, Transform parent)
        {
            GameObject surface = CreateUIObject(name, parent);
            Image image = surface.AddComponent<Image>();
            image.color = SurfaceColor;
            return surface;
        }

        private static GameObject CreateUIObject(string name, Transform parent)
        {
            GameObject gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.layer = 5;
            if (parent != null)
            {
                gameObject.transform.SetParent(parent, false);
            }

            return gameObject;
        }

        private static TextMeshProUGUI CreateLabel(
            string name,
            Transform parent,
            string text,
            float fontSize)
        {
            GameObject labelObject = CreateUIObject(name, parent);
            TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            label.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            return label;
        }

        private static Button CreateButton(
            string name,
            Transform parent,
            string text,
            Color color,
            float height)
        {
            GameObject buttonObject = CreateUIObject(name, parent);
            Image image = buttonObject.AddComponent<Image>();
            image.color = color;
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            LayoutElement layout = buttonObject.AddComponent<LayoutElement>();
            layout.preferredHeight = height;
            layout.minHeight = height;

            TextMeshProUGUI label = CreateLabel("Label", buttonObject.transform, text, 17f);
            Stretch(label.rectTransform);
            label.fontStyle = FontStyles.Bold;
            return button;
        }

        private static void AddPreferredHeight(GameObject gameObject, float height)
        {
            LayoutElement layout = gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = height;
            layout.minHeight = height;
        }

        private static void Stretch(RectTransform rectTransform)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
        }
    }
}
#endif
