using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Quản lý toàn bộ âm thanh nhạc nền (BGM) và hiệu ứng (SFX) bền vững xuyên Scene (DontDestroyOnLoad).
/// Hỗ trợ 2 kênh AudioSource để Crossfade mượt bằng Time.unscaledDeltaTime (hoạt động cả khi Pause),
/// lưu cấu hình âm lượng qua PlayerPrefs và thoát êm không báo lỗi đỏ khi AudioClip đang null (Greybox).
/// </summary>
public class AudioManager : MonoBehaviour
{
    private const string PrefKeyMaster = "VN_Audio_Master";
    private const string PrefKeyBgm = "VN_Audio_BGM";
    private const string PrefKeySfx = "VN_Audio_SFX";
    private const string PrefKeyMuted = "VN_Audio_Muted";

    public static AudioManager Instance { get; private set; }

    /// <summary>Âm lượng tổng (0..1).</summary>
    public float MasterVolume { get; private set; } = 1f;

    /// <summary>Âm lượng nhạc nền (0..1).</summary>
    public float BgmVolume { get; private set; } = 1f;

    /// <summary>Âm lượng hiệu ứng (0..1).</summary>
    public float SfxVolume { get; private set; } = 1f;

    /// <summary>Trạng thái tắt tiếng toàn bộ âm thanh.</summary>
    public bool IsMuted { get; private set; }

    /// <summary>Mã nhạc nền hiện đang phát (rỗng nếu không phát bản nào).</summary>
    public string CurrentBgmId { get; private set; } = string.Empty;

    /// <summary>Sự kiện phát mỗi khi âm lượng hoặc trạng thái Mute thay đổi để mọi UI Settings tự đồng bộ.</summary>
    public event Action OnSettingsChanged;

    [Header("Thư viện âm thanh")]
    [Tooltip("ScriptableObject AudioLibrary chứa toàn bộ danh sách BGM và SFX của dự án.")]
    [SerializeField] private AudioLibrary audioLibrary;

    [Header("Các kênh phát AudioSource (Tự tạo nếu để trống)")]
    [Tooltip("Kênh nhạc nền A dùng luân phiên với kênh B để chuyển nhạc Crossfade mượt.")]
    [SerializeField] private AudioSource bgmSourceA;

    [Tooltip("Kênh nhạc nền B dùng luân phiên với kênh A để chuyển nhạc Crossfade mượt.")]
    [SerializeField] private AudioSource bgmSourceB;

    [Tooltip("Kênh phát hiệu ứng ngắn bằng PlayOneShot.")]
    [SerializeField] private AudioSource sfxSource;

    [Header("Cấu hình chuyển nhạc (Crossfade)")]
    [Tooltip("Thời gian (giây thực tế unscaled) hòa trộn mặc định khi đổi sang bản nhạc nền mới.")]
    [SerializeField] private float defaultCrossfadeDuration = 0.8f;

    private AudioSource _activeBgmSource;
    private AudioSource _inactiveBgmSource;
    private float _activeBgmBaseVolume = 1f;
    private float _inactiveBgmBaseVolume = 1f;
    private float _activeFadeFactor;
    private float _inactiveFadeFactor;
    private Coroutine _bgmFadeCoroutine;

    private void Awake()
    {
        // Chống trùng lặp DontDestroyOnLoad: hủy bản thừa NGAY LẬP TỨC trước khi tạo AudioSource hay đọc PlayerPrefs
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // Edge Case Unity: DontDestroyOnLoad chỉ có hiệu lực trên GameObject ở cấp Root của Hierarchy
        if (transform.parent != null)
        {
            transform.SetParent(null);
        }

        DontDestroyOnLoad(gameObject);

        EnsureAudioSources();
        _activeBgmSource = bgmSourceA;
        _inactiveBgmSource = bgmSourceB;

        if (audioLibrary != null)
        {
            audioLibrary.BuildCache();
        }

        LoadSettings();
    }

    private void OnDestroy()
    {
        StopBgmFadeCoroutine();

        if (Instance == this)
        {
            Instance = null;
        }

        OnSettingsChanged = null;
    }

    // ================= QUẢN LÝ ÂM LƯỢNG & LƯU TRỮ PLAYERPREFS =================

    public void SetMasterVolume(float value)
    {
        float clamped = Mathf.Clamp01(value);
        if (Mathf.Approximately(MasterVolume, clamped))
        {
            return;
        }

        MasterVolume = clamped;
        ApplyLiveVolumes();
        OnSettingsChanged?.Invoke();
    }

    public void SetBgmVolume(float value)
    {
        float clamped = Mathf.Clamp01(value);
        if (Mathf.Approximately(BgmVolume, clamped))
        {
            return;
        }

        BgmVolume = clamped;
        ApplyLiveVolumes();
        OnSettingsChanged?.Invoke();
    }

    public void SetSfxVolume(float value)
    {
        float clamped = Mathf.Clamp01(value);
        if (Mathf.Approximately(SfxVolume, clamped))
        {
            return;
        }

        SfxVolume = clamped;
        ApplyLiveVolumes();
        OnSettingsChanged?.Invoke();
    }

