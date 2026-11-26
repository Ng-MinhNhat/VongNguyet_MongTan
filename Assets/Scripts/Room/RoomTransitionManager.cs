using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Quản lý chuyển đổi giữa các phòng trong cùng Scene kèm hiệu ứng màn đen Fade (uGUI CanvasGroup),
/// tối ưu Draw Calls bằng cách tắt phòng cũ / bật phòng mới và đồng bộ nhảy tức thì cho Cinemachine 3.
/// </summary>
public class RoomTransitionManager : MonoBehaviour
{
    private const string DefaultSpawnId = "Default";

    // Singleton gọn nhẹ theo vòng đời Scene hiện tại (KHÔNG dùng DontDestroyOnLoad)
    public static RoomTransitionManager Instance { get; private set; }

    /// <summary>True khi đang thực hiện hiệu ứng chuyển phòng (dùng để chặn tương tác chồng chéo).</summary>
    public bool IsTransitioning { get; private set; }

    /// <summary>Mã phòng hiện tại nhân vật đang đứng.</summary>
    public string CurrentRoomId { get; private set; } = string.Empty;

    /// <summary>Mã điểm spawn gần nhất đã đưa nhân vật vào phòng hiện tại.</summary>
    public string CurrentSpawnPointId { get; private set; } = string.Empty;

    /// <summary>Sự kiện phát ngay khi đổi phòng xong (truyền roomId, spawnPointId).</summary>
    public event Action<string, string> OnRoomChanged;

    [Header("Nhân vật & Hiệu ứng Fade (uGUI)")]
    [Tooltip("Transform của nhân vật chính cần dịch chuyển khi đổi phòng.")]
    [SerializeField] private Transform playerTransform;

    [Tooltip("CanvasGroup của tấm màn đen phủ toàn màn hình trên TransitionCanvas (uGUI).")]
    [SerializeField] private CanvasGroup fadeCanvasGroup;

    [Header("Thời lượng chuyển cảnh (Giây)")]
    [Tooltip("Thời gian màn hình tối dần từ trong suốt (0) sang đen hoàn toàn (1).")]
    [SerializeField] private float fadeOutDuration = 0.35f;

    [Tooltip("Thời gian giữ màn hình đen sau khi đã đổi phòng để mắt người chơi dễ thích nghi.")]
    [SerializeField] private float holdBlackDuration = 0.1f;

    [Tooltip("Thời gian màn hình sáng dần từ đen hoàn toàn (1) về trong suốt (0).")]
    [SerializeField] private float fadeInDuration = 0.35f;

    [Header("Phòng khởi đầu")]
    [Tooltip("Mã phòng mặc định khi bắt đầu Scene nếu chưa có hệ thống Save gọi TeleportImmediate trước.")]
    [SerializeField] private string initialRoomId = "Ch1_PhongNgu";

    [Tooltip("Mã điểm spawn mặc định trong phòng khởi đầu.")]
    [SerializeField] private string initialSpawnPointId = DefaultSpawnId;

    [Header("Danh sách phòng trong Scene")]
    [Tooltip("Kéo toàn bộ các RoomNode của Chapter/Scene này vào đây để lập bảng tra cứu O(1) trong Awake().")]
    [SerializeField] private List<RoomNode> rooms = new List<RoomNode>();

    [Header("Đồng bộ Cinemachine 3 (Tùy chọn)")]
    [Tooltip("CinemachineCamera đang theo dõi nhân vật. Nếu để trống sẽ tự tìm 1 lần duy nhất trong Awake().")]
    [SerializeField] private CinemachineCamera virtualCamera;

    [Tooltip("CinemachineConfiner2D gắn trên Camera để tự động cập nhật BoundingShape2D theo từng phòng.")]
    [SerializeField] private CinemachineConfiner2D cameraConfiner;

    // Bảng tra cứu O(1) xây 1 lần trong Awake() — tuyệt đối không gọi Find() trong lúc chơi
    private readonly Dictionary<string, RoomNode> _roomLookup = new Dictionary<string, RoomNode>(StringComparer.OrdinalIgnoreCase);

    // Cache sẵn Rigidbody2D của nhân vật để reset linearVelocity về 0 mà không gọi GetComponent() lúc chuyển phòng
    private Rigidbody2D _playerRigidbody;
    private RoomNode _currentRoom;
    private Coroutine _transitionCoroutine;

    // Cờ chống Race Condition (Script Execution Order) giữa GameStateManager.Start() và RoomTransitionManager.Start()
    private bool _hasInitializedRoom = false;

    private void Awake()
    {
        // Chặn trùng lặp Singleton trong cùng một Scene
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[RoomTransitionManager] Phát hiện RoomTransitionManager trùng lặp trong Scene, tự động hủy bản thừa.");
            Destroy(gameObject);
            return;
        }

