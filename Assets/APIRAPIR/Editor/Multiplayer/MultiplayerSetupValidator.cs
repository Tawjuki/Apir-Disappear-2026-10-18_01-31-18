#if UNITY_EDITOR
using System;
using ApirApir.Multiplayer;
using ApirApir.Patintero;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ApirApir.Editor
{
    public static class MultiplayerSetupValidator
    {
        private const string PrototypeScenePath = "Assets/Scenes/PatinteroPrototype.unity";

        [MenuItem("Tools/APIR APIR/Validate Multiplayer Setup")]
        public static void ValidateFromMenu()
        {
            ValidateOrThrow();
            Debug.Log("APIR APIR multiplayer setup is valid.");
        }

        public static void RunFromCommandLine()
        {
            try
            {
                ValidateOrThrow();
                Debug.Log("APIR APIR multiplayer setup validation passed.");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static void ValidateOrThrow()
        {
            if (!System.IO.File.Exists(PrototypeScenePath))
            {
                throw new InvalidOperationException($"Missing multiplayer scene: {PrototypeScenePath}");
            }

            SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                Scene scene = EditorSceneManager.OpenScene(PrototypeScenePath, OpenSceneMode.Single);
                NetworkManager manager = UnityEngine.Object.FindFirstObjectByType<NetworkManager>();
                Require(manager != null, "The scene requires one NetworkManager.");
                Require(manager.GetComponent<UnityTransport>() != null, "NetworkManager requires UnityTransport.");
                Require(manager.NetworkConfig.ConnectionApproval, "Connection approval must be enabled.");
                Require(manager.NetworkConfig.PlayerPrefab != null, "A networked player prefab must be assigned.");
                Require(manager.NetworkConfig.PlayerPrefab.GetComponent<NetworkObject>() != null,
                    "Player prefab requires NetworkObject.");
                Require(manager.NetworkConfig.PlayerPrefab.GetComponent<NetworkTransform>() != null,
                    "Player prefab requires NetworkTransform.");
                Require(manager.NetworkConfig.PlayerPrefab.GetComponent<PatinteroPlayer>() != null,
                    "Player prefab requires PatinteroPlayer.");

                NetworkSessionService sessionService = UnityEngine.Object.FindFirstObjectByType<NetworkSessionService>();
                Require(sessionService != null,
                    "The scene requires NetworkSessionService on a NetworkObject. " +
                    DescribeScriptResolution<NetworkSessionService>(
                        "Assets/APIRAPIR/Runtime/Systems/Multiplayer/NetworkSessionService.cs"));
                Require(sessionService.GetComponent<NetworkObject>() != null,
                    "NetworkSessionService requires NetworkObject.");
                Require(UnityEngine.Object.FindFirstObjectByType<NetworkSessionUI>() != null,
                    "The scene requires NetworkSessionUI.");

                PatinteroGameManager gameManager = UnityEngine.Object.FindFirstObjectByType<PatinteroGameManager>();
                Require(gameManager != null && gameManager.Settings != null, "PatinteroGameManager settings are missing.");
                Require(gameManager.Settings.requiredPlayers == NetworkSessionService.MaximumPlayers,
                    "Patintero must require exactly four players.");
                Require(gameManager.Settings.tayaCount == 1,
                    "The current Patintero format must contain one Taya and three runners.");

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Transform item in root.GetComponentsInChildren<Transform>(true))
                    {
                        Component[] components = item.GetComponents<Component>();
                        for (int i = 0; i < components.Length; i++)
                        {
                            Require(components[i] != null, $"Missing script on {item.name} at component index {i}.");
                        }
                    }
                }
            }
            finally
            {
                if (previousSetup.Length > 0)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
                }
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static string DescribeScriptResolution<T>(string path) where T : MonoBehaviour
        {
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            string resolved = script == null
                ? "missing MonoScript asset"
                : script.GetClass()?.AssemblyQualifiedName ?? "MonoScript class is unresolved";
            return $"Expected {typeof(T).AssemblyQualifiedName}; importer reports {resolved}.";
        }
    }
}
#endif
