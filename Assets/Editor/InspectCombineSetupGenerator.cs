using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Công cụ Unity Editor tự động hoàn thiện toàn bộ hệ thống Soi & Ghép đồ (Inspect & Combine):
/// 1) Sinh ItemInspectCanvas (uGUI + TextMeshPro, sortingOrder = 70).
/// 2) Nâng cấp InventoryCanvas hiện có thêm thanh nút SOI / GHÉP / HỦY GHÉP và CombineStatusText.
/// 3) Tạo bộ ScriptableObject mẫu Chapter 1 (4 ItemData + 1 ItemCombineRecipe) và đăng ký vào InventoryManager.
/// 4) Đặt 3 vật phẩm Greybox nhặt thử trong Room_PhongNgu.
/// </summary>
public static class InspectCombineSetupGenerator
{
    private const string InspectCanvasName = "ItemInspectCanvas";
    private const string TargetRoomName = "Room_PhongNgu";

    private const string Chapter1ItemsFolder = "Assets/Items/Chapter1";
    private const string RecipesFolder = "Assets/Items/Recipes";

    private const string BuiltinSquareSpriteV2Path = "Packages/com.unity.2d.sprite/Editor/ObjectMenuCreation/DefaultAssets/Textures/v2/Square.png";
    private const string BuiltinSquareSpriteV1Path = "Packages/com.unity.2d.sprite/Editor/ObjectMenuCreation/DefaultAssets/Textures/Square.png";

    // Bảng màu tối uGUI nhất quán với InventoryCanvas / KeypadCanvas / PauseCanvas
    private static readonly Color InspectBackdropColor = new Color(0.00f, 0.00f, 0.00f, 0.85f);
    private static readonly Color WindowBgColor = new Color(0.08f, 0.10f, 0.15f, 0.98f);
    private static readonly Color ImageFrameBgColor = new Color(0.04f, 0.05f, 0.09f, 0.95f);
    private static readonly Color InspectButtonColor = new Color(0.18f, 0.46f, 0.78f, 1.00f);   // Xanh dương (SOI)
    private static readonly Color CombineButtonColor = new Color(0.84f, 0.56f, 0.14f, 1.00f);   // Cam/vàng (GHÉP)
    private static readonly Color CancelButtonColor = new Color(0.56f, 0.22f, 0.24f, 1.00f);    // Xám/đỏ (HỦY GHÉP)
    private static readonly Color CloseButtonColor = new Color(0.46f, 0.20f, 0.24f, 1.00f);
    private static readonly Color BorderSubtleColor = new Color(0.55f, 0.65f, 0.84f, 0.45f);
    private static readonly Color PrimaryTextColor = new Color(0.95f, 0.94f, 0.90f, 1.00f);
    private static readonly Color SecondaryTextColor = new Color(0.82f, 0.85f, 0.92f, 1.00f);
    private static readonly Color CombineStatusColor = new Color(1.00f, 0.84f, 0.36f, 1.00f);

    // Màu pastel cho 3 vật phẩm Greybox để phân biệt rõ với cửa (xanh lá/cam) và két sắt
    private static readonly Color PickupLeftLetterColor = new Color(0.65f, 0.84f, 0.98f, 1.00f);  // Xanh pastel
    private static readonly Color PickupRightLetterColor = new Color(0.66f, 0.92f, 0.78f, 1.00f); // Bạc hà pastel
    private static readonly Color PickupNewspaperColor = new Color(0.98f, 0.86f, 0.68f, 1.00f);   // Vàng kem pastel

