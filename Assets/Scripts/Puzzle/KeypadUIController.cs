using System;
using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Dữ liệu cấu hình một phiên nhập mật mã số (tái sử dụng cho mọi ổ khóa/tủ/cửa số ở nhiều Chapter).
/// </summary>
[Serializable]
public struct KeypadSessionData
{
    [Tooltip("Tiêu đề hiển thị ở đầu bảng ổ khóa (VD: TỦ HỒ SƠ HÀNH LANG).")]
    public string title;

    [Tooltip("Chuỗi mật mã chính xác để mở khóa (VD: 1408).")]
    public string correctCode;

    [Tooltip("Số ký tự tối đa được phép nhập. Nếu <= 0 sẽ tự động lấy theo độ dài của correctCode.")]
    public int maxDigits;

    [Tooltip("Nếu bật, tự động kiểm tra mật mã ngay khi nhập đủ số ký tự mà không cần bấm nút Xác nhận.")]
    public bool autoSubmitWhenFull;
}

/// <summary>
/// Điều khiển giao diện bảng nhập mật mã số theo mô hình uGUI + TextMeshPro.
/// Hỗ trợ cả bấm nút cảm ứng (Mobile), click chuột (PC) lẫn nhập trực tiếp bằng bàn phím số / Numpad (PC).
/// Tối ưu GC (StringBuilder tái sử dụng, SetText(StringBuilder), đếm unscaledDeltaTime) và chống Softlock.
/// </summary>
public class KeypadUIController : MonoBehaviour
{
    private const string DefaultTitle = "Ổ KHÓA MẬT MÃ";
    private const string PromptEnterCodeText = "NHẬP MẬT MÃ";
    private const string WrongCodeStatusText = "SAI MẬT MÃ!";
    private const string UnlockedStatusText = "ĐÃ MỞ KHÓA!";
    private const int FallbackMinDigits = 4;

#if ENABLE_INPUT_SYSTEM
    // Ánh xạ tĩnh 0..9 giúp đọc bàn phím số + Numpad không phụ thuộc thứ tự enum Key và không cấp phát GC
    private static readonly Key[] TopRowDigitKeys =
    {
        Key.Digit0, Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4,
        Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9
    };

    private static readonly Key[] NumpadDigitKeys =
    {
        Key.Numpad0, Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4,
        Key.Numpad5, Key.Numpad6, Key.Numpad7, Key.Numpad8, Key.Numpad9
    };
#elif ENABLE_LEGACY_INPUT_MANAGER
    private static readonly KeyCode[] LegacyTopRowDigitKeys =
    {
        KeyCode.Alpha0, KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4,
        KeyCode.Alpha5, KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9
    };

    private static readonly KeyCode[] LegacyNumpadDigitKeys =
    {
        KeyCode.Keypad0, KeyCode.Keypad1, KeyCode.Keypad2, KeyCode.Keypad3, KeyCode.Keypad4,
        KeyCode.Keypad5, KeyCode.Keypad6, KeyCode.Keypad7, KeyCode.Keypad8, KeyCode.Keypad9
    };
#endif

    // Singleton gọn nhẹ theo vòng đời Scene hiện tại (KHÔNG dùng DontDestroyOnLoad)
    public static KeypadUIController Instance { get; private set; }

    /// <summary>Trạng thái bảng nhập mật mã hiện đang mở hay đóng.</summary>
    public bool IsOpen { get; private set; }

    [Header("Thành phần hiển thị uGUI & TextMeshPro")]
    [Tooltip("Panel gốc chứa toàn bộ giao diện Keypad (ẩn mặc định khi khởi động; đặt tách khỏi GameObject gắn script này).")]
    [SerializeField] private GameObject panelRoot;

    [Tooltip("Tiêu đề ổ khóa (VD: TỦ HỒ SƠ HÀNH LANG).")]
    [SerializeField] private TMP_Text titleText;

