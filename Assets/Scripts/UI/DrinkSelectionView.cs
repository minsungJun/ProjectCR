using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectCR
{
    /// <summary>
    /// 라운드 통과 보상 창(음료 고르기). HUD와 같은 UIDocument 안의 drink-panel을 그린다.
    /// 후보는 GameFlowController가 한 번 만든 것을 읽기만 한다. 카드를 누르면 강조만 되고, [선택 확정]에서 이벤트를 보낸다.
    /// 두 번 적용되지 않게 하는 건 GameFlowController.ConfirmDrink(상태 확인)이다. 여기서 버튼을 끄는 건 보기 좋으라고.
    /// 창 전체에 blocks-input이 붙어 있어 열려 있는 동안 3D 테이블 입력이 막힌다.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class DrinkSelectionView : MonoBehaviour
    {
        private const string SelectedClass = "drink-card--selected";

        /// <summary>확정한 후보 번호.</summary>
        public event Action<int> Confirmed;

        private readonly List<VisualElement> cards = new List<VisualElement>();

        private VisualElement panel;
        private VisualElement cardRow;
        private Label header;
        private Button confirmButton;
        private int selected = -1;

        public bool IsOpen { get; private set; }

        /// <summary>RouletteGame.Start에서 부른다. UIDocument가 루트를 만든 뒤여야 한다.</summary>
        public void Initialize()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            panel = root.Q("drink-panel");
            cardRow = root.Q("drink-cards");
            header = root.Q<Label>("drink-header");
            confirmButton = root.Q<Button>("drink-confirm");
            confirmButton.clicked += OnConfirmClicked;
            Hide();
        }

        /// <summary>지금 후보로 카드를 만들고 연다. 다시 불러도 후보는 그대로다(GameFlowController가 한 번만 만든다).</summary>
        public void Show(GameFlowController flow)
        {
            IReadOnlyList<DrinkOffer> offer = flow.Rewards.CurrentOffer;
            if (offer == null || offer.Count == 0)
            {
                Hide();
                return;
            }

            header.text = $"라운드 {flow.Round.Round} 통과 · 납부 {flow.LastPaidDue:N0} · 남은 돈 {flow.Wallet.Balance:N0}";

            SpinConfig config = flow.BuildSpinConfig();
            cardRow.Clear();
            cards.Clear();
            for (int i = 0; i < offer.Count; i++)
            {
                VisualElement card = BuildCard(offer[i], flow, config);
                int index = i;
                card.RegisterCallback<ClickEvent>(_ => Select(index));
                cardRow.Add(card);
                cards.Add(card);
            }

            Select(-1);
            panel.style.display = DisplayStyle.Flex;
            IsOpen = true;
        }

        public void Hide()
        {
            if (panel != null)
            {
                panel.style.display = DisplayStyle.None;
            }

            IsOpen = false;
            selected = -1;
        }

        private void Select(int index)
        {
            selected = index;
            for (int i = 0; i < cards.Count; i++)
            {
                cards[i].EnableInClassList(SelectedClass, i == index);
            }

            confirmButton.SetEnabled(index >= 0);
        }

        private void OnConfirmClicked()
        {
            if (!IsOpen || selected < 0)
            {
                return;
            }

            confirmButton.SetEnabled(false);
            Confirmed?.Invoke(selected);
        }

        // ---------- 카드 ----------

        private static VisualElement BuildCard(DrinkOffer offer, GameFlowController flow, SpinConfig config)
        {
            return offer.IsProbability
                ? BuildProbabilityCard(offer, flow, config)
                : BuildPayoutCard(offer.Payout, flow, config);
        }

        /// <summary>지급 강화: 묶음 지급 배율 지금 → 마시면, 대표 베팅의 배수 변화.</summary>
        private static VisualElement BuildPayoutCard(DrinkDefinition drink, GameFlowController flow, SpinConfig config)
        {
            DrinkGroup group = drink.Group;
            int stacks = flow.Drinks.StacksOf(group);
            decimal now = flow.Drinks.MultiplierOf(group);
            decimal after = flow.Drinks.MultiplierAt(group, stacks + 1);

            var card = new VisualElement();
            card.AddToClassList("drink-card");

            card.Add(BuildBottle(drink.Icon, drink.Tint, ShortName(group)));
            card.Add(MakeLabel(drink.DisplayName, "drink-name"));
            card.Add(MakeLabel($"{group.ToKorean()} · 지급", "drink-group"));
            card.Add(MakeLabel(drink.Description, "drink-effect"));

            var divider = new VisualElement();
            divider.AddToClassList("drink-divider");
            card.Add(divider);

            card.Add(MakeLabel(stacks > 0 ? $"지금 {stacks}잔 · ×{NumberText.Multiplier(now)}" : "아직 마시지 않음 · ×1", "drink-stat"));
            card.Add(MakeLabel($"마시면 {stacks + 1}잔 · ×{NumberText.Multiplier(after)}", "drink-stat", "drink-stat--after"));

            // 대표 베팅의 지급 배수 변화. 기본 배수 × 묶음 배율(정산과 같은 식).
            BetTarget sample = group.Representative();
            int basePayout = config.PayoutFor(sample);
            string sampleName = group == DrinkGroup.Number ? "숫자 하나" : sample.ToKorean(flow.Definition);
            card.Add(MakeLabel(
                $"예: {sampleName} {NumberText.Multiplier(basePayout * now)}배 → {NumberText.Multiplier(basePayout * after)}배",
                "drink-example"));

            return card;
        }

        /// <summary>
        /// 확률 강화: 구체적인 대상의 가중치 증가, 그 대상의 잔 수 지금 → 마시면,
        /// 대상 전체의 실제 등장 확률 지금 → 마시면(가지고 있는 모든 확률 효과 반영, 해골 1/38 고정).
        /// 미리보기는 임시 효과 목록으로 계산하고 실제 상태는 바꾸지 않는다.
        /// </summary>
        private static VisualElement BuildProbabilityCard(DrinkOffer offer, GameFlowController flow, SpinConfig config)
        {
            BetTarget target = offer.Target;
            string targetName = DrinkOffer.TargetLabel(target);
            int stacks = flow.Probabilities.StacksOf(target);

            double now = config.ProbabilityOf(target);
            double[] previewWeights = PocketWeightCalculator.Compute(flow.Definition, flow.Probabilities.WithExtraStack(target, offer.Boost));
            double[] previewOdds = PocketWeightCalculator.Probabilities(flow.Definition, previewWeights, config.SkullChance);
            double after = PocketWeightCalculator.ProbabilityOf(flow.Definition, previewOdds, target);

            var card = new VisualElement();
            card.AddToClassList("drink-card");
            card.AddToClassList("drink-card--chance");

            card.Add(BuildGlass(offer.Icon, offer.Tint));
            card.Add(MakeLabel(offer.DisplayName, "drink-name"));
            card.Add(MakeLabel($"{offer.Group.ToKorean()} · 확률", "drink-group", "drink-group--chance"));
            card.Add(MakeLabel($"{targetName} 추첨 가중치 +{BoostPercent(offer.Boost)}", "drink-effect"));

            var divider = new VisualElement();
            divider.AddToClassList("drink-divider");
            card.Add(divider);

            card.Add(MakeLabel(stacks > 0 ? $"지금 {stacks}잔 · 가중치 +{BoostPercent(offer.Boost * stacks)}" : "아직 마시지 않음", "drink-stat"));
            card.Add(MakeLabel($"마시면 {stacks + 1}잔 · 가중치 +{BoostPercent(offer.Boost * (stacks + 1))}", "drink-stat", "drink-stat--after"));
            card.Add(MakeLabel($"등장 확률 {NumberText.Percent(now)} → {NumberText.Percent(after)}", "drink-example"));
            return card;
        }

        private static string BoostPercent(decimal boost) =>
            (boost * 100m).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%";

        /// <summary>지급 강화 대체 그림: 강조색 병.</summary>
        private static VisualElement BuildBottle(Sprite icon, Color tint, string label)
        {
            var art = new VisualElement();
            art.AddToClassList("drink-art");

            if (icon != null)
            {
                art.style.backgroundImage = new StyleBackground(icon);
                return art;
            }

            var neck = new VisualElement();
            neck.AddToClassList("drink-bottle__neck");
            neck.style.backgroundColor = Color.Lerp(tint, Color.black, 0.25f);

            var body = new VisualElement();
            body.AddToClassList("drink-bottle__body");
            body.style.backgroundColor = tint;
            body.Add(MakeLabel(label, "drink-bottle__label"));

            art.Add(neck);
            art.Add(body);
            return art;
        }

        /// <summary>확률 강화 대체 그림: 위스키 잔(낮고 넓은 잔에 강조색 음료).</summary>
        private static VisualElement BuildGlass(Sprite icon, Color tint)
        {
            var art = new VisualElement();
            art.AddToClassList("drink-art");

            if (icon != null)
            {
                art.style.backgroundImage = new StyleBackground(icon);
                return art;
            }

            var glass = new VisualElement();
            glass.AddToClassList("drink-glass");

            var liquid = new VisualElement();
            liquid.AddToClassList("drink-glass__liquid");
            liquid.style.backgroundColor = tint;
            liquid.Add(MakeLabel("%", "drink-glass__label"));

            glass.Add(liquid);
            art.Add(glass);
            return art;
        }

        private static string ShortName(DrinkGroup group)
        {
            switch (group)
            {
                case DrinkGroup.Column:
                    return "COL";
                case DrinkGroup.Dozen:
                    return "DOZ";
                case DrinkGroup.Parity:
                    return "O/E";
                case DrinkGroup.Color:
                    return "R/B";
                case DrinkGroup.Range:
                    return "H/L";
                default:
                    return "No.";
            }
        }

        private static Label MakeLabel(string text, params string[] classes)
        {
            var label = new Label(text);
            foreach (string name in classes)
            {
                label.AddToClassList(name);
            }

            label.pickingMode = PickingMode.Ignore;
            return label;
        }
    }
}
