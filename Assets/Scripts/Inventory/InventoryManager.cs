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

// Kết quả ghép vật phẩm dạng enum để UI hiển thị phản hồi mà không tạo chuỗi rác GC không cần thiết
public enum CombineResult
{
    Success,
    InvalidInput,
    MissingRequiredItems,
    NoMatchingRecipe
}

public class InventoryManager : MonoBehaviour
{
    public static InventoryManager Instance { get; private set; }

    private const string DefaultItemSource = "Gameplay";

    [Header("Danh mục toàn bộ vật phẩm trong game (Kéo thả ItemData vào đây)")]
    [Tooltip("Danh sách toàn bộ ScriptableObject ItemData hợp lệ dùng để tra cứu và xác thực khi nhặt đồ hoặc tải file Save.")]
    [SerializeField] private List<ItemData> itemDatabase = new List<ItemData>();

    [Header("Công thức ghép đồ (Combine)")]
    [Tooltip("Danh sách toàn bộ ScriptableObject ItemCombineRecipe hợp lệ trong game.")]
    [SerializeField] private List<ItemCombineRecipe> combineRecipes = new List<ItemCombineRecipe>();

    [Tooltip("Nếu bật, tự động lưu file Save ngay sau khi ghép vật phẩm thành công để chống mất đồng bộ.")]
    [SerializeField] private bool autoSaveOnCombine = true;

    // Bảng tra cứu O(1) thay vì dùng Resources.Load hoặc List.Find trong lúc chơi
    private readonly Dictionary<string, ItemData> _itemLookup = new Dictionary<string, ItemData>();

    // Kho đồ hiện tại của người chơi: Key = itemId
    private readonly Dictionary<string, InventorySlotData> _inventory = new Dictionary<string, InventorySlotData>();

    // Sự kiện báo cho InventoryUI vẽ lại đúng thời điểm có thay đổi (không chạy trong Update)
    public event Action OnInventoryChanged;

    private void Awake()
    {
        // Chặn lỗi nhân bản Singleton khi người chơi chuyển hoặc quay lại Scene cũ
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // Edge Case: DontDestroyOnLoad chỉ có hiệu lực trên GameObject ở cấp Root của Hierarchy
        if (transform.parent != null)
            transform.SetParent(null);

        DontDestroyOnLoad(gameObject);
        BuildItemLookup();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void BuildItemLookup()
    {
        _itemLookup.Clear();
        for (int i = 0; i < itemDatabase.Count; i++)
        {
            RegisterItemInLookup(itemDatabase[i]);
        }

        // Chống Softlock: tự đăng ký thêm các ItemData tham gia công thức ghép đồ nếu quên kéo vào itemDatabase
        if (combineRecipes != null)
        {
            for (int i = 0; i < combineRecipes.Count; i++)
            {
                ItemCombineRecipe recipe = combineRecipes[i];
                if (recipe == null) continue;

                RegisterItemInLookup(recipe.ItemA);
                RegisterItemInLookup(recipe.ItemB);
                RegisterItemInLookup(recipe.ResultItem);
            }
        }
    }

    private void RegisterItemInLookup(ItemData data)
    {
        if (data != null && !string.IsNullOrEmpty(data.itemId) && !_itemLookup.ContainsKey(data.itemId))
        {
            _itemLookup[data.itemId] = data;
        }
    }

    /// <summary>Thêm vật phẩm bằng ScriptableObject ItemData (tự bảo đảm có trong bảng tra cứu).</summary>
    public bool AddItem(ItemData itemData, int amount = 1, string source = DefaultItemSource)
    {
        if (itemData == null || string.IsNullOrEmpty(itemData.itemId))
        {
            return false;
        }

        RegisterItemInLookup(itemData);
        return AddItem(itemData.itemId, amount, source);
    }

    /// <summary>Thêm vật phẩm vào kho đồ. Hỗ trợ cả nhặt trong game và nhận từ Webshop Giftcode.</summary>
    public bool AddItem(string itemId, int amount = 1, string source = DefaultItemSource)
    {
        if (string.IsNullOrEmpty(itemId) || amount <= 0 || !_itemLookup.TryGetValue(itemId, out ItemData itemData))
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
                source = NormalizeSource(source)
            };
        }

