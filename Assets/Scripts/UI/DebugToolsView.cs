#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectCR
{
    /// <summary>
    /// 에디터 전용 디버그 버튼(오른쪽 위 [옵션] 아래). 파일 전체가 UNITY_EDITOR 안이라 빌드에는 코드도 화면도 들어가지 않는다.
    /// 버튼은 HUD 안에 코드로 만들고 blocks-input이라 누를 때 테이블 입력이 같이 눌리지 않는다.
    /// 동작은 RouletteGame이 넘겨준다(결과 문구를 돌려주면 버튼 아래에 잠깐 보여 준다).
    /// </summary>
    public sealed class DebugToolsView
    {
        private readonly Label status;
        private IVisualElementScheduledItem clearStatus;

        public DebugToolsView(VisualElement root, Func<string> clearRound, Func<string> addMoney)
        {
            var box = new VisualElement { name = "debug-tools" };
            box.AddToClassList("blocks-input");
            box.style.position = Position.Absolute;
            box.style.right = 16;
            box.style.top = 64;
            box.style.width = 170;
            box.style.paddingLeft = 8;
            box.style.paddingRight = 8;
            box.style.paddingTop = 6;
            box.style.paddingBottom = 8;
            box.style.backgroundColor = new Color(0.08f, 0.04f, 0.04f, 0.85f);
            SetBorder(box, new Color(0.85f, 0.3f, 0.25f), 1f, 6f);

            var title = new Label("DEBUG (에디터 전용)");
            title.style.fontSize = 12;
            title.style.color = new Color(1f, 0.55f, 0.5f);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 4;
            box.Add(title);

            box.Add(MakeButton("라운드 클리어", () => Show(clearRound())));
            box.Add(MakeButton("돈 +100,000", () => Show(addMoney())));

            status = new Label();
            status.style.fontSize = 12;
            status.style.color = new Color(0.95f, 0.9f, 0.75f);
            status.style.whiteSpace = WhiteSpace.Normal;
            status.style.marginTop = 4;
            status.style.display = DisplayStyle.None;
            box.Add(status);

            root.Add(box);
        }

        private void Show(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            Debug.Log("[Debug] " + message);
            status.text = message;
            status.style.display = DisplayStyle.Flex;
            clearStatus?.Pause();
            clearStatus = status.schedule.Execute(() => status.style.display = DisplayStyle.None).StartingIn(3000);
        }

        private static Button MakeButton(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.style.height = 30;
            button.style.marginLeft = 0;
            button.style.marginRight = 0;
            button.style.marginTop = 3;
            button.style.fontSize = 14;
            button.style.color = new Color(0.95f, 0.92f, 0.85f);
            button.style.backgroundColor = new Color(0.25f, 0.12f, 0.1f);
            SetBorder(button, new Color(0.6f, 0.25f, 0.2f), 1f, 4f);
            return button;
        }

        private static void SetBorder(VisualElement element, Color color, float width, float radius)
        {
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }
    }
}
#endif
