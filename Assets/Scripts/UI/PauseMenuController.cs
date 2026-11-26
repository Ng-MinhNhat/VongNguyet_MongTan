using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Quản lý giao diện Tạm dừng (Pause Menu) theo chuẩn uGUI + TextMeshPro.
/// Chống kẹt đóng băng Time.timeScale, chống xung đột phím Escape cùng frame với Keypad/Inventory,
/// và hỗ trợ nút Pause riêng trên HUD cho màn hình cảm ứng Mobile.
/// </summary>
public class PauseMenuController : MonoBehaviour
{
    private const string SavedFeedbackMessage = "Đã lưu tiến độ!";

    // Singleton gọn nhẹ theo vòng đời Scene hiện tại (KHÔNG dùng DontDestroyOnLoad)
    public static PauseMenuController Instance { get; private set; }

    /// <summary>Trạng thái game hiện đang bị tạm dừng bởi Pause Menu hay không.</summary>
    public bool IsPaused { get; private set; }

    [Header("Thành phần giao diện Pause (uGUI & TextMeshPro)")]
    [Tooltip("Panel gốc chứa toàn bộ menu Tạm dừng (ẩn mặc định khi khởi động; đặt tách khỏi GameObject gắn script này).")]
    [SerializeField] private GameObject pausePanelRoot;

    [Tooltip("Nút Pause nhỏ luôn hiện trên góc màn hình cho Mobile; tự ẩn khi đang Pause hoặc đang chuyển phòng.")]
    [SerializeField] private Button hudPauseButton;

    [Tooltip("Nút tiếp tục chơi (Resume).")]
    [SerializeField] private Button resumeButton;

    [Tooltip("Nút lưu tiến độ thủ công ngay trong menu Tạm dừng.")]
    [SerializeField] private Button saveButton;

    [Tooltip("Nút quay về màn hình Main Menu.")]
    [SerializeField] private Button mainMenuButton;

    [Tooltip("Văn bản TextMeshPro hiển thị thông báo ngắn (VD: Đã lưu tiến độ!) khi người chơi bấm Lưu game.")]
    [SerializeField] private TMP_Text feedbackText;

    [Header("Cấu hình chuyển Scene & Phản hồi")]
    [Tooltip("Tên Scene màn hình chính trong Build Settings để quay về khi bấm Main Menu.")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    [Tooltip("Thời gian (giây thực tế unscaled) hiển thị dòng thông báo đã lưu trước khi tự ẩn.")]
    [SerializeField] private float feedbackDuration = 1.5f;

    private Coroutine _feedbackCoroutine;
    private bool _areListenersRegistered;

    private void Awake()
    {
        // Chặn trùng lặp Singleton trong cùng một Scene
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[PauseMenuController] Phát hiện PauseMenuController trùng lặp trong Scene, tự động hủy bản thừa.");
            Destroy(gameObject);
            return;
        }

        Instance = this;

        RegisterButtonListeners();
        ResetInitialState();
    }

    private void OnDisable()
    {
        StopFeedbackCoroutine();

        // Chống kẹt đóng băng game nếu object bị tắt đột ngột khi đang Pause
        if (IsPaused)
        {
            Time.timeScale = 1f;
            IsPaused = false;
        }
    }