        Debug.Log($"[InventoryManager] Đã thêm vật phẩm: {itemData.displayName} (x{amount})");
        OnInventoryChanged?.Invoke();
        return true;
    }

    /// <summary>Trừ vật phẩm khi giải đố thành công. Trả về false nếu không đủ số lượng.</summary>
    public bool RemoveItem(string itemId, int amount = 1)
    {
        if (string.IsNullOrEmpty(itemId) || amount <= 0 || !_inventory.TryGetValue(itemId, out InventorySlotData slot) || slot.quantity < amount)
            return false;

        slot.quantity -= amount;
        if (slot.quantity <= 0)
            _inventory.Remove(itemId);

        OnInventoryChanged?.Invoke();
        return true;
    }

    public bool HasItem(string itemId, int requiredAmount = 1)
    {
        return !string.IsNullOrEmpty(itemId) && _inventory.TryGetValue(itemId, out InventorySlotData slot) && slot.quantity >= requiredAmount;
    }

    public ItemData GetItemData(string itemId)
    {
        return !string.IsNullOrEmpty(itemId) && _itemLookup.TryGetValue(itemId, out ItemData data) ? data : null;
    }

    // Trả về IReadOnlyCollection để UI đọc trực tiếp mà không cấp phát mảng/List mới gây rác GC
    public IReadOnlyCollection<InventorySlotData> GetAllSlots() => _inventory.Values;

    // ================= GHÉP VẬT PHẨM (COMBINE SYSTEM) =================

    /// <summary>
    /// Thử ghép 2 vật phẩm theo danh sách công thức combineRecipes (duyệt for không LINQ, giao dịch nguyên tử).
    /// Hỗ trợ chống Self-Combine Exploit (yêu cầu số lượng >= 2 nếu ghép 2 vật phẩm cùng ID)
    /// và tôn trọng cờ consumeItemA/B để giữ lại công cụ dùng nhiều lần chống Softlock.
    /// </summary>
    public CombineResult TryCombineItems(ItemData firstItem, ItemData secondItem, out ItemCombineRecipe matchedRecipe)
    {
        matchedRecipe = null;

        if (firstItem == null || secondItem == null
            || string.IsNullOrEmpty(firstItem.itemId) || string.IsNullOrEmpty(secondItem.itemId))
        {
            return CombineResult.InvalidInput;
        }

        if (combineRecipes == null || combineRecipes.Count == 0)
        {
            return CombineResult.NoMatchingRecipe;
        }

        // 1. Duyệt for (không dùng LINQ để tránh sinh rác GC) tìm công thức hợp lệ khớp 2 chiều
        bool isSwapped = false;
        for (int i = 0; i < combineRecipes.Count; i++)
        {
            ItemCombineRecipe recipe = combineRecipes[i];
            if (recipe != null && recipe.IsValid() && recipe.Matches(firstItem, secondItem, out isSwapped))
            {
                matchedRecipe = recipe;
                break;
            }
        }

        if (matchedRecipe == null)
        {
            return CombineResult.NoMatchingRecipe;
        }

        // 2. Kiểm tra đủ số lượng trước khi trừ (Atomic Check & Chống Self-Combine Exploit)
        bool isSameItem = firstItem == secondItem
                          || string.Equals(firstItem.itemId, secondItem.itemId, StringComparison.Ordinal);

        if (isSameItem)
        {
            // Chống Self-Combine Exploit: ghép cùng 1 loại vật phẩm bắt buộc phải có từ 2 món trở lên trong túi
            if (!HasItem(firstItem.itemId, 2))
            {
                return CombineResult.MissingRequiredItems;
            }
        }
        else
        {
            if (!HasItem(firstItem.itemId, 1) || !HasItem(secondItem.itemId, 1))
            {
                return CombineResult.MissingRequiredItems;
            }
        }

        // 3. Xác định đúng cờ tiêu hao theo chiều khớp (isSwapped) để giữ lại công cụ tái sử dụng (chống Softlock)
        bool consumeFirst = isSwapped ? matchedRecipe.ConsumeItemB : matchedRecipe.ConsumeItemA;
        bool consumeSecond = isSwapped ? matchedRecipe.ConsumeItemA : matchedRecipe.ConsumeItemB;

        // Chặn trường hợp vật phẩm kết quả không cộng dồn đã có sẵn trong túi mà không bị thay thế -> tránh xóa oan nguyên liệu
        ItemData resultItem = matchedRecipe.ResultItem;
        if (!resultItem.isStackable && HasItem(resultItem.itemId))
        {
            bool resultWillBeRemovedFirst = (consumeFirst && string.Equals(firstItem.itemId, resultItem.itemId, StringComparison.Ordinal))
                                            || (consumeSecond && string.Equals(secondItem.itemId, resultItem.itemId, StringComparison.Ordinal));
            if (!resultWillBeRemovedFirst)
            {
                Debug.LogWarning($"[InventoryManager] Người chơi đã sở hữu vật phẩm không cộng dồn '{resultItem.itemId}', hủy ghép để tránh mất nguyên liệu.");
                return CombineResult.InvalidInput;
            }
        }

        // 4. Thực thi giao dịch nguyên tử: trừ nguyên liệu tiêu hao -> cộng vật phẩm kết quả -> tự động lưu Save
        if (isSameItem)
        {
            int consumeAmount = (consumeFirst ? 1 : 0) + (consumeSecond ? 1 : 0);
            if (consumeAmount > 0)
            {
                RemoveItem(firstItem.itemId, consumeAmount);
            }
        }
        else
        {
            if (consumeFirst)
            {
                RemoveItem(firstItem.itemId, 1);
            }

            if (consumeSecond)
            {
                RemoveItem(secondItem.itemId, 1);
            }
        }

        AddItem(resultItem, matchedRecipe.ResultQuantity);

        if (autoSaveOnCombine && GameStateManager.Instance != null)
        {
            GameStateManager.Instance.SaveGameProgress();
        }

        return CombineResult.Success;
    }

    /// <summary>Kiểm tra nhanh vật phẩm có tham gia bất kỳ công thức ghép đồ hợp lệ nào hay không (duyệt for không GC).</summary>
    public bool CanCombineAny(ItemData item)
    {
        if (item == null || combineRecipes == null)
        {
            return false;
        }

        for (int i = 0; i < combineRecipes.Count; i++)
        {
            ItemCombineRecipe recipe = combineRecipes[i];
            if (recipe != null && recipe.InvolvesItem(item))
            {
                return true;
            }
        }

        return false;
    }

    // ================= ĐỒNG BỘ DỮ LIỆU SAVE / LOAD =================

    /// <summary>Xuất danh sách bản sao độc lập (Deep Copy) của kho đồ để ghi vào GameSaveData / JSON.</summary>
    public List<InventorySlotData> ExportSaveData()
    {
        List<InventorySlotData> exportedSlots = new List<InventorySlotData>(_inventory.Count);

        foreach (InventorySlotData slot in _inventory.Values)
        {
            // Bỏ qua các slot lỗi hoặc số lượng không hợp lệ
            if (slot == null || string.IsNullOrEmpty(slot.itemId) || slot.quantity <= 0)
                continue;

            // Tạo bản sao mới để tránh rò rỉ tham chiếu khi GameSaveData xử lý JSON
            exportedSlots.Add(new InventorySlotData
            {
                itemId = slot.itemId,
                quantity = slot.quantity,
                source = NormalizeSource(slot.source)
            });
        }

        return exportedSlots;
    }

    /// <summary>Nạp dữ liệu kho đồ từ file Save và đồng bộ lại UI trong một lần phát sự kiện duy nhất.</summary>
    public void ImportSaveData(List<InventorySlotData> savedSlots)
    {
        // Tái sử dụng Dictionary nội bộ bằng Clear() thay vì cấp phát mới để hạn chế GC
        _inventory.Clear();

        if (savedSlots == null || savedSlots.Count == 0)
        {
            OnInventoryChanged?.Invoke();
            return;
        }

        for (int i = 0; i < savedSlots.Count; i++)
        {
            InventorySlotData slot = savedSlots[i];

            // Edge Case JsonUtility: phần tử thiếu field trong JSON có thể khởi tạo thành object rỗng
            if (slot == null || string.IsNullOrEmpty(slot.itemId) || slot.quantity <= 0)
                continue;

            // Kiểm tra vật phẩm có tồn tại trong Database; không ném ngoại lệ nếu save chứa ID cũ/không hợp lệ
            if (!_itemLookup.TryGetValue(slot.itemId, out ItemData itemData))
            {
                Debug.LogWarning($"[InventoryManager] Bỏ qua itemId '{slot.itemId}' trong file Save vì chưa được đăng ký trong Item Database.");
                continue;
            }

            string normalizedSource = NormalizeSource(slot.source);

            if (_inventory.TryGetValue(slot.itemId, out InventorySlotData existingSlot))
            {
                // Edge Case: file JSON có nhiều bản ghi trùng itemId -> chỉ cộng dồn nếu vật phẩm cho phép stack
                if (itemData.isStackable)
                    existingSlot.quantity += slot.quantity;
            }
            else
            {
                // Tạo bản sao độc lập; vật phẩm không cộng dồn luôn ép số lượng về 1
                _inventory[slot.itemId] = new InventorySlotData
                {
                    itemId = slot.itemId,
                    quantity = itemData.isStackable ? slot.quantity : 1,
                    source = normalizedSource
                };
            }
        }

        // Chỉ phát sự kiện 1 lần duy nhất sau vòng lặp để UI vẽ lại đúng 1 lần
        OnInventoryChanged?.Invoke();
    }

    /// <summary>Xóa sạch kho đồ hiện tại (dùng khi người chơi chọn New Game / Chơi mới).</summary>
    public void ClearInventory()
    {
        _inventory.Clear();
        OnInventoryChanged?.Invoke();
    }

    private static string NormalizeSource(string source)
    {
        return string.IsNullOrWhiteSpace(source) ? DefaultItemSource : source.Trim();
    }
}