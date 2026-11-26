using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Công cụ Unity Editor tự động sinh Prefab ô vật phẩm (InventorySlot.prefab)
/// và toàn bộ giao diện Kho đồ uGUI + TextMeshPro (1B Popup Grid + 2B Detail Modal + HUD).
/// </summary>
public static class InventoryUIGenerator
{
    private const string CanvasName = "InventoryCanvas";
    private const string PrefabFolderPath = "Assets/Prefabs/UI";
    private const string SlotPrefabPath = "Assets/Prefabs/UI/InventorySlot.prefab";
    private const int DefaultInitialPoolSize = 12;

    // Bảng màu tối ánh trăng thống nhất cho giao diện Kho đồ (uGUI)
    private static readonly Color DimOverlayColor = new Color(0.03f, 0.04f, 0.08f, 0.74f);
    private static readonly Color MainPanelColor = new Color(0.08f, 0.10f, 0.16f, 0.97f);
    private static readonly Color SubPanelColor = new Color(0.05f, 0.07f, 0.11f, 0.90f);
    private static readonly Color DetailPanelColor = new Color(0.11f, 0.14f, 0.21f, 0.96f);
    private static readonly Color SlotBgColor = new Color(0.15f, 0.18f, 0.26f, 0.95f);
    private static readonly Color SelectionGlowColor = new Color(0.95f, 0.78f, 0.35f, 0.28f);
    private static readonly Color SelectionOutlineColor = new Color(0.98f, 0.82f, 0.36f, 0.95f);
    private static readonly Color GiftcodeBadgeBgColor = new Color(0.90f, 0.60f, 0.14f, 0.96f);
    private static readonly Color GiftcodeTextColor = new Color(1.00f, 0.80f, 0.30f, 1.00f);
    private static readonly Color HudButtonColor = new Color(0.12f, 0.16f, 0.25f, 0.95f);
    private static readonly Color PrimaryActionColor = new Color(0.20f, 0.46f, 0.72f, 1.00f);
    private static readonly Color CloseDangerColor = new Color(0.66f, 0.20f, 0.24f, 0.95f);
    private static readonly Color BorderSubtleColor = new Color(0.55f, 0.64f, 0.82f, 0.45f);
    private static readonly Color PrimaryTextColor = new Color(0.95f, 0.94f, 0.90f, 1.00f);
    private static readonly Color SecondaryTextColor = new Color(0.76f, 0.80f, 0.88f, 1.00f);