    private void OnDestroy()
    {
        StopFeedbackCoroutine();
        UnregisterButtonListeners();

        // Chống kẹt Time.timeScale = 0 khi chuyển Scene hoặc hủy Canvas trong lúc đang Pause
        if (IsPaused)
        {
            Time.timeScale = 1f;
            IsPaused = false;
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        // Đồng bộ bật/tắt nút Pause trên HUD (ẩn khi đang Pause hoặc đang chuyển phòng Fade)
        UpdateHudPauseButtonVisibility();

        if (!WasEscapeKeyPressed())
        {
            return;
        }

        // Nếu đang Pause -> bấm Escape lần nữa để tiếp tục chơi
        if (IsPaused)
        {
            Resume();
            return;
        }

        // Chống mở đè & chống xung đột phím Escape cùng frame với KeypadUIController / InventoryUIController
        if (IsBlockedFromOpeningPause())
        {
            return;
        }

        Pause();
    }

    // ================= API CÔNG KHAI (PAUSE / RESUME / SAVE / MAIN MENU) =================

    /// <summary>Tạm dừng game, bật Pause Panel và đặt Time.timeScale = 0.</summary>
    public void Pause()
    {
        if (IsPaused || IsBlockedFromOpeningPause())
        {
            return;
        }

        StopFeedbackCoroutine();
        HideFeedbackText();

        IsPaused = true;
        SetGameObjectActiveSafe(pausePanelRoot, true);
        Time.timeScale = 0f;

        UpdateHudPauseButtonVisibility();
    }

    /// <summary>Khôi phục Time.timeScale = 1, tắt Pause Panel và trả lại quyền điều khiển cho người chơi.</summary>
    public void Resume()
    {
        StopFeedbackCoroutine();
        HideFeedbackText();

        // Luôn khôi phục Time.timeScale trước khi tắt Panel để chống kẹt đóng băng
        Time.timeScale = 1f;
        SetGameObjectActiveSafe(pausePanelRoot, false);
        IsPaused = false;

        UpdateHudPauseButtonVisibility();
    }

    /// <summary>Lưu tiến độ thủ công và hiện thông báo phản hồi bằng Time.unscaledDeltaTime (hoạt động khi timeScale = 0).</summary>
    public void SaveGameManual()
    {
        if (GameStateManager.Instance == null)
        {
            Debug.LogWarning("[PauseMenuController] Không tìm thấy GameStateManager.Instance để lưu tiến độ.");
            return;
        }

        GameStateManager.Instance.SaveGameProgress();

        if (feedbackText != null)
        {
            StopFeedbackCoroutine();
            _feedbackCoroutine = StartCoroutine(ShowFeedbackRoutine(SavedFeedbackMessage));
        }
    }

    /// <summary>Khôi phục Time.timeScale = 1 bắt buộc rồi quay về Scene Main Menu.</summary>
    public void ReturnToMainMenu()
    {
        StopFeedbackCoroutine();

        // BẮT BUỘC khôi phục Time.timeScale = 1f và IsPaused = false trước khi LoadScene để chống đóng băng ở MainMenu
        Time.timeScale = 1f;
        IsPaused = false;
        SetGameObjectActiveSafe(pausePanelRoot, false);

        if (string.IsNullOrWhiteSpace(mainMenuSceneName))
        {
            Debug.LogError("[PauseMenuController] Chưa cấu hình mainMenuSceneName trên Inspector.");
            return;
        }

        SceneManager.LoadScene(mainMenuSceneName);
    }

    // ================= KIỂM TRA XUNG ĐỘT & PHẢN HỒI ZERO-GC =================

    private bool IsBlockedFromOpeningPause()
    {
        // Chặn mở Pause khi đang Fade chuyển phòng
        if (RoomTransitionManager.Instance != null && RoomTransitionManager.Instance.IsTransitioning)
        {
            return true;
        }

        // Chống xung đột Escape cùng frame: nếu Keypad đang mở HOẶC vừa mới đóng ở đúng frame hiện tại -> bỏ qua
        if (KeypadUIController.Instance != null
            && (KeypadUIController.Instance.IsOpen || KeypadUIController.Instance.LastClosedFrame == Time.frameCount))
        {
            return true;
        }

        // Chống xung đột Escape cùng frame: nếu Popup Kho đồ đang mở HOẶC vừa mới đóng ở đúng frame hiện tại -> bỏ qua
        if (InventoryUIController.Instance != null
            && (InventoryUIController.Instance.IsPopupOpen || InventoryUIController.Instance.LastClosedFrame == Time.frameCount))
        {
            return true;
        }

        return false;
    }

    private IEnumerator ShowFeedbackRoutine(string message)
    {
        if (feedbackText == null)
        {
            yield break;
        }

        feedbackText.text = message;
        SetGameObjectActiveSafe(feedbackText.gameObject, true);
        feedbackText.enabled = true;

        try
        {
            // BẮT BUỘC dùng Time.unscaledDeltaTime vì Time.timeScale đang = 0 khi Pause (đồng thời tránh cấp phát WaitForSeconds)
            float duration = Mathf.Max(0f, feedbackDuration);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }
        finally
        {
            HideFeedbackText();
            _feedbackCoroutine = null;
        }
    }

    private void UpdateHudPauseButtonVisibility()
    {
        if (hudPauseButton == null)
        {
            return;
        }

        bool isTransitioning = RoomTransitionManager.Instance != null && RoomTransitionManager.Instance.IsTransitioning;
        bool shouldShow = !IsPaused && !isTransitioning;
        SetGameObjectActiveSafe(hudPauseButton.gameObject, shouldShow);
    }

    // ================= ĐĂNG KÝ LISTENER & INPUT =================

    private void RegisterButtonListeners()
    {
        if (_areListenersRegistered)
        {
            return;
        }

        if (hudPauseButton != null) hudPauseButton.onClick.AddListener(Pause);
        if (resumeButton != null) resumeButton.onClick.AddListener(Resume);
        if (saveButton != null) saveButton.onClick.AddListener(SaveGameManual);
        if (mainMenuButton != null) mainMenuButton.onClick.AddListener(ReturnToMainMenu);

        _areListenersRegistered = true;
    }

    private void UnregisterButtonListeners()
    {
        if (!_areListenersRegistered)
        {
            return;
        }

        if (hudPauseButton != null) hudPauseButton.onClick.RemoveListener(Pause);
        if (resumeButton != null) resumeButton.onClick.RemoveListener(Resume);
        if (saveButton != null) saveButton.onClick.RemoveListener(SaveGameManual);
        if (mainMenuButton != null) mainMenuButton.onClick.RemoveListener(ReturnToMainMenu);

        _areListenersRegistered = false;
    }

    private void ResetInitialState()
    {
        IsPaused = false;
        SetGameObjectActiveSafe(pausePanelRoot, false);
        HideFeedbackText();
        UpdateHudPauseButtonVisibility();
    }

    private void HideFeedbackText()
    {
        if (feedbackText == null)
        {
            return;
        }

        feedbackText.enabled = false;
        SetGameObjectActiveSafe(feedbackText.gameObject, false);
    }

    private void StopFeedbackCoroutine()
    {
        if (_feedbackCoroutine != null)
        {
            StopCoroutine(_feedbackCoroutine);
            _feedbackCoroutine = null;
        }
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
