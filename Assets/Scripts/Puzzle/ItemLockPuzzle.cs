using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Component câu đố mở khóa bằng vật phẩm (tái sử dụng cho Két Sắt và mọi ổ khóa/vật thể nhiều Chapter).
/// Hỗ trợ gọi trực tiếp qua TryInteract() hoặc bấm/chạm vào Collider2D (tương thích Input System PC & Mobile).
/// Đảm bảo chống Softlock, chống giải đúp (Idempotency) và khôi phục sạch trạng thái khi tải Save.
/// </summary>
public class ItemLockPuzzle : MonoBehaviour
{
    [Header("Định danh & Điều kiện mở khóa")]
    [Tooltip("Mã định danh duy nhất của câu đố lưu trong GameStateManager (VD: Ch1_KetSat_MoKhoa).")]
    [SerializeField] private string puzzleId = "Ch1_KetSat_MoKhoa";

    [Tooltip("Mã itemId người chơi bắt buộc phải đang cầm trên tay để mở khóa (VD: ch1_key_ketsat).")]
    [SerializeField] private string requiredItemId = "ch1_key_ketsat";

    [Tooltip("Có trừ 1 vật phẩm sau khi mở thành công hay không. Tắt (false) với vật phẩm dùng nhiều lần như đèn pin/xà beng.")]
    [SerializeField] private bool consumeItemOnSuccess = true;

    [Tooltip("Tên trạng thái chuỗi ghi vào puzzleStages của GameStateManager khi giải thành công (mặc định: Solved).")]
    [SerializeField] private string solvedStageName = "Solved";

    [Header("Phần thưởng khi mở khóa (Tùy chọn)")]
    [Tooltip("ScriptableObject ItemData tặng ngay cho người chơi khi mở thành công. Để trống nếu không có phần thưởng.")]
    [SerializeField] private ItemData rewardItem;

    [Tooltip("Số lượng vật phẩm thưởng cộng vào InventoryManager khi giải thành công.")]
    [SerializeField] private int rewardQuantity = 1;

    [Header("Tương tác bấm / chạm trực tiếp")]
    [Tooltip("Bật để cho phép người chơi click chuột (PC) hoặc chạm màn hình (Mobile) trực tiếp lên Collider2D của vật thể.")]
    [SerializeField] private bool allowDirectClickOrTouch = true;

    [Header("Sự kiện phản hồi (Gắn SFX / Animation / Đổi Sprite / Hội thoại)")]
    [Tooltip("Chạy đúng 1 lần ngay thời điểm mở khóa thành công (phát âm thanh mở khóa, chạy animation, bật thoại mới...).")]
    [SerializeField] private UnityEvent onSolved;

    [Tooltip("Chạy trong Start() khi tải Save mà câu đố đã giải từ trước (chỉ đổi sẵn hình két mở, KHÔNG phát tiếng hay cộng lại đồ).")]
    [SerializeField] private UnityEvent onRestoredSolvedState;

    [Tooltip("Chạy khi người chơi bấm lại vào vật thể đã được mở khóa trước đó.")]
    [SerializeField] private UnityEvent onAlreadySolved;

    [Tooltip("Chạy khi người chơi tương tác nhưng chưa cầm vật phẩm nào trên tay.")]
    [SerializeField] private UnityEvent onNoItemEquipped;

    [Tooltip("Chạy khi người chơi đang cầm sai vật phẩm (hoặc vật phẩm không còn hợp lệ trong kho).")]
    [SerializeField] private UnityEvent onWrongItemEquipped;

    /// <summary>Trạng thái câu đố đã được giải hay chưa.</summary>
    public bool IsSolved => _isSolved;

    /// <summary>Mã câu đố đang cấu hình trên Inspector.</summary>
    public string PuzzleId => puzzleId;

    /// <summary>Mã vật phẩm yêu cầu để mở khóa.</summary>
    public string RequiredItemId => requiredItemId;

    // Cache Component ở Awake để tuyệt đối không gọi GetComponent()/Find() trong Update()
    private Collider2D _collider2D;
    private Camera _cachedMainCamera;
    private GameStateManager _subscribedGameStateManager;

