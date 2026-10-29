using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace LandFlipper.Core
{
    /// <summary>Oyuncunun yerel ayarları (ağ üzerinden paylaşılmaz).</summary>
    [Serializable]
    public class GameSettings
    {
        // Görüntü
        public int displayMode = 1;          // 0 Tam ekran, 1 Pencereli tam ekran, 2 Pencere
        public int resolutionWidth;          // 0 = ekranın kendi çözünürlüğü
        public int resolutionHeight;
        public int qualityLevel = -1;        // -1 = proje varsayılanı
        public bool vsync = true;
        public int frameLimitIndex;          // SettingsService.FrameLimits
        public float fov = 75f;
        public float brightness;             // -1..1
        public bool shadows = true;
        public bool postProcessing = true;
        public int antiAliasing = 2;         // 0 Kapalı, 1 FXAA, 2 SMAA

        // Kontroller
        public float mouseSensitivity = 0.12f;
        public float gamepadSensitivity = 180f;
        public bool invertY;

        // Ses
        public float masterVolume = 1f;
        public float musicVolume = 0.8f;
        public float sfxVolume = 1f;

        public GameSettings Clone() => (GameSettings)MemberwiseClone();
    }

    /// <summary>
    /// Ayarları yükler, kaydeder ve oyuna uygular. Kamera/ışık/parlaklık her sahne yüklenince
    /// yeniden uygulanır; oyuncu kamerası doğduğunda FirstPersonController da ApplyToCamera çağırır.
    /// </summary>
    public static class SettingsService
    {
        const string PrefsKey = "LF_Settings";
        const float BasePostExposure = 0.05f; // LF_StylizedVolume'daki değer

        public static readonly int[] FrameLimits = { 0, 30, 60, 120, 144 };
        public static readonly string[] FrameLimitNames = { "Sınırsız", "30", "60", "120", "144" };

        public static GameSettings Current { get; private set; } = new GameSettings();

        /// <summary>Ayar değişince (canlı) tetiklenir.</summary>
        public static event Action Changed;

        static Volume brightnessVolume;
        static ColorAdjustments brightnessOverride;
        static bool initialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Initialize()
        {
            if (initialized) return;
            initialized = true;
            Load();
            LFInput.EnsureCreated();
            SceneManager.sceneLoaded += (_, __) => ApplyAll();
            ApplyAll();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            initialized = false;
            brightnessVolume = null;
            brightnessOverride = null;
            Changed = null;
        }

        public static void Load()
        {
            string json = PlayerPrefs.GetString(PrefsKey, string.Empty);
            Current = new GameSettings();
            if (string.IsNullOrEmpty(json)) return;
            try { JsonUtility.FromJsonOverwrite(json, Current); }
            catch (Exception e) { Debug.LogWarning($"[LandFlipper] Ayarlar okunamadı, varsayılanlar kullanılıyor: {e.Message}"); }
        }

        public static void Save()
        {
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(Current));
            PlayerPrefs.Save();
        }

        /// <summary>Bir ayarı değiştir, hemen uygula ve kaydet.</summary>
        public static void Modify(Action<GameSettings> change)
        {
            change(Current);
            ApplyAll();
            Save();
            Changed?.Invoke();
        }

        public static void ResetToDefaults()
        {
            Current = new GameSettings();
            ApplyAll();
            Save();
            Changed?.Invoke();
        }

        // ------------------------------------------------------------ uygulama

        public static void ApplyAll()
        {
            var s = Current;

            // Ekran
            var mode = s.displayMode switch
            {
                0 => FullScreenMode.ExclusiveFullScreen,
                2 => FullScreenMode.Windowed,
                _ => FullScreenMode.FullScreenWindow
            };
            int width = s.resolutionWidth > 0 ? s.resolutionWidth : Screen.currentResolution.width;
            int height = s.resolutionHeight > 0 ? s.resolutionHeight : Screen.currentResolution.height;
            if (!Application.isEditor && (Screen.width != width || Screen.height != height || Screen.fullScreenMode != mode))
                Screen.SetResolution(width, height, mode);

            if (s.qualityLevel >= 0 && s.qualityLevel < QualitySettings.names.Length && QualitySettings.GetQualityLevel() != s.qualityLevel)
                QualitySettings.SetQualityLevel(s.qualityLevel, true);

            QualitySettings.vSyncCount = s.vsync ? 1 : 0;
            int limit = FrameLimits[Mathf.Clamp(s.frameLimitIndex, 0, FrameLimits.Length - 1)];
            Application.targetFrameRate = s.vsync || limit == 0 ? -1 : limit;

            // Ses
            AudioListener.volume = Mathf.Clamp01(s.masterVolume);

            // Işık ve kameralar
            if (RenderSettings.sun != null)
                RenderSettings.sun.shadows = s.shadows ? LightShadows.Soft : LightShadows.None;

            foreach (var cam in Camera.allCameras)
                ApplyToCamera(cam, false);

            ApplyBrightness(s.brightness);
        }

        /// <summary>Post-processing, kenar yumuşatma ve (istenirse) görüş alanı.</summary>
        public static void ApplyToCamera(Camera cam, bool applyFov)
        {
            if (cam == null) return;
            var data = cam.GetComponent<UniversalAdditionalCameraData>();
            if (data != null)
            {
                data.renderPostProcessing = Current.postProcessing;
                data.antialiasing = Current.antiAliasing switch
                {
                    0 => AntialiasingMode.None,
                    1 => AntialiasingMode.FastApproximateAntialiasing,
                    _ => AntialiasingMode.SubpixelMorphologicalAntiAliasing
                };
            }
            if (applyFov) cam.fieldOfView = Current.fov;
        }

        static void ApplyBrightness(float brightness)
        {
            if (brightnessVolume == null)
            {
                var go = new GameObject("SettingsBrightnessVolume");
                UnityEngine.Object.DontDestroyOnLoad(go);
                brightnessVolume = go.AddComponent<Volume>();
                brightnessVolume.isGlobal = true;
                brightnessVolume.priority = 10f;
                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                brightnessOverride = profile.Add<ColorAdjustments>(false);
                brightnessVolume.sharedProfile = profile;
            }

            // Sadece pozlama ezilir; diğer renk ayarları sahne profilinden gelir.
            brightnessOverride.postExposure.Override(BasePostExposure + brightness);
        }

        // ------------------------------------------------------------ çözünürlük listesi

        public static List<Resolution> AvailableResolutions()
        {
            var unique = new List<Resolution>();
            var seen = new HashSet<long>();
            var all = Screen.resolutions;
            for (int i = all.Length - 1; i >= 0; i--)
            {
                long key = ((long)all[i].width << 32) | (uint)all[i].height;
                if (seen.Add(key)) unique.Add(all[i]);
            }
            if (unique.Count == 0) unique.Add(Screen.currentResolution);
            return unique;
        }
    }
}
