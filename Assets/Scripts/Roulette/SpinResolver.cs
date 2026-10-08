using System;
using System.Collections.Generic;

namespace ProjectCR
{
    /// <summary>
    /// 공별 당첨 칸을 추첨한다. 3D 연출과 무관하게 결과를 먼저 정하고, 연출은 이 결과에 맞춰 멈춘다.
    /// 공마다 독립 추첨이라 같은 칸에 여러 공이 걸릴 수 있다.
    /// </summary>
    public static class SpinResolver
    {
        public static SpinResult Resolve(SpinConfig config, Random rng)
        {
            var balls = new List<BallResult>(config.BallCount);
            for (int i = 0; i < config.BallCount; i++)
            {
                int pocketIndex = PickPocket(config.Weights, rng);
                balls.Add(BallResult.FromPocket(config.Definition, i, pocketIndex));
            }

            return new SpinResult(balls);
        }

        /// <summary>가중치 비례 추첨. 음수 가중치는 0으로 본다. 합이 0이면 예외.</summary>
        public static int PickPocket(IReadOnlyList<int> weights, Random rng)
        {
            long total = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                total += Math.Max(0, weights[i]);
            }

            if (total <= 0)
            {
                throw new InvalidOperationException("추첨 가중치 합이 0이다.");
            }

            long roll = total <= int.MaxValue
                ? rng.Next((int)total)
                : Math.Min(total - 1, (long)(rng.NextDouble() * total));

            for (int i = 0; i < weights.Count; i++)
            {
                int weight = Math.Max(0, weights[i]);
                if (roll < weight)
                {
                    return i;
                }

                roll -= weight;
            }

            // 위 루프에서 반드시 끝난다. 부동소수 경로의 안전망.
            for (int i = weights.Count - 1; i >= 0; i--)
            {
                if (weights[i] > 0)
                {
                    return i;
                }
            }

            throw new InvalidOperationException("추첨 가중치 합이 0이다.");
        }

        /// <summary>
        /// 이미 추첨된 결과의 공 i를 numbers[i] 번호로 바꾼다. -1이나 휠에 없는 번호는 건너뛰고,
        /// <see cref="RouletteDefinition.SkullNumber"/>(-2)는 해골 칸으로 바꾼다.
        ///
        /// 디버그 지정 번호가 쓰는 길이다. 추첨을 먼저 하고 덮어쓰므로 난수 소비 순서는 그대로다.
        /// "결과 변환" 아이템이 들어올 자리도 여기다.
        /// </summary>
        public static SpinResult OverrideNumbers(SpinResult original, RouletteDefinition definition, IReadOnlyList<int> numbers)
        {
            if (numbers == null || numbers.Count == 0)
            {
                return original;
            }

            var balls = new List<BallResult>(original.Balls);
            for (int i = 0; i < balls.Count && i < numbers.Count; i++)
            {
                int pocketIndex = definition.IndexOfPocketFor(numbers[i]);
                if (pocketIndex >= 0)
                {
                    balls[i] = BallResult.FromPocket(definition, i, pocketIndex);
                }
            }

            return new SpinResult(balls);
        }
    }
}
