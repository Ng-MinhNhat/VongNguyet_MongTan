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
    /// Biểu cảm của Vân Nguyệt (Nữ chính) — thiết kế bám sát kịch bản Vọng Nguyệt Chapter 1.
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
    /// chuyển biểu cảm mượt (cross-fade) và tự động làm tối nhân vật không nói.
    /// Tất cả FullBody PNG đều chuẩn hóa kích thước 900x1400, cùng đường chân (baseline)
    /// nên có thể đặt Main và Vân Nguyệt cùng vị trí Y & Scale (1, 1, 1).
    /// </summary>
    public class VNCharacterDisplay : MonoBehaviour
    {
        [Header("UI References (Visual Novel Canvas)")]
        [SerializeField] private Image primaryImage;
        [SerializeField] private Image crossFadeOverlayImage;
        [SerializeField] private Image dialoguePortraitImage;

        [Header("Expression Library")]
        [SerializeField] private List<ExpressionSpriteEntry> expressions = new List<ExpressionSpriteEntry>();

        [Header("Visual Settings")]
        [SerializeField] private float defaultFadeDuration = 0.18f;
        [SerializeField] private Color activeSpeakerColor = Color.white;
        [SerializeField] private Color inactiveSpeakerColor = new Color(0.62f, 0.64f, 0.72f, 1f);

        private readonly Dictionary<string, ExpressionSpriteEntry> _lookup = new Dictionary<string, ExpressionSpriteEntry>(StringComparer.OrdinalIgnoreCase);
        private Coroutine _fadeRoutine;
        private bool _isSpeaking = true;

        private void Awake()
        {
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
                ApplySprites(entry.fullBodySprite, entry.portraitSprite, fadeDuration < 0f ? defaultFadeDuration : fadeDuration);
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
            ApplySprites(entry.fullBodySprite, entry.portraitSprite, fadeDuration < 0f ? defaultFadeDuration : fadeDuration);
        }

        /// <summary>
        /// Làm sáng nhân vật đang nói hoặc làm tối nhẹ nhân vật đang lắng nghe.
        /// </summary>
        public void SetSpeaking(bool isSpeaking)
        {
            _isSpeaking = isSpeaking;
            if (primaryImage != null)
            {
                Color target = isSpeaking ? activeSpeakerColor : inactiveSpeakerColor;
                target.a = primaryImage.color.a;
                primaryImage.color = target;
            }
        }

        private void ApplySprites(Sprite fullBody, Sprite portrait, float duration)
        {
            if (dialoguePortraitImage != null && portrait != null)
            {
                dialoguePortraitImage.sprite = portrait;
            }

            if (primaryImage == null || fullBody == null) return;

            if (crossFadeOverlayImage == null || duration <= 0.001f || primaryImage.sprite == null)
            {
                primaryImage.sprite = fullBody;
                return;
            }

            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
            _fadeRoutine = StartCoroutine(CrossFadeRoutine(fullBody, duration));
        }

        private IEnumerator CrossFadeRoutine(Sprite nextSprite, float duration)
        {
            Color baseColor = _isSpeaking ? activeSpeakerColor : inactiveSpeakerColor;

            crossFadeOverlayImage.sprite = nextSprite;
            crossFadeOverlayImage.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0f);
            crossFadeOverlayImage.gameObject.SetActive(true);

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                crossFadeOverlayImage.color = new Color(baseColor.r, baseColor.g, baseColor.b, t);
                yield return null;
            }

            primaryImage.sprite = nextSprite;
            primaryImage.color = baseColor;
            crossFadeOverlayImage.gameObject.SetActive(false);
            _fadeRoutine = null;
        }
    }
}
