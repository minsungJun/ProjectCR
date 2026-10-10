using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectCR
{
    /// <summary>
    /// 왼쪽 위 딜러 대화창(HUD UIDocument의 tutorial-panel). 왼쪽 딜러 아이콘, 오른쪽 이름·대사·[다음] 또는 [네]/[아니오].
    ///
    /// 한 묶음을 <see cref="PlayPages"/>로 보여 준다: 창과 아이콘이 먼저 뜨고 잠시 뒤 대사가 한 글자씩 나온다.
    /// 출력 중 대화창을 누르면 그 문장을 바로 다 보여 주고(다음 페이지로 넘어가지 않는다), 다 나온 뒤에만 버튼이 켜진다.
    /// 자동으로 넘기지 않는다. 페이지가 바뀌어도 창은 그대로 있다. 시간은 화면 시간(unscaled)이라 timeScale과 상관없고,
    /// 일시정지(GamePause) 중에는 흐르지 않아 그 자리에서 멈췄다 이어진다. 일시정지 중에는 누름도 받지 않는다.
    /// 창 전체에 blocks-input이 붙어 있어 대화창 위 클릭은 3D 테이블로 가지 않는다.
    /// </summary>
    public class TutorialDialogueView : MonoBehaviour
    {
        private const string OpenClass = "tutorial-panel--open";

        // 대화 중에는 창 위 클릭이 3D 테이블로 가지 않게 막고, 안내 중에는 막지 않는다(창 아래 판 칸을 누를 수 있게).
        private const string BlocksInputClass = "blocks-input";

        [Tooltip("HUD와 같은 UIDocument.")]
        [SerializeField] private UIDocument document;

        [Tooltip("딜러 픽토그램(투명 배경 스프라이트).")]
        [SerializeField] private Sprite dealerIcon;

        [Tooltip("창·아이콘이 뜬 뒤 대사가 나오기 시작할 때까지(초).")]
        [Min(0f)]
        [SerializeField] private float textDelay = 0.15f;

        [Tooltip("대사 출력 속도(초당 글자 수).")]
        [Min(1f)]
        [SerializeField] private float charactersPerSecond = 30f;

        private VisualElement panel;
        private VisualElement icon;
        private Label speaker;
        private Label text;
        private VisualElement choiceRow;
        private Button yes;
        private Button no;
        private Button next;

        private bool typing;
        private bool completeRequested;

        // 버튼 응답: 0 없음, 1 다음/네, 2 아니오.
        private int answer;
        private bool acceptingAnswer;

        public bool IsOpen { get; private set; }

        /// <summary>버튼 없는 안내(<see cref="ShowNotice"/>)가 떠 있다. 게임 입력을 막지 않는다.</summary>
        public bool IsNoticeShown { get; private set; }

        private Coroutine noticeRoutine;

        public void Initialize()
        {
            if (panel != null || document == null)
            {
                return;
            }

            VisualElement root = document.rootVisualElement;
            panel = root.Q("tutorial-panel");
            icon = root.Q("tutorial-dealer-icon");
            speaker = root.Q<Label>("tutorial-speaker");
            text = root.Q<Label>("tutorial-text");
            choiceRow = root.Q("tutorial-choice-row");
            yes = root.Q<Button>("tutorial-choice-yes");
            no = root.Q<Button>("tutorial-choice-no");
            next = root.Q<Button>("tutorial-next");
            if (panel == null)
            {
                Debug.LogWarning("[Tutorial] RouletteHud.uxml에 tutorial-panel이 없다.");
                return;
            }

            if (dealerIcon != null)
            {
                icon.style.backgroundImage = new StyleBackground(dealerIcon);
            }

            // 출력 중 대화창을 누르면 문장을 바로 완성한다. 버튼은 출력이 끝나야 보이므로 같은 누름이 다음으로 넘기지 않는다.
            panel.RegisterCallback<PointerDownEvent>(_ =>
            {
                if (typing && !GamePause.IsPaused)
                {
                    completeRequested = true;
                }
            });

            next.clicked += () => Answer(1);
            yes.clicked += () => Answer(1);
            no.clicked += () => Answer(2);
            HideImmediately();
        }

        /// <summary>
        /// 대사 묶음을 순서대로 보여 준다. 질문 페이지에서 고른 답(네 = true)을 answered로 넘기고 그 페이지에서 멈춘다(창은 열어 둔다).
        /// 질문이 없으면 마지막 페이지의 [다음]까지 기다린다. 창을 닫지는 않는다(<see cref="Close"/>).
        /// </summary>
        public IEnumerator PlayPages(TutorialPage[] pages, TutorialDialogueData data, System.Action<bool> answered = null,
            System.Action<TutorialPage> pageShown = null)
        {
            if (panel == null || pages == null)
            {
                yield break;
            }

            // 대화가 시작되면 떠 있던 안내는 대화로 바뀐다(같은 창).
            StopNotice();

            if (!IsOpen)
            {
                yield return Open(data);
            }

            foreach (TutorialPage page in pages)
            {
                if (page == null)
                {
                    continue;
                }

                pageShown?.Invoke(page);
                yield return Type(data.Format(page.text));

                // 출력이 끝나면 버튼을 켠다. 질문이면 [네]/[아니오], 아니면 [다음].
                SetButtons(page.isQuestion ? ButtonMode.Choice : ButtonMode.Next);
                answer = 0;
                acceptingAnswer = true;
                while (answer == 0)
                {
                    yield return null;
                }

                acceptingAnswer = false;
                SetButtons(ButtonMode.None);
                if (page.isQuestion)
                {
                    answered?.Invoke(answer == 1);
                    yield break;
                }
            }
        }

        /// <summary>대화를 마치고 창을 닫는다(안내도 닫힌다).</summary>
        public void Close()
        {
            StopNotice();
            HideImmediately();
        }

        /// <summary>
        /// 버튼 없는 딜러 안내 한 줄: 창과 아이콘이 뜨고 대사가 한 글자씩 나온 뒤 그대로 떠 있다. 게임 입력은 막지 않는다.
        /// <see cref="HideNotice"/>로 닫는다. 대화가 시작되면 대화가 이 창을 이어 쓴다.
        /// </summary>
        public void ShowNotice(string line, TutorialDialogueData data)
        {
            if (panel == null || IsOpen || string.IsNullOrEmpty(line))
            {
                return;
            }

            StopNotice();
            IsNoticeShown = true;
            noticeRoutine = StartCoroutine(Notice(data.Format(line), data));
        }

        public void HideNotice()
        {
            if (!IsNoticeShown)
            {
                return;
            }

            StopNotice();
            HideImmediately();
        }

        private IEnumerator Notice(string line, TutorialDialogueData data)
        {
            yield return Open(data);
            IsOpen = false; // 대화가 아니라서 열린 대화로 치지 않는다.
            panel.RemoveFromClassList(BlocksInputClass);
            yield return Type(line);
            noticeRoutine = null;
        }

        private void StopNotice()
        {
            if (noticeRoutine != null)
            {
                StopCoroutine(noticeRoutine);
                noticeRoutine = null;
            }

            if (IsNoticeShown)
            {
                IsNoticeShown = false;
                typing = false;
                completeRequested = false;
            }
        }

        private IEnumerator Open(TutorialDialogueData data)
        {
            IsOpen = true;
            panel.AddToClassList(BlocksInputClass);
            speaker.text = data.DealerName;
            text.text = string.Empty;
            SetButtons(ButtonMode.None);
            panel.style.display = DisplayStyle.Flex;

            // 다음 프레임에 열림 클래스를 붙여 나타나는 전환(USS transition)이 보이게 한다.
            yield return null;
            panel.AddToClassList(OpenClass);
            float waited = 0f;
            while (waited < textDelay)
            {
                waited += PlayingDeltaTime;
                yield return null;
            }
        }

        private IEnumerator Type(string line)
        {
            typing = true;
            completeRequested = false;
            text.text = string.Empty;
            float elapsed = 0f;
            int shown = 0;
            while (shown < line.Length && !completeRequested)
            {
                elapsed += PlayingDeltaTime;
                int target = Mathf.Min(line.Length, Mathf.FloorToInt(elapsed * charactersPerSecond) + 1);
                if (target != shown)
                {
                    shown = target;
                    text.text = line.Substring(0, shown);
                }

                yield return null;
            }

            text.text = line;
            typing = false;
            completeRequested = false;

            // 문장을 완성한 누름이 막 켜진 버튼까지 이어지지 않게 한 프레임 쉰다.
            yield return null;
        }

        /// <summary>이번 프레임의 화면 시간. 일시정지 중이면 0.</summary>
        private static float PlayingDeltaTime => GamePause.IsPaused ? 0f : Time.unscaledDeltaTime;

        private void Answer(int value)
        {
            if (!acceptingAnswer || answer != 0 || GamePause.IsPaused)
            {
                return;
            }

            // 고르는 즉시 버튼을 꺼서 중복 선택을 막는다.
            answer = value;
            yes.SetEnabled(false);
            no.SetEnabled(false);
            next.SetEnabled(false);
        }

        private enum ButtonMode
        {
            None,
            Next,
            Choice,
        }

        private void SetButtons(ButtonMode mode)
        {
            next.style.display = mode == ButtonMode.Next ? DisplayStyle.Flex : DisplayStyle.None;
            choiceRow.style.display = mode == ButtonMode.Choice ? DisplayStyle.Flex : DisplayStyle.None;
            next.SetEnabled(mode == ButtonMode.Next);
            yes.SetEnabled(mode == ButtonMode.Choice);
            no.SetEnabled(mode == ButtonMode.Choice);
        }

        private void HideImmediately()
        {
            IsOpen = false;
            IsNoticeShown = false;
            typing = false;
            acceptingAnswer = false;
            if (panel == null)
            {
                return;
            }

            panel.RemoveFromClassList(OpenClass);
            panel.style.display = DisplayStyle.None;
            SetButtons(ButtonMode.None);
        }

        private void OnDisable()
        {
            HideImmediately();
        }
    }
}
