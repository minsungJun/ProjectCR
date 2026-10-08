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
        private int payoutMultiplier;

        private SpinConfig(RouletteDefinition definition, int ballCount, int payoutMultiplier, int[] weights)
        {
            Definition = definition;
            BallCount = ballCount;
            PayoutMultiplier = payoutMultiplier;
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

        /// <summary>숫자 베팅의 원금 포함 총 지급 배수.</summary>
        public int PayoutMultiplier
        {
            get => payoutMultiplier;
            set => payoutMultiplier = Math.Max(0, value);
        }

        public static SpinConfig Create(RouletteDefinition definition, GameBalance balance)
        {
            var weights = new int[definition.PocketCount];
            for (int i = 0; i < weights.Length; i++)
            {
                weights[i] = definition.GetPocket(i).BaseWeight;
            }

            return new SpinConfig(definition, balance.BallCount, balance.StraightTotalPayout, weights);
        }
    }
}
