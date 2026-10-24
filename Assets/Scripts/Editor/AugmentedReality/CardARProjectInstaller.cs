#if APIRAPIR_ARFOUNDATION
using System;
using System.Collections.Generic;
using System.IO;
using ApirApir.AugmentedReality;
using ApirApir.UI;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.ARCore;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;

namespace ApirApir.EditorTools
{
    public static class CardARProjectInstaller
    {
        private const string ScenePath = "Assets/Scenes/CardAR.unity";
        private const string PrefabPath = "Assets/Ar/ARCardTrack.prefab";
        private const string XRGeneralSettingsPath = "Assets/XR/Settings/XRGeneralSettingsPerBuildTarget.asset";

        private static readonly (string Id, string Guid)[] PartSprites =
        {
            ("part_common_magsasaka_head", "c079e6f5337fb7f4ab7349f7a5cc3e2e"),
            ("part_common_magsasaka_body", "bb2c81c797db7f34eb946b28126a9d17"),
            ("part_common_magsasaka_legs", "01fc04318dd5db34bbc302adbcc783e0"),
            ("part_rare_abogado_head", "3a92461d403beba4dbe667df3b9ce705"),
            ("part_rare_abogado_body", "5ef75b0782d77084e8a20465f9f40a39"),
            ("part_rare_abogado_legs", "00ca3cb7c6b5d4a4a9a7879aef48435e"),
            ("part_epic_bakunawa_head", "a7285f640603e314e8b6402721802ba9"),
            ("part_epic_bakunawa_body_legs", "36a77289323984b4f8f9ba799754dad6")
        };

        [InitializeOnLoadMethod]
        private static void InstallMissingSceneAfterReload()
        {
            EditorApplication.delayCall += TryAutoInstall;
        }

        private static void TryAutoInstall()
        {
            if (EditorApplication.isCompiling)
            {
                EditorApplication.delayCall += TryAutoInstall;
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode || File.Exists(ScenePath)) return;
            try { Install(); }
            catch (Exception exception) { Debug.LogException(exception); }
        }

        [MenuItem("APIR APIR/Card AR/Install Card AR Scene and Android Settings")]
        public static void Install()
        {
            ConfigureAndroid();
            ConfigureARCoreLoader();
            CreateScene();
            AddSceneToBuildSettings();
            AssetDatabase.SaveAssets();
            Debug.Log("APIR APIR Card AR installation completed.");
        }

        public static void RunFromCommandLine()
        {
            try { Install(); }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
            EditorApplication.Exit(0);
        }

        private static void ConfigureAndroid()
        {
            if ((int)PlayerSettings.Android.minSdkVersion < (int)AndroidSdkVersions.AndroidApiLevel25)
                PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel25;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            GraphicsDeviceType[] current = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
            var supported = new List<GraphicsDeviceType>();
            for (int i = 0; i < current.Length; i++)
                if (current[i] == GraphicsDeviceType.Vulkan || current[i] == GraphicsDeviceType.OpenGLES3)
                    supported.Add(current[i]);
            if (supported.Count == 0)
                supported.Add(GraphicsDeviceType.OpenGLES3);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, supported.ToArray());

            AddDefine(NamedBuildTarget.Android, ARFoundationIntegrationStatus.RequiredScriptingDefine);
            AddDefine(NamedBuildTarget.Standalone, ARFoundationIntegrationStatus.RequiredScriptingDefine);
        }

        private static void ConfigureARCoreLoader()
        {
            BuildTargetGroup group = BuildTargetGroup.Android;
            XRGeneralSettingsPerBuildTarget allSettings = FindOrCreateXRSettings();
            if (!allSettings.HasManagerSettingsForBuildTarget(group))
                allSettings.CreateDefaultManagerSettingsForBuildTarget(group);
            XRGeneralSettings settings = allSettings.SettingsForBuildTarget(group);
            if (settings == null || settings.Manager == null)
            {
                Debug.LogWarning("XR Plug-in Management settings could not be initialized for Android.");
                return;
            }

            if (!XRPackageMetadataStore.AssignLoader(settings.Manager, typeof(ARCoreLoader).FullName, group))
                Debug.LogWarning("ARCore loader assignment was not changed; verify it under XR Plug-in Management > Android.");
            settings.Manager.automaticLoading = true;
            settings.Manager.automaticRunning = true;
            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(settings.Manager);
        }

