using System;
using System.Linq;
using NUnit.Framework;

namespace ProjectCR.Tests
{
    /// <summary>
    /// 확률 강화 규칙: 가중치 = 기본 × (1 + 겹치는 효과 증가량 합), 해골은 1/38 고정,
    /// P(일반 칸 i) = (37/38) × w_i / Σw. 추첨과 미리보기가 같은 식을 쓴다.
    /// </summary>
    public class ProbabilityRulesTests
    {
        private const double Eps = 1e-12;
        private static readonly BetTarget Red = BetTarget.Outside(BetKind.Red);
        private static readonly BetTarget Low = BetTarget.Outside(BetKind.Low);
        private static readonly BetTarget Odd = BetTarget.Outside(BetKind.Odd);
        private static readonly BetTarget Dozen2 = BetTarget.Outside(BetKind.Dozen2);

        private TestAssets assets;
        private RouletteDefinition definition;
        private GameFlowController flow;

        [SetUp]
        public void SetUp()
        {
            assets = new TestAssets();
            definition = assets.Definition();
            flow = new GameFlowController(definition, assets.Balance(), seed: 11);
        }

        [TearDown]
        public void TearDown() => assets.DestroyAll();

        private double P(int number) => flow.BuildSpinConfig().PocketProbabilities()[definition.IndexOfPocketFor(number)];

        private double Weight(int number) => flow.BuildSpinConfig().Weights[definition.IndexOfNumber(number)];

        [Test]
        public void NoEffects_EveryPocketIsOneIn38()
        {
            double[] p = flow.BuildSpinConfig().PocketProbabilities();

            Assert.AreEqual(38, p.Length);
            foreach (double value in p)
            {
                Assert.AreEqual(1d / 38d, value, Eps);
            }
        }

        [Test]
        public void Skull_StaysOneIn38_AndTotalIsOne_WithEffects()
        {
            flow.Probabilities.SetStacks(Red, 0.1m, 10);
            flow.Probabilities.SetStacks(BetTarget.Straight(7), 0.3m, 5);
            flow.Probabilities.SetStacks(BetTarget.Inside(BetKind.Corner, 1, 2, 4, 5), 0.2m, 3);

            double[] p = flow.BuildSpinConfig().PocketProbabilities();

            Assert.AreEqual(1d / 38d, p[definition.IndexOfSkull()], Eps);
            Assert.AreEqual(1d, p.Sum(), 1e-9);
            Assert.IsTrue(flow.BuildSpinConfig().Weights.Where((w, i) => !definition.IsSkull(i)).All(w => w > 0 && double.IsFinite(w)));
        }

        [Test]
        public void SameEffectStacks_AddUp()
        {
            flow.Probabilities.SetStacks(Red, 0.1m, 5);
            Assert.AreEqual(1.5d, Weight(32), Eps, "0.1 × 5잔 = ×1.5");

            flow.Probabilities.SetStacks(Red, 0.1m, 10);
            Assert.AreEqual(2d, Weight(32), Eps, "0.1 × 10잔 = ×2 (곱해 가지 않는다)");
            Assert.AreEqual(1d, Weight(2), Eps, "검정 칸은 그대로.");
        }

        [Test]
        public void OverlappingEffects_AddUp_AndOrderDoesNotMatter()
        {
            // 7번: 빨강 2잔(+20%) + 1–18 1잔(+10%) + 7번 1잔(+30%) = ×1.6
            flow.Probabilities.SetStacks(Red, 0.1m, 2);
            flow.Probabilities.SetStacks(Low, 0.1m, 1);
            flow.Probabilities.SetStacks(BetTarget.Straight(7), 0.3m, 1);
            Assert.AreEqual(1.6d, Weight(7), 1e-9);
            double[] forward = flow.BuildSpinConfig().PocketProbabilities();

            var reversed = new GameFlowController(definition, flow.GameBalance, seed: 11);
            reversed.Probabilities.SetStacks(BetTarget.Straight(7), 0.3m, 1);
            reversed.Probabilities.SetStacks(Low, 0.1m, 1);
            reversed.Probabilities.SetStacks(Red, 0.1m, 2);
            CollectionAssert.AreEqual(forward, reversed.BuildSpinConfig().PocketProbabilities(), new DoubleComparer(1e-12));
        }

        [Test]
        public void ZeroAndSkull_AreNotInOutsideTargets()
        {
            flow.Probabilities.SetStacks(Red, 0.1m, 10);
            flow.Probabilities.SetStacks(Odd, 0.1m, 10);
            flow.Probabilities.SetStacks(Low, 0.1m, 10);
            flow.Probabilities.SetStacks(Dozen2, 0.15m, 10);

            Assert.AreEqual(1d, Weight(0), Eps, "0은 색깔·홀짝·하이로우·더즌에 들지 않는다.");
            Assert.AreEqual(0d, flow.BuildSpinConfig().Weights[definition.IndexOfSkull()], "해골은 가중치 목록에 없다.");

            flow.Probabilities.SetStacks(BetTarget.Straight(0), 0.3m, 1);
            Assert.AreEqual(1.3d, Weight(0), Eps, "0은 숫자 하나 대상으로는 강화된다.");
        }

