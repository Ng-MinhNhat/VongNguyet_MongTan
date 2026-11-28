using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Điều khiển bảng Soi manh mối (Inspect Modal) theo chuẩn uGUI + TextMeshPro.
/// Hỗ trợ phóng to ảnh manh mối giữ nguyên tỉ lệ (preserveAspect = true), đóng bằng nút / chạm nền / phím Escape,
/// và đồng bộ ưu tiên phím Escape tầng 1 (Inspect -> Combine -> Inventory -> Pause).
/// </summary>
public class ItemInspectUIController : MonoBehaviour
{
    // Singleton gọn nhẹ theo vòng đời Scene hiện tại (KHÔNG dùng DontDestroyOnLoad)
    public static ItemInspectUIController Instance { get; private set; }

    /// <summary>Trạng thái bảng Soi manh mối hiện đang mở hay đóng.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Frame gần nhất bảng Soi vừa đóng (chống xung đột phím Escape cùng frame với Kho đồ và Pause Menu).</summary>
    public int LastClosedFrame { get; private set; } = -1;

    [Header("Thành phần hiển thị uGUI & TextMeshPro")]
    [Tooltip("Panel gốc chứa toàn bộ giao diện Soi manh mối (ẩn mặc định khi khởi động; đặt tách khỏi GameObject gắn script này).")]
    [SerializeField] private GameObject panelRoot;

    [Tooltip("Văn bản TextMeshPro hiển thị tên vật phẩm đang soi.")]
    [SerializeField] private TMP_Text itemNameText;

    [Tooltip("Ảnh phóng to độ phân giải cao của vật phẩm (tự động ép preserveAspect = true để tránh méo tỉ lệ ảnh thư/báo).")]
    [SerializeField] private Image inspectImage;

    [Tooltip("Văn bản TextMeshPro hiển thị nội dung chữ / manh mối chi tiết khi soi.")]
    [SerializeField] private TMP_Text descriptionText;

    [Header("Nút đóng (PC & Mobile)")]
    [Tooltip("Nút Đóng rõ ràng trên bảng Soi cho người chơi Mobile và PC.")]
    [SerializeField] private Button closeButton;

    [Tooltip("Tùy chọn: nút trong suốt phủ nền tối bên ngoài khung để chạm ra ngoài là đóng nhanh.")]
    [SerializeField] private Button backdropCloseButton;

    private bool _areListenersRegistered;

    private void Awake()
    {
        // Chặn trùng lặp Singleton trong cùng một Scene
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[ItemInspectUIController] Phát hiện ItemInspectUIController trùng lặp trong Scene, tự động hủy bản thừa.");
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // Chống méo tỉ lệ ảnh manh mối (thư, báo, ảnh bé gái...) ngay từ lúc khởi tạo
        if (inspectImage != null)
        {
            inspectImage.preserveAspect = true;
        }

        RegisterButtonListeners();

        IsOpen = false;
        SetGameObjectActiveSafe(panelRoot, false);
    }

    private void OnDisable()
    {
        IsOpen = false;
        SetGameObjectActiveSafe(panelRoot, false);
    }

    private void OnDestroy()
    {
        UnregisterButtonListeners();

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        // Chỉ chạy khi bảng Soi đang mở; ưu tiên tầng 1 của chuỗi Esc (Inspect -> Combine -> Inventory -> Pause)
        if (!IsOpen)
        {
            return;
        }

        if (PauseMenuController.Instance != null && PauseMenuController.Instance.IsPaused)
        {
            return;
        }

        if (WasEscapeKeyPressed())
        {
            Close();
        }
    }

    // ================= API CÔNG KHAI (OPEN / CLOSE) =================

    /// <summary>
    /// Mở bảng Soi manh mối cho một ItemData có IsInspectable == true.
    /// Chặn mở khi đang chuyển phòng, đang Pause hoặc đang mở bảng Keypad.
    /// </summary>
    public bool Open(ItemData item)
    {
        if (item == null || !item.IsInspectable || IsOpen)
        {
            return false;
        }

        if (RoomTransitionManager.Instance != null && RoomTransitionManager.Instance.IsTransitioning)
        {
            return false;
        }

        if (PauseMenuController.Instance != null && PauseMenuController.Instance.IsPaused)
        {
            return false;
        }

        if (KeypadUIController.Instance != null && KeypadUIController.Instance.IsOpen)
        {
            return false;
        }

        if (itemNameText != null)
        {
            itemNameText.text = item.displayName ?? string.Empty;
        }

        if (inspectImage != null)
        {
            Sprite sprite = item.InspectSprite;
            bool hasSprite = sprite != null;

            inspectImage.sprite = sprite;
            // BẮT BUỘC giữ preserveAspect = true để ảnh manh mối không bao giờ bị kéo méo tỉ lệ
            inspectImage.preserveAspect = true;
            inspectImage.enabled = hasSprite;
            SetGameObjectActiveSafe(inspectImage.gameObject, hasSprite);
        }

        if (descriptionText != null)
        {
            descriptionText.text = item.InspectDescription ?? string.Empty;
        }

        SetGameObjectActiveSafe(panelRoot, true);
        IsOpen = true;

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySfx("SFX_UI_Click");
        }

        return true;
    }

    /// <summary>
    /// Đóng bảng Soi manh mối và ghi nhận LastClosedFrame để tránh đóng dây chuyền Kho đồ / mở Pause cùng frame.
    /// </summary>
    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySfx("SFX_UI_Click");
        }

        IsOpen = false;
        // Ghi dấu frame vừa đóng để InventoryUIController và PauseMenuController bỏ qua phím Escape ở đúng frame này
        LastClosedFrame = Time.frameCount;
        SetGameObjectActiveSafe(panelRoot, false);
    }

    // ================= ĐĂNG KÝ LISTENER & INPUT =================

    private void RegisterButtonListeners()
    {
        if (_areListenersRegistered)
        {
            return;
        }

        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (backdropCloseButton != null) backdropCloseButton.onClick.AddListener(Close);

        _areListenersRegistered = true;
    }

    private void UnregisterButtonListeners()
    {
        if (!_areListenersRegistered)
        {
            return;
        }

        if (closeButton != null) closeButton.onClick.RemoveListener(Close);
        if (backdropCloseButton != null) backdropCloseButton.onClick.RemoveListener(Close);

        _areListenersRegistered = false;
    }

    private static bool WasEscapeKeyPressed()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.Escape);
#else
        return false;
#endif
    }

    private static void SetGameObjectActiveSafe(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
        {
            target.SetActive(active);
        }
    }
}
