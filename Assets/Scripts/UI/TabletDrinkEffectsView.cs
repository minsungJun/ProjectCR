using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectCR
{
    /// <summary>
    /// 태블릿 "마신 음료" 화면. 별도 팝업이 아니라 <b>태블릿 화면 자리에</b> 그린다:
    /// 카메라가 상점과 같은 태블릿 자리(CameraRig.MoveToTablet)에 도착하면, 매 프레임 태블릿 화면 메시를 비춘 사각형에
    /// 목록을 맞춰 놓는다(<see cref="TabletScreenFit"/>). 그래서 실제 태블릿 화면이 바뀐 것처럼 보인다.
    /// 마신 음료가 많아 화면을 넘으면 목록이 세로로 스크롤된다(마우스 휠·스크롤바).
    /// 카메라가 오가는 동안은 숨긴다. 내용은 <see cref="DrinkEffectSummary"/>(마신 음료 효과만)이고 상태는 바꾸지 않는다.
    /// 요소는 HUD UIDocument 안에 코드로 만든다(uxml에 창을 따로 두지 않는다). 전체에 blocks-input이 붙어 3D 입력을 막는다.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class TabletDrinkEffectsView : MonoBehaviour
    {
        private const string HiddenClass = "tablet-drinks--hidden";

        [Tooltip("목록을 맞출 태블릿 화면 메시(CRT_Tablet_Screen).")]
        [SerializeField] private MeshFilter tabletScreen;

        [Tooltip("화면 메시 테두리에서 안쪽으로 줄이는 여백(패널 px).")]
        [Min(0f)]
        [SerializeField] private float inset = 4f;

        [Tooltip("화면을 비추는 카메라. 비우면 Camera.main.")]
        [SerializeField] private Camera view;

        // 화면 메시 꼭짓점(로컬). 메시가 런타임에 읽히지 않아 에디터에서 미리 읽어 둔다(OnValidate).
        [SerializeField, HideInInspector] private Vector3[] screenPoints = new Vector3[0];

        /// <summary>[X]를 눌렀다.</summary>
        public event Action CloseRequested;

        /// <summary>카메라가 아직 태블릿 앞으로 옮겨 가는 중인가(RouletteGame이 넣는다). 그동안은 숨긴다.</summary>
        public Func<bool> CameraBusy;

        private VisualElement screen;
        private ScrollView list;
        private Label empty;

        public bool IsOpen { get; private set; }

        /// <summary>태블릿 앞에 도착해 목록이 보이는 중(조작할 수 있다).</summary>
        public bool IsShown => IsOpen && screen != null && !screen.ClassListContains(HiddenClass);

#if UNITY_EDITOR
        private void OnValidate()
        {
            screenPoints = TabletScreenFit.ReadPoints(tabletScreen);
        }
#endif

        public void Initialize()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            VisualElement host = root.Q("root") ?? root;

            screen = new VisualElement { name = "tablet-drinks" };
            screen.AddToClassList("tablet-drinks");
            screen.AddToClassList("blocks-input");
            screen.AddToClassList(HiddenClass);

            var header = new VisualElement();
            header.AddToClassList("tablet-drinks__header");
            var titles = new VisualElement();
            titles.AddToClassList("tablet-drinks__titles");
            titles.Add(MakeLabel("마신 음료", "tablet-drinks__title"));
            titles.Add(MakeLabel("이번 게임 동안 유지되는 효과", "tablet-drinks__subtitle"));
            header.Add(titles);
            var spacer = new VisualElement();
            spacer.AddToClassList("spacer");
            header.Add(spacer);
            var close = new Button(() => CloseRequested?.Invoke()) { text = "✕" };
            close.AddToClassList("tablet-drinks__close");
            header.Add(close);
            screen.Add(header);

            list = new ScrollView(ScrollViewMode.Vertical);
            list.AddToClassList("tablet-drinks__list");
            list.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            screen.Add(list);

            empty = MakeLabel("아직 마신 음료가 없습니다.", "tablet-drinks__empty");

            // 튜토리얼 대화창보다 아래에 둔다.
            VisualElement tutorial = host.Q("tutorial-panel");
            int index = tutorial != null ? host.IndexOf(tutorial) : -1;
            if (index >= 0)
            {
                host.Insert(index, screen);
            }
            else
            {
                host.Add(screen);
            }

            screen.style.display = DisplayStyle.None;
        }

        public void Open(GameFlowController flow)
        {
            if (screen == null)
            {
                return;
            }

            IsOpen = true;
            Refresh(flow);
            screen.style.display = DisplayStyle.Flex;
            screen.AddToClassList(HiddenClass);
            list.scrollOffset = Vector2.zero;
        }

        public void Close()
        {
            IsOpen = false;
            if (screen != null)
            {
                screen.AddToClassList(HiddenClass);
                screen.style.display = DisplayStyle.None;
            }
        }

        /// <summary>목록을 다시 만든다(마신 음료 효과만).</summary>
        public void Refresh(GameFlowController flow)
        {
            if (list == null)
            {
                return;
            }

            list.Clear();
            List<DrinkEffectLine> lines = DrinkEffectSummary.For(flow);
            if (lines.Count == 0)
            {
                list.Add(empty);
                return;
            }

            for (int i = 0; i < lines.Count; i++)
            {
                list.Add(MakeRow(lines[i], i == lines.Count - 1));
            }
        }

        /// <summary>태블릿 화면 자리에 맞춘다. 카메라가 움직이는 중이면 숨긴다.</summary>
        private void LateUpdate()
        {
            if (!IsOpen || screen == null || screen.panel == null)
            {
                return;
            }

            bool busy = CameraBusy != null && CameraBusy();
            Camera camera = view != null ? view : Camera.main;
            if (busy || tabletScreen == null
                || !TabletScreenFit.TryGetPanelRect(camera, tabletScreen.transform, screenPoints, screen.panel, inset, out Rect rect))
            {
                screen.AddToClassList(HiddenClass);
                return;
            }

            TabletScreenFit.Place(screen, rect);
            screen.RemoveFromClassList(HiddenClass);
        }

        private static VisualElement MakeRow(DrinkEffectLine line, bool last)
        {
            var row = new VisualElement();
            row.AddToClassList("tablet-drinks__row");
            row.EnableInClassList("tablet-drinks__row--last", last);

            row.Add(MakeGlass(line.Tint));

            var text = new VisualElement();
            text.AddToClassList("tablet-drinks__text");
            text.Add(MakeLabel(line.Title, "tablet-drinks__name"));
            text.Add(MakeLabel(line.Effect, "tablet-drinks__effect"));
            row.Add(text);

            row.Add(MakeLabel($"{line.Stacks}잔", "tablet-drinks__stacks"));
            return row;
        }

        /// <summary>칵테일 잔 그림: 역삼각형 잔(테두리 삼각형) + 다리 + 받침. 음료 색으로 칠한다.</summary>
        private static VisualElement MakeGlass(Color tint)
        {
            var glass = new VisualElement();
            glass.AddToClassList("tablet-drinks__glass");

            var bowl = new VisualElement();
            bowl.AddToClassList("tablet-drinks__glass-bowl");
            bowl.style.borderTopColor = tint;
            glass.Add(bowl);

            var stem = new VisualElement();
            stem.AddToClassList("tablet-drinks__glass-stem");
            glass.Add(stem);

            var foot = new VisualElement();
            foot.AddToClassList("tablet-drinks__glass-foot");
            glass.Add(foot);
            return glass;
        }

        private static Label MakeLabel(string text, string className)
        {
            var label = new Label(text);
            label.AddToClassList(className);
            label.pickingMode = PickingMode.Ignore;
            return label;
        }
    }
}