        [Test]
        public void TargetProbability_SumsItsPockets()
        {
            SpinConfig plain = flow.BuildSpinConfig();
            Assert.AreEqual(18d / 38d, plain.ProbabilityOf(Red), Eps);
            Assert.AreEqual(12d / 38d, plain.ProbabilityOf(Dozen2), Eps);
            Assert.AreEqual(4d / 38d, plain.ProbabilityOf(BetTarget.Inside(BetKind.Corner, 1, 2, 4, 5)), Eps);

            // 빨강 10잔(×2): 빨강 가중치 36, 나머지 일반 칸 19 → (37/38) × 36/55
            flow.Probabilities.SetStacks(Red, 0.1m, 10);
            Assert.AreEqual(37d / 38d * 36d / 55d, flow.BuildSpinConfig().ProbabilityOf(Red), 1e-12);
        }

        [Test]
        public void Preview_DoesNotChangeState()
        {
            flow.Probabilities.SetStacks(Red, 0.1m, 1);
            var preview = flow.Probabilities.WithExtraStack(Red, 0.1m);

            Assert.AreEqual(2, preview.Single().Stacks);
            Assert.AreEqual(1, flow.Probabilities.StacksOf(Red), "미리보기만으로는 잔 수가 늘지 않는다.");
        }

        [Test]
        public void Draws_FollowWeights_WithinTolerance()
        {
            // 7번 ×4(1 + 0.3 × 10): 기대 확률 (37/38) × 4/40 ≈ 0.0974.
            flow.Probabilities.SetStacks(BetTarget.Straight(7), 0.3m, 10);
            SpinConfig config = flow.BuildSpinConfig();
            double expected = config.PocketProbabilities()[definition.IndexOfNumber(7)];
            var rng = new Random(2026);
            int draws = 60000, seven = 0, skull = 0;
            for (int i = 0; i < draws; i++)
            {
                int index = SpinResolver.PickBall(config, rng);
                seven += index == definition.IndexOfNumber(7) ? 1 : 0;
                skull += definition.IsSkull(index) ? 1 : 0;
            }

            // 표준오차의 약 5배를 허용(고정 시드라 매번 같다).
            Assert.AreEqual(expected, (double)seven / draws, 5 * Math.Sqrt(expected * (1 - expected) / draws));
            Assert.AreEqual(1d / 38d, (double)skull / draws, 5 * Math.Sqrt((1d / 38d) * (37d / 38d) / draws));
        }

        [Test]
        public void SpinUsesEffectsAtStart_ForEveryBall()
        {
            flow.Probabilities.SetStacks(Red, 0.1m, 10);
            flow.ExtraBallCount = 2;
            flow.PlaceBet(Red, 1);
            flow.TryBeginSpin(out ActiveSpin spin);

            double[] atStart = spin.Config.PocketProbabilities();
            flow.Probabilities.SetStacks(Red, 0.1m, 0);
            CollectionAssert.AreEqual(atStart, spin.Config.PocketProbabilities(), "회전 중에 바뀐 상태는 이미 시작한 회전에 영향이 없다.");
            Assert.AreEqual(3, spin.Result.Balls.Count);
        }

        [Test]
        public void SameSeed_SameEffects_SameResults()
        {
            var a = new GameFlowController(definition, flow.GameBalance, seed: 77);
            var b = new GameFlowController(definition, flow.GameBalance, seed: 77);
            a.Probabilities.SetStacks(Dozen2, 0.15m, 4);
            b.Probabilities.SetStacks(Dozen2, 0.15m, 4);

            for (int i = 0; i < 5; i++)
            {
                a.PlaceBet(Red, 1);
                b.PlaceBet(Red, 1);
                a.TryBeginSpin(out ActiveSpin sa);
                b.TryBeginSpin(out ActiveSpin sb);
                Assert.AreEqual(sa.Result.Balls[0].PocketIndex, sb.Result.Balls[0].PocketIndex);
                a.CompleteSpin(sa.Id, out _);
                b.CompleteSpin(sb.Id, out _);
            }
        }

        [Test]
        public void NewRun_HasNoProbabilityEffects()
        {
            flow.Probabilities.SetStacks(Red, 0.1m, 3);
            var restarted = new GameFlowController(definition, flow.GameBalance, seed: 0);

            Assert.AreEqual(0, restarted.Probabilities.TotalStacks);
            Assert.AreEqual(18d / 38d, restarted.BuildSpinConfig().ProbabilityOf(Red), Eps);
            Assert.AreEqual(1, definition.GetPocket(definition.IndexOfNumber(32)).BaseWeight, "원본 가중치는 그대로.");
        }

        private sealed class DoubleComparer : System.Collections.IComparer
        {
            private readonly double tolerance;

            public DoubleComparer(double tolerance) => this.tolerance = tolerance;

            public int Compare(object x, object y)
            {
                double a = (double)x, b = (double)y;
                return Math.Abs(a - b) <= tolerance ? 0 : a.CompareTo(b);
            }
        }
    }
}
