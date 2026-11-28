using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Điều khiển toàn bộ giao diện Kho đồ theo mô hình uGUI + TextMeshPro:
/// 1B (Popup Grid dùng Object Pooling) + 2B (Detail Modal) + HUD vật phẩm đang cầm ngoài Scene.
/// Hướng sự kiện hoàn toàn (Event-Driven), chỉ sống theo Canvas của Scene hiện tại.
/// </summary>
public class InventoryUIController : MonoBehaviour
{
    private const string WebshopGiftcodeSource = "Webshop_Giftcode";

    // Singleton gọn nhẹ theo vòng đời Canvas của Scene hiện tại (KHÔNG dùng DontDestroyOnLoad)
    public static InventoryUIController Instance { get; private set; }

    /// <summary>Trạng thái Popup kho đồ hiện đang mở hay đóng.</summary>
    public bool IsPopupOpen { get; private set; }

    /// <summary>Frame gần nhất Popup Kho đồ vừa đóng (dùng để PauseMenuController chống xung đột phím Escape cùng frame).</summary>
    public int LastClosedFrame { get; private set; } = -1;

    /// <summary>ID vật phẩm người chơi đang cầm trên tay để tương tác câu đố ngoài Scene (rỗng nếu không cầm).</summary>
    public string EquippedItemId { get; private set; } = string.Empty;

    /// <summary>Phát khi người chơi cầm vật phẩm mới hoặc bỏ cầm vật phẩm (truyền string.Empty khi bỏ cầm).</summary>
    public event Action<string> OnEquippedItemChanged;

    /// <summary>Phát khi người chơi bấm Sử dụng vật phẩm tiêu hao/đặc biệt để hệ thống khác lắng nghe.</summary>
    public event Action<string> OnItemUseRequested;

    [Header("Điều khiển Popup Kho đồ (1B)")]
    [Tooltip("GameObject gốc chứa toàn bộ khung Popup lưới vật phẩm (đặt tách biệt với GameObject gắn script này).")]
    [SerializeField] private GameObject popupRoot;

    [Tooltip("Nút mở/đóng nhanh túi đồ trên HUD (hỗ trợ cả click chuột PC và chạm cảm ứng Mobile).")]
    [SerializeField] private Button openBagButton;

    [Tooltip("Nút đóng Popup kho đồ (VD: nút X góc trên khung Popup).")]
    [SerializeField] private Button closeBagButton;

    [Tooltip("Văn bản TextMeshPro trên nút túi đồ hiển thị tổng số ô đang có vật phẩm.")]
    [SerializeField] private TMP_Text bagCountBadgeText;

    [Tooltip("Transform cha (có GridLayoutGroup) dùng để chứa các ô vật phẩm sinh từ Object Pool.")]
    [SerializeField] private Transform slotContainer;

    [Tooltip("Prefab một ô vật phẩm có gắn script InventorySlotUI.")]
    [SerializeField] private InventorySlotUI slotPrefab;

    [Tooltip("Số lượng ô vật phẩm khởi tạo sẵn trong Pool ở Awake() để tái sử dụng không sinh rác GC.")]
    [SerializeField] private int initialPoolSize = 12;

    [Header("Khung Chi tiết vật phẩm (2B)")]
    [Tooltip("GameObject gốc của khung Modal hiển thị thông tin chi tiết vật phẩm đang xem.")]
    [SerializeField] private GameObject detailPanel;

    [Tooltip("Ảnh hiển thị biểu tượng lớn của vật phẩm đang xem (nên tắt Raycast Target).")]
    [SerializeField] private Image detailIconImage;

    [Tooltip("Tên hiển thị của vật phẩm đang xem.")]
    [SerializeField] private TMP_Text detailNameText;

    [Tooltip("Mô tả gợi ý công dụng của vật phẩm đang xem.")]
    [SerializeField] private TMP_Text detailDescriptionText;

    [Tooltip("Nhãn nguồn gốc vật phẩm (chỉ bật khi source == \"Webshop_Giftcode\").")]
    [SerializeField] private TMP_Text detailSourceText;

    [Tooltip("Nút thực hiện hành động Cầm lên / Sử dụng / Bỏ cầm đối với vật phẩm đang xem.")]
    [SerializeField] private Button useOrEquipButton;

    [Tooltip("Chữ hiển thị trên nút hành động (tự đổi theo trạng thái Cầm lên / Sử dụng / Bỏ cầm).")]
    [SerializeField] private TMP_Text useOrEquipButtonText;

    [Header("HUD Vật phẩm đang cầm ngoài Scene")]
    [Tooltip("Khung HUD góc màn hình hiển thị vật phẩm đang cầm để bấm vào vật thể câu đố trong Scene.")]
    [SerializeField] private GameObject equippedHudRoot;

