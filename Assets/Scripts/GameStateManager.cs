using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

// --- PHẦN THÊM MỚI: Định nghĩa trạng thái game ---
public enum GameState
{
    FreeRoam, // Tự do đi lại giải đố
    Cutscene  // Đang xem hội thoại, khóa di chuyển
}

/// <summary>
/// Quản lý trạng thái toàn cục của game (trạng thái chơi, câu đố, rẽ nhánh, vật thể đã nhặt, lưu/tải).
/// Singleton + DontDestroyOnLoad. Kho đồ được quản lý tách biệt bởi InventoryManager.
/// </summary>
public class GameStateManager : MonoBehaviour
{
    public static GameStateManager Instance { get; private set; }

    private const int CurrentSaveVersion = 1;
    private const string DefaultChapterId = "Ch1";
    private const string DefaultPlayerName = "Main";
    private const string SaveFileName = "vongnguyet_save.json";

    [Header("Cấu hình Lưu / Tải")]
    [Tooltip("Nếu bật, tự động tải file Save cục bộ trong Start() (sau khi InventoryManager.Awake đã chạy xong).")]
    [SerializeField] private bool autoLoadOnStart = false;

    [Tooltip("Nếu bật, tự động lưu tiến độ xuống file Save ngay sau khi người chơi hoàn tất chuyển phòng.")]
    [SerializeField] private bool autoSaveOnRoomChange = true;

    // --- CÁC KÊNH PHÁT SÓNG SỰ KIỆN ---
    public event Action<GameState> OnGameStateChanged;
    public event Action<string> OnPuzzleStateTriggered;
    public event Action<string, string> OnPuzzleStateChanged;

    private GameSaveData currentSaveData = new GameSaveData();

    public GameSaveData Data => currentSaveData;
    public GameSaveData currentData => currentSaveData;
    public GameState CurrentState { get; private set; }

    // Lưu trạng thái câu đố nhiều giai đoạn: Key = puzzleId, Value = stage (VD: "KeyDropped", "Solved")
    private readonly Dictionary<string, string> _puzzleStages = new Dictionary<string, string>();

    // Lưu ID các vật thể đã nhặt để chặn lỗi hồi sinh vật phẩm (Item Respawn) khi tải lại Scene
    private readonly HashSet<string> _consumedWorldObjectIds = new HashSet<string>();

    // Lưu đúng tham chiếu RoomTransitionManager của Scene hiện tại để hủy đăng ký chống Memory Leak
    private RoomTransitionManager _subscribedRoomTransitionManager;
    private bool _hasCompletedStart;
    private bool _isApplyingLoadedSave;

    private string SavePath => GetSaveFilePath();
    private string TempSavePath => SavePath + ".tmp";

    // ================= TIỆN ÍCH TĨNH CHO MAIN MENU (KHÔNG CẦN INSTANCE) =================

    /// <summary>Trả về đường dẫn đầy đủ tới file Save cục bộ vongnguyet_save.json.</summary>
    public static string GetSaveFilePath()
    {
        return Path.Combine(Application.persistentDataPath, SaveFileName);
    }

    /// <summary>Kiểm tra nhanh máy người chơi đã có file Save cục bộ hay chưa (dùng bật/tắt nút Tiếp tục ở MainMenu).</summary>
    public static bool HasSaveFile()
    {
        return File.Exists(GetSaveFilePath());
    }

    /// <summary>Xóa file Save cục bộ an toàn từ MainMenu mà không cần khởi tạo GameStateManager.</summary>
    public static void DeleteSaveFileStatic()
    {
        try
        {
            string savePath = GetSaveFilePath();
            if (File.Exists(savePath))
                File.Delete(savePath);

            string tempPath = savePath + ".tmp";
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[GameStateManager] Không thể xóa file save cục bộ (Static): {e.Message}");
        }
    }

    private void Awake()
    {
        // Chặn nhân bản Singleton khi người chơi chuyển hoặc quay lại Scene cũ
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // Edge Case: DontDestroyOnLoad chỉ hoạt động khi GameObject nằm ở cấp Root của Hierarchy
        if (transform.parent != null)
            transform.SetParent(null);

        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        TrySubscribeRoomTransition();
    }

