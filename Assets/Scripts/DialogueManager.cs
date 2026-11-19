using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VongNguyet.VN;

/// <summary>
/// Điều khiển hội thoại Visual Novel đọc từ JSON (Singleton).
/// Tương thích 100% với cấu trúc JSON hiện tại (Scene01..Scene06) và tự động tích hợp
/// sân khấu nhân vật đứng toàn thân (Main bên trái, Vân Nguyệt bên phải) kèm chuyển biểu cảm mượt.
/// </summary>
public class DialogueManager : MonoBehaviour
{
    public static DialogueManager Instance { get; private set; }

    // Sự kiện để các hệ thống khác (Audio, Camera, Cutscene, Quest...) lắng nghe
    public event Action<string> OnDialogueStarted;
    public event Action OnDialogueEnded;

    [Header("Giao diện hội thoại (UI)")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text dialogueText;
    [SerializeField] private Image portraitImage;
    [SerializeField] private GameObject choicesPanel;

    [Tooltip("Kéo đối tượng chứa toàn bộ background khung thoại vào đây")]
    [SerializeField] private GameObject dialoguePanel;

    [Header("Sân khấu Nhân vật Visual Novel (Tùy chọn)")]
    [Tooltip("Kéo VNCharacterDisplay vị trí Trái (mặc định hiển thị Main)")]
    [SerializeField] private VNCharacterDisplay leftCharacterDisplay;

    [Tooltip("Kéo VNCharacterDisplay vị trí Phải (mặc định hiển thị Vân Nguyệt)")]
    [SerializeField] private VNCharacterDisplay rightCharacterDisplay;

    [Tooltip("Đối tượng cha chứa cả 2 nhân vật đứng (tự bật/tắt khi vào/thoát hội thoại nếu có gán)")]
    [SerializeField] private GameObject stageRoot;

    [Header("Lựa chọn (Choices)")]
    [SerializeField] private Button choiceButtonPrefab;

    [Header("Hiệu ứng gõ chữ (Typewriter)")]
    [SerializeField] private float typeSpeed = 0.03f;

    private const string PlayerNameToken = "{playerName}";

    private DialogueDatabase database;
    private DialogueSequence currentSequence;
    private int lineIndex;
    private Coroutine typingCoroutine;
    private bool isTyping;
    private string fullLineText;
    private int dialogueStartedFrame = -1;

    public bool IsDialogueActive { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        if (dialoguePanel != null) dialoguePanel.SetActive(false);
        if (stageRoot != null) stageRoot.SetActive(false);
        if (leftCharacterDisplay != null) leftCharacterDisplay.Hide(0f);
        if (rightCharacterDisplay != null) rightCharacterDisplay.Hide(0f);
    }

    private void Update()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (!IsDialogueActive) return;

        // Tránh lỡ skip dòng đầu tiên ở đúng frame vừa bấm mở hội thoại
        if (Time.frameCount == dialogueStartedFrame) return;

        if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
        {
            AdvanceDialogue();
        }
#endif
    }

    // ---------------- Bắt đầu hội thoại ----------------

    public void StartDialogue(TextAsset jsonFile, string sequenceID)
    {
        if (jsonFile == null)
        {
            Debug.LogError("[DialogueManager] jsonFile bị null.");
            return;
        }

        database = JsonUtility.FromJson<DialogueDatabase>(jsonFile.text);
        if (database == null || database.sequences == null)
        {
            Debug.LogError($"[DialogueManager] Không parse được JSON: {jsonFile.name}");
            return;
        }

        // Xóa sân khấu cũ trước khi mở cuộc hội thoại mới
        if (leftCharacterDisplay != null) leftCharacterDisplay.Hide(0f);
        if (rightCharacterDisplay != null) rightCharacterDisplay.Hide(0f);

        PlaySequence(sequenceID);
    }

    private void PlaySequence(string sequenceID)
    {
        DialogueSequence found = database.sequences.Find(s => s.dialogueID == sequenceID);
        if (found == null)
        {
            Debug.LogError($"[DialogueManager] Không tìm thấy dialogueID '{sequenceID}'.");
            EndDialogue();
            return;
        }

        currentSequence = found;
        lineIndex = 0;
        IsDialogueActive = true;
        dialogueStartedFrame = Time.frameCount;

        if (dialoguePanel != null) dialoguePanel.SetActive(true);
        if (stageRoot != null) stageRoot.SetActive(true);

        // Khóa di chuyển khi bắt đầu hội thoại
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.ChangeState(GameState.Cutscene);
        }

        if (choicesPanel != null) choicesPanel.SetActive(false);

        OnDialogueStarted?.Invoke(sequenceID);

        if (currentSequence.lines == null || currentSequence.lines.Count == 0)
        {
            ShowChoicesOrEnd();
            return;
        }