        Instance = this;

        CacheComponents();
        BuildRoomLookup();
        ResetFadeStateImmediate();
    }

    private void Start()
    {
        // Chống Race Condition: nếu GameStateManager.LoadLocalGame() đã gọi TeleportImmediate() trước Start(),
        // _hasInitializedRoom đã là true nên sẽ KHÔNG ghi đè ngược nhân vật về phòng khởi đầu
        if (!_hasInitializedRoom && !string.IsNullOrEmpty(initialRoomId))
        {
            TeleportImmediate(initialRoomId, initialSpawnPointId);
        }
    }

    private void OnDisable()
    {
        // Chống kẹt màn hình đen nếu Manager bị tắt đột ngột giữa chừng Coroutine
        if (_transitionCoroutine != null)
        {
            StopCoroutine(_transitionCoroutine);
            _transitionCoroutine = null;
        }

        ResetFadeStateImmediate();
        IsTransitioning = false;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        OnRoomChanged = null;
    }

    // ================= CHUYỂN PHÒNG CÓ FADE & CHUYỂN TỨC THÌ =================

    /// <summary>
    /// Chuyển sang phòng đích kèm hiệu ứng Fade đen mượt (dùng Time.unscaledDeltaTime, không cấp phát WaitForSeconds).
    /// Trả về false nếu đang chuyển phòng hoặc không tìm thấy targetRoomId.
    /// </summary>
    public bool TransitionToRoom(string targetRoomId, string targetSpawnPointId = DefaultSpawnId)
    {
        // Chống spam trigger / gọi chồng Coroutine khi đang chuyển phòng
        if (IsTransitioning)
        {
            return false;
        }

        if (string.IsNullOrEmpty(targetRoomId) || !_roomLookup.TryGetValue(targetRoomId, out RoomNode targetRoom) || targetRoom == null)
        {
            Debug.LogWarning($"[RoomTransitionManager] Không tìm thấy phòng '{targetRoomId}' trong danh sách rooms.");
            return false;
        }

        // Edge Case: đóng Popup Kho đồ nếu đang mở để tránh UI đè lên hiệu ứng chuyển phòng
        if (InventoryUIController.Instance != null && InventoryUIController.Instance.IsPopupOpen)
        {
            InventoryUIController.Instance.ClosePopup();
        }

        string safeSpawnId = string.IsNullOrEmpty(targetSpawnPointId) ? DefaultSpawnId : targetSpawnPointId;
        _transitionCoroutine = StartCoroutine(TransitionRoutine(targetRoom, safeSpawnId));
        return true;
    }

    /// <summary>
    /// Dịch chuyển ngay sang phòng đích không qua Fade (dùng cho Start() hoặc khi tải file Save).
    /// Tự động fallback về initialRoomId nếu targetRoomId rỗng hoặc không tồn tại (file Save cũ/lỗi) để chống Softlock.
    /// </summary>
    public void TeleportImmediate(string targetRoomId, string targetSpawnPointId = DefaultSpawnId)
    {
        RoomNode targetRoom = null;
        string resolvedSpawnId = string.IsNullOrEmpty(targetSpawnPointId) ? DefaultSpawnId : targetSpawnPointId;

        if (!string.IsNullOrEmpty(targetRoomId))
        {
            _roomLookup.TryGetValue(targetRoomId, out targetRoom);
        }

        // Chống Softlock Save cũ/lỗi: nếu phòng yêu cầu rỗng hoặc không tồn tại trong Dictionary -> fallback về phòng khởi đầu
        if (targetRoom == null)
        {
            Debug.LogWarning(
                $"[RoomTransitionManager] Không tìm thấy phòng '{targetRoomId}' (Save cũ/không hợp lệ). " +
                $"Tự động fallback về phòng khởi đầu '{initialRoomId}'.");

            if (!string.IsNullOrEmpty(initialRoomId))
            {
                _roomLookup.TryGetValue(initialRoomId, out targetRoom);
            }

            // Fallback cấp 2: lấy phòng hợp lệ đầu tiên trong danh sách nếu initialRoomId cũng bị thiếu
            if (targetRoom == null && rooms != null)
            {
                for (int i = 0; i < rooms.Count; i++)
                {
                    if (rooms[i] != null)
                    {
                        targetRoom = rooms[i];
                        break;
                    }
                }
            }

            if (targetRoom == null)
            {
                Debug.LogError("[RoomTransitionManager] TeleportImmediate thất bại: danh sách rooms không có RoomNode nào hợp lệ.");
                return;
            }

            resolvedSpawnId = string.IsNullOrEmpty(initialSpawnPointId) ? DefaultSpawnId : initialSpawnPointId;
        }

        // Hủy Coroutine chuyển phòng đang chạy dở (nếu có) và đưa màn Fade về trạng thái an toàn
        if (_transitionCoroutine != null)
        {
            StopCoroutine(_transitionCoroutine);
            _transitionCoroutine = null;
        }

        ResetFadeStateImmediate();
        IsTransitioning = false;

        // Tắt toàn bộ các phòng khác để chỉ giữ đúng 1 phòng đang hoạt động (tối ưu Draw Calls)
        DeactivateAllRoomsExcept(targetRoom);

        ApplyRoomSwitchAndTeleport(targetRoom, resolvedSpawnId);
        _hasInitializedRoom = true;
    }

    /// <summary>Kiểm tra nhanh phòng có tồn tại trong bảng tra cứu O(1) hay không.</summary>
    public bool HasRoom(string roomId)
    {
        return !string.IsNullOrEmpty(roomId) && _roomLookup.ContainsKey(roomId);
    }

    /// <summary>Lấy tham chiếu RoomNode theo roomId (O(1)).</summary>
    public RoomNode GetRoom(string roomId)
    {
        return !string.IsNullOrEmpty(roomId) && _roomLookup.TryGetValue(roomId, out RoomNode node) ? node : null;
    }

    // ================= COROUTINE CHUYỂN PHÒNG (ZERO-GC) =================

    private IEnumerator TransitionRoutine(RoomNode targetRoom, string targetSpawnPointId)
    {
        IsTransitioning = true;
        bool didLockPlayerMovement = false;

        // Khóa di chuyển tạm thời trong lúc màn đen để chống nhân vật trượt vào cửa khác gây Softlock
        if (GameStateManager.Instance != null && GameStateManager.Instance.CurrentState == GameState.FreeRoam)
        {
            GameStateManager.Instance.ChangeState(GameState.Cutscene);
            didLockPlayerMovement = true;
        }

        if (_playerRigidbody != null)
        {
            _playerRigidbody.linearVelocity = Vector2.zero;
        }

        // Khối try/finally chống Softlock kẹt màn hình đen hoặc kẹt blocksRaycasts trong mọi tình huống
        try
        {
            // 1. Bật chặn click xuyên UI ngay khi bắt đầu Fade Out
            if (fadeCanvasGroup != null)
            {
                if (!fadeCanvasGroup.gameObject.activeSelf)
                {
                    fadeCanvasGroup.gameObject.SetActive(true);
                }

                fadeCanvasGroup.blocksRaycasts = true;
                fadeCanvasGroup.interactable = true;
            }

            // 2. Fade alpha 0 -> 1 dùng Time.unscaledDeltaTime (không tạo rác GC new WaitForSeconds)
            yield return FadeCanvasAlpha(0f, 1f, fadeOutDuration);

            // 3-6. Tắt phòng cũ, bật phòng mới, dịch chuyển nhân vật, nhảy Cinemachine và phát OnRoomChanged
            ApplyRoomSwitchAndTeleport(targetRoom, targetSpawnPointId);

            // 7. Giữ màn hình đen bằng bộ đếm unscaledDeltaTime (tối ưu GC)
            if (holdBlackDuration > 0f)
            {
                float holdElapsed = 0f;
                while (holdElapsed < holdBlackDuration)
                {
                    holdElapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            // 8. Fade alpha 1 -> 0
            yield return FadeCanvasAlpha(1f, 0f, fadeInDuration);
        }
        finally
        {
            // Đảm bảo tuyệt đối luôn mở khóa raycast và trạng thái chuyển phòng để không bao giờ kẹt màn hình đen
            ResetFadeStateImmediate();
            IsTransitioning = false;
            _transitionCoroutine = null;

            // Chỉ trả lại FreeRoam nếu không có đoạn hội thoại mới vừa kích hoạt khi vào phòng
            if (didLockPlayerMovement
                && GameStateManager.Instance != null
                && (DialogueManager.Instance == null || !DialogueManager.Instance.IsDialogueActive))
            {
                GameStateManager.Instance.ChangeState(GameState.FreeRoam);
            }
        }
    }

    private IEnumerator FadeCanvasAlpha(float fromAlpha, float toAlpha, float duration)
    {
        if (fadeCanvasGroup == null)
        {
            yield break;
        }

        if (duration <= 0f)
        {
            fadeCanvasGroup.alpha = toAlpha;
            yield break;
        }

        fadeCanvasGroup.alpha = fromAlpha;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            fadeCanvasGroup.alpha = Mathf.Lerp(fromAlpha, toAlpha, t);
            yield return null;
        }

        fadeCanvasGroup.alpha = toAlpha;
    }

    // ================= ĐỔI PHÒNG, DỊCH CHUYỂN & CINEMACHINE 3 =================

    private void ApplyRoomSwitchAndTeleport(RoomNode targetRoom, string targetSpawnPointId)
    {
        // Tắt phòng hiện tại (nếu khác phòng đích) rồi bật phòng đích để tối ưu Draw Calls
        if (_currentRoom != null && _currentRoom != targetRoom)
        {
            _currentRoom.SetRoomActive(false);
        }

        targetRoom.SetRoomActive(true);

        Vector3 positionDelta = Vector3.zero;
        if (playerTransform != null)
        {
            Transform spawnPoint = targetRoom.GetSpawnPoint(targetSpawnPointId);
            Vector3 oldPosition = playerTransform.position;

            // Giữ nguyên trục Z của nhân vật 2D để tránh lỗi bị lùi ra sau background hoặc camera
            Vector3 targetPosition = new Vector3(spawnPoint.position.x, spawnPoint.position.y, oldPosition.z);

            // Reset vận tốc Rigidbody2D (API Unity 6 linearVelocity) trước khi đặt vị trí để chống trôi quán tính
            if (_playerRigidbody != null)
            {
                _playerRigidbody.linearVelocity = Vector2.zero;
                _playerRigidbody.angularVelocity = 0f;
                _playerRigidbody.position = new Vector2(targetPosition.x, targetPosition.y);
            }

            playerTransform.position = targetPosition;
            positionDelta = targetPosition - oldPosition;
        }

        // Đồng bộ Cinemachine 3: cập nhật biên giới hạn phòng mới và báo camera nhảy tức thì (không trượt mượt từ phòng cũ)
        UpdateCinemachineForRoom(targetRoom, positionDelta);

        _currentRoom = targetRoom;
        CurrentRoomId = targetRoom.RoomId;
        CurrentSpawnPointId = targetSpawnPointId;
        _hasInitializedRoom = true;

        OnRoomChanged?.Invoke(CurrentRoomId, CurrentSpawnPointId);
    }

    private void UpdateCinemachineForRoom(RoomNode targetRoom, Vector3 playerPositionDelta)
    {
        // Cập nhật BoundingShape2D của CinemachineConfiner2D và xóa cache biên cũ trước khi warp camera
        if (cameraConfiner != null)
        {
            cameraConfiner.BoundingShape2D = targetRoom.CameraBounds;
            cameraConfiner.InvalidateBoundingShapeCache();
        }

        // Báo CinemachineCamera nhảy tức thì theo độ dời của nhân vật, tránh hiệu ứng lia camera xuyên tường
        if (virtualCamera != null && playerTransform != null)
        {
            virtualCamera.OnTargetObjectWarped(playerTransform, playerPositionDelta);
        }
    }

    // ================= KHỞI TẠO & TIỆN ÍCH NỘI BỘ =================

    private void CacheComponents()
    {
        if (playerTransform != null)
        {
            _playerRigidbody = playerTransform.GetComponent<Rigidbody2D>();
        }

        // Dự phòng 1 lần duy nhất trong Awake() nếu người dùng chưa kéo CinemachineCamera trên Inspector
        if (virtualCamera == null)
        {
            virtualCamera = FindFirstObjectByType<CinemachineCamera>(FindObjectsInactive.Include);
        }

        if (cameraConfiner == null && virtualCamera != null)
        {
            cameraConfiner = virtualCamera.GetComponent<CinemachineConfiner2D>();
        }
    }

    private void BuildRoomLookup()
    {
        _roomLookup.Clear();

        if (rooms == null)
        {
            return;
        }

        for (int i = 0; i < rooms.Count; i++)
        {
            RoomNode node = rooms[i];
            if (node == null || string.IsNullOrEmpty(node.RoomId))
            {
                continue;
            }

            if (_roomLookup.ContainsKey(node.RoomId))
            {
                Debug.LogWarning($"[RoomTransitionManager] Trùng mã roomId '{node.RoomId}' trong danh sách rooms.");
            }

            _roomLookup[node.RoomId] = node;
        }
    }

    private void DeactivateAllRoomsExcept(RoomNode activeRoom)
    {
        if (rooms == null)
        {
            return;
        }

        for (int i = 0; i < rooms.Count; i++)
        {
            RoomNode node = rooms[i];
            if (node != null && node != activeRoom)
            {
                node.SetRoomActive(false);
            }
        }
    }

    private void ResetFadeStateImmediate()
    {
        if (fadeCanvasGroup == null)
        {
            return;
        }

        fadeCanvasGroup.alpha = 0f;
        fadeCanvasGroup.blocksRaycasts = false;
        fadeCanvasGroup.interactable = false;
    }
}
