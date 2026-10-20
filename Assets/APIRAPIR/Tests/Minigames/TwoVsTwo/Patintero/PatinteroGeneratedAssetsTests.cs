using NUnit.Framework;
using ApirApir.Multiplayer;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ApirApir.Patintero.Tests
{
    public sealed class PatinteroGeneratedAssetsTests
    {
        private const string Root = "Assets/APIRAPIR/Runtime/Mechanics/Minigames/Patintero";
        private const string ScenePath = "Assets/Scenes/PatinteroPrototype.unity";
        private const string PrefabPath = Root + "/Prefabs/PatinteroPlayer.prefab";
        private const string SettingsPath = Root + "/Config/PatinteroSettings.asset";
        private const string NetworkPrefabsPath = Root + "/Config/PatinteroNetworkPrefabs.asset";

        [Test]
        public void PlayerPrefab_HasRequiredNetworkAndGameplayComponents()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<NetworkObject>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<NetworkTransform>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<PatinteroKeyboardInput>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<PatinteroPlayer>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<CapsuleCollider>(), Is.Not.Null);
            Assert.That(prefab.GetComponentInChildren<Renderer>(), Is.Not.Null);
            AssertNoMissingScripts(prefab);
        }

        [Test]
        public void PrototypeScene_HasConfiguredNetworkCourtCameraAndHud()
        {
            SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                Assert.That(scene.IsValid() && scene.isLoaded, Is.True);

                NetworkManager networkManager = Object.FindFirstObjectByType<NetworkManager>();
                Assert.That(networkManager, Is.Not.Null);
                UnityTransport transport = networkManager.GetComponent<UnityTransport>();
                Assert.That(transport, Is.Not.Null);
                Assert.That(networkManager.NetworkConfig.NetworkTransport, Is.EqualTo(transport));
                Assert.That(networkManager.NetworkConfig.ConnectionApproval, Is.True);
                Assert.That(networkManager.NetworkConfig.EnableSceneManagement, Is.True);
                Assert.That(networkManager.NetworkConfig.PlayerPrefab,
                    Is.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)));
                Assert.That(networkManager.NetworkConfig.Prefabs.NetworkPrefabsLists, Has.Count.EqualTo(1));
                NetworkPrefabsList prefabs = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
                Assert.That(prefabs, Is.Not.Null);
                Assert.That(networkManager.NetworkConfig.Prefabs.NetworkPrefabsLists[0], Is.EqualTo(prefabs));
                Assert.That(prefabs.Contains(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)), Is.True);

                PatinteroGameManager gameManager = Object.FindFirstObjectByType<PatinteroGameManager>();
                Assert.That(gameManager, Is.Not.Null);
                Assert.That(gameManager.GetComponent<NetworkObject>(), Is.Not.Null);
                NetworkSessionService sessionService = gameManager.GetComponent<NetworkSessionService>();
                Assert.That(sessionService, Is.Not.Null);
                SerializedObject gameManagerData = new(gameManager);
                Assert.That(gameManagerData.FindProperty("settings").objectReferenceValue,
                    Is.EqualTo(AssetDatabase.LoadAssetAtPath<PatinteroSettings>(SettingsPath)));

                Camera camera = Camera.main;
                Assert.That(camera, Is.Not.Null);
                Assert.That(camera.orthographic, Is.True);
                Assert.That(Object.FindFirstObjectByType<Canvas>(), Is.Not.Null);
                Assert.That(Object.FindFirstObjectByType<PatinteroNetworkUI>(), Is.Not.Null);
                Assert.That(Object.FindFirstObjectByType<NetworkSessionUI>(), Is.Not.Null);
                Assert.That(Object.FindFirstObjectByType<EventSystem>(), Is.Not.Null);
                Assert.That(Object.FindFirstObjectByType<InputSystemUIInputModule>(), Is.Not.Null);
                Assert.That(GameObject.Find("Court"), Is.Not.Null);

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    AssertNoMissingScripts(root);
                }
            }
            finally
            {
                bool hasLoadedScene = false;
                for (int i = 0; i < previousSetup.Length; i++)
                {
                    hasLoadedScene |= previousSetup[i].isLoaded;
                }

                if (hasLoadedScene)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
                }
                else
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                }
            }
        }

        private static void AssertNoMissingScripts(GameObject root)
        {
            foreach (Transform item in root.GetComponentsInChildren<Transform>(true))
            {
                Component[] components = item.GetComponents<Component>();
                for (int i = 0; i < components.Length; i++)
                {
                    Assert.That(components[i], Is.Not.Null, $"Missing script on {item.name} at component index {i}.");
                }
            }
        }
    }
}