    [MenuItem("Vong Nguyet/Inventory/Generate Inspect & Combine UI + Ch1 Sample Data")]
    public static void GenerateInspectCombineSetup()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid())
        {
            EditorUtility.DisplayDialog("Lỗi Scene", "Không tìm thấy Scene đang mở hợp lệ.", "Đóng");
            return;
        }

        // 1. Kiểm tra nếu Scene đã có ItemInspectCanvas -> hỏi xác nhận trước khi xóa tạo lại
        List<GameObject> existingInspectCanvases = FindSceneObjectsByName(activeScene, InspectCanvasName);
        if (existingInspectCanvases.Count > 0)
        {
            bool overwrite = EditorUtility.DisplayDialog(
                "Tạo lại ItemInspectCanvas?",
                $"Trong Scene '{activeScene.name}' đã tồn tại '{InspectCanvasName}'.\n" +
                "Bạn có muốn cập nhật lại ItemInspectCanvas, thanh nút Soi/Ghép trên InventoryCanvas và bộ dữ liệu mẫu Chapter 1 không?",
                "Tạo lại / Cập nhật",
                "Hủy"
            );

            if (!overwrite)
            {
                return;
            }

            for (int i = 0; i < existingInspectCanvases.Count; i++)
            {
                if (existingInspectCanvases[i] != null)
                {
                    Undo.DestroyObjectImmediate(existingInspectCanvases[i]);
                }
            }
        }

        TMP_FontAsset projectFont = FindProjectTMPFont(activeScene);
        Sprite squareSprite = LoadBuiltinSquareSprite();

        // Phần 1: Sinh ItemInspectCanvas (sortingOrder = 70)
        GameObject inspectCanvas = BuildItemInspectCanvas(activeScene, projectFont);

        // Phần 2: Nâng cấp InventoryCanvas sẵn có (không phá vỡ cấu trúc Slot cũ)
        bool upgradedInventoryUI = UpgradeExistingInventoryCanvas(projectFont);

        // Phần 3: Tạo 4 ItemData + 1 ItemCombineRecipe mẫu Chapter 1 và đăng ký vào InventoryManager
        SampleDataBundle sampleData = CreateOrUpdateChapter1SampleAssets(squareSprite);
        bool registeredInManager = RegisterSampleDataToInventoryManager(sampleData);

        // Phần 4: Đặt 3 vật phẩm Greybox nhặt thử trong Room_PhongNgu (nếu chưa có)
        int createdOrUpdatedPickups = CreateSamplePickupsInRoomPhongNgu(activeScene, sampleData, squareSprite);

        // Phần 5: Lưu toàn bộ Asset và đánh dấu Scene bẩn để bấm Ctrl+S lưu chuẩn GUID
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorSceneManager.MarkSceneDirty(activeScene);
        Selection.activeGameObject = inspectCanvas;

        Debug.Log(
            $"[InspectCombineSetupGenerator] Hoàn tất!\n" +
            $"- ItemInspectCanvas (sortingOrder = 70): OK\n" +
            $"- Nâng cấp InventoryCanvas (SOI/GHÉP/HỦY GHÉP): {(upgradedInventoryUI ? "OK" : "KHÔNG TÌM THẤY InventoryUIController")}\n" +
            $"- Dữ liệu mẫu Chapter 1 (4 ItemData + 1 Recipe) & InventoryManager Database: {(registeredInManager ? "OK" : "KHÔNG TÌM THẤY InventoryManager")}\n" +
            $"- Vật phẩm Greybox trong {TargetRoomName}: {createdOrUpdatedPickups}/3\n" +
            $"- Font sử dụng: {(projectFont != null ? projectFont.name : "TMP Default null")}"
        );
    }

    // ================= PHẦN 1: SINH ITEM INSPECT CANVAS (SORTING ORDER = 70) =================

    private static GameObject BuildItemInspectCanvas(Scene activeScene, TMP_FontAsset fontAsset)
    {
        int uiLayer = GetUILayer();

        // Root ItemInspectCanvas luôn Active ở cấp Root Scene để Awake() khởi tạo Singleton ItemInspectUIController.Instance
        GameObject canvasGO = CreateUIObject(InspectCanvasName, null, uiLayer);
        SceneManager.MoveGameObjectToScene(canvasGO, activeScene);
        Undo.RegisterCreatedObjectUndo(canvasGO, "Create ItemInspectCanvas");

        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Thứ tự sortingOrder chuẩn của dự án:
        // InventoryCanvas (10) < KeypadCanvas (60) < ItemInspectCanvas (70) < PauseCanvas (80) < TransitionCanvas (100)
        canvas.sortingOrder = 70;

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();
        ItemInspectUIController controller = canvasGO.AddComponent<ItemInspectUIController>();

        // InspectPanelRoot full stretch 0..1, nền đen mờ + Button không Transition làm backdropCloseButton
        GameObject panelRoot = CreateUIObject("InspectPanelRoot", canvasGO.transform, uiLayer);
        StretchFull(panelRoot.GetComponent<RectTransform>());

        Image backdropImg = panelRoot.AddComponent<Image>();
        backdropImg.color = InspectBackdropColor;
        backdropImg.raycastTarget = true;

        Button backdropCloseButton = panelRoot.AddComponent<Button>();
        backdropCloseButton.targetGraphic = backdropImg;
        backdropCloseButton.transition = Selectable.Transition.None;

        // InspectWindow căn giữa (1080 x 780), BẬT raycastTarget = true để bấm vào khung đọc chữ không xuyên xuống backdropCloseButton
        GameObject windowGO = CreateUIObject("InspectWindow", panelRoot.transform, uiLayer);
        RectTransform windowRect = windowGO.GetComponent<RectTransform>();
        windowRect.anchorMin = new Vector2(0.5f, 0.5f);
        windowRect.anchorMax = new Vector2(0.5f, 0.5f);
        windowRect.pivot = new Vector2(0.5f, 0.5f);
        windowRect.anchoredPosition = Vector2.zero;
        windowRect.sizeDelta = new Vector2(1080f, 780f);

        Image windowBg = windowGO.AddComponent<Image>();
        windowBg.color = WindowBgColor;
        windowBg.raycastTarget = true; // Bắt buộc bật để chặn xuyên click xuống backdropCloseButton phía sau

        Outline windowOutline = windowGO.AddComponent<Outline>();
        windowOutline.effectColor = BorderSubtleColor;
        windowOutline.effectDistance = new Vector2(2f, -2f);

        // ItemNameText cỡ chữ 36 căn giữa phía trên
        GameObject nameGO = CreateUIObject("ItemNameText", windowGO.transform, uiLayer);
        RectTransform nameRect = nameGO.GetComponent<RectTransform>();
        nameRect.anchorMin = new Vector2(0.5f, 1f);
        nameRect.anchorMax = new Vector2(0.5f, 1f);
        nameRect.pivot = new Vector2(0.5f, 1f);
        nameRect.anchoredPosition = new Vector2(0f, -24f);
        nameRect.sizeDelta = new Vector2(960f, 52f);
        TextMeshProUGUI itemNameText = CreateTMPText(
            nameGO,
            "TÊN VẬT PHẨM",
            36f,
            FontStyles.Bold,
            TextAlignmentOptions.Center,
            PrimaryTextColor,
            fontAsset
        );

        // Khung nền ảnh phóng to + InspectImage (420 x 360, BẮT BUỘC preserveAspect = true)
        GameObject imageFrameGO = CreateUIObject("InspectImageFrame", windowGO.transform, uiLayer);
        RectTransform imageFrameRect = imageFrameGO.GetComponent<RectTransform>();
        imageFrameRect.anchorMin = new Vector2(0.5f, 1f);
        imageFrameRect.anchorMax = new Vector2(0.5f, 1f);
        imageFrameRect.pivot = new Vector2(0.5f, 1f);
        imageFrameRect.anchoredPosition = new Vector2(0f, -88f);
        imageFrameRect.sizeDelta = new Vector2(440f, 376f);

        Image imageFrameBg = imageFrameGO.AddComponent<Image>();
        imageFrameBg.color = ImageFrameBgColor;
        imageFrameBg.raycastTarget = false;

        GameObject inspectImgGO = CreateUIObject("InspectImage", imageFrameGO.transform, uiLayer);
        RectTransform inspectImgRect = inspectImgGO.GetComponent<RectTransform>();
        inspectImgRect.anchorMin = new Vector2(0.5f, 0.5f);
        inspectImgRect.anchorMax = new Vector2(0.5f, 0.5f);
        inspectImgRect.pivot = new Vector2(0.5f, 0.5f);
        inspectImgRect.anchoredPosition = Vector2.zero;
        inspectImgRect.sizeDelta = new Vector2(420f, 360f);

        Image inspectImage = inspectImgGO.AddComponent<Image>();
        inspectImage.color = Color.white;
        inspectImage.preserveAspect = true; // Giữ nguyên tỉ lệ ảnh thư/báo/ảnh bé gái
        inspectImage.raycastTarget = false;

        // DescriptionText cỡ chữ 24 bật word wrapping, căn gọn bên dưới ảnh
        GameObject descGO = CreateUIObject("DescriptionText", windowGO.transform, uiLayer);
        RectTransform descRect = descGO.GetComponent<RectTransform>();
        descRect.anchorMin = Vector2.zero;
        descRect.anchorMax = Vector2.one;
        descRect.offsetMin = new Vector2(60f, 118f);
        descRect.offsetMax = new Vector2(-60f, -482f);

        TextMeshProUGUI descriptionText = CreateTMPText(
            descGO,
            string.Empty,
            24f,
            FontStyles.Normal,
            TextAlignmentOptions.TopLeft,
            SecondaryTextColor,
            fontAsset
        );
        descriptionText.textWrappingMode = TextWrappingModes.Normal;
        descriptionText.overflowMode = TextOverflowModes.Overflow;

        // CloseButton ("ĐÓNG", 200 x 72 >= 160 x 72 dễ chạm trên Mobile)
        Button closeButton = CreateStyledButton("CloseButton", windowGO.transform, "ĐÓNG", 26f, CloseButtonColor, fontAsset, uiLayer);
        RectTransform closeBtnRect = closeButton.GetComponent<RectTransform>();
        closeBtnRect.anchorMin = new Vector2(0.5f, 0f);
        closeBtnRect.anchorMax = new Vector2(0.5f, 0f);
        closeBtnRect.pivot = new Vector2(0.5f, 0f);
        closeBtnRect.anchoredPosition = new Vector2(0f, 24f);
        closeBtnRect.sizeDelta = new Vector2(200f, 72f);

        // Ẩn mặc định khi khởi động
        panelRoot.SetActive(false);

        // Nối chính xác 100% tên biến [SerializeField] của ItemInspectUIController.cs
        SerializedObject controllerSO = new SerializedObject(controller);
        AssignObjectReference(controllerSO, "panelRoot", panelRoot);
        AssignObjectReference(controllerSO, "itemNameText", itemNameText);
        AssignObjectReference(controllerSO, "inspectImage", inspectImage);
        AssignObjectReference(controllerSO, "descriptionText", descriptionText);
        AssignObjectReference(controllerSO, "closeButton", closeButton);
        AssignObjectReference(controllerSO, "backdropCloseButton", backdropCloseButton);
        controllerSO.ApplyModifiedPropertiesWithoutUndo();

        return canvasGO;
    }

    // ================= PHẦN 2: NÂNG CẤP INVENTORY CANVAS SẴN CÓ =================

    private static bool UpgradeExistingInventoryCanvas(TMP_FontAsset fontAsset)
    {
        InventoryUIController invController = UnityEngine.Object.FindFirstObjectByType<InventoryUIController>(FindObjectsInactive.Include);
        if (invController == null)
        {
            Debug.LogError("[InspectCombineSetupGenerator] Không tìm thấy InventoryUIController trong Scene! Hãy chạy 'Vong Nguyet/UI/Generate Inventory UI (1B + 2B)' trước.");
            return false;
        }

        SerializedObject invSO = new SerializedObject(invController);
        GameObject detailPanel = invSO.FindProperty("detailPanel")?.objectReferenceValue as GameObject;
        GameObject popupRoot = invSO.FindProperty("popupRoot")?.objectReferenceValue as GameObject;
        Button useOrEquipButton = invSO.FindProperty("useOrEquipButton")?.objectReferenceValue as Button;
        TMP_Text detailDescText = invSO.FindProperty("detailDescriptionText")?.objectReferenceValue as TMP_Text;

        Transform targetParent = detailPanel != null
            ? detailPanel.transform
            : (popupRoot != null ? popupRoot.transform : invController.transform);

        int uiLayer = targetParent.gameObject.layer;

        // Xóa bản InspectCombineBar / CombineStatusText cũ (nếu chạy lại tool) để tránh trùng lặp
        DestroyDirectChildIfExists(targetParent, "InspectCombineBar");
        DestroyDirectChildIfExists(targetParent, "CombineStatusText");

        // Căn lại gọn gàng UseOrEquipButton và DetailDescriptionText trong DetailPanel để không đè lên thanh Soi/Ghép mới
        if (detailPanel != null)
        {
            if (useOrEquipButton != null && useOrEquipButton.transform.parent == detailPanel.transform)
            {
                RectTransform useRect = useOrEquipButton.GetComponent<RectTransform>();
                useRect.anchoredPosition = new Vector2(0f, 134f);
                useRect.sizeDelta = new Vector2(300f, 58f);
            }

            if (detailDescText != null && detailDescText.transform.parent == detailPanel.transform)
            {
                RectTransform descRect = detailDescText.GetComponent<RectTransform>();
                descRect.offsetMin = new Vector2(descRect.offsetMin.x, 202f);
            }
        }

        // Tạo CombineStatusText đặt ngay phía trên InspectCombineBar
        GameObject statusGO = CreateUIObject("CombineStatusText", targetParent, uiLayer);
        Undo.RegisterCreatedObjectUndo(statusGO, "Create CombineStatusText");
        RectTransform statusRect = statusGO.GetComponent<RectTransform>();
        statusRect.anchorMin = new Vector2(0f, 0f);
        statusRect.anchorMax = new Vector2(1f, 0f);
        statusRect.pivot = new Vector2(0.5f, 0f);
        statusRect.anchoredPosition = new Vector2(0f, 94f);
        statusRect.sizeDelta = new Vector2(-56f, 32f);

        TextMeshProUGUI combineStatusText = CreateTMPText(
            statusGO,
            string.Empty,
            22f,
            FontStyles.Bold,
            TextAlignmentOptions.Center,
            CombineStatusColor,
            fontAsset
        );

        // Tạo InspectCombineBar dùng HorizontalLayoutGroup đặt ở đáy bảng chi tiết
        GameObject barGO = CreateUIObject("InspectCombineBar", targetParent, uiLayer);
        Undo.RegisterCreatedObjectUndo(barGO, "Create InspectCombineBar");
        RectTransform barRect = barGO.GetComponent<RectTransform>();
        barRect.anchorMin = new Vector2(0f, 0f);
        barRect.anchorMax = new Vector2(1f, 0f);
        barRect.pivot = new Vector2(0.5f, 0f);
        barRect.anchoredPosition = new Vector2(0f, 18f);
        barRect.sizeDelta = new Vector2(-56f, 70f);

        HorizontalLayoutGroup barLayout = barGO.AddComponent<HorizontalLayoutGroup>();
        barLayout.spacing = 14f;
        barLayout.childAlignment = TextAnchor.MiddleCenter;
        barLayout.childControlWidth = true;
        barLayout.childControlHeight = true;
        barLayout.childForceExpandWidth = true;
        barLayout.childForceExpandHeight = true;

        Button inspectButton = CreateStyledButton("InspectButton", barGO.transform, "SOI", 24f, InspectButtonColor, fontAsset, uiLayer);
        AddMinHeight(inspectButton.gameObject, 70f);

        Button combineButton = CreateStyledButton("CombineButton", barGO.transform, "GHÉP", 24f, CombineButtonColor, fontAsset, uiLayer);
        AddMinHeight(combineButton.gameObject, 70f);

        Button cancelCombineButton = CreateStyledButton("CancelCombineButton", barGO.transform, "HỦY GHÉP", 22f, CancelButtonColor, fontAsset, uiLayer);
        AddMinHeight(cancelCombineButton.gameObject, 70f);
        cancelCombineButton.gameObject.SetActive(false);

        // Nối chính xác 100% tên biến [SerializeField] mới của InventoryUIController.cs (giữ nguyên toàn bộ tham chiếu cũ)
        AssignObjectReference(invSO, "inspectButton", inspectButton);
        AssignObjectReference(invSO, "combineButton", combineButton);
        AssignObjectReference(invSO, "cancelCombineButton", cancelCombineButton);
        AssignObjectReference(invSO, "combineStatusText", combineStatusText);
        invSO.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(invController);
        return true;
    }

    // ================= PHẦN 3: TẠO DỮ LIỆU MẪU CHAPTER 1 & ĐĂNG KÝ SAVE DATABASE =================

    private struct SampleDataBundle
    {
        public ItemData LeftHalf;
        public ItemData RightHalf;
        public ItemData FullLetter;
        public ItemData Newspaper;
        public ItemCombineRecipe LetterRecipe;
    }

    private static SampleDataBundle CreateOrUpdateChapter1SampleAssets(Sprite fallbackIcon)
    {
        EnsureFolderExists(Chapter1ItemsFolder);
        EnsureFolderExists(RecipesFolder);

        SampleDataBundle bundle = new SampleDataBundle
        {
            LeftHalf = CreateOrUpdateItemData(
                $"{Chapter1ItemsFolder}/Ch1_ManhThu_NuaTrai.asset",
                itemId: "Ch1_ManhThu_NuaTrai",
                displayName: "Mảnh thư bị xé (Nửa trái)",
                shortDescription: "Một nửa mảnh giấy viết tay bị xé vội. Hãy thử ghép với nửa còn lại hoặc bấm Soi để đọc kỹ.",
                inspectDescription: "Nửa trái tờ giấy viết tay bị xé vội:\n'Cất ảnh con bé vào két... hai số đầu là 14...'",
                fallbackIcon: fallbackIcon
            ),
            RightHalf = CreateOrUpdateItemData(
                $"{Chapter1ItemsFolder}/Ch1_ManhThu_NuaPhai.asset",
                itemId: "Ch1_ManhThu_NuaPhai",
                displayName: "Mảnh thư bị xé (Nửa phải)",
                shortDescription: "Nửa phải của một tờ giấy bị xé rách, vẫn còn rõ nét chữ viết tay.",
                inspectDescription: "Nửa phải tờ giấy bị xé:\n'...đừng quên ngày mưa hôm đó... hai số cuối là 08.'",
                fallbackIcon: fallbackIcon
            ),
            FullLetter = CreateOrUpdateItemData(
                $"{Chapter1ItemsFolder}/Ch1_LaThu_CoChu.asset",
                itemId: "Ch1_LaThu_CoChu",
                displayName: "Lá thư của cô chủ",
                shortDescription: "Hai mảnh giấy bị xé đã được ghép lại hoàn chỉnh. Bấm Soi để xem toàn bộ mật mã.",
                inspectDescription: "Hai mảnh giấy đã khớp hoàn toàn:\n'Cất ảnh con bé vào két, đừng quên ngày mưa trên đèo Mệnh Khư hôm đó — mật mã: 1408.'",
                fallbackIcon: fallbackIcon
            ),
            Newspaper = CreateOrUpdateItemData(
                $"{Chapter1ItemsFolder}/Ch1_MauBao_ChayXem.asset",
                itemId: "Ch1_MauBao_ChayXem",
                displayName: "Mẩu báo cũ cháy xém",
                shortDescription: "Một mẩu báo cũ bị cháy xém phần mép, nhắc tới vụ sạt lở trên đèo Mệnh Khư.",
                inspectDescription: "\"...sạt lở nghiêm trọng trên đèo Mệnh Khư... một bé gái tử nạn... đất ở đồi Mộng Mơ có kết cấu rất yếu...\"",
                fallbackIcon: fallbackIcon
            )
        };

        bundle.LetterRecipe = CreateOrUpdateRecipeAsset(
            $"{RecipesFolder}/Recipe_Ch1_GhepLaThu.asset",
            recipeId: "Ch1_Ghep_LaThu_CoChu",
            itemA: bundle.LeftHalf,
            itemB: bundle.RightHalf,
            resultItem: bundle.FullLetter,
            resultQuantity: 1,
            consumeItemA: true,
            consumeItemB: true,
            successMessage: "Đã ghép hoàn chỉnh lá thư của cô chủ!"
        );

        return bundle;
    }

    private static ItemData CreateOrUpdateItemData(
        string assetPath,
        string itemId,
        string displayName,
        string shortDescription,
        string inspectDescription,
        Sprite fallbackIcon)
    {
        ItemData asset = AssetDatabase.LoadAssetAtPath<ItemData>(assetPath);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<ItemData>();
            AssetDatabase.CreateAsset(asset, assetPath);
        }

        // Khớp chính xác các trường trong ItemData.cs (chú ý: trường tên hiển thị là displayName)
        SerializedObject itemSO = new SerializedObject(asset);
        AssignStringValue(itemSO, "itemId", itemId);
        AssignStringValue(itemSO, "displayName", displayName);
        AssignStringValue(itemSO, "description", shortDescription);
        AssignBoolValue(itemSO, "isStackable", false);
        AssignBoolValue(itemSO, "isInspectable", true);
        AssignStringValue(itemSO, "inspectDescription", inspectDescription);

        SerializedProperty categoryProp = itemSO.FindProperty("category");
        if (categoryProp != null)
        {
            categoryProp.enumValueIndex = (int)ItemCategory.KeyItem;
        }

        // Chỉ gán icon mặc định nếu asset chưa có Sprite riêng (tránh ghi đè art thật nếu đã gán)
        SerializedProperty iconProp = itemSO.FindProperty("icon");
        if (iconProp != null && iconProp.objectReferenceValue == null && fallbackIcon != null)
        {
            iconProp.objectReferenceValue = fallbackIcon;
        }

        itemSO.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);
        return asset;
    }

    private static ItemCombineRecipe CreateOrUpdateRecipeAsset(
        string assetPath,
        string recipeId,
        ItemData itemA,
        ItemData itemB,
        ItemData resultItem,
        int resultQuantity,
        bool consumeItemA,
        bool consumeItemB,
        string successMessage)
    {
        ItemCombineRecipe recipe = AssetDatabase.LoadAssetAtPath<ItemCombineRecipe>(assetPath);
        if (recipe == null)
        {
            recipe = ScriptableObject.CreateInstance<ItemCombineRecipe>();
            AssetDatabase.CreateAsset(recipe, assetPath);
        }

        // Khớp chính xác các [SerializeField] trong ItemCombineRecipe.cs
        SerializedObject recipeSO = new SerializedObject(recipe);
        AssignStringValue(recipeSO, "recipeId", recipeId);
        AssignObjectReference(recipeSO, "itemA", itemA);
        AssignObjectReference(recipeSO, "itemB", itemB);
        AssignObjectReference(recipeSO, "resultItem", resultItem);
        AssignIntValue(recipeSO, "resultQuantity", resultQuantity);
        AssignBoolValue(recipeSO, "consumeItemA", consumeItemA);
        AssignBoolValue(recipeSO, "consumeItemB", consumeItemB);
        AssignStringValue(recipeSO, "successMessage", successMessage);
        recipeSO.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(recipe);
        return recipe;
    }

    private static bool RegisterSampleDataToInventoryManager(SampleDataBundle bundle)
    {
        InventoryManager manager = UnityEngine.Object.FindFirstObjectByType<InventoryManager>(FindObjectsInactive.Include);
        if (manager == null)
        {
            Debug.LogWarning("[InspectCombineSetupGenerator] Không tìm thấy InventoryManager trong Scene để đăng ký itemDatabase & combineRecipes.");
            return false;
        }

        SerializedObject managerSO = new SerializedObject(manager);

        // BẮT BUỘC đăng ký đủ 4 ItemData mới vào itemDatabase của InventoryManager
        // để khi LoadLocalGame() / ImportSaveData() không bỏ sót vật phẩm mới
        SerializedProperty itemDatabaseProp = managerSO.FindProperty("itemDatabase");
        if (itemDatabaseProp != null && itemDatabaseProp.isArray)
        {
            AddUniqueObjectToSerializedArray(itemDatabaseProp, bundle.LeftHalf);
            AddUniqueObjectToSerializedArray(itemDatabaseProp, bundle.RightHalf);
            AddUniqueObjectToSerializedArray(itemDatabaseProp, bundle.FullLetter);
            AddUniqueObjectToSerializedArray(itemDatabaseProp, bundle.Newspaper);
        }
        else
        {
            Debug.LogError("[InspectCombineSetupGenerator] Không tìm thấy SerializedProperty 'itemDatabase' trên InventoryManager.");
        }

        // Đăng ký công thức Recipe_Ch1_GhepLaThu vào combineRecipes (không thêm trùng)
        SerializedProperty recipesProp = managerSO.FindProperty("combineRecipes");
        if (recipesProp != null && recipesProp.isArray)
        {
            AddUniqueObjectToSerializedArray(recipesProp, bundle.LetterRecipe);
        }
        else
        {
            Debug.LogError("[InspectCombineSetupGenerator] Không tìm thấy SerializedProperty 'combineRecipes' trên InventoryManager.");
        }

        managerSO.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(manager);
        return true;
    }

    // ================= PHẦN 4: ĐẶT 3 VẬT PHẨM GREYBOX TRONG ROOM_PHONGNGU =================

    private static int CreateSamplePickupsInRoomPhongNgu(Scene activeScene, SampleDataBundle bundle, Sprite squareSprite)
    {
        List<GameObject> roomMatches = FindSceneObjectsByName(activeScene, TargetRoomName);
        Transform parentRoom = roomMatches.Count > 0 && roomMatches[0] != null ? roomMatches[0].transform : null;

        int count = 0;
        count += CreateOrUpdateWorldPickup(
            activeScene,
            parentRoom,
            objectName: "Pickup_ManhThu_Trai",
            worldObjectId: "ch1_world_manhthu_trai",
            localPos: new Vector3(-3.5f, -1.5f, 0f),
            tintColor: PickupLeftLetterColor,
            itemData: bundle.LeftHalf,
            squareSprite: squareSprite
        ) ? 1 : 0;

        count += CreateOrUpdateWorldPickup(
            activeScene,
            parentRoom,
            objectName: "Pickup_ManhThu_Phai",
            worldObjectId: "ch1_world_manhthu_phai",
            localPos: new Vector3(-1.5f, -1.5f, 0f),
            tintColor: PickupRightLetterColor,
            itemData: bundle.RightHalf,
            squareSprite: squareSprite
        ) ? 1 : 0;

        count += CreateOrUpdateWorldPickup(
            activeScene,
            parentRoom,
            objectName: "Pickup_MauBao_ChayXem",
            worldObjectId: "ch1_world_maubao_chayxem",
            localPos: new Vector3(1.5f, -1.5f, 0f),
            tintColor: PickupNewspaperColor,
            itemData: bundle.Newspaper,
            squareSprite: squareSprite
        ) ? 1 : 0;

        return count;
    }

    private static bool CreateOrUpdateWorldPickup(
        Scene activeScene,
        Transform parentRoom,
        string objectName,
        string worldObjectId,
        Vector3 localPos,
        Color tintColor,
        ItemData itemData,
        Sprite squareSprite)
    {
        List<GameObject> existing = FindSceneObjectsByName(activeScene, objectName);
        GameObject pickupGO;

        if (existing.Count > 0 && existing[0] != null)
        {
            pickupGO = existing[0];
        }
        else
        {
            pickupGO = new GameObject(objectName);
            if (parentRoom != null)
            {
                pickupGO.transform.SetParent(parentRoom, false);
                pickupGO.transform.localPosition = localPos;
            }
            else
            {
                SceneManager.MoveGameObjectToScene(pickupGO, activeScene);
                pickupGO.transform.position = localPos;
            }

            pickupGO.transform.localScale = new Vector3(0.85f, 0.85f, 1f);
            Undo.RegisterCreatedObjectUndo(pickupGO, $"Create {objectName}");
        }

        SpriteRenderer renderer = pickupGO.GetComponent<SpriteRenderer>();
        if (renderer == null) renderer = pickupGO.AddComponent<SpriteRenderer>();
        if (renderer.sprite == null) renderer.sprite = squareSprite;
        renderer.color = tintColor;
        renderer.sortingOrder = 1;

        BoxCollider2D collider = pickupGO.GetComponent<BoxCollider2D>();
        if (collider == null) collider = pickupGO.AddComponent<BoxCollider2D>();
        collider.isTrigger = true;
        collider.size = new Vector2(1.2f, 1.2f);

        WorldItemPickup pickup = pickupGO.GetComponent<WorldItemPickup>();
        if (pickup == null) pickup = pickupGO.AddComponent<WorldItemPickup>();

        // Khớp chính xác [SerializeField] của WorldItemPickup.cs;
        // Xóa rỗng requiredPuzzleId & requiredPuzzleState để vật phẩm hiện sẵn ngay từ đầu (tránh mặc định bị ẩn theo KetSat)
        SerializedObject pickupSO = new SerializedObject(pickup);
        AssignStringValue(pickupSO, "worldObjectId", worldObjectId);
        AssignObjectReference(pickupSO, "itemData", itemData);
        AssignStringValue(pickupSO, "requiredPuzzleId", string.Empty);
        AssignStringValue(pickupSO, "requiredPuzzleState", string.Empty);
        pickupSO.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(pickupGO);
        return true;
    }

    // ================= HÀM TIỆN ÍCH UI, FONT, SPRITE & SERIALIZED OBJECT =================

    private static Button CreateStyledButton(
        string buttonName,
        Transform parent,
        string labelText,
        float fontSize,
        Color bgColor,
        TMP_FontAsset fontAsset,
        int uiLayer)
    {
        GameObject btnGO = CreateUIObject(buttonName, parent, uiLayer);

        Image btnImage = btnGO.AddComponent<Image>();
        btnImage.color = bgColor;
        btnImage.raycastTarget = true;

        Outline outline = btnGO.AddComponent<Outline>();
        outline.effectColor = BorderSubtleColor;
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        Button button = btnGO.AddComponent<Button>();
        button.targetGraphic = btnImage;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.12f, 1.14f, 1.18f, 1f);
        colors.pressedColor = new Color(0.84f, 0.86f, 0.90f, 1f);
        colors.disabledColor = new Color(0.55f, 0.58f, 0.64f, 0.42f);
        button.colors = colors;

        GameObject labelGO = CreateUIObject("Label", btnGO.transform, uiLayer);
        StretchFull(labelGO.GetComponent<RectTransform>());
        CreateTMPText(labelGO, labelText, fontSize, FontStyles.Bold, TextAlignmentOptions.Center, Color.white, fontAsset);

        return button;
    }

    private static void AddMinHeight(GameObject target, float minHeight)
    {
        LayoutElement layoutElement = target.AddComponent<LayoutElement>();
        layoutElement.minHeight = minHeight;
        layoutElement.preferredHeight = minHeight;
    }

    private static void AddUniqueObjectToSerializedArray(SerializedProperty arrayProp, UnityEngine.Object item)
    {
        if (arrayProp == null || !arrayProp.isArray || item == null)
        {
            return;
        }

        for (int i = 0; i < arrayProp.arraySize; i++)
        {
            if (arrayProp.GetArrayElementAtIndex(i).objectReferenceValue == item)
            {
                return;
            }
        }

        int newIndex = arrayProp.arraySize;
        arrayProp.InsertArrayElementAtIndex(newIndex);
        arrayProp.GetArrayElementAtIndex(newIndex).objectReferenceValue = item;
    }

    private static TMP_FontAsset FindProjectTMPFont(Scene activeScene)
    {
        string[] priorityCanvases = { "DialogueCanvas", "InventoryCanvas", "KeypadCanvas", "PauseCanvas" };
        for (int i = 0; i < priorityCanvases.Length; i++)
        {
            List<GameObject> canvases = FindSceneObjectsByName(activeScene, priorityCanvases[i]);
            for (int j = 0; j < canvases.Count; j++)
            {
                TMP_FontAsset font = ExtractFirstFont(canvases[j]);
                if (font != null) return font;
            }
        }

        GameObject[] roots = activeScene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            TMP_FontAsset font = ExtractFirstFont(roots[i]);
            if (font != null) return font;
        }

        string[] guids = AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { "Assets" });
        TMP_FontAsset fallbackAsset = null;
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            TMP_FontAsset loaded = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (loaded == null) continue;

            if (fallbackAsset == null) fallbackAsset = loaded;
            if (path.IndexOf("LiberationSans", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return loaded;
            }
        }

        return fallbackAsset != null ? fallbackAsset : TMP_Settings.defaultFontAsset;
    }

    private static TMP_FontAsset ExtractFirstFont(GameObject root)
    {
        if (root == null) return null;

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

    private static Sprite LoadBuiltinSquareSprite()
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(BuiltinSquareSpriteV2Path);
        if (sprite != null) return sprite;

        sprite = AssetDatabase.LoadAssetAtPath<Sprite>(BuiltinSquareSpriteV1Path);
        if (sprite != null) return sprite;

        return AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
    }

    private static void DestroyDirectChildIfExists(Transform parent, string childName)
    {
        if (parent == null) return;

        Transform existing = parent.Find(childName);
        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing.gameObject);
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
        tmp.raycastTarget = false;
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

    private static List<GameObject> FindSceneObjectsByName(Scene scene, string targetName)
    {
        List<GameObject> matches = new List<GameObject>();
        GameObject[] roots = scene.GetRootGameObjects();

        for (int i = 0; i < roots.Length; i++)
        {
            Transform[] children = roots[i].GetComponentsInChildren<Transform>(true);
            for (int j = 0; j < children.Length; j++)
            {
                if (string.Equals(children[j].name, targetName, StringComparison.Ordinal))
                {
                    matches.Add(children[j].gameObject);
                }
            }
        }

        return matches;
    }

    private static void AssignObjectReference(SerializedObject serializedObject, string propertyName, UnityEngine.Object value)
    {
        SerializedProperty prop = serializedObject.FindProperty(propertyName);
        if (prop == null)
        {
            Debug.LogError($"[InspectCombineSetupGenerator] Không tìm thấy SerializedProperty '{propertyName}' trên '{serializedObject.targetObject.GetType().Name}'.");
            return;
        }

        prop.objectReferenceValue = value;
    }

    private static void AssignStringValue(SerializedObject serializedObject, string propertyName, string value)
    {
        SerializedProperty prop = serializedObject.FindProperty(propertyName);
        if (prop == null)
        {
            Debug.LogError($"[InspectCombineSetupGenerator] Không tìm thấy SerializedProperty '{propertyName}' trên '{serializedObject.targetObject.GetType().Name}'.");
            return;
        }

        prop.stringValue = value;
    }

    private static void AssignIntValue(SerializedObject serializedObject, string propertyName, int value)
    {
        SerializedProperty prop = serializedObject.FindProperty(propertyName);
        if (prop == null)
        {
            Debug.LogError($"[InspectCombineSetupGenerator] Không tìm thấy SerializedProperty '{propertyName}' trên '{serializedObject.targetObject.GetType().Name}'.");
            return;
        }

        prop.intValue = value;
    }

    private static void AssignBoolValue(SerializedObject serializedObject, string propertyName, bool value)
    {
        SerializedProperty prop = serializedObject.FindProperty(propertyName);
        if (prop == null)
        {
            Debug.LogError($"[InspectCombineSetupGenerator] Không tìm thấy SerializedProperty '{propertyName}' trên '{serializedObject.targetObject.GetType().Name}'.");
            return;
        }

        prop.boolValue = value;
    }

    private static int GetUILayer()
    {
        int layer = LayerMask.NameToLayer("UI");
        return layer >= 0 ? layer : 5;
    }
}
