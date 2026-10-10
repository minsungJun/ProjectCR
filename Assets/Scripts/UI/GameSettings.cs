using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace ProjectCR
{
    /// <summary>
    /// 사운드·화면 설정 하나(메인 메뉴와 게임이 같이 쓴다). 바꾸면 바로 적용하고 PlayerPrefs에 저장, 다음 실행 때 복원한다.
    ///
    /// 사운드: 0~1. AudioMixer(Resources/Audio/MainMixer)의 노출 파라미터 MasterVolume·BGMVolume·SFXVolume(dB)에 넣는다.
    /// 전체 음량은 Master 그룹이라 BGM·SFX 둘 다에 걸린다. 소리를 낼 AudioSource의 Output을 BGM/SFX 그룹으로 두면 된다.
    /// 화면: 화면 모드(전체화면 창/창 모드), 해상도(실행 환경이 지원하는 값, 같은 크기는 하나로), 품질 단계(QualitySettings),
    /// 프레임 제한(30/60/120/제한 없음 — 제한을 쓰면 수직 동기화를 끈다. 수직 동기화가 켜져 있으면 targetFrameRate가 무시되기 때문).
    /// 품질 단계를 바꾸면 그 단계의 수직 동기화 값이 돌아오므로 프레임 제한을 다시 건다.
    /// </summary>
    public static class GameSettings
    {
        public const string MixerResourcePath = "Audio/MainMixer";
        public const string MasterParameter = "MasterVolume";
        public const string BgmParameter = "BGMVolume";
        public const string SfxParameter = "SFXVolume";

        /// <summary>프레임 제한 선택지. 0 = 제한 없음.</summary>
        public static readonly int[] FrameLimits = { 30, 60, 120, 0 };

        private const string Prefix = "ProjectCR.Settings.";
        private const float MinDecibel = -80f;

        private static AudioMixer mixer;
        private static bool loaded;
        private static float master = 0.8f;
        private static float bgm = 0.6f;
        private static float sfx = 0.8f;
        private static bool fullscreen = true;
        private static int width;
        private static int height;
        private static int quality = -1;
        private static int frameLimit = 60;

        public static float MasterVolume { get { Load(); return master; } }
        public static float BgmVolume { get { Load(); return bgm; } }
        public static float SfxVolume { get { Load(); return sfx; } }
        public static bool Fullscreen { get { Load(); return fullscreen; } }
        public static int Quality { get { Load(); return quality; } }
        public static int FrameLimit { get { Load(); return frameLimit; } }

        public static Vector2Int Resolution
        {
            get
            {
                Load();
                // 저장 전에는 모니터 해상도(전체화면 기준).
                return width > 0 ? new Vector2Int(width, height) : new Vector2Int(Screen.currentResolution.width, Screen.currentResolution.height);
            }
        }

        /// <summary>저장된 값을 처음 한 번 읽는다.</summary>
        private static void Load()
        {
            if (!loaded)
            {
                loaded = true;
                master = PlayerPrefs.GetFloat(Prefix + "Master", master);
                bgm = PlayerPrefs.GetFloat(Prefix + "Bgm", bgm);
                sfx = PlayerPrefs.GetFloat(Prefix + "Sfx", sfx);
                fullscreen = PlayerPrefs.GetInt(Prefix + "Fullscreen", Screen.fullScreen ? 1 : 0) == 1;
                width = PlayerPrefs.GetInt(Prefix + "Width", 0);
                height = PlayerPrefs.GetInt(Prefix + "Height", 0);
                quality = PlayerPrefs.GetInt(Prefix + "Quality", QualitySettings.GetQualityLevel());
                frameLimit = PlayerPrefs.GetInt(Prefix + "FrameLimit", frameLimit);
                if (quality < 0 || quality >= QualitySettings.names.Length)
                {
                    quality = QualitySettings.GetQualityLevel();
                }
            }
        }

        /// <summary>
        /// 실행 시작(첫 씬을 불러온 뒤) 저장된 설정을 적용한다. AudioMixer.SetFloat는 첫 프레임 전에는 먹지 않아서
        /// 사운드는 숨은 오브젝트의 Start(첫 프레임)에서 넣는다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ApplySavedOnStart()
        {
            loaded = false;
            mixer = null;
            Load();
            ApplyQuality();
            ApplyScreen();

            var applier = new GameObject("GameSettingsApplier") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(applier);
            applier.AddComponent<GameSettingsApplier>();
        }

        // ---------- 사운드 ----------

        public static void SetMasterVolume(float value) => SetVolume(ref master, value, "Master");
        public static void SetBgmVolume(float value) => SetVolume(ref bgm, value, "Bgm");
        public static void SetSfxVolume(float value) => SetVolume(ref sfx, value, "Sfx");

        private static void SetVolume(ref float field, float value, string key)
        {
            Load();
            field = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(Prefix + key, field);
            PlayerPrefs.Save();
            ApplyAudio();
        }

        /// <summary>0~1 → dB. 0은 -80dB(무음).</summary>
        public static float ToDecibel(float volume) => volume <= 0.0001f ? MinDecibel : Mathf.Max(MinDecibel, 20f * Mathf.Log10(volume));

        internal static void ApplyAudio()
        {
            if (mixer == null)
            {
                mixer = Resources.Load<AudioMixer>(MixerResourcePath);
            }

            if (mixer == null)
            {
                return;
            }

            mixer.SetFloat(MasterParameter, ToDecibel(master));
            mixer.SetFloat(BgmParameter, ToDecibel(bgm));
            mixer.SetFloat(SfxParameter, ToDecibel(sfx));
        }

        // ---------- 화면 ----------

        /// <summary>지원 해상도(가로×세로, 같은 크기는 하나로, 큰 것부터).</summary>
        public static List<Vector2Int> SupportedResolutions()
        {
            var list = new List<Vector2Int>();
            foreach (UnityEngine.Resolution r in Screen.resolutions)
            {
                var size = new Vector2Int(r.width, r.height);
                if (!list.Contains(size))
                {
                    list.Add(size);
                }
            }

            Vector2Int current = Resolution;
            if (!list.Contains(current) && current.x > 0)
            {
                list.Add(current);
            }

            list.Sort((a, b) => a.x != b.x ? b.x.CompareTo(a.x) : b.y.CompareTo(a.y));
            return list;
        }

        public static void SetFullscreen(bool value)
        {
            Load();
            fullscreen = value;
            PlayerPrefs.SetInt(Prefix + "Fullscreen", value ? 1 : 0);
            PlayerPrefs.Save();
            ApplyScreen();
        }

        /// <summary>지원 목록에 있는 해상도만 받는다. 아니면 false(이전 설정 그대로).</summary>
        public static bool SetResolution(Vector2Int size)
        {
            Load();
            if (!SupportedResolutions().Contains(size))
            {
                return false;
            }

            width = size.x;
            height = size.y;
            PlayerPrefs.SetInt(Prefix + "Width", width);
            PlayerPrefs.SetInt(Prefix + "Height", height);
            PlayerPrefs.Save();
            ApplyScreen();
            return true;
        }

        public static void SetQuality(int level)
        {
            Load();
            if (level < 0 || level >= QualitySettings.names.Length)
            {
                return;
            }

            quality = level;
            PlayerPrefs.SetInt(Prefix + "Quality", quality);
            PlayerPrefs.Save();
            ApplyQuality();
        }

        public static void SetFrameLimit(int limit)
        {
            Load();
            frameLimit = Mathf.Max(0, limit);
            PlayerPrefs.SetInt(Prefix + "FrameLimit", frameLimit);
            PlayerPrefs.Save();
            ApplyFrameLimit();
        }

        private static void ApplyQuality()
        {
            if (quality >= 0 && quality < QualitySettings.names.Length && QualitySettings.GetQualityLevel() != quality)
            {
                QualitySettings.SetQualityLevel(quality, true);
            }

            ApplyFrameLimit();
        }

        private static void ApplyFrameLimit()
        {
            // 제한을 쓰면 수직 동기화를 끈다(켜져 있으면 targetFrameRate가 무시된다). 제한 없음이면 둘 다 끈다.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = frameLimit > 0 ? frameLimit : -1;
        }

        private static void ApplyScreen()
        {
            FullScreenMode mode = fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            Vector2Int size = width > 0 ? new Vector2Int(width, height) : new Vector2Int(Screen.currentResolution.width, Screen.currentResolution.height);
            if (size.x <= 0 || size.y <= 0)
            {
                return;
            }

            try
            {
                Screen.SetResolution(size.x, size.y, mode);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[GameSettings] 화면 설정 실패, 이전 설정 유지: {exception.Message}");
            }
        }
    }
}