    [Tooltip("Ảnh biểu tượng của vật phẩm đang cầm trên HUD góc màn hình (nên tắt Raycast Target).")]
    [SerializeField] private Image equippedHudIcon;

    [Tooltip("Nút bấm trên HUD để bỏ cầm vật phẩm hiện tại.")]
    [SerializeField] private Button unequipHudButton;

    [Header("Soi & Ghép đồ (Inspect & Combine)")]
    [Tooltip("Nút mở bảng Soi manh mối (SOI) cho vật phẩm đang xem có IsInspectable == true.")]
    [SerializeField] private Button inspectButton;

    [Tooltip("Nút bắt đầu chế độ ghép 2 bước (GHÉP) cho vật phẩm đang xem.")]
    [SerializeField] private Button combineButton;

    [Tooltip("Nút hủy chế độ ghép (HỦY GHÉP), chỉ hiển thị khi đang chờ người chơi chọn vật phẩm thứ 2.")]
    [SerializeField] private Button cancelCombineButton;

    [Tooltip("Văn bản TextMeshPro hiển thị hướng dẫn chọn món thứ 2 hoặc thông báo kết quả ghép.")]
    [SerializeField] private TMP_Text combineStatusText;

    [Tooltip("Thời gian (giây thực tế unscaled) hiển thị thông báo kết quả ghép trước khi tự ẩn.")]
    [SerializeField] private float combineFeedbackDuration = 2.0f;

    [Header("Nhãn văn bản hiển thị (Tùy chọn)")]
    [Tooltip("Chữ hiện trên nút hành động khi cầm vật phẩm cốt truyện (KeyItem) hoặc vật phẩm trang bị.")]
    [SerializeField] private string equipButtonLabel = "Cầm lên";

    [Tooltip("Chữ hiện trên nút hành động khi bấm dùng vật phẩm tiêu hao (Consumable).")]
    [SerializeField] private string useButtonLabel = "Sử dụng";

    [Tooltip("Chữ hiện trên nút hành động khi vật phẩm đang xem trùng với vật phẩm đang cầm.")]
    [SerializeField] private string unequipButtonLabel = "Bỏ cầm";

    [Tooltip("Nhãn nguồn hiển thị trong khung chi tiết nếu vật phẩm nhận từ Webshop Giftcode.")]
    [SerializeField] private string giftcodeSourceLabel = "Quà tặng Webshop / Giftcode";

    // Object Pool tái sử dụng toàn bộ ô InventorySlotUI — tuyệt đối không Destroy khi làm mới kho đồ
    private readonly List<InventorySlotUI> _slotPool = new List<InventorySlotUI>();

    // Cache ánh xạ O(1) từ itemId -> InventorySlotData mỗi lần Refresh để tránh duyệt tìm lặp khi bấm ô
    private readonly Dictionary<string, InventorySlotData> _slotDataCache = new Dictionary<string, InventorySlotData>();

    // Cache sẵn delegate callback 1 lần duy nhất ở Awake() để truyền vào slot.Bind() mà không cấp phát rác GC
    private Action<InventorySlotUI> _cachedSlotClickHandler;

    // Lưu đúng tham chiếu Singleton đã đăng ký để hủy đăng ký an toàn trong OnDisable()/OnDestroy()
    private InventoryManager _subscribedInventoryManager;
    private DialogueManager _subscribedDialogueManager;

    private string _inspectedItemId = string.Empty;
    private bool _areButtonListenersRegistered;

    // Trạng thái ghép 2 bước (thân thiện cả PC & Mobile, không dùng Drag-and-Drop)
    private bool _isCombineMode;
    private ItemData _combineFirstItem;
    private Coroutine _feedbackCoroutine;

    private void Awake()
    {
        // Chặn trùng lặp Singleton trong cùng một Scene
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[InventoryUIController] Phát hiện InventoryUIController trùng lặp trong Scene, tự động hủy bản thừa.");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        _cachedSlotClickHandler = HandleSlotClicked;

        RegisterButtonListeners();
        InitializeSlotPool();
        ResetInitialVisualState();
    }

    private void OnEnable()
    {
        TrySubscribeCoreEvents();
    }

    private void Start()
    {
        // Edge Case Execution Order: đảm bảo đăng ký được cả khi OnEnable() chạy trước Awake() của các Manager
        TrySubscribeCoreEvents();
        RefreshInventoryUI();
    }

    private void OnDisable()
    {
        // Luôn hủy đăng ký sự kiện và dọn trạng thái ghép / Coroutine khi tắt object chống Memory Leak & kẹt trạng thái
        UnsubscribeCoreEvents();
        ExitCombineMode(false);
        StopCombineFeedbackCoroutine();
        HideCombineStatusText();
    }

