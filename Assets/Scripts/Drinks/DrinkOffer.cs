using System.Collections.Generic;
using UnityEngine;

namespace ProjectCR
{
    public enum DrinkEffectKind
    {
        /// <summary>묶음 지급 배율 ×(<see cref="DrinkDefinition"/>).</summary>
        Payout,

        /// <summary>고른 대상 추첨 가중치 +(<see cref="ProbabilityDrinkDefinition"/>). 대상은 마실 때 고른다.</summary>
        Probability,
    }

    /// <summary>
    /// 보상 후보 카드 하나(음료와 한 잔 계수).
    /// 지급 강화는 후보를 만들 때 대상 하나(1열, 빨강 …)가 정해지고 그 대상 배율만 오른다. 숫자 지급 음료만 대상 없이 숫자 묶음 전체.
    /// 확률 강화도 후보를 만들 때 대상 하나가 정해진다(마신 대상이 다시 잘 나온다, <see cref="DrinkRewardController.DrawTarget"/>).
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

        /// <summary>
        /// 후보를 만들 때 정해진 대상. 지급 강화는 배율을 올릴 대상(숫자는 None = 묶음 전체),
        /// 확률 강화는 등장 확률을 올릴 대상 하나.
        /// </summary>
        public BetTarget Target { get; }

        /// <summary>지급 강화: 한 잔 배율(1.5). 확률 강화: 한 잔 가중치 증가 전체(0.26, 고른 대상 수로 나눈다).</summary>
        public decimal Boost { get; }

        public bool IsProbability => Kind == DrinkEffectKind.Probability;

        public DrinkGroup Group => IsProbability ? Probability.Group : Payout.Group;
        public string DisplayName => IsProbability ? Probability.DisplayName : Payout.DisplayName;
        public Color Tint => IsProbability ? Probability.Tint : Payout.Tint;
        public Sprite Icon => IsProbability ? Probability.Icon : Payout.Icon;

        /// <summary>
        /// 지급 음료 후보. 숫자 음료는 대상 없이 묶음 전체(target 무시). 나머지는 target이 그 묶음의 대상 하나여야 한다(아니면 묶음 대표 대상).
        /// </summary>
        public static DrinkOffer ForPayout(DrinkDefinition drink, BetTarget target)
        {
            if (drink.Group == DrinkGroup.Number)
            {
                target = BetTarget.None;
            }
            else if (target.IsNone || !drink.Group.Targets().Contains(target))
            {
                target = drink.Group.Representative();
            }

            return new DrinkOffer(DrinkEffectKind.Payout, drink, null, target, drink.BoostPerStack);
        }

        /// <summary>확률 음료 후보. target은 그 묶음의 대상 하나여야 한다(아니면 묶음의 첫 대상).</summary>
        public static DrinkOffer ForProbability(ProbabilityDrinkDefinition drink, BetTarget target = default)
        {
            List<BetTarget> options = drink.Group.Targets();
            if (!options.Contains(target))
            {
                target = options[0];
            }

            return new DrinkOffer(DrinkEffectKind.Probability, null, drink, target, drink.Boost);
        }

        /// <summary>지급 강화가 묶음 전체에 붙는가(숫자 지급 음료).</summary>
        public bool IsWholeGroup => !IsProbability && Target.IsNone;

        /// <summary>이 음료 묶음에서 대상이 될 수 있는 베팅.</summary>
        public List<BetTarget> TargetOptions() => Group.Targets();

        /// <summary>
        /// 대상 이름 + 포함 숫자(바깥 베팅만): "2열(2, 5, 8 … 35)", "빨강(1, 3, 5 … 36)". 숫자 하나·조합은 이름에 이미 숫자가 있어 그대로.
        /// </summary>
        public static string TargetWithNumbers(BetTarget target, RouletteDefinition definition)
        {
            string label = TargetLabel(target);
            if (target.IsNone || target.IsStraight || target.IsInside || definition == null)
            {
                return label;
            }

            var numbers = new System.Collections.Generic.List<int>();
            for (int number = 1; number <= 36; number++)
            {
                if (target.CoversNumber(number, definition.ColorOfNumber(number)))
                {
                    numbers.Add(number);
                }
            }

            string list = numbers.Count <= 4
                ? string.Join(", ", numbers)
                : $"{numbers[0]}, {numbers[1]}, {numbers[2]} … {numbers[numbers.Count - 1]}";
            return $"{label}({list})";
        }

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
