#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;
using System.Collections.Generic;
using ApirApir.UI;
using ApirApir.UI.DragDrop;
using ApirApir.UI.Inventory;

namespace ApirApir.UI.Editor
{
    public static class LobbyUIBuilder
    {
        [MenuItem("APIR APIR/Build Lobby UI")]
        public static void Build()
        {
            // Destroy existing canvas if any
            var existingCanvas = GameObject.Find("LobbyCanvas");
            if (existingCanvas != null)
            {
                Object.DestroyImmediate(existingCanvas);
            }

            // 0. Setup EventSystem with correct Input Module
            SetupEventSystem();

            // 1. Root Canvas (Landscape 1920x1080)
            var canvasGO = new GameObject("LobbyCanvas");
            Undo.RegisterCreatedObjectUndo(canvasGO, "Create Lobby Canvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 0;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f; // Landscape balanced match

            canvasGO.AddComponent<GraphicRaycaster>();
            var navigator = canvasGO.AddComponent<LobbyNavigator>();

            // 2. Safe Area Container (Warm nostalgic charcoal / notebook tone)
            var safeArea = CreateUIObject("SafeAreaContainer", canvasGO.transform);
            StretchFull(RT(safeArea));
            AddImage(safeArea, new Color(0.10f, 0.10f, 0.14f, 1f));

            // 3. Top Navigation Bar
            var navBar = CreateUIObject("NavBar", safeArea.transform);
            var navBarRT = RT(navBar);
            navBarRT.anchorMin = new Vector2(0, 1);
            navBarRT.anchorMax = new Vector2(1, 1);
            navBarRT.pivot = new Vector2(0.5f, 1f);
            navBarRT.sizeDelta = new Vector2(0, 75);
            navBarRT.anchoredPosition = Vector2.zero;
            AddImage(navBar, new Color(0.14f, 0.14f, 0.22f));

            var navHL = navBar.AddComponent<HorizontalLayoutGroup>();
            navHL.padding = new RectOffset(20, 20, 8, 8);
            navHL.spacing = 16;
            navHL.childForceExpandWidth = false;
            navHL.childForceExpandHeight = true;
            navHL.childControlWidth = false;
            navHL.childControlHeight = true;

            var btnCraftGO = CreateButton("BtnCrafting", "Crafting", navBar.transform, new Color(1f, 0.85f, 0.2f), 22, 200);
            var btnShopGO  = CreateButton("BtnShop",     "Tyangge / Shop", navBar.transform, new Color(0.7f, 0.7f, 0.7f), 22, 220);
            var btnSkillGO = CreateButton("BtnSkill",    "Souls & Skills", navBar.transform, new Color(0.7f, 0.7f, 0.7f), 22, 220);

            // 4. Content Area (stretches below navbar)
            var contentArea = CreateUIObject("ContentArea", safeArea.transform);
            var contentRT = RT(contentArea);
            contentRT.anchorMin = Vector2.zero;
            contentRT.anchorMax = Vector2.one;
            contentRT.offsetMin = Vector2.zero;
            contentRT.offsetMax = new Vector2(0, -75);

            // 5. Drag Ghost Overlay (top of canvas)
            var dragGhostGO = CreateUIObject("UIDragGhost", canvasGO.transform);
            var ghostRT = RT(dragGhostGO);
            ghostRT.sizeDelta = new Vector2(80, 80);
            var ghostImg = AddImage(dragGhostGO, Color.white);
            var ghostCG = dragGhostGO.AddComponent<CanvasGroup>();
            ghostCG.blocksRaycasts = false;
            ghostCG.alpha = 0.85f;
            var ghostLblGO = CreateUIObject("QtyLabel", dragGhostGO.transform);
            StretchFull(RT(ghostLblGO));
            var ghostLbl = AddTMP(ghostLblGO, "", 18, TextAlignmentOptions.BottomRight);
            ghostLbl.color = new Color(1f, 0.9f, 0.4f);
            var ghostComp = dragGhostGO.AddComponent<UIDragGhost>();
            ghostComp.Initialize(ghostImg, ghostLbl, ghostCG, canvas);
            dragGhostGO.SetActive(false);

            // Card templates for prefabs
            GameObject inventorySlotPrefab = null;

            // ═════════════════════════════════════════════════════════════════
            // 6. CRAFTING PANEL (Sketch 1 Layout)
            // ═════════════════════════════════════════════════════════════════
            var craftPanel = CreateUIObject("CraftingPanel", contentArea.transform);
            StretchFull(RT(craftPanel));
            var craftHL = craftPanel.AddComponent<HorizontalLayoutGroup>();
            craftHL.padding = new RectOffset(20, 20, 15, 15);
            craftHL.spacing = 20;
            craftHL.childForceExpandWidth = true;
            craftHL.childForceExpandHeight = true;
            craftHL.childControlWidth = true;
            craftHL.childControlHeight = true;

            var craftView = craftPanel.AddComponent<UICraftingView>();
            var craftPresenter = craftPanel.AddComponent<CraftingUIPresenter>();

            // LEFT SIDE: Workshop (Preview + 3 slots + Craft button)
            var craftLeft = CreateUIObject("LeftSide_Workshop", craftPanel.transform);
            craftLeft.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var craftLeftVL = craftLeft.AddComponent<VerticalLayoutGroup>();
            craftLeftVL.padding = new RectOffset(16, 16, 16, 16);
            craftLeftVL.spacing = 16;
            craftLeftVL.childForceExpandWidth = true;
            craftLeftVL.childForceExpandHeight = false;
            craftLeftVL.childControlWidth = true;
            craftLeftVL.childControlHeight = false;
            AddImage(craftLeft, new Color(0.14f, 0.14f, 0.20f, 0.95f));
            AddOutline(craftLeft, new Color(0.35f, 0.35f, 0.55f, 0.6f), 2);

            CreateHeader("Header_Craft", "CRAFT", craftLeft.transform, new Color(0.20f, 0.22f, 0.34f), 24, 46);

            // Preview Box
            var previewBox = CreateUIObject("PreviewBox", craftLeft.transform);
            var pBoxLE = previewBox.AddComponent<LayoutElement>();
            pBoxLE.preferredHeight = 170;
            AddImage(previewBox, new Color(0.18f, 0.18f, 0.28f));
            AddOutline(previewBox, new Color(0.5f, 0.5f, 0.8f, 0.7f), 2);

            var previewVL = previewBox.AddComponent<VerticalLayoutGroup>();
            previewVL.padding = new RectOffset(12, 12, 10, 10);
            previewVL.spacing = 6;
            previewVL.childForceExpandWidth = true;
            previewVL.childForceExpandHeight = false;
            previewVL.childControlWidth = true;
            previewVL.childControlHeight = true;

            var pIconGO = CreateUIObject("ResultIcon", previewBox.transform);
            pIconGO.AddComponent<LayoutElement>().preferredHeight = 70;
            var pIconImg = AddImage(pIconGO, new Color(0.4f, 0.4f, 0.65f));

            var pNameGO = CreateUIObject("ResultName", previewBox.transform);
            pNameGO.AddComponent<LayoutElement>().preferredHeight = 32;
            var pNameTMP = AddTMP(pNameGO, "", 22, TextAlignmentOptions.Center);
            pNameTMP.fontStyle = FontStyles.Bold;

            var pDetailsGO = CreateUIObject("ResultDetails", previewBox.transform);
            pDetailsGO.AddComponent<LayoutElement>().preferredHeight = 26;
            var pDetailsTMP = AddTMP(pDetailsGO, "", 17, TextAlignmentOptions.Center);
            pDetailsTMP.color = new Color(0.75f, 0.85f, 1f);

            var pPlaceholderGO = CreateUIObject("InvalidPlaceholder", previewBox.transform);
            pPlaceholderGO.AddComponent<LayoutElement>().preferredHeight = 100;
            var pPlaceTMP = AddTMP(pPlaceholderGO, "?\nIlagay ang 3 sangkap upang lumikha", 20, TextAlignmentOptions.Center);
            pPlaceTMP.color = new Color(0.6f, 0.6f, 0.75f);

            craftView.previewContainer = previewBox;
            craftView.resultIcon = pIconImg;
            craftView.resultNameLabel = pNameTMP;
            craftView.resultDetailsLabel = pDetailsTMP;
            craftView.invalidPlaceholder = pPlaceholderGO;

            // Arrow indicator ("^")
            var arrowGO = CreateUIObject("ArrowIndicator", craftLeft.transform);
            arrowGO.AddComponent<LayoutElement>().preferredHeight = 26;
            AddTMP(arrowGO, "^", 28, TextAlignmentOptions.Center).color = new Color(0.7f, 0.7f, 0.9f);

            // Three Ingredient Slots arranged horizontally: [Slot 1] + [Slot 2] + [Slot 3]
            var slotsRow = CreateUIObject("IngredientRow", craftLeft.transform);
            slotsRow.AddComponent<LayoutElement>().preferredHeight = 130;
            var slotsHL = slotsRow.AddComponent<HorizontalLayoutGroup>();
            slotsHL.padding = new RectOffset(6, 6, 6, 6);
            slotsHL.spacing = 8;
            slotsHL.childForceExpandWidth = true;
            slotsHL.childForceExpandHeight = true;
            slotsHL.childControlWidth = true;
            slotsHL.childControlHeight = true;

            for (int i = 0; i < 3; i++)
            {
                var slot = CreateCraftingSlot($"IngredientSlot_{i + 1}", i, slotsRow.transform);
                craftView.ingredientSlots[i] = slot;

                if (i < 2)
                {
                    var plusGO = CreateUIObject("Plus", slotsRow.transform);
                    var plusLE = plusGO.AddComponent<LayoutElement>();
                    plusLE.preferredWidth = 30;
                    var plusTMP = AddTMP(plusGO, "+", 28, TextAlignmentOptions.Center);
                    plusTMP.color = new Color(0.8f, 0.8f, 0.95f);
                }
            }

            // Craft Button
            var btnCraft = CreateButton("BtnCraft", "CRAFT", craftLeft.transform, new Color(0.25f, 0.75f, 0.40f), 24, 0, 60);
            craftView.btnCraft = btnCraft.GetComponent<Button>();
            craftView.craftButtonLabel = btnCraft.GetComponentInChildren<TextMeshProUGUI>();

            // RIGHT SIDE: Materials (Scrollable Grid)
            var craftRight = CreateUIObject("RightSide_Materials", craftPanel.transform);
            craftRight.AddComponent<LayoutElement>().flexibleWidth = 1.15f;
            var craftRightVL = craftRight.AddComponent<VerticalLayoutGroup>();
            craftRightVL.padding = new RectOffset(16, 16, 16, 16);
            craftRightVL.spacing = 10;
            craftRightVL.childForceExpandWidth = true;
            craftRightVL.childForceExpandHeight = true;
            craftRightVL.childControlWidth = true;
            craftRightVL.childControlHeight = true;
            AddImage(craftRight, new Color(0.14f, 0.14f, 0.20f, 0.95f));
            AddOutline(craftRight, new Color(0.35f, 0.35f, 0.55f, 0.6f), 2);

            CreateHeader("Header_Materials", "DRAGGABLE MATERIALS", craftRight.transform, new Color(0.22f, 0.26f, 0.38f), 24, 46);

            var matsDropTarget = craftRight.AddComponent<UIDropTarget>();
            matsDropTarget.AcceptedCategory = "Material";
            craftView.materialsDropArea = matsDropTarget;

            var matsScroll = CreateGridScrollView("ScrollView_Materials", craftRight.transform, out var matsContent, 3, new Vector2(160, 110));
            matsScroll.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;

            var matsGrid = craftRight.AddComponent<UIInventoryGrid>();
            craftView.materialsGrid = matsGrid;

            // Template slot for prefabs
            var sampleSlot = CreateInventorySlotTemplate("SlotTemplate", matsContent.transform);
            inventorySlotPrefab = sampleSlot;
            matsGrid.Initialize(matsContent.transform, sampleSlot);
            sampleSlot.SetActive(false); // Hide template

            // ═════════════════════════════════════════════════════════════════
            // 7. SHOP PANEL (Sketch 2 Layout)
            // ═════════════════════════════════════════════════════════════════
            var shopPanel = CreateUIObject("ShopPanel", contentArea.transform);
            StretchFull(RT(shopPanel));
            shopPanel.SetActive(false);

            var shopHL = shopPanel.AddComponent<HorizontalLayoutGroup>();
            shopHL.padding = new RectOffset(20, 20, 15, 15);
            shopHL.spacing = 20;
            shopHL.childForceExpandWidth = true;
            shopHL.childForceExpandHeight = true;
            shopHL.childControlWidth = true;
            shopHL.childControlHeight = true;

            var shopView = shopPanel.AddComponent<UIShopView>();
            var shopPresenter = shopPanel.AddComponent<ShopUIPresenter>();

            // LEFT SIDE: Shop Items + Maintenance Shelf + Buy Button
            var shopLeft = CreateUIObject("LeftSide_Shop", shopPanel.transform);
            shopLeft.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var shopLeftVL = shopLeft.AddComponent<VerticalLayoutGroup>();
            shopLeftVL.padding = new RectOffset(16, 16, 16, 16);
            shopLeftVL.spacing = 10;
            shopLeftVL.childForceExpandWidth = true;
            shopLeftVL.childForceExpandHeight = false;
            shopLeftVL.childControlWidth = true;
            shopLeftVL.childControlHeight = true;
            AddImage(shopLeft, new Color(0.12f, 0.16f, 0.14f, 0.95f));
            AddOutline(shopLeft, new Color(0.3f, 0.5f, 0.35f, 0.6f), 2);

            CreateHeader("Header_Items", "ITEMS", shopLeft.transform, new Color(0.18f, 0.26f, 0.20f), 24, 42);

            var shopItemsScroll = CreateGridScrollView("ScrollView_ShopItems", shopLeft.transform, out var shopItemsContent, 3, new Vector2(150, 105));
            var siLE = shopItemsScroll.gameObject.AddComponent<LayoutElement>();
            siLE.preferredHeight = 230;

            var itemsGrid = shopLeft.AddComponent<UIInventoryGrid>();
            var sItemSample = CreateInventorySlotTemplate("ShopSlotTemplate", shopItemsContent.transform);
            itemsGrid.Initialize(shopItemsContent.transform, sItemSample);
            sItemSample.SetActive(false);
            shopView.itemsGrid = itemsGrid;

            // Separate Maintenance Section
            CreateHeader("Header_Maintenance", "MAINTENANCE", shopLeft.transform, new Color(0.24f, 0.22f, 0.16f), 22, 38);

            var maintScroll = CreateGridScrollView("ScrollView_Maintenance", shopLeft.transform, out var maintContent, 3, new Vector2(150, 105));
            var mLE = maintScroll.gameObject.AddComponent<LayoutElement>();
            mLE.preferredHeight = 130;

            var maintGrid = shopLeft.AddComponent<UIInventoryGrid>();
            var mSlotSample = CreateInventorySlotTemplate("MaintSlotTemplate", maintContent.transform);
            maintGrid.Initialize(maintContent.transform, mSlotSample);
            mSlotSample.SetActive(false);
            shopView.maintenanceGrid = maintGrid;

            // Shop Selection Info & Buy Button
            var shopDetailGO = CreateUIObject("ShopDetails", shopLeft.transform);
            shopDetailGO.AddComponent<LayoutElement>().preferredHeight = 28;
            var shopDetailTMP = AddTMP(shopDetailGO, "Pumili ng bibilhin", 19, TextAlignmentOptions.Center);
            shopDetailTMP.color = new Color(0.8f, 1f, 0.85f);
            shopView.shopItemDetailsText = shopDetailTMP;

            var btnBuy = CreateButton("BtnBuy", "BUY", shopLeft.transform, new Color(0.25f, 0.75f, 0.40f), 24, 0, 56);
            shopView.btnBuy = btnBuy.GetComponent<Button>();
            shopView.buyButtonLabel = btnBuy.GetComponentInChildren<TextMeshProUGUI>();

            // RIGHT SIDE: Bag (Draggable Owned Materials) + Sell Button
            var shopRight = CreateUIObject("RightSide_Bag", shopPanel.transform);
            shopRight.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var shopRightVL = shopRight.AddComponent<VerticalLayoutGroup>();
            shopRightVL.padding = new RectOffset(16, 16, 16, 16);
            shopRightVL.spacing = 10;
            shopRightVL.childForceExpandWidth = true;
            shopRightVL.childForceExpandHeight = true;
            shopRightVL.childControlWidth = true;
            shopRightVL.childControlHeight = true;
            AddImage(shopRight, new Color(0.16f, 0.14f, 0.12f, 0.95f));
            AddOutline(shopRight, new Color(0.5f, 0.4f, 0.3f, 0.6f), 2);

            CreateHeader("Header_Bag", "DRAGGABLE BAG", shopRight.transform, new Color(0.28f, 0.22f, 0.18f), 24, 42);

            var bagScroll = CreateGridScrollView("ScrollView_Bag", shopRight.transform, out var bagContent, 3, new Vector2(150, 105));
            bagScroll.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;

            var bagGrid = shopRight.AddComponent<UIInventoryGrid>();
            var bagSlotSample = CreateInventorySlotTemplate("BagSlotTemplate", bagContent.transform);
            bagGrid.Initialize(bagContent.transform, bagSlotSample);
            bagSlotSample.SetActive(false);
            shopView.bagGrid = bagGrid;

            // Bag Selection Info & Sell Button
            var bagDetailGO = CreateUIObject("BagDetails", shopRight.transform);
            bagDetailGO.AddComponent<LayoutElement>().preferredHeight = 28;
            var bagDetailTMP = AddTMP(bagDetailGO, "Pumili ng ibebenta", 19, TextAlignmentOptions.Center);
            bagDetailTMP.color = new Color(1f, 0.85f, 0.7f);
            shopView.bagItemDetailsText = bagDetailTMP;

            var btnSell = CreateButton("BtnSell", "SELL", shopRight.transform, new Color(0.85f, 0.50f, 0.25f), 24, 0, 56);
            shopView.btnSell = btnSell.GetComponent<Button>();
            shopView.sellButtonLabel = btnSell.GetComponentInChildren<TextMeshProUGUI>();

            // Top currency display on navbar
            var currencyGO = CreateUIObject("CurrencyDisplay", navBar.transform);
            var currLE = currencyGO.AddComponent<LayoutElement>();
            currLE.preferredWidth = 200;
            var currTMP = AddTMP(currencyGO, "Pera: ₱350", 22, TextAlignmentOptions.Right);
            currTMP.color = new Color(1f, 0.88f, 0.4f);
            shopView.currencyLabel = currTMP;

            // ═════════════════════════════════════════════════════════════════
            // 8. SOULS & SKILLS PANEL (Sketch 3 Layout & Decision 2)
            // ═════════════════════════════════════════════════════════════════
            var skillPanel = CreateUIObject("SkillPanel", contentArea.transform);
            StretchFull(RT(skillPanel));
            skillPanel.SetActive(false);

            var skillHL = skillPanel.AddComponent<HorizontalLayoutGroup>();
            skillHL.padding = new RectOffset(20, 20, 15, 15);
            skillHL.spacing = 16;
            skillHL.childForceExpandWidth = true;
            skillHL.childForceExpandHeight = true;
            skillHL.childControlWidth = true;
            skillHL.childControlHeight = true;

            var soulSkillView = skillPanel.AddComponent<UISoulSkillView>();
            var soulSkillPresenter = skillPanel.AddComponent<SoulSkillUIPresenter>();

            // LEFT SIDE: 4 Equipped Skill Slots (Decision 2)
            var skillLeft = CreateUIObject("LeftSide_EquippedSlots", skillPanel.transform);
            var sLeftLE = skillLeft.AddComponent<LayoutElement>();
            sLeftLE.preferredWidth = 220;
            sLeftLE.flexibleWidth = 0f;
            var skillLeftVL = skillLeft.AddComponent<VerticalLayoutGroup>();
            skillLeftVL.padding = new RectOffset(12, 12, 14, 14);
            skillLeftVL.spacing = 10;
            skillLeftVL.childForceExpandWidth = true;
            skillLeftVL.childForceExpandHeight = false;
            skillLeftVL.childControlWidth = true;
            skillLeftVL.childControlHeight = true;
            AddImage(skillLeft, new Color(0.14f, 0.12f, 0.22f, 0.95f));
            AddOutline(skillLeft, new Color(0.45f, 0.35f, 0.65f, 0.6f), 2);

            CreateHeader("Header_Equipped", "EQUIPPED SKILLS", skillLeft.transform, new Color(0.24f, 0.18f, 0.36f), 20, 38);

            for (int i = 0; i < 4; i++)
            {
                var eqSlot = CreateSkillEquipSlot($"EquippedSlot_{i + 1}", i + 1, skillLeft.transform);
                soulSkillView.equippedSlots[i] = eqSlot;
            }

            // CENTER: 2-Column Scrollable Skill Catalog + Bottom Craft/Unlock Button
            var skillCenter = CreateUIObject("Center_SkillCatalog", skillPanel.transform);
            skillCenter.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var skillCenterVL = skillCenter.AddComponent<VerticalLayoutGroup>();
            skillCenterVL.padding = new RectOffset(14, 14, 14, 14);
            skillCenterVL.spacing = 10;
            skillCenterVL.childForceExpandWidth = true;
            skillCenterVL.childForceExpandHeight = true;
            skillCenterVL.childControlWidth = true;
            skillCenterVL.childControlHeight = true;
            AddImage(skillCenter, new Color(0.14f, 0.12f, 0.22f, 0.95f));
            AddOutline(skillCenter, new Color(0.45f, 0.35f, 0.65f, 0.6f), 2);

            CreateHeader("Header_Skills", "SKILLS CATALOG", skillCenter.transform, new Color(0.24f, 0.18f, 0.36f), 22, 38);

            var catalogScroll = CreateGridScrollView("ScrollView_Skills", skillCenter.transform, out var catalogContent, 2, new Vector2(230, 115));
            catalogScroll.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;

            var sCardSample = CreateSkillCardTemplate("SkillCardTemplate", catalogContent.transform);
            soulSkillView.skillCatalogContent = catalogContent.transform;
            soulSkillView.skillCardPrefab = sCardSample;
            sCardSample.SetActive(false);

            // Bottom Craft / Unlock Skill Button
            var btnCraftSkill = CreateButton("BtnCraftSkill", "CRAFT / UNLOCK", skillCenter.transform, new Color(0.3f, 0.75f, 0.45f), 22, 0, 54);
            soulSkillView.btnCraftSkill = btnCraftSkill.GetComponent<Button>();
            soulSkillView.craftSkillButtonLabel = btnCraftSkill.GetComponentInChildren<TextMeshProUGUI>();

            // RIGHT SIDE: 5 Soul Selection Buttons (Vertically stacked circular buttons)
            var skillRight = CreateUIObject("RightSide_SoulSelectors", skillPanel.transform);
            var sRightLE = skillRight.AddComponent<LayoutElement>();
            sRightLE.preferredWidth = 140;
            sRightLE.flexibleWidth = 0f;
            var skillRightVL = skillRight.AddComponent<VerticalLayoutGroup>();
            skillRightVL.padding = new RectOffset(10, 10, 14, 14);
            skillRightVL.spacing = 10;
            skillRightVL.childForceExpandWidth = true;
            skillRightVL.childForceExpandHeight = false;
            skillRightVL.childControlWidth = true;
            skillRightVL.childControlHeight = true;
            AddImage(skillRight, new Color(0.14f, 0.12f, 0.22f, 0.95f));
            AddOutline(skillRight, new Color(0.45f, 0.35f, 0.65f, 0.6f), 2);

            CreateHeader("Header_Souls", "SOULS", skillRight.transform, new Color(0.24f, 0.18f, 0.36f), 20, 38);

            soulSkillView.btnKanayunan = CreateRoundButton("Btn_Kanayunan", "Kanayunan", skillRight.transform, new Color(0.35f, 0.70f, 0.35f), 17, 72).GetComponent<Button>();
            soulSkillView.btnDambana   = CreateRoundButton("Btn_Dambana",   "Dambana",   skillRight.transform, new Color(0.85f, 0.55f, 0.22f), 17, 72).GetComponent<Button>();
            soulSkillView.btnKatubigan = CreateRoundButton("Btn_Katubigan", "Katubigan", skillRight.transform, new Color(0.25f, 0.55f, 0.85f), 17, 72).GetComponent<Button>();
            soulSkillView.btnPamayanan = CreateRoundButton("Btn_Pamayanan", "Pamayanan", skillRight.transform, new Color(0.70f, 0.30f, 0.70f), 17, 72).GetComponent<Button>();
            soulSkillView.btnAliwanan  = CreateRoundButton("Btn_Aliwanan",  "Aliwanan",  skillRight.transform, new Color(0.90f, 0.80f, 0.25f), 17, 72).GetComponent<Button>();

            // CENTERED DETAIL MODAL
            var modalRoot = CreateUIObject("SkillDetailModal", skillPanel.transform);
            StretchFull(RT(modalRoot));
            modalRoot.AddComponent<LayoutElement>().ignoreLayout = true;

            var modalBG = CreateUIObject("ModalBackground", modalRoot.transform);
            StretchFull(RT(modalBG));
            AddImage(modalBG, new Color(0, 0, 0, 0.75f));
            var modalBGBtn = modalBG.AddComponent<Button>();

            var modalPanel = CreateUIObject("ModalPanel", modalRoot.transform);
            var modalPanelRT = RT(modalPanel);
            modalPanelRT.anchorMin = new Vector2(0.30f, 0.20f);
            modalPanelRT.anchorMax = new Vector2(0.70f, 0.80f);
            modalPanelRT.offsetMin = Vector2.zero;
            modalPanelRT.offsetMax = Vector2.zero;
            AddImage(modalPanel, new Color(0.16f, 0.14f, 0.26f));
            AddOutline(modalPanel, new Color(0.75f, 0.55f, 1f, 0.85f), 2);

            var modalVL = modalPanel.AddComponent<VerticalLayoutGroup>();
            modalVL.padding = new RectOffset(20, 20, 20, 20);
            modalVL.spacing = 10;
            modalVL.childForceExpandWidth = true;
            modalVL.childForceExpandHeight = false;
            modalVL.childControlWidth = true;
            modalVL.childControlHeight = true;

            var mIcon = CreateUIObject("SkillIcon", modalPanel.transform);
            mIcon.AddComponent<LayoutElement>().preferredHeight = 90;
            var mIconImg = AddImage(mIcon, new Color(0.38f, 0.30f, 0.55f));

            var mName = CreateUIObject("SkillName", modalPanel.transform);
            mName.AddComponent<LayoutElement>().preferredHeight = 36;
            var mNameTMP = AddTMP(mName, "Skill Name", 26, TextAlignmentOptions.Center);
            mNameTMP.fontStyle = FontStyles.Bold;

            var mDesc = CreateUIObject("Description", modalPanel.transform);
            mDesc.AddComponent<LayoutElement>().preferredHeight = 80;
            var mDescTMP = AddTMP(mDesc, "Skill description text details here.", 19, TextAlignmentOptions.Center);
            mDescTMP.color = new Color(0.85f, 0.85f, 0.95f);

            var mCost = CreateUIObject("TalinoCost", modalPanel.transform);
            mCost.AddComponent<LayoutElement>().preferredHeight = 30;
            var mCostTMP = AddTMP(mCost, "20 Talino", 20, TextAlignmentOptions.Center);
            mCostTMP.color = new Color(1f, 0.88f, 0.35f);

            var mClose = CreateButton("BtnClose", "Close", modalPanel.transform, new Color(0.7f, 0.25f, 0.25f), 22, 0, 48);

            var modalComp = modalRoot.AddComponent<SkillDetailModal>();
            modalComp.modalRoot = modalRoot;
            modalComp.skillIcon = mIconImg;
            modalComp.skillNameLabel = mNameTMP;
            modalComp.descriptionLabel = mDescTMP;
            modalComp.talinoCostLabel = mCostTMP;
            modalComp.closeButton = mClose.GetComponent<Button>();
            modalBGBtn.onClick.AddListener(modalComp.Hide);
            soulSkillView.detailModal = modalComp;
            modalRoot.SetActive(false);

            // Wire Navigator
            navigator.craftingPanel = craftPanel;
            navigator.shopPanel = shopPanel;
            navigator.skillPanel = skillPanel;
            navigator.btnCrafting = btnCraftGO.GetComponent<Button>();
            navigator.btnShop = btnShopGO.GetComponent<Button>();
            navigator.btnSkill = btnSkillGO.GetComponent<Button>();

            // Save Scene
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());

            Debug.Log("[APIR APIR] Landscape Lobby UI built successfully matching sketches 1, 2, and 3!");
        }