    private void OnDestroy()
    {
        UnsubscribeCoreEvents();
        UnregisterButtonListeners();

        // Dọn sạch tham chiếu Singleton theo đúng vòng đời Canvas hiện tại
        if (Instance == this)
        {
            Instance = null;
        }

        OnEquippedItemChanged = null;
        OnItemUseRequested = null;
    }

    private void Update()
    {
        // Khóa toàn bộ phím tắt Kho đồ (I / Escape) khi game đang mở Pause Menu
        if (PauseMenuController.Instance != null && PauseMenuController.Instance.IsPaused)
        {
            return;
        }

        // Chuỗi ưu tiên Esc (tầng 1 Inspect -> tầng 2 Combine -> tầng 3 Inventory -> tầng 4 Pause):
        // Nếu bảng Soi đang mở hoặc vừa đóng ở đúng frame hiện tại -> bỏ qua toàn bộ phím tắt để không đóng đè Kho đồ
        if (ItemInspectUIController.Instance != null
            && (ItemInspectUIController.Instance.IsOpen || ItemInspectUIController.Instance.LastClosedFrame == Time.frameCount))
        {
            return;
        }

        // Tối ưu GC: tuyệt đối không gọi Find(), GetComponent() hay tạo chuỗi trong Update(), chỉ bắt phím tắt PC
        if (WasToggleBagKeyPressed())
        {
            // Edge Case: chặn mở túi bằng phím I khi đang chuyển phòng hoặc đang mở bảng Keypad
            if (!IsPopupOpen && RoomTransitionManager.Instance != null && RoomTransitionManager.Instance.IsTransitioning)
            {
                return;
            }

            if (!IsPopupOpen && KeypadUIController.Instance != null && KeypadUIController.Instance.IsOpen)
            {
                return;
            }

            TogglePopup();
            return;
        }

        if (IsPopupOpen && WasCloseBagKeyPressed())
        {
            // Ưu tiên tầng 2 (Combine): đang chờ chọn món thứ 2 -> bấm Escape chỉ hủy chế độ ghép, chưa đóng Kho đồ
            if (_isCombineMode)
            {
                ExitCombineMode(true);
                return;
            }

            // Ưu tiên tầng 3 (Inventory): đóng Kho đồ và ghi nhận LastClosedFrame để PauseMenu không bật cùng frame
            ClosePopup();
        }
    }

    // ================= KHỞI TẠO POOL & SỰ KIỆN =================

    private void InitializeSlotPool()
    {
        if (slotPrefab == null || slotContainer == null)
        {
            Debug.LogWarning("[InventoryUIController] Chưa gán slotPrefab hoặc slotContainer trên Inspector.");
            return;
        }

        int targetCount = Mathf.Max(0, initialPoolSize);
        if (_slotPool.Capacity < targetCount)
        {
            _slotPool.Capacity = targetCount;
        }

        for (int i = _slotPool.Count; i < targetCount; i++)
        {
            CreatePooledSlot();
        }
    }

    private InventorySlotUI CreatePooledSlot()
    {
        if (slotPrefab == null || slotContainer == null)
        {
            return null;
        }

        InventorySlotUI newSlot = Instantiate(slotPrefab, slotContainer);
        if (!newSlot.gameObject.activeSelf)
        {
            newSlot.gameObject.SetActive(true);
        }

        newSlot.Clear();
        _slotPool.Add(newSlot);
        return newSlot;
    }

    private void ResetInitialVisualState()
    {
        IsPopupOpen = false;
        _inspectedItemId = string.Empty;
        EquippedItemId = string.Empty;

        SetGameObjectActiveSafe(popupRoot, false);
        SetGameObjectActiveSafe(detailPanel, false);
        SetGameObjectActiveSafe(equippedHudRoot, false);

        if (equippedHudIcon != null)
        {
            equippedHudIcon.sprite = null;
            equippedHudIcon.enabled = false;
        }

        if (bagCountBadgeText != null)
        {
            // Tối ưu GC: dùng SetText của TextMeshPro thay vì count.ToString()
            bagCountBadgeText.SetText("{0}", 0);
        }

        ExitCombineMode(false);
        HideCombineStatusText();
        UpdateInspectAndCombineButtons(null);
    }