    [Tooltip("Màn hình hiển thị dãy số đang nhập kèm ký tự chỗ trống (VD: 1 4 _ _).")]
    [SerializeField] private TMP_Text displayText;

    [Tooltip("Dòng thông báo trạng thái (NHẬP MẬT MÃ / SAI MẬT MÃ! / ĐÃ MỞ KHÓA!).")]
    [SerializeField] private TMP_Text statusText;

    [Header("Các nút bấm bàn phím số (uGUI)")]
    [Tooltip("Mảng đúng 10 nút số theo thứ tự từ 0 đến 9 (index 0 = số 0, ..., index 9 = số 9).")]
    [SerializeField] private Button[] digitButtons = new Button[10];

    [Tooltip("Nút xóa 1 ký tự vừa nhập (Backspace).")]
    [SerializeField] private Button backspaceButton;

    [Tooltip("Nút xóa toàn bộ dãy số đang nhập (Clear).")]
    [SerializeField] private Button clearButton;

    [Tooltip("Nút xác nhận kiểm tra mật mã (Submit / OK).")]
    [SerializeField] private Button submitButton;

    [Tooltip("Nút đóng bảng nhập mật mã (X / Hủy).")]
    [SerializeField] private Button closeButton;

    [Header("Màu sắc & Thời gian phản hồi")]
    [Tooltip("Màu chữ bình thường của màn hình hiển thị số và dòng trạng thái.")]
    [SerializeField] private Color normalDisplayColor = new Color(0.95f, 0.94f, 0.90f, 1f);

    [Tooltip("Màu báo lỗi khi nhập sai mật mã.")]
    [SerializeField] private Color errorColor = new Color(0.92f, 0.28f, 0.28f, 1f);

    [Tooltip("Màu báo thành công khi nhập đúng mật mã.")]
    [SerializeField] private Color successColor = new Color(0.28f, 0.85f, 0.45f, 1f);

    [Tooltip("Thời gian (giây) hiển thị phản hồi Đúng/Sai trước khi xóa ô nhập hoặc tự đóng bảng.")]
    [SerializeField] private float feedbackDuration = 0.6f;

    // Tối ưu GC: tái sử dụng 2 StringBuilder xuyên suốt vòng đời component, không cộng chuỗi (+) trong runtime
    private readonly StringBuilder _inputBuffer = new StringBuilder(16);
    private readonly StringBuilder _formatBuffer = new StringBuilder(32);

    // Lưu tham chiếu delegate của 10 nút số để hủy đăng ký sạch sẽ trong OnDestroy() chống Memory Leak
    private UnityAction[] _digitButtonCallbacks;
    private bool _areListenersRegistered;

    private KeypadSessionData _currentSession;
    private string _expectedCode = string.Empty;
    private int _activeMaxDigits = FallbackMinDigits;

    private Action _onSolvedCallback;
    private Action _onClosedCallback;

    private Coroutine _feedbackCoroutine;
    private bool _isInputLocked; // Khóa bấm liên tục trong lúc đang chạy hiệu ứng báo Đúng/Sai

    private void Awake()
    {
        // Chặn trùng lặp Singleton trong cùng một Scene
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[KeypadUIController] Phát hiện KeypadUIController trùng lặp trong Scene, tự động hủy bản thừa.");
            Destroy(gameObject);
            return;
        }

        Instance = this;

