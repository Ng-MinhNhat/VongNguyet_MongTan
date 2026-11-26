using System.Collections.Generic;

/// <summary>
/// Trạng thái của một câu đố / biến rẽ nhánh (true = đã hoàn thành / được kích hoạt).
/// </summary>
[System.Serializable]
public class PuzzleState
{
    public string puzzleID;
    public bool state;

    public PuzzleState(string puzzleID, bool state)
    {
        this.puzzleID = puzzleID;
        this.state = state;
    }
}

[System.Serializable]
public class PuzzleStageEntry
{
    public string puzzleId;
    public string stage;

    public PuzzleStageEntry(string puzzleId, string stage)
    {
        this.puzzleId = puzzleId;
        this.stage = stage;
    }
}

/// <summary>
/// Dữ liệu lưu game, được serialize bằng JsonUtility.
/// </summary>
[System.Serializable]
public class GameSaveData
{
    // ---- Trạng thái câu đố cốt lõi ----
    public const string Puzzle_01_PowerOn = "Puzzle_01_PowerOn";
    public const string Puzzle_02_SafeOpened = "Puzzle_02_SafeOpened";
    public const string Puzzle_03_GirlCured = "Puzzle_03_GirlCured";
    public const string Puzzle_04_RadioFixed = "Puzzle_04_RadioFixed";

    // ---- Biến rẽ nhánh ----
    public const string Branch_Helped_Little_Girl = "Branch_Helped_Little_Girl";

    // ---- Dữ liệu lưu ----
    public int saveVersion = 1;
    public string chapterId = "Ch1";
    public string playerName = "Main";
    public int currentDay = 1;
    public string currentScene;
    public string currentRoomId = "";
    public string currentSpawnPointId = "";
    public List<InventorySlotData> inventory = new List<InventorySlotData>();
    public List<PuzzleState> puzzleStates = new List<PuzzleState>();
    // Dùng List<PuzzleStageEntry> thay cho Dictionary vì JsonUtility không hỗ trợ serialize Dictionary
    public List<PuzzleStageEntry> puzzleStages = new List<PuzzleStageEntry>();
    public List<string> consumedWorldObjectIds = new List<string>();
}