using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Vật thể câu đố mở khóa bằng mật mã số trên Scene (VD: Tủ hồ sơ, Cửa số, Két mật mã).
/// Hỗ trợ cả phím tương tác PC (E) khi đứng trong vùng Trigger lẫn bấm chuột / chạm trực tiếp (Mobile).
/// Chống Exploit nhận thưởng lặp, chống Phantom Trigger khi chuyển phòng và tối ưu GC.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class KeypadInteractable : MonoBehaviour
{
    [Header("Định danh & Cấu hình Mật mã")]
    [Tooltip("Mã duy nhất của câu đố lưu trong GameStateManager (VD: Ch1_HanhLang_TuHoSo).")]
    [SerializeField] private string puzzleId = "Ch1_HanhLang_TuHoSo";

    [Tooltip("Tiêu đề hiển thị ở đầu bảng nhập mật mã (VD: TỦ HỒ SƠ).")]
    [SerializeField] private string keypadTitle = "TỦ HỒ SƠ";

    [Tooltip("Chuỗi mật mã chính xác để mở khóa (VD: 1408).")]
    [SerializeField] private string correctCode = "1408";

    [Tooltip("Số ký tự tối đa được phép nhập. Để <= 0 sẽ tự động lấy theo độ dài của correctCode.")]
    [SerializeField] private int maxDigits = 0;

    [Tooltip("Nếu bật, bảng số sẽ tự kiểm tra ngay khi người chơi nhập đủ ký tự mà không cần bấm Xác nhận.")]
    [SerializeField] private bool autoSubmitWhenFull = false;

    [Tooltip("Tên trạng thái chuỗi ghi vào puzzleStages của GameStateManager khi giải thành công (mặc định: Solved).")]
    [SerializeField] private string solvedStageName = "Solved";

    [Header("Cài đặt tương tác (PC & Mobile)")]
    [Tooltip("Phím tương tác khi nhân vật đứng trong vùng Trigger trên PC.")]
    [SerializeField] private KeyCode interactKey = KeyCode.E;

    [Tooltip("Cho phép người chơi click chuột (PC) hoặc chạm trực tiếp (Mobile) lên Collider2D của vật thể.")]
    [SerializeField] private bool allowDirectClickOrTouch = true;

    [Header("Phần thưởng khi giải đúng (Tùy chọn)")]
    [Tooltip("ScriptableObject ItemData cộng ngay vào Kho đồ khi mở khóa thành công. Để trống nếu không có thưởng.")]
    [SerializeField] private ItemData rewardItem;

    [Tooltip("Số lượng vật phẩm thưởng cộng vào InventoryManager (tối thiểu 1).")]
    [SerializeField] private int rewardQuantity = 1;

    [Tooltip("Nếu bật, tự động gọi GameStateManager.SaveGameProgress() ngay sau khi mở khóa và trao thưởng.")]
    [SerializeField] private bool autoSaveOnSolved = true;

    [Header("Sự kiện phản hồi (SFX / Animation / Đổi Sprite / Hội thoại)")]
    [Tooltip("Chạy đúng 1 lần ngay khi người chơi nhập đúng mật mã mở khóa.")]
    [SerializeField] private UnityEvent onSolved;

    [Tooltip("Chạy khi người chơi tương tác lại vào ổ khóa đã được giải trước đó.")]
    [SerializeField] private UnityEvent onAlreadySolved;

    [Tooltip("Tùy chọn: chạy khi người chơi đóng bảng nhập mật mã mà chưa giải được.")]
    [SerializeField] private UnityEvent onClosedWithoutSolving;

    // Cache sẵn Component và callback ở Awake() — tuyệt đối không gọi Find()/GetComponent() trong Update()
    private Collider2D _collider2D;
    private Camera _cachedMainCamera;
    private Action _cachedSolvedCallback;
    private Action _cachedClosedCallback;

    private bool _isPlayerInRange;
    private bool _isSolvedLocal;
    private bool _isProcessingInteraction; // Khóa chống spam-click / gọi lồng trong cùng frame

    private void Awake()
    {
        _collider2D = GetComponent<Collider2D>();
        _cachedMainCamera = Camera.main;

        // Cache delegate 1 lần duy nhất để truyền vào KeypadUIController.Open() mà không sinh rác GC
        _cachedSolvedCallback = HandleSolved;
        _cachedClosedCallback = HandleClosedWithoutSolving;
    }

    private void OnDisable()
    {
        // Chống Phantom Trigger khi tắt RoomNode lúc nhân vật đang đứng trong vùng Trigger
        _isPlayerInRange = false;
        _isProcessingInteraction = false;
    }

    private void Update()
    {
        // 1. Tương tác bằng phím PC khi nhân vật đang đứng trong vùng Trigger
#if ENABLE_LEGACY_INPUT_MANAGER
        if (_isPlayerInRange && Input.GetKeyDown(interactKey))
        {
            TryInteract();
            return;
        }
#endif

        // 2. Tương tác bằng click chuột (PC) hoặc chạm trực tiếp (Mobile) lên Collider2D
        if (!allowDirectClickOrTouch || _collider2D == null || !_collider2D.enabled)
        {
            return;
        }

        if (!TryGetPointerDownScreenPosition(out Vector2 screenPosition, out int pointerId))
        {
            return;
        }

        // Chặn bấm/chạm xuyên qua UI (HUD, Popup Kho đồ, bảng Keypad, khung thoại)
        if (IsPointerOverUI(pointerId))
        {
            return;
        }

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

        // Dùng Collider2D.OverlapPoint thay vì Physics2D.Raycast để không bị CinemachineConfiner2D che chắn
        if (_collider2D.OverlapPoint(worldPoint))
        {
            TryInteract();
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            _isPlayerInRange = true;
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            _isPlayerInRange = false;
        }
    }

    /// <summary>Hàm bọc void tiện gán trực tiếp vào UnityEvent trên Inspector nếu cần.</summary>
    public void Interact()
    {
        TryInteract();
    }

    /// <summary>
    /// Kiểm tra điều kiện hợp lệ và mở bảng nhập mật mã KeypadUIController.
    /// Trả về false nếu bị chặn xung đột UI hoặc ổ khóa đã được giải từ trước.
    /// </summary>
    public bool TryInteract()
    {
        if (_isProcessingInteraction)
        {
            return false;
        }

        // Chặn mở nếu thiếu KeypadUIController hoặc bảng Keypad đang mở sẵn
        if (KeypadUIController.Instance == null || KeypadUIController.Instance.IsOpen)
        {
            return false;
        }

        // Chặn xung đột khi đang chuyển phòng, đang hội thoại hoặc đang mở Popup Kho đồ
        if (RoomTransitionManager.Instance != null && RoomTransitionManager.Instance.IsTransitioning)
        {
            return false;
        }

        if (DialogueManager.Instance != null && DialogueManager.Instance.IsDialogueActive)
        {
            return false;
        }

        if (InventoryUIController.Instance != null && InventoryUIController.Instance.IsPopupOpen)
        {
            return false;
        }

        _isProcessingInteraction = true;
        try
        {
            // Kiểm tra trạng thái đã giải trong GameStateManager -> không cho mở lại bảng số
            if (IsPuzzleSolved())
            {
                _isSolvedLocal = true;
                onAlreadySolved?.Invoke();
                return false;
            }

            KeypadSessionData session = new KeypadSessionData
            {
                title = keypadTitle,
                correctCode = correctCode,
                maxDigits = maxDigits,
                autoSubmitWhenFull = autoSubmitWhenFull
            };

            return KeypadUIController.Instance.Open(session, _cachedSolvedCallback, _cachedClosedCallback);
        }
        finally
        {
            _isProcessingInteraction = false;
        }
    }

    // ================= XỬ LÝ CALLBACK KHI GIẢI ĐÚNG / ĐÓNG BẢNG =================

    private void HandleSolved()
    {
        // Chống Exploit nhận thưởng 2 lần: xác thực lại một lần nữa trước khi đánh dấu và cộng đồ
        if (IsPuzzleSolved())
        {
            _isSolvedLocal = true;
            return;
        }

        // Bước 1: Khóa cờ cục bộ & ghi nhận đã giải vào GameStateManager (cả bool và puzzleStages)
        _isSolvedLocal = true;
        if (GameStateManager.Instance != null && !string.IsNullOrEmpty(puzzleId))
        {
            GameStateManager.Instance.SetPuzzleState(puzzleId, true);
            if (!string.IsNullOrEmpty(solvedStageName))
            {
                GameStateManager.Instance.SetPuzzleState(puzzleId, solvedStageName);
            }
        }

        // Bước 2: Trao vật phẩm thưởng (nếu có cấu hình)
        if (rewardItem != null && !string.IsNullOrEmpty(rewardItem.itemId) && InventoryManager.Instance != null)
        {
            int safeQuantity = Mathf.Max(1, rewardQuantity);
            InventoryManager.Instance.AddItem(rewardItem.itemId, safeQuantity);
        }

        // Bước 3: Tự động lưu tiến độ để tránh mất đồng bộ giữa trạng thái mở khóa và phần thưởng
        if (autoSaveOnSolved && GameStateManager.Instance != null)
        {
            GameStateManager.Instance.SaveGameProgress();
        }

        // Bước 4: Phát sự kiện phản hồi trên Inspector
        onSolved?.Invoke();
    }

    private void HandleClosedWithoutSolving()
    {
        onClosedWithoutSolving?.Invoke();
    }

    private bool IsPuzzleSolved()
    {
        if (_isSolvedLocal)
        {
            return true;
        }

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
        // Ưu tiên kiểm tra cảm ứng Mobile (Android)
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

    private static bool IsPointerOverUI(int activeTouchPointerId)
    {
        if (EventSystem.current == null)
        {
            return false;
        }

        if (activeTouchPointerId >= 0 && EventSystem.current.IsPointerOverGameObject(activeTouchPointerId))
        {
            return true;
        }

        return EventSystem.current.IsPointerOverGameObject();
    }
}