    private bool _isSolved;
    private bool _isProcessingInteraction; // Khóa chống gọi lồng / spam-click trong cùng frame

    private void Awake()
    {
        _collider2D = GetComponent<Collider2D>();
        _cachedMainCamera = Camera.main;
    }

    private void OnEnable()
    {
        TrySubscribeGameState();
    }

    private void Start()
    {
        // Edge Case Execution Order: đảm bảo đăng ký được cả khi OnEnable chạy trước GameStateManager.Awake
        TrySubscribeGameState();

        // Khôi phục trạng thái đã giải từ file Save hoặc khi chuyển quay lại Scene cũ
        TryRestoreSolvedStateFromGameState();
    }

    private void OnDisable()
    {
        // Hủy đăng ký sự kiện khi tắt object / chuyển Scene để chống Memory Leak
        UnsubscribeGameState();
    }

    private void OnDestroy()
    {
        UnsubscribeGameState();
    }

    private void Update()
    {
        // Thoát sớm để không tốn tài nguyên nếu tắt tương tác trực tiếp hoặc object không có Collider2D đang bật
        if (!allowDirectClickOrTouch || _collider2D == null || !_collider2D.enabled)
        {
            return;
        }

        // Không cấp phát rác GC trong Update(): chỉ kiểm tra frame có phát sinh click chuột / chạm màn hình
        if (!TryGetPointerDownScreenPosition(out Vector2 screenPosition, out int pointerId))
        {
            return;
        }

        // Chặn bấm xuyên UI ngay trước khi chiếu tọa độ xuống thế giới 2D
        if (IsPointerBlockedByUI(pointerId))
        {
            return;
        }

        // Kiểm tra camera dự phòng nếu MainCamera đổi khi chuyển Scene (chỉ chạy đúng frame người chơi bấm)
        if (_cachedMainCamera == null)
        {
            _cachedMainCamera = Camera.main;
            if (_cachedMainCamera == null)
            {
                return;
            }
        }

        float cameraDepth = Mathf.Abs(_cachedMainCamera.transform.position.z - transform.position.z);
        Vector3 worldPoint = _cachedMainCamera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, cameraDepth));

        // Dùng Collider2D.OverlapPoint thay cho Physics2D.Raycast để không bị Cinemachine Confiner2D hoặc Trigger nền che khuất
        if (_collider2D.OverlapPoint(worldPoint))
        {
            TryInteract();
        }
    }

    // ================= TƯƠNG TÁC CHÍNH =================

    /// <summary>
    /// Hàm bọc không trả về giá trị để dễ gắn trực tiếp vào UnityEvent / Button trên Inspector.
    /// </summary>
    public void Interact()
    {
        TryInteract();
    }

    /// <summary>
    /// Xử lý tương tác mở khóa bằng vật phẩm đang cầm.
    /// Có thể gọi từ bấm/chạm trực tiếp lên Collider2D hoặc từ hệ thống tương tác nhân vật.
    /// </summary>
    public bool TryInteract()
    {
        // Chống gọi lồng (re-entrancy) / spam chạm liên tục trong cùng một lượt xử lý
        if (_isProcessingInteraction)
        {
            return false;
        }

        // Chặn xung đột UI/Hội thoại: không cho mở khóa khi Popup túi đồ đang mở, đang hội thoại hoặc chạm trúng UI
        if (InventoryUIController.Instance != null && InventoryUIController.Instance.IsPopupOpen)
        {
            return false;
        }

        if (DialogueManager.Instance != null && DialogueManager.Instance.IsDialogueActive)
        {
            return false;
        }

        if (IsPointerBlockedByUI(-1))
        {
            return false;
        }

        _isProcessingInteraction = true;
        try
        {
            // Chống giải đúp / Exploit cộng thưởng lặp lại (Idempotency)
            if (_isSolved)
            {
                onAlreadySolved?.Invoke();
                return true;
            }

            // Kiểm tra đồng bộ lại với GameStateManager đề phòng trạng thái vừa được cập nhật từ hệ thống khác
            if (IsSolvedInGameState())
            {
                _isSolved = true;
                onAlreadySolved?.Invoke();
                return true;
            }

            // Lấy ID vật phẩm người chơi đang cầm từ InventoryUIController
            string equippedId = InventoryUIController.Instance != null
                ? InventoryUIController.Instance.EquippedItemId
                : string.Empty;

            if (string.IsNullOrEmpty(equippedId))
            {
                onNoItemEquipped?.Invoke();
                return false;
            }

            // Chống Exploit: vừa so khớp ID đang cầm (không phân biệt hoa thường) vừa xác thực chéo tồn kho thực tế trong InventoryManager
            bool isMatchingItem = !string.IsNullOrEmpty(requiredItemId)
                                  && string.Equals(equippedId, requiredItemId, StringComparison.OrdinalIgnoreCase);
            bool hasItemInInventory = InventoryManager.Instance != null
                                      && InventoryManager.Instance.HasItem(requiredItemId);

            if (!isMatchingItem || !hasItemInInventory)
            {
                onWrongItemEquipped?.Invoke();
                return false;
            }

            ExecuteSolveSuccess();
            return true;
        }
        finally
        {
            _isProcessingInteraction = false;
        }
    }

    // ================= LUỒNG XỬ LÝ CHỐNG SOFTLOCK =================

    private void ExecuteSolveSuccess()
    {
        // Bước 1 (Chống Softlock & Giải đúp): Khóa cờ nội bộ và ghi nhận trạng thái đã giải TRƯỚC khi trừ chìa khóa
        _isSolved = true;

        if (GameStateManager.Instance != null && !string.IsNullOrEmpty(puzzleId))
        {
            // Đồng bộ cả cờ bool (cho hệ thống lựa chọn hội thoại) lẫn chuỗi giai đoạn trong puzzleStages
            GameStateManager.Instance.SetPuzzleState(puzzleId, true);

            if (!string.IsNullOrEmpty(solvedStageName))
            {
                GameStateManager.Instance.SetPuzzleState(puzzleId, solvedStageName);
            }
        }

        // Bước 2 (Chống Softlock): Cộng vật phẩm thưởng trước khi tiêu thụ chìa khóa hoặc phát sự kiện ngoài
        if (rewardItem != null && !string.IsNullOrEmpty(rewardItem.itemId) && InventoryManager.Instance != null)
        {
            int safeQuantity = Mathf.Max(1, rewardQuantity);
            InventoryManager.Instance.AddItem(rewardItem.itemId, safeQuantity);
        }

        // Bước 3 (Chống Softlock): Chỉ tiêu thụ KeyItem SAU KHI đã xác nhận mở khóa và ghi nhận trạng thái thành công
        if (consumeItemOnSuccess && InventoryManager.Instance != null && !string.IsNullOrEmpty(requiredItemId))
        {
            InventoryManager.Instance.RemoveItem(requiredItemId, 1);
        }

        // Bước 4: Bỏ cầm vật phẩm trên HUD góc màn hình (dùng != null đúng chuẩn UnityEngine.Object)
        if (InventoryUIController.Instance != null)
        {
            InventoryUIController.Instance.ClearEquippedItem();
        }

        // Bước 5 (Chống Softlock/Exploit khi thoát game đột ngột): Lưu tiến trình ngay sau khi kho đồ và trạng thái câu đố đã khớp
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.SaveGameProgress();
        }

        // Bước 6: Phát UnityEvent cuối cùng (SFX, Animation, đổi Sprite, kích hoạt hội thoại...)
        onSolved?.Invoke();
    }

    // ================= KHÔI PHỤC TRẠNG THÁI TỪ SAVE =================

    private void TrySubscribeGameState()
    {
        if (_subscribedGameStateManager == null && GameStateManager.Instance != null)
        {
            _subscribedGameStateManager = GameStateManager.Instance;
            _subscribedGameStateManager.OnPuzzleStateChanged += HandlePuzzleStateChanged;
        }
    }

    private void UnsubscribeGameState()
    {
        if (_subscribedGameStateManager != null)
        {
            _subscribedGameStateManager.OnPuzzleStateChanged -= HandlePuzzleStateChanged;
            _subscribedGameStateManager = null;
        }
    }

    private void HandlePuzzleStateChanged(string changedPuzzleId, string newStage)
    {
        // Edge Case: nếu LoadLocalGame() chạy sau Start() của câu đố, tự động khôi phục trạng thái đã mở
        if (_isSolved || string.IsNullOrEmpty(puzzleId))
        {
            return;
        }

        if (string.Equals(changedPuzzleId, puzzleId, StringComparison.Ordinal)
            && !string.IsNullOrEmpty(solvedStageName)
            && string.Equals(newStage, solvedStageName, StringComparison.OrdinalIgnoreCase))
        {
            _isSolved = true;
            onRestoredSolvedState?.Invoke();
        }
    }

    private void TryRestoreSolvedStateFromGameState()
    {
        if (_isSolved)
        {
            return;
        }

        if (IsSolvedInGameState())
        {
            _isSolved = true;
            // Chỉ phát sự kiện khôi phục hình ảnh (không cộng thưởng lại, không phát SFX mở khóa)
            onRestoredSolvedState?.Invoke();
        }
    }

    private bool IsSolvedInGameState()
    {
        if (GameStateManager.Instance == null || string.IsNullOrEmpty(puzzleId))
        {
            return false;
        }

        if (GameStateManager.Instance.GetPuzzleState(puzzleId))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(solvedStageName))
        {
            string currentStage = GameStateManager.Instance.GetPuzzleStateString(puzzleId);
            if (string.Equals(currentStage, solvedStageName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // ================= INPUT SYSTEM & CHẶN BẤM XUYÊN UI =================

    private static bool TryGetPointerDownScreenPosition(out Vector2 screenPosition, out int pointerId)
    {
        screenPosition = Vector2.zero;
        pointerId = -1;

#if ENABLE_INPUT_SYSTEM
        // Ưu tiên kiểm tra cảm ứng Mobile (Android) trước
        Touchscreen touchscreen = Touchscreen.current;
        if (touchscreen != null)
        {
            var touches = touchscreen.touches;
            for (int i = 0; i < touches.Count; i++)
            {
                if (touches[i].press.wasPressedThisFrame)
                {
                    screenPosition = touches[i].position.ReadValue();
                    pointerId = touches[i].touchId.ReadValue();
                    return true;
                }
            }
        }

        // Kiểm tra chuột trái trên PC
        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            screenPosition = mouse.position.ReadValue();
            pointerId = -1;
            return true;
        }

        return false;
#elif ENABLE_LEGACY_INPUT_MANAGER
        if (Input.touchCount > 0)
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (touch.phase == TouchPhase.Began)
                {
                    screenPosition = touch.position;
                    pointerId = touch.fingerId;
                    return true;
                }
            }
        }

        if (Input.GetMouseButtonDown(0))
        {
            screenPosition = Input.mousePosition;
            pointerId = -1;
            return true;
        }

        return false;
#else
        return false;
#endif
    }

    private static bool IsPointerBlockedByUI(int activeTouchPointerId)
    {
        if (EventSystem.current == null)
        {
            return false;
        }

        // Nếu có touchId cụ thể vừa chạm (Android) -> kiểm tra chính xác ngón tay đó
        if (activeTouchPointerId >= 0 && EventSystem.current.IsPointerOverGameObject(activeTouchPointerId))
        {
            return true;
        }

        // PC / Editor: kiểm tra con trỏ chuột
        if (EventSystem.current.IsPointerOverGameObject())
        {
            return true;
        }

#if ENABLE_INPUT_SYSTEM
        // Quét bổ sung các điểm chạm đang hoạt động khi TryInteract() được gọi từ bên ngoài (không truyền touchId)
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
#elif ENABLE_LEGACY_INPUT_MANAGER
        if (Input.touchCount > 0)
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                if (EventSystem.current.IsPointerOverGameObject(Input.GetTouch(i).fingerId))
                {
                    return true;
                }
            }
        }
#endif

        return false;
    }
}
