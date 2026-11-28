using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Công cụ Unity Editor tự động thiết lập trọn gói Hệ thống Âm thanh & Cài đặt (uGUI + TextMeshPro):
/// 1) Sinh bộ file .wav Placeholder (PCM 16-bit Mono, 44100Hz) bằng C# thuần tại Assets/Audio/Placeholder/.
/// 2) Tạo/cập nhật Assets/Audio/AudioLibrary_Ch1.asset nối đủ 2 BGM và 7 SFX.
/// 3) Cấu hình đồng bộ cho cả MainMenu.unity và SampleScene.unity:
///    - Đặt AudioManager dự phòng ở cấp Root (Singleton DontDestroyOnLoad sẽ tự hủy bản thừa khi chuyển Scene).
///    - Sinh SettingsCanvas (sortingOrder = 90) và nối toàn bộ SettingsUIController.
///    - Bổ sung nút "CÀI ĐẶT" vào MainMenuController và PauseMenuController.
/// </summary>
public static class AudioSettingsSetupGenerator
{
    private const string AudioRootFolder = "Assets/Audio";
    private const string PlaceholderFolder = "Assets/Audio/Placeholder";
    private const string AudioLibraryAssetPath = "Assets/Audio/AudioLibrary_Ch1.asset";

    private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
    private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";

    private const string AudioManagerObjectName = "AudioManager";
    private const string SettingsCanvasName = "SettingsCanvas";

    private const int SampleRate = 44100;

    // Bảng màu tối ánh trăng đồng bộ với các Canvas uGUI khác của dự án
    private static readonly Color BackdropDimColor = new Color(0.00f, 0.00f, 0.00f, 0.75f);
    private static readonly Color WindowBgColor = new Color(0.08f, 0.10f, 0.16f, 0.98f);
    private static readonly Color TrackBgColor = new Color(0.18f, 0.21f, 0.29f, 1.00f);
    private static readonly Color FillAccentColor = new Color(0.24f, 0.58f, 0.88f, 1.00f);
    private static readonly Color HandleColor = new Color(0.95f, 0.94f, 0.90f, 1.00f);
    private static readonly Color ToggleCheckColor = new Color(0.92f, 0.36f, 0.32f, 1.00f);
    private static readonly Color SettingsButtonColor = new Color(0.24f, 0.30f, 0.44f, 1.00f);
    private static readonly Color TestSfxButtonColor = new Color(0.20f, 0.46f, 0.72f, 1.00f);
    private static readonly Color CloseButtonColor = new Color(0.56f, 0.22f, 0.24f, 1.00f);
    private static readonly Color BorderSubtleColor = new Color(0.55f, 0.65f, 0.84f, 0.45f);
    private static readonly Color PrimaryTextColor = new Color(0.96f, 0.94f, 0.89f, 1.00f);
    private static readonly Color SecondaryTextColor = new Color(0.80f, 0.84f, 0.92f, 1.00f);

    [MenuItem("Vong Nguyet/Audio/Generate Audio System, Placeholder WAVs & Settings UI")]
    public static void GenerateAudioSystemAndSettingsUI()
    {
        // Bảo vệ thay đổi chưa lưu trước khi công cụ mở lần lượt MainMenu.unity và SampleScene.unity
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        string originalScenePath = SceneManager.GetActiveScene().path;
        TMP_FontAsset cachedFont = FindProjectTMPFont(SceneManager.GetActiveScene());

        // Phần 1: Sinh (hoặc ghi đè) bộ file .wav Placeholder (PCM 16-bit Mono, 44100Hz) và import đồng bộ
        int generatedWavCount = GeneratePlaceholderWavFiles();

        // Phần 2: Tạo/cập nhật Assets/Audio/AudioLibrary_Ch1.asset và nối đủ 2 BGM + 7 SFX (đã import đồng bộ)
        AudioLibrary audioLibrary = CreateOrUpdateAudioLibrary();
        if (audioLibrary == null)
        {
            Debug.LogError("[AudioSettingsSetupGenerator] Không thể tạo AudioLibrary_Ch1.asset. Dừng quá trình thiết lập.");
            return;
        }

        // Phần 3: Tự động cấu hình đồng bộ cho cả MainMenu.unity và SampleScene.unity
        List<string> processedScenes = new List<string>();
        string[] targetScenes = { MainMenuScenePath, SampleScenePath };

        for (int i = 0; i < targetScenes.Length; i++)
        {
            string scenePath = targetScenes[i];
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            {
                Debug.LogWarning($"[AudioSettingsSetupGenerator] Không tìm thấy Scene '{scenePath}', bỏ qua Scene này.");
                continue;
            }

            bool isMainMenuScene = string.Equals(scenePath, MainMenuScenePath, StringComparison.OrdinalIgnoreCase);
            if (ConfigureSceneAudioAndSettings(scenePath, isMainMenuScene, audioLibrary, cachedFont))
            {
                processedScenes.Add(scenePath);
            }
        }

        // Phần 4: Lưu toàn bộ Asset và mở lại đúng Scene ban đầu của người dùng
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (!string.IsNullOrEmpty(originalScenePath) && File.Exists(GetFullProjectPath(originalScenePath)))
        {
            EditorSceneManager.OpenScene(originalScenePath, OpenSceneMode.Single);
        }

        Debug.Log(
            $"[AudioSettingsSetupGenerator] Hoàn tất thiết lập Hệ thống Âm thanh & Settings UI!\n" +
            $"- File .wav Placeholder đã ghi & import đồng bộ (44100Hz 16-bit Mono): {generatedWavCount} ({PlaceholderFolder})\n" +
            $"- Thư viện âm thanh: {AudioLibraryAssetPath} (2 BGM + 7 SFX)\n" +
            $"- Các Scene đã cấu hình & lưu: {(processedScenes.Count > 0 ? string.Join(", ", processedScenes) : "Không có")}"
        );
    }

    // ================= PHẦN 1: SINH FILE .WAV PLACEHOLDER (PCM 16-BIT MONO, 44100HZ) =================