        RegisterButtonListeners();
        ResetInitialVisualState();
    }

    private void OnDisable()
    {
        // Chống Softlock: nếu object bị tắt đột ngột giữa chừng Coroutine, lập tức giải phóng khóa & dọn callback
        StopFeedbackCoroutine();
        _isInputLocked = false;
        IsOpen = false;
        _inputBuffer.Clear();
        _onSolvedCallback = null;
        _onClosedCallback = null;

        SetGameObjectActiveSafe(panelRoot, false);
    }

    private void OnDestroy()
    {
        StopFeedbackCoroutine();
        UnregisterButtonListeners();

        _onSolvedCallback = null;
        _onClosedCallback = null;

        // Dọn tham chiếu Singleton theo đúng vòng đời Scene hiện tại
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        // Chỉ đọc bàn phím PC khi bảng đang mở và không bị khóa trong thời gian phản hồi Đúng/Sai
        if (!IsOpen || _isInputLocked)
        {
            return;
        }

        HandleKeyboardInput();
    }

    // ================= API CÔNG KHAI (OPEN / CLOSE) =================

    /// <summary>
    /// Mở bảng nhập mật mã cho một phiên câu đố.
    /// Trả về false nếu bị chặn bởi UI khác (đang mở Keypad, chuyển phòng, mở Kho đồ hoặc đang Hội thoại).
    /// </summary>
    public bool Open(KeypadSessionData session, Action onSolvedCallback, Action onClosedCallback = null)
    {
        // Chặn mở đè UI / xung đột hệ thống (đồng bộ cùng chuẩn với InventoryUIController & RoomTransitionManager)
        if (IsOpen)
        {
            return false;
        }

        if (RoomTransitionManager.Instance != null && RoomTransitionManager.Instance.IsTransitioning)
        {
            return false;
        }

        if (InventoryUIController.Instance != null && InventoryUIController.Instance.IsPopupOpen)
        {
            return false;
        }

        if (DialogueManager.Instance != null && DialogueManager.Instance.IsDialogueActive)
        {
            return false;
        }

        StopFeedbackCoroutine();

        _currentSession = session;
        _expectedCode = string.IsNullOrWhiteSpace(session.correctCode) ? string.Empty : session.correctCode.Trim();
        _activeMaxDigits = session.maxDigits > 0
            ? session.maxDigits
            : (_expectedCode.Length > 0 ? _expectedCode.Length : FallbackMinDigits);

        _inputBuffer.Clear();
        _isInputLocked = false;
        _onSolvedCallback = onSolvedCallback;
        _onClosedCallback = onClosedCallback;

        IsOpen = true;
        SetGameObjectActiveSafe(panelRoot, true);

        if (titleText != null)
        {
            titleText.text = string.IsNullOrWhiteSpace(session.title) ? DefaultTitle : session.title;
        }

        ApplyFeedbackVisual(PromptEnterCodeText, normalDisplayColor);
        RefreshDisplayText();

        return true;
    }

    /// <summary>
    /// Đóng bảng nhập mật mã, dừng Coroutine phản hồi, giải phóng callback chống Memory Leak.
    /// </summary>
    public void Close(bool invokeClosedCallback = true)
    {
        StopFeedbackCoroutine();

        _isInputLocked = false;
        IsOpen = false;
        _inputBuffer.Clear();

        SetGameObjectActiveSafe(panelRoot, false);

        // Lưu tạm và xóa sạch tham chiếu callback trước khi Invoke để chống Memory Leak & lỗi gọi lồng
        Action closedCallback = _onClosedCallback;
        _onSolvedCallback = null;
        _onClosedCallback = null;

        if (invokeClosedCallback)
        {
            closedCallback?.Invoke();
        }
    }

    // ================= XỬ LÝ NHẬP SỐ & KIỂM TRA MẬT MÃ =================

    private void AppendDigit(int digit)
    {
        if (!IsOpen || _isInputLocked)
        {
            return;
        }

        if (digit < 0 || digit > 9 || _inputBuffer.Length >= _activeMaxDigits)
        {
            return;
        }

        // Tối ưu GC: nối trực tiếp ký tự char vào StringBuilder thay vì int.ToString()
        _inputBuffer.Append((char)('0' + digit));
        RefreshDisplayText();

        if (_currentSession.autoSubmitWhenFull && _inputBuffer.Length >= _activeMaxDigits)
        {
            SubmitCode();
        }
    }

    private void DeleteLastDigit()
    {
        if (!IsOpen || _isInputLocked || _inputBuffer.Length == 0)
        {
            return;
        }

        _inputBuffer.Length--;
        RefreshDisplayText();
    }

    private void ClearInput()
    {
        if (!IsOpen || _isInputLocked)
        {
            return;
        }

        if (_inputBuffer.Length > 0)
        {
            _inputBuffer.Clear();
            RefreshDisplayText();
        }
    }

    /// <summary>Kiểm tra dãy số hiện tại với mật mã đúng và chạy phản hồi Đúng/Sai.</summary>
    public void SubmitCode()
    {
        if (!IsOpen || _isInputLocked)
        {
            return;
        }

        // Bỏ qua nếu người chơi bấm Xác nhận khi chưa nhập ký tự nào
        if (_inputBuffer.Length == 0)
        {
            return;
        }

        // So sánh chính xác sau khi loại bỏ khoảng trắng thừa (nếu có)
        string enteredCode = _inputBuffer.ToString().Trim();
        bool isCorrect = !string.IsNullOrEmpty(_expectedCode)
                         && string.Equals(enteredCode, _expectedCode, StringComparison.Ordinal);

        StopFeedbackCoroutine();
        _feedbackCoroutine = StartCoroutine(isCorrect ? HandleSuccessFeedbackRoutine() : HandleErrorFeedbackRoutine());
    }

    private IEnumerator HandleErrorFeedbackRoutine()
    {
        _isInputLocked = true;
        ApplyFeedbackVisual(WrongCodeStatusText, errorColor);

        // Chống Softlock: try/finally bảo đảm luôn mở khóa _isInputLocked dù Coroutine bị dừng giữa chừng
        try
        {
            // Tối ưu GC: đếm bằng Time.unscaledDeltaTime thay vì cấp phát new WaitForSeconds()
            float duration = Mathf.Max(0f, feedbackDuration);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            _inputBuffer.Clear();
            ApplyFeedbackVisual(PromptEnterCodeText, normalDisplayColor);
            RefreshDisplayText();
        }
        finally
        {
            _isInputLocked = false;
            _feedbackCoroutine = null;
        }
    }

    private IEnumerator HandleSuccessFeedbackRoutine()
    {
        _isInputLocked = true;
        ApplyFeedbackVisual(UnlockedStatusText, successColor);

        bool completedNormally = false;
        try
        {
            // Tối ưu GC: đếm bằng Time.unscaledDeltaTime thay vì cấp phát new WaitForSeconds()
            float duration = Mathf.Max(0f, feedbackDuration);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            completedNormally = true;
        }
        finally
        {
            _feedbackCoroutine = null;
            if (!completedNormally)
            {
                _isInputLocked = false;
            }
        }

        // Lưu tạm callback trước khi Close(false) dọn tham chiếu, sau đó mới Invoke khi IsOpen đã về false
        Action solvedCallback = _onSolvedCallback;
        Close(false);
        solvedCallback?.Invoke();
    }

    // ================= CẬP NHẬT GIAO DIỆN (ZERO-GC STRINGBUILDER) =================

    private void RefreshDisplayText()
    {
        if (displayText == null)
        {
            return;
        }

        _formatBuffer.Clear();
        for (int i = 0; i < _activeMaxDigits; i++)
        {
            if (i > 0)
            {
                _formatBuffer.Append(' ');
            }

            char ch = i < _inputBuffer.Length ? _inputBuffer[i] : '_';
            _formatBuffer.Append(ch);
        }

        // Tối ưu GC: TextMeshPro.SetText(StringBuilder) đọc trực tiếp bộ đệm mà không cấp phát string mới
        displayText.SetText(_formatBuffer);
    }

    private void ApplyFeedbackVisual(string statusMessage, Color tintColor)
    {
        if (displayText != null)
        {
            displayText.color = tintColor;
        }

        if (statusText != null)
        {
            statusText.text = statusMessage;
            statusText.color = tintColor;
        }
    }

    // ================= HỖ TRỢ BÀN PHÍM PC (INPUT SYSTEM / LEGACY) =================

    private void HandleKeyboardInput()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            Close(true);
            return;
        }

        if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
        {
            SubmitCode();
            return;
        }

        if (keyboard.backspaceKey.wasPressedThisFrame)
        {
            DeleteLastDigit();
            return;
        }

        for (int d = 0; d < 10; d++)
        {
            if (keyboard[TopRowDigitKeys[d]].wasPressedThisFrame || keyboard[NumpadDigitKeys[d]].wasPressedThisFrame)
            {
                AppendDigit(d);
                return;
            }
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Close(true);
            return;
        }

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            SubmitCode();
            return;
        }

        if (Input.GetKeyDown(KeyCode.Backspace))
        {
            DeleteLastDigit();
            return;
        }

        for (int d = 0; d < 10; d++)
        {
            if (Input.GetKeyDown(LegacyTopRowDigitKeys[d]) || Input.GetKeyDown(LegacyNumpadDigitKeys[d]))
            {
                AppendDigit(d);
                return;
            }
        }
