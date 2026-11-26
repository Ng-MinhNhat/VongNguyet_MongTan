using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Công cụ Unity Editor tự động sinh KeypadCanvas (uGUI + TextMeshPro)
/// và ổ khóa mẫu Greybox TuHoSo_Keypad bên trong Room_HanhLang.
/// </summary>
public static class KeypadSetupGenerator
{
    private const string KeypadCanvasName = "KeypadCanvas";
    private const string SampleObjectName = "TuHoSo_Keypad";
    private const string TargetRoomName = "Room_HanhLang";

    // Đường dẫn Sprite vuông chuẩn Unity 6 (giống RoomTransitionSetupGenerator) để tránh Sprite null
    private const string BuiltinSquareSpriteV2Path = "Packages/com.unity.2d.sprite/Editor/ObjectMenuCreation/DefaultAssets/Textures/v2/Square.png";
    private const string BuiltinSquareSpriteV1Path = "Packages/com.unity.2d.sprite/Editor/ObjectMenuCreation/DefaultAssets/Textures/Square.png";

    // Bảng màu tối uGUI cho giao diện ổ khóa mật mã
    private static readonly Color DimOverlayColor = new Color(0f, 0f, 0f, 0.75f);
    private static readonly Color WindowBgColor = new Color(0.09f, 0.11f, 0.16f, 0.98f);
    private static readonly Color DisplayBoxColor = new Color(0.04f, 0.06f, 0.10f, 1.00f);
    private static readonly Color DigitButtonColor = new Color(0.18f, 0.22f, 0.30f, 1.00f);
    private static readonly Color UtilityButtonColor = new Color(0.28f, 0.24f, 0.22f, 1.00f);
    private static readonly Color SubmitButtonColor = new Color(0.18f, 0.58f, 0.34f, 1.00f);
    private static readonly Color CloseButtonColor = new Color(0.58f, 0.22f, 0.24f, 1.00f);
    private static readonly Color BorderSubtleColor = new Color(0.52f, 0.62f, 0.80f, 0.45f);
    private static readonly Color PrimaryTextColor = new Color(0.95f, 0.94f, 0.90f, 1.00f);
    private static readonly Color SampleKeypadSpriteColor = new Color(0.22f, 0.58f, 0.96f, 0.95f); // Xanh dương sáng phân biệt với cửa cam

