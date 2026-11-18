using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Điều khiển hội thoại đọc từ JSON (Singleton).
/// Gắn các nút "tiếp tục" của UI vào <see cref="AdvanceDialogue"/>.
/// </summary>
public class DialogueManager : MonoBehaviour
{
    public static DialogueManager Instance { get; private set; }

    [Header("Giao diện hội thoại (UI)")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text dialogueText;
    [SerializeField] private Image portraitImage;
    [SerializeField] private GameObject choicesPanel;

    [Header("Lựa chọn (Choices)")]
    [SerializeField] private Button choiceButtonPrefab;

    [Header("Hiệu ứng gõ chữ (Typewriter)")]
    [SerializeField] private float typeSpeed = 0.03f;

    private const string PlayerNameToken = "{playerName}";
    private const string PortraitResourceFolder = "Portraits/";

    private DialogueDatabase database;
    private DialogueSequence currentSequence;
    private int lineIndex;
    private Coroutine typingCoroutine;
    private bool isTyping;
    private string fullLineText;

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

    private void Update()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (IsDialogueActive && Input.GetMouseButtonDown(0))
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

        // [CẤY CODE 1] Bật đèn đỏ: Khóa di chuyển khi bắt đầu hội thoại
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.ChangeState(GameState.Cutscene);
        }

        if (choicesPanel != null) choicesPanel.SetActive(false);

        if (currentSequence.lines == null || currentSequence.lines.Count == 0)
        {
            ShowChoicesOrEnd();
            return;
        }

        ShowLine(currentSequence.lines[lineIndex]);
    }

    // ---------------- Hiển thị dòng thoại ----------------

    private void ShowLine(DialogueLine line)
    {
        if (nameText != null) nameText.text = ReplacePlayerName(line.speaker);
        SetPortrait(line.portrait);

        fullLineText = ReplacePlayerName(line.text);

        if (typingCoroutine != null) StopCoroutine(typingCoroutine);
        typingCoroutine = StartCoroutine(TypeText(fullLineText));
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
            EndDialogue();
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
            EndDialogue();
            return;
        }

        choicesPanel.SetActive(true);
    }

    private void OnChoiceSelected(DialogueChoice choice)
    {
        // [CẤY CODE 3] Báo cho GameStateManager kích hoạt cờ sự kiện và phát sóng Event
        if (!string.IsNullOrEmpty(choice.setPuzzleState) && GameStateManager.Instance != null)
        {
            GameStateManager.Instance.TriggerPuzzleState(choice.setPuzzleState);
        }

        if (choicesPanel != null) choicesPanel.SetActive(false);

        if (string.IsNullOrEmpty(choice.nextDialogueID))
        {
            EndDialogue();
        }
        else
        {
            PlaySequence(choice.nextDialogueID);
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

        // [CẤY CODE 2] Bật đèn xanh: Trả lại quyền điều khiển cho người chơi
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.ChangeState(GameState.FreeRoam);
        }
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

        Sprite sprite = Resources.Load<Sprite>(PortraitResourceFolder + portraitName);
        if (sprite == null)
        {
            Debug.LogWarning($"[DialogueManager] Không tìm thấy portrait: Resources/{PortraitResourceFolder}{portraitName}");
            portraitImage.gameObject.SetActive(false);
            return;
        }

        portraitImage.sprite = sprite;
        portraitImage.gameObject.SetActive(true);
    }
}