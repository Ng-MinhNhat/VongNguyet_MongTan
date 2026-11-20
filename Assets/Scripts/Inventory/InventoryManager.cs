using System;
using System.Collections.Generic;
using UnityEngine;

// Cấu trúc dữ liệu từng ô vật phẩm — khớp trực tiếp với JSON Save và bảng MySQL/EF Core về sau
[Serializable]
public class InventorySlotData
{
    public string itemId;
    public int quantity;
    public string source; // "Gameplay" hoặc "Webshop_Giftcode" để phân biệt nguồn vật phẩm
}

public class InventoryManager : MonoBehaviour
{
    public static InventoryManager Instance { get; private set; }

    [Header("Danh mục toàn bộ vật phẩm trong game (Kéo thả ItemData vào đây)")]
    [SerializeField] private List<ItemData> itemDatabase = new List<ItemData>();

    // Bảng tra cứu O(1) thay vì dùng Resources.Load hoặc List.Find trong lúc chơi
    private readonly Dictionary<string, ItemData> _itemLookup = new Dictionary<string, ItemData>();

    // Kho đồ hiện tại của người chơi: Key = itemId
    private readonly Dictionary<string, InventorySlotData> _inventory = new Dictionary<string, InventorySlotData>();

    // Sự kiện báo cho InventoryUI vẽ lại đúng thời điểm có thay đổi (không chạy trong Update)
    public event Action OnInventoryChanged;

    private void Awake()
    {
        // Chặn lỗi nhân bản Singleton khi người chơi quay lại Scene cũ
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        BuildItemLookup();
    }

    private void BuildItemLookup()
    {
        _itemLookup.Clear();
        for (int i = 0; i < itemDatabase.Count; i++)
        {
            ItemData data = itemDatabase[i];
            if (data != null && !string.IsNullOrEmpty(data.itemId))
            {
                _itemLookup[data.itemId] = data;
            }
        }
    }

    /// <summary>Thêm vật phẩm vào kho đồ. Hỗ trợ cả nhặt trong game và nhận từ Webshop Giftcode.</summary>
    public bool AddItem(string itemId, int amount = 1, string source = "Gameplay")
    {
        if (amount <= 0 || !_itemLookup.TryGetValue(itemId, out ItemData itemData))
        {
            Debug.LogWarning($"[InventoryManager] Không tìm thấy itemId '{itemId}' trong Item Database.");
            return false;
        }

        if (_inventory.TryGetValue(itemId, out InventorySlotData existingSlot))
        {
            // Chặn lỗi nhận trùng vật phẩm cốt truyện không cộng dồn (như Chìa khóa)
            if (!itemData.isStackable) return false;
            existingSlot.quantity += amount;
        }
        else
        {
            _inventory[itemId] = new InventorySlotData
            {
                itemId = itemId,
                quantity = itemData.isStackable ? amount : 1,
                source = source
            };
        }

        Debug.Log($"[InventoryManager] Đã thêm vật phẩm: {itemData.displayName} (x{amount})");
        OnInventoryChanged?.Invoke();
        return true;
    }

    /// <summary>Trừ vật phẩm khi giải đố thành công. Trả về false nếu không đủ số lượng.</summary>
    public bool RemoveItem(string itemId, int amount = 1)
    {
        if (amount <= 0 || !_inventory.TryGetValue(itemId, out InventorySlotData slot) || slot.quantity < amount)
            return false;

        slot.quantity -= amount;
        if (slot.quantity <= 0)
            _inventory.Remove(itemId);

        OnInventoryChanged?.Invoke();
        return true;
    }

    public bool HasItem(string itemId, int requiredAmount = 1)
    {
        return _inventory.TryGetValue(itemId, out InventorySlotData slot) && slot.quantity >= requiredAmount;
    }

    public ItemData GetItemData(string itemId)
    {
        return _itemLookup.TryGetValue(itemId, out ItemData data) ? data : null;
    }

    // Trả về IReadOnlyCollection để UI đọc trực tiếp mà không cấp phát mảng/List mới gây rác GC
    public IReadOnlyCollection<InventorySlotData> GetAllSlots() => _inventory.Values;
}