using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ProjectCR
{
    /// <summary>
    /// 태블릿 "마신 음료" 화면의 한 줄: 대상, 지금 효과, 잔 수.
    /// 같은 대상을 여러 번 마셨으면 한 줄로 합친다.
    /// </summary>
    public readonly struct DrinkEffectLine
    {
        public DrinkEffectLine(DrinkEffectKind kind, string title, string effect, int stacks, Color tint)
        {
            Kind = kind;
            Title = title;
            Effect = effect;
            Stacks = stacks;
            Tint = tint;
        }

        public DrinkEffectKind Kind { get; }

        /// <summary>대상("빨강 등장", "13–24 지급", "숫자 전체 지급").</summary>
        public string Title { get; }

        /// <summary>지금 효과("가중치 +40% · 현재 확률 48.2%", "음료 배율 ×2.25").</summary>
        public string Effect { get; }

        /// <summary>마신 잔 수.</summary>
        public int Stacks { get; }

        /// <summary>잔 그림 색(그 묶음 음료 색).</summary>
        public Color Tint { get; }
    }

    /// <summary>
    /// 이번 게임에서 <b>실제로 마신</b> 음료 효과만 모은다(후보·상점·강화·예상치는 넣지 않는다). 상태는 읽기만 한다.
    /// - 확률 음료: 대상별 가중치 증가 합과 지금 실제 등장 확률(정산과 같은 BuildSpinConfig().ProbabilityOf). 가중치 +40%는 확률 +40%p가 아니다.
    /// - 지급 음료: 대상(숫자는 묶음 전체)별 음료 배율만. 상점 집중·교차 강화는 더하지 않는다.
    /// 순서: 확률 음료 → 지급 음료, 각각 처음 마신 순서.
    /// </summary>
    public static class DrinkEffectSummary
    {
        public static List<DrinkEffectLine> For(GameFlowController flow)
        {
            var lines = new List<DrinkEffectLine>();
            if (flow == null)
            {
                return lines;
            }

            SpinConfig config = flow.BuildSpinConfig();
            foreach (ProbabilityEffect effect in flow.Probabilities.Effects)
            {
                if (effect.Stacks <= 0)
                {
                    continue;
                }

                BetTarget target = effect.Target;
                lines.Add(new DrinkEffectLine(
                    DrinkEffectKind.Probability,
                    $"{DrinkOffer.TargetLabel(target)} 등장",
                    $"가중치 +{Percent(effect.Total)} · 현재 확률 {NumberText.Percent(config.ProbabilityOf(target))}",
                    effect.Stacks,
                    DrinkColors.Of(DrinkGroups.Of(target.Kind))));
            }

            foreach (BetTarget target in flow.Drinks.Targets)
            {
                int stacks = flow.Drinks.StacksOf(target);
                if (stacks <= 0)
                {
                    continue;
                }

                lines.Add(new DrinkEffectLine(
                    DrinkEffectKind.Payout,
                    $"{DrinkOffer.TargetLabel(target)} 지급",
                    $"음료 배율 ×{NumberText.Multiplier(flow.Drinks.MultiplierOf(target))}",
                    stacks,
                    DrinkColors.Of(DrinkGroups.Of(target.Kind))));
            }

            for (int i = 0; i < DrinkGroups.Count; i++)
            {
                var group = (DrinkGroup)i;
                int stacks = flow.Drinks.GroupStacksOf(group);
                if (stacks <= 0)
                {
                    continue;
                }

                lines.Add(new DrinkEffectLine(
                    DrinkEffectKind.Payout,
                    $"{group.ToKorean()} 전체 지급",
                    $"음료 배율 ×{NumberText.Multiplier(flow.Drinks.GroupMultiplierOf(group))}",
                    stacks,
                    DrinkColors.Of(group)));
            }

            return lines;
        }

        /// <summary>마신 음료가 하나라도 있는가(태블릿 [음료] 버튼을 켤지).</summary>
        public static bool HasAny(GameFlowController flow) =>
            flow != null && (flow.Drinks.TotalStacks > 0 || flow.Probabilities.TotalStacks > 0);

        private static string Percent(decimal value) => (value * 100m).ToString("0.#", CultureInfo.InvariantCulture) + "%";
    }
}