#endif
    }

    // ================= ĐĂNG KÝ / HỦY ĐĂNG KÝ LISTENER =================

    private void RegisterButtonListeners()
    {
        if (_areListenersRegistered)
        {
            return;
        }

        if (digitButtons != null && digitButtons.Length > 0)
        {
            int count = Mathf.Min(digitButtons.Length, 10);
            _digitButtonCallbacks = new UnityAction[count];

            for (int i = 0; i < count; i++)
            {
                Button btn = digitButtons[i];
                if (btn == null)
                {
                    continue;
                }

                // Bắt buộc sao chép biến vòng lặp sang biến cục bộ để tránh lỗi closure tất cả nút đều nhận số 10
                int digit = i;
                UnityAction callback = () => AppendDigit(digit);
                _digitButtonCallbacks[i] = callback;
                btn.onClick.AddListener(callback);
            }
        }

        if (backspaceButton != null) backspaceButton.onClick.AddListener(DeleteLastDigit);
        if (clearButton != null) clearButton.onClick.AddListener(ClearInput);
        if (submitButton != null) submitButton.onClick.AddListener(SubmitCode);
        if (closeButton != null) closeButton.onClick.AddListener(HandleCloseButtonClicked);

        _areListenersRegistered = true;
    }

    private void UnregisterButtonListeners()
    {
        if (!_areListenersRegistered)
        {
            return;
        }

        if (digitButtons != null && _digitButtonCallbacks != null)
        {
            int count = Mathf.Min(digitButtons.Length, _digitButtonCallbacks.Length);
            for (int i = 0; i < count; i++)
            {
                if (digitButtons[i] != null && _digitButtonCallbacks[i] != null)
                {
                    digitButtons[i].onClick.RemoveListener(_digitButtonCallbacks[i]);
                }
            }
        }

        if (backspaceButton != null) backspaceButton.onClick.RemoveListener(DeleteLastDigit);
        if (clearButton != null) clearButton.onClick.RemoveListener(ClearInput);
        if (submitButton != null) submitButton.onClick.RemoveListener(SubmitCode);
        if (closeButton != null) closeButton.onClick.RemoveListener(HandleCloseButtonClicked);

        _digitButtonCallbacks = null;
        _areListenersRegistered = false;
    }

    private void HandleCloseButtonClicked()
    {
        Close(true);
    }

    private void ResetInitialVisualState()
    {
        IsOpen = false;
        _isInputLocked = false;
        _inputBuffer.Clear();
        SetGameObjectActiveSafe(panelRoot, false);
    }

    private void StopFeedbackCoroutine()
    {
        if (_feedbackCoroutine != null)
        {
            StopCoroutine(_feedbackCoroutine);
            _feedbackCoroutine = null;
        }
    }

    private static void SetGameObjectActiveSafe(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
        {
            target.SetActive(active);
        }
    }
}
