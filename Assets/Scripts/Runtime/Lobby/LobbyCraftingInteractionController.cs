using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ApirApir.Lobby
{
    /// <summary>
    /// Owns only the Lobby transition between player controls and the crafting interface.
    /// Table triggers forward local-player enter/exit events to this coordinator.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LobbyCraftingInteractionController : MonoBehaviour
    {
        [SerializeField] private GameObject craftingHud;
        [SerializeField] private GameObject playerHud;
        [SerializeField] private GameObject joystickHud;
        [SerializeField] private Button exitButton;
        [SerializeField] private global::LobbyPlayerMovement localPlayerMovement;
        [SerializeField] private GameObject[] craftingTables = Array.Empty<GameObject>();
        [SerializeField] private TMP_FontAsset inventoryFont;

        private LobbyCraftingTableTrigger activeTrigger;
        private bool craftingOpen;

        private void Awake()
        {
            ConfigureTableTriggers();
            if (exitButton != null)
            {
                exitButton.onClick.RemoveListener(CloseCrafting);
                exitButton.onClick.AddListener(CloseCrafting);
            }

            SetCraftingState(false);
        }

        private void OnDestroy()
        {
            if (exitButton != null)
            {
                exitButton.onClick.RemoveListener(CloseCrafting);
            }
        }

        internal void TryOpen(
            LobbyCraftingTableTrigger source,
            global::LobbyPlayerMovement enteringPlayer)
        {
            if (craftingOpen || source == null || enteringPlayer == null ||
                enteringPlayer != localPlayerMovement || source.IsBlocked(enteringPlayer))
            {
                return;
            }

            activeTrigger = source;
            SetCraftingState(true);
            ConfigureLobbyInventory();
        }

        internal void NotifyExited(
            LobbyCraftingTableTrigger source,
            global::LobbyPlayerMovement exitingPlayer)
        {
            if (source != null && source == activeTrigger && exitingPlayer == localPlayerMovement &&
                !craftingOpen)
            {
                activeTrigger = null;
            }
        }

        public void CloseCrafting()
        {
            if (!craftingOpen)
            {
                return;
            }

            craftingOpen = false;
            if (activeTrigger != null && localPlayerMovement != null)
            {
                activeTrigger.BlockUntilExit(localPlayerMovement);
            }

            SetCraftingState(false);
        }

        private void SetCraftingState(bool open)
        {
            craftingOpen = open;
            if (craftingHud != null && craftingHud.activeSelf != open)
            {
                craftingHud.SetActive(open);
            }

            if (playerHud != null && playerHud.activeSelf == open)
            {
                playerHud.SetActive(!open);
            }

            if (joystickHud != null && joystickHud.activeSelf == open)
            {
                joystickHud.SetActive(!open);
            }

            if (localPlayerMovement != null)
            {
                localPlayerMovement.SetMovementEnabled(!open);
            }
        }

        private void ConfigureTableTriggers()
        {
            for (int i = 0; i < craftingTables.Length; i++)
            {
                GameObject table = craftingTables[i];
                if (table == null)
                {
                    continue;
                }

                Collider triggerCollider = table.GetComponent<Collider>();
                if (triggerCollider == null)
                {
                    Debug.LogWarning($"Crafting table '{table.name}' has no trigger collider.", table);
                    continue;
                }

                triggerCollider.isTrigger = true;
                LobbyCraftingTableTrigger trigger =
                    table.GetComponent<LobbyCraftingTableTrigger>() ??
                    table.AddComponent<LobbyCraftingTableTrigger>();
                trigger.Initialize(this);
            }
        }

        private void ConfigureLobbyInventory()
        {
            if (craftingHud == null)
            {
                return;
            }

            ApirApir.UI.UICraftingView view = craftingHud.GetComponent<ApirApir.UI.UICraftingView>();
            if (view != null)
            {
                view.ConfigureLobbyInventoryLayout(inventoryFont);
            }
        }
    }

}