        private static XRGeneralSettingsPerBuildTarget FindOrCreateXRSettings()
        {
            string[] guids = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget");
            XRGeneralSettingsPerBuildTarget settings = guids.Length == 0
                ? null
                : AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(
                    AssetDatabase.GUIDToAssetPath(guids[0]));
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(settings, XRGeneralSettingsPath);
            }
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, settings, true);
            return settings;
        }

        private static void CreateScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            GameObject sessionObject = new GameObject("AR Session", typeof(ARSession), typeof(ARInputManager));
            ARSession arSession = sessionObject.GetComponent<ARSession>();

            GameObject originObject = new GameObject("XR Origin", typeof(XROrigin));
            XROrigin origin = originObject.GetComponent<XROrigin>();
            GameObject cameraOffset = new GameObject("Camera Offset");
            cameraOffset.transform.SetParent(originObject.transform, false);
            GameObject cameraObject = new GameObject("AR Camera", typeof(Camera), typeof(AudioListener),
                typeof(ARCameraManager), typeof(ARCameraBackground));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(cameraOffset.transform, false);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            origin.CameraFloorOffsetObject = cameraOffset;
            origin.Camera = camera;
            ARCameraManager cameraManager = cameraObject.GetComponent<ARCameraManager>();

            GameObject runtime = new GameObject("Card AR Runtime");
            ARFoundationSessionAdapter sessionAdapter = runtime.AddComponent<ARFoundationSessionAdapter>();
            ARFoundationQRCodeScanner scanner = runtime.AddComponent<ARFoundationQRCodeScanner>();
            ARCardSpriteResolver resolver = runtime.AddComponent<ARCardSpriteResolver>();
            CardARSceneController controller = runtime.AddComponent<CardARSceneController>();

            SetObjectReference(sessionAdapter, "arSession", arSession);
            SetObjectReference(sessionAdapter, "xrOrigin", origin);
            SetObjectReference(sessionAdapter, "cameraManager", cameraManager);
            scanner.Configure(cameraManager);
            ConfigureSpriteBindings(resolver);

            Canvas canvas = CreateCanvas();
            TMP_Text status = CreateText("TrackingStatus", canvas.transform,
                "Starting AR camera…", 26, TextAlignmentOptions.Center,
                new Vector2(0.08f, 0.88f), new Vector2(0.92f, 0.97f));
            Button back = CreateButton("BackButton", canvas.transform, "BACK TO LOBBY",
                new Vector2(0.03f, 0.03f), new Vector2(0.25f, 0.11f));
            Button authorCard = CreateButton("AuthorCardButton", canvas.transform, "CREATE / VIEW CARD",
                new Vector2(0.27f, 0.03f), new Vector2(0.51f, 0.11f));

            GameObject displayRoot = new GameObject("RecognizedCardDisplay", typeof(RectTransform));
            displayRoot.transform.SetParent(canvas.transform, false);
            Stretch((RectTransform)displayRoot.transform);
            GameObject cardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (cardPrefab == null)
                throw new InvalidOperationException("Required prefab was not found at " + PrefabPath);
            GameObject card = (GameObject)PrefabUtility.InstantiatePrefab(cardPrefab, displayRoot.transform);
            card.name = "ARCardTrack";
            card.transform.localPosition = Vector3.zero;
            card.transform.localRotation = Quaternion.identity;
            card.transform.localScale = Vector3.one;

            Transform cardCanvas = FindDescendant(card.transform, "Canvas");
            if (cardCanvas == null)
                throw new InvalidOperationException("ARCardTrack requires its existing Canvas child.");
            RectTransform cardRect = cardCanvas.GetComponent<RectTransform>();
            cardRect.anchorMin = cardRect.anchorMax = new Vector2(0.28f, 0.52f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.anchoredPosition = Vector2.zero;
            cardRect.sizeDelta = new Vector2(120f, 180f);
            cardRect.localScale = new Vector3(3f, 3f, 1f);
            Image background = FindDescendant(cardCanvas, "Background").GetComponent<Image>();
            TMP_Text uniqueId = FindDescendant(cardCanvas, "UniqueID").GetComponent<TMP_Text>();
            Transform qrTransform = FindDescendant(cardCanvas, "QRArea");
            RectTransform qr = qrTransform as RectTransform;
            Image qrImage = qrTransform == null ? null : qrTransform.GetComponent<Image>();
            if (qr != null) qr.sizeDelta = new Vector2(64f, 64f);
            GameObject artwork = new GameObject("CharacterArtwork", typeof(RectTransform));
            artwork.transform.SetParent(cardCanvas, false);
            Stretch(artwork.GetComponent<RectTransform>());
            artwork.transform.SetSiblingIndex(1);
            Image legs = CreateImage("LegsSlot", artwork.transform);
            Image body = CreateImage("BodySlot", artwork.transform);
            Image head = CreateImage("HeadSlot", artwork.transform);
            UICardPreview preview = artwork.AddComponent<UICardPreview>();
            preview.BindSlots(background, legs, body, head);
            preview.ConfigureLayout(
                new Vector2(0f, 30f), new Vector2(84f, 60f),
                new Vector2(0f, 1f), new Vector2(92f, 78f),
                new Vector2(0f, -30f), new Vector2(84f, 60f));

            TMP_Text name = CreateText("CardName", displayRoot.transform, "Card Name", 30,
                TextAlignmentOptions.Left, new Vector2(0.52f, 0.72f), new Vector2(0.94f, 0.82f));
            TMP_Text classification = CreateText("Classification", displayRoot.transform, string.Empty, 19,
                TextAlignmentOptions.Left, new Vector2(0.52f, 0.63f), new Vector2(0.94f, 0.71f));
            TMP_Text stats = CreateText("Statistics", displayRoot.transform, string.Empty, 20,
                TextAlignmentOptions.TopLeft, new Vector2(0.52f, 0.43f), new Vector2(0.94f, 0.62f));
            TMP_Text skills = CreateText("Skills", displayRoot.transform, string.Empty, 19,
                TextAlignmentOptions.TopLeft, new Vector2(0.52f, 0.22f), new Vector2(0.94f, 0.42f));
            ARCardDisplayView display = runtime.AddComponent<ARCardDisplayView>();
            display.Configure(displayRoot, preview, name, uniqueId, skills, stats, classification, qrImage);
            controller.Configure(sessionAdapter, scanner, display, resolver, status, back, authorCard);
            display.Hide();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException("Unity could not save " + ScenePath);
            EditorSceneManager.CloseScene(scene, true);
        }

        private static Canvas CreateCanvas()
        {
            GameObject root = new GameObject("Card AR UI", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            return canvas;
        }

        private static void ConfigureSpriteBindings(ARCardSpriteResolver resolver)
        {
            SerializedObject serialized = new SerializedObject(resolver);
            SerializedProperty array = serialized.FindProperty("bindings");
            array.arraySize = PartSprites.Length;
            for (int i = 0; i < PartSprites.Length; i++)
            {
                SerializedProperty element = array.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("visualAssetId").stringValue = PartSprites[i].Id;
                string path = AssetDatabase.GUIDToAssetPath(PartSprites[i].Guid);
                element.FindPropertyRelative("sprite").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<Sprite>(path);
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddSceneToBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (int i = 0; i < scenes.Count; i++)
            {
                if (string.Equals(scenes[i].path, ScenePath, StringComparison.Ordinal)) return;
            }
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void AddDefine(NamedBuildTarget target, string define)
        {
            string current = PlayerSettings.GetScriptingDefineSymbols(target);
            var values = new HashSet<string>(current.Split(';'), StringComparer.Ordinal);
            if (values.Add(define))
                PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", values));
        }

        private static void SetObjectReference(UnityEngine.Object target, string property, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(property).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Image CreateImage(string name, Transform parent)
        {
            GameObject value = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            value.transform.SetParent(parent, false);
            Image image = value.GetComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        private static TMP_Text CreateText(
            string name, Transform parent, string text, float size, TextAlignmentOptions alignment,
            Vector2 anchorMin, Vector2 anchorMax)
        {
            GameObject value = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            value.transform.SetParent(parent, false);
            RectTransform rect = value.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            TextMeshProUGUI label = value.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.alignment = alignment;
            label.color = Color.white;
            label.textWrappingMode = TextWrappingModes.Normal;
            return label;
        }

        private static Button CreateButton(string name, Transform parent, string label, Vector2 min, Vector2 max)
        {
            GameObject value = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            value.transform.SetParent(parent, false);
            RectTransform rect = value.GetComponent<RectTransform>();
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            value.GetComponent<Image>().color = new Color(0.18f, 0.25f, 0.34f, 0.95f);
            CreateText("Label", value.transform, label, 20, TextAlignmentOptions.Center, Vector2.zero, Vector2.one);
            return value.GetComponent<Button>();
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDescendant(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
#endif
