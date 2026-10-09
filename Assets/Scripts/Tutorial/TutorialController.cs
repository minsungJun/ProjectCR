using System.Collections;
using UnityEngine;

namespace ProjectCR
{
    /// <summary>
    /// 딜러 튜토리얼 진행. 언제 보여 줄지는 RouletteGame이 기존 연출이 끝난 자리에서 부른다(코루틴을 yield):
    /// - 새 게임 시작 → <see cref="BeginRun"/>, 시작 카메라가 멈춘 뒤 <see cref="PlayIntro"/>
    /// - 첫 라운드 통과, 로봇이 음료를 내려놓은 뒤 → <see cref="PlayDrink"/>
    /// - 음료를 고르고 로봇이 떠나 베팅 화면으로 돌아온 뒤, 상점을 열 수 있으면 → <see cref="PlayService"/>(태블릿 [서비스]가 깜빡인다)
    /// - 게임 오버·게임 클리어 판정 뒤, 결과 창보다 먼저 → <see cref="PlayEnding"/>
    /// 안내마다 그 게임에서 한 번만이고, 튜토리얼 대상 게임(최초로 시작한 게임)에서만 나온다(<see cref="TutorialProgress"/>).
    /// 대화창이 열려 있는 동안 <see cref="IsRunning"/>이고, RouletteGame은 칩·베팅·회전·서비스·음료·시점 전환을 막는다.
    /// 돈·회전·음료·납부는 건드리지 않는다.
    /// 페이지마다 정해진 강조(<see cref="TutorialHighlight"/>)를 <see cref="HighlightChanged"/>로 알리고, 대화가 끝나면 None을 보낸다.
    /// </summary>
    public class TutorialController : MonoBehaviour
    {
        [SerializeField] private TutorialDialogueView view;
        [SerializeField] private TutorialDialogueData data;

        [Header("개발용")]
        [Tooltip("켜면 새 게임마다 튜토리얼을 보여 준다(저장된 '봤음'과 상관없이). 시험용.")]
        [SerializeField] private bool alwaysShow;

        private readonly ITutorialStore store = new PlayerPrefsTutorialStore();
        private TutorialProgress progress = TutorialProgress.None;
        private TabletPanel highlightedTablet;
        private int sessions;

        /// <summary>지금 페이지의 강조가 바뀌었다(대화가 끝나거나 취소되면 None).</summary>
        public event System.Action<TutorialHighlight> HighlightChanged;

        /// <summary>페이지가 카메라 자리를 정했다(Keep인 페이지는 보내지 않는다).</summary>
        public event System.Action<TutorialCamera> CameraRequested;

        private TutorialHighlight highlight;

        /// <summary>대화창이 열려 있다(게임 입력을 막는다).</summary>
        public bool IsRunning => sessions > 0;

        public TutorialProgress Progress => progress;

        private bool Ready => view != null && data != null;

        public void Initialize()
        {
            if (view != null)
            {
                view.Initialize();
            }
        }

        /// <summary>새 게임 하나를 시작한다. 진행 중인 대화·강조는 정리한다.</summary>
        public void BeginRun()
        {
            Cancel();
            progress = Ready ? TutorialProgress.BeginRun(store, alwaysShow) : TutorialProgress.None;
        }

        /// <summary>대화·강조를 바로 정리한다(새 게임, 오브젝트 꺼짐). 코루틴은 부른 쪽이 멈춘다.</summary>
        public void Cancel()
        {
            sessions = 0;
            if (view != null)
            {
                view.Close();
            }

            SetHighlight(TutorialHighlight.None);
            EndServiceHighlight();
        }

        public IEnumerator PlayIntro()
        {
            if (!Ready || !progress.TryBegin(TutorialStep.Intro))
            {
                yield break;
            }

            sessions++;
            bool knows = false;
            yield return view.PlayPages(data.Intro, data, answer => knows = answer, OnPage);
            yield return view.PlayPages(knows ? data.KnowsRules : data.RouletteRules, data, null, OnPage);
            yield return view.PlayPages(data.CommonRules, data, null, OnPage);
            Finish();
        }

        public IEnumerator PlayDrink()
        {
            if (!Ready || !progress.TryBegin(TutorialStep.Drink))
            {
                yield break;
            }

            sessions++;
            yield return view.PlayPages(data.Drink, data, null, OnPage);
            Finish();
        }

        /// <summary>서비스 안내. 상점을 열 수 있을 때만 부른다. 태블릿 [서비스]는 상점에 들어가거나 회전을 시작할 때까지 깜빡인다.</summary>
        public IEnumerator PlayService(TabletPanel tablet)
        {
            if (!Ready || !progress.TryBegin(TutorialStep.Service))
            {
                yield break;
            }

            if (tablet != null)
            {
                highlightedTablet = tablet;
                tablet.SetHighlighted(TabletAction.Service, true);
            }

            sessions++;
            yield return view.PlayPages(data.Service, data, null, OnPage);
            Finish();
        }

        public IEnumerator PlayEnding(bool cleared)
        {
            progress.CancelPendingGuides();
            if (!Ready || !progress.TryBegin(TutorialStep.Ending))
            {
                yield break;
            }

            EndServiceHighlight();
            sessions++;
            yield return view.PlayPages(cleared ? data.GameClear : data.GameOver, data, null, OnPage);
            Finish();
        }

        /// <summary>이번 결말에 대사가 나올 차례인가(결과 창을 잠시 미룰지 정할 때).</summary>
        public bool WillPlayEnding => Ready && progress.ShouldShow(TutorialStep.Ending);

        /// <summary>새 게임 시작 안내(버튼 없는 딜러 말). 대화 중이면 띄우지 않는다. 게임 입력은 막지 않는다.</summary>
        public void ShowStartNotice()
        {
            if (Ready && !IsRunning)
            {
                view.ShowNotice(data.StartNotice, data);
            }
        }

        public void HideNotice()
        {
            if (view != null)
            {
                view.HideNotice();
            }
        }

        /// <summary>서비스 강조를 끈다(상점 진입, 회전 시작, 새 게임).</summary>
        public void EndServiceHighlight()
        {
            if (highlightedTablet != null)
            {
                highlightedTablet.SetHighlighted(TabletAction.Service, false);
                highlightedTablet = null;
            }
        }

        private void Finish()
        {
            sessions = Mathf.Max(0, sessions - 1);
            if (sessions == 0)
            {
                SetHighlight(TutorialHighlight.None);
                if (view != null)
                {
                    view.Close();
                }
            }
        }

        private void OnPage(TutorialPage page)
        {
            if (page.camera != TutorialCamera.Keep)
            {
                CameraRequested?.Invoke(page.camera);
            }

            SetHighlight(page.highlight);
        }

        private void SetHighlight(TutorialHighlight value)
        {
            if (highlight == value)
            {
                return;
            }

            highlight = value;
            HighlightChanged?.Invoke(value);
        }

        private void OnDisable()
        {
            Cancel();
        }

        /// <summary>개발용: 저장된 '튜토리얼 시작함'을 지운다. 다음 새 게임부터 다시 나온다.</summary>
        [ContextMenu("튜토리얼 다시 보기(저장 초기화)")]
        public void ResetSavedProgress()
        {
            store.Clear();
            Debug.Log("[Tutorial] 저장된 튜토리얼 진행을 지웠다. 다음 새 게임부터 다시 나온다.");
        }
    }
}
