using System;
using System.Collections.Generic;

namespace ProjectCR
{
    /// <summary>
    /// 확률 효과 하나: 대상(빨강, 2번째 더즌, 7번, {1,2,4,5} 등), 받은 횟수, 가중치 증가 합.
    /// 같은 대상이면 한 효과로 묶어 횟수와 증가량을 더한다(<see cref="ProbabilityDrinkRunState"/>).
    /// 한 잔을 여러 대상에 나눠 받으면 잔마다 증가량이 달라서 계수 × 횟수가 아니라 합을 그대로 들고 있는다.
    /// </summary>
    public readonly struct ProbabilityEffect
    {
        /// <summary>같은 계수로 stacks번 받은 효과.</summary>
        public ProbabilityEffect(BetTarget target, decimal boostPerStack, int stacks)
            : this(target, stacks, boostPerStack * stacks, true)
        {
        }

        private ProbabilityEffect(BetTarget target, int stacks, decimal total, bool _)
        {
            Target = target;
            Stacks = stacks;
            Total = total;
        }

        /// <summary>받은 횟수와 증가량 합으로 만든다.</summary>
        public static ProbabilityEffect WithTotal(BetTarget target, int stacks, decimal total) =>
            new ProbabilityEffect(target, stacks, total, true);

        public BetTarget Target { get; }

        /// <summary>이 대상이 효과를 받은 횟수(잔 수, 집중 강화 개수).</summary>
        public int Stacks { get; }

        /// <summary>가중치 증가 합(0.1 = +10%).</summary>
        public decimal Total { get; }

        /// <summary>한 번에 받은 평균 증가량.</summary>
        public decimal BoostPerStack => Stacks > 0 ? Total / Stacks : 0m;

        /// <summary>이 효과가 대상 칸에 더하는 증가량.</summary>
        public double Increase => (double)Total;
    }

    /// <summary>
    /// 칸별 추첨 가중치와 실제 등장 확률. <b>추첨(SpinResolver)과 UI 미리보기가 모두 이 식을 쓴다.</b>
    ///
    /// 가중치: 일반 칸(0~36) = 기본 가중치 × (1 + 그 칸에 걸린 모든 효과의 증가량 합).
    /// 효과끼리는 곱하지 않고 더하므로 적용 순서와 상관없다. 매번 원본 가중치와 잔 수에서 다시 계산한다.
    ///
    /// 해골은 가중치 목록에 넣지 않는다. 해골 확률은 원본 칸 구성(1/38)으로 고정하고,
    /// 해골이 아니면 일반 칸 가중치로 추첨한다:
    /// P(해골) = s,  P(일반 칸 i) = (1 − s) × w_i / Σw.  강화가 없으면 모든 칸이 1/38.
    /// </summary>
    public static class PocketWeightCalculator
    {
        /// <summary>원본 칸 구성에서 해골이 차지하는 비율(38칸 휠이면 1/38, 해골이 없으면 0).</summary>
        public static double BaseSkullChance(RouletteDefinition definition)
        {
            long total = 0;
            long skull = 0;
            for (int i = 0; i < definition.PocketCount; i++)
            {
                PocketDefinition pocket = definition.GetPocket(i);
                int weight = Math.Max(0, pocket.BaseWeight);
                total += weight;
                if (pocket.IsSkull)
                {
                    skull += weight;
                }
            }

            return total > 0 ? (double)skull / total : 0d;
        }

        /// <summary>칸 인덱스별 가중치. 해골 칸은 0(가중치 추첨에 들어가지 않는다).</summary>
        public static double[] Compute(RouletteDefinition definition, IEnumerable<ProbabilityEffect> effects)
        {
            var weights = new double[definition.PocketCount];
            var list = effects != null ? new List<ProbabilityEffect>(effects) : new List<ProbabilityEffect>();

            for (int i = 0; i < weights.Length; i++)
            {
                PocketDefinition pocket = definition.GetPocket(i);
                if (pocket.IsSkull)
                {
                    continue;
                }

                double increase = 0d;
                foreach (ProbabilityEffect effect in list)
                {
                    if (effect.Stacks > 0 && effect.Target.CoversNumber(pocket.Number, pocket.Color))
                    {
                        increase += effect.Increase;
                    }
                }

                double weight = Math.Max(0, pocket.BaseWeight) * (1d + increase);
                weights[i] = double.IsFinite(weight) ? weight : double.MaxValue / weights.Length;
            }

            return weights;
        }

        /// <summary>칸 인덱스별 실제 등장 확률(공 하나 기준). 합은 1.</summary>
        public static double[] Probabilities(RouletteDefinition definition, IReadOnlyList<double> weights, double skullChance)
        {
            var probabilities = new double[definition.PocketCount];
            double total = 0d;
            int skullIndex = -1;
            for (int i = 0; i < probabilities.Length; i++)
            {
                if (definition.IsSkull(i))
                {
                    skullIndex = i;
                }
                else
                {
                    total += Math.Max(0d, weights[i]);
                }
            }

            double skull = skullIndex >= 0 ? skullChance : 0d;
            if (skullIndex >= 0)
            {
                probabilities[skullIndex] = skull;
            }

            if (total <= 0d)
            {
                return probabilities;
            }

            for (int i = 0; i < probabilities.Length; i++)
            {
                if (i != skullIndex)
                {
                    probabilities[i] = (1d - skull) * Math.Max(0d, weights[i]) / total;
                }
            }

            return probabilities;
        }

        /// <summary>대상 베팅에 포함된 모든 칸의 확률 합(공 하나 기준).</summary>
        public static double ProbabilityOf(RouletteDefinition definition, IReadOnlyList<double> probabilities, BetTarget target)
        {
            if (target.IsNone)
            {
                return 0d;
            }

            double sum = 0d;
            for (int i = 0; i < probabilities.Count; i++)
            {
                PocketDefinition pocket = definition.GetPocket(i);
                if (!pocket.IsSkull && target.CoversNumber(pocket.Number, pocket.Color))
                {
                    sum += probabilities[i];
                }
            }

            return sum;
        }
    }
}
