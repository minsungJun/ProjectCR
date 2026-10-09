using System.Globalization;

namespace ProjectCR
{
    /// <summary>
    /// 로봇 선반 음료에 마우스를 올렸을 때의 툴팁 문구(예전 음료 카드 내용). 상태는 읽기만 한다.
    /// - 지급 음료: "컬럼 진토닉 · COLUMN ×1.5 → ×2.25" / "COLUMN 베팅 지급 배율 · 2잔째" / "예: 1열 3배 → 4.5배"
    /// - 확률 음료: "루비 위스키 · 빨강 확률" / "추첨 가중치 +10% → +20% · 2잔째" / "등장 확률 47.4% → 49.6%"
    /// 넷째 줄은 고른 뒤 안내("한 번 더 누르면 마십니다").
    /// </summary>
    public readonly struct DrinkHoverText
    {
        public DrinkHoverText(string title, string accent, string effect, string detail)
        {
            Title = title;
            Accent = accent;
            Effect = effect;
            Detail = detail;
        }

        public string Title { get; }

        /// <summary>금색으로 강조할 변화(배율 → 배율).</summary>
        public string Accent { get; }

        public string Effect { get; }
        public string Detail { get; }

        public static DrinkHoverText For(DrinkOffer offer, GameFlowController flow)
        {
            SpinConfig config = flow.BuildSpinConfig();
            if (!offer.IsProbability)
            {
                DrinkGroup group = offer.Group;
                int stacks = flow.Drinks.StacksOf(group);
                decimal now = flow.Drinks.MultiplierOf(group);
                decimal after = flow.Drinks.MultiplierAt(group, stacks + 1);
                BetTarget sample = group.Representative();
                int basePayout = config.PayoutFor(sample);
                string sampleName = group == DrinkGroup.Number ? "숫자 하나" : sample.ToKorean(flow.Definition);
                return new DrinkHoverText(
                    offer.DisplayName,
                    $"×{NumberText.Multiplier(now)} → ×{NumberText.Multiplier(after)}",
                    $"{group.ToKorean()} 베팅 지급 배율 · {stacks + 1}잔째",
                    $"예: {sampleName} {NumberText.Multiplier(basePayout * now)}배 → {NumberText.Multiplier(basePayout * after)}배");
            }

            BetTarget target = offer.Target;
            int count = flow.Probabilities.StacksOf(target);
            double chanceNow = config.ProbabilityOf(target);
            double[] weights = PocketWeightCalculator.Compute(flow.Definition, flow.Probabilities.WithExtraStack(target, offer.Boost));
            double chanceAfter = PocketWeightCalculator.ProbabilityOf(flow.Definition,
                PocketWeightCalculator.Probabilities(flow.Definition, weights, config.SkullChance), target);
            return new DrinkHoverText(
                offer.DisplayName,
                $"{DrinkOffer.TargetLabel(target)} 확률",
                $"추첨 가중치 +{Percent(offer.Boost * count)} → +{Percent(offer.Boost * (count + 1))} · {count + 1}잔째",
                $"등장 확률 {NumberText.Percent(chanceNow)} → {NumberText.Percent(chanceAfter)}");
        }

        private static string Percent(decimal value) => (value * 100m).ToString("0.#", CultureInfo.InvariantCulture) + "%";
    }
}
