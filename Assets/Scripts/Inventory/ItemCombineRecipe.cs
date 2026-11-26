using System;
using UnityEngine;

/// <summary>
/// ScriptableObject định nghĩa một công thức ghép 2 vật phẩm trong Kho đồ.
/// Hỗ trợ kiểm tra khớp 2 chiều (A + B hoặc B + A) và tùy chọn giữ lại công cụ dùng nhiều lần để chống Softlock.
/// </summary>
[CreateAssetMenu(fileName = "NewCombineRecipe", menuName = "Vong Nguyet/Inventory/Combine Recipe")]
public class ItemCombineRecipe : ScriptableObject
{
    [Header("Định danh công thức")]
    [Tooltip("Mã định danh duy nhất của công thức để dễ quản lý và mở rộng theo từng Chapter (VD: ch1_combine_denpin).")]
    [SerializeField] private string recipeId;

    [Header("Vật phẩm đầu vào")]
    [Tooltip("Vật phẩm nguyên liệu thứ nhất.")]
    [SerializeField] private ItemData itemA;

    [Tooltip("Vật phẩm nguyên liệu thứ hai.")]
    [SerializeField] private ItemData itemB;

    [Header("Vật phẩm tạo thành")]
    [Tooltip("Vật phẩm kết quả nhận được sau khi ghép thành công.")]
    [SerializeField] private ItemData resultItem;

    [Tooltip("Số lượng vật phẩm kết quả nhận được (phải lớn hơn 0).")]
    [SerializeField] private int resultQuantity = 1;

    [Header("Quy tắc tiêu hao (Chống Softlock)")]
    [Tooltip("Tắt (false) nếu Item A là công cụ tái sử dụng (tua vít, bật lửa...) để giữ lại trong túi, chống Softlock.")]
    [SerializeField] private bool consumeItemA = true;

    [Tooltip("Tắt (false) nếu Item B là công cụ tái sử dụng để không bị mất sau khi ghép.")]
    [SerializeField] private bool consumeItemB = true;

    [Header("Thông báo phản hồi UI")]
    [Tooltip("Dòng thông báo hiển thị cho người chơi khi ghép thành công công thức này.")]
    [SerializeField] private string successMessage = "Ghép vật phẩm thành công!";

    public string RecipeId => recipeId;
    public ItemData ItemA => itemA;
    public ItemData ItemB => itemB;
    public ItemData ResultItem => resultItem;
    public int ResultQuantity => resultQuantity;
    public bool ConsumeItemA => consumeItemA;
    public bool ConsumeItemB => consumeItemB;
    public string SuccessMessage => successMessage;

    /// <summary>Kiểm tra công thức đã được cấu hình đầy đủ nguyên liệu, kết quả và số lượng hợp lệ hay chưa.</summary>
    public bool IsValid()
    {
        return itemA != null
               && itemB != null
               && resultItem != null
               && resultQuantity > 0
               && !string.IsNullOrEmpty(itemA.itemId)
               && !string.IsNullOrEmpty(itemB.itemId)
               && !string.IsNullOrEmpty(resultItem.itemId);
    }

    /// <summary>
    /// Kiểm tra 2 vật phẩm đầu vào có khớp công thức theo cả 2 chiều (A+B hoặc B+A) hay không.
    /// Trả về isSwapped = true nếu first khớp itemB và second khớp itemA để InventoryManager trừ đúng cờ consumeItemA/B.
    /// </summary>
    public bool Matches(ItemData first, ItemData second, out bool isSwapped)
    {
        isSwapped = false;
        if (!IsValid() || first == null || second == null)
        {
            return false;
        }

        if (AreSameItem(first, itemA) && AreSameItem(second, itemB))
        {
            isSwapped = false;
            return true;
        }

        if (AreSameItem(first, itemB) && AreSameItem(second, itemA))
        {
            isSwapped = true;
            return true;
        }

        return false;
    }

    /// <summary>Kiểm tra nhanh vật phẩm có tham gia công thức hợp lệ này hay không.</summary>
    public bool InvolvesItem(ItemData item)
    {
        return IsValid() && item != null && (AreSameItem(item, itemA) || AreSameItem(item, itemB));
    }

    private static bool AreSameItem(ItemData left, ItemData right)
    {
        if (left == right)
        {
            return true;
        }

        if (left == null || right == null)
        {
            return false;
        }

        // So khớp nhất quán theo Ordinal itemId đúng như khóa Dictionary của InventoryManager
        return !string.IsNullOrEmpty(left.itemId)
               && string.Equals(left.itemId, right.itemId, StringComparison.Ordinal);
    }
}
