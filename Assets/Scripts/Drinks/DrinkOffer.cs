using UnityEngine;

namespace ProjectCR
{
    public enum DrinkEffectKind
    {
        /// <summary>묶음 지급 배율 ×(<see cref="DrinkDefinition"/>).</summary>
        Payout,

        /// <summary>대상 추첨 가중치 +(<see cref="ProbabilityDrinkDefinition"/>).</summary>
        Probability,
    }

    /// <summary>
    /// 보상 후보 카드 하나. 지급 강화면 음료만, 확률 강화면 음료 + 후보를 만들 때 정한 구체적인 대상과 계수.
    /// 후보는 한 번 만들면 고를 때까지 그대로다(대상도 다시 뽑지 않는다).
    /// </summary>
    public sealed class DrinkOffer
    {
        private DrinkOffer(DrinkEffectKind kind, DrinkDefinition payout, ProbabilityDrinkDefinition probability, BetTarget target, decimal boost)
        {
            Kind = kind;
            Payout = payout;
            Probability = probability;
            Target = target;
            Boost = boost;
        }

        public DrinkEffectKind Kind { get; }

        /// <summary>지급 강화 음료. 확률 강화면 null.</summary>
        public DrinkDefinition Payout { get; }

        /// <summary>확률 강화 음료. 지급 강화면 null.</summary>
        public ProbabilityDrinkDefinition Probability { get; }

        /// <summary>확률 강화 대상. 지급 강화면 None.</summary>
        public BetTarget Target { get; }

        /// <summary>지급 강화: 한 잔 배율(1.5). 확률 강화: 한 잔 가중치 증가(0.1).</summary>
        public decimal Boost { get; }

        public bool IsProbability => Kind == DrinkEffectKind.Probability;

        public DrinkGroup Group => IsProbability ? Probability.Group : Payout.Group;
        public string DisplayName => IsProbability ? Probability.DisplayName : Payout.DisplayName;
        public Color Tint => IsProbability ? Probability.Tint : Payout.Tint;
        public Sprite Icon => IsProbability ? Probability.Icon : Payout.Icon;

        public static DrinkOffer ForPayout(DrinkDefinition drink) =>
            new DrinkOffer(DrinkEffectKind.Payout, drink, null, BetTarget.None, drink.BoostPerStack);

        public static DrinkOffer ForProbability(ProbabilityDrinkDefinition drink, BetTarget target) =>
            new DrinkOffer(DrinkEffectKind.Probability, null, drink, target, drink.BoostFor(target));

        /// <summary>확률 강화 대상 이름(빨강, 13–24, 7번, {1, 2, 4, 5}).</summary>
        public static string TargetLabel(BetTarget target)
        {
            if (target.IsStraight)
            {
                return $"{target.Number}번";
            }

            if (target.IsInside)
            {
                return "{" + string.Join(", ", target.Numbers) + "}";
            }

            return BetTarget.OutsideLabel(target.Kind);
        }
    }
}