    private void RegisterButtonListeners()
    {
        if (_areButtonListenersRegistered)
        {
            return;
        }

        if (openBagButton != null) openBagButton.onClick.AddListener(TogglePopup);
        if (closeBagButton != null) closeBagButton.onClick.AddListener(ClosePopup);
        if (useOrEquipButton != null) useOrEquipButton.onClick.AddListener(HandleUseOrEquipClicked);
        if (unequipHudButton != null) unequipHudButton.onClick.AddListener(ClearEquippedItem);
        if (inspectButton != null) inspectButton.onClick.AddListener(HandleInspectButtonClicked);
        if (combineButton != null) combineButton.onClick.AddListener(HandleCombineButtonClicked);
        if (cancelCombineButton != null) cancelCombineButton.onClick.AddListener(HandleCancelCombineClicked);

        _areButtonListenersRegistered = true;
    }

    private void UnregisterButtonListeners()
    {
        if (!_areButtonListenersRegistered)
        {
            return;
        }

        if (openBagButton != null) openBagButton.onClick.RemoveListener(TogglePopup);
        if (closeBagButton != null) closeBagButton.onClick.RemoveListener(ClosePopup);
        if (useOrEquipButton != null) useOrEquipButton.onClick.RemoveListener(HandleUseOrEquipClicked);
        if (unequipHudButton != null) unequipHudButton.onClick.RemoveListener(ClearEquippedItem);
        if (inspectButton != null) inspectButton.onClick.RemoveListener(HandleInspectButtonClicked);
        if (combineButton != null) combineButton.onClick.RemoveListener(HandleCombineButtonClicked);
        if (cancelCombineButton != null) cancelCombineButton.onClick.RemoveListener(HandleCancelCombineClicked);

        _areButtonListenersRegistered = false;
    }

    private void TrySubscribeCoreEvents()
    {
        if (_subscribedInventoryManager == null && InventoryManager.Instance != null)
        {
            _subscribedInventoryManager = InventoryManager.Instance;
            _subscribedInventoryManager.OnInventoryChanged += HandleInventoryChanged;
        }

        if (_subscribedDialogueManager == null && DialogueManager.Instance != null)
        {
            _subscribedDialogueManager = DialogueManager.Instance;
            _subscribedDialogueManager.OnDialogueStarted += HandleDialogueStarted;
        }
    }

    private void UnsubscribeCoreEvents()
    {
        if (_subscribedInventoryManager != null)
        {
            _subscribedInventoryManager.OnInventoryChanged -= HandleInventoryChanged;
            _subscribedInventoryManager = null;
        }

        if (_subscribedDialogueManager != null)
        {
            _subscribedDialogueManager.OnDialogueStarted -= HandleDialogueStarted;
            _subscribedDialogueManager = null;
        }
    }

    private void HandleInventoryChanged()
    {
        RefreshInventoryUI();
    }

    private void HandleDialogueStarted(string sequenceId)
    {
        // Edge Case: tự động đóng kho đồ ngay khi hội thoại / Cutscene bắt đầu để tránh che khung thoại
        ClosePopup();
    }

    // ================= ĐIỀU KHIỂN POPUP (1B) =================

    /// <summary>Đóng/mở nhanh Popup kho đồ.</summary>
    public void TogglePopup()
    {
        if (IsPopupOpen)
        {
            ClosePopup();
        }
        else
        {
            OpenPopup();
        }
    }

    /// <summary>Mở Popup kho đồ. Tự động chặn nếu đang có hội thoại hoạt động hoặc đang chuyển phòng.</summary>
    public void OpenPopup()
    {
        // Edge Case: chặn mở kho đồ khi đang Pause, đang hội thoại / Cutscene, đang chuyển phòng hoặc đang mở Keypad
        if (PauseMenuController.Instance != null && PauseMenuController.Instance.IsPaused)
        {
            return;
        }

        if (DialogueManager.Instance != null && DialogueManager.Instance.IsDialogueActive)
        {
            return;
        }

        if (RoomTransitionManager.Instance != null && RoomTransitionManager.Instance.IsTransitioning)
        {
            return;
        }

        // Edge Case: chặn mở túi đồ đè lên bảng nhập mật mã Keypad hoặc bảng Soi manh mối
        if (KeypadUIController.Instance != null && KeypadUIController.Instance.IsOpen)
        {
            return;
        }

        if (ItemInspectUIController.Instance != null && ItemInspectUIController.Instance.IsOpen)
        {
            return;
        }

        if (IsPopupOpen)
        {
            return;
        }

        // Bảo đảm đã lắng nghe sự kiện trong trường hợp Manager khởi tạo muộn
        TrySubscribeCoreEvents();

        IsPopupOpen = true;
        SetGameObjectActiveSafe(popupRoot, true);
        RefreshInventoryUI();
    }

