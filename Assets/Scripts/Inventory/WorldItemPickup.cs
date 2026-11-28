using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(SpriteRenderer), typeof(Collider2D))]
public class WorldItemPickup : MonoBehaviour
{
    [Header("Định danh chống hồi sinh (Anti-Respawn)")]
    [Tooltip("ID duy nhất của vật thể này trên bản đồ (VD: ch1_world_key_ketsat). Nếu để trống sẽ tự lấy tên GameObject làm ID dự phòng.")]
    [SerializeField] private string worldObjectId;

    [Header("Dữ liệu vật phẩm nhận được")]
    [Tooltip("Kéo tệp ScriptableObject ItemData (VD: Ch1_Key_KetSat) vào đây.")]
    [SerializeField] private ItemData itemData;

    [Header("Điều kiện xuất hiện (Để trống nếu luôn hiện sẵn từ đầu)")]
    [Tooltip("Mã câu đố cần theo dõi trong GameStateManager (để trống nếu vật phẩm luôn hiện sẵn).")]
    [SerializeField] private string requiredPuzzleId = "Ch1_KetSat_MoKhoa";

    [Tooltip("Trạng thái câu đố cần đạt để vật phẩm xuất hiện (VD: KeyDropped).")]
    [SerializeField] private string requiredPuzzleState = "KeyDropped";

    [Header("Tương tác PC")]
    [Tooltip("Phím tương tác để nhặt vật phẩm khi nhân vật đứng trong vùng Trigger trên PC.")]
    [SerializeField] private KeyCode interactKey = KeyCode.E;

    // Cache sẵn component ở Awake để tuyệt đối không gọi GetComponent() trong Update()
    private SpriteRenderer _spriteRenderer;
    private Collider2D _collider;

    private bool _isVisible;
    private bool _isPlayerInRange;
    private bool _isPickedUp = false; // Khóa trạng thái chống spam-click / nhặt đúp trên PC & Mobile
    private bool _isSubscribed;

    private void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _collider = GetComponent<Collider2D>();
        _collider.isTrigger = true;

        // Edge Case: nếu quên đặt worldObjectId trên Inspector thì dùng tên GameObject làm ID dự phòng
        if (string.IsNullOrWhiteSpace(worldObjectId))
        {
            worldObjectId = gameObject.name;
        }
    }

    private void Start()
    {
        // Chống hồi sinh vật phẩm (Anti-Respawn): ẩn ngay nếu vật thể đã bị nhặt trước đó
        if (GameStateManager.Instance != null && GameStateManager.Instance.IsObjectConsumed(worldObjectId))
        {
            _isPickedUp = true;
            gameObject.SetActive(false);
            return;
        }

        if (GameStateManager.Instance != null)
        {
            // Đăng ký lắng nghe sự kiện đổi trạng thái câu đố
            GameStateManager.Instance.OnPuzzleStateChanged += HandlePuzzleStateChanged;
            _isSubscribed = true;
        }

        RefreshVisibility();
    }

    private void OnDestroy()
    {
        // Hủy đăng ký sự kiện khi đổi Scene để chống rò rỉ bộ nhớ (Memory Leak)
        UnsubscribePuzzleEvent();
    }

    private void Update()
    {
        // Thoát sớm để không tốn tài nguyên khi vật phẩm đang ẩn, nhân vật ở xa hoặc đã nhặt
        if (!_isVisible || !_isPlayerInRange || _isPickedUp) return;

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(interactKey))
        {
            TryPickup();
        }
#endif
    }

    // Hỗ trợ tương tác Point-and-Click (Click chuột PC / Chạm trên Mobile)
    private void OnMouseDown()
    {
        if (!_isVisible || _isPickedUp) return;

        // Chặn bấm xuyên UI khi chạm vào các nút HUD / Kho đồ / Hộp thoại trên PC & Android
        if (IsPointerOverUI()) return;

        TryPickup();
    }

    /// <summary>
    /// Kiểm tra con trỏ chuột (PC) hoặc ngón tay chạm (Mobile) có đang nằm trên phần tử UI hay không.
    /// </summary>
    private bool IsPointerOverUI()
    {
        if (EventSystem.current == null)
            return false;

        // PC / Editor: kiểm tra con trỏ chuột
        if (EventSystem.current.IsPointerOverGameObject())
            return true;

#if ENABLE_LEGACY_INPUT_MANAGER
        // Edge Case Android (Legacy Input): IsPointerOverGameObject() không tham số chỉ kiểm tra chuột,
        // nên phải truyền fingerId của các điểm chạm cảm ứng đang hoạt động
        if (Input.touchCount > 0)
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                if (EventSystem.current.IsPointerOverGameObject(Input.GetTouch(i).fingerId))
                    return true;
            }
        }
