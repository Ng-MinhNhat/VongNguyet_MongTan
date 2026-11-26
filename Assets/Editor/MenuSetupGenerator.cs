using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

/// <summary>
/// Công cụ Unity Editor tách 2 Menu độc lập:
/// 1) Sinh PauseCanvas (uGUI + TextMeshPro) vào Scene gameplay đang mở.
/// 2) Sinh Scene MainMenu.unity hoàn chỉnh (Camera, EventSystem chuẩn Input System, MainMenuCanvas)
///    và tự động sắp xếp Build Settings (Index 0 = MainMenu, Index 1 = SampleScene).
/// </summary>
public static class MenuSetupGenerator
{
    private const string PauseCanvasName = "PauseCanvas";
    private const string MainMenuCanvasName = "MainMenuCanvas";
    private const string MainMenuSceneName = "MainMenu";
    private const string DefaultGameplaySceneName = "SampleScene";
    private const string ScenesFolderPath = "Assets/Scenes";
    private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
    private const string PreferredSampleScenePath = "Assets/Scenes/SampleScene.unity";

    // Bảng màu tối ánh trăng thống nhất cho giao diện uGUI
    private static readonly Color MainMenuBgColor = new Color(0.051f, 0.067f, 0.090f, 1.00f);       // #0D1117
    private static readonly Color PauseDimColor = new Color(0.00f, 0.00f, 0.00f, 0.78f);
    private static readonly Color ConfirmDimColor = new Color(0.00f, 0.00f, 0.00f, 0.80f);
    private static readonly Color WindowBgColor = new Color(0.09f, 0.11f, 0.17f, 0.97f);
    private static readonly Color HudButtonColor = new Color(0.12f, 0.16f, 0.25f, 0.94f);
    private static readonly Color PrimaryButtonColor = new Color(0.18f, 0.44f, 0.68f, 1.00f);
    private static readonly Color SecondaryButtonColor = new Color(0.20f, 0.24f, 0.34f, 1.00f);
    private static readonly Color SuccessButtonColor = new Color(0.18f, 0.56f, 0.34f, 1.00f);
    private static readonly Color DangerButtonColor = new Color(0.60f, 0.20f, 0.24f, 1.00f);
    private static readonly Color BorderSubtleColor = new Color(0.55f, 0.65f, 0.84f, 0.45f);
    private static readonly Color TitleTextColor = new Color(0.96f, 0.94f, 0.88f, 1.00f);
    private static readonly Color FeedbackSuccessColor = new Color(0.48f, 0.94f, 0.58f, 1.00f);
    private static readonly Color VersionTextColor = new Color(0.72f, 0.76f, 0.84f, 0.85f);

    // ================= MENU 1: GENERATE PAUSE MENU IN CURRENT SCENE =================