        // ── Input System Setup ───────────────────────────────────────
        private static void SetupEventSystem()
        {
            var es = Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
            if (es == null)
            {
                var esGO = new GameObject("EventSystem");
                es = esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
                Undo.RegisterCreatedObjectUndo(esGO, "Create EventSystem");
            }

            // Check if InputSystemUIInputModule is available
            var newModuleType = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (newModuleType != null)
            {
                var sim = es.GetComponent<UnityEngine.EventSystems.StandaloneInputModule>();
                if (sim != null) Object.DestroyImmediate(sim);

                if (es.GetComponent(newModuleType) == null)
                {
                    es.gameObject.AddComponent(newModuleType);
                }
            }
            else
            {
                if (es.GetComponent<UnityEngine.EventSystems.StandaloneInputModule>() == null)
                {
                    es.gameObject.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
                }
            }
        }

        // ── Helpers ───────────────────────────────────────────────────

        private static RectTransform RT(GameObject go) => go.GetComponent<RectTransform>();

        private static GameObject CreateUIObject(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            return go;
        }

        private static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static Image AddImage(GameObject go, Color color)
        {
            var img = go.AddComponent<Image>();
            img.color = color;
            return img;
        }

        private static Outline AddOutline(GameObject go, Color color, int dist = 1)
        {
            var outline = go.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = new Vector2(dist, -dist);
            return outline;
        }

