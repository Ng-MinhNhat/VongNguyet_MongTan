using UnityEngine;

// Phân loại vật phẩm để dễ mở rộng cho các Chapter sau và Webshop Giftcode
public enum ItemCategory
{
    KeyItem,    // Vật phẩm giải đố cốt truyện (Chìa khóa, Cầu chì...)
    Consumable, // Vật phẩm tiêu hao (Thuốc, Gợi ý...)
    Giftcode    // Vật phẩm nhận từ Webshop / Giftcode
}

// Tạo menu chuột phải trong Unity: Create -> VongNguyet -> Item Data
[CreateAssetMenu(fileName = "NewItem", menuName = "VongNguyet/Item Data")]
public class ItemData : ScriptableObject
{
    [Header("Định danh (Khớp với JSON & MySQL)")]
    public string itemId;          // Khóa chính dạng chuỗi, không dấu (VD: "ch1_key_ketsat")
    public string displayName;     // Tên hiển thị trên UI (VD: "Chìa khóa Két Sắt")
    
    [Header("Hiển thị uGUI")]
    public Sprite icon;            // Ảnh icon 2D hiển thị trong ô Kho đồ
    [TextArea(2, 4)]
    public string description;     // Mô tả gợi ý khi người chơi bấm xem vật phẩm

    [Header("Quy tắc Kho đồ")]
    public ItemCategory category = ItemCategory.KeyItem;
    public bool isStackable = false; // False cho chìa khóa, True cho vật phẩm cộng dồn
}