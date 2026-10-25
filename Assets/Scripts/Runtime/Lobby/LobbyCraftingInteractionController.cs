using System;
using System.Collections.Generic;
using ApirApir.Minigames;
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

        [Header("Action Context")]
        [SerializeField] private string craftingActionLabel = "CRAFT";
        [SerializeField] private string defaultActionLabel = "ACTION";

        private readonly HashSet<LobbyCraftingTableTrigger> nearbyTriggers =
            new HashSet<LobbyCraftingTableTrigger>();
        private LobbyCraftingTableTrigger activeTrigger;
        private UniversalPlayerInput playerControls;
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

        private void Start()
        {
            BindPlayerControls();
            RefreshActionContext();
        }

        private void OnDestroy()
        {
            if (playerControls != null)
            {
                playerControls.ActionPressed -= HandleActionPressed;
                playerControls.SetActionContext(
                    MinigameActionSlot.Primary, defaultActionLabel, false);
            }

            if (exitButton != null)
            {
                exitButton.onClick.RemoveListener(CloseCrafting);
            }
        }

        internal void NotifyEntered(
            LobbyCraftingTableTrigger source,
            global::LobbyPlayerMovement enteringPlayer)
        {
            if (source == null || enteringPlayer == null || enteringPlayer != localPlayerMovement)
            {
                return;
            }

            nearbyTriggers.Add(source);
            RefreshActionContext();
        }

        internal void NotifyExited(
            LobbyCraftingTableTrigger source,
            global::LobbyPlayerMovement exitingPlayer)
        {
            if (source == null || exitingPlayer != localPlayerMovement)
            {
                return;
            }

            nearbyTriggers.Remove(source);
            if (!craftingOpen && source == activeTrigger) activeTrigger = null;
            RefreshActionContext();
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
            RefreshActionContext();
        }

        private void BindPlayerControls()
        {
            playerControls = localPlayerMovement == null ? null : localPlayerMovement.Controls;
            if (playerControls == null) return;
            playerControls.ActionPressed -= HandleActionPressed;
            playerControls.ActionPressed += HandleActionPressed;
        }

        private void HandleActionPressed(MinigameActionSlot slot)
        {
            if (slot != MinigameActionSlot.Primary || craftingOpen) return;
            LobbyCraftingTableTrigger source = FindAvailableTrigger();
            if (source == null) return;

            activeTrigger = source;
            SetCraftingState(true);
            RefreshActionContext();
            ConfigureLobbyInventory();
        }

        private LobbyCraftingTableTrigger FindAvailableTrigger()
        {
            foreach (LobbyCraftingTableTrigger trigger in nearbyTriggers)
            {
                if (trigger != null && localPlayerMovement != null &&
                    !trigger.IsBlocked(localPlayerMovement))
                {
                    return trigger;
                }
            }

            return null;
        }

        private void RefreshActionContext()
        {
            if (playerControls == null) return;
            bool available = !craftingOpen && FindAvailableTrigger() != null;
            playerControls.SetActionContext(
                MinigameActionSlot.Primary,
                available ? craftingActionLabel : defaultActionLabel,
                available);
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