        private static TextMeshProUGUI AddTMP(GameObject go, string text, int fontSize = 22, TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = align;
            tmp.color = Color.white;
            return tmp;
        }

        private static GameObject CreateButton(string name, string label, Transform parent, Color bgColor, int fontSize = 22, int width = 0, int height = 0)
        {
            var go = CreateUIObject(name, parent);
            if (width > 0 || height > 0)
            {
                var le = go.AddComponent<LayoutElement>();
                if (width > 0) le.preferredWidth = width;
                if (height > 0) le.preferredHeight = height;
            }
            AddImage(go, bgColor);
            go.AddComponent<Button>();

            var lblGO = CreateUIObject("Label", go.transform);
            StretchFull(RT(lblGO));
            var tmp = AddTMP(lblGO, label, fontSize, TextAlignmentOptions.Center);
            tmp.color = Color.white;
            return go;
        }

        private static GameObject CreateRoundButton(string name, string label, Transform parent, Color bgColor, int fontSize = 16, int height = 70)
        {
            var go = CreateUIObject(name, parent);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            AddImage(go, bgColor);
            AddOutline(go, new Color(0.2f, 0.2f, 0.3f, 0.8f), 2);
            go.AddComponent<Button>();

            var lblGO = CreateUIObject("Label", go.transform);
            StretchFull(RT(lblGO));
            var tmp = AddTMP(lblGO, label, fontSize, TextAlignmentOptions.Center);
            tmp.color = Color.white;
            return go;
        }

