using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Công cụ Unity Editor tự động sinh TransitionCanvas (uGUI Fade đen),
/// khung Greybox 2 phòng (Room_PhongNgu & Room_HanhLang) và nối toàn bộ tham chiếu cho RoomTransitionManager.
/// Giữ nguyên toàn bộ đối tượng hệ thống ở Root Scene (MainCharacter, CinemachineCamera, Managers, Canvases).
/// </summary>
public static class RoomTransitionSetupGenerator
{
    private const string TransitionCanvasName = "TransitionCanvas";
    private const string ManagerObjectName = "RoomTransitionManager";
    private const string Room1ObjectName = "Room_PhongNgu";
    private const string Room2ObjectName = "Room_HanhLang";

    private const string Room1Id = "Ch1_PhongNgu";
    private const string Room2Id = "Ch1_HanhLang";

    // Các đường dẫn Sprite vuông có sẵn trong gói com.unity.2d.sprite (Unity 6)
    private const string BuiltinSquareSpriteV2Path = "Packages/com.unity.2d.sprite/Editor/ObjectMenuCreation/DefaultAssets/Textures/v2/Square.png";
    private const string BuiltinSquareSpriteV1Path = "Packages/com.unity.2d.sprite/Editor/ObjectMenuCreation/DefaultAssets/Textures/Square.png";

    private static readonly Color DoorToHanhLangColor = new Color(0.22f, 0.78f, 0.36f, 0.95f); // Xanh lá dễ nhận diện Greybox
    private static readonly Color DoorToPhongNguColor = new Color(0.95f, 0.52f, 0.16f, 0.95f); // Cam quay về Phòng Ngủ
    private static readonly Color FloorHanhLangColor = new Color(0.24f, 0.22f, 0.32f, 1.00f);  // Xám tím nhạt nền Hành Lang