    public void SetMuted(bool muted)
    {
        if (IsMuted == muted)
        {
            return;
        }

        IsMuted = muted;
        ApplyLiveVolumes();
        OnSettingsChanged?.Invoke();
    }

    /// <summary>Ghi toàn bộ cấu hình âm lượng hiện tại xuống PlayerPrefs.</summary>
    public void SaveSettings()
    {
        PlayerPrefs.SetFloat(PrefKeyMaster, MasterVolume);
        PlayerPrefs.SetFloat(PrefKeyBgm, BgmVolume);
        PlayerPrefs.SetFloat(PrefKeySfx, SfxVolume);
        PlayerPrefs.SetInt(PrefKeyMuted, IsMuted ? 1 : 0);
        PlayerPrefs.Save();
    }

    private void LoadSettings()
    {
        MasterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(PrefKeyMaster, 1f));
        BgmVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(PrefKeyBgm, 1f));
        SfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(PrefKeySfx, 1f));
        IsMuted = PlayerPrefs.GetInt(PrefKeyMuted, 0) != 0;

        ApplyLiveVolumes();
    }

    private float GetEffectiveBgmVolume()
    {
        return IsMuted ? 0f : MasterVolume * BgmVolume;
    }

    private float GetEffectiveSfxVolume()
    {
        return IsMuted ? 0f : MasterVolume * SfxVolume;
    }

    private void ApplyLiveVolumes()
    {
        float effectiveBgm = GetEffectiveBgmVolume();

        if (_activeBgmSource != null)
        {
            _activeBgmSource.mute = IsMuted;
            _activeBgmSource.volume = _activeBgmBaseVolume * effectiveBgm * _activeFadeFactor;
        }

        if (_inactiveBgmSource != null)
        {
            _inactiveBgmSource.mute = IsMuted;
            _inactiveBgmSource.volume = _inactiveBgmBaseVolume * effectiveBgm * _inactiveFadeFactor;
        }

        if (sfxSource != null)
        {
            sfxSource.mute = IsMuted;
        }
    }

    // ================= PHÁT NHẠC NỀN (BGM) & CROSSFADE =================

    /// <summary>
    /// Phát nhạc nền theo ID từ AudioLibrary kèm hiệu ứng Crossfade mượt bằng Time.unscaledDeltaTime.
    /// An toàn cho giai đoạn Greybox: nếu chưa gắn AudioClip hoặc thiếu ID thì thoát êm không báo lỗi đỏ.
    /// </summary>
    public void PlayBgm(string bgmId, float fadeDuration = -1f)
    {
        // Xử lý an toàn khi giai đoạn Greybox chưa nạp AudioLibrary hoặc chưa có AudioClip thật
        if (audioLibrary == null || string.IsNullOrWhiteSpace(bgmId))
        {
            return;
        }

        if (!audioLibrary.TryGetBgm(bgmId, out BgmEntry entry) || entry == null || entry.clip == null)
        {
            return;
        }

        EnsureAudioSources();

        // Tránh phát lại từ đầu nếu đúng bản nhạc này đang chạy sẵn trên kênh active
        if (_activeBgmSource != null
            && _activeBgmSource.isPlaying
            && _activeBgmSource.clip == entry.clip
            && string.Equals(CurrentBgmId, entry.id, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        StopBgmFadeCoroutine();

        // Đảo vai trò 2 kênh AudioSource để kênh cũ giảm dần (Fade Out) và kênh mới tăng dần (Fade In)
        AudioSource previousActive = _activeBgmSource;
        float previousBaseVolume = _activeBgmBaseVolume;
        float previousFadeFactor = _activeFadeFactor;

        _activeBgmSource = _inactiveBgmSource;
        _inactiveBgmSource = previousActive;

        _inactiveBgmBaseVolume = previousBaseVolume;
        _inactiveFadeFactor = (_inactiveBgmSource != null && _inactiveBgmSource.isPlaying) ? previousFadeFactor : 0f;

        _activeBgmBaseVolume = Mathf.Clamp01(entry.defaultVolume);
        _activeFadeFactor = 0f;

        CurrentBgmId = entry.id;

        _activeBgmSource.clip = entry.clip;
        _activeBgmSource.loop = entry.loop;
        _activeBgmSource.mute = IsMuted;
        _activeBgmSource.volume = 0f;
        _activeBgmSource.Play();

        float duration = fadeDuration < 0f ? Mathf.Max(0f, defaultCrossfadeDuration) : fadeDuration;
        if (duration <= 0f)
        {
            FinishCrossfadeImmediate();
            return;
        }

        _bgmFadeCoroutine = StartCoroutine(CrossfadeBgmRoutine(duration));
    }

    /// <summary>Dừng nhạc nền hiện tại với hiệu ứng nhỏ dần bằng Time.unscaledDeltaTime.</summary>
    public void StopBgm(float fadeDuration = 0.5f)
    {
        StopBgmFadeCoroutine();
        CurrentBgmId = string.Empty;

        bool anyPlaying = (_activeBgmSource != null && _activeBgmSource.isPlaying)
                          || (_inactiveBgmSource != null && _inactiveBgmSource.isPlaying);
        if (!anyPlaying)
        {
            _activeFadeFactor = 0f;
            _inactiveFadeFactor = 0f;
            return;
        }

        if (fadeDuration <= 0f)
        {
            StopAllBgmSourcesImmediate();
            return;
        }

        _bgmFadeCoroutine = StartCoroutine(FadeOutAllBgmRoutine(fadeDuration));
    }

    private IEnumerator CrossfadeBgmRoutine(float duration)
    {
        float startInactiveFactor = _inactiveFadeFactor;
        float elapsed = 0f;

        try
        {
            // Dùng Time.unscaledDeltaTime (không cấp phát WaitForSeconds) để nhạc vẫn Crossfade mượt khi Time.timeScale == 0 lúc Pause
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                _activeFadeFactor = t;
                _inactiveFadeFactor = Mathf.Lerp(startInactiveFactor, 0f, t);
                ApplyLiveVolumes();
                yield return null;
            }
        }
        finally
        {
            _bgmFadeCoroutine = null;
        }

        FinishCrossfadeImmediate();
    }

    private IEnumerator FadeOutAllBgmRoutine(float duration)
    {
        float startActiveFactor = _activeFadeFactor;
        float startInactiveFactor = _inactiveFadeFactor;
        float elapsed = 0f;

        try
        {
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                _activeFadeFactor = Mathf.Lerp(startActiveFactor, 0f, t);
                _inactiveFadeFactor = Mathf.Lerp(startInactiveFactor, 0f, t);
                ApplyLiveVolumes();
                yield return null;
            }
        }
        finally
        {
            _bgmFadeCoroutine = null;
        }

        StopAllBgmSourcesImmediate();
    }

    private void FinishCrossfadeImmediate()
    {
        _activeFadeFactor = 1f;
        _inactiveFadeFactor = 0f;

        if (_inactiveBgmSource != null)
        {
            _inactiveBgmSource.Stop();
            _inactiveBgmSource.volume = 0f;
        }

        ApplyLiveVolumes();
    }

    private void StopAllBgmSourcesImmediate()
    {
        _activeFadeFactor = 0f;
        _inactiveFadeFactor = 0f;

        if (_activeBgmSource != null)
        {
            _activeBgmSource.Stop();
            _activeBgmSource.volume = 0f;
        }

        if (_inactiveBgmSource != null)
        {
            _inactiveBgmSource.Stop();
            _inactiveBgmSource.volume = 0f;
        }
    }

    // ================= PHÁT HIỆU ỨNG ÂM THANH (SFX) =================

    /// <summary>
    /// Phát hiệu ứng âm thanh theo ID từ AudioLibrary bằng PlayOneShot.
    /// Tự động thoát êm nếu AudioClip null (Greybox) để tuyệt đối không gọi PlayOneShot(null).
    /// </summary>
    public void PlaySfx(string sfxId)
    {
        if (audioLibrary == null || sfxSource == null || string.IsNullOrWhiteSpace(sfxId))
        {
            return;
        }

        if (!audioLibrary.TryGetSfx(sfxId, out SfxEntry entry) || entry == null || entry.clip == null)
        {
            return;
        }

        if (IsMuted)
        {
            return;
        }

        float finalVolume = Mathf.Clamp01(entry.defaultVolume) * GetEffectiveSfxVolume();
        if (finalVolume <= 0f)
        {
            return;
        }

        sfxSource.pitch = entry.pitchVariance > 0f
            ? 1f + UnityEngine.Random.Range(-entry.pitchVariance, entry.pitchVariance)
            : 1f;

        sfxSource.PlayOneShot(entry.clip, finalVolume);
    }

    // ================= TIỆN ÍCH KHỞI TẠO =================

    private void EnsureAudioSources()
    {
        if (bgmSourceA == null)
        {
            bgmSourceA = CreateInternalAudioSource(loop: true);
        }

        if (bgmSourceB == null)
        {
            bgmSourceB = CreateInternalAudioSource(loop: true);
        }

        if (sfxSource == null)
        {
            sfxSource = CreateInternalAudioSource(loop: false);
        }

        if (_activeBgmSource == null)
        {
            _activeBgmSource = bgmSourceA;
        }

        if (_inactiveBgmSource == null)
        {
            _inactiveBgmSource = bgmSourceB;
        }
    }

    private AudioSource CreateInternalAudioSource(bool loop)
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 0f; // Âm thanh 2D toàn màn hình
        return source;
    }

    private void StopBgmFadeCoroutine()
    {
        if (_bgmFadeCoroutine != null)
        {
            StopCoroutine(_bgmFadeCoroutine);
            _bgmFadeCoroutine = null;
        }
    }
}