    [MenuItem("Vong Nguyet/Puzzle/Generate Keypad UI & Greybox Sample")]
    public static void GenerateKeypadUIAndSample()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid())
        {
            EditorUtility.DisplayDialog("Lỗi Scene", "Không tìm thấy Scene đang mở hợp lệ.", "Đóng");
            return;
        }

        // 1. Kiểm tra nếu đã có KeypadCanvas hoặc TuHoSo_Keypad trong Scene -> hỏi xác nhận trước khi ghi đè
        List<GameObject> existingCanvases = FindSceneObjectsByName(activeScene, KeypadCanvasName);
        List<GameObject> existingSamples = FindSceneObjectsByName(activeScene, SampleObjectName);

        if (existingCanvases.Count > 0 || existingSamples.Count > 0)
        {
            bool overwrite = EditorUtility.DisplayDialog(
                "Tạo lại Keypad UI & Mẫu Greybox?",
                $"Trong Scene '{activeScene.name}' đã tồn tại '{KeypadCanvasName}' hoặc '{SampleObjectName}'.\n" +
                "Bạn có muốn xóa bản cũ và tạo mới lại toàn bộ giao diện Keypad cùng ổ khóa mẫu không?",
                "Tạo lại / Cập nhật",
                "Hủy"
            );

            if (!overwrite)
            {
                return;
            }

            DestroyObjectListWithUndo(existingCanvases);
            DestroyObjectListWithUndo(existingSamples);
        }

        // 2. Lấy TMP_FontAsset hỗ trợ tiếng Việt đang dùng trong Scene (DialogueCanvas / InventoryCanvas)
        TMP_FontAsset projectFont = FindProjectTMPFont(activeScene);

        // 3. Sinh KeypadCanvas ở cấp Root Scene (luôn Active để Awake() khởi tạo KeypadUIController.Instance)
        GameObject keypadCanvas = BuildKeypadCanvas(activeScene, projectFont);

        // 4. Sinh ổ khóa mẫu Greybox TuHoSo_Keypad trong Room_HanhLang (hoặc Root nếu chưa có phòng)
        GameObject sampleKeypad = BuildSampleKeypadInRoom(activeScene);

        // 5. Đánh dấu Scene đã thay đổi để người dùng bấm Ctrl+S lưu chuẩn GUID
        EditorSceneManager.MarkSceneDirty(activeScene);
        Selection.activeGameObject = keypadCanvas;

        Debug.Log(
            $"[KeypadSetupGenerator] Đã sinh thành công '{KeypadCanvasName}' và '{sampleKeypad.name}' " +
            $"(Parent: {(sampleKeypad.transform.parent != null ? sampleKeypad.transform.parent.name : "Scene Root")}) " +
            $"trong Scene '{activeScene.name}'. Font sử dụng: {(projectFont != null ? projectFont.name : "TMP Default null")}."
        );
    }

    // ================= 1. TẠO KEYPAD CANVAS (uGUI + TEXTMESHPRO) =================

    private static GameObject BuildKeypadCanvas(Scene activeScene, TMP_FontAsset fontAsset)
    {
        int uiLayer = GetUILayer();

        // Root KeypadCanvas luôn Active ở cấp Root Scene để đảm bảo Awake() gán KeypadUIController.Instance
        GameObject canvasGO = CreateUIObject(KeypadCanvasName, null, uiLayer);
        SceneManager.MoveGameObjectToScene(canvasGO, activeScene);
        Undo.RegisterCreatedObjectUndo(canvasGO, "Create KeypadCanvas");

        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60; // Nằm dưới TransitionCanvas (100) và trên InventoryCanvas (10) / DialogueCanvas

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();
        KeypadUIController controller = canvasGO.AddComponent<KeypadUIController>();

        // KeypadPanelRoot phủ toàn màn hình (0..1), bật raycastTarget = true để chặn click xuyên xuống phòng phía sau
        GameObject panelRoot = CreateUIObject("KeypadPanelRoot", canvasGO.transform, uiLayer);
        StretchFull(panelRoot.GetComponent<RectTransform>());
        Image dimOverlay = panelRoot.AddComponent<Image>();
        dimOverlay.color = DimOverlayColor;
        dimOverlay.raycastTarget = true;

        // KeypadWindow căn giữa màn hình (520 x 720)
        GameObject windowGO = CreateUIObject("KeypadWindow", panelRoot.transform, uiLayer);
        RectTransform windowRect = windowGO.GetComponent<RectTransform>();
        windowRect.anchorMin = new Vector2(0.5f, 0.5f);
        windowRect.anchorMax = new Vector2(0.5f, 0.5f);
        windowRect.pivot = new Vector2(0.5f, 0.5f);
        windowRect.anchoredPosition = Vector2.zero;
        windowRect.sizeDelta = new Vector2(520f, 720f);

        Image windowBg = windowGO.AddComponent<Image>();
        windowBg.color = WindowBgColor;
        windowBg.raycastTarget = false; // Lớp nền KeypadPanelRoot phía sau đã chặn bấm xuyên

        Outline windowOutline = windowGO.AddComponent<Outline>();
        windowOutline.effectColor = BorderSubtleColor;
        windowOutline.effectDistance = new Vector2(2f, -2f);

        // Tiêu đề ổ khóa trên cùng
        GameObject titleGO = CreateUIObject("TitleText", windowGO.transform, uiLayer);
        RectTransform titleRect = titleGO.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.5f, 1f);
        titleRect.anchorMax = new Vector2(0.5f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -24f);
        titleRect.sizeDelta = new Vector2(440f, 44f);
        TextMeshProUGUI titleText = CreateTMPText(
            titleGO,
            "TỦ HỒ SƠ",
            28f,
            FontStyles.Bold,
            TextAlignmentOptions.Center,
            PrimaryTextColor,
            fontAsset
        );

        // DisplayBox + DisplayText cỡ chữ lớn 48 hiện "_ _ _ _"
        GameObject displayBoxGO = CreateUIObject("DisplayBox", windowGO.transform, uiLayer);
        RectTransform displayBoxRect = displayBoxGO.GetComponent<RectTransform>();
        displayBoxRect.anchorMin = new Vector2(0.5f, 1f);
        displayBoxRect.anchorMax = new Vector2(0.5f, 1f);
        displayBoxRect.pivot = new Vector2(0.5f, 1f);
        displayBoxRect.anchoredPosition = new Vector2(0f, -78f);
        displayBoxRect.sizeDelta = new Vector2(405f, 82f);

        Image displayBoxImg = displayBoxGO.AddComponent<Image>();
        displayBoxImg.color = DisplayBoxColor;
        displayBoxImg.raycastTarget = false;

        Outline displayOutline = displayBoxGO.AddComponent<Outline>();
        displayOutline.effectColor = BorderSubtleColor;
        displayOutline.effectDistance = new Vector2(1.5f, -1.5f);

        GameObject displayTextGO = CreateUIObject("DisplayText", displayBoxGO.transform, uiLayer);
        StretchFull(displayTextGO.GetComponent<RectTransform>());
        TextMeshProUGUI displayText = CreateTMPText(
            displayTextGO,
            "_ _ _ _",
            48f,
            FontStyles.Bold,
            TextAlignmentOptions.Center,
            PrimaryTextColor,
            fontAsset
        );

        // StatusText cỡ chữ 22 hiện "NHẬP MẬT MÃ"
        GameObject statusGO = CreateUIObject("StatusText", windowGO.transform, uiLayer);
        RectTransform statusRect = statusGO.GetComponent<RectTransform>();
        statusRect.anchorMin = new Vector2(0.5f, 1f);
        statusRect.anchorMax = new Vector2(0.5f, 1f);
        statusRect.pivot = new Vector2(0.5f, 1f);
        statusRect.anchoredPosition = new Vector2(0f, -168f);
        statusRect.sizeDelta = new Vector2(420f, 34f);
        TextMeshProUGUI statusText = CreateTMPText(
            statusGO,
            "NHẬP MẬT MÃ",
            22f,
            FontStyles.Bold,
            TextAlignmentOptions.Center,
            PrimaryTextColor,
            fontAsset
        );

        // NumpadGrid: 3 cột, ô 125x85, khoảng cách 15x15 (tổng kích thước khớp 405 x 385)
        GameObject numpadGridGO = CreateUIObject("NumpadGrid", windowGO.transform, uiLayer);
        RectTransform numpadRect = numpadGridGO.GetComponent<RectTransform>();
        numpadRect.anchorMin = new Vector2(0.5f, 1f);
        numpadRect.anchorMax = new Vector2(0.5f, 1f);
        numpadRect.pivot = new Vector2(0.5f, 1f);
        numpadRect.anchoredPosition = new Vector2(0f, -216f);
        numpadRect.sizeDelta = new Vector2(405f, 390f);

        GridLayoutGroup grid = numpadGridGO.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(125f, 85f);
        grid.spacing = new Vector2(15f, 15f);
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.childAlignment = TextAnchor.UpperCenter;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;

        // Mảng ánh xạ chính xác theo chỉ số giá trị số: digitButtons[0] = nút 0 ... digitButtons[9] = nút 9
        // Trong khi thứ tự Hierarchy trong NumpadGrid đi theo bàn phím điện thoại chuẩn (1-9, C-0-<)
        Button[] digitButtons = new Button[10];

        // Hàng 1: 1, 2, 3
        digitButtons[1] = CreateKeypadButton("Digit_1", numpadGridGO.transform, "1", 34f, DigitButtonColor, fontAsset, uiLayer);
        digitButtons[2] = CreateKeypadButton("Digit_2", numpadGridGO.transform, "2", 34f, DigitButtonColor, fontAsset, uiLayer);
        digitButtons[3] = CreateKeypadButton("Digit_3", numpadGridGO.transform, "3", 34f, DigitButtonColor, fontAsset, uiLayer);

        // Hàng 2: 4, 5, 6
        digitButtons[4] = CreateKeypadButton("Digit_4", numpadGridGO.transform, "4", 34f, DigitButtonColor, fontAsset, uiLayer);
        digitButtons[5] = CreateKeypadButton("Digit_5", numpadGridGO.transform, "5", 34f, DigitButtonColor, fontAsset, uiLayer);
        digitButtons[6] = CreateKeypadButton("Digit_6", numpadGridGO.transform, "6", 34f, DigitButtonColor, fontAsset, uiLayer);

        // Hàng 3: 7, 8, 9
        digitButtons[7] = CreateKeypadButton("Digit_7", numpadGridGO.transform, "7", 34f, DigitButtonColor, fontAsset, uiLayer);
        digitButtons[8] = CreateKeypadButton("Digit_8", numpadGridGO.transform, "8", 34f, DigitButtonColor, fontAsset, uiLayer);
        digitButtons[9] = CreateKeypadButton("Digit_9", numpadGridGO.transform, "9", 34f, DigitButtonColor, fontAsset, uiLayer);

        // Hàng 4: Clear ("C"), 0 (lưu vào digitButtons[0]), Backspace ("<")
        Button clearButton = CreateKeypadButton("ClearButton", numpadGridGO.transform, "C", 32f, UtilityButtonColor, fontAsset, uiLayer);
        digitButtons[0] = CreateKeypadButton("Digit_0", numpadGridGO.transform, "0", 34f, DigitButtonColor, fontAsset, uiLayer);
        Button backspaceButton = CreateKeypadButton("BackspaceButton", numpadGridGO.transform, "<", 32f, UtilityButtonColor, fontAsset, uiLayer);

        // ActionRow ở đáy khung dùng HorizontalLayoutGroup: SubmitButton ("XÁC NHẬN") + CloseButton ("ĐÓNG")
        GameObject actionRowGO = CreateUIObject("ActionRow", windowGO.transform, uiLayer);
        RectTransform actionRowRect = actionRowGO.GetComponent<RectTransform>();
        actionRowRect.anchorMin = new Vector2(0.5f, 0f);
        actionRowRect.anchorMax = new Vector2(0.5f, 0f);
        actionRowRect.pivot = new Vector2(0.5f, 0f);
        actionRowRect.anchoredPosition = new Vector2(0f, 26f);
        actionRowRect.sizeDelta = new Vector2(405f, 66f);

        HorizontalLayoutGroup actionLayout = actionRowGO.AddComponent<HorizontalLayoutGroup>();
        actionLayout.spacing = 15f;
        actionLayout.childControlWidth = true;
        actionLayout.childControlHeight = true;
        actionLayout.childForceExpandWidth = true;
        actionLayout.childForceExpandHeight = true;
        actionLayout.childAlignment = TextAnchor.MiddleCenter;

        Button submitButton = CreateKeypadButton("SubmitButton", actionRowGO.transform, "XÁC NHẬN", 24f, SubmitButtonColor, fontAsset, uiLayer);
        Button closeButton = CreateKeypadButton("CloseButton", actionRowGO.transform, "ĐÓNG", 24f, CloseButtonColor, fontAsset, uiLayer);

        // Mặc định ẩn Panel khi chưa mở ổ khóa (KeypadCanvas gốc vẫn Active)
        panelRoot.SetActive(false);

        // Nối chính xác 100% các [SerializeField] của KeypadUIController.cs
        SerializedObject controllerSO = new SerializedObject(controller);
        AssignObjectReference(controllerSO, "panelRoot", panelRoot);
        AssignObjectReference(controllerSO, "titleText", titleText);
        AssignObjectReference(controllerSO, "displayText", displayText);
        AssignObjectReference(controllerSO, "statusText", statusText);
        AssignObjectReference(controllerSO, "backspaceButton", backspaceButton);
        AssignObjectReference(controllerSO, "clearButton", clearButton);
        AssignObjectReference(controllerSO, "submitButton", submitButton);
        AssignObjectReference(controllerSO, "closeButton", closeButton);

        SerializedProperty digitButtonsProp = controllerSO.FindProperty("digitButtons");
        if (digitButtonsProp != null && digitButtonsProp.isArray)
        {
            digitButtonsProp.arraySize = 10;
            for (int i = 0; i < 10; i++)
            {
                // Bảo đảm tuyệt đối index i khớp đúng với nút chữ số i (0..9)
                digitButtonsProp.GetArrayElementAtIndex(i).objectReferenceValue = digitButtons[i];
            }
        }
        else
        {
            Debug.LogError("[KeypadSetupGenerator] Không tìm thấy SerializedProperty 'digitButtons' trên KeypadUIController.");
        }

        controllerSO.ApplyModifiedPropertiesWithoutUndo();
        return canvasGO;
    }

    // ================= 2. TẠO Ổ KHÓA MẪU GREYBOX TUHOSO_KEYPAD =================

    private static GameObject BuildSampleKeypadInRoom(Scene activeScene)
    {
        List<GameObject> roomMatches = FindSceneObjectsByName(activeScene, TargetRoomName);
        Transform parentRoom = roomMatches.Count > 0 && roomMatches[0] != null ? roomMatches[0].transform : null;

        GameObject sampleGO = new GameObject(SampleObjectName);
        if (parentRoom != null)
        {
            sampleGO.transform.SetParent(parentRoom, false);
            sampleGO.transform.localPosition = new Vector3(3.5f, 0f, 0f);
        }
        else
        {
            SceneManager.MoveGameObjectToScene(sampleGO, activeScene);
            sampleGO.transform.position = new Vector3(3.5f, 0f, 0f);
        }

        // Giữ Scale = (1, 1, 1) để BoxCollider2D size (1.5, 2) đúng kích thước thế giới thực
        sampleGO.transform.localScale = Vector3.one;
        Undo.RegisterCreatedObjectUndo(sampleGO, "Create TuHoSo_Keypad");

        SpriteRenderer renderer = sampleGO.AddComponent<SpriteRenderer>();
        renderer.sprite = LoadBuiltinSquareSprite();
        renderer.color = SampleKeypadSpriteColor;
        renderer.sortingOrder = 1;

        BoxCollider2D collider = sampleGO.AddComponent<BoxCollider2D>();
        collider.isTrigger = true;
        collider.size = new Vector2(1.5f, 2f);

        KeypadInteractable interactable = sampleGO.AddComponent<KeypadInteractable>();
        SerializedObject interactableSO = new SerializedObject(interactable);

        // Khớp chính xác 100% tên biến [SerializeField] trong KeypadInteractable.cs
        AssignStringValue(interactableSO, "puzzleId", "Ch1_HanhLang_TuHoSo");
        AssignStringValue(interactableSO, "keypadTitle", "TỦ HỒ SƠ HÀNH LANG");
        AssignStringValue(interactableSO, "correctCode", "1408");
        AssignIntValue(interactableSO, "maxDigits", 4);
        AssignBoolValue(interactableSO, "autoSubmitWhenFull", false);
        AssignBoolValue(interactableSO, "allowDirectClickOrTouch", true);

        interactableSO.ApplyModifiedPropertiesWithoutUndo();
        return sampleGO;
    }

    // ================= HÀM TIỆN ÍCH UI, FONT & SPRITE =================

    private static Button CreateKeypadButton(
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
        outline.effectDistance = new Vector2(1.2f, -1.2f);

        Button button = btnGO.AddComponent<Button>();
        button.targetGraphic = btnImage;

        GameObject labelGO = CreateUIObject("Label", btnGO.transform, uiLayer);
        StretchFull(labelGO.GetComponent<RectTransform>());
        CreateTMPText(
            labelGO,
            labelText,
            fontSize,
            FontStyles.Bold,
            TextAlignmentOptions.Center,
            Color.white,
            fontAsset
        );

        return button;
    }

    private static TMP_FontAsset FindProjectTMPFont(Scene activeScene)
    {
        // Ưu tiên 1: Lấy font tiếng Việt từ DialogueCanvas hoặc InventoryCanvas trong Scene
        TMP_FontAsset font = FindFontInNamedCanvas(activeScene, "DialogueCanvas");
        if (font != null) return font;

        font = FindFontInNamedCanvas(activeScene, "InventoryCanvas");
        if (font != null) return font;

        // Ưu tiên 2: Quét toàn bộ TMP_Text hiện có trong Scene đang mở
        GameObject[] roots = activeScene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            font = ExtractFirstFont(roots[i]);
            if (font != null) return font;
        }

        // Dự phòng cuối cùng: font mặc định của TextMeshPro
        return TMP_Settings.defaultFontAsset;
    }

    private static TMP_FontAsset FindFontInNamedCanvas(Scene activeScene, string canvasName)
    {
        List<GameObject> canvases = FindSceneObjectsByName(activeScene, canvasName);
        for (int i = 0; i < canvases.Count; i++)
        {
            TMP_FontAsset font = ExtractFirstFont(canvases[i]);
            if (font != null) return font;
        }

        return null;
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
        tmp.raycastTarget = false; // Tắt Raycast Target trên chữ để tối ưu uGUI và không cản click Button
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

    private static void DestroyObjectListWithUndo(List<GameObject> objects)
    {
        for (int i = 0; i < objects.Count; i++)
        {
            if (objects[i] != null)
            {
                Undo.DestroyObjectImmediate(objects[i]);
            }
        }
    }

    private static void AssignObjectReference(SerializedObject serializedObject, string propertyName, UnityEngine.Object value)
    {
        SerializedProperty prop = serializedObject.FindProperty(propertyName);
        if (prop == null)
        {
            Debug.LogError($"[KeypadSetupGenerator] Không tìm thấy SerializedProperty '{propertyName}' trên '{serializedObject.targetObject.GetType().Name}'.");
            return;
        }

        prop.objectReferenceValue = value;
    }

    private static void AssignStringValue(SerializedObject serializedObject, string propertyName, string value)
    {
        SerializedProperty prop = serializedObject.FindProperty(propertyName);
        if (prop == null)
        {
            Debug.LogError($"[KeypadSetupGenerator] Không tìm thấy SerializedProperty '{propertyName}' trên '{serializedObject.targetObject.GetType().Name}'.");
            return;
        }

        prop.stringValue = value;
    }

    private static void AssignIntValue(SerializedObject serializedObject, string propertyName, int value)
    {
        SerializedProperty prop = serializedObject.FindProperty(propertyName);
        if (prop == null)
        {
            Debug.LogError($"[KeypadSetupGenerator] Không tìm thấy SerializedProperty '{propertyName}' trên '{serializedObject.targetObject.GetType().Name}'.");
            return;
        }

        prop.intValue = value;
    }

    private static void AssignBoolValue(SerializedObject serializedObject, string propertyName, bool value)
    {
        SerializedProperty prop = serializedObject.FindProperty(propertyName);
        if (prop == null)
        {
            Debug.LogError($"[KeypadSetupGenerator] Không tìm thấy SerializedProperty '{propertyName}' trên '{serializedObject.targetObject.GetType().Name}'.");
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
