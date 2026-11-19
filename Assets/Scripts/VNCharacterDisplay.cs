using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VongNguyet.VN
{
    /// <summary>
    /// Biểu cảm của Main (Nam chính) — thiết kế bám sát kịch bản Vọng Nguyệt Chapter 1.
    /// </summary>
    public enum MainExpression
    {
        Neutral = 0,          // 01_Neutral: Bình thường / mỉm cười tự tin nhẹ
        PlayfulSmile = 1,     // 02_Playful_Smile: Cười tươi / trêu đùa hào hứng
        SeriousThinking = 2,  // 03_Serious_Thinking: Nghiêm túc / giải đố / đọc manh mối
        Shocked = 3,          // 04_Shocked: Bàng hoàng / hoảng hốt
        GuiltyRegret = 4,     // 05_Guilty_Regret: Dằn vặt / hối hận đau khổ
        Determined = 5,       // 06_Determined: Kiên quyết / gồng mình bảo vệ
        HollowDenial = 6,     // 07_Hollow_Denial: Chết lặng / cười run rẩy chối bỏ thực tại
        BrokenDespair = 7     // 08_Broken_Despair: Sụp đổ / gào khóc tuyệt vọng
    }

    /// <summary>
    /// Biểu cảm của Vân Nguyệt (Nữ chính tóc dài) — thiết kế bám sát kịch bản Vọng Nguyệt Chapter 1.
    /// </summary>
    public enum VanNguyetExpression
    {
        NeutralSmile = 0,     // 01_Neutral_Smile: Mỉm cười dịu dàng (Base tóc dài)
        Happy = 1,            // 02_Happy: Cười tươi hạnh phúc
        Uneasy = 2,           // 03_Uneasy: Lo lắng / linh cảm bất an
        Scared = 3,           // 04_Scared: Sợ hãi / hoảng loạn giữa bão lũ
        SickWeak = 4,         // 05_Sick_Weak: Sốt cao / mệt mỏi yếu ớt
        CryingBlame = 5,      // 06_Crying_Blame: Khóc tủi thân trách móc
        GentleForgive = 6,    // 07_Gentle_Forgive: Nhắm mắt mỉm cười tha thứ
        DeterminedUrgent = 7  // 08_Determined_Urgent: Kiên quyết / gấp gáp đẩy Main ra
    }

    [Serializable]
    public struct ExpressionSpriteEntry
    {
        public string key;
        public Sprite fullBodySprite;
        public Sprite portraitSprite;
    }

    /// <summary>
    /// Component hiển thị nhân vật 2D Visual Novel trong Unity UI (Canvas) với hiệu ứng
    /// hiện/ẩn mượt, chuyển biểu cảm cross-fade và làm tối nhân vật không nói.
    /// Có thể điều khiển trực tiếp từ DialogueManager (qua ShowSprite/Hide) hoặc gán thủ công trên Inspector.
    /// </summary>
    public class VNCharacterDisplay : MonoBehaviour
    {
        [Header("UI References (Visual Novel Canvas)")]
        [SerializeField] private Image primaryImage;
        [SerializeField] private Image crossFadeOverlayImage;
        [SerializeField] private Image dialoguePortraitImage;

        [Header("Expression Library (Tùy chọn nếu gán thủ công)")]
        [SerializeField] private List<ExpressionSpriteEntry> expressions = new List<ExpressionSpriteEntry>();

        [Header("Visual Settings")]
        [SerializeField] private float defaultFadeDuration = 0.18f;
        [SerializeField] private Color activeSpeakerColor = Color.white;
        [SerializeField] private Color inactiveSpeakerColor = new Color(0.58f, 0.60f, 0.68f, 1f);

        private readonly Dictionary<string, ExpressionSpriteEntry> _lookup =
            new Dictionary<string, ExpressionSpriteEntry>(StringComparer.OrdinalIgnoreCase);

        private Coroutine _fadeRoutine;
        private bool _isSpeaking = true;

        public bool IsVisible => primaryImage != null && primaryImage.gameObject.activeSelf && primaryImage.color.a > 0.01f;

        private void Awake()
        {
            if (primaryImage == null) primaryImage = GetComponent<Image>();
            RebuildLookup();

            if (crossFadeOverlayImage != null)
            {
                crossFadeOverlayImage.gameObject.SetActive(false);
            }
        }

        public void RebuildLookup()
        {
            _lookup.Clear();
            foreach (var entry in expressions)
            {
                if (!string.IsNullOrEmpty(entry.key))
                {
                    _lookup[entry.key] = entry;
                }
            }
        }

        public void SetMainExpression(MainExpression expr, float fadeDuration = -1f)
        {
            SetExpressionByIndex((int)expr, fadeDuration);
        }

        public void SetVanNguyetExpression(VanNguyetExpression expr, float fadeDuration = -1f)
        {
            SetExpressionByIndex((int)expr, fadeDuration);
        }

        public void SetExpression(string key, float fadeDuration = -1f)
        {
            if (_lookup.Count == 0) RebuildLookup();
            if (_lookup.TryGetValue(key, out var entry))
            {
                ShowSprite(entry.fullBodySprite, fadeDuration, entry.portraitSprite);
                return;
            }

            // Tự động fallback sang VNCharacterLibrary (Resources/Characters/...)
            Sprite full = VNCharacterLibrary.LoadFullBody(key);
            Sprite port = VNCharacterLibrary.LoadPortrait(key);
            if (full != null)
            {
                ShowSprite(full, fadeDuration, port);
            }
            else
            {
                Debug.LogWarning($"[VNCharacterDisplay] Không tìm thấy biểu cảm '{key}' trên {name}.");
            }
        }

        public void SetExpressionByIndex(int index, float fadeDuration = -1f)
        {
            if (index < 0 || index >= expressions.Count)
            {
                Debug.LogWarning($"[VNCharacterDisplay] Index biểu cảm {index} nằm ngoài danh sách trên {name}.");
                return;
            }
            var entry = expressions[index];
            ShowSprite(entry.fullBodySprite, fadeDuration, entry.portraitSprite);
        }

        /// <summary>
        /// Hiển thị trực tiếp một Sprite toàn thân lên vị trí nhân vật này (có fade-in hoặc cross-fade mượt).
        /// </summary>
        public void ShowSprite(Sprite fullBody, float fadeDuration = -1f, Sprite portrait = null)
        {
            if (dialoguePortraitImage != null && portrait != null)
            {
                dialoguePortraitImage.sprite = portrait;
            }

            if (primaryImage == null) return;
            if (fullBody == null)
            {
                Hide(fadeDuration);
                return;
            }

            float duration = fadeDuration < 0f ? defaultFadeDuration : fadeDuration;
            Color targetColor = _isSpeaking ? activeSpeakerColor : inactiveSpeakerColor;

            // Nếu đang ẩn -> fade-in mượt
            if (!primaryImage.gameObject.activeSelf || primaryImage.sprite == null || primaryImage.color.a <= 0.01f)
            {
                primaryImage.sprite = fullBody;
                primaryImage.preserveAspect = true;
                primaryImage.gameObject.SetActive(true);

                if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
                if (!gameObject.activeInHierarchy || duration <= 0.001f)
                {
                    primaryImage.color = targetColor;
                }
                else
                {
                    _fadeRoutine = StartCoroutine(FadeAlphaRoutine(0f, 1f, duration, false));
                }
                return;
            }

            // Nếu trùng sprite hiện tại -> chỉ cập nhật độ sáng nói/nghe
            if (primaryImage.sprite == fullBody)
            {
                primaryImage.color = targetColor;
                return;
            }

            // Đang hiển thị sprite khác -> cross-fade (nếu có overlay) hoặc swap tức thì
            if (crossFadeOverlayImage == null || !gameObject.activeInHierarchy || duration <= 0.001f)
            {
                primaryImage.sprite = fullBody;
                primaryImage.color = targetColor;
                return;
            }

            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
            _fadeRoutine = StartCoroutine(CrossFadeRoutine(fullBody, duration));
        }

        /// <summary>
        /// Ẩn nhân vật khỏi sân khấu Visual Novel (có fade-out).
        /// </summary>
        public void Hide(float fadeDuration = -1f)
        {
            if (primaryImage == null) return;
            if (crossFadeOverlayImage != null) crossFadeOverlayImage.gameObject.SetActive(false);

            if (!primaryImage.gameObject.activeSelf) return;

            float duration = fadeDuration < 0f ? defaultFadeDuration : fadeDuration;
            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);

            if (!gameObject.activeInHierarchy || duration <= 0.001f)
            {
                primaryImage.gameObject.SetActive(false);
                primaryImage.sprite = null;
            }
            else
            {
                _fadeRoutine = StartCoroutine(FadeAlphaRoutine(primaryImage.color.a, 0f, duration, true));
            }
        }

        /// <summary>
        /// Làm sáng nhân vật đang nói hoặc làm tối nhẹ nhân vật đang lắng nghe.
        /// </summary>
        public void SetSpeaking(bool isSpeaking)
        {
            _isSpeaking = isSpeaking;
            if (primaryImage != null && primaryImage.gameObject.activeSelf)
            {
                Color target = isSpeaking ? activeSpeakerColor : inactiveSpeakerColor;
                target.a = primaryImage.color.a;
                primaryImage.color = target;
            }
        }

        private IEnumerator FadeAlphaRoutine(float fromA, float toA, float duration, bool hideOnComplete)
        {
            Color baseColor = _isSpeaking ? activeSpeakerColor : inactiveSpeakerColor;
            primaryImage.color = new Color(baseColor.r, baseColor.g, baseColor.b, fromA);

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float a = Mathf.Lerp(fromA, toA, t);
                Color c = _isSpeaking ? activeSpeakerColor : inactiveSpeakerColor;
                primaryImage.color = new Color(c.r, c.g, c.b, a);
                yield return null;
            }

            Color endColor = _isSpeaking ? activeSpeakerColor : inactiveSpeakerColor;
            primaryImage.color = new Color(endColor.r, endColor.g, endColor.b, toA);

            if (hideOnComplete)
            {
                primaryImage.gameObject.SetActive(false);
                primaryImage.sprite = null;
            }

            _fadeRoutine = null;
        }

        private IEnumerator CrossFadeRoutine(Sprite nextSprite, float duration)
        {
            Color baseColor = _isSpeaking ? activeSpeakerColor : inactiveSpeakerColor;

            crossFadeOverlayImage.sprite = nextSprite;
            crossFadeOverlayImage.preserveAspect = true;
            crossFadeOverlayImage.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0f);
            crossFadeOverlayImage.gameObject.SetActive(true);

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                Color c = _isSpeaking ? activeSpeakerColor : inactiveSpeakerColor;
                crossFadeOverlayImage.color = new Color(c.r, c.g, c.b, t);
                yield return null;
            }

            Color finalColor = _isSpeaking ? activeSpeakerColor : inactiveSpeakerColor;
            primaryImage.sprite = nextSprite;
            primaryImage.color = finalColor;
            crossFadeOverlayImage.gameObject.SetActive(false);
            _fadeRoutine = null;
        }
    }
}
