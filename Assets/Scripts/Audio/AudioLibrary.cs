using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bản ghi một bản nhạc nền (BGM) trong thư viện âm thanh.
/// Cho phép để trống AudioClip trong giai đoạn Greybox mà không gây lỗi runtime.
/// </summary>
[Serializable]
public class BgmEntry
{
    [Tooltip("Mã định danh nhạc nền dùng để gọi qua AudioManager.PlayBgm (VD: bgm_mainmenu, bgm_phongngu).")]
    public string id;

    [Tooltip("Tệp âm thanh nhạc nền. Có thể để trống ở giai đoạn Greybox.")]
    public AudioClip clip;

    [Tooltip("Âm lượng gốc của bản nhạc này trước khi nhân với Master và BGM Volume.")]
    [Range(0f, 1f)]
    public float defaultVolume = 1f;

    [Tooltip("Lặp lại vô hạn khi phát hết bản nhạc.")]
    public bool loop = true;
}

/// <summary>
/// Bản ghi một hiệu ứng âm thanh (SFX) trong thư viện âm thanh.
/// </summary>
[Serializable]
public class SfxEntry
{
    [Tooltip("Mã định danh hiệu ứng dùng để gọi qua AudioManager.PlaySfx (VD: sfx_ui_click, sfx_pickup, sfx_keypad_beep).")]
    public string id;

    [Tooltip("Tệp âm thanh hiệu ứng. Có thể để trống ở giai đoạn Greybox.")]
    public AudioClip clip;

    [Tooltip("Âm lượng gốc của hiệu ứng này trước khi nhân với Master và SFX Volume.")]
    [Range(0f, 1f)]
    public float defaultVolume = 1f;

    [Tooltip("Biên độ ngẫu nhiên cao độ (Pitch) để âm thanh bấm phím/bước chân nghe tự nhiên hơn (0 = giữ nguyên 1.0).")]
    [Range(0f, 0.3f)]
    public float pitchVariance = 0f;
}

/// <summary>
/// ScriptableObject tập trung toàn bộ dữ liệu nhạc nền (BGM) và hiệu ứng (SFX) của dự án.
/// Xây dựng bảng tra cứu Dictionary O(1) không dùng LINQ để tối ưu GC khi phát âm thanh.
/// </summary>
[CreateAssetMenu(fileName = "AudioLibrary", menuName = "Vong Nguyet/Audio/Audio Library")]
public class AudioLibrary : ScriptableObject
{
    [Header("Danh sách Nhạc nền (BGM)")]
    [Tooltip("Toàn bộ các bản nhạc nền theo màn hình và từng phòng/Chapter.")]
    [SerializeField] private List<BgmEntry> bgmEntries = new List<BgmEntry>();

    [Header("Danh sách Hiệu ứng âm thanh (SFX)")]
    [Tooltip("Toàn bộ hiệu ứng tương tác UI, cửa, ổ khóa, nhặt vật phẩm và ghép đồ.")]
    [SerializeField] private List<SfxEntry> sfxEntries = new List<SfxEntry>();

    // Cache tra cứu O(1) không phân biệt hoa thường — khởi tạo 1 lần bằng BuildCache(), không dùng LINQ
    private readonly Dictionary<string, BgmEntry> _bgmCache = new Dictionary<string, BgmEntry>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SfxEntry> _sfxCache = new Dictionary<string, SfxEntry>(StringComparer.OrdinalIgnoreCase);
    private bool _isCacheBuilt;

    private void OnEnable()
    {
        _isCacheBuilt = false;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Đánh dấu cần nạp lại cache nếu người dùng vừa sửa danh sách trong Unity Inspector
        _isCacheBuilt = false;
    }
#endif

    /// <summary>Xây dựng lại bảng tra cứu O(1) cho toàn bộ BGM và SFX bằng vòng lặp for (không sinh rác GC).</summary>
    public void BuildCache()
    {
        _bgmCache.Clear();
        if (bgmEntries != null)
        {
            for (int i = 0; i < bgmEntries.Count; i++)
            {
                BgmEntry entry = bgmEntries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.id))
                {
                    continue;
                }

                string key = entry.id.Trim();
                _bgmCache[key] = entry;
            }
        }

        _sfxCache.Clear();
        if (sfxEntries != null)
        {
            for (int i = 0; i < sfxEntries.Count; i++)
            {
                SfxEntry entry = sfxEntries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.id))
                {
                    continue;
                }

                string key = entry.id.Trim();
                _sfxCache[key] = entry;
            }
        }

        _isCacheBuilt = true;
    }

    /// <summary>Tra cứu an toàn một bản nhạc nền theo ID. Tự gọi BuildCache() nếu chưa khởi tạo.</summary>
    public bool TryGetBgm(string id, out BgmEntry entry)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            entry = null;
            return false;
        }

        if (!_isCacheBuilt)
        {
            BuildCache();
        }

        return _bgmCache.TryGetValue(id.Trim(), out entry);
    }

    /// <summary>Tra cứu an toàn một hiệu ứng âm thanh theo ID. Tự gọi BuildCache() nếu chưa khởi tạo.</summary>
    public bool TryGetSfx(string id, out SfxEntry entry)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            entry = null;
            return false;
        }

        if (!_isCacheBuilt)
        {
            BuildCache();
        }

        return _sfxCache.TryGetValue(id.Trim(), out entry);
    }
}
