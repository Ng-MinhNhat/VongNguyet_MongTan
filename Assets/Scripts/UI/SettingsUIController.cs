using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Điều khiển bảng Cài đặt âm thanh (Settings Modal) theo chuẩn uGUI + TextMeshPro.
/// Dùng chung cho cả Scene MainMenu lẫn PauseMenu trong Gameplay.
/// Tối ưu hiệu suất: dùng SetValueWithoutNotify để chống vòng lặp sự kiện, dùng TMP SetText không rác GC,
/// và chỉ ghi PlayerPrefs xuống ổ đĩa một lần duy nhất khi đóng bảng.
/// </summary>
public class SettingsUIController : MonoBehaviour
{
    private const string TestSfxId = "SFX_UI_Click";

    // Singleton gọn nhẹ theo vòng đời Scene hiện tại (KHÔNG dùng DontDestroyOnLoad)
    public static SettingsUIController Instance { get; private set; }

    /// <summary>Trạng thái bảng Cài đặt hiện đang mở hay đóng.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Frame gần nhất bảng Settings vừa đóng (chống đóng đè PauseMenu / Popup khi bấm Escape cùng frame).</summary>
    public int LastClosedFrame { get; private set; } = -1;

    [Header("Khung bảng Cài đặt (uGUI)")]
    [Tooltip("Panel gốc chứa toàn bộ giao diện Cài đặt (ẩn mặc định khi khởi động; đặt tách khỏi GameObject gắn script này).")]
    [SerializeField] private GameObject panelRoot;

    [Header("Thanh trượt âm lượng (Slider 0..1)")]
    [Tooltip("Thanh trượt chỉnh âm lượng tổng (Master Volume).")]
    [SerializeField] private Slider masterSlider;

    [Tooltip("Thanh trượt chỉnh âm lượng nhạc nền (BGM Volume).")]
    [SerializeField] private Slider bgmSlider;

    [Tooltip("Thanh trượt chỉnh âm lượng hiệu ứng (SFX Volume).")]
    [SerializeField] private Slider sfxSlider;

    [Header("Hiển thị phần trăm âm lượng (TextMeshPro)")]
    [Tooltip("Văn bản hiển thị phần trăm Master (0% - 100%). Nên tắt Raycast Target.")]
    [SerializeField] private TMP_Text masterValueText;

    [Tooltip("Văn bản hiển thị phần trăm BGM (0% - 100%). Nên tắt Raycast Target.")]
    [SerializeField] private TMP_Text bgmValueText;

    [Tooltip("Văn bản hiển thị phần trăm SFX (0% - 100%). Nên tắt Raycast Target.")]
    [SerializeField] private TMP_Text sfxValueText;

    [Header("Tùy chọn & Nút điều khiển")]
    [Tooltip("Nút gạt tắt tiếng toàn bộ âm thanh (Mute).")]
    [SerializeField] private Toggle muteToggle;

    [Tooltip("Nút 'NGHE THỬ SFX' để kiểm chứng âm lượng hiệu ứng ngay khi đang kéo thanh trượt.")]
    [SerializeField] private Button testSfxButton;

    [Tooltip("Nút Đóng bảng Cài đặt và lưu cấu hình.")]
    [SerializeField] private Button closeButton;

    [Tooltip("Tùy chọn: nút nền tối phía sau để chạm ra ngoài khung là đóng và lưu nhanh.")]
    [SerializeField] private Button backdropCloseButton;

    private AudioManager _subscribedAudioManager;
    private bool _areListenersRegistered;

    private void Awake()
    {
        // Chặn trùng lặp Singleton trong cùng một Scene
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[SettingsUIController] Phát hiện SettingsUIController trùng lặp trong Scene, tự động hủy bản thừa.");
            Destroy(gameObject);
            return;
        }

        Instance = this;

        RegisterUIListeners();

