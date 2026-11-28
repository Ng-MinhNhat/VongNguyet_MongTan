using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Điều khiển màn hình chính (Main Menu) theo chuẩn uGUI + TextMeshPro.
/// Hỗ trợ Tiếp tục (Continue), Chơi mới (kèm Popup xác nhận khi đã có Save), Thoát game,
/// chống spam-click, Fade chuyển Scene không sinh rác GC và kiểm tra Build Settings chống Softlock.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Header("Cấu hình Scene Gameplay")]
    [Tooltip("Tên Scene gameplay sẽ nạp khi bấm Tiếp tục hoặc Chơi mới (phải được thêm vào Build Settings).")]
    [SerializeField] private string gameplaySceneName = "SampleScene";

    [Header("Các nút điều hướng chính (uGUI)")]
    [Tooltip("Nút Tiếp tục chơi từ file Save cục bộ.")]
    [SerializeField] private Button continueButton;

    [Tooltip("Nút bắt đầu Chơi mới.")]
    [SerializeField] private Button newGameButton;

    [Tooltip("Nút mở bảng Cài đặt âm thanh.")]
    [SerializeField] private Button settingsButton;

    [Tooltip("Nút thoát trò chơi.")]
    [SerializeField] private Button quitButton;

    [Tooltip("Nếu bật (true): ẩn hẳn nút Tiếp tục khi chưa có file Save. Nếu tắt (false): giữ nút nhưng làm mờ interactable = false.")]
    [SerializeField] private bool hideContinueWhenNoSave = false;

    [Header("Popup xác nhận Chơi mới (Khi đã có Save)")]
    [Tooltip("Panel xác nhận ghi đè tiến độ cũ (mặc định ẩn khi khởi động).")]
    [SerializeField] private GameObject confirmNewGamePanel;

    [Tooltip("Nút Đồng ý xóa tiến độ cũ và bắt đầu Chơi mới.")]
    [SerializeField] private Button confirmYesButton;

    [Tooltip("Nút Hủy để đóng Popup xác nhận.")]
    [SerializeField] private Button confirmNoButton;

    [Header("Hiệu ứng Fade chuyển Scene (Tùy chọn)")]
    [Tooltip("CanvasGroup tấm màn đen phủ toàn màn hình để Fade trước khi nạp Scene gameplay.")]
    [SerializeField] private CanvasGroup fadeCanvasGroup;

    [Tooltip("Thời gian (giây thực tế unscaled) màn hình tối dần từ 0 lên 1 trước khi nạp Scene.")]
    [SerializeField] private float fadeOutDuration = 0.35f;

    [Header("Thông tin phiên bản (Tùy chọn)")]
    [Tooltip("Văn bản TextMeshPro hiển thị phiên bản ứng dụng (VD: v1.0). Nên tắt Raycast Target.")]
    [SerializeField] private TMP_Text versionText;

    // Cờ khóa chống spam-click bấm đúp nhiều nút trong lúc đang Fade / nạp Scene
    private bool _isTransitioning;
    private Coroutine _transitionCoroutine;
    private bool _areListenersRegistered;

    private void Awake()
    {
        // Chốt chặn dự phòng: luôn khôi phục Time.timeScale = 1 khi quay về MainMenu từ Gameplay
        Time.timeScale = 1f;

        RegisterButtonListeners();
        SetGameObjectActiveSafe(confirmNewGamePanel, false);
        ResetFadeImmediate();
    }

    private void Start()
    {
        RefreshContinueButtonState();

        if (versionText != null)
        {
            versionText.text = $"v{Application.version}";
        }

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayBgm("BGM_MainMenu");
        }
    }

    private void OnDisable()
    {
        if (_transitionCoroutine != null)
        {
            StopCoroutine(_transitionCoroutine);
            _transitionCoroutine = null;
        }

        _isTransitioning = false;
    }

    private void OnDestroy()
    {
        UnregisterButtonListeners();
    }

    private void Update()
    {
        // Chống xung đột Escape cùng frame: nếu bảng Settings đang mở hoặc vừa đóng -> không đóng nhầm Popup xác nhận
        if (SettingsUIController.Instance != null
            && (SettingsUIController.Instance.IsOpen || SettingsUIController.Instance.LastClosedFrame == Time.frameCount))
        {
            return;
        }

        // Chỉ lắng nghe phím Escape để đóng Popup xác nhận khi Popup đang mở và chưa bắt đầu chuyển Scene
        if (_isTransitioning || confirmNewGamePanel == null || !confirmNewGamePanel.activeSelf)
        {
            return;
        }

        if (WasEscapeKeyPressed())
        {
            SetGameObjectActiveSafe(confirmNewGamePanel, false);
        }
    }

    // ================= CẬP NHẬT TRẠNG THÁI NÚT & XỬ LÝ SỰ KIỆN =================

    /// <summary>Cập nhật hiển thị / khả năng bấm của nút Tiếp tục dựa theo sự tồn tại của file Save cục bộ.</summary>
    public void RefreshContinueButtonState()
    {
        if (continueButton == null)
        {
            return;
        }

        bool hasSave = GameStateManager.HasSaveFile();
        if (hideContinueWhenNoSave)
        {
            SetGameObjectActiveSafe(continueButton.gameObject, hasSave);
            continueButton.interactable = hasSave;
        }
        else
        {
            SetGameObjectActiveSafe(continueButton.gameObject, true);
            continueButton.interactable = hasSave;
        }
    }

    private void OnContinueClicked()
    {
        // Chống spam-click và chặn tiếp tục nếu file Save không còn tồn tại
        if (_isTransitioning || !GameStateManager.HasSaveFile())
        {
            RefreshContinueButtonState();
            return;
        }

        PlayUiClickSfx();

        if (!ValidateGameplaySceneInBuildSettings())
        {
            return;
        }

        GameSessionLaunchContext.SetPendingLaunchMode(SessionLaunchMode.ContinueSave);
        BeginSceneTransition();
    }

    private void OnNewGameClicked()
    {
        if (_isTransitioning)
        {
            return;
        }

        PlayUiClickSfx();

        // Nếu đã có file Save và có gán Popup xác nhận -> hỏi xác nhận trước khi xóa tiến độ cũ
        if (GameStateManager.HasSaveFile() && confirmNewGamePanel != null)
        {
            SetGameObjectActiveSafe(confirmNewGamePanel, true);
            return;
        }

        StartNewGame();
    }

    private void OnSettingsClicked()
    {
        if (_isTransitioning)
        {
            return;
        }

        PlayUiClickSfx();

        if (SettingsUIController.Instance != null)
        {
            SettingsUIController.Instance.Open();
        }
    }

    private void OnConfirmNewGameYesClicked()
    {
        if (_isTransitioning)
        {
            return;
        }

        PlayUiClickSfx();
        SetGameObjectActiveSafe(confirmNewGamePanel, false);
        StartNewGame();
    }

    private void OnConfirmNewGameNoClicked()
    {
        if (_isTransitioning)
        {
            return;
        }

        PlayUiClickSfx();
        SetGameObjectActiveSafe(confirmNewGamePanel, false);
    }

    private void StartNewGame()
    {
        if (_isTransitioning)
        {
            return;
        }

        // Chống Softlock Build Settings: xác nhận Scene nạp được TRƯỚC khi xóa file Save cũ của người chơi
        if (!ValidateGameplaySceneInBuildSettings())
        {
            return;
        }

        GameStateManager.DeleteSaveFileStatic();
        GameSessionLaunchContext.SetPendingLaunchMode(SessionLaunchMode.NewGame);
        BeginSceneTransition();
    }

    private void OnQuitClicked()
    {
        if (_isTransitioning)
        {
            return;
        }

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ================= FADE & NẠP SCENE AN TOÀN =================

    private void BeginSceneTransition()
    {
        if (_isTransitioning)
        {
            return;
        }

        _transitionCoroutine = StartCoroutine(FadeAndLoadGameplayScene());
    }

    private IEnumerator FadeAndLoadGameplayScene()
    {
        // Chống Softlock kẹt màn hình đen: kiểm tra lại Scene có trong Build Settings trước khi Fade
        if (!ValidateGameplaySceneInBuildSettings())
        {
            yield break;
        }

        _isTransitioning = true;
        SetMainButtonsInteractable(false);

        if (fadeCanvasGroup != null)
        {
            SetGameObjectActiveSafe(fadeCanvasGroup.gameObject, true);
            fadeCanvasGroup.blocksRaycasts = true;
            fadeCanvasGroup.interactable = true;
            fadeCanvasGroup.alpha = 0f;

            // Tối ưu GC: tăng alpha bằng Time.unscaledDeltaTime thay vì tạo new WaitForSeconds()
            float duration = Mathf.Max(0f, fadeOutDuration);
            if (duration > 0f)
            {
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    fadeCanvasGroup.alpha = Mathf.Clamp01(elapsed / duration);
                    yield return null;
                }
            }

            fadeCanvasGroup.alpha = 1f;
        }

        AsyncOperation op = SceneManager.LoadSceneAsync(gameplaySceneName);
        if (op == null)
        {
            // Khôi phục giao diện nếu thao tác nạp Scene trả về null bất thường để tránh kẹt màn hình đen
            Debug.LogError($"[MainMenuController] Không thể khởi tạo LoadSceneAsync cho '{gameplaySceneName}'.");
            ResetFadeImmediate();
            _isTransitioning = false;
            _transitionCoroutine = null;
            SetMainButtonsInteractable(true);
            RefreshContinueButtonState();
            yield break;
        }

        while (!op.isDone)
        {
            yield return null;
        }
    }

    private bool ValidateGameplaySceneInBuildSettings()
    {
        if (string.IsNullOrWhiteSpace(gameplaySceneName) || !Application.CanStreamedLevelBeLoaded(gameplaySceneName))
        {
            Debug.LogError(
                $"[MainMenuController] Không thể nạp Scene '{gameplaySceneName}'! " +
                "Hãy vào File -> Build Profiles / Build Settings và thêm Scene này vào danh sách Scene List.");
            return false;
        }

        return true;
    }

    // ================= ĐĂNG KÝ LISTENER & TIỆN ÍCH =================

    private void RegisterButtonListeners()
    {
        if (_areListenersRegistered)
        {
            return;
        }

        if (continueButton != null) continueButton.onClick.AddListener(OnContinueClicked);
        if (newGameButton != null) newGameButton.onClick.AddListener(OnNewGameClicked);
        if (settingsButton != null) settingsButton.onClick.AddListener(OnSettingsClicked);
        if (quitButton != null) quitButton.onClick.AddListener(OnQuitClicked);
        if (confirmYesButton != null) confirmYesButton.onClick.AddListener(OnConfirmNewGameYesClicked);
        if (confirmNoButton != null) confirmNoButton.onClick.AddListener(OnConfirmNewGameNoClicked);

        _areListenersRegistered = true;
    }

    private void UnregisterButtonListeners()
    {
        if (!_areListenersRegistered)
        {
            return;
        }

        if (continueButton != null) continueButton.onClick.RemoveListener(OnContinueClicked);
        if (newGameButton != null) newGameButton.onClick.RemoveListener(OnNewGameClicked);
        if (settingsButton != null) settingsButton.onClick.RemoveListener(OnSettingsClicked);
        if (quitButton != null) quitButton.onClick.RemoveListener(OnQuitClicked);
        if (confirmYesButton != null) confirmYesButton.onClick.RemoveListener(OnConfirmNewGameYesClicked);
        if (confirmNoButton != null) confirmNoButton.onClick.RemoveListener(OnConfirmNewGameNoClicked);

        _areListenersRegistered = false;
    }

    private void SetMainButtonsInteractable(bool interactable)
    {
        if (continueButton != null) continueButton.interactable = interactable;
        if (newGameButton != null) newGameButton.interactable = interactable;
        if (settingsButton != null) settingsButton.interactable = interactable;
        if (quitButton != null) quitButton.interactable = interactable;
        if (confirmYesButton != null) confirmYesButton.interactable = interactable;
        if (confirmNoButton != null) confirmNoButton.interactable = interactable;
    }

    private static void PlayUiClickSfx()
    {
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySfx("SFX_UI_Click");
        }
    }

    private void ResetFadeImmediate()
    {
        if (fadeCanvasGroup == null)
        {
            return;
        }

        fadeCanvasGroup.alpha = 0f;
        fadeCanvasGroup.blocksRaycasts = false;
        fadeCanvasGroup.interactable = false;
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
