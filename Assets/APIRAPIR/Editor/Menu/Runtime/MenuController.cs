using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ApirApir.Menu
{
    public sealed class MenuController : MonoBehaviour
    {
        [Header("Panels")]
        [SerializeField] private GameObject mainMenuPanel;
        [SerializeField] private GameObject joinPanel;
        [SerializeField] private GameObject hostPanel;

        [Header("Join")]
        [SerializeField] private Transform lobbyContent;
        [SerializeField] private LobbyEntryView lobbyEntryPrefab;
        [SerializeField] private GameObject emptyState;
        [SerializeField] private TMP_InputField addressInput;
        [SerializeField] private TMP_InputField portInput;
        [SerializeField] private TMP_Text joinStatus;

        [Header("Host")]
        [SerializeField] private TMP_Text hostInfo;

        [Header("Integration")]
        [SerializeField] private string lobbySceneName = "Lobby";

        private readonly List<LobbyEntryView> entries = new();

        private void Awake()
        {
            ShowMainMenu();
            if (addressInput != null && string.IsNullOrWhiteSpace(addressInput.text))
                addressInput.text = "127.0.0.1";
            if (portInput != null && string.IsNullOrWhiteSpace(portInput.text))
                portInput.text = "7777";
            if (joinStatus != null)
                joinStatus.text = "No LAN lobby discovery is configured yet.";
            if (hostInfo != null)
                hostInfo.text = "Host a Patintero game for up to four players.\n\n" +
                                "The existing Lobby scene owns networking and will handle the actual session.";
        }

        public void ShowMainMenu()
        {
            SetPanel(mainMenuPanel);
        }

        public void ShowJoin()
        {
            SetPanel(joinPanel);
            RefreshLobbyList();
        }

        public void ShowHost()
        {
            SetPanel(hostPanel);
        }

        public void RefreshLobbyList()
        {
            foreach (LobbyEntryView entry in entries)
                if (entry != null) Destroy(entry.gameObject);
            entries.Clear();
            if (emptyState != null) emptyState.SetActive(true);
        }

        public void JoinAddress()
        {
            string address = addressInput == null ? string.Empty : addressInput.text.Trim();
            string port = portInput == null ? string.Empty : portInput.text.Trim();
            if (string.IsNullOrWhiteSpace(address))
            {
                SetJoinStatus("Enter a host address first.");
                return;
            }
            if (!ushort.TryParse(port, out ushort parsedPort) || parsedPort == 0)
            {
                SetJoinStatus("Enter a valid port from 1 to 65535.");
                return;
            }

            SetJoinStatus($"Ready to join {address}:{parsedPort}. Open the existing Lobby scene to connect.");
        }

        public void SelectLobby(string lobbyName)
        {
            SetJoinStatus($"Selected {lobbyName}. Network discovery is not configured yet.");
        }

        public void CreateLobby()
        {
            if (!Application.CanStreamedLevelBeLoaded(lobbySceneName))
            {
                SetJoinStatus("The existing Lobby scene is not available in Build Settings.");
                return;
            }

            SceneManager.LoadScene(lobbySceneName);
        }

        public void ExitApplication()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void SetPanel(GameObject activePanel)
        {
            if (mainMenuPanel != null) mainMenuPanel.SetActive(activePanel == mainMenuPanel);
            if (joinPanel != null) joinPanel.SetActive(activePanel == joinPanel);
            if (hostPanel != null) hostPanel.SetActive(activePanel == hostPanel);
        }

        private void SetJoinStatus(string message)
        {
            if (joinStatus != null) joinStatus.text = message;
        }
    }
}
