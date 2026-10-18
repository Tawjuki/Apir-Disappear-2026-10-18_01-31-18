#if UNITY_EDITOR
using System.IO;
using ApirApir.Patintero;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ApirApir.Editor
{
    public static class PatinteroPrototypeBuilder
    {
        private const string Root = "Assets/APIRAPIR/Patintero";
        private const string SettingsPath = Root + "/Config/PatinteroSettings.asset";
        private const string NetworkPrefabsPath = Root + "/Config/PatinteroNetworkPrefabs.asset";
        private const string PlayerPrefabPath = Root + "/Prefabs/PatinteroPlayer.prefab";
        private const string ScenePath = "Assets/Scenes/PatinteroPrototype.unity";

        [MenuItem("Tools/APIR APIR/Build Patintero Prototype")]
        public static void BuildFromMenu() => BuildAll();

        public static void BuildAll()
        {
            EnsureFolders();
            PatinteroSettings settings = CreateOrLoadSettings();
            if (settings == null)
            {
                throw new InvalidDataException($"Could not load the Patintero settings asset at {SettingsPath}.");
            }

            GameObject playerPrefab = CreatePlayerPrefab();
            NetworkPrefabsList networkPrefabs = CreateOrLoadNetworkPrefabs(playerPrefab);
            CreateScene(settings, playerPrefab, networkPrefabs);
            ConfigureBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Patintero prototype generated successfully at {ScenePath}");
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets/APIRAPIR");
            EnsureFolder(Root);
            EnsureFolder(Root + "/Config");
            EnsureFolder(Root + "/Prefabs");
            EnsureFolder(Root + "/Materials");
            EnsureFolder("Assets/Scenes");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private static PatinteroSettings CreateOrLoadSettings()
        {
            PatinteroSettings settings = AssetDatabase.LoadAssetAtPath<PatinteroSettings>(SettingsPath);
            if (settings != null) return settings;

            settings = ScriptableObject.CreateInstance<PatinteroSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssetIfDirty(settings);
            return AssetDatabase.LoadAssetAtPath<PatinteroSettings>(SettingsPath);
        }

        private static GameObject CreatePlayerPrefab()
        {
            GameObject root = new("PatinteroPlayer");
            root.layer = LayerMask.NameToLayer("Default");
            root.AddComponent<NetworkObject>();
            root.AddComponent<NetworkTransform>();
            root.AddComponent<PatinteroKeyboardInput>();
            PatinteroPlayer player = root.AddComponent<PatinteroPlayer>();

            CapsuleCollider collider = root.AddComponent<CapsuleCollider>();
            collider.center = new Vector3(0f, 0f, 0f);
            collider.height = 2f;
            collider.radius = 0.45f;

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            Object.DestroyImmediate(body.GetComponent<Collider>());
            Renderer bodyRenderer = body.GetComponent<Renderer>();
            bodyRenderer.sharedMaterial = GetOrCreateMaterial("PlayerBase", new Color(0.1f, 0.55f, 0.95f));

            GameObject direction = GameObject.CreatePrimitive(PrimitiveType.Cube);
            direction.name = "DirectionMarker";
            direction.transform.SetParent(root.transform, false);
            direction.transform.localPosition = new Vector3(0f, 0.35f, 0.5f);
            direction.transform.localScale = new Vector3(0.35f, 0.2f, 0.35f);
            Object.DestroyImmediate(direction.GetComponent<Collider>());
            direction.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("Direction", Color.white);

            SerializedObject serializedPlayer = new(player);
            serializedPlayer.FindProperty("bodyRenderer").objectReferenceValue = bodyRenderer;
            serializedPlayer.ApplyModifiedPropertiesWithoutUndo();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static NetworkPrefabsList CreateOrLoadNetworkPrefabs(GameObject playerPrefab)
        {
            NetworkPrefabsList prefabs = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
            if (prefabs == null)
            {
                prefabs = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(prefabs, NetworkPrefabsPath);
            }

            if (!prefabs.Contains(playerPrefab))
            {
                prefabs.Add(new NetworkPrefab { Prefab = playerPrefab });
                EditorUtility.SetDirty(prefabs);
            }

            AssetDatabase.SaveAssetIfDirty(prefabs);
            return prefabs;
        }

        private static void CreateScene(
            PatinteroSettings settings,
            GameObject playerPrefab,
            NetworkPrefabsList networkPrefabs)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "PatinteroPrototype";

            CreateEnvironment(settings);
            CreateCamera(settings);
            CreateLighting();

            GameObject networkObject = new("NetworkManager");
            NetworkManager networkManager = networkObject.AddComponent<NetworkManager>();
            UnityTransport transport = networkObject.AddComponent<UnityTransport>();
            networkManager.NetworkConfig.NetworkTransport = transport;
            networkManager.NetworkConfig.PlayerPrefab = playerPrefab;
            networkManager.NetworkConfig.Prefabs.NetworkPrefabsLists.Clear();
            networkManager.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(networkPrefabs);
            networkManager.NetworkConfig.ConnectionApproval = true;
            networkManager.NetworkConfig.EnableSceneManagement = true;
            networkManager.NetworkConfig.TickRate = 30;

            GameObject gameObject = new("PatinteroGameManager");
            gameObject.AddComponent<NetworkObject>();
            PatinteroGameManager gameManager = gameObject.AddComponent<PatinteroGameManager>();
            gameManager.SetSettingsForEditor(settings);
            if (gameManager.Settings != settings)
            {
                throw new InvalidDataException("The Patintero settings reference could not be assigned to the game manager.");
            }

            EditorUtility.SetDirty(gameManager);
            EditorUtility.SetDirty(networkManager);

            CreateInterface();
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static void CreateEnvironment(PatinteroSettings settings)
        {
            GameObject environment = new("Court");
            Material ground = GetOrCreateMaterial("CourtGround", new Color(0.82f, 0.68f, 0.43f));
            Material white = GetOrCreateMaterial("CourtWhite", new Color(0.95f, 0.94f, 0.85f));
            Material red = GetOrCreateMaterial("GuardRed", new Color(0.9f, 0.2f, 0.16f));
            Material blue = GetOrCreateMaterial("StartBlue", new Color(0.12f, 0.5f, 0.9f));
            Material yellow = GetOrCreateMaterial("FarYellow", new Color(1f, 0.78f, 0.12f));
            Material grass = GetOrCreateMaterial("Grass", new Color(0.2f, 0.48f, 0.22f));

            CreateBox("Grass", environment.transform, new Vector3(0f, -0.18f, 0f),
                new Vector3(settings.courtWidth + 7f, 0.2f, settings.courtLength + 7f), grass);
            CreateBox("CourtSurface", environment.transform, new Vector3(0f, -0.05f, 0f),
                new Vector3(settings.courtWidth, 0.15f, settings.courtLength), ground);

            float lineWidth = 0.12f;
            CreateBox("LeftBoundary", environment.transform, new Vector3(-settings.HalfWidth, 0.04f, 0f),
                new Vector3(lineWidth, 0.04f, settings.courtLength), white);
            CreateBox("RightBoundary", environment.transform, new Vector3(settings.HalfWidth, 0.04f, 0f),
                new Vector3(lineWidth, 0.04f, settings.courtLength), white);
            CreateBox("StartBoundary", environment.transform, new Vector3(0f, 0.04f, -settings.HalfLength),
                new Vector3(settings.courtWidth, 0.04f, lineWidth), white);
            CreateBox("FarBoundary", environment.transform, new Vector3(0f, 0.04f, settings.HalfLength),
                new Vector3(settings.courtWidth, 0.04f, lineWidth), white);
            CreateBox("StartZone", environment.transform, new Vector3(0f, 0.035f, settings.StartThresholdZ),
                new Vector3(settings.courtWidth, 0.03f, 0.28f), blue);
            CreateBox("FarZone", environment.transform, new Vector3(0f, 0.035f, settings.FarThresholdZ),
                new Vector3(settings.courtWidth, 0.03f, 0.28f), yellow);

            for (int i = 0; i < settings.guardingLineZ.Length; i++)
            {
                CreateBox($"GuardLine_{i + 1}", environment.transform,
                    new Vector3(0f, 0.045f, settings.guardingLineZ[i]),
                    new Vector3(settings.courtWidth, 0.05f, 0.24f), red);
            }
        }

        private static void CreateCamera(PatinteroSettings settings)
        {
            GameObject cameraObject = new("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(settings.courtLength * 0.62f, settings.courtWidth * 0.72f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.2f, 0.16f);
            cameraObject.transform.position = new Vector3(0f, 20f, 0f);
            cameraObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            cameraObject.AddComponent<AudioListener>();
        }

        private static void CreateLighting()
        {
            GameObject lightObject = new("Sun");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightObject.transform.rotation = Quaternion.Euler(48f, -30f, 0f);
        }

        private static void CreateInterface()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            GameObject canvasObject = new("PatinteroHUD");
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            canvasObject.AddComponent<GraphicRaycaster>();

            GameObject connectionPanel = CreatePanel("ConnectionPanel", canvasObject.transform,
                new Vector2(12f, -12f), new Vector2(430f, 166f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            Text title = CreateText("Title", connectionPanel.transform, "APIR APIR - PATINTERO", font, 24,
                TextAnchor.MiddleLeft, new Vector2(14f, -10f), new Vector2(400f, 34f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            InputField address = CreateInput("Address", connectionPanel.transform, "127.0.0.1", font,
                new Vector2(14f, -52f), new Vector2(238f, 34f));
            InputField port = CreateInput("Port", connectionPanel.transform, "7777", font,
                new Vector2(260f, -52f), new Vector2(76f, 34f));
            Button host = CreateButton("HostButton", connectionPanel.transform, "HOST", font,
                new Vector2(14f, -94f), new Vector2(94f, 34f));
            Button join = CreateButton("JoinButton", connectionPanel.transform, "JOIN", font,
                new Vector2(116f, -94f), new Vector2(94f, 34f));
            Button disconnect = CreateButton("DisconnectButton", connectionPanel.transform, "DISCONNECT", font,
                new Vector2(218f, -94f), new Vector2(118f, 34f));
            Text connection = CreateText("ConnectionStatus", connectionPanel.transform, "Offline - host or join a LAN game",
                font, 16, TextAnchor.MiddleLeft, new Vector2(14f, -132f), new Vector2(400f, 26f),
                new Vector2(0f, 1f), new Vector2(0f, 1f));

            GameObject statusPanel = CreatePanel("StatusPanel", canvasObject.transform,
                new Vector2(-12f, -12f), new Vector2(430f, 188f), new Vector2(1f, 1f), new Vector2(1f, 1f));
            Text timer = CreateText("Timer", statusPanel.transform, "Time: --", font, 34, TextAnchor.MiddleRight,
                new Vector2(-414f, -10f), new Vector2(400f, 44f), new Vector2(1f, 1f), new Vector2(1f, 1f));
            Text role = CreateText("Role", statusPanel.transform, "Role: --", font, 19, TextAnchor.MiddleRight,
                new Vector2(-414f, -60f), new Vector2(400f, 34f), new Vector2(1f, 1f), new Vector2(1f, 1f));
            Text round = CreateText("Round", statusPanel.transform, "Waiting for network", font, 18, TextAnchor.MiddleRight,
                new Vector2(-414f, -98f), new Vector2(400f, 30f), new Vector2(1f, 1f), new Vector2(1f, 1f));
            Button restart = CreateButton("RestartButton", statusPanel.transform, "RESTART ROUND", font,
                new Vector2(-174f, -140f), new Vector2(160f, 34f), new Vector2(1f, 1f));

            Text result = CreateText("Result", canvasObject.transform, string.Empty, font, 52, TextAnchor.MiddleCenter,
                new Vector2(-350f, 48f), new Vector2(700f, 76f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            result.color = new Color(1f, 0.86f, 0.2f);

            PatinteroNetworkUI ui = canvasObject.AddComponent<PatinteroNetworkUI>();
            SerializedObject serialized = new(ui);
            SetReference(serialized, "addressInput", address);
            SetReference(serialized, "portInput", port);
            SetReference(serialized, "hostButton", host);
            SetReference(serialized, "joinButton", join);
            SetReference(serialized, "disconnectButton", disconnect);
            SetReference(serialized, "restartButton", restart);
            SetReference(serialized, "connectionText", connection);
            SetReference(serialized, "timerText", timer);
            SetReference(serialized, "roleText", role);
            SetReference(serialized, "roundText", round);
            SetReference(serialized, "resultText", result);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            GameObject eventSystem = new("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<InputSystemUIInputModule>();
        }

        private static void SetReference(SerializedObject serialized, string property, Object value)
        {
            serialized.FindProperty(property).objectReferenceValue = value;
        }

        private static GameObject CreatePanel(string name, Transform parent, Vector2 position, Vector2 size,
            Vector2 anchorMin, Vector2 anchorMax)
        {
            GameObject panel = new(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = anchorMin;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            panel.GetComponent<Image>().color = new Color(0.04f, 0.07f, 0.08f, 0.86f);
            return panel;
        }

        private static Text CreateText(string name, Transform parent, string value, Font font, int size,
            TextAnchor alignment, Vector2 position, Vector2 dimensions, Vector2 anchorMin, Vector2 anchorMax)
        {
            GameObject item = new(name, typeof(RectTransform), typeof(Text));
            item.transform.SetParent(parent, false);
            RectTransform rect = item.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = anchorMin;
            rect.anchoredPosition = position;
            rect.sizeDelta = dimensions;
            Text text = item.GetComponent<Text>();
            text.text = value;
            text.font = font;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = Color.white;
            return text;
        }

        private static Button CreateButton(string name, Transform parent, string label, Font font,
            Vector2 position, Vector2 size, Vector2? anchor = null)
        {
            Vector2 resolvedAnchor = anchor ?? new Vector2(0f, 1f);
            GameObject item = new(name, typeof(RectTransform), typeof(Image), typeof(Button));
            item.transform.SetParent(parent, false);
            RectTransform rect = item.GetComponent<RectTransform>();
            rect.anchorMin = resolvedAnchor;
            rect.anchorMax = resolvedAnchor;
            rect.pivot = resolvedAnchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            item.GetComponent<Image>().color = new Color(0.15f, 0.48f, 0.72f, 1f);
            CreateText("Label", item.transform, label, font, 16, TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one).rectTransform.offsetMax = Vector2.zero;
            Text labelText = item.GetComponentInChildren<Text>();
            labelText.rectTransform.offsetMin = Vector2.zero;
            return item.GetComponent<Button>();
        }

        private static InputField CreateInput(string name, Transform parent, string initial, Font font,
            Vector2 position, Vector2 size)
        {
            GameObject item = new(name, typeof(RectTransform), typeof(Image), typeof(InputField));
            item.transform.SetParent(parent, false);
            RectTransform rect = item.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            item.GetComponent<Image>().color = new Color(0.93f, 0.93f, 0.9f, 1f);

            Text text = CreateText("Text", item.transform, initial, font, 16, TextAnchor.MiddleLeft,
                new Vector2(8f, 0f), new Vector2(-16f, 0f), Vector2.zero, Vector2.one);
            text.color = new Color(0.05f, 0.08f, 0.09f);
            text.rectTransform.offsetMin = new Vector2(8f, 2f);
            text.rectTransform.offsetMax = new Vector2(-8f, -2f);
            InputField input = item.GetComponent<InputField>();
            input.textComponent = text;
            input.text = initial;
            return input;
        }

        private static GameObject CreateBox(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            GameObject item = GameObject.CreatePrimitive(PrimitiveType.Cube);
            item.name = name;
            item.transform.SetParent(parent, false);
            item.transform.position = position;
            item.transform.localScale = scale;
            item.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(item.GetComponent<Collider>());
            return item;
        }

        private static Material GetOrCreateMaterial(string name, Color color)
        {
            string path = $"{Root}/Materials/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.color = color;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ConfigureBuildSettings()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };
        }
    }
}
#endif
