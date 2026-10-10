using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectCR
{
    /// <summary>
    /// 공용 옵션창(Options.uxml). 메인 메뉴와 게임 일시정지 메뉴가 같은 UXML·같은 설정(<see cref="GameSettings"/>)을 쓴다.
    /// 위에 [사운드] [화면] 탭을 가로로, 고른 탭의 항목만 아래에 세로로. 바꾸면 바로 적용·저장한다(적용·취소 버튼 없음).
    /// 닫기(X 또는 Esc)는 <see cref="Closed"/>로 알리고, 연 쪽이 원래 화면으로 돌아간다(게임을 다시 돌리지 않는다).
    /// 이벤트는 처음 한 번만 등록한다(여러 번 열어도 중복 없음).
    /// </summary>
    public sealed class OptionsView
    {
        private const string ActiveTabClass = "options-tab--active";

        private readonly VisualElement panel;
        private readonly Button soundTab;
        private readonly Button screenTab;
        private readonly VisualElement soundPage;
        private readonly VisualElement screenPage;
        private readonly Slider master;
        private readonly Slider bgm;
        private readonly Slider sfx;
        private readonly Label masterValue;
        private readonly Label bgmValue;
        private readonly Label sfxValue;
        private readonly DropdownField screenMode;
        private readonly DropdownField resolution;
        private readonly DropdownField quality;
        private readonly DropdownField frameLimit;
        private List<Vector2Int> resolutions = new List<Vector2Int>();
        private bool refreshing;

        public event Action Closed;

        public bool IsOpen { get; private set; }

        /// <summary>root 아래의 options-panel(Options.uxml 인스턴스)을 찾아 묶는다. 없으면 IsValid false.</summary>
        public OptionsView(VisualElement root)
        {
            panel = root.Q("options-panel");
            if (panel == null)
            {
                return;
            }

            soundTab = panel.Q<Button>("options-tab-sound");
            screenTab = panel.Q<Button>("options-tab-screen");
            soundPage = panel.Q("options-sound");
            screenPage = panel.Q("options-screen");
            master = panel.Q<Slider>("options-master");
            bgm = panel.Q<Slider>("options-bgm");
            sfx = panel.Q<Slider>("options-sfx");
            masterValue = panel.Q<Label>("options-master-value");
            bgmValue = panel.Q<Label>("options-bgm-value");
            sfxValue = panel.Q<Label>("options-sfx-value");
            screenMode = panel.Q<DropdownField>("options-screen-mode");
            resolution = panel.Q<DropdownField>("options-resolution");
            quality = panel.Q<DropdownField>("options-quality");
            frameLimit = panel.Q<DropdownField>("options-frame-limit");

            panel.Q<Button>("options-close").clicked += Close;
            soundTab.clicked += () => ShowTab(false);
            screenTab.clicked += () => ShowTab(true);

            master.RegisterValueChangedCallback(e => OnVolume(e.newValue, GameSettings.SetMasterVolume, masterValue));
            bgm.RegisterValueChangedCallback(e => OnVolume(e.newValue, GameSettings.SetBgmVolume, bgmValue));
            sfx.RegisterValueChangedCallback(e => OnVolume(e.newValue, GameSettings.SetSfxVolume, sfxValue));

            screenMode.choices = new List<string> { "전체화면", "창 모드" };
            screenMode.RegisterValueChangedCallback(e =>
            {
                if (!refreshing)
                {
                    GameSettings.SetFullscreen(screenMode.index == 0);
                }
            });

            resolution.RegisterValueChangedCallback(e =>
            {
                if (refreshing || resolution.index < 0 || resolution.index >= resolutions.Count)
                {
                    return;
                }

                // 지원되지 않으면 이전 해상도로 표시를 되돌린다.
                if (!GameSettings.SetResolution(resolutions[resolution.index]))
                {
                    Refresh();
                }
            });

            quality.choices = new List<string>(QualitySettings.names);
            quality.RegisterValueChangedCallback(e =>
            {
                if (!refreshing)
                {
                    GameSettings.SetQuality(quality.index);
                }
            });

            var limits = new List<string>();
            foreach (int limit in GameSettings.FrameLimits)
            {
                limits.Add(limit > 0 ? $"{limit} FPS" : "제한 없음");
            }

            frameLimit.choices = limits;
            frameLimit.RegisterValueChangedCallback(e =>
            {
                if (!refreshing && frameLimit.index >= 0)
                {
                    GameSettings.SetFrameLimit(GameSettings.FrameLimits[frameLimit.index]);
                }
            });

            panel.style.display = DisplayStyle.None;
        }

        public bool IsValid => panel != null;

        public void Open()
        {
            if (panel == null)
            {
                return;
            }

            IsOpen = true;
            Refresh();
            ShowTab(false);
            panel.style.display = DisplayStyle.Flex;
        }

        public void Close()
        {
            if (panel == null || !IsOpen)
            {
                return;
            }

            IsOpen = false;
            panel.style.display = DisplayStyle.None;
            Closed?.Invoke();
        }

        private void ShowTab(bool screen)
        {
            soundPage.style.display = screen ? DisplayStyle.None : DisplayStyle.Flex;
            screenPage.style.display = screen ? DisplayStyle.Flex : DisplayStyle.None;
            soundTab.EnableInClassList(ActiveTabClass, !screen);
            screenTab.EnableInClassList(ActiveTabClass, screen);
        }

        /// <summary>저장된 설정을 화면에 맞춘다(값을 다시 쓰지 않게 refreshing 동안 콜백을 무시).</summary>
        private void Refresh()
        {
            refreshing = true;
            SetSlider(master, masterValue, GameSettings.MasterVolume);
            SetSlider(bgm, bgmValue, GameSettings.BgmVolume);
            SetSlider(sfx, sfxValue, GameSettings.SfxVolume);

            screenMode.index = GameSettings.Fullscreen ? 0 : 1;

            resolutions = GameSettings.SupportedResolutions();
            var names = new List<string>();
            foreach (Vector2Int size in resolutions)
            {
                names.Add($"{size.x}×{size.y}");
            }

            resolution.choices = names;
            resolution.index = Mathf.Max(0, resolutions.IndexOf(GameSettings.Resolution));

            quality.index = Mathf.Clamp(GameSettings.Quality, 0, quality.choices.Count - 1);
            frameLimit.index = Mathf.Max(0, Array.IndexOf(GameSettings.FrameLimits, GameSettings.FrameLimit));
            refreshing = false;
        }

        private void OnVolume(float percent, Action<float> apply, Label value)
        {
            value.text = $"{Mathf.RoundToInt(percent)}%";
            if (!refreshing)
            {
                apply(percent / 100f);
            }
        }

        private static void SetSlider(Slider slider, Label value, float volume)
        {
            float percent = Mathf.Round(volume * 100f);
            slider.SetValueWithoutNotify(percent);
            value.text = $"{percent:0}%";
        }
    }
}
