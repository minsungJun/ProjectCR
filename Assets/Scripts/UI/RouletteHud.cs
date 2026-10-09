using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectCR
{
    /// <summary>
    /// 룰렛 화면 UI: 왼쪽 위 결과 안내(당첨·낙첨·진행 문구), 오른쪽 위 시점 전환·옵션 버튼, 판 위 호버 툴팁, 게임 오버 창.
    /// 음료는 서빙 로봇(DrinkServerRobot) 선반에서 고르고(툴팁만 여기 ShowDrinkTooltip), 서비스 창은 ShopView가 그린다. 보유 금액·납부금·남은 회전·회전/서비스/전체 회수 버튼은
    /// 테이블 태블릿(TabletPanel)에 있다(2026-10-09 위아래 임시 HUD 띠 제거).
    /// 납부는 버튼 없이 라운드가 끝나면 자동으로 판정한다(RouletteGame). 상태를 읽어 그리고, 버튼 입력을 이벤트로 넘기기만 한다.
    ///
    /// 칩을 놓는 건 3D 테이블에서 한다(TableBetInput). 3D 입력이 HUD 위 클릭을 무시하도록
    /// <see cref="IsPointerOverUi"/>를 제공한다. 입력을 막는 요소에는 USS 클래스 blocks-input을 붙인다.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class RouletteHud : MonoBehaviour
    {
        private const string SkullMark = "☠";

        // 결과 아래에 보여 주는 이전 숫자 수(공 단위, 최근 것부터).
        private const int HistoryLength = 5;
        private const string BlocksInputClass = "blocks-input";

        // 호버 툴팁: 커서에서 띄우는 거리와 화면 가장자리 여백(패널 px).
        private const float TooltipOffset = 26f;
        private const float TooltipMargin = 12f;

        public event Action RestartClicked;
        public event Action ViewToggled;

        private RouletteDefinition definition;
        private UIDocument document;

        private Label resultLabel;
        private VisualElement resultBalls;
        private VisualElement resultHistory;
        private Label dealerHint;

        // 지금 결과 줄에 보이는 회전의 공들, 그리고 그 이전에 나온 공들(최근 것이 앞).
        private IReadOnlyList<BallResult> shownBalls;
        private readonly List<BallResult> history = new List<BallResult>();
        private Label gameOverLabel;
        private VisualElement gameOverPanel;
        private VisualElement optionsMenu;
        private VisualElement betTooltip;
        private Label tooltipKind;
        private Label tooltipBase;
        private Label tooltipFinal;
        private Label tooltipTargets;
        private Label tooltipStake;
        private Label tooltipProgress;

        /// <summary>RouletteGame.Start에서 부른다. UIDocument가 루트를 만든 뒤여야 한다.</summary>
        public void Initialize(RouletteDefinition definition)
        {
            this.definition = definition;
            document = GetComponent<UIDocument>();
            VisualElement root = document.rootVisualElement;

            resultLabel = root.Q<Label>("result");
            resultBalls = root.Q("result-balls");
            resultHistory = root.Q("result-history");
            dealerHint = root.Q<Label>("dealer-hint");
            gameOverLabel = root.Q<Label>("gameover-text");
            gameOverPanel = root.Q("gameover-panel");
            betTooltip = root.Q("bet-tooltip");
            tooltipKind = root.Q<Label>("bet-tooltip-kind");
            tooltipBase = root.Q<Label>("bet-tooltip-base");
            tooltipFinal = root.Q<Label>("bet-tooltip-final");
            tooltipTargets = root.Q<Label>("bet-tooltip-targets");
            tooltipStake = root.Q<Label>("bet-tooltip-stake");
            tooltipProgress = root.Q<Label>("bet-tooltip-progress");

            root.Q<Button>("restart").clicked += () => RestartClicked?.Invoke();

            // 옵션: 누르면 메뉴를 열고 닫는다(지금은 항목이 없어 열리지 않는다). 시점 전환은 옵션 왼쪽 버튼.
            optionsMenu = root.Q("options-menu");
            root.Q<Button>("options").clicked += () => SetOptionsOpen(!optionsMenu.ClassListContains(OptionsOpenClass));
            root.Q<Button>("view-toggle").clicked += () =>
            {
                SetOptionsOpen(false);
                ViewToggled?.Invoke();
            };
        }

        private const string OptionsOpenClass = "options-menu--open";

        private void SetOptionsOpen(bool open) => optionsMenu.EnableInClassList(OptionsOpenClass, open && optionsMenu.childCount > 0);

        /// <summary>화면 좌표(Input System, 왼쪽 아래 원점)가 입력을 막는 HUD 요소 위인가.</summary>
        public bool IsPointerOverUi(Vector2 screenPosition)
        {
            if (document == null || document.rootVisualElement?.panel == null)
            {
                return false;
            }

            IPanel panel = document.rootVisualElement.panel;
            Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));
            for (VisualElement element = panel.Pick(panelPosition); element != null; element = element.parent)
            {
                if (element.ClassListContains(BlocksInputClass))
                {
                    return true;
                }
            }

            return false;
        }

        public void Refresh(GameFlowController flow)
        {
            RefreshGameOver(flow);
            RefreshDealerHint(flow);
        }

        /// <summary>딜러의 귀띔: "이번 회전에 두 번째 DOZEN(13~24)은 나오지 않습니다". 단서가 없으면 숨긴다.</summary>
        private void RefreshDealerHint(GameFlowController flow)
        {
            if (dealerHint == null)
            {
                return;
            }

            bool show = flow.HasDealerHint && flow.Phase == GamePhase.Betting;
            dealerHint.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (show)
            {
                dealerHint.text = $"딜러의 귀띔 · 이번 회전에 {HintName(flow.DealerHintTarget)}은(는) 나오지 않습니다";
            }
        }

        private static string HintName(BetTarget target)
        {
            switch (target.Kind)
            {
                case BetKind.Straight:
                    return $"{target.Number}";
                case BetKind.Red:
                case BetKind.Black:
                case BetKind.Odd:
                case BetKind.Even:
                    return BetHoverInfo.KindNameOf(target);
                default:
                    return $"{BetHoverInfo.KindNameOf(target)}({BetHoverInfo.TargetsOf(target)})";
            }
        }

        /// <summary>
        /// 판 위 호버 툴팁 내용. 첫 줄 = 베팅 이름 · (강화 시 기본 배율 →) 최종 배율, 둘째 줄 = 대상, 셋째 줄 = 칩 한 개 예상 지급액.
        /// showPayout이 false면 이름과 대상만(상점의 숫자 집중 대상 고르기).
        /// </summary>
        public void ShowBetTooltip(BetHoverInfo info, bool showPayout)
        {
            if (betTooltip == null)
            {
                return;
            }

            bool boosted = showPayout && info.IsBoosted;
            tooltipKind.text = showPayout ? $"{info.KindName} ·" : info.KindName;
            tooltipBase.text = $"×{info.BasePayout} →";
            tooltipBase.style.display = boosted ? DisplayStyle.Flex : DisplayStyle.None;
            tooltipFinal.text = $"×{NumberText.Multiplier(info.Multiplier)}";
            tooltipFinal.style.display = showPayout ? DisplayStyle.Flex : DisplayStyle.None;
            tooltipFinal.EnableInClassList("bet-tooltip__final--boosted", boosted);
            tooltipTargets.text = info.Targets;

            string stake = $"칩 {info.ChipValue:N0} → 당첨 시 {info.ExpectedPayout:N0} 지급";
            if (info.BallCount > 1)
            {
                stake += " (공 1개 기준)";
            }

            if (!info.Affordable)
            {
                stake += " · 잔액 부족";
            }

            tooltipStake.text = stake;
            tooltipStake.EnableInClassList("bet-tooltip__stake--short", !info.Affordable);
            tooltipStake.style.display = showPayout ? DisplayStyle.Flex : DisplayStyle.None;

            // 누적형 교차 강화 진행도(예: DOZEN 위에서 "COLUMN 강화까지 35 / 60").
            tooltipProgress.text = info.Progress;
            tooltipProgress.style.display = showPayout && info.Progress.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            betTooltip.style.display = DisplayStyle.Flex;
        }

        /// <summary>
        /// 로봇 선반 음료 툴팁: 이름 · 변화(금색) / 효과 / 예시·확률 / 안내. 판 호버 툴팁과 같은 요소·위치 규칙을 쓴다.
        /// </summary>
        public void ShowDrinkTooltip(DrinkHoverText text, string hint)
        {
            if (betTooltip == null)
            {
                return;
            }

            tooltipKind.text = $"{text.Title} ·";
            tooltipBase.style.display = DisplayStyle.None;
            tooltipFinal.text = text.Accent;
            tooltipFinal.style.display = DisplayStyle.Flex;
            tooltipFinal.EnableInClassList("bet-tooltip__final--boosted", true);
            tooltipTargets.text = text.Effect;
            tooltipStake.text = text.Detail;
            tooltipStake.RemoveFromClassList("bet-tooltip__stake--short");
            tooltipStake.style.display = DisplayStyle.Flex;
            tooltipProgress.text = hint ?? string.Empty;
            tooltipProgress.style.display = string.IsNullOrEmpty(hint) ? DisplayStyle.None : DisplayStyle.Flex;
            betTooltip.style.display = DisplayStyle.Flex;
        }

        /// <summary>
        /// 툴팁을 커서(화면 좌표, Input System = 왼쪽 아래 원점) 위쪽에 놓는다. 좌우는 화면 안으로 밀고, 위가 모자라면 커서 아래로 뒤집는다.
        /// 크기가 아직 계산되지 않았으면(처음 뜬 프레임) 엉뚱한 자리에 보이지 않게 한 프레임 숨긴다.
        /// </summary>
        public void MoveBetTooltip(Vector2 screenPosition)
        {
            if (betTooltip?.panel == null || betTooltip.parent == null)
            {
                return;
            }

            Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(betTooltip.panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));
            Vector2 point = betTooltip.parent.WorldToLocal(panelPoint);
            float width = betTooltip.resolvedStyle.width;
            float height = betTooltip.resolvedStyle.height;
            if (float.IsNaN(width) || float.IsNaN(height) || width <= 0f || height <= 0f)
            {
                betTooltip.style.visibility = Visibility.Hidden;
                return;
            }

            Rect area = betTooltip.parent.layout;
            float left = Mathf.Clamp(point.x - width * 0.5f, TooltipMargin, Mathf.Max(TooltipMargin, area.width - width - TooltipMargin));
            float top = point.y - TooltipOffset - height;
            if (top < TooltipMargin)
            {
                top = Mathf.Min(point.y + TooltipOffset, area.height - height - TooltipMargin);
            }

            betTooltip.style.left = left;
            betTooltip.style.top = top;
            betTooltip.style.visibility = Visibility.Visible;
        }

        public void HideBetTooltip()
        {
            if (betTooltip != null)
            {
                betTooltip.style.display = DisplayStyle.None;
                betTooltip.style.visibility = Visibility.Hidden;
            }
        }

        public void ShowMessage(string message)
        {
            SetResultBalls(null);
            resultLabel.text = message;
            resultLabel.RemoveFromClassList("result--skull");
            resultLabel.RemoveFromClassList("result--gain");
        }

        public void ShowSettlement(SettlementResult settlement)
        {
            // 결과 줄에 있던 회전은 "이전 숫자"로 내려간다(한 회전의 공 여러 개는 결과 줄과 같은 순서로).
            if (shownBalls != null)
            {
                for (int i = shownBalls.Count - 1; i >= 0; i--)
                {
                    history.Insert(0, shownBalls[i]);
                }

                if (history.Count > HistoryLength)
                {
                    history.RemoveRange(HistoryLength, history.Count - HistoryLength);
                }

                RefreshHistory();
            }

            // 나온 숫자는 칸 색 배경의 패로 보여 준다(글자로 "(빨강)"을 붙이지 않는다).
            shownBalls = settlement.Spin.Balls;
            SetResultBalls(settlement.Spin.Balls);

            // 숫자 뒤에는 이번 회전 손익만: 지급 − 건 금액 − 해골 벌칙 + 보험 환급(SettlementResult.Net).
            long net = settlement.Net;
            resultLabel.text = Signed(net);
            resultLabel.EnableInClassList("result--gain", net > 0);
            resultLabel.EnableInClassList("result--skull", net < 0);
        }

        // ---------- 문구 ----------

        private static string Signed(long value) => value >= 0 ? $"+{value:N0}" : $"−{-value:N0}";

        /// <summary>새 게임: 결과 줄과 이전 숫자를 비운다.</summary>
        public void ClearHistory()
        {
            shownBalls = null;
            history.Clear();
            RefreshHistory();
        }

        private void RefreshHistory()
        {
            if (resultHistory == null)
            {
                return;
            }

            resultHistory.Clear();
            resultHistory.style.display = history.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (history.Count == 0)
            {
                return;
            }

            var label = new Label("이전") { pickingMode = PickingMode.Ignore };
            label.AddToClassList("result-history__label");
            resultHistory.Add(label);
            foreach (BallResult ball in history)
            {
                Label badge = MakeBallBadge(ball);
                badge.AddToClassList("result-ball--small");
                resultHistory.Add(badge);
            }
        }

        private static Label MakeBallBadge(BallResult ball)
        {
            var badge = new Label(ball.IsSkull ? SkullMark : ball.Number.ToString()) { pickingMode = PickingMode.Ignore };
            badge.AddToClassList("result-ball");
            badge.AddToClassList(ball.IsSkull ? "result-ball--skull"
                : ball.Color == PocketColor.Red ? "result-ball--red"
                : ball.Color == PocketColor.Black ? "result-ball--black"
                : "result-ball--green");
            return badge;
        }

        /// <summary>결과 숫자 패를 다시 만든다. null이면 숨긴다(진행 안내 문구일 때).</summary>
        private void SetResultBalls(IReadOnlyList<BallResult> balls)
        {
            if (resultBalls == null)
            {
                return;
            }

            resultBalls.Clear();
            resultBalls.style.display = balls != null && balls.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (balls == null)
            {
                return;
            }

            var prefix = new Label("결과") { pickingMode = PickingMode.Ignore };
            prefix.AddToClassList("result-prefix");
            resultBalls.Add(prefix);

            foreach (BallResult ball in balls)
            {
                resultBalls.Add(MakeBallBadge(ball));
            }

            // 숫자와 손익 사이 구분선(손익의 +/− 색과 섞이지 않게 따로 흐린 색으로).
            var separator = new Label("─") { pickingMode = PickingMode.Ignore };
            separator.AddToClassList("result-separator");
            resultBalls.Add(separator);
        }

        private void RefreshGameOver(GameFlowController flow)
        {
            bool visible = flow.Phase == GamePhase.GameOver;
            gameOverPanel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible)
            {
                return;
            }

            var text = new StringBuilder("게임 오버\n");
            text.AppendLine($"라운드 {flow.Round.Round}의 납부금 {flow.Round.CurrentDue:N0}을 내지 못했습니다. 보유 {flow.Wallet.Balance:N0}");
            if (flow.Round.ForfeitedSpins > 0)
            {
                text.AppendLine($"걸 돈이 없어 남은 회전 {flow.Round.ForfeitedSpins}회를 쓰지 못했습니다.");
            }

            text.Append($"마신 음료 {flow.Drinks.TotalStacks}잔");
            gameOverLabel.text = text.ToString();
        }
    }
}
