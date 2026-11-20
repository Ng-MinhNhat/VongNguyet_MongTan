using UnityEngine;

[RequireComponent(typeof(SpriteRenderer), typeof(Collider2D))]
public class WorldItemPickup : MonoBehaviour
{
    [Header("Định danh chống hồi sinh (Anti-Respawn)")]
    [Tooltip("ID duy nhất của vật thể này trên bản đồ (VD: ch1_world_key_ketsat)")]
    [SerializeField] private string worldObjectId = "ch1_world_key_ketsat";

    [Header("Dữ liệu vật phẩm nhận được")]
    [Tooltip("Kéo tệp ScriptableObject Ch1_Key_KetSat vào đây")]
    [SerializeField] private ItemData itemData;

    [Header("Điều kiện xuất hiện (Để trống nếu luôn hiện sẵn từ đầu)")]
    [SerializeField] private string requiredPuzzleId = "Ch1_KetSat_MoKhoa";
    [SerializeField] private string requiredPuzzleState = "KeyDropped";

    [Header("Tương tác PC")]
    [SerializeField] private KeyCode interactKey = KeyCode.E;

    // Cache sẵn component ở Awake để tuyệt đối không gọi GetComponent() trong Update()
    private SpriteRenderer _spriteRenderer;
    private Collider2D _collider;

    private bool _isVisible;
    private bool _isPlayerInRange;
    private bool _isPickedUp; // Chặn lỗi nhặt đúp (Double-pickup exploit)

    private void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _collider = GetComponent<Collider2D>();
        _collider.isTrigger = true;
    }

    private void Start()
    {
        if (GameStateManager.Instance != null)
        {
            // Đăng ký lắng nghe sự kiện đổi trạng thái câu đố
            GameStateManager.Instance.OnPuzzleStateChanged += HandlePuzzleStateChanged;
        }

        RefreshVisibility();
    }

    private void OnDestroy()
    {
        // Hủy đăng ký sự kiện khi đổi Scene để chống rò rỉ bộ nhớ (Memory Leak)
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnPuzzleStateChanged -= HandlePuzzleStateChanged;
        }
    }

    private void Update()
    {
        // Thoát sớm để không tốn tài nguyên khi vật phẩm đang ẩn hoặc nhân vật ở xa
        if (!_isVisible || !_isPlayerInRange || _isPickedUp) return;

        if (Input.GetKeyDown(interactKey))
        {
            TryPickup();
        }
    }

    // Hỗ trợ tương tác Point-and-Click (Click chuột PC / Chạm trên Mobile)
    private void OnMouseDown()
    {
        if (!_isVisible || _isPickedUp) return;
        TryPickup();
    }

    private void HandlePuzzleStateChanged(string puzzleId, string newState)
    {
        // Chỉ phản ứng đúng câu đố được chỉ định
        if (puzzleId == requiredPuzzleId)
        {
            RefreshVisibility();
        }
    }

    private void RefreshVisibility()
    {
        // 1. Nếu vật thể này đã từng bị nhặt trước đó -> ẩn vĩnh viễn
        if (GameStateManager.Instance != null && GameStateManager.Instance.IsObjectConsumed(worldObjectId))
        {
            _isPickedUp = true;
            SetVisible(false);
            return;
        }

        // 2. Nếu không yêu cầu điều kiện câu đố -> hiện luôn
        if (string.IsNullOrEmpty(requiredPuzzleId))
        {
            SetVisible(true);
            return;
        }

        // 3. So khớp trạng thái hiện tại trong GameStateManager
        string currentState = GameStateManager.Instance != null
            ? GameStateManager.Instance.GetPuzzleStateString(requiredPuzzleId)
            : string.Empty;

        SetVisible(currentState == requiredPuzzleState);
    }

    private void SetVisible(bool visible)
    {
        _isVisible = visible;
        _spriteRenderer.enabled = visible;
        _collider.enabled = visible;
    }

    private void TryPickup()
    {
        // Không cho phép nhặt xuyên qua hộp thoại khi đang đọc Cutscene
        if (DialogueManager.Instance != null && DialogueManager.Instance.IsDialogueActive) return;
        if (itemData == null)
        {
            Debug.LogError($"[WorldItemPickup] Chưa gán ItemData trên '{gameObject.name}'.");
            return;
        }

        _isPickedUp = true;
        SetVisible(false);

        // Lưu vào InventoryManager và đánh dấu đã nhặt vĩnh viễn trong GameStateManager
        if (InventoryManager.Instance != null)
        {
            // Kiểm tra khớp tên/tham số hàm AddItem trong InventoryManager.cs của Bro (string itemId hoặc ItemData)
            InventoryManager.Instance.AddItem(itemData.itemId);
        }

        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.MarkObjectConsumed(worldObjectId);
        }

        Debug.Log($"[WorldItemPickup] Đã nhặt vật phẩm: {itemData.displayName} ({itemData.itemId})");
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player")) _isPlayerInRange = true;
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.CompareTag("Player")) _isPlayerInRange = false;
    }
}