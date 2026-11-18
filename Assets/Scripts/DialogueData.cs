using System.Collections.Generic;

/// <summary>
/// Một dòng thoại. Hỗ trợ placeholder "{playerName}" trong speaker/text.
/// </summary>
[System.Serializable]
public class DialogueLine
{
    public string speaker;
    public string text;
    /// <summary>Tên sprite nằm trong Resources/Portraits/ (để trống = ẩn chân dung).</summary>
    public string portrait;
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
}

/// <summary>
/// Gốc của file JSON hội thoại: { "sequences": [ ... ] }.
/// </summary>
[System.Serializable]
public class DialogueDatabase
{
    public List<DialogueSequence> sequences = new List<DialogueSequence>();
}

