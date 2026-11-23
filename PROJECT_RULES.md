# PROJECT RULES — VỌNG NGUYỆT: MỘNG TÀN

## 1. Bối cảnh & Kiến trúc (Context & Architecture)
- **Engine:** Unity 6 (URP 2D), C#, Cinemachine 3 (`CinemachineCamera`), TextMeshPro, Input System.
- **Nền tảng mục tiêu:** PC (Windows) & Mobile (Android) — mọi tương tác phải hỗ trợ cả chuột/phím (`E`, click) và chạm cảm ứng (Touch).
- **Tổ chức màn chơi:** Kiến trúc Đa Scene (Multi-Scene). Các Singleton quản lý toàn cục (`GameStateManager`, `InventoryManager`) bắt buộc dùng `DontDestroyOnLoad` và chặn nhân bản trong `Awake()`.
- **Định hướng mở rộng:** Phát triển theo từng Chapter (`Ch1`, `Ch2`...) và sẵn sàng đồng bộ Webshop Giftcode + MySQL (Entity Framework Core) về sau.

## 2. Quy ước thư mục & Đặt tên (Folder & Naming Conventions)
- `Assets/Scripts/`: Mã nguồn cốt lõi (`GameStateManager`, `GameSaveData`, `DialogueManager`, `DialogueData`, `ItemData`, `PlayerController`, namespace `VongNguyet.VN`).
- `Assets/Scripts/Inventory/`: `InventoryManager`, `WorldItemPickup` và các script kho đồ/UI kho đồ.
- `Assets/Data/Items/`: `ScriptableObject` kiểu `ItemData`. Đặt `itemId` dạng snake_case không dấu kèm tiền tố Chapter (VD: `ch1_key_ketsat`, `ch1_key_kho`, `ch1_thuoc_ha_sot`).
- `Assets/Resources/Dialogues/`: File kịch bản `.json` (`Scene01..Scene06`, `KetSat_TuongTac`).
- `Assets/Resources/Characters/`: Sprite `FullBody` và `Portraits` nạp qua `VNCharacterLibrary`.
- `Assets/Editor/`: Chứa các Editor Script công cụ (không đóng gói vào Build).

## 3. Hiệu suất Unity 6 & Clean Code (Performance Standards)
- **API Unity 6:** Dùng `Rigidbody2D.linearVelocity` (không dùng `.velocity` cũ), tránh các API đã lỗi thời như `FindObjectOfType()`.
- **Giảm Garbage Collection (GC):**
  - TUYỆT ĐỐI không gọi `Find()`, `GetComponent()`, `Resources.Load()` hoặc cấp phát `new` trong `Update()` / `FixedUpdate()`.
  - Cache sẵn `Component` trong `Awake()` và cache `WaitForSeconds` trước vòng lặp `Coroutine`.
  - Dùng mô hình hướng sự kiện (`event Action`) để cập nhật UI (`OnInventoryChanged`, `OnPuzzleStateChanged`, `OnGameStateChanged`) thay vì kiểm tra liên tục trong `Update()`.
  - Dùng Object Pooling (tái sử dụng ô slot) cho Inventory UI thay vì `Instantiate`/`Destroy` liên tục.
- **uGUI & TextMeshPro:** Tắt `Raycast Target` trên ảnh trang trí không tương tác; tách Canvas tĩnh và Canvas động; chặn bấm xuyên UI bằng `EventSystem.current.IsPointerOverGameObject()`.
- **Nguyên tắc code:** Tuân thủ SOLID, DRY; luôn hủy đăng ký event trong `OnDisable()` hoặc `OnDestroy()` để chống Memory Leak khi chuyển Scene.
- **Bất đồng bộ & Quy chuẩn Inspector:**
  - Ưu tiên dùng `Coroutine` (`IEnumerator`) và `UnityWebRequest` để tương thích tốt với vòng đời `MonoBehaviour`.
  - Tách biệt rõ Data (JSON / ScriptableObject), Logic (Xử lý) và UI (`TMP_Text`, `TMP_InputField`, uGUI).
  - Luôn thêm `[Header("...")]` và `[Tooltip("...")]` bằng tiếng Việt cho biến `public` hoặc `[SerializeField]`.
  - Luôn kiểm tra `null` trước khi gọi sự kiện hoặc thao tác đối tượng UI để tránh `NullReferenceException`.
  - Cuối mỗi câu trả lời có tạo/sửa Script, giải thích ngắn gọn cách gắn Script và kéo thả tham chiếu trên Unity Inspector.

