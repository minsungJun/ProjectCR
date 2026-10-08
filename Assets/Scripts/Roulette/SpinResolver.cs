using System;
using System.Collections.Generic;

namespace ProjectCR
{
    /// <summary>
    /// 공별 당첨 칸을 추첨한다. 3D 연출과 무관하게 결과를 먼저 정하고, 연출은 이 결과에 맞춰 멈춘다.
    /// 공마다 독립 추첨이라 같은 칸에 여러 공이 걸릴 수 있다. 모든 공이 같은 회전 설정(같은 확률)을 쓴다.
    ///
    /// 공 하나의 추첨(<see cref="PickBall"/>):
    /// 1. 해골 여부를 <see cref="SpinConfig.SkullChance"/>(1/38)로 먼저 정한다.
    /// 2. 해골이 아니면 일반 칸(0~36)만 가중치로 추첨한다. 해골은 가중치 목록에 들어가지 않는다.
    /// 그래서 숫자 확률을 올려도 해골 위험은 그대로다. 확률식은 <see cref="PocketWeightCalculator.Probabilities"/>와 같다.
    /// </summary>
    public static class SpinResolver
    {
        public static SpinResult Resolve(SpinConfig config, Random rng)
        {
            var balls = new List<BallResult>(config.BallCount);
            for (int i = 0; i < config.BallCount; i++)
            {
                int pocketIndex = PickBall(config, rng);
                balls.Add(BallResult.FromPocket(config.Definition, i, pocketIndex));
            }

            return new SpinResult(balls);
        }

        /// <summary>
        /// 해골 방패: 해골이 나온 공마다 방패를 하나 쓰고 <b>그 공만</b> 같은 설정으로 다시 뽑는다(해골 확률도 그대로 1/38).
        /// 다시 해골이면 남은 방패로 또 막는다. 방패가 없으면 해골로 남는다. 다른 공은 그대로다.
        /// </summary>
        public static SpinResult ShieldSkulls(SpinResult result, SpinConfig config, Random rng, int shields, out int used)
        {
            used = 0;
            if (shields <= 0 || !result.HasSkull)
            {
                return result;
            }

            var balls = new List<BallResult>(result.Balls);
            for (int i = 0; i < balls.Count; i++)
            {
                while (balls[i].IsSkull && used < shields)
                {
                    used++;
                    balls[i] = BallResult.FromPocket(config.Definition, i, PickBall(config, rng));
                }
            }

            return new SpinResult(balls);
        }

        /// <summary>공 하나의 칸 인덱스. 해골이 있는 휠이면 난수를 늘 두 번(해골 판정, 일반 칸) 써서 소비 순서가 결과와 무관하다.</summary>
        public static int PickBall(SpinConfig config, Random rng)
        {
            int skullIndex = config.Definition.IndexOfSkull();
            if (skullIndex >= 0)
            {
                bool skull = rng.NextDouble() < config.SkullChance;
                int number = PickNumberPocket(config.Weights, skullIndex, rng);
                return skull ? skullIndex : number;
            }

            return PickNumberPocket(config.Weights, -1, rng);
        }

        /// <summary>해골 칸을 뺀 가중치 비례 추첨. 음수 가중치는 0으로 본다. 합이 0이면 예외.</summary>
        private static int PickNumberPocket(IReadOnlyList<double> weights, int skullIndex, Random rng)
        {
            double total = 0d;
            int last = -1;
            for (int i = 0; i < weights.Count; i++)
            {
                if (i != skullIndex && weights[i] > 0d)
                {
                    total += weights[i];
                    last = i;
                }
            }

            if (total <= 0d)
            {
                throw new InvalidOperationException("추첨 가중치 합이 0이다.");
            }

            double roll = rng.NextDouble() * total;
            for (int i = 0; i < weights.Count; i++)
            {
                if (i == skullIndex || weights[i] <= 0d)
                {
                    continue;
                }

                if (roll < weights[i])
                {
                    return i;
                }

                roll -= weights[i];
            }

            // 부동소수 오차로 끝까지 왔으면 마지막 양수 칸.
            return last;
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