        private static GameObject CreateHeader(string name, string text, Transform parent, Color bgColor, int fontSize = 22, int height = 44)
        {
            var hdr = CreateUIObject(name, parent);
            hdr.AddComponent<LayoutElement>().preferredHeight = height;
            AddImage(hdr, bgColor);
            var textGO = CreateUIObject("Text", hdr.transform);
            StretchFull(RT(textGO));
            AddTMP(textGO, text, fontSize, TextAlignmentOptions.Center);
            return hdr;
        }

        private static ScrollRect CreateGridScrollView(string name, Transform parent, out GameObject contentGO, int columns = 3, Vector2? cellSize = null)
        {
            var scrollGO = CreateUIObject(name, parent);
            AddImage(scrollGO, new Color(0, 0, 0, 0.25f));
            var scroll = scrollGO.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;

            var vp = CreateUIObject("Viewport", scrollGO.transform);
            StretchFull(RT(vp));
            vp.AddComponent<RectMask2D>();

            contentGO = CreateUIObject("Content", vp.transform);
            var crt = RT(contentGO);
            crt.anchorMin = new Vector2(0, 1);
            crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;

            var csf = contentGO.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var grid = contentGO.AddComponent<GridLayoutGroup>();
            grid.padding = new RectOffset(8, 8, 8, 8);
            grid.spacing = new Vector2(8, 8);
            grid.cellSize = cellSize ?? new Vector2(140, 100);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;

            scroll.viewport = RT(vp);
            scroll.content = crt;
            return scroll;
        }

