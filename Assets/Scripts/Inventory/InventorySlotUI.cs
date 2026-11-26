using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Quản lý giao diện một ô vật phẩm (uGUI + TextMeshPro) được tái sử dụng theo mô hình Object Pooling.
/// Tách biệt hoàn toàn phần hiển thị khỏi InventoryManager để dễ tái sử dụng và mở rộng Webshop Giftcode.
/// </summary>
public class InventorySlotUI : MonoBehaviour
{
    private const string WebshopGiftcodeSource = "Webshop_Giftcode";

    [Header("Thành phần tương tác uGUI")]
    [Tooltip("Nút bấm chính của ô vật phẩm để nhận sự kiện click chuột (PC) và chạm cảm ứng (Mobile).")]
    [SerializeField] private Button slotButton;

    [Header("Thành phần hiển thị vật phẩm")]
    [Tooltip("Ảnh hiển thị biểu tượng (Sprite) của vật phẩm. Nên tắt Raycast Target trên Inspector.")]
    [SerializeField] private Image iconImage;

    [Tooltip("Văn bản TextMeshPro hiển thị số lượng vật phẩm khi lớn hơn 1. Nên tắt Raycast Target trên Inspector.")]
    [SerializeField] private TMP_Text quantityText;

    [Header("Trạng thái & Nhãn mở rộng")]
    [Tooltip("Khung viền sáng bật lên khi ô vật phẩm này đang được người chơi chọn.")]
    [SerializeField] private GameObject selectionHighlight;

    [Tooltip("Nhãn tùy chọn đánh dấu vật phẩm nhận từ Webshop Giftcode (chỉ bật khi source == \"Webshop_Giftcode\").")]
    [SerializeField] private GameObject giftcodeBadge;

    /// <summary>Mã định danh vật phẩm hiện đang gắn trên ô (rỗng nếu ô đang trống).</summary>
    public string CurrentItemId { get; private set; } = string.Empty;

    /// <summary>Kiểm tra nhanh ô hiện có đang chứa vật phẩm hợp lệ hay không.</summary>
    public bool HasItem => !string.IsNullOrEmpty(CurrentItemId);

    // Lưu callback từ InventoryUI thay vì tạo lambda mới mỗi lần Bind() để tránh cấp phát rác GC
    private Action<InventorySlotUI> _onSlotClicked;
    private bool _isButtonListenerRegistered;

    private void Awake()
    {
        // Cache dự phòng nếu người dùng gắn script trực tiếp lên GameObject chứa Button nhưng quên kéo tham chiếu
        if (slotButton == null)
        {
            slotButton = GetComponent<Button>();
        }

        // Tối ưu GC & chống click lặp: chỉ đăng ký listener duy nhất 1 lần trong vòng đời object thay vì gọi trong Bind()
        if (slotButton != null && !_isButtonListenerRegistered)
        {
            slotButton.onClick.AddListener(HandleClick);
            _isButtonListenerRegistered = true;
        }
    }

    private void OnDestroy()
    {
        // Luôn hủy đăng ký sự kiện khi hủy object / chuyển Scene để chống rò rỉ bộ nhớ (Memory Leak)
        if (slotButton != null && _isButtonListenerRegistered)
        {
            slotButton.onClick.RemoveListener(HandleClick);
            _isButtonListenerRegistered = false;
        }

        _onSlotClicked = null;
    }

    /// <summary>
    /// Nạp dữ liệu vật phẩm vào ô giao diện từ Pool. Tự động dọn sạch ô nếu dữ liệu không hợp lệ.
    /// </summary>
    public void Bind(ItemData itemData, InventorySlotData slotData, bool isSelected, Action<InventorySlotUI> onClickCallback)
    {
        // Edge Case: kiểm tra cả string.IsNullOrEmpty(slotData.itemId) vì Unity JsonUtility có thể khởi tạo object rỗng
        if (itemData == null || slotData == null || string.IsNullOrEmpty(slotData.itemId) || slotData.quantity <= 0)
        {
            Clear();
            return;
        }

        CurrentItemId = slotData.itemId;
        _onSlotClicked = onClickCallback;

        // Cập nhật biểu tượng vật phẩm
        if (iconImage != null)
        {
            bool hasIcon = itemData.icon != null;
            iconImage.sprite = itemData.icon;

            // Edge Case: nếu prefab tắt sẵn GameObject Icon thì bật lại khi có Sprite; tắt Image nếu Sprite null để tránh hiện ô vuông trắng
            if (hasIcon && !iconImage.gameObject.activeSelf)
            {
                iconImage.gameObject.SetActive(true);
            }

            iconImage.enabled = hasIcon;
        }

        // Cập nhật số lượng: chỉ hiện khi quantity > 1
        if (quantityText != null)
        {
            bool showQuantity = slotData.quantity > 1;
            if (quantityText.gameObject.activeSelf != showQuantity)
            {
                quantityText.gameObject.SetActive(showQuantity);
            }

            quantityText.enabled = showQuantity;

            if (showQuantity)
            {
                // Tối ưu GC: dùng SetText của TextMeshPro thay vì slotData.quantity.ToString() để không sinh chuỗi rác trên Heap
                quantityText.SetText("{0}", slotData.quantity);
            }
        }

        // Sẵn sàng cho Webshop: bật nhãn Giftcode khi nguồn vật phẩm là "Webshop_Giftcode"
        if (giftcodeBadge != null)
        {
            bool isGiftcode = string.Equals(slotData.source, WebshopGiftcodeSource, StringComparison.Ordinal);
            if (giftcodeBadge.activeSelf != isGiftcode)
            {
                giftcodeBadge.SetActive(isGiftcode);
            }
        }

        SetSelected(isSelected);

        if (slotButton != null)
        {
            slotButton.interactable = true;
        }
    }

    /// <summary>
    /// Đưa ô vật phẩm về trạng thái trống an toàn trước khi trả về Pool hoặc khi dữ liệu đầu vào rỗng.
    /// </summary>
    public void Clear()
    {
        CurrentItemId = string.Empty;
        _onSlotClicked = null;

        if (iconImage != null)
        {
            iconImage.sprite = null;
            iconImage.enabled = false;
        }

        if (quantityText != null)
        {
            quantityText.enabled = false;
            if (quantityText.gameObject.activeSelf)
            {
                quantityText.gameObject.SetActive(false);
            }
        }

        SetSelected(false);

        if (giftcodeBadge != null && giftcodeBadge.activeSelf)
        {
            giftcodeBadge.SetActive(false);
        }

        if (slotButton != null)
        {
            slotButton.interactable = false;
        }
    }

    /// <summary>
    /// Đổi trạng thái khung chọn mà không cần vẽ lại Icon hay Text của ô (giảm tối đa Canvas Rebuild).
    /// </summary>
    public void SetSelected(bool isSelected)
    {
        if (selectionHighlight == null)
        {
            return;
        }

        // Edge Case: chặn bật khung chọn trên ô đang trống (khi chưa Bind hoặc vừa Clear)
        bool shouldHighlight = isSelected && HasItem;
        if (selectionHighlight.activeSelf != shouldHighlight)
        {
            selectionHighlight.SetActive(shouldHighlight);
        }
    }

    private void HandleClick()
    {
        // Edge Case: bỏ qua click nếu ô đang trống hoặc trạng thái chưa được gán dữ liệu
        if (!HasItem)
        {
            return;
        }

        _onSlotClicked?.Invoke(this);
    }
}