    [MenuItem("Vong Nguyet/UI/1. Generate Pause Menu in Current Scene")]
    public static void GeneratePauseMenuInCurrentScene()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid())
        {
            EditorUtility.DisplayDialog("Lỗi Scene", "Không tìm thấy Scene đang mở hợp lệ.", "Đóng");
            return;
        }

        // Kiểm tra nếu đã có PauseCanvas trong Scene hiện tại -> hỏi xác nhận trước khi xóa bản cũ
        List<GameObject> existingCanvases = FindSceneObjectsByName(activeScene, PauseCanvasName);
        if (existingCanvases.Count > 0)
        {
            bool overwrite = EditorUtility.DisplayDialog(
                "Tạo lại PauseCanvas?",
                $"Trong Scene '{activeScene.name}' đã tồn tại '{PauseCanvasName}'.\n" +
                "Bạn có muốn xóa bản cũ và tạo mới lại giao diện Pause Menu không?",
                "Tạo lại / Cập nhật",
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

        // Tự động lấy font tiếng Việt đang dùng trong Scene (hoặc AssetDatabase -> TMP default)
        TMP_FontAsset projectFont = FindProjectTMPFont(activeScene);

        GameObject pauseCanvas = BuildPauseCanvasInScene(activeScene, projectFont);
        EditorSceneManager.MarkSceneDirty(activeScene);
        Selection.activeGameObject = pauseCanvas;

        Debug.Log(
            $"[MenuSetupGenerator] Đã sinh thành công '{PauseCanvasName}' trong Scene '{activeScene.name}'. " +
            $"Font sử dụng: {(projectFont != null ? projectFont.name : "TMP Default null")}."
        );
    }

    private static GameObject BuildPauseCanvasInScene(Scene activeScene, TMP_FontAsset fontAsset)
    {
        int uiLayer = GetUILayer();

        // Root PauseCanvas luôn Active ở cấp Root Scene để Awake() khởi tạo PauseMenuController.Instance
        GameObject canvasGO = CreateUIObject(PauseCanvasName, null, uiLayer);
        SceneManager.MoveGameObjectToScene(canvasGO, activeScene);
        Undo.RegisterCreatedObjectUndo(canvasGO, "Generate PauseCanvas");

        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 80; // Nằm trên KeypadCanvas (60) và dưới TransitionCanvas (100)

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();
        PauseMenuController controller = canvasGO.AddComponent<PauseMenuController>();

        // Nút HUD Pause góc trên bên trái (tránh đè lên nút "Túi đồ" ở góc trên bên phải)
        Button hudPauseButton = CreateStyledButton("HudPauseButton", canvasGO.transform, "|| MENU", 20f, HudButtonColor, fontAsset, uiLayer);
        RectTransform hudBtnRect = hudPauseButton.GetComponent<RectTransform>();
        hudBtnRect.anchorMin = new Vector2(0f, 1f);
        hudBtnRect.anchorMax = new Vector2(0f, 1f);
        hudBtnRect.pivot = new Vector2(0f, 1f);
        hudBtnRect.anchoredPosition = new Vector2(30f, -30f);
        hudBtnRect.sizeDelta = new Vector2(120f, 56f);

        // PausePanelRoot phủ toàn màn hình (0..1), bật raycastTarget = true để chặn click xuyên, mặc định tắt
        GameObject pausePanelRoot = CreateUIObject("PausePanelRoot", canvasGO.transform, uiLayer);
        StretchFull(pausePanelRoot.GetComponent<RectTransform>());
        Image dimImage = pausePanelRoot.AddComponent<Image>();
        dimImage.color = PauseDimColor;
        dimImage.raycastTarget = true;

        // Khung PauseWindow căn giữa (460 x 520, VerticalLayoutGroup)
        GameObject pauseWindow = CreateUIObject("PauseWindow", pausePanelRoot.transform, uiLayer);
        RectTransform windowRect = pauseWindow.GetComponent<RectTransform>();
        windowRect.anchorMin = new Vector2(0.5f, 0.5f);
        windowRect.anchorMax = new Vector2(0.5f, 0.5f);
        windowRect.pivot = new Vector2(0.5f, 0.5f);
        windowRect.anchoredPosition = Vector2.zero;
        windowRect.sizeDelta = new Vector2(460f, 520f);

        Image windowBg = pauseWindow.AddComponent<Image>();
        windowBg.color = WindowBgColor;
        windowBg.raycastTarget = false;

        Outline windowOutline = pauseWindow.AddComponent<Outline>();
        windowOutline.effectColor = BorderSubtleColor;
        windowOutline.effectDistance = new Vector2(2f, -2f);

        VerticalLayoutGroup windowLayout = pauseWindow.AddComponent<VerticalLayoutGroup>();
        windowLayout.padding = new RectOffset(40, 40, 38, 30);
        windowLayout.spacing = 20f;
        windowLayout.childAlignment = TextAnchor.UpperCenter;
        windowLayout.childControlWidth = true;
        windowLayout.childControlHeight = true;
        windowLayout.childForceExpandWidth = true;
        windowLayout.childForceExpandHeight = false;

        // TitleText: "TẠM DỪNG"
        GameObject titleGO = CreateUIObject("TitleText", pauseWindow.transform, uiLayer);
        AddPreferredHeight(titleGO, 64f);
        CreateTMPText(titleGO, "TẠM DỪNG", 42f, FontStyles.Bold, TextAlignmentOptions.Center, TitleTextColor, fontAsset);

        // Các nút chức năng chính trong PauseWindow
        Button resumeButton = CreateStyledButton("ResumeButton", pauseWindow.transform, "TIẾP TỤC", 26f, SuccessButtonColor, fontAsset, uiLayer);
        AddPreferredHeight(resumeButton.gameObject, 68f);

        Button saveButton = CreateStyledButton("SaveButton", pauseWindow.transform, "LƯU GAME", 26f, PrimaryButtonColor, fontAsset, uiLayer);
        AddPreferredHeight(saveButton.gameObject, 68f);

        Button mainMenuButton = CreateStyledButton("MainMenuButton", pauseWindow.transform, "VỀ MENU CHÍNH", 26f, DangerButtonColor, fontAsset, uiLayer);
        AddPreferredHeight(mainMenuButton.gameObject, 68f);

        // FeedbackText: màu xanh lá nhạt, nội dung rỗng mặc định
        GameObject feedbackGO = CreateUIObject("FeedbackText", pauseWindow.transform, uiLayer);
        AddPreferredHeight(feedbackGO, 36f);
        TextMeshProUGUI feedbackText = CreateTMPText(
            feedbackGO,
            string.Empty,
            22f,
            FontStyles.Bold,
            TextAlignmentOptions.Center,
            FeedbackSuccessColor,
            fontAsset
        );

        pausePanelRoot.SetActive(false);

        // Ánh xạ chính xác 100% tên biến [SerializeField] của PauseMenuController.cs
        SerializedObject controllerSO = new SerializedObject(controller);
        AssignObjectReference(controllerSO, "pausePanelRoot", pausePanelRoot);
        AssignObjectReference(controllerSO, "hudPauseButton", hudPauseButton);
        AssignObjectReference(controllerSO, "resumeButton", resumeButton);
        AssignObjectReference(controllerSO, "saveButton", saveButton);
        AssignObjectReference(controllerSO, "mainMenuButton", mainMenuButton);
        AssignObjectReference(controllerSO, "feedbackText", feedbackText);
        AssignStringValue(controllerSO, "mainMenuSceneName", MainMenuSceneName);
        controllerSO.ApplyModifiedPropertiesWithoutUndo();

        return canvasGO;
    }

    // ================= MENU 2: GENERATE MAINMENU SCENE & BUILD SETTINGS =================

    [MenuItem("Vong Nguyet/UI/2. Generate MainMenu Scene & Build Settings")]
    public static void GenerateMainMenuSceneAndBuildSettings()
    {
        // Bảo vệ thay đổi chưa lưu ở Scene hiện tại trước khi chuyển sang tạo Scene MainMenu mới
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        // Tìm sẵn TMP_FontAsset tiếng Việt từ Scene đang mở trước khi tạo Scene mới trống
        TMP_FontAsset projectFont = FindProjectTMPFont(SceneManager.GetActiveScene());

        // Tìm đường dẫn chính xác của SampleScene.unity trong Assets/
        string sampleScenePath = FindSampleScenePath();
        string gameplaySceneName = !string.IsNullOrEmpty(sampleScenePath)
            ? System.IO.Path.GetFileNameWithoutExtension(sampleScenePath)
            : DefaultGameplaySceneName;

        // Tạo Scene mới hoàn toàn trống
        Scene newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 1. Tạo Main Camera nền xanh đêm tối #0D1117
        GameObject cameraGO = new GameObject("Main Camera");
        cameraGO.tag = "MainCamera";
        cameraGO.transform.position = new Vector3(0f, 0f, -10f);
        Camera cam = cameraGO.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5.4f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = MainMenuBgColor;
        cameraGO.AddComponent<AudioListener>();

        // 2. Tạo EventSystem khớp chuẩn Input System của dự án (Unity 6 dùng InputSystemUIInputModule)
        GameObject eventSystemGO = new GameObject("EventSystem");
        eventSystemGO.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
        // Dùng InputSystemUIInputModule khớp với SampleScene để nút uGUI bấm được trên Unity 6 Input System
        eventSystemGO.AddComponent<InputSystemUIInputModule>();
#else
        eventSystemGO.AddComponent<StandaloneInputModule>();
#endif

        // 3. Tạo MainMenuCanvas + MainMenuController
        GameObject mainMenuCanvas = BuildMainMenuCanvas(projectFont, gameplaySceneName);

        // 4. Lưu Scene vào Assets/Scenes/MainMenu.unity
        EnsureFolderExists(ScenesFolderPath);
        bool saved = EditorSceneManager.SaveScene(newScene, MainMenuScenePath);
        if (!saved)
        {
            Debug.LogError($"[MenuSetupGenerator] Không thể lưu Scene tại '{MainMenuScenePath}'.");
            return;
        }

        AssetDatabase.Refresh();

        // 5. Cập nhật Build Settings: Index 0 = MainMenu.unity, Index 1 = SampleScene.unity (không trùng lặp)
        UpdateBuildSettingsOrder(MainMenuScenePath, sampleScenePath);

        Selection.activeGameObject = mainMenuCanvas;
        LogBuildSettingsSummary(projectFont);
    }

    private static GameObject BuildMainMenuCanvas(TMP_FontAsset fontAsset, string gameplaySceneName)
    {
        int uiLayer = GetUILayer();

        GameObject canvasGO = CreateUIObject(MainMenuCanvasName, null, uiLayer);
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 0;

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();
        MainMenuController controller = canvasGO.AddComponent<MainMenuController>();

        // BackgroundPanel phủ toàn màn hình (trang trí, tắt raycastTarget)
        GameObject bgPanel = CreateUIObject("BackgroundPanel", canvasGO.transform, uiLayer);
        StretchFull(bgPanel.GetComponent<RectTransform>());
        Image bgImg = bgPanel.AddComponent<Image>();
        bgImg.color = MainMenuBgColor;
        bgImg.raycastTarget = false;

        // GameTitleText căn giữa phía trên
        GameObject titleGO = CreateUIObject("GameTitleText", canvasGO.transform, uiLayer);
        RectTransform titleRect = titleGO.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.5f, 1f);
        titleRect.anchorMax = new Vector2(0.5f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -170f);
        titleRect.sizeDelta = new Vector2(1500f, 110f);
        CreateTMPText(titleGO, "VỌNG NGUYỆT: MỘNG TÀN", 64f, FontStyles.Bold, TextAlignmentOptions.Center, TitleTextColor, fontAsset);

        // ButtonGroup căn giữa rộng 360 dùng VerticalLayoutGroup
        GameObject buttonGroup = CreateUIObject("ButtonGroup", canvasGO.transform, uiLayer);
        RectTransform groupRect = buttonGroup.GetComponent<RectTransform>();
        groupRect.anchorMin = new Vector2(0.5f, 0.5f);
        groupRect.anchorMax = new Vector2(0.5f, 0.5f);
        groupRect.pivot = new Vector2(0.5f, 0.5f);
        groupRect.anchoredPosition = new Vector2(0f, -60f);
        groupRect.sizeDelta = new Vector2(360f, 268f);

        VerticalLayoutGroup groupLayout = buttonGroup.AddComponent<VerticalLayoutGroup>();
        groupLayout.spacing = 22f;
        groupLayout.childAlignment = TextAnchor.MiddleCenter;
        groupLayout.childControlWidth = true;
        groupLayout.childControlHeight = true;
        groupLayout.childForceExpandWidth = true;
        groupLayout.childForceExpandHeight = false;

        Button continueButton = CreateStyledButton("ContinueButton", buttonGroup.transform, "TIẾP TỤC", 28f, PrimaryButtonColor, fontAsset, uiLayer);
        AddPreferredHeight(continueButton.gameObject, 72f);

        Button newGameButton = CreateStyledButton("NewGameButton", buttonGroup.transform, "CHƠI MỚI", 28f, SecondaryButtonColor, fontAsset, uiLayer);
        AddPreferredHeight(newGameButton.gameObject, 72f);

        Button quitButton = CreateStyledButton("QuitButton", buttonGroup.transform, "THOÁT GAME", 28f, DangerButtonColor, fontAsset, uiLayer);
        AddPreferredHeight(quitButton.gameObject, 72f);

        // VersionText góc dưới bên phải
        GameObject versionGO = CreateUIObject("VersionText", canvasGO.transform, uiLayer);
        RectTransform versionRect = versionGO.GetComponent<RectTransform>();
        versionRect.anchorMin = new Vector2(1f, 0f);
        versionRect.anchorMax = new Vector2(1f, 0f);
        versionRect.pivot = new Vector2(1f, 0f);
        versionRect.anchoredPosition = new Vector2(-28f, 22f);
        versionRect.sizeDelta = new Vector2(280f, 34f);
        TextMeshProUGUI versionText = CreateTMPText(
            versionGO,
            $"v{Application.version}",
            20f,
            FontStyles.Normal,
            TextAlignmentOptions.BottomRight,
            VersionTextColor,
            fontAsset
        );

        // ConfirmNewGamePanel phủ toàn màn hình (mặc định tắt)
        GameObject confirmPanel = CreateUIObject("ConfirmNewGamePanel", canvasGO.transform, uiLayer);
        StretchFull(confirmPanel.GetComponent<RectTransform>());
        Image confirmDim = confirmPanel.AddComponent<Image>();
        confirmDim.color = ConfirmDimColor;
        confirmDim.raycastTarget = true;

        GameObject confirmDialog = CreateUIObject("ConfirmDialog", confirmPanel.transform, uiLayer);
        RectTransform dialogRect = confirmDialog.GetComponent<RectTransform>();
        dialogRect.anchorMin = new Vector2(0.5f, 0.5f);
        dialogRect.anchorMax = new Vector2(0.5f, 0.5f);
        dialogRect.pivot = new Vector2(0.5f, 0.5f);
        dialogRect.anchoredPosition = Vector2.zero;
        dialogRect.sizeDelta = new Vector2(660f, 310f);

        Image dialogBg = confirmDialog.AddComponent<Image>();
        dialogBg.color = WindowBgColor;
        dialogBg.raycastTarget = false;

        Outline dialogOutline = confirmDialog.AddComponent<Outline>();
        dialogOutline.effectColor = BorderSubtleColor;
        dialogOutline.effectDistance = new Vector2(2f, -2f);

        GameObject confirmMsgGO = CreateUIObject("ConfirmMessageText", confirmDialog.transform, uiLayer);
        RectTransform msgRect = confirmMsgGO.GetComponent<RectTransform>();
        msgRect.anchorMin = Vector2.zero;
        msgRect.anchorMax = Vector2.one;
        msgRect.offsetMin = new Vector2(36f, 120f);
        msgRect.offsetMax = new Vector2(-36f, -32f);
        TextMeshProUGUI confirmMsgText = CreateTMPText(
            confirmMsgGO,
            "Bắt đầu chơi mới sẽ xóa tiến độ đã lưu.\nBạn có chắc chắn muốn tiếp tục?",
            26f,
            FontStyles.Normal,
            TextAlignmentOptions.Center,
            TitleTextColor,
            fontAsset
        );
        confirmMsgText.textWrappingMode = TextWrappingModes.Normal;

        GameObject confirmBtnRow = CreateUIObject("ConfirmButtonRow", confirmDialog.transform, uiLayer);
        RectTransform rowRect = confirmBtnRow.GetComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0.5f, 0f);
        rowRect.anchorMax = new Vector2(0.5f, 0f);
        rowRect.pivot = new Vector2(0.5f, 0f);
        rowRect.anchoredPosition = new Vector2(0f, 32f);
        rowRect.sizeDelta = new Vector2(460f, 66f);

        HorizontalLayoutGroup rowLayout = confirmBtnRow.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 24f;
        rowLayout.childAlignment = TextAnchor.MiddleCenter;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = true;

        Button confirmYesButton = CreateStyledButton("ConfirmYesButton", confirmBtnRow.transform, "ĐỒNG Ý", 24f, DangerButtonColor, fontAsset, uiLayer);
        Button confirmNoButton = CreateStyledButton("ConfirmNoButton", confirmBtnRow.transform, "HỦY", 24f, SecondaryButtonColor, fontAsset, uiLayer);

        confirmPanel.SetActive(false);

        // FadePanel đặt làm con CUỐI CÙNG của MainMenuCanvas để phủ đè lên mọi nút khi chuyển Scene
        GameObject fadePanel = CreateUIObject("FadePanel", canvasGO.transform, uiLayer);
        StretchFull(fadePanel.GetComponent<RectTransform>());
        Image fadeImage = fadePanel.AddComponent<Image>();
        fadeImage.color = Color.black;
        fadeImage.raycastTarget = true;

        CanvasGroup fadeCanvasGroup = fadePanel.AddComponent<CanvasGroup>();
        fadeCanvasGroup.alpha = 0f;
        fadeCanvasGroup.blocksRaycasts = false;
        fadeCanvasGroup.interactable = false;

        // Ánh xạ chính xác 100% tên biến [SerializeField] của MainMenuController.cs
        SerializedObject controllerSO = new SerializedObject(controller);
        AssignStringValue(controllerSO, "gameplaySceneName", gameplaySceneName);
        AssignObjectReference(controllerSO, "continueButton", continueButton);
        AssignObjectReference(controllerSO, "newGameButton", newGameButton);
        AssignObjectReference(controllerSO, "quitButton", quitButton);
        AssignBoolValue(controllerSO, "hideContinueWhenNoSave", false);
        AssignObjectReference(controllerSO, "confirmNewGamePanel", confirmPanel);
        AssignObjectReference(controllerSO, "confirmYesButton", confirmYesButton);
        AssignObjectReference(controllerSO, "confirmNoButton", confirmNoButton);
        AssignObjectReference(controllerSO, "fadeCanvasGroup", fadeCanvasGroup);
        AssignObjectReference(controllerSO, "versionText", versionText);
        controllerSO.ApplyModifiedPropertiesWithoutUndo();

        return canvasGO;
    }

    // ================= CẬP NHẬT BUILD SETTINGS (INDEX 0 = MAINMENU, INDEX 1 = SAMPLESCENE) =================

    private static void UpdateBuildSettingsOrder(string mainMenuPath, string sampleScenePath)
    {
        // Giữ lại các Scene khác đã có trong Build Settings nhưng đặt cứng MainMenu ở [0] và SampleScene ở [1]
        List<EditorBuildSettingsScene> orderedScenes = new List<EditorBuildSettingsScene>();
        orderedScenes.Add(new EditorBuildSettingsScene(mainMenuPath, true));

        if (!string.IsNullOrEmpty(sampleScenePath))
        {
            orderedScenes.Add(new EditorBuildSettingsScene(sampleScenePath, true));
        }
        else
        {
            Debug.LogWarning("[MenuSetupGenerator] Không tìm thấy SampleScene.unity trong thư mục Assets/. Vui lòng thêm Scene gameplay vào Build Settings thủ công.");
        }

        EditorBuildSettingsScene[] existingScenes = EditorBuildSettings.scenes;
        if (existingScenes != null)
        {
            for (int i = 0; i < existingScenes.Length; i++)
            {
                EditorBuildSettingsScene existing = existingScenes[i];
                if (existing == null || string.IsNullOrEmpty(existing.path))
                {
                    continue;
                }

                string normalized = existing.path.Replace('\\', '/');
                if (string.Equals(normalized, mainMenuPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(sampleScenePath)
                    && string.Equals(normalized, sampleScenePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                orderedScenes.Add(existing);
            }
        }

        EditorBuildSettings.scenes = orderedScenes.ToArray();
    }

    private static string FindSampleScenePath()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(PreferredSampleScenePath) != null)
        {
            return PreferredSampleScenePath;
        }

        string[] guids = AssetDatabase.FindAssets("SampleScene t:Scene", new[] { "Assets" });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]).Replace('\\', '/');
            if (path.EndsWith("/SampleScene.unity", StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, "Assets/SampleScene.unity", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }
        }

        return string.Empty;
    }

    private static void LogBuildSettingsSummary(TMP_FontAsset fontAsset)
    {
        EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine($"[MenuSetupGenerator] Đã sinh thành công '{MainMenuScenePath}' và cập nhật Build Settings.");
        sb.AppendLine($"Font sử dụng: {(fontAsset != null ? fontAsset.name : "TMP Default null")}");
        sb.AppendLine("Danh sách Scene trong Build Settings:");

        for (int i = 0; i < scenes.Length; i++)
        {
            sb.AppendLine($"  [{i}] {scenes[i].path} (Enabled: {scenes[i].enabled})");
        }

        Debug.Log(sb.ToString());
    }

    // ================= HÀM TIỆN ÍCH UI, FONT & SERIALIZED OBJECT =================

    private static TMP_FontAsset FindProjectTMPFont(Scene activeScene)
    {
        // Ưu tiên 1: Quét trực tiếp các Canvas tiếng Việt đã có trong Scene hiện tại
        if (activeScene.IsValid())
        {
            string[] priorityCanvases = { "DialogueCanvas", "InventoryCanvas", "KeypadCanvas" };
            for (int i = 0; i < priorityCanvases.Length; i++)
            {
                List<GameObject> matches = FindSceneObjectsByName(activeScene, priorityCanvases[i]);
                for (int j = 0; j < matches.Count; j++)
                {
                    TMP_FontAsset font = ExtractFirstFont(matches[j]);
                    if (font != null) return font;
                }
            }

            GameObject[] roots = activeScene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                TMP_FontAsset font = ExtractFirstFont(roots[i]);
                if (font != null) return font;
            }
        }

        // Ưu tiên 2: Quét toàn bộ TMP_FontAsset trong Assets (ưu tiên font tùy chỉnh tiếng Việt ngoài LiberationSans)
        string[] guids = AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { "Assets" });
        TMP_FontAsset fallbackAssetFont = null;
        for (int i = 0; i < guids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
            TMP_FontAsset loaded = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (loaded == null) continue;

            if (fallbackAssetFont == null)
            {
                fallbackAssetFont = loaded;
            }

            if (assetPath.IndexOf("LiberationSans", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return loaded;
            }
        }

        if (fallbackAssetFont != null)
        {
            return fallbackAssetFont;
        }

        // Dự phòng cuối cùng: font mặc định của TextMeshPro
        return TMP_Settings.defaultFontAsset;
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

        Image btnImg = btnGO.AddComponent<Image>();
        btnImg.color = bgColor;
        btnImg.raycastTarget = true;

        Outline outline = btnGO.AddComponent<Outline>();
        outline.effectColor = BorderSubtleColor;
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        Button button = btnGO.AddComponent<Button>();
        button.targetGraphic = btnImg;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.12f, 1.14f, 1.18f, 1f);
        colors.pressedColor = new Color(0.84f, 0.86f, 0.90f, 1f);
        colors.selectedColor = Color.white;
        colors.disabledColor = new Color(0.55f, 0.58f, 0.65f, 0.42f); // Làm mờ rõ ràng khi interactable = false (VD: chưa có Save)
        button.colors = colors;

        GameObject labelGO = CreateUIObject("Label", btnGO.transform, uiLayer);
        StretchFull(labelGO.GetComponent<RectTransform>());
        CreateTMPText(labelGO, labelText, fontSize, FontStyles.Bold, TextAlignmentOptions.Center, Color.white, fontAsset);

        return button;
    }

    private static void AddPreferredHeight(GameObject target, float height)
    {
        LayoutElement layoutElement = target.AddComponent<LayoutElement>();
        layoutElement.preferredHeight = height;
        layoutElement.minHeight = height;
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
        tmp.raycastTarget = false; // Tắt Raycast Target trên chữ trang trí để tối ưu uGUI
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
            Debug.LogError($"[MenuSetupGenerator] Không tìm thấy SerializedProperty '{propertyName}' trên '{serializedObject.targetObject.GetType().Name}'.");
            return;
        }

        prop.objectReferenceValue = value;
    }

    private static void AssignStringValue(SerializedObject serializedObject, string propertyName, string value)
    {
        SerializedProperty prop = serializedObject.FindProperty(propertyName);
        if (prop == null)
        {
            Debug.LogError($"[MenuSetupGenerator] Không tìm thấy SerializedProperty '{propertyName}' trên '{serializedObject.targetObject.GetType().Name}'.");
            return;
        }

        prop.stringValue = value;
    }

    private static void AssignBoolValue(SerializedObject serializedObject, string propertyName, bool value)
    {
        SerializedProperty prop = serializedObject.FindProperty(propertyName);
        if (prop == null)
        {
            Debug.LogError($"[MenuSetupGenerator] Không tìm thấy SerializedProperty '{propertyName}' trên '{serializedObject.targetObject.GetType().Name}'.");
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