    private static int GeneratePlaceholderWavFiles()
    {
        EnsureFolderExists(AudioRootFolder);
        EnsureFolderExists(PlaceholderFolder);

        int count = 0;

        // 2 BGM dạng sóng trầm lặp mượt (4.0s, biên độ 0.25f, fade nhẹ 2 đầu để không bị tiếng "tạch" khi loop)
        count += WriteAndImportWav($"{PlaceholderFolder}/BGM_MainMenu.wav", SynthesizeMainMenuBgm(4.0f));
        count += WriteAndImportWav($"{PlaceholderFolder}/BGM_BietThu_BinhThuong.wav", SynthesizeMansionDroneBgm(4.0f));

        // 7 SFX ngắn (0.07s - 0.34s, biên độ 0.45f, không dư khoảng lặng đầu file)
        count += WriteAndImportWav($"{PlaceholderFolder}/SFX_UI_Click.wav", SynthesizeUiClick(0.07f));
        count += WriteAndImportWav($"{PlaceholderFolder}/SFX_Item_Pickup.wav", SynthesizeItemPickup(0.24f));
        count += WriteAndImportWav($"{PlaceholderFolder}/SFX_Item_Combine.wav", SynthesizeItemCombine(0.34f));
        count += WriteAndImportWav($"{PlaceholderFolder}/SFX_Keypad_Press.wav", SynthesizeKeypadPress(0.07f));
        count += WriteAndImportWav($"{PlaceholderFolder}/SFX_Keypad_Success.wav", SynthesizeKeypadSuccess(0.30f));
        count += WriteAndImportWav($"{PlaceholderFolder}/SFX_Keypad_Error.wav", SynthesizeKeypadError(0.28f));
        count += WriteAndImportWav($"{PlaceholderFolder}/SFX_Door_Transition.wav", SynthesizeDoorTransition(0.32f));

        return count;
    }

    private static int WriteAndImportWav(string assetPath, float[] samples)
    {
        string fullPath = GetFullProjectPath(assetPath);
        string directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Cho phép ghi đè lại file .wav Placeholder cũ mỗi khi chạy lại menu để sửa lỗi file hỏng/câm
        using (FileStream fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
        using (BinaryWriter writer = new BinaryWriter(fs, Encoding.ASCII))
        {
            const short numChannels = 1;
            const short bitsPerSample = 16;
            int bytesPerSample = bitsPerSample / 8;
            short blockAlign = (short)(numChannels * bytesPerSample);
            int byteRate = SampleRate * blockAlign;
            int subChunk2Size = samples.Length * blockAlign;
            int chunkSize = 36 + subChunk2Size;

            // Header chuẩn RIFF / WAVE PCM 16-bit Mono 44100Hz (44 bytes theo đúng thứ tự đặc tả WAV)
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(chunkSize);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));

            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);               // Subchunk1Size = 16 (PCM)
            writer.Write((short)1);         // AudioFormat = 1 (PCM không nén)
            writer.Write(numChannels);      // NumChannels = 1 (Mono)
            writer.Write(SampleRate);       // SampleRate = 44100
            writer.Write(byteRate);         // ByteRate = SampleRate * BlockAlign
            writer.Write(blockAlign);       // BlockAlign = 2
            writer.Write(bitsPerSample);    // BitsPerSample = 16

            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(subChunk2Size);