        ShowLine(currentSequence.lines[lineIndex]);
    }

    // ---------------- Hiển thị dòng thoại & sân khấu ----------------

    private void ShowLine(DialogueLine line)
    {
        if (nameText != null) nameText.text = ReplacePlayerName(line.speaker);

        SetPortrait(line.portrait);
        UpdateVisualNovelStage(line);

        fullLineText = ReplacePlayerName(line.text);

        if (typingCoroutine != null) StopCoroutine(typingCoroutine);
        typingCoroutine = StartCoroutine(TypeText(fullLineText));
    }

    private void UpdateVisualNovelStage(DialogueLine line)
    {
        if (leftCharacterDisplay == null && rightCharacterDisplay == null) return;

        if (line.clearStage)
        {
            if (leftCharacterDisplay != null) leftCharacterDisplay.Hide();
            if (rightCharacterDisplay != null) rightCharacterDisplay.Hide();
            return;
        }

        bool hasExplicitLeft = !string.IsNullOrEmpty(line.leftCharacter);
        bool hasExplicitRight = !string.IsNullOrEmpty(line.rightCharacter);

        // 1. Xử lý chỉ định thủ công từ JSON (nếu có)
        if (hasExplicitLeft && leftCharacterDisplay != null)
        {
            if (line.leftCharacter.Equals("hide", StringComparison.OrdinalIgnoreCase))
                leftCharacterDisplay.Hide();
            else
                leftCharacterDisplay.ShowSprite(VNCharacterLibrary.LoadFullBody(line.leftCharacter));
        }

        if (hasExplicitRight && rightCharacterDisplay != null)
        {
            if (line.rightCharacter.Equals("hide", StringComparison.OrdinalIgnoreCase))
                rightCharacterDisplay.Hide();
            else
                rightCharacterDisplay.ShowSprite(VNCharacterLibrary.LoadFullBody(line.rightCharacter));
        }

        // 2. Tự động suy diễn từ line.portrait (tương thích 100% với toàn bộ Scene01..06.json)
        string folder = VNCharacterLibrary.GetCharacterFolder(line.portrait);

        if (!hasExplicitLeft && folder == "Main" && leftCharacterDisplay != null)
        {
            Sprite full = VNCharacterLibrary.LoadFullBody(line.portrait);
            if (full != null) leftCharacterDisplay.ShowSprite(full);
        }
        else if (!hasExplicitRight && folder == "VanNguyet" && rightCharacterDisplay != null)
        {
            Sprite full = VNCharacterLibrary.LoadFullBody(line.portrait);
            if (full != null) rightCharacterDisplay.ShowSprite(full);
        }

        // 3. Làm sáng nhân vật đang nói, làm tối nhẹ nhân vật đang nghe
        ApplySpeakerHighlight(line.activeSide, folder);
    }

    private void ApplySpeakerHighlight(string explicitSide, string portraitFolder)
    {
        string side = explicitSide;
        if (string.IsNullOrEmpty(side))
        {
            if (portraitFolder == "Main") side = "left";
            else if (portraitFolder == "VanNguyet") side = "right";
            else side = "none"; // Người dẫn chuyện hoặc NPC (Ông lão, Bà quản gia, Hoa, Cô bé)
        }

        bool leftSpeaking = side.Equals("left", StringComparison.OrdinalIgnoreCase)
                            || side.Equals("both", StringComparison.OrdinalIgnoreCase);
        bool rightSpeaking = side.Equals("right", StringComparison.OrdinalIgnoreCase)
                             || side.Equals("both", StringComparison.OrdinalIgnoreCase);

        if (leftCharacterDisplay != null) leftCharacterDisplay.SetSpeaking(leftSpeaking);
        if (rightCharacterDisplay != null) rightCharacterDisplay.SetSpeaking(rightSpeaking);
    }

    private IEnumerator TypeText(string text)
    {
        if (dialogueText == null)
        {
            Debug.LogError("[DialogueManager] Chưa gán dialogueText trên Inspector.");
            yield break;
        }

        isTyping = true;
        dialogueText.text = text;
        dialogueText.maxVisibleCharacters = 0;

        dialogueText.ForceMeshUpdate();
        int total = dialogueText.textInfo.characterCount;

        for (int i = 0; i <= total; i++)
        {
            dialogueText.maxVisibleCharacters = i;
            yield return new WaitForSeconds(typeSpeed);
        }

        FinishTyping();
    }

    private void FinishTyping()
    {
        isTyping = false;
        typingCoroutine = null;
        if (dialogueText != null) dialogueText.maxVisibleCharacters = int.MaxValue;
    }

    public void AdvanceDialogue()
    {
        if (!IsDialogueActive || currentSequence == null) return;
        if (choicesPanel != null && choicesPanel.activeSelf) return;

        if (isTyping)
        {
            if (typingCoroutine != null) StopCoroutine(typingCoroutine);
            if (dialogueText != null) dialogueText.text = fullLineText;
            FinishTyping();
            return;
        }

        lineIndex++;
        if (lineIndex < currentSequence.lines.Count)
        {
            ShowLine(currentSequence.lines[lineIndex]);
        }
        else
        {
            ShowChoicesOrEnd();
        }
    }

    // ---------------- Lựa chọn ----------------

    private void ShowChoicesOrEnd()
    {
        if (choicesPanel == null || choiceButtonPrefab == null
            || currentSequence.choices == null || currentSequence.choices.Count == 0)
        {
            FinishCurrentSequenceAndEnd();
            return;
        }

        foreach (Transform child in choicesPanel.transform)
        {
            Destroy(child.gameObject);
        }

        int shown = 0;
        foreach (DialogueChoice choice in currentSequence.choices)
        {
            if (!string.IsNullOrEmpty(choice.requiredPuzzleState)
                && (GameStateManager.Instance == null
                    || !GameStateManager.Instance.GetPuzzleState(choice.requiredPuzzleState)))
            {
                continue;
            }

            Button button = Instantiate(choiceButtonPrefab, choicesPanel.transform);
            TMP_Text label = button.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = ReplacePlayerName(choice.choiceText);

            DialogueChoice captured = choice;
            button.onClick.AddListener(() => OnChoiceSelected(captured));
            shown++;
        }

        if (shown == 0)
        {
            FinishCurrentSequenceAndEnd();
            return;
        }

        choicesPanel.SetActive(true);
    }

    private void OnChoiceSelected(DialogueChoice choice)
    {
        if (!string.IsNullOrEmpty(choice.setPuzzleState) && GameStateManager.Instance != null)
        {
            GameStateManager.Instance.TriggerPuzzleState(choice.setPuzzleState);
        }

        if (choicesPanel != null) choicesPanel.SetActive(false);

        if (string.IsNullOrEmpty(choice.nextDialogueID))
        {
            FinishCurrentSequenceAndEnd();
        }
        else
        {
            PlaySequence(choice.nextDialogueID);
        }
    }

    private void FinishCurrentSequenceAndEnd()
    {
        string endState = currentSequence != null ? currentSequence.setPuzzleStateOnEnd : null;
        string nextScene = currentSequence != null ? currentSequence.nextSceneName : null;

        if (!string.IsNullOrEmpty(endState) && GameStateManager.Instance != null)
        {
            GameStateManager.Instance.TriggerPuzzleState(endState);
        }

        EndDialogue();

        if (!string.IsNullOrEmpty(nextScene))
        {
            SceneManager.LoadScene(nextScene);
        }
    }

    public void EndDialogue()
    {
        if (typingCoroutine != null) StopCoroutine(typingCoroutine);
        typingCoroutine = null;
        isTyping = false;
        IsDialogueActive = false;
        currentSequence = null;

        if (choicesPanel != null) choicesPanel.SetActive(false);
        if (dialogueText != null) dialogueText.text = string.Empty;
        if (nameText != null) nameText.text = string.Empty;
        if (portraitImage != null) portraitImage.gameObject.SetActive(false);
        if (leftCharacterDisplay != null) leftCharacterDisplay.Hide(0f);
        if (rightCharacterDisplay != null) rightCharacterDisplay.Hide(0f);
        if (stageRoot != null) stageRoot.SetActive(false);
        if (dialoguePanel != null) dialoguePanel.SetActive(false);

        // Trả lại quyền điều khiển cho người chơi
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.ChangeState(GameState.FreeRoam);
        }

        OnDialogueEnded?.Invoke();
    }

    // ---------------- Tiện ích ----------------

    private string ReplacePlayerName(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        string playerName = "Main";
        if (GameStateManager.Instance != null && GameStateManager.Instance.currentData != null)
        {
            playerName = GameStateManager.Instance.currentData.playerName;
        }

        return input.Replace(PlayerNameToken, playerName);
    }

    private void SetPortrait(string portraitName)
    {
        if (portraitImage == null) return;

        if (string.IsNullOrEmpty(portraitName))
        {
            portraitImage.gameObject.SetActive(false);
            return;
        }

        Sprite sprite = VNCharacterLibrary.LoadPortrait(portraitName);
        if (sprite == null)
        {
            Debug.LogWarning($"[DialogueManager] Không tìm thấy portrait cho key '{portraitName}'.");
            portraitImage.gameObject.SetActive(false);
            return;
        }

        portraitImage.sprite = sprite;
        portraitImage.preserveAspect = true;
        portraitImage.gameObject.SetActive(true);
    }
}