        private static UICraftingIngredientSlot CreateCraftingSlot(string name, int index, Transform parent)
        {
            var slotGO = CreateUIObject(name, parent);
            AddImage(slotGO, new Color(0.18f, 0.16f, 0.28f));
            AddOutline(slotGO, new Color(0.55f, 0.45f, 0.8f, 0.6f), 2);

            var vl = slotGO.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(6, 6, 6, 6);
            vl.spacing = 4;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = true;
            vl.childControlWidth = true;
            vl.childControlHeight = true;

            var iconGO = CreateUIObject("Icon", slotGO.transform);
            var iconImg = AddImage(iconGO, new Color(0.35f, 0.35f, 0.55f));

            var lblGO = CreateUIObject("Label", slotGO.transform);
            lblGO.AddComponent<LayoutElement>().preferredHeight = 22;
            var lblTMP = AddTMP(lblGO, $"Slot {index + 1}", 16, TextAlignmentOptions.Center);
            lblTMP.color = new Color(0.8f, 0.8f, 0.95f);

            var emptyPlaceholder = CreateUIObject("Placeholder", slotGO.transform);
            StretchFull(RT(emptyPlaceholder));
            var phTMP = AddTMP(emptyPlaceholder, "+", 24, TextAlignmentOptions.Center);
            phTMP.color = new Color(0.5f, 0.5f, 0.7f, 0.5f);

            var slotComp = slotGO.AddComponent<UICraftingIngredientSlot>();
            slotComp.slotIndex = index;

            return slotComp;
        }

