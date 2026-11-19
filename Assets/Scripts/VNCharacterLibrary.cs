using System;
using System.Collections.Generic;
using UnityEngine;

namespace VongNguyet.VN
{
    /// <summary>
    /// Bộ ánh xạ & nạp Sprite thông minh cho các nhân vật Vọng Nguyệt.
    /// Tự động dịch cả key tiếng Việt trong JSON (Main_TuTin, VanNguyet_LoLang...)
    /// lẫn key chuẩn hóa (Main_07_Hollow_Denial, VanNguyet_01_Neutral_Smile...)
    /// sang đúng ảnh FullBody và Portrait nằm trong Resources/.
    /// Có cache bộ nhớ và cơ chế fallback an toàn nếu biểu cảm chưa có ảnh.
    /// </summary>
    public static class VNCharacterLibrary
    {
        private const string CharactersRoot = "Characters/";
        private const string LegacyPortraitsRoot = "Portraits/";

        // Bảng ánh xạ từ key tiếng Việt (trong Scene01..06.json) -> (Thư mục nhân vật, Mã biểu cảm chuẩn)
        private static readonly Dictionary<string, (string folder, string code)> AliasMap =
            new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
            {
                // ---- MAIN (Nam chính — 8 biểu cảm) ----
                { "Main_BinhThuong",     ("Main", "01_Neutral") },
                { "Main_01_Neutral",     ("Main", "01_Neutral") },

                { "Main_TuTin",          ("Main", "02_Playful_Smile") },
                { "Main_VuiVe",          ("Main", "02_Playful_Smile") },
                { "Main_02_Playful_Smile", ("Main", "02_Playful_Smile") },

                { "Main_ThacMac",        ("Main", "03_Serious_Thinking") },
                { "Main_NghiemTuc",      ("Main", "03_Serious_Thinking") },
                { "Main_03_Serious_Thinking", ("Main", "03_Serious_Thinking") },

                { "Main_HoangMang",      ("Main", "04_Shocked") },
                { "Main_SoHai",          ("Main", "04_Shocked") },
                { "Main_04_Shocked",     ("Main", "04_Shocked") },

                { "Main_LoLang",         ("Main", "05_Guilty_Regret") },
                { "Main_YeuOt",          ("Main", "05_Guilty_Regret") },
                { "Main_HoiHan",         ("Main", "05_Guilty_Regret") },
                { "Main_05_Guilty_Regret", ("Main", "05_Guilty_Regret") },

                { "Main_KienQuyet",      ("Main", "06_Determined") },
                { "Main_QuyetTam",       ("Main", "06_Determined") },
                { "Main_06_Determined",  ("Main", "06_Determined") },

                { "Main_CuoiRunRay",     ("Main", "07_Hollow_Denial") },
                { "Main_ChongChe",       ("Main", "07_Hollow_Denial") },
                { "Main_07_Hollow_Denial", ("Main", "07_Hollow_Denial") },

                { "Main_TuyetVong",      ("Main", "08_Broken_Despair") },
                { "Main_Khoc",           ("Main", "08_Broken_Despair") },
                { "Main_08_Broken_Despair", ("Main", "08_Broken_Despair") },

                // ---- VÂN NGUYỆT (Nữ chính tóc dài — 8 biểu cảm) ----
                { "VanNguyet_BinhThuong",     ("VanNguyet", "01_Neutral_Smile") },
                { "VanNguyet_01_Neutral_Smile", ("VanNguyet", "01_Neutral_Smile") },

                { "VanNguyet_Vui",            ("VanNguyet", "02_Happy") },
                { "VanNguyet_HanhPhuc",       ("VanNguyet", "02_Happy") },
                { "VanNguyet_02_Happy",       ("VanNguyet", "02_Happy") },

                { "VanNguyet_LoLang",         ("VanNguyet", "03_Uneasy") },
                { "VanNguyet_BatAn",          ("VanNguyet", "03_Uneasy") },
                { "VanNguyet_03_Uneasy",      ("VanNguyet", "03_Uneasy") },

                { "VanNguyet_SoHai",          ("VanNguyet", "04_Scared") },
                { "VanNguyet_04_Scared",      ("VanNguyet", "04_Scared") },

                { "VanNguyet_YeuOt",          ("VanNguyet", "05_Sick_Weak") },
                { "VanNguyet_Sot",            ("VanNguyet", "05_Sick_Weak") },
                { "VanNguyet_05_Sick_Weak",   ("VanNguyet", "05_Sick_Weak") },

                { "VanNguyet_Khoc",           ("VanNguyet", "06_Crying_Blame") },
                { "VanNguyet_06_Crying_Blame", ("VanNguyet", "06_Crying_Blame") },

                { "VanNguyet_ThaThu",         ("VanNguyet", "07_Gentle_Forgive") },
                { "VanNguyet_07_Gentle_Forgive", ("VanNguyet", "07_Gentle_Forgive") },

                { "VanNguyet_KienQuyet",      ("VanNguyet", "08_Determined_Urgent") },
                { "VanNguyet_08_Determined_Urgent", ("VanNguyet", "08_Determined_Urgent") }
            };

