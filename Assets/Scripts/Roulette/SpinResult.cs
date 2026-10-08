using System.Collections.Generic;

namespace ProjectCR
{
    public readonly struct BallResult
    {
        public BallResult(int ballIndex, int pocketIndex, int number, PocketColor color)
        {
            BallIndex = ballIndex;
            PocketIndex = pocketIndex;
            Number = number;
            Color = color;
        }

        public int BallIndex { get; }

        /// <summary>RouletteDefinition 칸 인덱스(휠 위 순서). 3D 안착 위치도 이 값으로 찾는다.</summary>
        public int PocketIndex { get; }

        public int Number { get; }
        public PocketColor Color { get; }

        public static BallResult FromPocket(RouletteDefinition definition, int ballIndex, int pocketIndex)
        {
            PocketDefinition pocket = definition.GetPocket(pocketIndex);
            return new BallResult(ballIndex, pocketIndex, pocket.Number, pocket.Color);
        }
    }

    /// <summary>
    /// 회전 한 번의 확정 결과. 공 하나에 BallResult 하나.
    /// 정산은 공의 실제 3D 위치가 아니라 이 데이터만 본다.
    /// </summary>
    public sealed class SpinResult
    {
        private readonly BallResult[] balls;

        public SpinResult(IEnumerable<BallResult> balls)
        {
            this.balls = new List<BallResult>(balls).ToArray();
        }

        public IReadOnlyList<BallResult> Balls => balls;

        /// <summary>번호를 직접 지정해 결과를 만든다. 테스트와 연출 검증용.</summary>
        public static SpinResult FromNumbers(RouletteDefinition definition, params int[] numbers)
        {
            var list = new List<BallResult>(numbers.Length);
            for (int i = 0; i < numbers.Length; i++)
            {
                int pocketIndex = definition.IndexOfNumber(numbers[i]);
                if (pocketIndex < 0)
                {
                    throw new System.ArgumentException($"휠에 없는 번호다: {numbers[i]}");
                }

                list.Add(BallResult.FromPocket(definition, i, pocketIndex));
            }

            return new SpinResult(list);
        }
    }
}