        private static GameObject CreateInventorySlotTemplate(string name, Transform parent)
        {
            var slotGO = CreateUIObject(name, parent);
            AddImage(slotGO, new Color(0.20f, 0.20f, 0.32f));
            AddOutline(slotGO, new Color(0.45f, 0.45f, 0.65f, 0.5f), 1);

            var vl = slotGO.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(6, 6, 4, 4);
            vl.spacing = 2;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = true;
            vl.childControlWidth = true;
            vl.childControlHeight = true;

            var iconGO = CreateUIObject("Icon", slotGO.transform);
            var iconImg = AddImage(iconGO, new Color(0.35f, 0.38f, 0.52f));

            var nameGO = CreateUIObject("Name", slotGO.transform);
            nameGO.AddComponent<LayoutElement>().preferredHeight = 20;
            var nameTMP = AddTMP(nameGO, "Item", 16, TextAlignmentOptions.Center);

            var badgeRow = CreateUIObject("BadgeRow", slotGO.transform);
            badgeRow.AddComponent<LayoutElement>().preferredHeight = 18;
            var badgeHL = badgeRow.AddComponent<HorizontalLayoutGroup>();
            badgeHL.childForceExpandWidth = true;
            badgeHL.childControlWidth = false;

            var qtyGO = CreateUIObject("Qty", badgeRow.transform);
            var qtyTMP = AddTMP(qtyGO, "x1", 15, TextAlignmentOptions.Left);
            qtyTMP.color = new Color(0.75f, 0.95f, 0.75f);

            var spacer = CreateUIObject("Spacer", badgeRow.transform);
            spacer.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var priceGO = CreateUIObject("Price", badgeRow.transform);
            var priceTMP = AddTMP(priceGO, "₱50", 15, TextAlignmentOptions.Right);
            priceTMP.color = new Color(1f, 0.85f, 0.4f);

            var slotComp = slotGO.AddComponent<UIInventorySlot>();
            return slotGO;
        }