        IsOpen = false;
        SetGameObjectActiveSafe(panelRoot, false);
    }

    private void OnEnable()
    {
        TrySubscribeAudioManager();
    }

    private void OnDisable()
    {
        UnsubscribeAudioManager();

        // Chỉ ghi PlayerPrefs khi bảng bị tắt từ trạng thái đang mở, tránh ghi đĩa liên tục khi kéo Slider
        if (IsOpen)
        {
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.SaveSettings();
            }

            IsOpen = false;
            SetGameObjectActiveSafe(panelRoot, false);
        }
    }

    private void OnDestroy()
    {
        UnsubscribeAudioManager();
        UnregisterUIListeners();

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        // Chỉ chạy khi bảng Settings đang mở; không cập nhật Text hay gọi Find/GetComponent trong Update()
        if (!IsOpen)
        {
            return;
        }

        if (WasEscapeKeyPressed())
        {
            Close();
        }
    }

    // ================= API CÔNG KHAI (OPEN / CLOSE) =================

    /// <summary>Đồng bộ giá trị âm lượng hiện tại từ AudioManager và mở bảng Cài đặt.</summary>
    public void Open()
    {
        if (IsOpen)
        {
            return;
        }

        TrySubscribeAudioManager();
        SyncUIFromAudioManager();

        SetGameObjectActiveSafe(panelRoot, true);
        IsOpen = true;
    }

    /// <summary>
    /// Lưu cấu hình âm lượng xuống PlayerPrefs một lần duy nhất, ghi nhận LastClosedFrame và đóng bảng Cài đặt.
    /// </summary>
    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        // Chỉ ghi PlayerPrefs lúc đóng bảng thay vì trong callback Slider để tránh giật lag I/O trên Mobile/PC
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.SaveSettings();
        }

        IsOpen = false;
        // Ghi dấu frame đóng để PauseMenuController / MainMenuController không xử lý trùng phím Escape cùng frame
        LastClosedFrame = Time.frameCount;
        SetGameObjectActiveSafe(panelRoot, false);
    }

    /// <summary>
    /// Đồng bộ trạng thái Slider/Toggle từ AudioManager bằng WithoutNotify để không gây vòng lặp sự kiện.
    /// </summary>
    public void SyncUIFromAudioManager()
    {
        AudioManager audio = AudioManager.Instance;
        float master = audio != null ? audio.MasterVolume : 1f;
        float bgm = audio != null ? audio.BgmVolume : 1f;
        float sfx = audio != null ? audio.SfxVolume : 1f;
        bool isMuted = audio != null && audio.IsMuted;

        if (masterSlider != null) masterSlider.SetValueWithoutNotify(master);
        if (bgmSlider != null) bgmSlider.SetValueWithoutNotify(bgm);
        if (sfxSlider != null) sfxSlider.SetValueWithoutNotify(sfx);
        if (muteToggle != null) muteToggle.SetIsOnWithoutNotify(isMuted);

        UpdatePercentText(masterValueText, master);
        UpdatePercentText(bgmValueText, bgm);
        UpdatePercentText(sfxValueText, sfx);
    }

    // ================= XỬ LÝ CALLBACK UI =================

    private void HandleMasterSliderChanged(float value)
    {
        float clamped = Mathf.Clamp01(value);
        UpdatePercentText(masterValueText, clamped);

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.SetMasterVolume(clamped);
        }
    }

    private void HandleBgmSliderChanged(float value)
    {
        float clamped = Mathf.Clamp01(value);
        UpdatePercentText(bgmValueText, clamped);

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.SetBgmVolume(clamped);
        }
    }

    private void HandleSfxSliderChanged(float value)
    {
        float clamped = Mathf.Clamp01(value);
        UpdatePercentText(sfxValueText, clamped);

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.SetSfxVolume(clamped);
        }
    }

    private void HandleMuteToggleChanged(bool isMuted)
    {
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.SetMuted(isMuted);
        }
    }

    private void HandleTestSfxClicked()
    {
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySfx(TestSfxId);
        }
    }

    private void HandleCloseButtonClicked()
    {
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySfx(TestSfxId);
        }

        Close();
    }

    private void HandleAudioSettingsChanged()
    {
        if (IsOpen)
        {
            SyncUIFromAudioManager();
        }
    }

    // ================= ĐĂNG KÝ LISTENER & TIỆN ÍCH =================

    private void TrySubscribeAudioManager()
    {
        if (_subscribedAudioManager == null && AudioManager.Instance != null)
        {
            _subscribedAudioManager = AudioManager.Instance;
            _subscribedAudioManager.OnSettingsChanged += HandleAudioSettingsChanged;
        }
    }

    private void UnsubscribeAudioManager()
    {
        if (_subscribedAudioManager != null)
        {
            _subscribedAudioManager.OnSettingsChanged -= HandleAudioSettingsChanged;
            _subscribedAudioManager = null;
        }
    }

    private void RegisterUIListeners()
    {
        if (_areListenersRegistered)
        {
            return;
        }

        if (masterSlider != null) masterSlider.onValueChanged.AddListener(HandleMasterSliderChanged);
        if (bgmSlider != null) bgmSlider.onValueChanged.AddListener(HandleBgmSliderChanged);
        if (sfxSlider != null) sfxSlider.onValueChanged.AddListener(HandleSfxSliderChanged);
        if (muteToggle != null) muteToggle.onValueChanged.AddListener(HandleMuteToggleChanged);
        if (testSfxButton != null) testSfxButton.onClick.AddListener(HandleTestSfxClicked);
        if (closeButton != null) closeButton.onClick.AddListener(HandleCloseButtonClicked);
        if (backdropCloseButton != null) backdropCloseButton.onClick.AddListener(HandleCloseButtonClicked);

        _areListenersRegistered = true;
    }

    private void UnregisterUIListeners()
    {
        if (!_areListenersRegistered)
        {
            return;
        }

        if (masterSlider != null) masterSlider.onValueChanged.RemoveListener(HandleMasterSliderChanged);
        if (bgmSlider != null) bgmSlider.onValueChanged.RemoveListener(HandleBgmSliderChanged);
        if (sfxSlider != null) sfxSlider.onValueChanged.RemoveListener(HandleSfxSliderChanged);
        if (muteToggle != null) muteToggle.onValueChanged.RemoveListener(HandleMuteToggleChanged);
        if (testSfxButton != null) testSfxButton.onClick.RemoveListener(HandleTestSfxClicked);
        if (closeButton != null) closeButton.onClick.RemoveListener(HandleCloseButtonClicked);
        if (backdropCloseButton != null) backdropCloseButton.onClick.RemoveListener(HandleCloseButtonClicked);

        _areListenersRegistered = false;
    }

    private static void UpdatePercentText(TMP_Text label, float normalizedValue)
    {
        if (label == null)
        {
            return;
        }

        // Tối ưu GC: dùng TMP SetText thay vì nối chuỗi "{percent}%" trên Heap
        int percent = Mathf.RoundToInt(Mathf.Clamp01(normalizedValue) * 100f);
        label.SetText("{0}%", percent);
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
