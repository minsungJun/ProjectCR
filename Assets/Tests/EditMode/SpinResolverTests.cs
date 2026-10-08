using System;
using NUnit.Framework;

namespace ProjectCR.Tests
{
    public class SpinResolverTests
    {
        private TestAssets assets;
        private RouletteDefinition definition;
        private GameBalance balance;

        [SetUp]
        public void SetUp()
        {
            assets = new TestAssets();
            definition = assets.Definition();
            balance = assets.Balance();
        }

        [TearDown]
        public void TearDown() => assets.DestroyAll();

        [Test]
        public void SameSeed_GivesSameSequence()
        {
            SpinConfig config = SpinConfig.Create(definition, balance);
            var a = new Random(1234);
            var b = new Random(1234);

            for (int i = 0; i < 200; i++)
            {
                Assert.AreEqual(
                    SpinResolver.Resolve(config, a).Balls[0].Number,
                    SpinResolver.Resolve(config, b).Balls[0].Number,
                    $"{i}번째 회전");
            }
        }

        [Test]
        public void Result_HasOneEntryPerBall_WithConsistentNumberAndColor()
        {
            SpinConfig config = SpinConfig.Create(definition, balance);
            config.BallCount = 3;

            SpinResult result = SpinResolver.Resolve(config, new Random(7));

            Assert.AreEqual(3, result.Balls.Count);
            for (int i = 0; i < 3; i++)
            {
                BallResult ball = result.Balls[i];
                Assert.AreEqual(i, ball.BallIndex);
                Assert.AreEqual(definition.GetPocket(ball.PocketIndex).Number, ball.Number);
                Assert.AreEqual(definition.GetPocket(ball.PocketIndex).Color, ball.Color);
            }
        }

        [Test]
        public void ZeroWeightPocket_IsNeverPicked()
        {
            SpinConfig config = SpinConfig.Create(definition, balance);
            int banned = definition.IndexOfNumber(17);
            config.Weights[banned] = 0;
            var rng = new Random(99);

            for (int i = 0; i < 20000; i++)
            {
                Assert.AreNotEqual(banned, SpinResolver.PickBall(config, rng));
            }
        }

        [Test]
        public void SingleWeightedPocket_IsAlwaysPicked()
        {
            var weights = new int[definition.PocketCount];
            weights[definition.IndexOfNumber(0)] = 5;
            var rng = new Random(3);

            for (int i = 0; i < 1000; i++)
            {
                Assert.AreEqual(0, SpinResolver.PickPocket(weights, rng));
            }
        }

        [Test]
        public void AllZeroWeights_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => SpinResolver.PickPocket(new int[37], new Random(1)));
        }

        [Test]
        public void EqualWeights_AreRoughlyUniform()
        {
            SpinConfig config = SpinConfig.Create(definition, balance);
            var counts = new int[definition.PocketCount];
            var rng = new Random(2026);
            int draws = definition.PocketCount * 2000;

            for (int i = 0; i < draws; i++)
            {
                counts[SpinResolver.PickBall(config, rng)]++;
            }

            for (int i = 0; i < counts.Length; i++)
            {
                Assert.That(counts[i], Is.InRange(1700, 2300), $"칸 {i}");
            }
        }

        [Test]
        public void OverrideNumbers_ReplacesOnlyGivenBalls()
        {
            SpinConfig config = SpinConfig.Create(definition, balance);
            config.BallCount = 2;
            SpinResult original = SpinResolver.Resolve(config, new Random(5));

            SpinResult forced = SpinResolver.OverrideNumbers(original, definition, new[] { 0, -1 });

            Assert.AreEqual(0, forced.Balls[0].Number);
            Assert.AreEqual(definition.IndexOfNumber(0), forced.Balls[0].PocketIndex);
            Assert.AreEqual(original.Balls[1].Number, forced.Balls[1].Number);
        }

        [Test]
        public void SpinConfig_CopiesWeights_SoDefinitionStaysUntouched()
        {
            SpinConfig config = SpinConfig.Create(definition, balance);
            config.Weights[0] = 100;
            config.BallCount = 4;
            config.StraightPayout = 72;

            Assert.AreEqual(1, definition.GetPocket(0).BaseWeight);
            Assert.AreEqual(1, balance.BallCount);
            Assert.AreEqual(36, balance.StraightTotalPayout);
            Assert.AreEqual(1, SpinConfig.Create(definition, balance).Weights[0]);
        }
    }
}