        private static readonly Dictionary<string, string> DefaultBaseCode =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Main", "01_Neutral" },
                { "VanNguyet", "01_Neutral_Smile" }
            };

        private static readonly Dictionary<string, Sprite> FullBodyCache = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Sprite> PortraitCache = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Kiểm tra một key portrait thuộc nhân vật chính nào ("Main", "VanNguyet", hoặc null nếu là nhân vật phụ).
        /// </summary>
        public static string GetCharacterFolder(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            string trimmed = key.Trim();

            if (AliasMap.TryGetValue(trimmed, out var mapped))
            {
                return mapped.folder;
            }

            if (trimmed.StartsWith("Main_", StringComparison.OrdinalIgnoreCase)) return "Main";
            if (trimmed.StartsWith("VanNguyet_", StringComparison.OrdinalIgnoreCase)) return "VanNguyet";
            return null;
        }

        /// <summary>
        /// Nạp Sprite toàn thân (FullBody) cho sân khấu Visual Novel.
        /// Nếu biểu cảm cụ thể chưa tồn tại trong Resources/, tự động fallback về bản Base của nhân vật đó.
        /// </summary>
        public static Sprite LoadFullBody(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            string trimmed = key.Trim();

            if (FullBodyCache.TryGetValue(trimmed, out Sprite cached)) return cached;

            Sprite sprite = null;

            if (AliasMap.TryGetValue(trimmed, out var mapped))
            {
                sprite = Resources.Load<Sprite>($"{CharactersRoot}{mapped.folder}/FullBody/{mapped.folder}_{mapped.code}");
                if (sprite == null && DefaultBaseCode.TryGetValue(mapped.folder, out string baseCode))
                {
                    sprite = Resources.Load<Sprite>($"{CharactersRoot}{mapped.folder}/FullBody/{mapped.folder}_{baseCode}");
                }
            }
            else
            {
                string folder = GetCharacterFolder(trimmed);
                if (!string.IsNullOrEmpty(folder))
                {
                    sprite = Resources.Load<Sprite>($"{CharactersRoot}{folder}/FullBody/{trimmed}");
                    if (sprite == null && DefaultBaseCode.TryGetValue(folder, out string baseCode))
                    {
                        sprite = Resources.Load<Sprite>($"{CharactersRoot}{folder}/FullBody/{folder}_{baseCode}");
                    }
                }
            }

            FullBodyCache[trimmed] = sprite;
            return sprite;
        }

        /// <summary>
        /// Nạp Sprite cận mặt (Portrait 512x512) cho ô avatar hộp thoại.
        /// Hỗ trợ đồng thời:
        /// 1. Resources/Characters/{Character}/Portraits/{Character}_Portrait_{Code}
        /// 2. Fallback về Portrait Base nếu biểu cảm chưa có ảnh
        /// 3. Resources/Portraits/{rawKey} (cho nhân vật phụ: OngLao_*, Hoa_*, QuanGia_*, CoBe_*)
        /// </summary>
        public static Sprite LoadPortrait(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            string trimmed = key.Trim();

            if (PortraitCache.TryGetValue(trimmed, out Sprite cached)) return cached;

            Sprite sprite = null;

            if (AliasMap.TryGetValue(trimmed, out var mapped))
            {
                sprite = Resources.Load<Sprite>($"{CharactersRoot}{mapped.folder}/Portraits/{mapped.folder}_Portrait_{mapped.code}");
                if (sprite == null && DefaultBaseCode.TryGetValue(mapped.folder, out string baseCode))
                {
                    sprite = Resources.Load<Sprite>($"{CharactersRoot}{mapped.folder}/Portraits/{mapped.folder}_Portrait_{baseCode}");
                }
            }

            // Thử trực tiếp thư mục cũ Resources/Portraits/{key} (cho NPC hoặc ảnh đặt thủ công)
            if (sprite == null)
            {
                sprite = Resources.Load<Sprite>(LegacyPortraitsRoot + trimmed);
            }

            PortraitCache[trimmed] = sprite;
            return sprite;
        }

        public static void ClearCache()
        {
            FullBodyCache.Clear();
            PortraitCache.Clear();
        }
    }
}
