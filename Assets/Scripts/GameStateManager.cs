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
}