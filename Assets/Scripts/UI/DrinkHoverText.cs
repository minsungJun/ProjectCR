using System;
using System.Globalization;

namespace ProjectCR
{
    /// <summary>
    /// 로봇 선반 음료에 마우스를 올렸을 때의 툴팁 문구. 상태는 읽기만 한다(미리보기는 잔 수를 바꾸지 않는다).
    /// - 지급 음료: "컬럼 진토닉 · 지급 강화 · 1열 ×1.5 → ×2.25"
    ///   / "1열 베팅의 당첨금 배율에 ×1.5를 곱합니다. 2열·3열은 그대로입니다." / "예: 1열에 10을 걸어 맞히면 45 → 67"
    ///   / "이번 게임 내내 유지 · 1열 2잔째, 같은 대상을 또 마시면 배율이 곱해져 쌓입니다"
    /// - 확률 음료: "코랄 위스키 · 확률 강화 · 2열 31.6% → 36.7%"
    ///   / "2열에 공이 떨어질 확률을 올립니다. 당첨금 배율은 그대로입니다." / "추첨 가중치 +26% · 2열 1잔째"
    ///   / "이번 게임 내내 유지 · 마신 대상은 다음 같은 음료에 다시 나올 확률이 높습니다"
    /// 마지막 줄 아래 안내("누르면 고릅니다")는 RouletteGame이 붙인다.
    /// 효과 설명 줄은 음료 에셋의 Description을 적으면 그 문장을 쓴다(자리 표시 {target}·{group}·{bets}·{boost}).
    /// </summary>
    public readonly struct DrinkHoverText
    {
        // 예시에 쓰는 베팅금. 튜토리얼 대사의 예("10을 걸어")와 맞춘다.
        public const long ExampleStake = 10;

        public DrinkHoverText(string title, string kind, string accent, string effect, string detail, string duration)
        {
            Title = title;
            Kind = kind;
            Accent = accent;
            Effect = effect;
            Detail = detail;
            Duration = duration;
        }

        public string Title { get; }

        /// <summary>강화 종류("지급 강화" / "확률 강화").</summary>
        public string Kind { get; }

        /// <summary>금색으로 강조할 변화(배율 → 배율, 확률 → 확률).</summary>
        public string Accent { get; }

        /// <summary>무엇을 하는지 한 문장.</summary>
        public string Effect { get; }

        /// <summary>예시 또는 수치 변화.</summary>
        public string Detail { get; }

        /// <summary>유지 기간과 쌓이는 방식.</summary>
        public string Duration { get; }

        public static DrinkHoverText For(DrinkOffer offer, GameFlowController flow)
        {
            SpinConfig config = flow.BuildSpinConfig();
            if (!offer.IsProbability)
            {
                DrinkGroup group = offer.Group;
                decimal boost = offer.Payout.BoostPerStack;
                if (offer.IsWholeGroup)
                {
                    // 숫자 음료: 0을 포함한 숫자 베팅 전체.
                    int groupStacks = flow.Drinks.GroupStacksOf(group);
                    decimal groupNow = flow.Drinks.GroupMultiplierOf(group);
                    decimal groupAfter = flow.Drinks.GroupMultiplierAt(group, groupStacks + 1);
                    int straight = config.PayoutFor(BetTarget.Straight(7));
                    return new DrinkHoverText(
                        offer.DisplayName,
                        "지급 강화",
                        $"{group.ToKorean()} 전체 ×{NumberText.Multiplier(groupNow)} → ×{NumberText.Multiplier(groupAfter)}",
                        Custom(offer.Payout.CustomDescription, group, NumberText.Multiplier(boost), group.BetsKorean(), $"{group.ToKorean()} 전체")
                        ?? $"{group.ToKorean()} 베팅 전체({group.BetsKorean()}, 0 포함)의 당첨금 배율에 ×{NumberText.Multiplier(boost)}를 곱합니다.",
                        $"예: 숫자 하나에 {ExampleStake}을 걸어 맞히면 {Payout(straight, groupNow)} → {Payout(straight, groupAfter)}",
                        $"이번 게임 내내 유지 · {groupStacks + 1}잔째, 마실 때마다 배율이 곱해져 쌓입니다");
                }

                BetTarget target = offer.Target;
                string label = DrinkOffer.TargetLabel(target);
                int stacks = flow.Drinks.StacksOf(target);
                decimal now = flow.Drinks.MultiplierOf(target);
                decimal after = flow.Drinks.MultiplierAt(target, stacks + 1);
                int basePayout = config.PayoutFor(target);
                string others = OthersUnchanged(group, target);
                string betName = label;
                return new DrinkHoverText(
                    offer.DisplayName,
                    "지급 강화",
                    $"{label} ×{NumberText.Multiplier(now)} → ×{NumberText.Multiplier(after)}",
                    Custom(offer.Payout.CustomDescription, group, NumberText.Multiplier(boost), group.BetsKorean(), label)
                    ?? $"{betName} 베팅의 당첨금 배율에 ×{NumberText.Multiplier(boost)}를 곱합니다. {others}",
                    $"예: {betName}에 {ExampleStake}을 걸어 맞히면 {Payout(basePayout, now)} → {Payout(basePayout, after)}",
                    $"이번 게임 내내 유지 · {label} {stacks + 1}잔째, 같은 대상을 또 마시면 배율이 곱해져 쌓입니다");
            }

            DrinkGroup chanceGroup = offer.Group;
            BetTarget chanceTarget = offer.Target;
            string chanceLabel = DrinkOffer.TargetLabel(chanceTarget);
            int drunk = flow.Probabilities.StacksOf(chanceTarget);
            (double chanceNow, double chanceAfter) = flow.PreviewProbabilityDrink(new[] { chanceTarget }, offer.Boost);
            return new DrinkHoverText(
                offer.DisplayName,
                "확률 강화",
                $"{chanceLabel} {NumberText.Percent(chanceNow)} → {NumberText.Percent(chanceAfter)}",
                Custom(offer.Probability.CustomDescription, chanceGroup, "+" + Percent(offer.Boost), Choices(chanceGroup), chanceLabel)
                ?? $"{chanceLabel}에 공이 떨어질 확률을 올립니다. 당첨금 배율은 그대로입니다.",
                $"추첨 가중치 +{Percent(offer.Boost)} · {chanceLabel} {drunk + 1}잔째",
                "이번 게임 내내 유지 · 마신 대상은 다음 같은 음료에 다시 나올 확률이 높습니다");
        }

        /// <summary>묶음의 대상을 짧게(1열·2열·3열, 숫자 0~36). 설명 자리 표시 {bets}에 쓴다.</summary>
        private static string Choices(DrinkGroup group) => group == DrinkGroup.Number ? "숫자(0~36)" : group.BetsKorean();

        /// <summary>같은 묶음의 다른 대상은 그대로라는 문장("2열·3열은 그대로입니다.").</summary>
        private static string OthersUnchanged(DrinkGroup group, BetTarget target)
        {
            string others = string.Join("·", group.Targets().FindAll(t => t != target).ConvertAll(DrinkOffer.TargetLabel));
            return $"{others}{Particle(others, "은", "는")} 그대로입니다.";
        }

        /// <summary>마지막 글자에 받침이 있으면 withFinal, 없으면 withoutFinal. 숫자는 한국어 읽기(1 일, 2 이 …)로 본다.</summary>
        private static string Particle(string word, string withFinal, string withoutFinal)
        {
            if (string.IsNullOrEmpty(word))
            {
                return withFinal;
            }

            char last = word[word.Length - 1];
            if (last >= '0' && last <= '9')
            {
                return "013678".IndexOf(last) >= 0 ? withFinal : withoutFinal;
            }

            if (last >= '가' && last <= '힣')
            {
                return (last - '가') % 28 != 0 ? withFinal : withoutFinal;
            }

            return withFinal;
        }

        /// <summary>에셋에 적은 설명의 자리 표시를 채운다. 적지 않았으면 null(자동 문장을 쓴다).</summary>
        private static string Custom(string text, DrinkGroup group, string boost, string bets, string target)
        {
            if (text == null)
            {
                return null;
            }

            return text.Replace("{target}", target)
                .Replace("{bets}", bets)
                .Replace("{group}", group.ToKorean())
                .Replace("{boost}", boost);
        }

        /// <summary>예시 지급액(원금 포함, 버림): 베팅금 × 기본 배수 × 음료 배율.</summary>
        private static string Payout(int basePayout, decimal multiplier) =>
            Math.Floor(ExampleStake * basePayout * multiplier).ToString("N0", CultureInfo.InvariantCulture);

        private static string Percent(decimal value) => (value * 100m).ToString("0.#", CultureInfo.InvariantCulture) + "%";
    }
}
