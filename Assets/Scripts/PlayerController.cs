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

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>(); // Sẽ lấy Animator nếu Main có gắn hiệu ứng chạy
    }

    // ================= LẮNG NGHE SỰ KIỆN =================

    private void OnEnable()
    {
        if (GameStateManager.Instance != null)
        {
            // Bắt đầu dóng tai nghe lệnh từ GameStateManager
            GameStateManager.Instance.OnGameStateChanged += HandleGameStateChanged;
        }
    }

    private void OnDisable()
    {
        if (GameStateManager.Instance != null)
        {
            // Hủy nghe lệnh khi tắt/chuyển scene để chống tràn bộ nhớ (Memory Leak)
            GameStateManager.Instance.OnGameStateChanged -= HandleGameStateChanged;
        }
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
            rb.velocity = Vector2.zero;
            
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
        // Nếu bị khóa (đang nói chuyện) thì return luôn, không đọc phím nữa
        if (!canMove) return;

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
        // Hàm này chuyên dùng để di chuyển Rigidbody2D cho mượt
        rb.velocity = movementInput * moveSpeed;
    }
}