        private static EquippedSkillSlotUI CreateSkillEquipSlot(string name, int index, Transform parent)
        {
            var slotGO = CreateUIObject(name, parent);
            var le = slotGO.AddComponent<LayoutElement>();
            le.preferredHeight = 90;
            le.minHeight = 90;

            AddImage(slotGO, new Color(0.18f, 0.16f, 0.30f));
            AddOutline(slotGO, new Color(0.6f, 0.45f, 0.9f, 0.6f), 2);

            var vl = slotGO.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(6, 6, 6, 6);
            vl.spacing = 2;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = true;
            vl.childControlWidth = true;
            vl.childControlHeight = true;

            var iconGO = CreateUIObject("Icon", slotGO.transform);
            var iconImg = AddImage(iconGO, new Color(0.30f, 0.25f, 0.45f));

            var lblGO = CreateUIObject("Label", slotGO.transform);
            lblGO.AddComponent<LayoutElement>().preferredHeight = 20;
            var lblTMP = AddTMP(lblGO, $"Slot {index}", 16, TextAlignmentOptions.Center);
            lblTMP.color = new Color(0.7f, 0.7f, 0.9f);

            var nameGO = CreateUIObject("SkillName", slotGO.transform);
            nameGO.AddComponent<LayoutElement>().preferredHeight = 20;
            var nameTMP = AddTMP(nameGO, "", 16, TextAlignmentOptions.Center);
            nameTMP.color = new Color(1f, 0.9f, 0.4f);
            nameGO.SetActive(false);

            var slotComp = slotGO.AddComponent<EquippedSkillSlotUI>();
            slotComp.slotIndex = index;
            slotComp.slotIcon = iconImg;
            slotComp.slotLabel = lblTMP;
            slotComp.skillNameLabel = nameTMP;

            return slotComp;
        }

