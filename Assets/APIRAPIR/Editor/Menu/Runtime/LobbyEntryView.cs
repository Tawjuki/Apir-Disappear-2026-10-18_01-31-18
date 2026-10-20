using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ApirApir.Menu
{
    public sealed class LobbyEntryView : MonoBehaviour
    {
        [SerializeField] private TMP_Text lobbyName;
        [SerializeField] private TMP_Text playerCount;
        [SerializeField] private Button joinButton;

        private MenuController controller;

        public void Configure(MenuController menu, string name, int players, int capacity)
        {
            controller = menu;
            if (lobbyName != null) lobbyName.text = name;
            if (playerCount != null) playerCount.text = $"{players}/{capacity}";
            if (joinButton != null)
            {
                joinButton.onClick.RemoveAllListeners();
                joinButton.onClick.AddListener(Join);
            }
        }

        private void Join()
        {
            controller?.SelectLobby(lobbyName == null ? "lobby" : lobbyName.text);
        }
    }
}
