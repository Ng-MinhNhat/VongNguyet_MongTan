using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))] // Tự động thêm Rigidbody2D nếu chưa có
public class PlayerController : MonoBehaviour
{
    [Header("Thông số di chuyển")]
    [SerializeField] private float moveSpeed = 5f;

    private Rigidbody2D rb;
    private Vector2 movementInput;
    private Animator animator;

    // Biến kiểm soát: true = được đi, false = đứng im nghe thoại
    private bool canMove = true;
    private bool isSubscribed = false;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>(); // Sẽ lấy Animator nếu Main có gắn hiệu ứng chạy
    }

    // ================= LẮNG NGHE SỰ KIỆN =================

    private void OnEnable()
    {
        TrySubscribeGameState();
    }

    private void Start()
    {
        // Đảm bảo đăng ký được cả khi PlayerController.OnEnable chạy trước GameStateManager.Awake
        TrySubscribeGameState();
    }

    private void TrySubscribeGameState()
    {
        if (isSubscribed || GameStateManager.Instance == null) return;

        GameStateManager.Instance.OnGameStateChanged += HandleGameStateChanged;
        isSubscribed = true;
        HandleGameStateChanged(GameStateManager.Instance.CurrentState);
    }

    private void OnDisable()
    {
        if (isSubscribed && GameStateManager.Instance != null)
        {
            // Hủy nghe lệnh khi tắt/chuyển scene để chống tràn bộ nhớ (Memory Leak)
            GameStateManager.Instance.OnGameStateChanged -= HandleGameStateChanged;
        }
        isSubscribed = false;
    }

    // ================= XỬ LÝ KHÓA CHÂN =================

    private void HandleGameStateChanged(GameState newState)
    {
        // Đèn xanh (FreeRoam) thì true, Đèn đỏ (Cutscene) thì false
        canMove = (newState == GameState.FreeRoam);

        if (!canMove)
        {
            // Hãm phanh khẩn cấp: Ép vận tốc về 0 để Main không bị trượt đi
            movementInput = Vector2.zero;
            rb.linearVelocity = Vector2.zero;
            
            // Dừng hoạt ảnh chạy (nếu có)
            if (animator != null)
            {
                animator.SetFloat("Speed", 0f);
            }
        }
    }

    // ================= NHẬN NÚT BẤM (INPUT) =================

    private void Update()
    {
        // Edge Case: khóa di chuyển và hãm vận tốc ngay khi đang hội thoại, đang Fade chuyển phòng hoặc đang mở Popup Kho đồ
        if (IsMovementBlocked())
        {
            StopMovementImmediate();
            return;
        }

        // Nhận phím WASD hoặc phím mũi tên
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveY = Input.GetAxisRaw("Vertical");

        // .normalized để đi chéo không bị nhanh hơn đi thẳng
        movementInput = new Vector2(moveX, moveY).normalized;

        // Cập nhật hoạt ảnh chạy (tuỳ chọn)
        if (animator != null)
        {
            animator.SetFloat("Speed", movementInput.sqrMagnitude);
            
            // Lật mặt nhân vật (Quay trái/phải)
            if (moveX > 0.1f)
                transform.localScale = new Vector3(1, 1, 1);
            else if (moveX < -0.1f)
                transform.localScale = new Vector3(-1, 1, 1);
        }
    }

    // ================= ÁP DỤNG VẬT LÝ =================

    private void FixedUpdate()
    {
        if (IsMovementBlocked())
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        // Hàm này chuyên dùng để di chuyển Rigidbody2D cho mượt
        rb.linearVelocity = movementInput * moveSpeed;
    }

    private bool IsMovementBlocked()
    {
        if (!canMove)
            return true;

        if (RoomTransitionManager.Instance != null && RoomTransitionManager.Instance.IsTransitioning)
            return true;

        if (InventoryUIController.Instance != null && InventoryUIController.Instance.IsPopupOpen)
            return true;

        // Khóa di chuyển khi người chơi đang nhập mật mã trên bảng Keypad
        if (KeypadUIController.Instance != null && KeypadUIController.Instance.IsOpen)
            return true;

        return false;
    }

    private void StopMovementImmediate()
    {
        movementInput = Vector2.zero;
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
        }

        if (animator != null)
        {
            animator.SetFloat("Speed", 0f);
        }
    }
}