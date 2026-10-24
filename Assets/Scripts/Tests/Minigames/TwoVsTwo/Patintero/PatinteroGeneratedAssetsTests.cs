using NUnit.Framework;
using ApirApir.Minigames;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ApirApir.Patintero.Tests
{
    public sealed class PatinteroGeneratedAssetsTests
    {
        private const string Root = "Assets/Minigames/Patintero";
        private const string ScenePath = "Assets/Scenes/Patintero.unity";
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
        public void PatinteroScene_HasCameraInputAndPlayableCharacterWithoutMissingScripts()
        {
            SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                Assert.That(scene.IsValid() && scene.isLoaded, Is.True);

                NetworkPrefabsList prefabs = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
                Assert.That(prefabs, Is.Not.Null);
                Assert.That(prefabs.Contains(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)), Is.True);

                PatinteroSettings settings = AssetDatabase.LoadAssetAtPath<PatinteroSettings>(SettingsPath);
                Assert.That(settings, Is.Not.Null);
                Assert.That(settings.requiredPlayers, Is.EqualTo(4));
                Assert.That(settings.tayaCount, Is.EqualTo(2));

                Camera camera = Camera.main;
                Assert.That(camera, Is.Not.Null);
                Assert.That(Object.FindFirstObjectByType<Canvas>(), Is.Not.Null);
                Assert.That(Object.FindFirstObjectByType<EventSystem>(), Is.Not.Null);
                PatinteroPlayer player = Object.FindFirstObjectByType<PatinteroPlayer>();
                Assert.That(player, Is.Not.Null);
                Assert.That(player.GetComponent<NetworkObject>(), Is.Not.Null);
                Assert.That(player.GetComponent<PatinteroKeyboardInput>(), Is.Not.Null);
                Assert.That(player.GetComponent<PatinteroKeyboardInput>(), Is.InstanceOf<MinigamePlayerInput>());
                Assert.That(player.GetComponent<CharacterController>(), Is.Not.Null);
                Assert.That(player.GetComponentInChildren<Animator>(), Is.Not.Null);

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
