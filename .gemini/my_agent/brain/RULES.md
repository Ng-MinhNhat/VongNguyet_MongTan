# QUY TẮC DỰ ÁN: VỌNG NGUYỆT: MỘNG TÀN (UNITY 2D PUZZLE RPG)

## 1. Ngữ cảnh & Công nghệ (Tech Stack)
- Engine: Unity 2D (C#).
- UI: Sử dụng Unity UI (uGUI) kết hợp với TextMeshPro (`TMP_Text`, `TMP_InputField`).
- Serialization (Tuần tự hóa dữ liệu): BẮT BUỘC dùng `UnityEngine.JsonUtility`. Tuyệt đối không dùng `Dictionary` trong các class cần serialize (hãy dùng `List` chứa các class `[System.Serializable]`).
- Xử lý bất đồng bộ: Ưu tiên dùng `Coroutine` (`IEnumerator`) và `UnityWebRequest` để tương thích tốt nhất với vòng đời của MonoBehaviour.

## 2. Kiến trúc Lõi (Core Architecture) - KHÔNG ĐƯỢC PHÁ VỠ
Mọi logic mới phải kết nối trực tiếp với các Singleton (Đơn bản) đã có sẵn:
- `GameStateManager.Instance`: Quản lý trạng thái trung tâm.
  - Túi đồ: `AddItem(string itemID)`, `RemoveItem(string itemID)`, `HasItem(string itemID)`. Sự kiện: `OnInventoryChanged` (`System.Action`).
  - Câu đố & Rẽ nhánh: `SetPuzzleState(string puzzleID, bool state)`, `GetPuzzleState(string puzzleID)`.
  - Lưu trữ: `SaveGameProgress()`, `LoadLocalGame()`.
- `CloudAPIManager.Instance`: Xử lý đồng bộ API lưu game đám mây và thống kê kết thúc (`SubmitEndingCoroutine`).

## 3. Quy chuẩn Viết Code (Coding Conventions)
- Tách biệt rõ ràng giữa Data (Dữ liệu JSON), Logic (Xử lý) và UI (Hiển thị).
- Luôn thêm thuộc tính `[Header("...")]` và `[Tooltip("...")]` bằng tiếng Việt rõ ràng cho các biến `public` hoặc `[SerializeField]` để dễ kéo thả trên Unity Inspector (Thanh kiểm tra).
- Luôn kiểm tra `null` trước khi gọi sự kiện hoặc đối tượng UI để tránh lỗi `NullReferenceException`.
- Giải thích ngắn gọn cách gắn Script vào GameObject (Đối tượng trò chơi) ở cuối mỗi câu trả lời.