    [MenuItem("Vong Nguyet/Room/Generate Room Transition & Greybox 2 Rooms")]
    public static void GenerateRoomTransitionAndGreybox()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid())
        {
            EditorUtility.DisplayDialog("Lỗi Scene", "Không tìm thấy Scene đang mở hợp lệ.", "Đóng");
            return;
        }

        // 1. Kiểm tra nếu Scene đã có TransitionCanvas hoặc RoomTransitionManager -> hỏi xác nhận trước khi ghi đè
        List<GameObject> existingCanvases = FindSceneObjectsByName(activeScene, TransitionCanvasName);
        List<GameObject> existingManagers = FindSceneObjectsByName(activeScene, ManagerObjectName);
        List<GameObject> existingRoom1 = FindSceneObjectsByName(activeScene, Room1ObjectName);
        List<GameObject> existingRoom2 = FindSceneObjectsByName(activeScene, Room2ObjectName);

        bool hasExistingSetup = existingCanvases.Count > 0 || existingManagers.Count > 0;
        if (hasExistingSetup)
        {
            bool overwrite = EditorUtility.DisplayDialog(
                "Cập nhật hệ thống Chuyển phòng?",
                $"Trong Scene '{activeScene.name}' đã tồn tại '{TransitionCanvasName}' hoặc '{ManagerObjectName}'.\n" +
                "Bạn có muốn xóa bản cũ và tạo mới lại TransitionCanvas, RoomTransitionManager cùng Greybox 2 phòng không?\n" +
                "(KetSat và Key_KetSat_World sẽ được giữ nguyên tọa độ thế giới).",
                "Tạo mới / Cập nhật",
                "Hủy"
            );

            if (!overwrite)
            {
                return;
            }
        }

        // Bảo vệ dữ liệu cũ: nếu chạy lại tool lần 2, đưa KetSat & Key_KetSat_World tạm ra Root
        // trước khi hủy Room_PhongNgu cũ để tuyệt đối không làm mất 2 object cốt truyện này
        RescueNamedObjectToSceneRoot(activeScene, "KetSat");
        RescueNamedObjectToSceneRoot(activeScene, "Key_KetSat_World");

        // Xóa các bản tạo cũ (nếu có) để tránh trùng lặp Hierarchy
        DestroyObjectListWithUndo(existingCanvases);
        DestroyObjectListWithUndo(existingManagers);
        DestroyObjectListWithUndo(existingRoom1);
        DestroyObjectListWithUndo(existingRoom2);

        // Tìm nhân vật chính và CinemachineCamera ở Root (TUYỆT ĐỐI giữ ở Root, không kéo vào RoomNode)
        Transform playerTransform = FindPlayerTransform(activeScene);
        CinemachineCamera virtualCamera = UnityEngine.Object.FindFirstObjectByType<CinemachineCamera>(FindObjectsInactive.Include);
        CinemachineConfiner2D cameraConfiner = virtualCamera != null ? virtualCamera.GetComponent<CinemachineConfiner2D>() : null;

        Sprite greyboxSquareSprite = LoadBuiltinSquareSprite();

        // 2. Tạo TransitionCanvas (uGUI ScreenSpaceOverlay, sortingOrder = 100) + FadePanel
        CanvasGroup fadeCanvasGroup = BuildTransitionCanvas(activeScene);

        // 3. Dựng Phòng 1: Room_PhongNgu tại (0, 0, 0)
        RoomNode room1Node = BuildRoomPhongNgu(activeScene, playerTransform, greyboxSquareSprite);

        // 4. Dựng Phòng 2: Room_HanhLang tại (30, 0, 0) tách biệt hẳn trong Scene View
        RoomNode room2Node = BuildRoomHanhLang(activeScene, greyboxSquareSprite);

        // 5. Tạo & nối RoomTransitionManager tại Root Scene (luôn Active)
        GameObject managerGO = BuildRoomTransitionManager(
            activeScene,
            playerTransform,
            fadeCanvasGroup,
            room1Node,
            room2Node,
            virtualCamera,
            cameraConfiner
        );

        // 6. Đánh dấu Scene bẩn để người dùng bấm Ctrl+S lưu chuẩn GUID
        EditorSceneManager.MarkSceneDirty(activeScene);
        Selection.activeGameObject = managerGO;

        Debug.Log(
            $"[RoomTransitionSetupGenerator] Hoàn tất dựng TransitionCanvas, RoomTransitionManager và Greybox 2 phòng " +
            $"('{Room1Id}' & '{Room2Id}') trong Scene '{activeScene.name}'. " +
            $"Player: {(playerTransform != null ? playerTransform.name : "CHƯA TÌM THẤY - hãy kéo thủ công")}, " +
            $"CinemachineCamera: {(virtualCamera != null ? virtualCamera.name : "Không có trong Scene")}."
        );
    }

    // ================= 1. TẠO TRANSITION CANVAS (uGUI) =================

    private static CanvasGroup BuildTransitionCanvas(Scene activeScene)
    {
        int uiLayer = GetUILayer();

        GameObject canvasGO = new GameObject(TransitionCanvasName, typeof(RectTransform));
        canvasGO.layer = uiLayer;
        SceneManager.MoveGameObjectToScene(canvasGO, activeScene);
        Undo.RegisterCreatedObjectUndo(canvasGO, "Create TransitionCanvas");

        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100; // Phủ lên trên cả InventoryCanvas (10) và DialogueCanvas

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        // FadePanel phủ toàn màn hình (0..1), Image màu đen, CanvasGroup khởi đầu trong suốt và không chặn click
        GameObject fadePanelGO = new GameObject("FadePanel", typeof(RectTransform));
        fadePanelGO.layer = uiLayer;
        fadePanelGO.transform.SetParent(canvasGO.transform, false);

        RectTransform fadeRect = fadePanelGO.GetComponent<RectTransform>();
        fadeRect.anchorMin = Vector2.zero;
        fadeRect.anchorMax = Vector2.one;
        fadeRect.offsetMin = Vector2.zero;
        fadeRect.offsetMax = Vector2.zero;
        fadeRect.pivot = new Vector2(0.5f, 0.5f);

        Image fadeImage = fadePanelGO.AddComponent<Image>();
        fadeImage.color = Color.black;
        fadeImage.raycastTarget = true; // Khi CanvasGroup bật blocksRaycasts lúc chuyển phòng sẽ chặn bấm xuyên xuống Scene/UI

        CanvasGroup fadeCanvasGroup = fadePanelGO.AddComponent<CanvasGroup>();
        fadeCanvasGroup.alpha = 0f;
        fadeCanvasGroup.blocksRaycasts = false;
        fadeCanvasGroup.interactable = false;

        return fadeCanvasGroup;
    }

    // ================= 2. DỰNG PHÒNG 1: ROOM_PHONGNGU =================

    private static RoomNode BuildRoomPhongNgu(Scene activeScene, Transform playerTransform, Sprite squareSprite)
    {
        GameObject room1GO = new GameObject(Room1ObjectName);
        room1GO.transform.position = Vector3.zero;
        SceneManager.MoveGameObjectToScene(room1GO, activeScene);
        Undo.RegisterCreatedObjectUndo(room1GO, "Create Room_PhongNgu");

        RoomNode roomNode = room1GO.AddComponent<RoomNode>();

        // Di chuyển KetSat và Key_KetSat_World (nếu đang nằm ở Root Scene) vào trong Room_PhongNgu
        // dùng worldPositionStays = true để giữ nguyên 100% tọa độ thế giới đã căn chỉnh trước đó
        ReparentRootObjectIfPresent(activeScene, "KetSat", room1GO.transform);
        ReparentRootObjectIfPresent(activeScene, "Key_KetSat_World", room1GO.transform);

        // Điểm Spawn_Default: đặt đúng vị trí hiện tại của MainCharacter (hoặc (0,0,0) nếu chưa tìm thấy)
        Vector3 defaultSpawnPos = playerTransform != null
            ? new Vector3(playerTransform.position.x, playerTransform.position.y, 0f)
            : Vector3.zero;

        Transform spawnDefault = CreateSpawnPointChild("Spawn_Default", room1GO.transform, defaultSpawnPos, isWorldPosition: true);

        // Điểm Spawn_FromHanhLang: đặt lệch ở (3.5, 0, 0), cách xa cửa (5.5, 0, 0) để tránh dính ngay Trigger cửa
        Transform spawnFromHanhLang = CreateSpawnPointChild("Spawn_FromHanhLang", room1GO.transform, new Vector3(3.5f, 0f, 0f), isWorldPosition: false);

        // Cửa Door_ToHanhLang tại (5.5, 0, 0) màu xanh lá
        CreateGreyboxDoor(
            doorName: "Door_ToHanhLang",
            parent: room1GO.transform,
            localPosition: new Vector3(5.5f, 0f, 0f),
            sprite: squareSprite,
            tintColor: DoorToHanhLangColor,
            targetRoomId: Room2Id,
            targetSpawnPointId: "From_PhongNgu",
            requiredSolvedPuzzleId: string.Empty // Để trống cho test đi tự do giữa 2 phòng
        );

        // Nối chính xác các [SerializeField] của RoomNode.cs (roomId, cameraBounds, spawnPoints)
        ConfigureRoomNodeSerializedFields(
            roomNode,
            Room1Id,
            cameraBounds: null,
            new (string, Transform)[]
            {
                ("Default", spawnDefault),
                ("From_HanhLang", spawnFromHanhLang)
            }
        );

        return roomNode;
    }

    // ================= 3. DỰNG PHÒNG 2: ROOM_HANHLANG =================

    private static RoomNode BuildRoomHanhLang(Scene activeScene, Sprite squareSprite)
    {
        // Đặt Room_HanhLang ở tọa độ thế giới (30, 0, 0) để tách biệt trực quan hẳn khỏi Phòng Ngủ trong Scene View
        GameObject room2GO = new GameObject(Room2ObjectName);
        room2GO.transform.position = new Vector3(30f, 0f, 0f);
        SceneManager.MoveGameObjectToScene(room2GO, activeScene);
        Undo.RegisterCreatedObjectUndo(room2GO, "Create Room_HanhLang");

        RoomNode roomNode = room2GO.AddComponent<RoomNode>();

        // Nền Greybox phía sau (sortingOrder = -5) để nhận biết rõ ràng khi bước sang Hành Lang
        GameObject floorGO = new GameObject("Floor_HanhLang");
        floorGO.transform.SetParent(room2GO.transform, false);
        floorGO.transform.localPosition = Vector3.zero;
        floorGO.transform.localScale = new Vector3(16f, 10f, 1f);

        SpriteRenderer floorRenderer = floorGO.AddComponent<SpriteRenderer>();
        floorRenderer.sprite = squareSprite;
        floorRenderer.color = FloorHanhLangColor;
        floorRenderer.sortingOrder = -5;

        // Điểm Spawn_FromPhongNgu tại tọa độ cục bộ (-3.5, 0, 0) — lệch ra khỏi cửa (-5.5, 0, 0)
        Transform spawnFromPhongNgu = CreateSpawnPointChild("Spawn_FromPhongNgu", room2GO.transform, new Vector3(-3.5f, 0f, 0f), isWorldPosition: false);

        // Cửa Door_ToPhongNgu tại tọa độ cục bộ (-5.5, 0, 0) màu cam quay ngược về Phòng Ngủ
        CreateGreyboxDoor(
            doorName: "Door_ToPhongNgu",
            parent: room2GO.transform,
            localPosition: new Vector3(-5.5f, 0f, 0f),
            sprite: squareSprite,
            tintColor: DoorToPhongNguColor,
            targetRoomId: Room1Id,
            targetSpawnPointId: "From_HanhLang",
            requiredSolvedPuzzleId: string.Empty
        );

        // Nối chính xác các [SerializeField] của RoomNode.cs
        ConfigureRoomNodeSerializedFields(
            roomNode,
            Room2Id,
            cameraBounds: null,
            new (string, Transform)[]
            {
                ("From_PhongNgu", spawnFromPhongNgu)
            }
        );

        return roomNode;
    }

    // ================= 4. TẠO & NỐI ROOM TRANSITION MANAGER =================

    private static GameObject BuildRoomTransitionManager(
        Scene activeScene,
        Transform playerTransform,
        CanvasGroup fadeCanvasGroup,
        RoomNode room1Node,
        RoomNode room2Node,
        CinemachineCamera virtualCamera,
        CinemachineConfiner2D cameraConfiner)
    {
        // Đặt ở Root Scene và luôn Active để quản lý việc bật/tắt các RoomNode con
        GameObject managerGO = new GameObject(ManagerObjectName);
        SceneManager.MoveGameObjectToScene(managerGO, activeScene);
        Undo.RegisterCreatedObjectUndo(managerGO, "Create RoomTransitionManager");

        RoomTransitionManager manager = managerGO.AddComponent<RoomTransitionManager>();
        SerializedObject managerSO = new SerializedObject(manager);

        // Khớp chính xác 100% tên biến [SerializeField] trong RoomTransitionManager.cs
        AssignObjectReference(managerSO, "playerTransform", playerTransform);
        AssignObjectReference(managerSO, "fadeCanvasGroup", fadeCanvasGroup);
        AssignStringValue(managerSO, "initialRoomId", Room1Id);
        AssignStringValue(managerSO, "initialSpawnPointId", "Default");
        AssignObjectReference(managerSO, "virtualCamera", virtualCamera);
        AssignObjectReference(managerSO, "cameraConfiner", cameraConfiner);

        SerializedProperty roomsProp = managerSO.FindProperty("rooms");
        if (roomsProp != null && roomsProp.isArray)
        {
            roomsProp.arraySize = 2;
            roomsProp.GetArrayElementAtIndex(0).objectReferenceValue = room1Node;
            roomsProp.GetArrayElementAtIndex(1).objectReferenceValue = room2Node;
        }
        else
        {
            Debug.LogError("[RoomTransitionSetupGenerator] Không tìm thấy SerializedProperty 'rooms' trên RoomTransitionManager.");
        }

        managerSO.ApplyModifiedPropertiesWithoutUndo();
        return managerGO;
    }

    // ================= HÀM TIỆN ÍCH CẤU HÌNH GREYBOX & SERIALIZED OBJECT =================

    private static Transform CreateSpawnPointChild(string pointName, Transform parent, Vector3 position, bool isWorldPosition)
    {
        GameObject spawnGO = new GameObject(pointName);
        spawnGO.transform.SetParent(parent, false);
        if (isWorldPosition)
        {
            spawnGO.transform.position = position;
        }
        else
        {
            spawnGO.transform.localPosition = position;
        }

        return spawnGO.transform;
    }

    private static void CreateGreyboxDoor(
        string doorName,
        Transform parent,
        Vector3 localPosition,
        Sprite sprite,
        Color tintColor,
        string targetRoomId,
        string targetSpawnPointId,
        string requiredSolvedPuzzleId)
    {
        GameObject doorGO = new GameObject(doorName);
        doorGO.transform.SetParent(parent, false);
        doorGO.transform.localPosition = localPosition;
        // Giữ localScale = (1, 1, 1) để BoxCollider2D size (1.5, 2) phản ánh đúng kích thước thế giới thực
        doorGO.transform.localScale = Vector3.one;

        SpriteRenderer renderer = doorGO.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = tintColor;
        renderer.sortingOrder = 1;

        BoxCollider2D collider = doorGO.AddComponent<BoxCollider2D>();
        collider.isTrigger = true;
        collider.size = new Vector2(1.5f, 2f);

        RoomDoor roomDoor = doorGO.AddComponent<RoomDoor>();
        SerializedObject doorSO = new SerializedObject(roomDoor);

        // Khớp chính xác 100% tên biến [SerializeField] trong RoomDoor.cs
        AssignStringValue(doorSO, "targetRoomId", targetRoomId);
        AssignStringValue(doorSO, "targetSpawnPointId", targetSpawnPointId);
        AssignStringValue(doorSO, "requiredSolvedPuzzleId", requiredSolvedPuzzleId);

        SerializedProperty allowClickProp = doorSO.FindProperty("allowDirectClickOrTouch");
        if (allowClickProp != null)
        {
            allowClickProp.boolValue = true;
        }

        doorSO.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ConfigureRoomNodeSerializedFields(
        RoomNode roomNode,
        string roomId,
        Collider2D cameraBounds,
        (string spawnId, Transform point)[] entries)
    {
        SerializedObject nodeSO = new SerializedObject(roomNode);

        // Khớp chính xác 100% tên biến [SerializeField] trong RoomNode.cs
        AssignStringValue(nodeSO, "roomId", roomId);
        AssignObjectReference(nodeSO, "cameraBounds", cameraBounds);

        SerializedProperty spawnPointsProp = nodeSO.FindProperty("spawnPoints");
        if (spawnPointsProp != null && spawnPointsProp.isArray)
        {
            spawnPointsProp.arraySize = entries.Length;
            for (int i = 0; i < entries.Length; i++)
            {
                SerializedProperty element = spawnPointsProp.GetArrayElementAtIndex(i);
                SerializedProperty idProp = element.FindPropertyRelative("spawnId");
                SerializedProperty pointProp = element.FindPropertyRelative("point");

                if (idProp != null) idProp.stringValue = entries[i].spawnId;
                if (pointProp != null) pointProp.objectReferenceValue = entries[i].point;
            }
        }
        else
        {
            Debug.LogError("[RoomTransitionSetupGenerator] Không tìm thấy SerializedProperty 'spawnPoints' trên RoomNode.");
        }

        nodeSO.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Transform FindPlayerTransform(Scene activeScene)
    {
        // Ưu tiên tìm đúng tên MainCharacter trong Scene hiện tại
        List<GameObject> byName = FindSceneObjectsByName(activeScene, "MainCharacter");
        if (byName.Count > 0 && byName[0] != null)
        {
            return byName[0].transform;
        }

        // Dự phòng: tìm theo component PlayerController
        PlayerController playerController = UnityEngine.Object.FindFirstObjectByType<PlayerController>(FindObjectsInactive.Include);
        return playerController != null ? playerController.transform : null;
    }

    private static void ReparentRootObjectIfPresent(Scene activeScene, string rootObjectName, Transform newParent)
    {
        GameObject[] roots = activeScene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i] != null && string.Equals(roots[i].name, rootObjectName, StringComparison.Ordinal))
            {
                // worldPositionStays = true để giữ nguyên tọa độ thế giới của KetSat / Key_KetSat_World
                Undo.SetTransformParent(roots[i].transform, newParent, true, $"Move {rootObjectName} into {newParent.name}");
                break;
            }
        }
    }

    private static void RescueNamedObjectToSceneRoot(Scene activeScene, string objectName)
    {
        List<GameObject> matches = FindSceneObjectsByName(activeScene, objectName);
        for (int i = 0; i < matches.Count; i++)
        {
            GameObject go = matches[i];
            if (go != null && go.transform.parent != null)
            {
                Undo.SetTransformParent(go.transform, null, true, $"Rescue {objectName} to Scene Root");
            }
        }
    }

    private static Sprite LoadBuiltinSquareSprite()
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(BuiltinSquareSpriteV2Path);
        if (sprite != null) return sprite;

        sprite = AssetDatabase.LoadAssetAtPath<Sprite>(BuiltinSquareSpriteV1Path);
        if (sprite != null) return sprite;

        // Fallback cuối cùng: sprite UI mặc định có sẵn trong Unity Editor
        return AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
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
            Debug.LogError(
                $"[RoomTransitionSetupGenerator] Không tìm thấy SerializedProperty '{propertyName}' trên '{serializedObject.targetObject.GetType().Name}'.");
            return;
        }

        prop.objectReferenceValue = value;
    }

    private static void AssignStringValue(SerializedObject serializedObject, string propertyName, string value)
    {
        SerializedProperty prop = serializedObject.FindProperty(propertyName);
        if (prop == null)
        {
            Debug.LogError(
                $"[RoomTransitionSetupGenerator] Không tìm thấy SerializedProperty '{propertyName}' trên '{serializedObject.targetObject.GetType().Name}'.");
            return;
        }

        prop.stringValue = value;
    }

    private static int GetUILayer()
    {
        int layer = LayerMask.NameToLayer("UI");
        return layer >= 0 ? layer : 5;
    }
}