            for (int i = 0; i < samples.Length; i++)
            {
                float clamped = Mathf.Clamp(samples[i], -1f, 1f);
                short pcm = (short)Mathf.RoundToInt(clamped * 32767f);
                writer.Write(pcm);
            }
        }

        // BẮT BUỘC import đồng bộ (ForceSynchronousImport) ngay sau khi ghi file để LoadAssetAtPath<AudioClip> không bao giờ trả về null
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        return 1;
    }

    private static float[] SynthesizeMainMenuBgm(float duration)
    {
        int count = Mathf.RoundToInt(duration * SampleRate);
        float[] buffer = new float[count];
        float fadeEdge = 0.08f;

        // Hợp âm thứ trầm chậm (Am: A2 110Hz, C3 130.81Hz, E3 164.81Hz, A3 220Hz), biên độ tổng 0.25f
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / SampleRate;
            float lfo = 0.80f + 0.20f * Mathf.Sin(2f * Mathf.PI * 0.5f * t);
            float wave =
                0.40f * Mathf.Sin(2f * Mathf.PI * 110.0f * t) +
                0.25f * Mathf.Sin(2f * Mathf.PI * 130.81f * t) +
                0.22f * Mathf.Sin(2f * Mathf.PI * 164.81f * t) +
                0.13f * Mathf.Sin(2f * Mathf.PI * 220.0f * t);

            buffer[i] = wave * lfo * ComputeLoopEdgeEnvelope(t, duration, fadeEdge) * 0.25f;
        }

        return buffer;
    }

    private static float[] SynthesizeMansionDroneBgm(float duration)
    {
        int count = Mathf.RoundToInt(duration * SampleRate);
        float[] buffer = new float[count];
        float fadeEdge = 0.08f;

        // Âm drone trầm u ám hơn (Dm thấp: D2 73.42Hz, F2 87.31Hz, A2 110Hz, D3 146.83Hz), biên độ tổng 0.25f
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / SampleRate;
            float lfo = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 0.25f * t);
            float wave =
                0.45f * Mathf.Sin(2f * Mathf.PI * 73.42f * t) +
                0.25f * Mathf.Sin(2f * Mathf.PI * 87.31f * t) +
                0.20f * Mathf.Sin(2f * Mathf.PI * 110.0f * t) +
                0.10f * Mathf.Sin(2f * Mathf.PI * 146.83f * t);

            buffer[i] = wave * lfo * ComputeLoopEdgeEnvelope(t, duration, fadeEdge) * 0.25f;
        }

        return buffer;
    }

    private static float[] SynthesizeUiClick(float duration)
    {
        int count = Mathf.RoundToInt(duration * SampleRate);
        float[] buffer = new float[count];
        float phase = 0f;

        for (int i = 0; i < count; i++)
        {
            float t = (float)i / SampleRate;
            float freq = Mathf.Lerp(940f, 520f, t / duration);
            phase += 2f * Mathf.PI * freq / SampleRate;
            float env = ComputeShortAttackDecay(t, duration, 0.002f, 32f);
            buffer[i] = Mathf.Sin(phase) * env * 0.45f;
        }

        return buffer;
    }

    private static float[] SynthesizeItemPickup(float duration)
    {
        int count = Mathf.RoundToInt(duration * SampleRate);
        float[] buffer = new float[count];
        float half = duration * 0.45f;

        // 2 nốt tăng sáng C5 -> G5 (biên độ 0.45f)
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / SampleRate;
            float freq = t < half ? 523.25f : 783.99f;
            float localT = t < half ? t : (t - half);
            float env = ComputeShortAttackDecay(localT, duration, 0.003f, 10f);
            buffer[i] = (0.75f * Mathf.Sin(2f * Mathf.PI * freq * t) + 0.25f * Mathf.Sin(2f * Mathf.PI * freq * 2f * t)) * env * 0.45f;
        }

        return buffer;
    }

    private static float[] SynthesizeItemCombine(float duration)
    {
        int count = Mathf.RoundToInt(duration * SampleRate);
        float[] buffer = new float[count];
        float step = duration / 3f;

        // 3 nốt hợp âm trưởng A4 -> C#5 -> E5 (biên độ 0.45f)
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / SampleRate;
            float freq = t < step ? 440f : (t < step * 2f ? 554.37f : 659.25f);
            float env = ComputeShortAttackDecay(t, duration, 0.004f, 5.5f);
            buffer[i] = (0.7f * Mathf.Sin(2f * Mathf.PI * freq * t) + 0.3f * Mathf.Sin(2f * Mathf.PI * freq * 1.5f * t)) * env * 0.45f;
        }

        return buffer;
    }

    private static float[] SynthesizeKeypadPress(float duration)
    {
        int count = Mathf.RoundToInt(duration * SampleRate);
        float[] buffer = new float[count];

        for (int i = 0; i < count; i++)
        {
            float t = (float)i / SampleRate;
            float env = ComputeShortAttackDecay(t, duration, 0.002f, 28f);
            buffer[i] = Mathf.Sin(2f * Mathf.PI * 1180f * t) * env * 0.45f;
        }

        return buffer;
    }

    private static float[] SynthesizeKeypadSuccess(float duration)
    {
        int count = Mathf.RoundToInt(duration * SampleRate);
        float[] buffer = new float[count];
        float split = duration * 0.42f;

        for (int i = 0; i < count; i++)
        {
            float t = (float)i / SampleRate;
            float freq = t < split ? 880f : 1318.5f;
            float localT = t < split ? t : (t - split);
            float env = ComputeShortAttackDecay(localT, duration, 0.003f, 9f);
            buffer[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * env * 0.45f;
        }

        return buffer;
    }

    private static float[] SynthesizeKeypadError(float duration)
    {
        int count = Mathf.RoundToInt(duration * SampleRate);
        float[] buffer = new float[count];

        // 2 tần số thấp nghịch tai 185Hz + 170Hz (biên độ 0.45f)
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / SampleRate;
            float env = ComputeShortAttackDecay(t, duration, 0.003f, 7f);
            float wave = 0.55f * Mathf.Sin(2f * Mathf.PI * 185f * t) + 0.45f * Mathf.Sin(2f * Mathf.PI * 170f * t);
            buffer[i] = wave * env * 0.45f;
        }

        return buffer;
    }

    private static float[] SynthesizeDoorTransition(float duration)
    {
        int count = Mathf.RoundToInt(duration * SampleRate);
        float[] buffer = new float[count];
        float phase = 0f;

        // Tiếng trầm đóng/mở cửa gỗ (quét tần số 230Hz -> 85Hz, biên độ 0.45f)
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / SampleRate;
            float freq = Mathf.Lerp(230f, 85f, t / duration);
            phase += 2f * Mathf.PI * freq / SampleRate;
            float env = ComputeShortAttackDecay(t, duration, 0.005f, 7.5f);
            float wave = 0.75f * Mathf.Sin(phase) + 0.25f * Mathf.Sin(phase * 2.1f);
            buffer[i] = wave * env * 0.45f;
        }

        return buffer;
    }

    private static float ComputeLoopEdgeEnvelope(float t, float duration, float edgeSeconds)
    {
        if (t < edgeSeconds)
        {
            return 0.5f * (1f - Mathf.Cos(Mathf.PI * (t / edgeSeconds)));
        }

        float remaining = duration - t;
        if (remaining < edgeSeconds)
        {
            return 0.5f * (1f - Mathf.Cos(Mathf.PI * (remaining / edgeSeconds)));
        }

        return 1f;
    }

    private static float ComputeShortAttackDecay(float t, float duration, float attackSeconds, float decayRate)
    {
        float attack = attackSeconds > 0f ? Mathf.Clamp01(t / attackSeconds) : 1f;
        float tailFade = Mathf.Clamp01((duration - t) / 0.008f);
        return attack * Mathf.Exp(-decayRate * t) * tailFade;
    }

    // ================= PHẦN 2: TẠO / CẬP NHẬT AUDIOLIBRARY_CH1.ASSET =================

    private static AudioLibrary CreateOrUpdateAudioLibrary()
    {
        EnsureFolderExists(AudioRootFolder);

        AudioLibrary library = AssetDatabase.LoadAssetAtPath<AudioLibrary>(AudioLibraryAssetPath);
        if (library == null)
        {
            library = ScriptableObject.CreateInstance<AudioLibrary>();
            AssetDatabase.CreateAsset(library, AudioLibraryAssetPath);
        }

        SerializedObject libSO = new SerializedObject(library);

        // 2 BGM khớp 100% với MainMenuController & RoomTransitionManager
        (string id, string path, float vol, bool loop)[] bgms =
        {
            ("BGM_MainMenu", $"{PlaceholderFolder}/BGM_MainMenu.wav", 0.7f, true),
            ("BGM_BietThu_BinhThuong", $"{PlaceholderFolder}/BGM_BietThu_BinhThuong.wav", 0.65f, true)
        };

        SerializedProperty bgmProp = libSO.FindProperty("bgmEntries");
        if (bgmProp != null && bgmProp.isArray)
        {
            bgmProp.arraySize = bgms.Length;
            for (int i = 0; i < bgms.Length; i++)
            {
                SerializedProperty elem = bgmProp.GetArrayElementAtIndex(i);
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(bgms[i].path);
                if (clip == null)
                {
                    Debug.LogError($"[AudioSettingsSetupGenerator] Không nạp được BGM AudioClip tại '{bgms[i].path}'.");
                }
                elem.FindPropertyRelative("id").stringValue = bgms[i].id;
                elem.FindPropertyRelative("clip").objectReferenceValue = clip;
                elem.FindPropertyRelative("defaultVolume").floatValue = bgms[i].vol;
                elem.FindPropertyRelative("loop").boolValue = bgms[i].loop;
            }
        }

        // 7 SFX khớp 100% với tất cả móc gọi ở Bước 8.2
        (string id, string path, float vol, float pitchVar)[] sfxs =
        {
            ("SFX_UI_Click", $"{PlaceholderFolder}/SFX_UI_Click.wav", 0.8f, 0.04f),
            ("SFX_Item_Pickup", $"{PlaceholderFolder}/SFX_Item_Pickup.wav", 0.9f, 0.05f),
            ("SFX_Item_Combine", $"{PlaceholderFolder}/SFX_Item_Combine.wav", 0.95f, 0.03f),
            ("SFX_Keypad_Press", $"{PlaceholderFolder}/SFX_Keypad_Press.wav", 0.75f, 0.06f),
            ("SFX_Keypad_Success", $"{PlaceholderFolder}/SFX_Keypad_Success.wav", 0.95f, 0f),
            ("SFX_Keypad_Error", $"{PlaceholderFolder}/SFX_Keypad_Error.wav", 0.9f, 0f),
            ("SFX_Door_Transition", $"{PlaceholderFolder}/SFX_Door_Transition.wav", 0.85f, 0.04f)
        };

        SerializedProperty sfxProp = libSO.FindProperty("sfxEntries");
        if (sfxProp != null && sfxProp.isArray)
        {
            sfxProp.arraySize = sfxs.Length;
            for (int i = 0; i < sfxs.Length; i++)
            {
                SerializedProperty elem = sfxProp.GetArrayElementAtIndex(i);
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(sfxs[i].path);
                if (clip == null)
                {
                    Debug.LogError($"[AudioSettingsSetupGenerator] Không nạp được SFX AudioClip tại '{sfxs[i].path}'.");
                }
                elem.FindPropertyRelative("id").stringValue = sfxs[i].id;
                elem.FindPropertyRelative("clip").objectReferenceValue = clip;
                elem.FindPropertyRelative("defaultVolume").floatValue = sfxs[i].vol;
                elem.FindPropertyRelative("pitchVariance").floatValue = sfxs[i].pitchVar;
            }
        }

        libSO.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(library);
        return library;
    }

    // ================= PHẦN 3: CẤU HÌNH ĐỒNG BỘ CHO MAINMENU & SAMPLESCENE =================

    private static bool ConfigureSceneAudioAndSettings(
        string scenePath,
        bool isMainMenuScene,
        AudioLibrary audioLibrary,
        TMP_FontAsset fallbackFont)
    {
        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        if (!scene.IsValid())
        {
            Debug.LogError($"[AudioSettingsSetupGenerator] Không thể mở Scene '{scenePath}'.");
            return false;
        }

        TMP_FontAsset sceneFont = FindProjectTMPFont(scene) ?? fallbackFont;

        // 3a. Bảo đảm Scene có đúng 1 AudioListener (thêm vào Main Camera nếu thiếu, không tạo trùng)
        EnsureAudioListenerInScene(scene);

        // 3b. Đặt AudioManager dự phòng ở cấp Root của cả 2 Scene:
        // Giúp test trực tiếp SampleScene vẫn có âm thanh; khi đi từ MainMenu sang SampleScene,
        // Singleton DontDestroyOnLoad của AudioManager sẽ tự hủy bản trùng trong Awake()
        EnsureAudioManagerInScene(scene, audioLibrary);

        // 3c. Sinh (hoặc cập nhật) SettingsCanvas ở cấp Root (sortingOrder = 90)
        BuildOrReplaceSettingsCanvas(scene, sceneFont);

        // 3d. Bổ sung nút "CÀI ĐẶT" vào MainMenuController hoặc PauseMenuController (nếu chưa có)
        if (isMainMenuScene)
        {
            EnsureSettingsButtonInMainMenu(sceneFont);
        }
        else
        {
            EnsureSettingsButtonInPauseMenu(sceneFont);
        }

        // 3e. Đánh dấu bẩn và lưu Scene chuẩn GUID
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return true;
    }

    private static void EnsureAudioListenerInScene(Scene scene)
    {
        // Giữ nguyên nếu Scene đã có sẵn AudioListener để tránh lỗi cảnh báo 2 AudioListener trong Console
        AudioListener existing = UnityEngine.Object.FindFirstObjectByType<AudioListener>(FindObjectsInactive.Include);
        if (existing != null)
        {
            existing.enabled = true;
            return;
        }

        // Tìm Main Camera (theo tag MainCamera, tên Main Camera hoặc Camera đầu tiên trong Scene)
        Camera targetCamera = null;
        Camera[] cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i] != null && (cameras[i].CompareTag("MainCamera") || string.Equals(cameras[i].name, "Main Camera", StringComparison.Ordinal)))
            {
                targetCamera = cameras[i];
                break;
            }
        }

        if (targetCamera == null && cameras.Length > 0)
        {
            targetCamera = cameras[0];
        }

        if (targetCamera != null)
        {
            targetCamera.gameObject.AddComponent<AudioListener>();
            EditorUtility.SetDirty(targetCamera.gameObject);
        }
        else
        {
            Debug.LogWarning($"[AudioSettingsSetupGenerator] Scene '{scene.name}' chưa có Main Camera để gắn AudioListener.");
        }
    }

    private static void EnsureAudioManagerInScene(Scene scene, AudioLibrary audioLibrary)
    {
        AudioManager audioManager = UnityEngine.Object.FindFirstObjectByType<AudioManager>(FindObjectsInactive.Include);
        GameObject managerGO;

        if (audioManager == null)
        {
            managerGO = new GameObject(AudioManagerObjectName);
            SceneManager.MoveGameObjectToScene(managerGO, scene);
            audioManager = managerGO.AddComponent<AudioManager>();
        }
        else
        {
            managerGO = audioManager.gameObject;
            if (managerGO.transform.parent != null)
            {
                managerGO.transform.SetParent(null, true);
            }
        }

        AudioSource bgmA = EnsureChildAudioSource(managerGO.transform, "BgmSourceA", loop: true);
        AudioSource bgmB = EnsureChildAudioSource(managerGO.transform, "BgmSourceB", loop: true);
        AudioSource sfx = EnsureChildAudioSource(managerGO.transform, "SfxSource", loop: false);

        // Khớp chính xác 100% tên biến [SerializeField] của AudioManager.cs
        SerializedObject managerSO = new SerializedObject(audioManager);
        AssignObjectReference(managerSO, "audioLibrary", audioLibrary);
        AssignObjectReference(managerSO, "bgmSourceA", bgmA);
        AssignObjectReference(managerSO, "bgmSourceB", bgmB);
        AssignObjectReference(managerSO, "sfxSource", sfx);
        managerSO.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(audioManager);
    }

    private static AudioSource EnsureChildAudioSource(Transform parent, string childName, bool loop)
    {
        Transform existing = parent.Find(childName);
        GameObject childGO = existing != null ? existing.gameObject : new GameObject(childName);
        if (existing == null)
        {
            childGO.transform.SetParent(parent, false);
        }

        AudioSource source = childGO.GetComponent<AudioSource>();
        if (source == null)
        {
            source = childGO.AddComponent<AudioSource>();
        }

        // BẮT BUỘC đặt spatialBlend = 0f (âm thanh 2D), mute = false và volume = 1f để không bị suy giảm theo khoảng cách 3D
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 0f;
        source.mute = false;
        source.volume = 1f;
        return source;
    }

    private static void BuildOrReplaceSettingsCanvas(Scene scene, TMP_FontAsset fontAsset)
    {
        List<GameObject> existing = FindSceneObjectsByName(scene, SettingsCanvasName);
        for (int i = 0; i < existing.Count; i++)
        {
            if (existing[i] != null)
            {
                UnityEngine.Object.DestroyImmediate(existing[i]);
            }
        }

        int uiLayer = GetUILayer();

        // Root SettingsCanvas luôn Active ở cấp Root Scene để Awake() khởi tạo Singleton SettingsUIController.Instance
        GameObject canvasGO = CreateUIObject(SettingsCanvasName, null, uiLayer);
        SceneManager.MoveGameObjectToScene(canvasGO, scene);

        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Thứ tự sortingOrder chuẩn của toàn bộ dự án:
        // InventoryCanvas (10) < KeypadCanvas (60) < ItemInspectCanvas (70) < PauseCanvas (80) < SettingsCanvas (90) < TransitionCanvas (100)
        canvas.sortingOrder = 90;

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();
        SettingsUIController controller = canvasGO.AddComponent<SettingsUIController>();

        // SettingsPanelRoot CHỈ làm container bật/tắt cả bảng (tuyệt đối KHÔNG gắn Image hay Button để tránh uGUI Event Bubbling)
        GameObject panelRoot = CreateUIObject("SettingsPanelRoot", canvasGO.transform, uiLayer);
        StretchFull(panelRoot.GetComponent<RectTransform>());

        // Sibling 1 (nằm lớp dưới): BackdropCloseButton phủ toàn màn hình, nhận click vùng tối bên ngoài để đóng nhanh
        // Tách thành anh em (Sibling) với SettingsWindow để chặn hoàn toàn Event Bubbling từ SettingsWindow lên nút đóng nền
        GameObject backdropGO = CreateUIObject("BackdropCloseButton", panelRoot.transform, uiLayer);
        StretchFull(backdropGO.GetComponent<RectTransform>());
        Image backdropImg = backdropGO.AddComponent<Image>();
        backdropImg.color = BackdropDimColor;
        backdropImg.raycastTarget = true;

        Button backdropCloseButton = backdropGO.AddComponent<Button>();
        backdropCloseButton.targetGraphic = backdropImg;
        backdropCloseButton.transition = Selectable.Transition.None;

        // Sibling 2 (nằm lớp trên, KHÔNG phải con của BackdropCloseButton): SettingsWindow chặn mọi tia Raycast xuyên xuống nền
        GameObject windowGO = CreateUIObject("SettingsWindow", panelRoot.transform, uiLayer);
        RectTransform windowRect = windowGO.GetComponent<RectTransform>();
        windowRect.anchorMin = new Vector2(0.5f, 0.5f);
        windowRect.anchorMax = new Vector2(0.5f, 0.5f);
        windowRect.pivot = new Vector2(0.5f, 0.5f);
        windowRect.anchoredPosition = Vector2.zero;
        windowRect.sizeDelta = new Vector2(960f, 720f);

        Image windowBg = windowGO.AddComponent<Image>();
        windowBg.color = WindowBgColor;
        windowBg.raycastTarget = true;

        Outline windowOutline = windowGO.AddComponent<Outline>();
        windowOutline.effectColor = BorderSubtleColor;
        windowOutline.effectDistance = new Vector2(2f, -2f);

        // Tiêu đề: "CÀI ĐẶT ÂM THANH" (cỡ chữ 38)
        GameObject titleGO = CreateUIObject("TitleText", windowGO.transform, uiLayer);
        RectTransform titleRect = titleGO.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.5f, 1f);
        titleRect.anchorMax = new Vector2(0.5f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -32f);
        titleRect.sizeDelta = new Vector2(840f, 52f);
        CreateTMPText(titleGO, "CÀI ĐẶT ÂM THANH", 38f, FontStyles.Bold, TextAlignmentOptions.Center, PrimaryTextColor, fontAsset);

        // 3 cụm thanh trượt uGUI Slider tách biệt (height = 88 mỗi cụm, cách nhau 24px để không đè chữ)
        CreateSliderRow("MasterSliderRow", windowGO.transform, -114f, "Âm lượng tổng", fontAsset, uiLayer, out Slider masterSlider, out TMP_Text masterValueText);
        CreateSliderRow("BgmSliderRow", windowGO.transform, -226f, "Nhạc nền (BGM)", fontAsset, uiLayer, out Slider bgmSlider, out TMP_Text bgmValueText);
        CreateSliderRow("SfxSliderRow", windowGO.transform, -338f, "Hiệu ứng (SFX)", fontAsset, uiLayer, out Slider sfxSlider, out TMP_Text sfxValueText);

        // 1 uGUI Toggle: "Tắt toàn bộ âm thanh (Mute)"
        Toggle muteToggle = CreateMuteToggleRow("MuteToggleRow", windowGO.transform, -464f, "Tắt toàn bộ âm thanh (Mute)", fontAsset, uiLayer);

        // Hàng nút đáy (HorizontalLayoutGroup): "NGHE THỬ SFX" và "ĐÓNG" (chiều cao >= 72 cho Mobile)
        GameObject actionRow = CreateUIObject("ActionRow", windowGO.transform, uiLayer);
        RectTransform actionRect = actionRow.GetComponent<RectTransform>();
        actionRect.anchorMin = new Vector2(0.5f, 0f);
        actionRect.anchorMax = new Vector2(0.5f, 0f);
        actionRect.pivot = new Vector2(0.5f, 0f);
        actionRect.anchoredPosition = new Vector2(0f, 36f);
        actionRect.sizeDelta = new Vector2(760f, 76f);

        HorizontalLayoutGroup actionLayout = actionRow.AddComponent<HorizontalLayoutGroup>();
        actionLayout.spacing = 28f;
        actionLayout.childAlignment = TextAnchor.MiddleCenter;
        actionLayout.childControlWidth = true;
        actionLayout.childControlHeight = true;
        actionLayout.childForceExpandWidth = true;
        actionLayout.childForceExpandHeight = true;

        Button testSfxButton = CreateStyledButton("TestSfxButton", actionRow.transform, "NGHE THỬ SFX", 25f, TestSfxButtonColor, fontAsset, uiLayer);
        AddPreferredHeight(testSfxButton.gameObject, 72f);

        Button closeButton = CreateStyledButton("CloseButton", actionRow.transform, "ĐÓNG", 25f, CloseButtonColor, fontAsset, uiLayer);
        AddPreferredHeight(closeButton.gameObject, 72f);

        panelRoot.SetActive(false);

        // Nối chính xác 100% toàn bộ [SerializeField] của SettingsUIController.cs
        SerializedObject controllerSO = new SerializedObject(controller);
        AssignObjectReference(controllerSO, "panelRoot", panelRoot);
        AssignObjectReference(controllerSO, "masterSlider", masterSlider);
        AssignObjectReference(controllerSO, "bgmSlider", bgmSlider);
        AssignObjectReference(controllerSO, "sfxSlider", sfxSlider);
        AssignObjectReference(controllerSO, "masterValueText", masterValueText);
        AssignObjectReference(controllerSO, "bgmValueText", bgmValueText);
        AssignObjectReference(controllerSO, "sfxValueText", sfxValueText);
        AssignObjectReference(controllerSO, "muteToggle", muteToggle);
        AssignObjectReference(controllerSO, "testSfxButton", testSfxButton);
        AssignObjectReference(controllerSO, "closeButton", closeButton);
        AssignObjectReference(controllerSO, "backdropCloseButton", backdropCloseButton);
        controllerSO.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(controller);
    }

    private static void EnsureSettingsButtonInMainMenu(TMP_FontAsset fontAsset)
    {
        MainMenuController mainMenu = UnityEngine.Object.FindFirstObjectByType<MainMenuController>(FindObjectsInactive.Include);
        if (mainMenu == null)
        {
            Debug.LogWarning("[AudioSettingsSetupGenerator] Không tìm thấy MainMenuController trong MainMenu.unity.");
            return;
        }

        SerializedObject menuSO = new SerializedObject(mainMenu);
        Button existingSettingsBtn = menuSO.FindProperty("settingsButton")?.objectReferenceValue as Button;
        if (existingSettingsBtn != null)
        {
            return;
        }

        Button quitBtn = menuSO.FindProperty("quitButton")?.objectReferenceValue as Button;
        Button newGameBtn = menuSO.FindProperty("newGameButton")?.objectReferenceValue as Button;
        Transform buttonGroup = quitBtn != null
            ? quitBtn.transform.parent
            : (newGameBtn != null ? newGameBtn.transform.parent : mainMenu.transform);

        int uiLayer = buttonGroup.gameObject.layer;

        // Tái sử dụng nếu nút SettingsButton đã có sẵn trong ButtonGroup nhưng chưa được kéo vào Inspector
        Transform existingChild = buttonGroup.Find("SettingsButton");
        Button settingsButton = existingChild != null ? existingChild.GetComponent<Button>() : null;

        if (settingsButton == null)
        {
            settingsButton = CreateStyledButton("SettingsButton", buttonGroup, "CÀI ĐẶT", 28f, SettingsButtonColor, fontAsset, uiLayer);
            AddPreferredHeight(settingsButton.gameObject, 72f);

            // Đặt nút CÀI ĐẶT nằm ngay trước nút THOÁT GAME cho tự nhiên
            if (quitBtn != null && quitBtn.transform.parent == buttonGroup)
            {
                settingsButton.transform.SetSiblingIndex(quitBtn.transform.GetSiblingIndex());
            }

            // Mở rộng chiều cao ButtonGroup để chứa thoải mái 4 nút (4 x 72 + 3 x 22 = 354px)
            RectTransform groupRect = buttonGroup.GetComponent<RectTransform>();
            if (groupRect != null && groupRect.sizeDelta.y < 362f)
            {
                groupRect.sizeDelta = new Vector2(groupRect.sizeDelta.x, 362f);
            }
        }

        AssignObjectReference(menuSO, "settingsButton", settingsButton);
        menuSO.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(mainMenu);
    }

    private static void EnsureSettingsButtonInPauseMenu(TMP_FontAsset fontAsset)
    {
        PauseMenuController pauseMenu = UnityEngine.Object.FindFirstObjectByType<PauseMenuController>(FindObjectsInactive.Include);
        if (pauseMenu == null)
        {
            Debug.LogWarning("[AudioSettingsSetupGenerator] Không tìm thấy PauseMenuController trong SampleScene.unity.");
            return;
        }

        SerializedObject pauseSO = new SerializedObject(pauseMenu);
        Button existingSettingsBtn = pauseSO.FindProperty("settingsButton")?.objectReferenceValue as Button;
        if (existingSettingsBtn != null)
        {
            return;
        }

        Button mainMenuBtn = pauseSO.FindProperty("mainMenuButton")?.objectReferenceValue as Button;
        Button saveBtn = pauseSO.FindProperty("saveButton")?.objectReferenceValue as Button;
        Transform pauseWindow = mainMenuBtn != null
            ? mainMenuBtn.transform.parent
            : (saveBtn != null ? saveBtn.transform.parent : pauseMenu.transform);

        int uiLayer = pauseWindow.gameObject.layer;

        Transform existingChild = pauseWindow.Find("SettingsButton");
        Button settingsButton = existingChild != null ? existingChild.GetComponent<Button>() : null;

        if (settingsButton == null)
        {
            settingsButton = CreateStyledButton("SettingsButton", pauseWindow, "CÀI ĐẶT", 26f, SettingsButtonColor, fontAsset, uiLayer);
            AddPreferredHeight(settingsButton.gameObject, 72f);

            // Đặt nút CÀI ĐẶT nằm giữa LƯU GAME và VỀ MENU CHÍNH
            if (mainMenuBtn != null && mainMenuBtn.transform.parent == pauseWindow)
            {
                settingsButton.transform.SetSiblingIndex(mainMenuBtn.transform.GetSiblingIndex());
            }

            // Mở rộng chiều cao PauseWindow từ 520 lên 616 để chứa gọn 4 nút + tiêu đề + dòng phản hồi
            RectTransform windowRect = pauseWindow.GetComponent<RectTransform>();
            if (windowRect != null && windowRect.sizeDelta.y < 616f)
            {
                windowRect.sizeDelta = new Vector2(windowRect.sizeDelta.x, 616f);
            }
        }

        AssignObjectReference(pauseSO, "settingsButton", settingsButton);
        pauseSO.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(pauseMenu);
    }

    // ================= HÀM TẠO SLIDER / TOGGLE / BUTTON uGUI CHUẨN MOBILE =================

    private static void CreateSliderRow(
        string rowName,
        Transform parent,
        float anchoredY,
        string labelCaption,
        TMP_FontAsset fontAsset,
        int uiLayer,
        out Slider slider,
        out TMP_Text valueText)
    {
        // Container cụm âm lượng độc lập (height = 88): HeaderRow (top 34) + khoảng hở 18px + Slider (bottom 36)
        GameObject rowGO = CreateUIObject(rowName, parent, uiLayer);
        RectTransform rowRect = rowGO.GetComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0.5f, 1f);
        rowRect.anchorMax = new Vector2(0.5f, 1f);
        rowRect.pivot = new Vector2(0.5f, 1f);
        rowRect.anchoredPosition = new Vector2(0f, anchoredY);
        rowRect.sizeDelta = new Vector2(820f, 88f);

        // Hàng trên (HeaderRow, neo top, height = 34): nhãn trái + text phần trăm phải
        GameObject headerRowGO = CreateUIObject("HeaderRow", rowGO.transform, uiLayer);
        RectTransform headerRect = headerRowGO.GetComponent<RectTransform>();
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(1f, 1f);
        headerRect.pivot = new Vector2(0.5f, 1f);
        headerRect.anchoredPosition = Vector2.zero;
        headerRect.sizeDelta = new Vector2(0f, 34f);

        GameObject labelGO = CreateUIObject("Label", headerRowGO.transform, uiLayer);
        RectTransform labelRect = labelGO.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(0.75f, 1f);
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        CreateTMPText(labelGO, labelCaption, 25f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, SecondaryTextColor, fontAsset);

        GameObject valueGO = CreateUIObject("ValueText", headerRowGO.transform, uiLayer);
        RectTransform valueRect = valueGO.GetComponent<RectTransform>();
        valueRect.anchorMin = new Vector2(0.75f, 0f);
        valueRect.anchorMax = new Vector2(1f, 1f);
        valueRect.offsetMin = Vector2.zero;
        valueRect.offsetMax = Vector2.zero;
        valueText = CreateTMPText(valueGO, "100%", 25f, FontStyles.Bold, TextAlignmentOptions.MidlineRight, PrimaryTextColor, fontAsset);

        // Hàng dưới (Slider, neo bottom, height = 36 -> cách HeaderRow 18px >= 14px)
        GameObject sliderGO = CreateUIObject("Slider", rowGO.transform, uiLayer);
        RectTransform sliderRect = sliderGO.GetComponent<RectTransform>();
        sliderRect.anchorMin = new Vector2(0f, 0f);
        sliderRect.anchorMax = new Vector2(1f, 0f);
        sliderRect.pivot = new Vector2(0.5f, 0f);
        sliderRect.anchoredPosition = Vector2.zero;
        sliderRect.sizeDelta = new Vector2(0f, 36f);

        // Gắn Image trong suốt bật raycastTarget = true trực tiếp trên Slider làm Hitbox rộng 36px
        // để bấm/chạm bất kỳ điểm nào dọc thanh trượt (PC & Mobile) đều nhảy giá trị ngay lập tức
        Image sliderHitboxImg = sliderGO.AddComponent<Image>();
        sliderHitboxImg.color = new Color(0f, 0f, 0f, 0f);
        sliderHitboxImg.raycastTarget = true;

        slider = sliderGO.AddComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.direction = Slider.Direction.LeftToRight;

        // Background: thanh rãnh tối căn giữa dọc, height = 10, bật raycastTarget = true
        GameObject bgGO = CreateUIObject("Background", sliderGO.transform, uiLayer);
        RectTransform bgRect = bgGO.GetComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0f, 0.5f);
        bgRect.anchorMax = new Vector2(1f, 0.5f);
        bgRect.pivot = new Vector2(0.5f, 0.5f);
        bgRect.anchoredPosition = Vector2.zero;
        bgRect.sizeDelta = new Vector2(0f, 10f);
        Image bgImg = bgGO.AddComponent<Image>();
        bgImg.color = TrackBgColor;
        bgImg.raycastTarget = true;

        // Fill Area (height = 10 căn giữa dọc) -> Fill (stretch 0..1 bên trong Fill Area để luôn giữ đúng chiều cao 10)
        GameObject fillAreaGO = CreateUIObject("Fill Area", sliderGO.transform, uiLayer);
        RectTransform fillAreaRect = fillAreaGO.GetComponent<RectTransform>();
        fillAreaRect.anchorMin = new Vector2(0f, 0.5f);
        fillAreaRect.anchorMax = new Vector2(1f, 0.5f);
        fillAreaRect.pivot = new Vector2(0.5f, 0.5f);
        fillAreaRect.offsetMin = new Vector2(0f, -5f);
        fillAreaRect.offsetMax = new Vector2(0f, 5f);

        GameObject fillGO = CreateUIObject("Fill", fillAreaGO.transform, uiLayer);
        RectTransform fillRect = fillGO.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        Image fillImg = fillGO.AddComponent<Image>();
        fillImg.color = FillAccentColor;
        fillImg.raycastTarget = false;

        // Handle Slide Area: chừa lề 14px hai đầu và đặt height = 0 ở trục giữa dọc (anchorY = 0.5..0.5)
        // Edge Case Unity Slider: Slider.UpdateVisuals() tự ghi đè anchorY của Handle thành 0..1;
        // nhờ Handle Slide Area có height = 0 nên Handle giữ cố định sizeDelta = (28, 28), TUYỆT ĐỐI không kéo giãn dọc đè chữ 100%
        GameObject handleAreaGO = CreateUIObject("Handle Slide Area", sliderGO.transform, uiLayer);
        RectTransform handleAreaRect = handleAreaGO.GetComponent<RectTransform>();
        handleAreaRect.anchorMin = new Vector2(0f, 0.5f);
        handleAreaRect.anchorMax = new Vector2(1f, 0.5f);
        handleAreaRect.pivot = new Vector2(0.5f, 0.5f);
        handleAreaRect.offsetMin = new Vector2(14f, 0f);
        handleAreaRect.offsetMax = new Vector2(-14f, 0f);

        GameObject handleGO = CreateUIObject("Handle", handleAreaGO.transform, uiLayer);
        RectTransform handleRect = handleGO.GetComponent<RectTransform>();
        handleRect.anchorMin = new Vector2(0f, 0.5f);
        handleRect.anchorMax = new Vector2(0f, 0.5f);
        handleRect.pivot = new Vector2(0.5f, 0.5f);
        handleRect.anchoredPosition = Vector2.zero;
        handleRect.sizeDelta = new Vector2(28f, 28f);
        Image handleImg = handleGO.AddComponent<Image>();
        handleImg.color = HandleColor;
        handleImg.raycastTarget = true;

        Outline handleOutline = handleGO.AddComponent<Outline>();
        handleOutline.effectColor = FillAccentColor;
        handleOutline.effectDistance = new Vector2(1.5f, -1.5f);

        slider.fillRect = fillRect;
        slider.handleRect = handleRect;
        slider.targetGraphic = handleImg;
        slider.SetValueWithoutNotify(1f);
    }

    private static Toggle CreateMuteToggleRow(
        string rowName,
        Transform parent,
        float anchoredY,
        string caption,
        TMP_FontAsset fontAsset,
        int uiLayer)
    {
        GameObject toggleGO = CreateUIObject(rowName, parent, uiLayer);
        RectTransform toggleRect = toggleGO.GetComponent<RectTransform>();
        toggleRect.anchorMin = new Vector2(0.5f, 1f);
        toggleRect.anchorMax = new Vector2(0.5f, 1f);
        toggleRect.pivot = new Vector2(0.5f, 1f);
        toggleRect.anchoredPosition = new Vector2(0f, anchoredY);
        toggleRect.sizeDelta = new Vector2(820f, 62f);

        Toggle toggle = toggleGO.AddComponent<Toggle>();

        // Background ô vuông tích chọn (44x44 dễ bấm cảm ứng)
        GameObject bgGO = CreateUIObject("Background", toggleGO.transform, uiLayer);
        RectTransform bgRect = bgGO.GetComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0f, 0.5f);
        bgRect.anchorMax = new Vector2(0f, 0.5f);
        bgRect.pivot = new Vector2(0f, 0.5f);
        bgRect.anchoredPosition = Vector2.zero;
        bgRect.sizeDelta = new Vector2(44f, 44f);
        Image bgImg = bgGO.AddComponent<Image>();
        bgImg.color = TrackBgColor;
        bgImg.raycastTarget = true;

        Outline bgOutline = bgGO.AddComponent<Outline>();
        bgOutline.effectColor = BorderSubtleColor;
        bgOutline.effectDistance = new Vector2(1.5f, -1.5f);

        // Checkmark bên trong ô tích chọn
        GameObject checkGO = CreateUIObject("Checkmark", bgGO.transform, uiLayer);
        RectTransform checkRect = checkGO.GetComponent<RectTransform>();
        checkRect.anchorMin = new Vector2(0.5f, 0.5f);
        checkRect.anchorMax = new Vector2(0.5f, 0.5f);
        checkRect.pivot = new Vector2(0.5f, 0.5f);
        checkRect.sizeDelta = new Vector2(28f, 28f);
        Image checkImg = checkGO.AddComponent<Image>();
        checkImg.color = ToggleCheckColor;
        checkImg.raycastTarget = false;

        // Nhãn chữ của Toggle
        GameObject labelGO = CreateUIObject("Label", toggleGO.transform, uiLayer);
        RectTransform labelRect = labelGO.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(62f, 0f);
        labelRect.offsetMax = Vector2.zero;
        CreateTMPText(labelGO, caption, 26f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, PrimaryTextColor, fontAsset);

        toggle.targetGraphic = bgImg;
        toggle.graphic = checkImg;
        toggle.SetIsOnWithoutNotify(false);
        return toggle;
    }

    // ================= HÀM TIỆN ÍCH CHUNG =================

    private static Button CreateStyledButton(
        string buttonName,
        Transform parent,
        string labelText,
        float fontSize,
        Color bgColor,
        TMP_FontAsset fontAsset,
        int uiLayer)
    {
        GameObject btnGO = CreateUIObject(buttonName, parent, uiLayer);

        Image btnImg = btnGO.AddComponent<Image>();
        btnImg.color = bgColor;
        btnImg.raycastTarget = true;

        Outline outline = btnGO.AddComponent<Outline>();
        outline.effectColor = BorderSubtleColor;
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        Button button = btnGO.AddComponent<Button>();
        button.targetGraphic = btnImg;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.12f, 1.14f, 1.18f, 1f);
        colors.pressedColor = new Color(0.84f, 0.86f, 0.90f, 1f);
        colors.disabledColor = new Color(0.55f, 0.58f, 0.64f, 0.42f);
        button.colors = colors;

        GameObject labelGO = CreateUIObject("Label", btnGO.transform, uiLayer);
        StretchFull(labelGO.GetComponent<RectTransform>());
        CreateTMPText(labelGO, labelText, fontSize, FontStyles.Bold, TextAlignmentOptions.Center, Color.white, fontAsset);

        return button;
    }

    private static void AddPreferredHeight(GameObject target, float height)
    {
        LayoutElement layoutElement = target.AddComponent<LayoutElement>();
        layoutElement.minHeight = height;
        layoutElement.preferredHeight = height;
    }

    private static TMP_FontAsset FindProjectTMPFont(Scene activeScene)
    {
        if (activeScene.IsValid())
        {
            string[] priorityCanvases = { "DialogueCanvas", "InventoryCanvas", "PauseCanvas", "MainMenuCanvas", "KeypadCanvas" };
            for (int i = 0; i < priorityCanvases.Length; i++)
            {
                List<GameObject> canvases = FindSceneObjectsByName(activeScene, priorityCanvases[i]);
                for (int j = 0; j < canvases.Count; j++)
                {
                    TMP_FontAsset font = ExtractFirstFont(canvases[j]);
                    if (font != null) return font;
                }
            }

            GameObject[] roots = activeScene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                TMP_FontAsset font = ExtractFirstFont(roots[i]);
                if (font != null) return font;
            }
        }

        string[] guids = AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { "Assets" });
        TMP_FontAsset fallbackAsset = null;
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            TMP_FontAsset loaded = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (loaded == null) continue;

            if (fallbackAsset == null) fallbackAsset = loaded;
            if (path.IndexOf("LiberationSans", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return loaded;
            }
        }

        return fallbackAsset != null ? fallbackAsset : TMP_Settings.defaultFontAsset;
    }

    private static TMP_FontAsset ExtractFirstFont(GameObject root)
    {
        if (root == null) return null;

        TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i] != null && texts[i].font != null)
            {
                return texts[i].font;
            }
        }

        return null;
    }

    private static GameObject CreateUIObject(string name, Transform parent, int uiLayer)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = uiLayer;
        if (parent != null)
        {
            go.transform.SetParent(parent, false);
        }

        return go;
    }

    private static TextMeshProUGUI CreateTMPText(
        GameObject target,
        string content,
        float fontSize,
        FontStyles fontStyle,
        TextAlignmentOptions alignment,
        Color color,
        TMP_FontAsset fontAsset)
    {
        TextMeshProUGUI tmp = target.AddComponent<TextMeshProUGUI>();
        if (fontAsset != null)
        {
            tmp.font = fontAsset;
        }

        tmp.text = content;
        tmp.fontSize = fontSize;
        tmp.fontStyle = fontStyle;
        tmp.alignment = alignment;
        tmp.color = color;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void StretchFull(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
    }

    private static void EnsureFolderExists(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }

        string[] parts = folderPath.Split('/');
        string currentPath = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string nextPath = $"{currentPath}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(nextPath))
            {
                AssetDatabase.CreateFolder(currentPath, parts[i]);
            }

            currentPath = nextPath;
        }
    }

    private static string GetFullProjectPath(string assetPath)
    {
        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        return Path.Combine(projectRoot ?? string.Empty, assetPath);
    }

    private static List<GameObject> FindSceneObjectsByName(Scene scene, string targetName)
    {
        List<GameObject> matches = new List<GameObject>();
        GameObject[] roots = scene.GetRootGameObjects();

        for (int i = 0; i < roots.Length; i++)
        {
            Transform[] children = roots[i].GetComponentsInChildren<Transform>(true);
            for (int j = 0; j < children.Length; j++)
            {
                if (string.Equals(children[j].name, targetName, StringComparison.Ordinal))
                {
                    matches.Add(children[j].gameObject);
                }
            }
        }

        return matches;
    }

    private static void AssignObjectReference(SerializedObject serializedObject, string propertyName, UnityEngine.Object value)
    {
        SerializedProperty prop = serializedObject.FindProperty(propertyName);
        if (prop == null)
        {
            Debug.LogError($"[AudioSettingsSetupGenerator] Không tìm thấy SerializedProperty '{propertyName}' trên '{serializedObject.targetObject.GetType().Name}'.");
            return;
        }

        prop.objectReferenceValue = value;
    }

    private static int GetUILayer()
    {
        int layer = LayerMask.NameToLayer("UI");
        return layer >= 0 ? layer : 5;
    }
}