#endif

#if ENABLE_INPUT_SYSTEM
        // Edge Case Android (New Input System): InputSystemUIInputModule ánh xạ theo touchId
        Touchscreen touchscreen = Touchscreen.current;
        if (touchscreen != null)
        {
            var touches = touchscreen.touches;
            for (int i = 0; i < touches.Count; i++)
            {
                if ((touches[i].press.isPressed || touches[i].press.wasPressedThisFrame)
                    && EventSystem.current.IsPointerOverGameObject(touches[i].touchId.ReadValue()))
                {
                    return true;
                }
            }
        }
#endif

        return false;
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
        // 1. Nếu vật thể đã bị tiêu thụ (kể cả khi LoadLocalGame chạy sau Start) -> tắt hẳn GameObject
        if (GameStateManager.Instance != null && GameStateManager.Instance.IsObjectConsumed(worldObjectId))
        {
            _isPickedUp = true;
            UnsubscribePuzzleEvent();
            SetVisible(false);
            gameObject.SetActive(false);
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
        // Chặn nhặt đúp / spam-click trên màn hình cảm ứng ngay dòng đầu tiên
        if (_isPickedUp || !_isVisible) return;

        // Chặn bấm xuyên UI và chặn nhặt đồ khi đang Pause, đang mở hội thoại Cutscene hoặc đang mở bảng Keypad
        if (IsPointerOverUI()) return;
        if (PauseMenuController.Instance != null && PauseMenuController.Instance.IsPaused) return;
        if (DialogueManager.Instance != null && DialogueManager.Instance.IsDialogueActive) return;
        if (KeypadUIController.Instance != null && KeypadUIController.Instance.IsOpen) return;
        if (ItemInspectUIController.Instance != null && ItemInspectUIController.Instance.IsOpen) return;

        if (itemData == null)
        {
            Debug.LogError($"[WorldItemPickup] Chưa gán ItemData trên '{gameObject.name}'.");
            return;
        }

        if (InventoryManager.Instance == null)
        {
            Debug.LogError($"[WorldItemPickup] Không tìm thấy InventoryManager.Instance khi nhặt '{gameObject.name}'.");
            return;
        }

        // Khóa cờ ngay trước khi thêm vào kho đồ để chặn gọi lồng / spam chạm trong cùng frame
        _isPickedUp = true;

        bool added = InventoryManager.Instance.AddItem(itemData.itemId);
        if (!added)
        {
            // Chống Softlock: nếu thêm vào kho thất bại thì mở khóa lại, không ẩn hay đánh dấu đã nhặt
            _isPickedUp = false;
            Debug.LogWarning($"[WorldItemPickup] Thêm vật phẩm '{itemData.itemId}' vào kho thất bại, giữ nguyên vật thể ngoài bản đồ.");
            return;
        }

        // Đánh dấu đã nhặt vĩnh viễn trong GameStateManager trước khi tắt GameObject
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.MarkObjectConsumed(worldObjectId);
        }

        // Đặt SFX_Item_Pickup tại WorldItemPickup thay vì InventoryManager.AddItem để tránh nổ loạt tiếng khi Load Save
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySfx("SFX_Item_Pickup");
        }

        Debug.Log($"[WorldItemPickup] Đã nhặt vật phẩm: {itemData.displayName} ({itemData.itemId})");

        UnsubscribePuzzleEvent();
        SetVisible(false);
        gameObject.SetActive(false);
    }

    private void UnsubscribePuzzleEvent()
    {
        if (_isSubscribed && GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnPuzzleStateChanged -= HandlePuzzleStateChanged;
        }

        _isSubscribed = false;
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