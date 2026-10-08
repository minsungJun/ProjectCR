using System;

namespace ProjectCR
{
    /// <summary>
    /// 회전 한 번에 쓰는 설정. 매 회전 정의 에셋에서 <b>새로 복사</b>한다.
    ///
    /// 아이템(공 추가, 칸 가중치 변경, 지급 배율 변경)과 음료 묶음 배율·확률 효과는 이 복사본만 고친다.
    /// 그래서 RouletteDefinition·GameBalance 에셋이 런타임에 바뀌는 일이 없다.
    /// 회전 시작 때 만든 복사본으로 그 회전의 모든 공을 추첨·정산하므로, 회전 중에 음료 상태가 바뀌어도 영향이 없다.
    /// </summary>
    public sealed class SpinConfig
    {
        private int ballCount;
        private readonly int[] payouts = new int[Enum.GetValues(typeof(PayoutClass)).Length];
        private readonly decimal[] groupBoosts = new decimal[DrinkGroups.Count];

        private SpinConfig(RouletteDefinition definition, int ballCount, double[] weights, double skullChance)
        {
            Definition = definition;
            BallCount = ballCount;
            Weights = weights;
            SkullChance = skullChance;
            for (int i = 0; i < groupBoosts.Length; i++)
            {
                groupBoosts[i] = 1m;
            }
        }

        public RouletteDefinition Definition { get; }

        public int BallCount
        {
            get => ballCount;
            set => ballCount = Math.Max(1, value);
        }

        /// <summary>
        /// 칸 인덱스 기준 일반 칸 추첨 가중치. 음수는 0으로 친다. <b>해골 칸 값은 쓰지 않는다</b>(해골은 <see cref="SkullChance"/>).
        /// 기본은 원본 가중치, 확률 음료가 있으면 <see cref="PocketWeightCalculator.Compute"/> 결과.
        /// </summary>
        public double[] Weights { get; }

        /// <summary>공 하나가 해골에 들어갈 확률. 원본 칸 구성(38칸이면 1/38)으로 고정하고 음료가 바꾸지 않는다.</summary>
        public double SkullChance { get; set; }

        /// <summary>칸 인덱스별 실제 등장 확률(공 하나 기준). 추첨과 같은 식.</summary>
        public double[] PocketProbabilities() => PocketWeightCalculator.Probabilities(Definition, Weights, SkullChance);

        /// <summary>대상 베팅에 포함된 칸들의 등장 확률 합(공 하나 기준).</summary>
        public double ProbabilityOf(BetTarget target) => PocketWeightCalculator.ProbabilityOf(Definition, PocketProbabilities(), target);

        /// <summary>일반 칸 가중치를 통째로 바꾼다(확률 효과). 길이는 칸 수와 같아야 한다.</summary>
        public void SetWeights(double[] weights)
        {
            if (weights == null || weights.Length != Weights.Length)
            {
                throw new ArgumentException("가중치 수가 칸 수와 다르다.");
            }

            Array.Copy(weights, Weights, Weights.Length);
        }

        /// <summary>숫자 하나 베팅의 원금 포함 총 지급 배수.</summary>
        public int StraightPayout
        {
            get => payouts[(int)PayoutClass.Straight];
            set => payouts[(int)PayoutClass.Straight] = Math.Max(0, value);
        }

        /// <summary>더즌·컬럼의 원금 포함 총 지급 배수.</summary>
        public int DozenColumnPayout
        {
            get => payouts[(int)PayoutClass.DozenColumn];
            set => payouts[(int)PayoutClass.DozenColumn] = Math.Max(0, value);
        }

        /// <summary>빨강·검정, 홀·짝, 로우·하이의 원금 포함 총 지급 배수.</summary>
        public int EvenMoneyPayout
        {
            get => payouts[(int)PayoutClass.EvenMoney];
            set => payouts[(int)PayoutClass.EvenMoney] = Math.Max(0, value);
        }

        /// <summary>배수 묶음별 원금 포함 총 지급 배수.</summary>
        public int PayoutOf(PayoutClass payoutClass) => payouts[(int)payoutClass];

        /// <summary>배수 묶음 하나의 배수를 바꾼다. 아이템이 쓰는 길.</summary>
        public void SetPayout(PayoutClass payoutClass, int multiplier) => payouts[(int)payoutClass] = Math.Max(0, multiplier);

        /// <summary>이 베팅의 <b>기본</b> 원금 포함 총 지급 배수(음료 적용 전).</summary>
        public int PayoutFor(BetTarget target) => target.IsNone ? 0 : payouts[(int)target.PayoutClass];

        /// <summary>음료 묶음 배율. 기본 1. GameFlowController.BuildSpinConfig가 이번 게임의 음료 잔 수로 채운다.</summary>
        public decimal GroupBoost(DrinkGroup group) => groupBoosts[(int)group];

        public void SetGroupBoost(DrinkGroup group, decimal multiplier) => groupBoosts[(int)group] = Math.Max(0m, multiplier);

        /// <summary>
        /// 이 베팅이 맞았을 때의 최종 배수 = 기본 배수 × 묶음 배율. UI 미리보기와 정산이 같이 쓴다.
        /// 기본 배수와 잔 수에서 매번 새로 계산하므로 이전 결과에 겹쳐 곱하지 않는다.
        /// </summary>
        public decimal MultiplierFor(BetTarget target)
        {
            return target.IsNone ? 0m : PayoutFor(target) * GroupBoost(DrinkGroups.Of(target.Kind));
        }

        public static SpinConfig Create(RouletteDefinition definition, GameBalance balance)
        {
            double[] weights = PocketWeightCalculator.Compute(definition, null);
            var config = new SpinConfig(definition, balance.BallCount, weights, PocketWeightCalculator.BaseSkullChance(definition));
            foreach (PayoutClass payoutClass in (PayoutClass[])Enum.GetValues(typeof(PayoutClass)))
            {
                config.SetPayout(payoutClass, balance.TotalPayoutOf(payoutClass));
            }

            return config;
        }
    }
}
