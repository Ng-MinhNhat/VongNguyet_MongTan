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
/// Quản lý trạng thái toàn cục của game (túi đồ, câu đố, rẽ nhánh, lưu/tải, khóa di chuyển).
/// Singleton + DontDestroyOnLoad.
/// </summary>
public class GameStateManager : MonoBehaviour
{
    public static GameStateManager Instance { get; private set; }

    // --- CÁC KÊNH PHÁT SÓNG SỰ KIỆN ---
    public event Action OnInventoryChanged;
    public event Action<GameState> OnGameStateChanged; // Phát sóng khi đổi trạng thái (Mới)
    public event Action<string> OnPuzzleStateTriggered; // Phát sóng cờ cốt truyện (Mới)

    private const string SaveFileName = "vongnguyet_save.json";

    public GameSaveData Data { get; private set; } = new GameSaveData();
    public GameSaveData currentData => Data;
    
    // Lưu trữ trạng thái hiện tại của game (Mới)
    public GameState CurrentState { get; private set; } 

    private string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        // Mặc định vào game là cho phép đi lại tự do
        ChangeState(GameState.FreeRoam);
    }

    // ================= QUẢN LÝ TRẠNG THÁI (MỚI) =================

    public void ChangeState(GameState newState)
    {
        CurrentState = newState;
        OnGameStateChanged?.Invoke(newState); // Báo cho PlayerController khóa/mở di chuyển
    }

    public void TriggerPuzzleState(string stateName)
    {
        if (!string.IsNullOrEmpty(stateName))
        {
            Debug.Log($"[GameStateManager] Kích hoạt cờ cốt truyện: {stateName}");
            OnPuzzleStateTriggered?.Invoke(stateName);
            
            // Tự động lưu cờ này vào Data luôn để ghi nhớ
            SetPuzzleState(stateName, true);
        }
    }

    // ================= TÚI ĐỒ =================

    public void AddItem(string itemID)
    {
        if (string.IsNullOrEmpty(itemID) || HasItem(itemID)) return;

        Data.inventoryItems.Add(itemID);
        OnInventoryChanged?.Invoke();
    }

    public void RemoveItem(string itemID)
    {
        if (string.IsNullOrEmpty(itemID)) return;

        if (Data.inventoryItems.Remove(itemID))
        {
            OnInventoryChanged?.Invoke();
        }
    }

    public bool HasItem(string itemID)
    {
        return !string.IsNullOrEmpty(itemID) && Data.inventoryItems.Contains(itemID);
    }

    // ================= CÂU ĐỐ & RẼ NHÁNH =================

    public void SetPuzzleState(string puzzleID, bool state)
    {
        if (string.IsNullOrEmpty(puzzleID)) return;

        foreach (PuzzleState p in Data.puzzleStates)
        {
            if (p.puzzleID == puzzleID)
            {
                p.state = state;
                return;
            }
        }

        Data.puzzleStates.Add(new PuzzleState(puzzleID, state));
    }

    public bool GetPuzzleState(string puzzleID)
    {
        foreach (PuzzleState p in Data.puzzleStates)
        {
            if (p.puzzleID == puzzleID) return p.state;
        }

        return false;
    }

    // ================= NGƯỜI CHƠI =================

    public void SetPlayerName(string name)
    {
        Data.playerName = string.IsNullOrWhiteSpace(name) ? "Main" : name.Trim();
    }

    // ================= LƯU / TẢI =================

    public void SaveGameProgress()
    {
        try
        {
            Data.currentScene = SceneManager.GetActiveScene().name;
            string json = JsonUtility.ToJson(Data, true);
            File.WriteAllText(SavePath, json);
        }
        catch (Exception e)
        {
            Debug.LogError($"[GameStateManager] Lưu game thất bại: {e.Message}");
        }
    }

    public void LoadLocalGame()
    {
        try
        {
            if (!File.Exists(SavePath))
            {
                Debug.LogWarning("[GameStateManager] Không tìm thấy file save.");
                return;
            }

            string json = File.ReadAllText(SavePath);
            GameSaveData loaded = JsonUtility.FromJson<GameSaveData>(json);
            if (loaded == null) return;

            if (loaded.inventoryItems == null) loaded.inventoryItems = new List<string>();
            if (loaded.puzzleStates == null) loaded.puzzleStates = new List<PuzzleState>();

            Data = loaded;
            OnInventoryChanged?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogError($"[GameStateManager] Tải game thất bại: {e.Message}");
        }
    }
    // ================= CÂU ĐỐ ĐA TRẠNG THÁI & VẬT THỂ ĐÃ NHẶT =================

    // Lưu trạng thái câu đố nhiều giai đoạn: Key = puzzleId, Value = state (VD: "KeyDropped", "Solved")
    private readonly Dictionary<string, string> _puzzleStages = new Dictionary<string, string>();

    // Lưu ID các vật thể đã nhặt để chặn lỗi hồi sinh vật phẩm (Item Respawn) khi tải lại Scene
    private readonly HashSet<string> _consumedObjectIds = new HashSet<string>();

    // Sự kiện (Event) phát sóng cho các vật thể ngoài bản đồ tự lắng nghe mà không cần Update()
    public event Action<string, string> OnPuzzleStateChanged;

    /// <summary>Cập nhật trạng thái câu đố dạng chuỗi (Overload - Nạp chồng của bản bool cũ).</summary>
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

    /// <summary>Đọc trạng thái câu đố dạng chuỗi (Đổi tên để tránh lỗi trùng chữ ký với hàm bool GetPuzzleState cũ).</summary>
    public string GetPuzzleStateString(string puzzleId)
    {
        return _puzzleStages.TryGetValue(puzzleId, out string state) ? state : string.Empty;
    }

    /// <summary>Đánh dấu vật thể ngoài bản đồ đã bị nhặt/tiêu thụ vĩnh viễn.</summary>
    public void MarkObjectConsumed(string objectId)
    {
        if (!string.IsNullOrEmpty(objectId))
            _consumedObjectIds.Add(objectId);
    }

    public bool IsObjectConsumed(string objectId)
    {
        return !string.IsNullOrEmpty(objectId) && _consumedObjectIds.Contains(objectId);
    }
}