## 3.5. Kiến trúc Lõi hiện có (Core Singletons)
- `InventoryManager.Instance`: Quản lý toàn bộ Kho đồ (`AddItem`, `RemoveItem`, `HasItem`, `GetItemData`, `GetAllSlots`, sự kiện `OnInventoryChanged`). Không viết thêm logic kho đồ vào `GameStateManager`.
- `GameStateManager.Instance`: Quản lý trạng thái game (`ChangeState`), cờ cốt truyện (`TriggerPuzzleState`, `SetPuzzleState`/`GetPuzzleState` dạng `bool`), câu đố đa giai đoạn (`SetPuzzleState(string, string)`, `GetPuzzleStateString`, sự kiện `OnPuzzleStateChanged`), chống hồi sinh vật phẩm (`MarkObjectConsumed`, `IsObjectConsumed`), và Lưu/Tải (`SaveGameProgress`, `LoadLocalGame`).
- `DialogueManager.Instance`: Điều khiển hội thoại JSON (`StartDialogue`, `IsDialogueActive`, `OnDialogueStarted`, `OnDialogueEnded`).
- *Lưu ý:* `CloudAPIManager` (đồng bộ đám mây / Webshop) **chưa được tạo** — không tự ý gọi khi chưa có yêu cầu triển khai.

## 4. Dữ liệu & Lưu trữ (Data & Persistence Rules)
- **Tương thích `JsonUtility`:** Không dùng `Dictionary` trực tiếp trong class dữ liệu Save; dùng `List<T>` cặp khóa-giá trị (VD: `List<PuzzleStageEntry>`, `List<InventorySlotData>`).
- **Cảnh báo `JsonUtility` Edge Case:** Unity `JsonUtility` tự khởi tạo object rỗng cho các trường con vắng mặt trong JSON — luôn kiểm tra `string.IsNullOrEmpty(...)` trên từng field con thay vì chỉ kiểm tra `!= null`.
- **Chuẩn mở rộng Webshop/DB:** Mọi `InventorySlotData` phải giữ các trường `itemId`, `quantity`, `source` (`"Gameplay"` hoặc `"Webshop_Giftcode"`).
- **Tương thích ngược:** Giữ nguyên cấu trúc tương thích với các file `Scene01`–`Scene06.json` hiện có trừ khi được yêu cầu nâng cấp rõ ràng.

## 5. Vùng cấm tuyệt đối cho AI Agent (Hard Constraints)
- **KHÔNG** tự ý sửa trực tiếp mã YAML trong file `.unity`, `.prefab`, `.asset` hoặc chỉnh sửa/xóa file `.meta`.
- **KHÔNG** tự đổi tên hoặc di chuyển file `.cs` bên ngoài Unity Editor (tránh làm hỏng GUID gây lỗi *Missing Script*).
- **KHÔNG** chỉnh sửa bất kỳ file nào trong `Assets/TextMesh Pro/`.
- **Tạo UI tự động:** Khi được yêu cầu dựng UI mới, viết một Unity Editor Script đặt trong `Assets/Editor/` (tạo menu trong Unity để người dùng bấm sinh Canvas/Prefab chuẩn GUID).
- **Phạm vi sửa đổi:** Chỉ sửa đúng file được yêu cầu; giữ comment ngắn gọn bằng tiếng Việt ngay trong khối code để giải thích các đoạn logic quan trọng hoặc Edge Cases.

## 6. Kiểm soát Edge Cases (Bắt buộc rà soát trước khi trả code)
- **Chống Spam-click / Double-pickup:** Khóa cờ trạng thái ngay thời điểm bắt đầu tương tác, chặn kích hoạt khi `DialogueManager.Instance.IsDialogueActive == true`.
- **Chống Item Respawn:** Vật phẩm ngoài bản đồ đã nhặt phải ghi ID vào `consumedWorldObjectIds` trong `GameStateManager`.
- **Chống Softlock:** Không trừ vật phẩm cốt truyện (`ItemCategory.KeyItem`) trước khi xác nhận điều kiện giải đố thành công.