    /// <summary>Đóng Popup kho đồ và dọn khung chi tiết (2B), chế độ ghép đang mở.</summary>
    public void ClosePopup()
    {
        ExitCombineMode(false);
        StopCombineFeedbackCoroutine();
        HideCombineStatusText();

        if (!IsPopupOpen && (popupRoot == null || !popupRoot.activeSelf))
        {
            ClearInspection();
            return;
        }

        // Ghi nhận frame vừa đóng để PauseMenuController không mở đè khi cùng bắt phím Escape trong 1 frame
        LastClosedFrame = Time.frameCount;

        IsPopupOpen = false;
        ClearInspection();
        SetGameObjectActiveSafe(popupRoot, false);
    }

    // ================= ĐỒNG BỘ DỮ LIỆU & OBJECT POOLING =================

    /// <summary>
    /// Làm mới toàn bộ ô vật phẩm từ Object Pool, cập nhật badge số lượng và đồng bộ trạng thái cầm/đang xem.
    /// </summary>
    public void RefreshInventoryUI()
    {
        _slotDataCache.Clear();

        InventoryManager manager = InventoryManager.Instance;
        int occupiedCount = 0;

        if (manager != null)
        {
            IReadOnlyCollection<InventorySlotData> slots = manager.GetAllSlots();
            if (slots != null)
            {
                foreach (InventorySlotData slotData in slots)
                {
                    // Edge Case JsonUtility / dữ liệu lỗi: bỏ qua ô trống hoặc itemId không hợp lệ
                    if (slotData == null || string.IsNullOrEmpty(slotData.itemId) || slotData.quantity <= 0)
                    {
                        continue;
                    }

                    ItemData itemData = manager.GetItemData(slotData.itemId);
                    if (itemData == null)
                    {
                        continue;
                    }

                    _slotDataCache[slotData.itemId] = slotData;

                    // Tự mở rộng Pool nếu kho đồ vượt quá initialPoolSize (tuyệt đối không Destroy)
                    InventorySlotUI slotUI = occupiedCount < _slotPool.Count
                        ? _slotPool[occupiedCount]
                        : CreatePooledSlot();

                    if (slotUI != null)
                    {
                        if (!slotUI.gameObject.activeSelf)
                        {
                            slotUI.gameObject.SetActive(true);
                        }

                        bool isSelected = string.Equals(slotData.itemId, _inspectedItemId, StringComparison.Ordinal);
                        slotUI.Bind(itemData, slotData, isSelected, _cachedSlotClickHandler);
                    }

                    occupiedCount++;
                }
            }
        }

        // Dọn các ô dư trong Pool bằng Clear(); giữ hiển thị tối thiểu initialPoolSize ô nền cho đẹp lưới
        int minVisibleSlots = Mathf.Max(0, initialPoolSize);
        for (int i = occupiedCount; i < _slotPool.Count; i++)
        {
            InventorySlotUI extraSlot = _slotPool[i];
            if (extraSlot == null)
            {
                continue;
            }

            extraSlot.Clear();
            bool shouldStayVisible = i < minVisibleSlots;
            if (extraSlot.gameObject.activeSelf != shouldStayVisible)
            {
                extraSlot.gameObject.SetActive(shouldStayVisible);
            }
        }

        // Tối ưu GC: dùng SetText("{0}", count) của TextMeshPro để không cấp phát chuỗi mới
        if (bagCountBadgeText != null)
        {
            bagCountBadgeText.SetText("{0}", occupiedCount);
        }

        // Edge Case đồng bộ khi mất vật phẩm: tự xóa trạng thái đang cầm nếu vật phẩm không còn trong kho
        if (!string.IsNullOrEmpty(EquippedItemId))
        {
            if (manager == null || !manager.HasItem(EquippedItemId))
            {
                ClearEquippedItem();
            }
            else
            {
                UpdateEquippedHudVisual(manager.GetItemData(EquippedItemId));
            }
        }

        // Edge Case đồng bộ khi mất vật phẩm nguồn trong lúc đang ở chế độ Ghép
        if (_isCombineMode && (_combineFirstItem == null || manager == null || !manager.HasItem(_combineFirstItem.itemId)))
        {
            ExitCombineMode(true);
        }

        // Edge Case đồng bộ khi mất vật phẩm đang xem trong Detail Modal (2B)
        if (!string.IsNullOrEmpty(_inspectedItemId))
        {
            if (manager == null || !manager.HasItem(_inspectedItemId))
            {
                ClearInspection();
            }
            else if (IsPopupOpen && detailPanel != null && detailPanel.activeSelf)
            {
                ShowItemDetail(_inspectedItemId);
            }
        }
        else
        {
            SetGameObjectActiveSafe(detailPanel, false);
            UpdateInspectAndCombineButtons(null);
        }
    }

    // ================= XỬ LÝ KHUNG CHI TIẾT (2B), SOI & GHÉP VẬT PHẨM =================

