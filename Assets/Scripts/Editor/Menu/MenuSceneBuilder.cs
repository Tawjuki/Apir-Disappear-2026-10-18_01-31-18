using ApirApir.Menu;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ApirApir.Menu.Editor
{
    public static class MenuSceneBuilder
    {
        private static readonly Color Cream = new(0.98f, 0.93f, 0.82f);
        private static readonly Color PanelColor = new(1f, 0.97f, 0.9f);
        private static readonly Color Brown = new(0.35f, 0.16f, 0.1f);
        private static readonly Color Orange = new(0.88f, 0.42f, 0.2f);
        private static readonly Color Yellow = new(0.98f, 0.74f, 0.31f);
        private static readonly Color Muted = new(0.58f, 0.37f, 0.25f);

        [MenuItem("APIR APIR/Build Menu Scene")]
        public static void Build()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "Menu";

            GameObject canvasObject = New("MenuCanvas", null, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            New("EventSystem", null, typeof(EventSystem), typeof(InputSystemUIInputModule));

            GameObject background = Box("Background", canvasObject.transform, Cream);
            Full(background);
            GameObject main = Panel("MainMenuPanel", canvasObject.transform);
            GameObject join = Panel("JoinPanel", canvasObject.transform);
            GameObject host = Panel("HostPanel", canvasObject.transform);
            join.SetActive(false);
            host.SetActive(false);

            Text("GameTitle", main.transform, "APIR APIR", 104, Brown, new Vector2(0.5f, 0.78f), new Vector2(1000, 170));
            GameObject joinButton = Button("JoinButton", main.transform, "JOIN", Yellow, new Vector2(0.5f, 0.52f), new Vector2(620, 130));
            GameObject hostButton = Button("HostButton", main.transform, "HOST", Orange, new Vector2(0.5f, 0.34f), new Vector2(620, 130));
            GameObject exitButton = Button("ExitButton", main.transform, "EXIT", new Color(0.84f, 0.68f, 0.52f), new Vector2(0.5f, 0.16f), new Vector2(620, 130));

            Text("JoinTitle", join.transform, "JOIN", 86, Brown, new Vector2(0.5f, 0.9f), new Vector2(800, 130));
            GameObject listFrame = Box("LobbyListFrame", join.transform, new Color(1f, 0.99f, 0.94f));
            Set(listFrame, new Vector2(0.5f, 0.55f), new Vector2(1250, 540));
            GameObject scroll = New("LobbyScrollView", listFrame.transform, typeof(Image), typeof(ScrollRect));
            Set(scroll, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            scroll.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.35f);
            GameObject viewport = New("Viewport", scroll.transform, typeof(RectMask2D));
            Set(viewport, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            GameObject content = New("Content", viewport.transform, typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            Set(content, new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, new Vector2(0, 0));
            content.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 1);
            VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 18;
            layout.padding = new RectOffset(18, 18, 18, 18);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            ScrollRect scrollRect = scroll.GetComponent<ScrollRect>();
            scrollRect.viewport = viewport.GetComponent<RectTransform>();
            scrollRect.content = content.GetComponent<RectTransform>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.scrollSensitivity = 40;
            GameObject empty = Text("EmptyState", listFrame.transform, "No LAN lobbies found yet.\nUse the address fields below to join a host.", 34, Muted, new Vector2(0.5f, 0.5f), new Vector2(800, 150));
            TMP_InputField address = Input("AddressInput", join.transform, "127.0.0.1", new Vector2(0.39f, 0.14f), new Vector2(340, 76));
            TMP_InputField port = Input("PortInput", join.transform, "7777", new Vector2(0.68f, 0.14f), new Vector2(190, 76));
            port.contentType = TMP_InputField.ContentType.IntegerNumber;
            Text("AddressLabel", join.transform, "HOST ADDRESS", 28, Brown, new Vector2(0.25f, 0.18f), new Vector2(260, 50));
            Text("PortLabel", join.transform, "PORT", 28, Brown, new Vector2(0.57f, 0.18f), new Vector2(140, 50));
            GameObject manualJoin = Button("ManualJoinButton", join.transform, "JOIN ADDRESS", Yellow, new Vector2(0.86f, 0.14f), new Vector2(310, 76));
            GameObject joinStatus = Text("JoinStatus", join.transform, string.Empty, 24, Muted, new Vector2(0.5f, 0.06f), new Vector2(1200, 45));
            GameObject joinExit = Button("ExitButton", join.transform, "EXIT", new Color(0.84f, 0.68f, 0.52f), new Vector2(0.5f, 0.02f), new Vector2(360, 80));

            Text("HostTitle", host.transform, "HOST", 86, Brown, new Vector2(0.5f, 0.9f), new Vector2(800, 130));
            GameObject info = Box("InformationPanel", host.transform, new Color(1f, 0.99f, 0.94f));
            Set(info, new Vector2(0.5f, 0.52f), new Vector2(1250, 530));
            GameObject hostInfo = Text("InformationText", info.transform, "Host a Patintero game for up to four players.\n\nNetwork joining instructions will appear when a real host session is started.", 38, Brown, Vector2.zero, Vector2.zero);
            Full(hostInfo, new Vector2(70, 60), new Vector2(-70, -60));
            GameObject hostExit = Button("ExitButton", host.transform, "EXIT", new Color(0.84f, 0.68f, 0.52f), new Vector2(0.36f, 0.12f), new Vector2(420, 110));
            GameObject createLobby = Button("CreateLobbyButton", host.transform, "CREATE LOBBY", Orange, new Vector2(0.64f, 0.12f), new Vector2(520, 110));

            LobbyEntryView prefab = CreateLobbyEntryPrefab();
            MenuController controller = New("MenuController", canvasObject.transform).AddComponent<MenuController>();
            SerializedObject serialized = new SerializedObject(controller);
            serialized.FindProperty("mainMenuPanel").objectReferenceValue = main;
            serialized.FindProperty("joinPanel").objectReferenceValue = join;
            serialized.FindProperty("hostPanel").objectReferenceValue = host;
            serialized.FindProperty("lobbyContent").objectReferenceValue = content.transform;
            serialized.FindProperty("lobbyEntryPrefab").objectReferenceValue = prefab;
            serialized.FindProperty("emptyState").objectReferenceValue = empty;
            serialized.FindProperty("addressInput").objectReferenceValue = address;
            serialized.FindProperty("portInput").objectReferenceValue = port;
            serialized.FindProperty("joinStatus").objectReferenceValue = joinStatus.GetComponent<TMP_Text>();
            serialized.FindProperty("hostInfo").objectReferenceValue = hostInfo.GetComponent<TMP_Text>();
            serialized.ApplyModifiedPropertiesWithoutUndo();

            UnityEventTools.AddPersistentListener(joinButton.GetComponent<Button>().onClick, controller.ShowJoin);
            UnityEventTools.AddPersistentListener(hostButton.GetComponent<Button>().onClick, controller.ShowHost);
            UnityEventTools.AddPersistentListener(exitButton.GetComponent<Button>().onClick, controller.ExitApplication);
            UnityEventTools.AddPersistentListener(joinExit.GetComponent<Button>().onClick, controller.ShowMainMenu);
            UnityEventTools.AddPersistentListener(hostExit.GetComponent<Button>().onClick, controller.ShowMainMenu);
            UnityEventTools.AddPersistentListener(manualJoin.GetComponent<Button>().onClick, controller.JoinAddress);
            UnityEventTools.AddPersistentListener(createLobby.GetComponent<Button>().onClick, controller.CreateLobby);

            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Menu.unity");
            EditorSceneManager.CloseScene(scene, true);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("APIR APIR Menu scene created.");
        }

        private static LobbyEntryView CreateLobbyEntryPrefab()
        {
            GameObject entry = New("LobbyEntry", null, typeof(Image), typeof(LobbyEntryView));
            entry.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 112);
            entry.GetComponent<Image>().color = new Color(0.98f, 0.89f, 0.68f);
            Box("Avatar", entry.transform, Muted);
            Text("LobbyName", entry.transform, "Lobby name", 32, Brown, new Vector2(0.35f, 0.5f), new Vector2(430, 70));
            Text("PlayerCount", entry.transform, "0/4", 30, Brown, new Vector2(0.72f, 0.5f), new Vector2(150, 70));
            GameObject join = Button("JoinButton", entry.transform, "JOIN", Yellow, new Vector2(0.9f, 0.5f), new Vector2(220, 78));
            Set(entry.transform.Find("Avatar").gameObject, new Vector2(0.08f, 0.5f), new Vector2(74, 74));
            LobbyEntryView view = entry.GetComponent<LobbyEntryView>();
            SerializedObject serialized = new SerializedObject(view);
            serialized.FindProperty("lobbyName").objectReferenceValue = entry.transform.Find("LobbyName").GetComponent<TMP_Text>();
            serialized.FindProperty("playerCount").objectReferenceValue = entry.transform.Find("PlayerCount").GetComponent<TMP_Text>();
            serialized.FindProperty("joinButton").objectReferenceValue = join.GetComponent<Button>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            const string folder = "Assets/Prefabs/Menu";
            EnsureFolder("Assets/Prefabs");
            EnsureFolder(folder);
            LobbyEntryView prefab = PrefabUtility.SaveAsPrefabAsset(entry, folder + "/LobbyEntry.prefab").GetComponent<LobbyEntryView>();
            Object.DestroyImmediate(entry);
            return prefab;
        }

        private static GameObject Panel(string name, Transform parent)
        {
            GameObject panel = Box(name, parent, PanelColor);
            Full(panel);
            return panel;
        }

        private static GameObject Button(string name, Transform parent, string label, Color color, Vector2 anchor, Vector2 size)
        {
            GameObject button = New(name, parent, typeof(Image), typeof(Button));
            button.GetComponent<Image>().color = color;
            Set(button, anchor, size);
            GameObject text = Text("Label", button.transform, label, 42, Brown, Vector2.zero, Vector2.zero);
            Full(text, new Vector2(18, 8), new Vector2(-18, -8));
            return button;
        }

        private static TMP_InputField Input(string name, Transform parent, string value, Vector2 anchor, Vector2 size)
        {
            GameObject input = New(name, parent, typeof(Image), typeof(TMP_InputField));
            input.GetComponent<Image>().color = Color.white;
            Set(input, anchor, size);
            GameObject text = Text("Text", input.transform, value, 28, Brown, Vector2.zero, Vector2.zero);
            Full(text, new Vector2(18, 8), new Vector2(-18, -8));
            TMP_InputField field = input.GetComponent<TMP_InputField>();
            field.textComponent = text.GetComponent<TMP_Text>();
            field.text = value;
            return field;
        }

        private static GameObject Text(string name, Transform parent, string value, float size, Color color, Vector2 anchor, Vector2 dimensions)
        {
            GameObject text = New(name, parent, typeof(TextMeshProUGUI));
            TextMeshProUGUI tmp = text.GetComponent<TextMeshProUGUI>();
            tmp.text = value;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            if (dimensions == Vector2.zero) Full(text);
            else Set(text, anchor, dimensions);
            return text;
        }

        private static GameObject Box(string name, Transform parent, Color color)
        {
            GameObject box = New(name, parent, typeof(Image));
            box.GetComponent<Image>().color = color;
            return box;
        }

        private static GameObject New(string name, Transform parent, params System.Type[] components)
        {
            GameObject go = new GameObject(name, components);
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        private static void Set(GameObject go, Vector2 anchor, Vector2 size)
        {
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = Vector2.zero;
        }

        private static void Set(GameObject go, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }

        private static void Full(GameObject go)
        {
            Full(go, Vector2.zero, Vector2.zero);
        }

        private static void Full(GameObject go, Vector2 offsetMin, Vector2 offsetMax)
        {
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace("\\", "/");
            string name = System.IO.Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