    [MenuItem("Vong Nguyet/UI/Generate Inventory UI (1B + 2B)")]
    public static void GenerateInventoryUI()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid())
        {
            EditorUtility.DisplayDialog("Lỗi Scene", "Không tìm thấy Scene đang mở hợp lệ.", "Đóng");
            return;
        }

        // 1. Kiểm tra nếu đã có InventoryCanvas trong Scene (kể cả đang bị ẩn) -> hỏi xác nhận trước khi ghi đè
        List<GameObject> existingCanvases = FindSceneObjectsByName(activeScene, CanvasName);
        if (existingCanvases.Count > 0)
        {
            bool overwrite = EditorUtility.DisplayDialog(
                "Ghi đè InventoryCanvas?",
                $"Trong Scene '{activeScene.name}' đã tồn tại đối tượng '{CanvasName}'.\n" +
                "Bạn có muốn xóa bản cũ và tạo mới lại toàn bộ giao diện Kho đồ (1B + 2B) không?",
                "Ghi đè",
                "Hủy"
            );

            if (!overwrite)
            {
                return;
            }

            for (int i = 0; i < existingCanvases.Count; i++)
            {
                if (existingCanvases[i] != null)
                {
                    Undo.DestroyObjectImmediate(existingCanvases[i]);
                }
            }
        }

        // 2. Lấy TMP_FontAsset tiếng Việt của dự án (ưu tiên DialogueCanvas -> Scene -> TMP default)
        TMP_FontAsset projectFont = FindProjectTMPFont(activeScene);

        // 3. Tạo hoặc cập nhật Prefab ô vật phẩm chuẩn GUID tại Assets/Prefabs/UI/InventorySlot.prefab
        InventorySlotUI slotPrefabAsset = CreateOrUpdateSlotPrefab(projectFont);
        if (slotPrefabAsset == null)
        {
            Debug.LogError("[InventoryUIGenerator] Tạo Prefab InventorySlot.prefab thất bại. Dừng quá trình sinh UI.");
            return;
        }

        // 4. Tạo InventoryCanvas (1B + 2B + HUD) trong Scene đang mở và nối toàn bộ SerializedField
        GameObject inventoryCanvas = BuildInventoryCanvasInScene(activeScene, projectFont, slotPrefabAsset);

        // 5. Đánh dấu Scene đã thay đổi để người dùng bấm Ctrl+S lưu chuẩn GUID
        EditorSceneManager.MarkSceneDirty(activeScene);
        Selection.activeGameObject = inventoryCanvas;

        Debug.Log(
            $"[InventoryUIGenerator] Đã sinh thành công '{SlotPrefabPath}' và '{CanvasName}' trong Scene '{activeScene.name}'. " +
            $"Font sử dụng: {(projectFont != null ? projectFont.name : "Chưa có (TMP Default null)")}."
        );
    }

    // ================= TẠO PREFAB Ô VẬT PHẨM (INVENTORY SLOT) =================

    private static InventorySlotUI CreateOrUpdateSlotPrefab(TMP_FontAsset fontAsset)
    {
        EnsureFolderExists(PrefabFolderPath);

        int uiLayer = GetUILayer();

        // Root: InventorySlot (110x110 dễ chạm trên Mobile)
        GameObject root = CreateUIObject("InventorySlot", null, uiLayer);
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(110f, 110f);

        Image rootBg = root.AddComponent<Image>();
        rootBg.color = SlotBgColor;
        rootBg.raycastTarget = true; // Phải bật để Button nhận click PC / chạm Mobile

        Outline rootOutline = root.AddComponent<Outline>();
        rootOutline.effectColor = BorderSubtleColor;
        rootOutline.effectDistance = new Vector2(1.5f, -1.5f);

        Button slotButton = root.AddComponent<Button>();
        slotButton.targetGraphic = rootBg;
        ColorBlock slotColors = slotButton.colors;
        slotColors.normalColor = Color.white;
        slotColors.highlightedColor = new Color(1.12f, 1.15f, 1.22f, 1f);
        slotColors.pressedColor = new Color(0.82f, 0.85f, 0.92f, 1f);
        slotColors.selectedColor = Color.white;
        // Giữ ô trống trong Pool hiển thị mờ nhẹ, rõ lưới khi interactable = false
        slotColors.disabledColor = new Color(0.75f, 0.78f, 0.86f, 0.52f);
        slotButton.colors = slotColors;

        InventorySlotUI slotUI = root.AddComponent<InventorySlotUI>();

        // 1. SelectionHighlight (đặt trước IconImage để khung sáng phủ kín ô mà không che biểu tượng vật phẩm)
        GameObject selectionHighlight = CreateUIObject("SelectionHighlight", root.transform, uiLayer);
        StretchFull(selectionHighlight.GetComponent<RectTransform>());
        Image highlightImg = selectionHighlight.AddComponent<Image>();
        highlightImg.color = SelectionGlowColor;
        highlightImg.raycastTarget = false;

        Outline highlightOutline = selectionHighlight.AddComponent<Outline>();
        highlightOutline.effectColor = SelectionOutlineColor;
        highlightOutline.effectDistance = new Vector2(3f, -3f);
        selectionHighlight.SetActive(false);

        // 2. IconImage (căn giữa có padding 12px, preserveAspect = true, mặc định tắt)
        GameObject iconGO = CreateUIObject("IconImage", root.transform, uiLayer);
        StretchWithPadding(iconGO.GetComponent<RectTransform>(), 12f, 12f, 12f, 12f);
        Image iconImage = iconGO.AddComponent<Image>();
        iconImage.color = Color.white;
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;
        iconImage.enabled = false;
        iconGO.SetActive(false);

        // 3. QuantityText (góc dưới phải, mặc định tắt)
        GameObject quantityGO = CreateUIObject("QuantityText", root.transform, uiLayer);
        RectTransform qtyRect = quantityGO.GetComponent<RectTransform>();
        qtyRect.anchorMin = new Vector2(1f, 0f);
        qtyRect.anchorMax = new Vector2(1f, 0f);
        qtyRect.pivot = new Vector2(1f, 0f);
        qtyRect.anchoredPosition = new Vector2(-6f, 4f);
        qtyRect.sizeDelta = new Vector2(64f, 28f);

        TextMeshProUGUI quantityText = CreateTMPText(
            quantityGO,
            string.Empty,
            22f,
            FontStyles.Bold,
            TextAlignmentOptions.BottomRight,
            Color.white,
            fontAsset
        );
        quantityGO.SetActive(false);

        // 4. GiftcodeBadge (góc trên trái, Image + chữ nhỏ, mặc định tắt)
        GameObject giftcodeBadge = CreateUIObject("GiftcodeBadge", root.transform, uiLayer);
        RectTransform badgeRect = giftcodeBadge.GetComponent<RectTransform>();
        badgeRect.anchorMin = new Vector2(0f, 1f);
        badgeRect.anchorMax = new Vector2(0f, 1f);
        badgeRect.pivot = new Vector2(0f, 1f);
        badgeRect.anchoredPosition = new Vector2(4f, -4f);
        badgeRect.sizeDelta = new Vector2(54f, 20f);

        Image badgeImg = giftcodeBadge.AddComponent<Image>();
        badgeImg.color = GiftcodeBadgeBgColor;
        badgeImg.raycastTarget = false;

        GameObject badgeLabelGO = CreateUIObject("GiftcodeLabel", giftcodeBadge.transform, uiLayer);
        StretchFull(badgeLabelGO.GetComponent<RectTransform>());
        CreateTMPText(
            badgeLabelGO,
            "GIFT",
            12f,
            FontStyles.Bold,
            TextAlignmentOptions.Center,
            new Color(0.10f, 0.08f, 0.04f, 1f),
            fontAsset
        );
        giftcodeBadge.SetActive(false);

        // Gán toàn bộ [SerializeField] khớp chính xác 100% với InventorySlotUI.cs
        SerializedObject slotSO = new SerializedObject(slotUI);
        AssignObjectReference(slotSO, "slotButton", slotButton);
        AssignObjectReference(slotSO, "iconImage", iconImage);
        AssignObjectReference(slotSO, "quantityText", quantityText);
        AssignObjectReference(slotSO, "selectionHighlight", selectionHighlight);
        AssignObjectReference(slotSO, "giftcodeBadge", giftcodeBadge);
        slotSO.ApplyModifiedPropertiesWithoutUndo();

        // Lưu Prefab chuẩn GUID và hủy object tạm trong Scene
        PrefabUtility.SaveAsPrefabAsset(root, SlotPrefabPath, out bool saveSuccess);
        UnityEngine.Object.DestroyImmediate(root);

        if (!saveSuccess)
        {
            Debug.LogError($"[InventoryUIGenerator] Không thể lưu Prefab tại '{SlotPrefabPath}'.");
            return null;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        return AssetDatabase.LoadAssetAtPath<InventorySlotUI>(SlotPrefabPath);
    }

    // ================= TẠO INVENTORY CANVAS (1B + 2B + HUD) =================

    private static GameObject BuildInventoryCanvasInScene(Scene activeScene, TMP_FontAsset fontAsset, InventorySlotUI slotPrefabAsset)
    {
        int uiLayer = GetUILayer();

        // Root: InventoryCanvas (luôn Active để nhận phím tắt I / Escape và sự kiện InventoryManager)
        GameObject canvasGO = CreateUIObject(CanvasName, null, uiLayer);
        SceneManager.MoveGameObjectToScene(canvasGO, activeScene);
        Undo.RegisterCreatedObjectUndo(canvasGO, "Generate Inventory UI");

        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f; // Cân bằng cả màn hình PC 16:9 và điện thoại Android

        canvasGO.AddComponent<GraphicRaycaster>();
        InventoryUIController controller = canvasGO.AddComponent<InventoryUIController>();

        // ------------------------------------------------------------------
        // NHÓM 1: HUD GÓC TRÊN BÊN PHẢI (Luôn hiển thị ngoài Scene)
        // ------------------------------------------------------------------
        GameObject hudTopRight = CreateUIObject("HudTopRight", canvasGO.transform, uiLayer);
        RectTransform hudRect = hudTopRight.GetComponent<RectTransform>();
        hudRect.anchorMin = new Vector2(1f, 1f);
        hudRect.anchorMax = new Vector2(1f, 1f);
        hudRect.pivot = new Vector2(1f, 1f);
        hudRect.anchoredPosition = new Vector2(-28f, -24f);
        hudRect.sizeDelta = new Vector2(320f, 72f);

        // Nút mở túi đồ (164x66 >= 120x60 dễ bấm trên màn hình cảm ứng Mobile)
        GameObject openBagGO = CreateUIObject("OpenBagButton", hudTopRight.transform, uiLayer);
        RectTransform openBagRect = openBagGO.GetComponent<RectTransform>();
        openBagRect.anchorMin = new Vector2(1f, 1f);
        openBagRect.anchorMax = new Vector2(1f, 1f);
        openBagRect.pivot = new Vector2(1f, 1f);
        openBagRect.anchoredPosition = Vector2.zero;
        openBagRect.sizeDelta = new Vector2(164f, 66f);

        Image openBagImg = openBagGO.AddComponent<Image>();
        openBagImg.color = HudButtonColor;
        openBagImg.raycastTarget = true;

        Outline openBagOutline = openBagGO.AddComponent<Outline>();
        openBagOutline.effectColor = BorderSubtleColor;
        openBagOutline.effectDistance = new Vector2(1.5f, -1.5f);

        Button openBagButton = openBagGO.AddComponent<Button>();
        openBagButton.targetGraphic = openBagImg;

        GameObject openBagLabelGO = CreateUIObject("OpenBagLabel", openBagGO.transform, uiLayer);
        StretchWithPadding(openBagLabelGO.GetComponent<RectTransform>(), 16f, 54f, 6f, 6f);
        CreateTMPText(
            openBagLabelGO,
            "Túi đồ",
            25f,
            FontStyles.Bold,
            TextAlignmentOptions.MidlineLeft,
            PrimaryTextColor,
            fontAsset
        );

        // Badge tổng số ô đang có vật phẩm trên nút Túi đồ
        GameObject badgeBgGO = CreateUIObject("BagCountBadgeBg", openBagGO.transform, uiLayer);
        RectTransform badgeBgRect = badgeBgGO.GetComponent<RectTransform>();
        badgeBgRect.anchorMin = new Vector2(1f, 0.5f);
        badgeBgRect.anchorMax = new Vector2(1f, 0.5f);
        badgeBgRect.pivot = new Vector2(1f, 0.5f);
        badgeBgRect.anchoredPosition = new Vector2(-12f, 0f);
        badgeBgRect.sizeDelta = new Vector2(38f, 38f);

        Image badgeBgImg = badgeBgGO.AddComponent<Image>();
        badgeBgImg.color = GiftcodeBadgeBgColor;
        badgeBgImg.raycastTarget = false;

        GameObject bagCountTextGO = CreateUIObject("BagCountBadgeText", badgeBgGO.transform, uiLayer);
        StretchFull(bagCountTextGO.GetComponent<RectTransform>());
        TextMeshProUGUI bagCountBadgeText = CreateTMPText(
            bagCountTextGO,
            "0",
            21f,
            FontStyles.Bold,
            TextAlignmentOptions.Center,
            new Color(0.08f, 0.09f, 0.12f, 1f),
            fontAsset
        );

        // EquippedHudRoot: đặt ngay cạnh bên trái nút Túi đồ (mặc định tắt)
        GameObject equippedHudRoot = CreateUIObject("EquippedHudRoot", hudTopRight.transform, uiLayer);
        RectTransform equippedRect = equippedHudRoot.GetComponent<RectTransform>();
        equippedRect.anchorMin = new Vector2(1f, 1f);
        equippedRect.anchorMax = new Vector2(1f, 1f);
        equippedRect.pivot = new Vector2(1f, 1f);
        equippedRect.anchoredPosition = new Vector2(-180f, 0f);
        equippedRect.sizeDelta = new Vector2(118f, 66f);

        Image equippedBgImg = equippedHudRoot.AddComponent<Image>();
        equippedBgImg.color = HudButtonColor;
        equippedBgImg.raycastTarget = false; // Ảnh nền HUD trang trí tắt Raycast Target

        Outline equippedOutline = equippedHudRoot.AddComponent<Outline>();
        equippedOutline.effectColor = SelectionOutlineColor;
        equippedOutline.effectDistance = new Vector2(1.5f, -1.5f);

        GameObject equippedIconGO = CreateUIObject("EquippedHudIcon", equippedHudRoot.transform, uiLayer);
        RectTransform equippedIconRect = equippedIconGO.GetComponent<RectTransform>();
        equippedIconRect.anchorMin = new Vector2(0f, 0.5f);
        equippedIconRect.anchorMax = new Vector2(0f, 0.5f);
        equippedIconRect.pivot = new Vector2(0f, 0.5f);
        equippedIconRect.anchoredPosition = new Vector2(10f, 0f);
        equippedIconRect.sizeDelta = new Vector2(52f, 52f);

        Image equippedHudIcon = equippedIconGO.AddComponent<Image>();
        equippedHudIcon.color = Color.white;
        equippedHudIcon.preserveAspect = true;
        equippedHudIcon.raycastTarget = false;
        equippedHudIcon.enabled = false;

        GameObject unequipBtnGO = CreateUIObject("UnequipHudButton", equippedHudRoot.transform, uiLayer);
        RectTransform unequipBtnRect = unequipBtnGO.GetComponent<RectTransform>();
        unequipBtnRect.anchorMin = new Vector2(1f, 0.5f);
        unequipBtnRect.anchorMax = new Vector2(1f, 0.5f);
        unequipBtnRect.pivot = new Vector2(1f, 0.5f);
        unequipBtnRect.anchoredPosition = new Vector2(-8f, 0f);
        unequipBtnRect.sizeDelta = new Vector2(40f, 40f);

        Image unequipBtnImg = unequipBtnGO.AddComponent<Image>();
        unequipBtnImg.color = CloseDangerColor;
        unequipBtnImg.raycastTarget = true;

        Button unequipHudButton = unequipBtnGO.AddComponent<Button>();
        unequipHudButton.targetGraphic = unequipBtnImg;

        GameObject unequipTextGO = CreateUIObject("UnequipText", unequipBtnGO.transform, uiLayer);
        StretchFull(unequipTextGO.GetComponent<RectTransform>());
        CreateTMPText(
            unequipTextGO,
            "X",
            20f,
            FontStyles.Bold,
            TextAlignmentOptions.Center,
            Color.white,
            fontAsset
        );

        equippedHudRoot.SetActive(false);

        // ------------------------------------------------------------------
        // NHÓM 2: POPUP ROOT (1B Popup Grid + 2B Detail Modal — mặc định tắt)
        // ------------------------------------------------------------------
        GameObject popupRoot = CreateUIObject("PopupRoot", canvasGO.transform, uiLayer);
        StretchFull(popupRoot.GetComponent<RectTransform>());

        // DimBackground: phủ toàn màn hình, BẬT raycastTarget = true để chặn bấm xuyên xuống Scene (KetSat / WorldItemPickup)
        GameObject dimBgGO = CreateUIObject("DimBackground", popupRoot.transform, uiLayer);
        StretchFull(dimBgGO.GetComponent<RectTransform>());
        Image dimBgImg = dimBgGO.AddComponent<Image>();
        dimBgImg.color = DimOverlayColor;
        dimBgImg.raycastTarget = true;

        // MainPanel: căn giữa màn hình, kích thước 1300x760
        GameObject mainPanel = CreateUIObject("MainPanel", popupRoot.transform, uiLayer);
        RectTransform mainPanelRect = mainPanel.GetComponent<RectTransform>();
        mainPanelRect.anchorMin = new Vector2(0.5f, 0.5f);
        mainPanelRect.anchorMax = new Vector2(0.5f, 0.5f);
        mainPanelRect.pivot = new Vector2(0.5f, 0.5f);
        mainPanelRect.anchoredPosition = Vector2.zero;
        mainPanelRect.sizeDelta = new Vector2(1300f, 760f);

        Image mainPanelImg = mainPanel.AddComponent<Image>();
        mainPanelImg.color = MainPanelColor;
        mainPanelImg.raycastTarget = false; // DimBackground phía sau đã đảm nhiệm chặn bấm xuyên

        Outline mainPanelOutline = mainPanel.AddComponent<Outline>();
        mainPanelOutline.effectColor = BorderSubtleColor;
        mainPanelOutline.effectDistance = new Vector2(2f, -2f);

        // Tiêu đề khung Kho đồ
        GameObject titleGO = CreateUIObject("PopupTitleText", mainPanel.transform, uiLayer);
        RectTransform titleRect = titleGO.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -18f);
        titleRect.sizeDelta = new Vector2(-140f, 52f);
        CreateTMPText(
            titleGO,
            "KHO ĐỒ",
            34f,
            FontStyles.Bold,
            TextAlignmentOptions.MidlineLeft,
            PrimaryTextColor,
            fontAsset
        );

        // Đường kẻ mảnh phân cách Header
        GameObject dividerGO = CreateUIObject("HeaderDivider", mainPanel.transform, uiLayer);
        RectTransform dividerRect = dividerGO.GetComponent<RectTransform>();
        dividerRect.anchorMin = new Vector2(0f, 1f);
        dividerRect.anchorMax = new Vector2(1f, 1f);
        dividerRect.pivot = new Vector2(0.5f, 1f);
        dividerRect.anchoredPosition = new Vector2(0f, -82f);
        dividerRect.sizeDelta = new Vector2(-72f, 2f);
        Image dividerImg = dividerGO.AddComponent<Image>();
        dividerImg.color = BorderSubtleColor;
        dividerImg.raycastTarget = false;

        // Nút CloseBagButton (X) góc trên bên phải MainPanel
        GameObject closeBagGO = CreateUIObject("CloseBagButton", mainPanel.transform, uiLayer);
        RectTransform closeBagRect = closeBagGO.GetComponent<RectTransform>();
        closeBagRect.anchorMin = new Vector2(1f, 1f);
        closeBagRect.anchorMax = new Vector2(1f, 1f);
        closeBagRect.pivot = new Vector2(1f, 1f);
        closeBagRect.anchoredPosition = new Vector2(-20f, -14f);
        closeBagRect.sizeDelta = new Vector2(56f, 56f);

        Image closeBagImg = closeBagGO.AddComponent<Image>();
        closeBagImg.color = CloseDangerColor;
        closeBagImg.raycastTarget = true;

        Button closeBagButton = closeBagGO.AddComponent<Button>();
        closeBagButton.targetGraphic = closeBagImg;

        GameObject closeTextGO = CreateUIObject("CloseBagText", closeBagGO.transform, uiLayer);
        StretchFull(closeTextGO.GetComponent<RectTransform>());
        CreateTMPText(
            closeTextGO,
            "X",
            28f,
            FontStyles.Bold,
            TextAlignmentOptions.Center,
            Color.white,
            fontAsset
        );

        // --- VÙNG BÊN TRÁI (1B): GridArea với ScrollRect + Viewport (RectMask2D) + SlotContainer ---
        // Kích thước 560x624 vừa khít 4 cột x 110px + 3 khoảng cách x 14px + padding 20px mỗi bên (= 522px)
        GameObject gridAreaGO = CreateUIObject("GridArea", mainPanel.transform, uiLayer);
        RectTransform gridAreaRect = gridAreaGO.GetComponent<RectTransform>();
        gridAreaRect.anchorMin = new Vector2(0f, 1f);
        gridAreaRect.anchorMax = new Vector2(0f, 1f);
        gridAreaRect.pivot = new Vector2(0f, 1f);
        gridAreaRect.anchoredPosition = new Vector2(36f, -102f);
        gridAreaRect.sizeDelta = new Vector2(560f, 622f);

        Image gridAreaBg = gridAreaGO.AddComponent<Image>();
        gridAreaBg.color = SubPanelColor;
        gridAreaBg.raycastTarget = false;

        ScrollRect scrollRect = gridAreaGO.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 35f;

        // Viewport dùng RectMask2D thay vì Mask cũ để giảm Draw Call và không cần stencil buffer
        GameObject viewportGO = CreateUIObject("Viewport", gridAreaGO.transform, uiLayer);
        RectTransform viewportRect = viewportGO.GetComponent<RectTransform>();
        StretchWithPadding(viewportRect, 4f, 4f, 4f, 4f);

        // Viewport cần Image trong suốt bật raycastTarget = true để nhận thao tác kéo cuộn cả ở vùng trống
        Image viewportImg = viewportGO.AddComponent<Image>();
        viewportImg.color = new Color(0f, 0f, 0f, 0f);
        viewportImg.raycastTarget = true;
        viewportGO.AddComponent<RectMask2D>();

        // SlotContainer: gắn GridLayoutGroup (110x110, spacing 14x14, 4 cột) + ContentSizeFitter
        GameObject slotContainerGO = CreateUIObject("SlotContainer", viewportGO.transform, uiLayer);
        RectTransform slotContainerRect = slotContainerGO.GetComponent<RectTransform>();
        slotContainerRect.anchorMin = new Vector2(0f, 1f);
        slotContainerRect.anchorMax = new Vector2(1f, 1f);
        slotContainerRect.pivot = new Vector2(0.5f, 1f);
        slotContainerRect.anchoredPosition = Vector2.zero;
        slotContainerRect.sizeDelta = Vector2.zero;

        GridLayoutGroup gridLayout = slotContainerGO.AddComponent<GridLayoutGroup>();
        gridLayout.cellSize = new Vector2(110f, 110f);
        gridLayout.spacing = new Vector2(14f, 14f);
        gridLayout.padding = new RectOffset(20, 20, 20, 20);
        gridLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
        gridLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
        gridLayout.childAlignment = TextAnchor.UpperCenter;
        gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        gridLayout.constraintCount = 4;

        ContentSizeFitter contentSizeFitter = slotContainerGO.AddComponent<ContentSizeFitter>();
        contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = viewportRect;
        scrollRect.content = slotContainerRect;

        // --- VÙNG BÊN PHẢI (2B): Khung nền gợi ý + DetailPanel (mặc định tắt khi chưa chọn món) ---
        GameObject detailPlaceholderGO = CreateUIObject("DetailAreaPlaceholder", mainPanel.transform, uiLayer);
        RectTransform placeholderRect = detailPlaceholderGO.GetComponent<RectTransform>();
        placeholderRect.anchorMin = new Vector2(1f, 1f);
        placeholderRect.anchorMax = new Vector2(1f, 1f);
        placeholderRect.pivot = new Vector2(1f, 1f);
        placeholderRect.anchoredPosition = new Vector2(-36f, -102f);
        placeholderRect.sizeDelta = new Vector2(640f, 622f);

        Image placeholderBg = detailPlaceholderGO.AddComponent<Image>();
        placeholderBg.color = SubPanelColor;
        placeholderBg.raycastTarget = false;

        GameObject placeholderHintGO = CreateUIObject("EmptySelectionHintText", detailPlaceholderGO.transform, uiLayer);
        StretchWithPadding(placeholderHintGO.GetComponent<RectTransform>(), 40f, 40f, 40f, 40f);
        CreateTMPText(
            placeholderHintGO,
            "Chọn một vật phẩm ở bên trái để xem chi tiết.",
            24f,
            FontStyles.Italic,
            TextAlignmentOptions.Center,
            SecondaryTextColor,
            fontAsset
        );

        // DetailPanel (2B) đặt đè khớp hoàn toàn lên DetailAreaPlaceholder, mặc định tắt
        GameObject detailPanel = CreateUIObject("DetailPanel", mainPanel.transform, uiLayer);
        RectTransform detailPanelRect = detailPanel.GetComponent<RectTransform>();
        detailPanelRect.anchorMin = new Vector2(1f, 1f);
        detailPanelRect.anchorMax = new Vector2(1f, 1f);
        detailPanelRect.pivot = new Vector2(1f, 1f);
        detailPanelRect.anchoredPosition = new Vector2(-36f, -102f);
        detailPanelRect.sizeDelta = new Vector2(640f, 622f);

        Image detailPanelImg = detailPanel.AddComponent<Image>();
        detailPanelImg.color = DetailPanelColor;
        detailPanelImg.raycastTarget = false;

        // Khung ảnh lớn của vật phẩm đang xem
        GameObject detailIconFrame = CreateUIObject("DetailIconFrame", detailPanel.transform, uiLayer);
        RectTransform detailIconFrameRect = detailIconFrame.GetComponent<RectTransform>();
        detailIconFrameRect.anchorMin = new Vector2(0.5f, 1f);
        detailIconFrameRect.anchorMax = new Vector2(0.5f, 1f);
        detailIconFrameRect.pivot = new Vector2(0.5f, 1f);
        detailIconFrameRect.anchoredPosition = new Vector2(0f, -28f);
        detailIconFrameRect.sizeDelta = new Vector2(170f, 170f);

        Image detailIconFrameImg = detailIconFrame.AddComponent<Image>();
        detailIconFrameImg.color = SubPanelColor;
        detailIconFrameImg.raycastTarget = false;

        GameObject detailIconGO = CreateUIObject("DetailIconImage", detailIconFrame.transform, uiLayer);
        StretchWithPadding(detailIconGO.GetComponent<RectTransform>(), 14f, 14f, 14f, 14f);
        Image detailIconImage = detailIconGO.AddComponent<Image>();
        detailIconImage.color = Color.white;
        detailIconImage.preserveAspect = true;
        detailIconImage.raycastTarget = false;
        detailIconImage.enabled = false;

        // Tên vật phẩm
        GameObject detailNameGO = CreateUIObject("DetailNameText", detailPanel.transform, uiLayer);
        RectTransform detailNameRect = detailNameGO.GetComponent<RectTransform>();
        detailNameRect.anchorMin = new Vector2(0f, 1f);
        detailNameRect.anchorMax = new Vector2(1f, 1f);
        detailNameRect.pivot = new Vector2(0.5f, 1f);
        detailNameRect.anchoredPosition = new Vector2(0f, -214f);
        detailNameRect.sizeDelta = new Vector2(-64f, 48f);
        TextMeshProUGUI detailNameText = CreateTMPText(
            detailNameGO,
            "Tên vật phẩm",
            30f,
            FontStyles.Bold,
            TextAlignmentOptions.Center,
            PrimaryTextColor,
            fontAsset
        );

        // Nhãn nguồn Webshop Giftcode (màu vàng nổi bật, mặc định tắt khi là vật phẩm Gameplay)
        GameObject detailSourceGO = CreateUIObject("DetailSourceText", detailPanel.transform, uiLayer);
        RectTransform detailSourceRect = detailSourceGO.GetComponent<RectTransform>();
        detailSourceRect.anchorMin = new Vector2(0f, 1f);
        detailSourceRect.anchorMax = new Vector2(1f, 1f);
        detailSourceRect.pivot = new Vector2(0.5f, 1f);
        detailSourceRect.anchoredPosition = new Vector2(0f, -264f);
        detailSourceRect.sizeDelta = new Vector2(-64f, 30f);
        TextMeshProUGUI detailSourceText = CreateTMPText(
            detailSourceGO,
            "Quà tặng Webshop / Giftcode",
            20f,
            FontStyles.Bold,
            TextAlignmentOptions.Center,
            GiftcodeTextColor,
            fontAsset
        );
        detailSourceGO.SetActive(false);

        // Mô tả chi tiết vật phẩm
        GameObject detailDescGO = CreateUIObject("DetailDescriptionText", detailPanel.transform, uiLayer);
        RectTransform detailDescRect = detailDescGO.GetComponent<RectTransform>();
        detailDescRect.anchorMin = new Vector2(0f, 0f);
        detailDescRect.anchorMax = new Vector2(1f, 1f);
        detailDescRect.offsetMin = new Vector2(36f, 114f);
        detailDescRect.offsetMax = new Vector2(-36f, -304f);
        TextMeshProUGUI detailDescriptionText = CreateTMPText(
            detailDescGO,
            string.Empty,
            23f,
            FontStyles.Normal,
            TextAlignmentOptions.TopLeft,
            SecondaryTextColor,
            fontAsset
        );
        detailDescriptionText.textWrappingMode = TextWrappingModes.Normal;
        detailDescriptionText.overflowMode = TextOverflowModes.Overflow;

        // Nút Sử dụng / Cầm lên / Bỏ cầm
        GameObject useOrEquipGO = CreateUIObject("UseOrEquipButton", detailPanel.transform, uiLayer);
        RectTransform useOrEquipRect = useOrEquipGO.GetComponent<RectTransform>();
        useOrEquipRect.anchorMin = new Vector2(0.5f, 0f);
        useOrEquipRect.anchorMax = new Vector2(0.5f, 0f);
        useOrEquipRect.pivot = new Vector2(0.5f, 0f);
        useOrEquipRect.anchoredPosition = new Vector2(0f, 26f);
        useOrEquipRect.sizeDelta = new Vector2(300f, 66f);

        Image useOrEquipImg = useOrEquipGO.AddComponent<Image>();
        useOrEquipImg.color = PrimaryActionColor;
        useOrEquipImg.raycastTarget = true;

        Button useOrEquipButton = useOrEquipGO.AddComponent<Button>();
        useOrEquipButton.targetGraphic = useOrEquipImg;

        GameObject useOrEquipTextGO = CreateUIObject("UseOrEquipButtonText", useOrEquipGO.transform, uiLayer);
        StretchFull(useOrEquipTextGO.GetComponent<RectTransform>());
        TextMeshProUGUI useOrEquipButtonText = CreateTMPText(
            useOrEquipTextGO,
            "Cầm lên",
            26f,
            FontStyles.Bold,
            TextAlignmentOptions.Center,
            Color.white,
            fontAsset
        );

        detailPanel.SetActive(false);
        popupRoot.SetActive(false);

        // ------------------------------------------------------------------
        // NỐI TOÀN BỘ THAM CHIẾU SERIALIZEDFIELD VÀO InventoryUIController
        // ------------------------------------------------------------------
        SerializedObject controllerSO = new SerializedObject(controller);

        // Điều khiển Popup Kho đồ (1B)
        AssignObjectReference(controllerSO, "popupRoot", popupRoot);
        AssignObjectReference(controllerSO, "openBagButton", openBagButton);
        AssignObjectReference(controllerSO, "closeBagButton", closeBagButton);
        AssignObjectReference(controllerSO, "bagCountBadgeText", bagCountBadgeText);
        AssignObjectReference(controllerSO, "slotContainer", slotContainerGO.transform);
        AssignObjectReference(controllerSO, "slotPrefab", slotPrefabAsset);

        SerializedProperty initialPoolProp = controllerSO.FindProperty("initialPoolSize");
        if (initialPoolProp != null)
        {
            initialPoolProp.intValue = DefaultInitialPoolSize;
        }
        else
        {
            Debug.LogError("[InventoryUIGenerator] Không tìm thấy SerializedProperty 'initialPoolSize' trên InventoryUIController.");
        }

        // Khung Chi tiết vật phẩm (2B)
        AssignObjectReference(controllerSO, "detailPanel", detailPanel);
        AssignObjectReference(controllerSO, "detailIconImage", detailIconImage);
        AssignObjectReference(controllerSO, "detailNameText", detailNameText);
        AssignObjectReference(controllerSO, "detailDescriptionText", detailDescriptionText);
        AssignObjectReference(controllerSO, "detailSourceText", detailSourceText);
        AssignObjectReference(controllerSO, "useOrEquipButton", useOrEquipButton);
        AssignObjectReference(controllerSO, "useOrEquipButtonText", useOrEquipButtonText);

        // HUD Vật phẩm đang cầm ngoài Scene
        AssignObjectReference(controllerSO, "equippedHudRoot", equippedHudRoot);
        AssignObjectReference(controllerSO, "equippedHudIcon", equippedHudIcon);
        AssignObjectReference(controllerSO, "unequipHudButton", unequipHudButton);

        controllerSO.ApplyModifiedPropertiesWithoutUndo();

        return canvasGO;
    }

    // ================= HÀM TIỆN ÍCH TẠO UI & TÌM FONT =================

    private static TMP_FontAsset FindProjectTMPFont(Scene activeScene)
    {
        // Ưu tiên 1: Tìm TMP_Text trên DialogueCanvas để giữ đúng font tiếng Việt của dự án
        List<GameObject> dialogueCanvases = FindSceneObjectsByName(activeScene, "DialogueCanvas");
        for (int i = 0; i < dialogueCanvases.Count; i++)
        {
            TMP_FontAsset font = ExtractFirstFont(dialogueCanvases[i]);
            if (font != null)
            {
                return font;
            }
        }

        // Ưu tiên 2: Nếu tên Canvas hội thoại khác 'DialogueCanvas', thử tìm từ DialogueManager trong Scene
        DialogueManager dialogueManager = UnityEngine.Object.FindFirstObjectByType<DialogueManager>(FindObjectsInactive.Include);
        if (dialogueManager != null)
        {
            TMP_FontAsset font = ExtractFirstFont(dialogueManager.gameObject);
            if (font != null)
            {
                return font;
            }
        }

        // Ưu tiên 3: Quét toàn bộ TMP_Text hiện có trong Scene đang mở
        GameObject[] roots = activeScene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            TMP_FontAsset font = ExtractFirstFont(roots[i]);
            if (font != null)
            {
                return font;
            }
        }

        // Dự phòng cuối cùng: dùng font mặc định của TextMeshPro
        return TMP_Settings.defaultFontAsset;
    }

    private static TMP_FontAsset ExtractFirstFont(GameObject root)
    {
        if (root == null)
        {
            return null;
        }

        TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i] != null && texts[i].font != null)
            {
                return texts[i].font;
            }
        }

        return null;
    }

    private static List<GameObject> FindSceneObjectsByName(Scene scene, string targetName)
    {
        List<GameObject> matches = new List<GameObject>();
        GameObject[] roots = scene.GetRootGameObjects();

        for (int i = 0; i < roots.Length; i++)
        {
            Transform[] allTransforms = roots[i].GetComponentsInChildren<Transform>(true);
            for (int j = 0; j < allTransforms.Length; j++)
            {
                if (string.Equals(allTransforms[j].name, targetName, StringComparison.Ordinal))
                {
                    matches.Add(allTransforms[j].gameObject);
                }
            }
        }

        return matches;
    }

    private static void EnsureFolderExists(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }

        string[] parts = folderPath.Split('/');
        string currentPath = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string nextPath = $"{currentPath}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(nextPath))
            {
                AssetDatabase.CreateFolder(currentPath, parts[i]);
            }

            currentPath = nextPath;
        }
    }

    private static GameObject CreateUIObject(string name, Transform parent, int uiLayer)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = uiLayer;

        if (parent != null)
        {
            go.transform.SetParent(parent, false);
        }

        return go;
    }

    private static TextMeshProUGUI CreateTMPText(
        GameObject target,
        string content,
        float fontSize,
        FontStyles fontStyle,
        TextAlignmentOptions alignment,
        Color color,
        TMP_FontAsset fontAsset)
    {
        TextMeshProUGUI tmp = target.AddComponent<TextMeshProUGUI>();
        if (fontAsset != null)
        {
            tmp.font = fontAsset;
        }

        tmp.text = content;
        tmp.fontSize = fontSize;
        tmp.fontStyle = fontStyle;
        tmp.alignment = alignment;
        tmp.color = color;
        tmp.raycastTarget = false; // Tắt Raycast Target cho toàn bộ văn bản trang trí để tối ưu uGUI
        return tmp;
    }

    private static void StretchFull(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
    }

    private static void StretchWithPadding(RectTransform rect, float left, float right, float top, float bottom)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
        rect.pivot = new Vector2(0.5f, 0.5f);
    }

    private static void AssignObjectReference(SerializedObject serializedObject, string propertyName, UnityEngine.Object value)
    {
        SerializedProperty prop = serializedObject.FindProperty(propertyName);
        if (prop == null)
        {
            Debug.LogError(
                $"[InventoryUIGenerator] Không tìm thấy SerializedProperty '{propertyName}' trên '{serializedObject.targetObject.GetType().Name}'.");
            return;
        }

        prop.objectReferenceValue = value;
    }

    private static int GetUILayer()
    {
        int layer = LayerMask.NameToLayer("UI");
        return layer >= 0 ? layer : 5;
    }
}
