using UnityEngine;

/// <summary>
/// Chế độ khởi chạy phiên chơi được truyền từ MainMenu sang Scene gameplay.
/// </summary>
public enum SessionLaunchMode
{
    /// <summary>Chạy trực tiếp trong Unity Editor -> giữ nguyên hành vi theo cờ autoLoadOnStart trên Inspector.</summary>
    EditorDefault,

    /// <summary>Bắt đầu chơi mới từ MainMenu -> xóa sạch dữ liệu phiên hiện tại và bắt đầu ở phòng khởi đầu.</summary>
    NewGame,

    /// <summary>Tiếp tục từ file Save cục bộ -> nạp dữ liệu Save và đưa nhân vật về đúng phòng đã lưu.</summary>
    ContinueSave
}

/// <summary>
/// Lớp tĩnh thuần C# (không kế thừa MonoBehaviour) truyền chế độ khởi chạy một lần từ MainMenu sang GameStateManager.
/// Tự reset về EditorDefault ngay sau khi đọc (Consume) để tránh dính trạng thái cũ ở các lần tải Scene sau.
/// </summary>
public static class GameSessionLaunchContext
{
    private static SessionLaunchMode _pendingMode = SessionLaunchMode.EditorDefault;

    // Reset biến tĩnh khi bật tắt Play Mode không Domain Reload trong Unity 6 Editor
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        _pendingMode = SessionLaunchMode.EditorDefault;
    }

    /// <summary>Đặt chế độ khởi chạy sẽ được tiêu thụ bởi GameStateManager khi Scene gameplay tải xong.</summary>
    public static void SetPendingLaunchMode(SessionLaunchMode mode)
    {
        _pendingMode = mode;
    }

    /// <summary>
    /// Đọc chế độ khởi chạy đang chờ và lập tức reset về EditorDefault để tránh lặp lại ở các lần nạp sau.
    /// </summary>
    public static SessionLaunchMode ConsumePendingLaunchMode()
    {
        SessionLaunchMode mode = _pendingMode;
        _pendingMode = SessionLaunchMode.EditorDefault;
        return mode;
    }
}