    /// <summary>Xử lý khi người chơi bấm/chạm vào một ô vật phẩm trong lưới (chế độ bình thường hoặc chọn món thứ 2 khi Ghép).</summary>
    private void HandleSlotClicked(InventorySlotUI clickedSlot)
    {
        if (clickedSlot == null || !clickedSlot.HasItem)
        {
            return;
        }

        InventoryManager manager = InventoryManager.Instance;
        string clickedItemId = clickedSlot.CurrentItemId;
        if (manager == null || !manager.HasItem(clickedItemId))
        {
            RefreshInventoryUI();
            return;
        }

        // Chế độ Ghép 2 bước: người chơi vừa chọn vật phẩm thứ 2 -> thực hiện ghép nguyên tử qua InventoryManager
        if (_isCombineMode)
        {
            ItemData firstItem = _combineFirstItem;
            ItemData clickedItem = manager.GetItemData(clickedItemId);

            CombineResult result = manager.TryCombineItems(firstItem, clickedItem, out ItemCombineRecipe matchedRecipe);
            ExitCombineMode(false);

            switch (result)
            {
                case CombineResult.Success:
                    RefreshInventoryUI();

                    // Tự động chọn vật phẩm kết quả mới tạo thành nếu đang tồn tại trong túi
                    if (matchedRecipe != null && matchedRecipe.ResultItem != null && manager.HasItem(matchedRecipe.ResultItem.itemId))
                    {
                        _inspectedItemId = matchedRecipe.ResultItem.itemId;
                        ShowItemDetail(_inspectedItemId);
                        UpdateSlotSelectionHighlights();
                    }

                    string successMsg = matchedRecipe != null && !string.IsNullOrEmpty(matchedRecipe.SuccessMessage)
                        ? matchedRecipe.SuccessMessage
                        : "Ghép vật phẩm thành công!";
                    ShowCombineFeedback(successMsg);
                    break;

                case CombineResult.MissingRequiredItems:
                    ShowCombineFeedback("Không đủ số lượng vật phẩm để ghép!");
                    break;

                case CombineResult.NoMatchingRecipe:
                case CombineResult.InvalidInput:
                default:
                    ShowCombineFeedback("Không thể ghép hai vật phẩm này!");
                    break;
            }

            return;
        }

        // Chế độ bình thường: xem chi tiết vật phẩm và cập nhật nút SOI / GHÉP
        _inspectedItemId = clickedItemId;
        ShowItemDetail(_inspectedItemId);
        UpdateSlotSelectionHighlights();
    }

    private void ShowItemDetail(string itemId)
    {
        InventoryManager manager = InventoryManager.Instance;
        if (manager == null || string.IsNullOrEmpty(itemId))
        {
            ClearInspection();
            return;
        }

        ItemData itemData = manager.GetItemData(itemId);
        if (itemData == null || !manager.HasItem(itemId))
        {
            ClearInspection();
            return;
        }

        SetGameObjectActiveSafe(detailPanel, true);

        if (detailIconImage != null)
        {
            bool hasIcon = itemData.icon != null;
            detailIconImage.sprite = itemData.icon;
            if (hasIcon && !detailIconImage.gameObject.activeSelf)
            {
                detailIconImage.gameObject.SetActive(true);
            }

            detailIconImage.enabled = hasIcon;
        }

        if (detailNameText != null)
        {
            detailNameText.text = itemData.displayName ?? string.Empty;
        }

        if (detailDescriptionText != null)
        {
            detailDescriptionText.text = itemData.description ?? string.Empty;
        }

        // Sẵn sàng cho Webshop: chỉ bật nhãn nguồn khi source == "Webshop_Giftcode"
        if (detailSourceText != null)
        {
            bool isGiftcode = _slotDataCache.TryGetValue(itemId, out InventorySlotData slotData)
                              && slotData != null
                              && string.Equals(slotData.source, WebshopGiftcodeSource, StringComparison.Ordinal);

            SetGameObjectActiveSafe(detailSourceText.gameObject, isGiftcode);
            detailSourceText.enabled = isGiftcode;

            if (isGiftcode)
            {
                detailSourceText.text = string.IsNullOrEmpty(giftcodeSourceLabel)
                    ? WebshopGiftcodeSource
                    : giftcodeSourceLabel;
            }
        }

        UpdateUseOrEquipButtonVisual(itemData);
        UpdateInspectAndCombineButtons(itemData);
    }

    private void UpdateInspectAndCombineButtons(ItemData selectedItem)
    {
        if (inspectButton != null)
        {
            inspectButton.interactable = selectedItem != null && selectedItem.IsInspectable;
        }

        if (combineButton != null)
        {
            combineButton.interactable = selectedItem != null;
        }
    }

