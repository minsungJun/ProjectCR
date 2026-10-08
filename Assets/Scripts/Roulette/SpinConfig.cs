using System;

namespace ProjectCR
{
    /// <summary>
    /// 회전 한 번에 쓰는 설정. 매 회전 정의 에셋에서 <b>새로 복사</b>한다.
    ///
    /// 아이템(공 추가, 칸 가중치 변경, 지급 배율 변경)은 이 복사본만 고친다.
    /// 그래서 RouletteDefinition·GameBalance 에셋이 런타임에 바뀌는 일이 없다.
    /// </summary>
    public sealed class SpinConfig
    {
        private int ballCount;
        private readonly int[] payouts = new int[Enum.GetValues(typeof(PayoutClass)).Length];

        private SpinConfig(RouletteDefinition definition, int ballCount, int[] weights)
        {
            Definition = definition;
            BallCount = ballCount;
            Weights = weights;
        }

        public RouletteDefinition Definition { get; }

        public int BallCount
        {
            get => ballCount;
            set => ballCount = Math.Max(1, value);
        }

        /// <summary>칸 인덱스 기준 추첨 가중치. 음수는 0으로 친다.</summary>
        public int[] Weights { get; }

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

        /// <summary>이 베팅이 맞았을 때의 원금 포함 총 지급 배수. UI 미리보기와 정산이 같이 쓴다.</summary>
        public int PayoutFor(BetTarget target) => target.IsNone ? 0 : payouts[(int)target.PayoutClass];

        public static SpinConfig Create(RouletteDefinition definition, GameBalance balance)
        {
            var weights = new int[definition.PocketCount];
            for (int i = 0; i < weights.Length; i++)
            {
                weights[i] = definition.GetPocket(i).BaseWeight;
            }

            var config = new SpinConfig(definition, balance.BallCount, weights);
            foreach (PayoutClass payoutClass in (PayoutClass[])Enum.GetValues(typeof(PayoutClass)))
            {
                config.SetPayout(payoutClass, balance.TotalPayoutOf(payoutClass));
            }

            return config;
        }
    }
}