        private static GameObject CreateSkillCardTemplate(string name, Transform parent)
        {
            var cardGO = CreateUIObject(name, parent);
            AddImage(cardGO, new Color(0.18f, 0.16f, 0.32f));
            AddOutline(cardGO, new Color(0.55f, 0.40f, 0.85f, 0.7f), 1);

            var vl = cardGO.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(8, 8, 6, 6);
            vl.spacing = 4;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;
            vl.childControlWidth = true;
            vl.childControlHeight = true;

            var iconGO = CreateUIObject("Icon", cardGO.transform);
            iconGO.AddComponent<LayoutElement>().preferredHeight = 45;
            var iconImg = AddImage(iconGO, new Color(0.35f, 0.30f, 0.55f));

            var nameGO = CreateUIObject("SkillName", cardGO.transform);
            nameGO.AddComponent<LayoutElement>().preferredHeight = 24;
            var nameTMP = AddTMP(nameGO, "Skill Name", 17, TextAlignmentOptions.Center);
            nameTMP.color = Color.white;

            var costRow = CreateUIObject("CostRow", cardGO.transform);
            costRow.AddComponent<LayoutElement>().preferredHeight = 20;
            var costHL = costRow.AddComponent<HorizontalLayoutGroup>();
            costHL.childForceExpandWidth = true;
            costHL.childControlWidth = false;

            var spacer = CreateUIObject("Spacer", costRow.transform);
            spacer.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var costGO = CreateUIObject("TalinoCost", costRow.transform);
            var costTMP = AddTMP(costGO, "15✦", 16, TextAlignmentOptions.Right);
            costTMP.color = new Color(1f, 0.88f, 0.35f);

            cardGO.AddComponent<CanvasGroup>();
            var cardComp = cardGO.AddComponent<SkillCardUI>();
            cardComp.iconImage = iconImg;
            cardComp.skillNameLabel = nameTMP;
            cardComp.talinoCostLabel = costTMP;

            return cardGO;
        }
    }
}
#endif
