using System.Collections.Generic;

/// <summary>
/// Một dòng thoại. Hỗ trợ placeholder "{playerName}" trong speaker/text.
/// Giữ tương thích 100% với các file JSON cũ (chỉ dùng speaker, text, portrait)
/// và bổ sung tùy chọn điều khiển sân khấu Visual Novel 2 nhân vật (trái/phải).
/// </summary>
[System.Serializable]

public class PuzzleStateCommand
{
    public string puzzleId; // Mã định danh duy nhất của câu đố (VD: "Ch1_KetSat_MoKhoa")
    public string state;    // Trạng thái mới (VD: "KeyDropped", "Solved")
}
[System.Serializable]
public class DialogueLine
{
    public string speaker;
    public string text;

    /// <summary>
    /// Key chân dung / biểu cảm (VD: "Main_TuTin", "Main_CuoiRunRay", "VanNguyet_LoLang", "OngLao_CuoiMaMi"...).
    /// Để trống = ẩn chân dung hộp thoại.
    /// </summary>
    public string portrait;

    // ---- Tùy chọn mở rộng cho sân khấu Visual Novel (để trống = tự động suy diễn từ portrait/speaker) ----

    /// <summary>Ép hiển thị biểu cảm cụ thể ở vị trí trái (VD: "Main_01_Neutral" hoặc "hide" để ẩn).</summary>
    public string leftCharacter;

    /// <summary>Ép hiển thị biểu cảm cụ thể ở vị trí phải (VD: "VanNguyet_01_Neutral_Smile" hoặc "hide" để ẩn).</summary>
    public string rightCharacter;

    /// <summary>Tùy chọn làm sáng bên nói: "left", "right", "both", "none" (để trống = tự động).</summary>
    public string activeSide;

    /// <summary>Nếu true: ẩn toàn bộ nhân vật đứng trên sân khấu ở dòng này (hợp cảnh dẫn chuyện / chuyển cảnh).</summary>
    public bool clearStage;
    public PuzzleStateCommand setPuzzleState; // Tự động null nếu dòng thoại JSON không có lệnh này
}

/// <summary>
/// Một lựa chọn hiển thị sau khi hết các dòng thoại.
/// </summary>
[System.Serializable]
public class DialogueChoice
{
    public string choiceText;
    /// <summary>ID đoạn hội thoại tiếp theo (để trống = kết thúc hội thoại).</summary>
    public string nextDialogueID;
    /// <summary>Nếu không rỗng: lựa chọn chỉ hiện khi puzzle/biến này đang là true.</summary>
    public string requiredPuzzleState;
    /// <summary>Nếu không rỗng: khi chọn, puzzle/biến này được đặt thành true.</summary>
    public string setPuzzleState;
}

/// <summary>
/// Một đoạn hội thoại hoàn chỉnh: các dòng thoại + các lựa chọn (có thể rỗng).
/// </summary>
[System.Serializable]
public class DialogueSequence
{
    public string dialogueID;
    public List<DialogueLine> lines = new List<DialogueLine>();
    public List<DialogueChoice> choices = new List<DialogueChoice>();

    /// <summary>Tùy chọn: tự động kích hoạt cờ cốt truyện khi kết thúc sequence (nếu không có choice).</summary>
    public string setPuzzleStateOnEnd;

    /// <summary>Tùy chọn: tên scene chuyển tới sau khi kết thúc sequence (nếu để trống thì giữ nguyên scene).</summary>
    public string nextSceneName;
}

/// <summary>
/// Gốc của file JSON hội thoại: { "sequences": [ ... ] }.
/// </summary>
[System.Serializable]
public class DialogueDatabase
{
    public List<DialogueSequence> sequences = new List<DialogueSequence>();
}
