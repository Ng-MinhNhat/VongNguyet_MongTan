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

    private void Update()
    {
        // Nếu người chơi đang ở gần, bấm phím E, và hệ thống thoại đang không bận rộn thì bật thoại
       if (!isPlayerInRange || !Input.GetKeyDown(interactKey)) return;

        // Trường hợp 1: Đối tượng DialogueManager trong Scene chưa gắn script hoặc đang bị tắt (Inactive)
        if (DialogueManager.Instance == null)
        {
            Debug.LogError("[DialogueTrigger] LỖI: DialogueManager.Instance đang bị NULL! Hãy kiểm tra đối tượng DialogueManager trong Hierarchy.");
            return;
        }

        // Trường hợp 2: Hệ thống đang bị kẹt ở trạng thái đang mở hội thoại
        if (DialogueManager.Instance.IsDialogueActive)
        {
            Debug.LogWarning("[DialogueTrigger] Bị chặn vì DialogueManager.IsDialogueActive đang là TRUE.");
            return;
        }

        Debug.Log($"[DialogueTrigger] Đã nhận phím '{interactKey}' -> Gọi hội thoại: {sequenceID}");
        PlayDialogue();
    }

    // Bắt sự kiện khi có vật thể chạm vào vùng Trigger
    private void OnTriggerEnter2D(Collider2D collision)
    {
        // Kiểm tra xem vật chạm vào có phải là Nhân vật chính (Player) không
        if (collision.CompareTag("Player"))
        {
            isPlayerInRange = true;
            Debug.Log("[DialogueTrigger] Bro đã vào vùng tương tác. Bấm 'E' để nói chuyện.");
        }
    }

    // Bắt sự kiện khi vật thể rời khỏi vùng Trigger
    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            isPlayerInRange = false;
            Debug.Log("[DialogueTrigger] Bro đã rời khỏi vùng tương tác.");
        }
    }

    public void PlayDialogue()
    {
        if (dialogueFile != null)
        {
            DialogueManager.Instance.StartDialogue(dialogueFile, sequenceID);
        }
        else
        {
            Debug.LogError("Bro quên kéo file JSON kịch bản vào DialogueTrigger rồi!");
        }
    }
}