    private void HandleInspectButtonClicked()
    {
        if (string.IsNullOrEmpty(_inspectedItemId) || InventoryManager.Instance == null)
        {
            return;
        }

        if (_isCombineMode)
        {
            ExitCombineMode(true);
        }

        ItemData selectedItem = InventoryManager.Instance.GetItemData(_inspectedItemId);
        if (ItemInspectUIController.Instance != null)
        {
            ItemInspectUIController.Instance.Open(selectedItem);
        }
    }

    private void HandleCombineButtonClicked()
    {
        if (string.IsNullOrEmpty(_inspectedItemId) || InventoryManager.Instance == null)
        {
            return;
        }

        ItemData selectedItem = InventoryManager.Instance.GetItemData(_inspectedItemId);
        if (selectedItem == null || !InventoryManager.Instance.HasItem(selectedItem.itemId))
        {
            RefreshInventoryUI();
            return;
        }

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySfx("SFX_UI_Click");
        }

        StopCombineFeedbackCoroutine();
        _isCombineMode = true;
        _combineFirstItem = selectedItem;

        if (cancelCombineButton != null)
        {
            SetGameObjectActiveSafe(cancelCombineButton.gameObject, true);
        }

        if (combineStatusText != null)
        {
            combineStatusText.text = "Chọn vật phẩm thứ 2 để ghép...";
            SetGameObjectActiveSafe(combineStatusText.gameObject, true);
            combineStatusText.enabled = true;
        }
    }

    private void HandleCancelCombineClicked()
    {
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySfx("SFX_UI_Click");
        }

        ExitCombineMode(true);
    }

    private void ExitCombineMode(bool clearStatusText)
    {
        _isCombineMode = false;
        _combineFirstItem = null;

        if (cancelCombineButton != null)
        {
            SetGameObjectActiveSafe(cancelCombineButton.gameObject, false);
        }

        if (clearStatusText)
        {
            StopCombineFeedbackCoroutine();
            HideCombineStatusText();
        }
    }

    private void ShowCombineFeedback(string message)
    {
        if (combineStatusText == null)
        {
            return;
        }

        StopCombineFeedbackCoroutine();
        _feedbackCoroutine = StartCoroutine(CombineFeedbackRoutine(message));
    }

    private IEnumerator CombineFeedbackRoutine(string message)
    {
        if (combineStatusText == null)
        {
            yield break;
        }

        combineStatusText.text = message;
        SetGameObjectActiveSafe(combineStatusText.gameObject, true);
        combineStatusText.enabled = true;

        try
        {
            // Tối ưu GC: đếm thời gian bằng Time.unscaledDeltaTime thay vì cấp phát new WaitForSeconds()
            float duration = Mathf.Max(0f, combineFeedbackDuration);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }
        finally
        {
            HideCombineStatusText();
            _feedbackCoroutine = null;
        }
    }

    private void StopCombineFeedbackCoroutine()
    {
        if (_feedbackCoroutine != null)
        {
            StopCoroutine(_feedbackCoroutine);
            _feedbackCoroutine = null;
        }
    }

    private void HideCombineStatusText()
    {
        if (combineStatusText == null)
        {
            return;
        }

        combineStatusText.enabled = false;
        SetGameObjectActiveSafe(combineStatusText.gameObject, false);
    }

    private void UpdateUseOrEquipButtonVisual(ItemData itemData)
    {
        if (useOrEquipButton != null)
        {
            useOrEquipButton.interactable = itemData != null;
        }

        if (useOrEquipButtonText == null || itemData == null)
        {
            return;
        }

        if (string.Equals(_inspectedItemId, EquippedItemId, StringComparison.Ordinal))
        {
            useOrEquipButtonText.text = unequipButtonLabel;
        }
        else if (itemData.category == ItemCategory.Consumable)
        {
            useOrEquipButtonText.text = useButtonLabel;
        }
        else
        {
            useOrEquipButtonText.text = equipButtonLabel;
        }
    }

    /// <summary>Xử lý khi bấm nút Sử dụng / Cầm lên / Bỏ cầm trong khung chi tiết (2B).</summary>
    private void HandleUseOrEquipClicked()
    {
        if (string.IsNullOrEmpty(_inspectedItemId))
        {
            return;
        }

        InventoryManager manager = InventoryManager.Instance;
        if (manager == null || !manager.HasItem(_inspectedItemId))
        {
            RefreshInventoryUI();
            return;
        }

        // Nếu đang xem đúng vật phẩm đang cầm -> cho phép bỏ cầm ngay
        if (string.Equals(_inspectedItemId, EquippedItemId, StringComparison.Ordinal))
        {
            ClearEquippedItem();
            return;
        }

        string targetItemId = _inspectedItemId;
        ItemData itemData = manager.GetItemData(targetItemId);
        if (itemData == null)
        {
            ClearInspection();
            return;
        }

        // Gán trạng thái đang cầm & cập nhật HUD góc màn hình (KHÔNG tự ý xóa KeyItem để chống Softlock)
        EquipItemInternal(targetItemId, itemData);

        // Phát sự kiện yêu cầu sử dụng cho vật phẩm tiêu hao/đặc biệt để hệ thống khác lắng nghe nếu cần
        if (itemData.category != ItemCategory.KeyItem)
        {
            OnItemUseRequested?.Invoke(targetItemId);
        }

        // Tự động đóng Popup để người chơi bấm trực tiếp lên vật thể câu đố trong Scene
        ClosePopup();
    }

    /// <summary>Chủ động cầm một vật phẩm hợp lệ đang có trong kho lên tay.</summary>
    public bool EquipItem(string itemId)
    {
        if (string.IsNullOrEmpty(itemId) || InventoryManager.Instance == null || !InventoryManager.Instance.HasItem(itemId))
        {
            return false;
        }

        ItemData itemData = InventoryManager.Instance.GetItemData(itemId);
        if (itemData == null)
        {
            return false;
        }

        EquipItemInternal(itemId, itemData);
        return true;
    }

    private void EquipItemInternal(string itemId, ItemData itemData)
    {
        bool changed = !string.Equals(EquippedItemId, itemId, StringComparison.Ordinal);
        EquippedItemId = itemId;
        UpdateEquippedHudVisual(itemData);

        if (changed)
        {
            OnEquippedItemChanged?.Invoke(EquippedItemId);
        }
    }

    /// <summary>Bỏ cầm vật phẩm hiện tại, ẩn HUD góc màn hình và phát sự kiện OnEquippedItemChanged.</summary>
    public void ClearEquippedItem()
    {
        bool hadEquippedItem = !string.IsNullOrEmpty(EquippedItemId);
        EquippedItemId = string.Empty;

        SetGameObjectActiveSafe(equippedHudRoot, false);
        if (equippedHudIcon != null)
        {
            equippedHudIcon.sprite = null;
            equippedHudIcon.enabled = false;
        }

        // Nếu đang mở khung chi tiết của vật phẩm vừa bỏ cầm, đổi lại chữ nút từ "Bỏ cầm" -> "Cầm lên"/"Sử dụng"
        if (IsPopupOpen && !string.IsNullOrEmpty(_inspectedItemId) && InventoryManager.Instance != null)
        {
            UpdateUseOrEquipButtonVisual(InventoryManager.Instance.GetItemData(_inspectedItemId));
        }

        if (hadEquippedItem)
        {
            OnEquippedItemChanged?.Invoke(string.Empty);
        }
    }

    /// <summary>Kiểm tra nhanh người chơi có đang cầm đúng mã vật phẩm yêu cầu hay không.</summary>
    public bool IsItemEquipped(string itemId)
    {
        return !string.IsNullOrEmpty(itemId) && string.Equals(EquippedItemId, itemId, StringComparison.Ordinal);
    }

    private void UpdateEquippedHudVisual(ItemData itemData)
    {
        if (itemData == null)
        {
            SetGameObjectActiveSafe(equippedHudRoot, false);
            return;
        }

        SetGameObjectActiveSafe(equippedHudRoot, true);

        if (equippedHudIcon != null)
        {
            bool hasIcon = itemData.icon != null;
            equippedHudIcon.sprite = itemData.icon;
            if (hasIcon && !equippedHudIcon.gameObject.activeSelf)
            {
                equippedHudIcon.gameObject.SetActive(true);
            }

            equippedHudIcon.enabled = hasIcon;
        }
    }

    private void ClearInspection()
    {
        _inspectedItemId = string.Empty;
        SetGameObjectActiveSafe(detailPanel, false);
        UpdateSlotSelectionHighlights();
    }

    private void UpdateSlotSelectionHighlights()
    {
        // Cập nhật viền sáng trực tiếp trên Pool mà không cần vẽ lại Icon/Text của ô
        for (int i = 0; i < _slotPool.Count; i++)
        {
            InventorySlotUI slot = _slotPool[i];
            if (slot == null)
            {
                continue;
            }

            bool isSelected = !string.IsNullOrEmpty(_inspectedItemId)
                              && string.Equals(slot.CurrentItemId, _inspectedItemId, StringComparison.Ordinal);
            slot.SetSelected(isSelected);
        }
    }

    // ================= PHÍM TẮT PC (TƯƠNG THÍCH INPUT SYSTEM) =================

    private static bool WasToggleBagKeyPressed()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.iKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.I);
#else
        return false;
#endif
    }

    private static bool WasCloseBagKeyPressed()
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
