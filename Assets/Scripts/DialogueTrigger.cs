using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))] // Đảm bảo luôn có vùng va chạm
public class DialogueTrigger : MonoBehaviour
{
    [Header("Dữ liệu Hội thoại")]
    [Tooltip("Kéo file JSON kịch bản (VD: Scene04_Ngay2_3.json) vào ô này")]
    [SerializeField] private TextAsset dialogueFile;

    [Tooltip("ID của đoạn thoại muốn gọi (VD: manh_moi_anh_be_gai)")]
    [SerializeField] private string sequenceID = "intro_01";
    
    [Header("Cài đặt tương tác")]
    [Tooltip("Phím dùng để tương tác khi đứng gần")]
    [SerializeField] private KeyCode interactKey = KeyCode.E;

    // Biến để biết nhân vật có đang đứng trong vùng tương tác hay không
    private bool isPlayerInRange = false;

    // Cache sẵn ItemLockPuzzle ở Awake() để tuyệt đối không gọi GetComponent() trong Update()
    private ItemLockPuzzle _itemLockPuzzle;

    private void Awake()
    {
        _itemLockPuzzle = GetComponent<ItemLockPuzzle>();
    }

    private void OnDisable()
    {
        // Edge Case chuyển phòng: khi RoomNode tắt GameObject lúc người chơi đứng trong vùng Trigger,
        // Unity không gọi OnTriggerExit2D -> phải reset cờ để tránh kẹt trạng thái khi bật lại phòng
        isPlayerInRange = false;
    }

    private void Update()
    {
        // Thoát sớm khi người chơi đứng ngoài vùng tương tác
        if (!isPlayerInRange) return;

        // Edge Case: chặn lọt phím tương tác (E) khi đang Pause, chuyển phòng, mở Popup Kho đồ, mở Keypad hoặc đang Soi manh mối
        if (PauseMenuController.Instance != null && PauseMenuController.Instance.IsPaused) return;
        if (RoomTransitionManager.Instance != null && RoomTransitionManager.Instance.IsTransitioning) return;
        if (InventoryUIController.Instance != null && InventoryUIController.Instance.IsPopupOpen) return;
        if (KeypadUIController.Instance != null && KeypadUIController.Instance.IsOpen) return;
        if (ItemInspectUIController.Instance != null && ItemInspectUIController.Instance.IsOpen) return;

#if ENABLE_LEGACY_INPUT_MANAGER
        if (!Input.GetKeyDown(interactKey)) return;
#else
        return;
#endif

        // Nếu vật thể có gắn ItemLockPuzzle (VD: Két Sắt) -> nhường quyền xử lý chính cho câu đố
        if (_itemLockPuzzle != null)
        {
            _itemLockPuzzle.TryInteract();
            return;
        }

        // Đối tượng hội thoại thông thường: giữ nguyên luồng kích hoạt đoạn thoại mặc định
        PlayDialogue();
    }

    // Bắt sự kiện khi có vật thể chạm vào vùng Trigger
    private void OnTriggerEnter2D(Collider2D collision)
    {
        // Kiểm tra xem vật chạm vào có phải là Nhân vật chính (Player) không
        if (collision.CompareTag("Player"))
        {
            isPlayerInRange = true;
            Debug.Log("[DialogueTrigger] Đã vào vùng tương tác. Bấm 'E' để tương tác.");
        }
    }

    // Bắt sự kiện khi vật thể rời khỏi vùng Trigger
    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            isPlayerInRange = false;
            Debug.Log("[DialogueTrigger] Đã rời khỏi vùng tương tác.");
        }
    }

    /// <summary>Gọi đoạn thoại mặc định theo trường sequenceID cấu hình trên Inspector.</summary>
    public void PlayDialogue()
    {
        PlaySequence(sequenceID);
    }

    /// <summary>
    /// Gọi bất kỳ nhánh thoại nào trong cùng file dialogueFile.
    /// Có thể kéo thả trực tiếp vào các UnityEvent của ItemLockPuzzle trên Inspector (truyền ID nhánh thoại dạng chuỗi).
    /// </summary>
    public void PlaySequence(string overrideSequenceID)
    {
        // Edge Case: chặn kích hoạt hội thoại mới khi đang Pause, đang Fade chuyển phòng hoặc bảng Keypad đang mở
        if (PauseMenuController.Instance != null && PauseMenuController.Instance.IsPaused)
        {
            return;
        }

        if (RoomTransitionManager.Instance != null && RoomTransitionManager.Instance.IsTransitioning)
        {
            return;
        }

        if (KeypadUIController.Instance != null && KeypadUIController.Instance.IsOpen)
        {
            return;
        }

        if (ItemInspectUIController.Instance != null && ItemInspectUIController.Instance.IsOpen)
        {
            return;
        }

        // Kiểm tra an toàn tránh NullReferenceException và chặn bật chồng khi hội thoại đang chạy
        if (DialogueManager.Instance == null)
        {
            Debug.LogError("[DialogueTrigger] LỖI: DialogueManager.Instance đang bị NULL! Hãy kiểm tra đối tượng DialogueManager trong Hierarchy.");
            return;
        }

        if (DialogueManager.Instance.IsDialogueActive)
        {
            Debug.LogWarning("[DialogueTrigger] Bị chặn vì DialogueManager.IsDialogueActive đang là TRUE.");
            return;
        }

        if (dialogueFile == null)
        {
            Debug.LogError($"[DialogueTrigger] Chưa gán file JSON kịch bản (dialogueFile) trên '{gameObject.name}'.");
            return;
        }

        if (string.IsNullOrEmpty(overrideSequenceID))
        {
            Debug.LogWarning($"[DialogueTrigger] overrideSequenceID đang rỗng trên '{gameObject.name}'.");
            return;
        }

        Debug.Log($"[DialogueTrigger] Gọi nhánh hội thoại: '{overrideSequenceID}' từ '{dialogueFile.name}'.");
        DialogueManager.Instance.StartDialogue(dialogueFile, overrideSequenceID);
    }
}