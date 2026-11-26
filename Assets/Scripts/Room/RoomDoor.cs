using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Cửa chuyển phòng giữa các RoomNode trong cùng Scene.
/// Hỗ trợ cả phím tương tác PC (E) khi đứng trong vùng Trigger lẫn bấm chuột / chạm cảm ứng trực tiếp (Mobile).
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class RoomDoor : MonoBehaviour
{
    [Header("Phòng đích & Điểm xuất hiện")]
    [Tooltip("Mã roomId của phòng đích đã đăng ký trong RoomTransitionManager (VD: Ch1_HanhLang).")]
    [SerializeField] private string targetRoomId = "Ch1_HanhLang";

    [Tooltip("Mã spawnId nơi nhân vật sẽ đứng sau khi bước sang phòng đích (VD: From_PhongNgu hoặc Default).")]
    [SerializeField] private string targetSpawnPointId = "Default";

    [Header("Cài đặt tương tác (PC & Mobile)")]
    [Tooltip("Phím tương tác khi nhân vật đứng trong vùng Trigger của cửa trên PC.")]
    [SerializeField] private KeyCode interactKey = KeyCode.E;

    [Tooltip("Cho phép người chơi click chuột (PC) hoặc chạm trực tiếp (Mobile) lên Collider2D của cửa.")]
    [SerializeField] private bool allowDirectClickOrTouch = true;

    [Header("Điều kiện mở khóa (Tùy chọn)")]
    [Tooltip("Mã puzzleId trong GameStateManager phải được giải trước mới cho đi qua. Để trống nếu cửa mở tự do.")]
    [SerializeField] private string requiredSolvedPuzzleId = string.Empty;

    [Tooltip("Tên trạng thái chuỗi xem là đã giải trong puzzleStages (mặc định: Solved).")]
    [SerializeField] private string requiredSolvedStageName = "Solved";

    [Header("Sự kiện phản hồi (SFX / Hội thoại)")]
    [Tooltip("Phát khi cửa đang khóa vì câu đố điều kiện chưa được giải (gắn thoại báo khóa hoặc SFX lắc tay nắm).")]
    [SerializeField] private UnityEvent onDoorLocked;

    [Tooltip("Phát ngay trước khi bắt đầu Fade chuyển phòng (gắn SFX mở cửa / bước chân).")]
    [SerializeField] private UnityEvent onDoorEntered;

    // Cache sẵn Component trong Awake() — tuyệt đối không gọi Find() hay GetComponent() trong Update()
    private Collider2D _collider2D;
    private Camera _cachedMainCamera;

    private bool _isPlayerInRange;
    private bool _isProcessingDoor; // Chống gọi lồng / spam-click trong cùng một frame

    private void Awake()
    {
        _collider2D = GetComponent<Collider2D>();
        _cachedMainCamera = Camera.main;
    }

    private void OnDisable()
    {
        // Edge Case chuyển phòng: khi RoomNode tắt GameObject phòng lúc nhân vật còn trong Trigger,
        // Unity không gọi OnTriggerExit2D -> bắt buộc reset cờ về false để tránh kẹt trạng thái khi quay lại
        _isPlayerInRange = false;
        _isProcessingDoor = false;
    }

    private void Update()
    {
        // 1. Tương tác bằng phím PC khi nhân vật đang đứng trong vùng Trigger của cửa
#if ENABLE_LEGACY_INPUT_MANAGER
        if (_isPlayerInRange && Input.GetKeyDown(interactKey))
        {
            TryEnterDoor();
            return;
        }
#endif

        // 2. Tương tác bằng bấm chuột (PC) hoặc chạm cảm ứng trực tiếp (Mobile) lên Collider2D
        if (!allowDirectClickOrTouch || _collider2D == null || !_collider2D.enabled)
        {
            return;
        }

        if (!TryGetPointerDownScreenPosition(out Vector2 screenPosition, out int pointerId))
        {
            return;
        }

        // Chặn bấm/chạm xuyên qua UI (HUD, Popup Kho đồ, khung thoại)
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

        // Dùng Collider2D.OverlapPoint để tránh bị CinemachineConfiner2D che mất raycast
        if (_collider2D.OverlapPoint(worldPoint))
        {
            TryEnterDoor();
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
    public void EnterDoor()
    {
        TryEnterDoor();
    }

    /// <summary>
    /// Kiểm tra điều kiện hợp lệ và kích hoạt chuyển sang phòng đích qua RoomTransitionManager.
    /// </summary>
    public bool TryEnterDoor()
    {
        if (_isProcessingDoor)
        {
            return false;
        }

        // Chặn khi thiếu RoomTransitionManager hoặc đang trong quá trình Fade chuyển phòng
        if (RoomTransitionManager.Instance == null || RoomTransitionManager.Instance.IsTransitioning)
        {
            return false;
        }

        // Chặn xung đột khi đang hội thoại hoặc đang mở Popup Kho đồ
        if (DialogueManager.Instance != null && DialogueManager.Instance.IsDialogueActive)
        {
            return false;
        }

        if (InventoryUIController.Instance != null && InventoryUIController.Instance.IsPopupOpen)
        {
            return false;
        }

        // Chặn bước qua cửa khi người chơi đang nhập mật mã trên bảng Keypad
        if (KeypadUIController.Instance != null && KeypadUIController.Instance.IsOpen)
        {
            return false;
        }

        _isProcessingDoor = true;
        try
        {
            // Kiểm tra điều kiện câu đố mở khóa cửa (nếu có cấu hình)
            if (!IsRequiredPuzzleSatisfied())
            {
                onDoorLocked?.Invoke();
                return false;
            }

            // Kiểm tra phòng đích tồn tại trước khi phát SFX mở cửa để tránh phát âm thanh giả khi sai ID
            if (!RoomTransitionManager.Instance.HasRoom(targetRoomId))
            {
                Debug.LogWarning($"[RoomDoor] Phòng đích '{targetRoomId}' trên '{gameObject.name}' chưa được đăng ký trong RoomTransitionManager.");
                return false;
            }

            onDoorEntered?.Invoke();
            return RoomTransitionManager.Instance.TransitionToRoom(targetRoomId, targetSpawnPointId);
        }
        finally
        {
            _isProcessingDoor = false;
        }
    }

    private bool IsRequiredPuzzleSatisfied()
    {
        // Cửa mở tự do nếu không yêu cầu câu đố nào
        if (string.IsNullOrEmpty(requiredSolvedPuzzleId))
        {
            return true;
        }

        if (GameStateManager.Instance == null)
        {
            return false;
        }

        if (GameStateManager.Instance.GetPuzzleState(requiredSolvedPuzzleId))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(requiredSolvedStageName))
        {
            string currentStage = GameStateManager.Instance.GetPuzzleStateString(requiredSolvedPuzzleId);
            if (string.Equals(currentStage, requiredSolvedStageName, StringComparison.OrdinalIgnoreCase))
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

        // Android: kiểm tra chính xác ngón tay vừa chạm
        if (activeTouchPointerId >= 0 && EventSystem.current.IsPointerOverGameObject(activeTouchPointerId))
        {
            return true;
        }

        // PC / Editor: kiểm tra con trỏ chuột
        return EventSystem.current.IsPointerOverGameObject();
    }
}
