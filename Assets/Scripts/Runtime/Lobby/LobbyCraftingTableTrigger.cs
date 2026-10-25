using UnityEngine;

namespace ApirApir.Lobby
{
    [DisallowMultipleComponent]
    public sealed class LobbyCraftingTableTrigger : MonoBehaviour
    {
        private LobbyCraftingInteractionController coordinator;
        private global::LobbyPlayerMovement blockedPlayer;

        internal void Initialize(LobbyCraftingInteractionController owner)
        {
            coordinator = owner;
        }

        internal bool IsBlocked(global::LobbyPlayerMovement player) => blockedPlayer == player;

        internal void BlockUntilExit(global::LobbyPlayerMovement player)
        {
            blockedPlayer = player;
        }

        private void OnTriggerEnter(Collider other)
        {
            global::LobbyPlayerMovement player =
                other == null ? null : other.GetComponentInParent<global::LobbyPlayerMovement>();
            coordinator?.NotifyEntered(this, player);
        }

        private void OnTriggerExit(Collider other)
        {
            global::LobbyPlayerMovement player =
                other == null ? null : other.GetComponentInParent<global::LobbyPlayerMovement>();
            if (player == null)
            {
                return;
            }

            if (blockedPlayer == player)
            {
                blockedPlayer = null;
            }

            coordinator?.NotifyExited(this, player);
        }
    }
}