    private void Start()
    {
        // Đăng ký lại trong Start() đề phòng GameStateManager.OnEnable chạy trước RoomTransitionManager.Awake
        TrySubscribeRoomTransition();

        // Edge Case Execution Order: chỉ tiêu thụ LaunchMode trong Start() sau khi InventoryManager.Awake đã chạy xong
        ApplyPendingLaunchMode(allowEditorAutoLoadFallback: true);

        _hasCompletedStart = true;

        // Mặc định vào game là cho phép đi lại tự do
        ChangeState(GameState.FreeRoam);
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        UnsubscribeRoomTransition();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        UnsubscribeRoomTransition();

        if (Instance == this)
            Instance = null;
    }

    // ================= ĐỒNG BỘ CHUYỂN PHÒNG & CHẾ ĐỘ KHỞI CHẠY =================

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Khi sang Scene mới, gắn lại sự kiện với RoomTransitionManager mới của Scene đó
        UnsubscribeRoomTransition();
        TrySubscribeRoomTransition();

        // Edge Case DontDestroyOnLoad: khi quay về MainMenu rồi vào lại Gameplay Scene,
        // Start() của GameStateManager không chạy lại -> phải tiêu thụ LaunchMode tại sceneLoaded
        if (_hasCompletedStart)
        {
            ApplyPendingLaunchMode(allowEditorAutoLoadFallback: false);
            ChangeState(GameState.FreeRoam);
        }
    }

    private void ApplyPendingLaunchMode(bool allowEditorAutoLoadFallback)
    {
        SessionLaunchMode launchMode = GameSessionLaunchContext.ConsumePendingLaunchMode();
        switch (launchMode)
        {
            case SessionLaunchMode.NewGame:
                // Xóa sạch tiến độ cũ trong bộ nhớ để bắt đầu lại từ phòng khởi đầu
                ResetSaveData();
                break;

            case SessionLaunchMode.ContinueSave:
                LoadLocalGame();
                break;

            case SessionLaunchMode.EditorDefault:
            default:
                // Giữ nguyên hành vi test trực tiếp Scene trong Editor theo cờ autoLoadOnStart trên Inspector
                if (allowEditorAutoLoadFallback && autoLoadOnStart && File.Exists(SavePath))
                {
                    LoadLocalGame();
                }
                break;
        }
    }

    private void TrySubscribeRoomTransition()
    {
        RoomTransitionManager currentRoomManager = RoomTransitionManager.Instance;
        if (currentRoomManager == null || _subscribedRoomTransitionManager == currentRoomManager)
            return;

        UnsubscribeRoomTransition();
        _subscribedRoomTransitionManager = currentRoomManager;
        _subscribedRoomTransitionManager.OnRoomChanged += HandleRoomChanged;
    }

    private void UnsubscribeRoomTransition()
    {
        if (_subscribedRoomTransitionManager != null)
        {
            _subscribedRoomTransitionManager.OnRoomChanged -= HandleRoomChanged;
            _subscribedRoomTransitionManager = null;
        }
    }

    private void HandleRoomChanged(string roomId, string spawnPointId)
    {
        EnsureSaveDataIntegrity(currentSaveData);
        currentSaveData.currentRoomId = roomId ?? string.Empty;
        currentSaveData.currentSpawnPointId = spawnPointId ?? string.Empty;

        // Chống Race Condition / ghi đè Save khi khởi tạo phòng đầu hoặc đang nạp file Save:
        // Chỉ tự động lưu xuống đĩa khi thực sự đang chuyển phòng trong lúc chơi (IsTransitioning == true)
        if (autoSaveOnRoomChange
            && _hasCompletedStart
            && !_isApplyingLoadedSave
            && RoomTransitionManager.Instance != null
            && RoomTransitionManager.Instance.IsTransitioning)
        {
            SaveGameProgress();
        }
    }

    // ================= QUẢN LÝ TRẠNG THÁI =================

    public void ChangeState(GameState newState)
    {
        CurrentState = newState;
        OnGameStateChanged?.Invoke(newState);
    }

    public void TriggerPuzzleState(string stateName)
    {
        if (!string.IsNullOrEmpty(stateName))
        {
            Debug.Log($"[GameStateManager] Kích hoạt cờ cốt truyện: {stateName}");
            OnPuzzleStateTriggered?.Invoke(stateName);

            // Tự động lưu cờ này vào dữ liệu Save để ghi nhớ
            SetPuzzleState(stateName, true);
        }
    }

    // ================= CÂU ĐỐ & RẼ NHÁNH (BOOL) =================

    public void SetPuzzleState(string puzzleID, bool state)
    {
        if (string.IsNullOrEmpty(puzzleID)) return;

        EnsureSaveDataIntegrity(currentSaveData);

        for (int i = 0; i < currentSaveData.puzzleStates.Count; i++)
        {
            PuzzleState p = currentSaveData.puzzleStates[i];
            if (p != null && p.puzzleID == puzzleID)
            {
                p.state = state;
                return;
            }
        }

        currentSaveData.puzzleStates.Add(new PuzzleState(puzzleID, state));
    }

    public bool GetPuzzleState(string puzzleID)
    {
        if (string.IsNullOrEmpty(puzzleID) || currentSaveData == null || currentSaveData.puzzleStates == null)
            return false;

        for (int i = 0; i < currentSaveData.puzzleStates.Count; i++)
        {
            PuzzleState p = currentSaveData.puzzleStates[i];
            if (p != null && p.puzzleID == puzzleID)
                return p.state;
        }

        return false;
    }

    // ================= CÂU ĐỐ ĐA TRẠNG THÁI & VẬT THỂ ĐÃ NHẶT =================

    /// <summary>Cập nhật trạng thái câu đố dạng chuỗi (Overload của bản bool cũ).</summary>
    public void SetPuzzleState(string puzzleId, string newState)
    {
        if (string.IsNullOrEmpty(puzzleId) || string.IsNullOrEmpty(newState)) return;

        // Chặn phát sự kiện thừa nếu trạng thái mới trùng với trạng thái hiện tại
        if (_puzzleStages.TryGetValue(puzzleId, out string currentState) && currentState == newState)
            return;

        _puzzleStages[puzzleId] = newState;
        Debug.Log($"[GameStateManager] Đổi trạng thái câu đố: {puzzleId} -> {newState}");

        OnPuzzleStateChanged?.Invoke(puzzleId, newState);
    }

    /// <summary>Đọc trạng thái câu đố dạng chuỗi (tránh trùng chữ ký với hàm bool GetPuzzleState cũ).</summary>
    public string GetPuzzleStateString(string puzzleId)
    {
        if (string.IsNullOrEmpty(puzzleId)) return string.Empty;
        return _puzzleStages.TryGetValue(puzzleId, out string state) ? state : string.Empty;
    }

    /// <summary>Đánh dấu vật thể ngoài bản đồ đã bị nhặt/tiêu thụ vĩnh viễn.</summary>
    public void MarkObjectConsumed(string objectId)
    {
        if (!string.IsNullOrEmpty(objectId))
            _consumedWorldObjectIds.Add(objectId);
    }

    public bool IsObjectConsumed(string objectId)
    {
        return !string.IsNullOrEmpty(objectId) && _consumedWorldObjectIds.Contains(objectId);
    }

    // ================= NGƯỜI CHƠI =================

    public void SetPlayerName(string name)
    {
        EnsureSaveDataIntegrity(currentSaveData);
        currentSaveData.playerName = string.IsNullOrWhiteSpace(name) ? DefaultPlayerName : name.Trim();
    }

    // ================= LƯU / TẢI / ĐẶT LẠI DỮ LIỆU =================

    public void SaveGameProgress()
    {
        try
        {
            EnsureSaveDataIntegrity(currentSaveData);

            currentSaveData.saveVersion = CurrentSaveVersion;
            currentSaveData.chapterId = string.IsNullOrWhiteSpace(currentSaveData.chapterId)
                ? DefaultChapterId
                : currentSaveData.chapterId;
            currentSaveData.currentScene = SceneManager.GetActiveScene().name;

            // Đồng bộ mã phòng & điểm spawn hiện tại từ RoomTransitionManager trước khi ghi JSON
            if (RoomTransitionManager.Instance != null && !string.IsNullOrEmpty(RoomTransitionManager.Instance.CurrentRoomId))
            {
                currentSaveData.currentRoomId = RoomTransitionManager.Instance.CurrentRoomId;
                currentSaveData.currentSpawnPointId = RoomTransitionManager.Instance.CurrentSpawnPointId ?? string.Empty;
            }

            // Đồng bộ kho đồ từ InventoryManager (bản sao độc lập)
            if (InventoryManager.Instance != null)
            {
                currentSaveData.inventory = InventoryManager.Instance.ExportSaveData();
            }

            // Đồng bộ Dictionary câu đố đa giai đoạn -> List<PuzzleStageEntry> (tái sử dụng List để giảm GC)
            currentSaveData.puzzleStages.Clear();
            foreach (KeyValuePair<string, string> pair in _puzzleStages)
            {
                if (string.IsNullOrEmpty(pair.Key) || string.IsNullOrEmpty(pair.Value))
                    continue;

                currentSaveData.puzzleStages.Add(new PuzzleStageEntry(pair.Key, pair.Value));
            }

            // Đồng bộ HashSet vật thể đã nhặt -> List<string> (tái sử dụng List để giảm GC)
            currentSaveData.consumedWorldObjectIds.Clear();
            foreach (string objectId in _consumedWorldObjectIds)
            {
                if (!string.IsNullOrEmpty(objectId))
                    currentSaveData.consumedWorldObjectIds.Add(objectId);
            }

            string directory = Path.GetDirectoryName(SavePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            string json = JsonUtility.ToJson(currentSaveData, true);

            // Edge Case Android/Crash: Atomic Write - ghi ra file tạm .tmp trước rồi mới thay thế file chính
            File.WriteAllText(TempSavePath, json);

            if (File.Exists(SavePath))
            {
                File.Replace(TempSavePath, SavePath, null);
            }
            else
            {
                File.Move(TempSavePath, SavePath);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[GameStateManager] Lưu game thất bại: {e.Message}");
            TryDeleteTempFile();
        }
    }

    public void LoadLocalGame()
    {
        _isApplyingLoadedSave = true;
        try
        {
            TrySubscribeRoomTransition();

            if (!File.Exists(SavePath))
            {
                Debug.LogWarning("[GameStateManager] Không tìm thấy file save. Khởi tạo dữ liệu mới sạch.");
                InitializeCleanSaveState();
                return;
            }

            string json = File.ReadAllText(SavePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                Debug.LogWarning("[GameStateManager] File save rỗng. Khởi tạo dữ liệu mới sạch.");
                InitializeCleanSaveState();
                return;
            }

            GameSaveData loaded = JsonUtility.FromJson<GameSaveData>(json);
            if (loaded == null)
            {
                Debug.LogWarning("[GameStateManager] Không parse được dữ liệu từ file save. Khởi tạo dữ liệu mới sạch.");
                InitializeCleanSaveState();
                return;
            }

            // Chống null cho mọi danh sách để tương thích ngược với file Save phiên bản cũ
            EnsureSaveDataIntegrity(loaded);
            currentSaveData = loaded;

            // Nạp ngược puzzleStages vào Dictionary nội bộ (bỏ qua entry rỗng do Edge Case JsonUtility)
            _puzzleStages.Clear();
            for (int i = 0; i < currentSaveData.puzzleStages.Count; i++)
            {
                PuzzleStageEntry entry = currentSaveData.puzzleStages[i];
                if (entry == null || string.IsNullOrEmpty(entry.puzzleId) || string.IsNullOrEmpty(entry.stage))
                    continue;

                _puzzleStages[entry.puzzleId] = entry.stage;
            }

            // Nạp ngược consumedWorldObjectIds vào HashSet nội bộ
            _consumedWorldObjectIds.Clear();
            for (int i = 0; i < currentSaveData.consumedWorldObjectIds.Count; i++)
            {
                string objectId = currentSaveData.consumedWorldObjectIds[i];
                if (!string.IsNullOrEmpty(objectId))
                    _consumedWorldObjectIds.Add(objectId);
            }

            // Khôi phục kho đồ sang InventoryManager (dùng != null đúng chuẩn UnityEngine.Object)
            if (InventoryManager.Instance != null)
            {
                InventoryManager.Instance.ImportSaveData(currentSaveData.inventory);
            }

            // Thông báo lại các trạng thái câu đố đã nạp để vật thể ngoài bản đồ đồng bộ hiển thị
            foreach (KeyValuePair<string, string> pair in _puzzleStages)
            {
                OnPuzzleStateChanged?.Invoke(pair.Key, pair.Value);
            }

            // Khôi phục vị trí phòng đã lưu (nếu Save cũ chưa có currentRoomId thì để RoomTransitionManager tự dùng phòng khởi đầu)
            if (RoomTransitionManager.Instance != null && !string.IsNullOrEmpty(currentSaveData.currentRoomId))
            {
                RoomTransitionManager.Instance.TeleportImmediate(
                    currentSaveData.currentRoomId,
                    currentSaveData.currentSpawnPointId
                );
            }
        }
        catch (Exception e)
        {
            // Edge Case: file JSON bị hỏng định dạng -> không làm sập game, fallback về Save sạch
            Debug.LogWarning($"[GameStateManager] Tải game thất bại do lỗi đọc/định dạng file save ({e.Message}). Khởi tạo dữ liệu mới sạch.");
            InitializeCleanSaveState();
        }
        finally
        {
            _isApplyingLoadedSave = false;
        }
    }

    /// <summary>Đặt lại toàn bộ dữ liệu Save về trạng thái sạch (dùng cho nút Chơi mới / New Game và test nhanh).</summary>
    public void ResetSaveData(bool deleteLocalFile = false)
    {
        currentSaveData = new GameSaveData();
        currentSaveData.currentRoomId = string.Empty;
        currentSaveData.currentSpawnPointId = string.Empty;
        _puzzleStages.Clear();
        _consumedWorldObjectIds.Clear();

        if (InventoryManager.Instance != null)
        {
            InventoryManager.Instance.ClearInventory();
        }

        if (deleteLocalFile)
        {
            try
            {
                if (File.Exists(SavePath))
                    File.Delete(SavePath);

                TryDeleteTempFile();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GameStateManager] Không thể xóa file save cục bộ: {e.Message}");
            }
        }
    }

    private void InitializeCleanSaveState()
    {
        currentSaveData = new GameSaveData();
        _puzzleStages.Clear();
        _consumedWorldObjectIds.Clear();

        if (InventoryManager.Instance != null)
        {
            InventoryManager.Instance.ImportSaveData(currentSaveData.inventory);
        }
    }

    private void EnsureSaveDataIntegrity(GameSaveData data)
    {
        if (data == null)
        {
            currentSaveData = new GameSaveData();
            data = currentSaveData;
        }

        if (data.saveVersion <= 0) data.saveVersion = CurrentSaveVersion;
        if (string.IsNullOrWhiteSpace(data.chapterId)) data.chapterId = DefaultChapterId;
        if (string.IsNullOrWhiteSpace(data.playerName)) data.playerName = DefaultPlayerName;
        if (data.currentDay <= 0) data.currentDay = 1;
        if (data.currentRoomId == null) data.currentRoomId = string.Empty;
        if (data.currentSpawnPointId == null) data.currentSpawnPointId = string.Empty;
        if (data.puzzleStates == null) data.puzzleStates = new List<PuzzleState>();
        if (data.puzzleStages == null) data.puzzleStages = new List<PuzzleStageEntry>();
        if (data.consumedWorldObjectIds == null) data.consumedWorldObjectIds = new List<string>();
        if (data.inventory == null) data.inventory = new List<InventorySlotData>();
    }

    private void TryDeleteTempFile()
    {
        try
        {
            if (File.Exists(TempSavePath))
                File.Delete(TempSavePath);
        }
        catch
        {
            // Bỏ qua lỗi dọn file tạm để không che khuất lỗi gốc
        }
    }
}