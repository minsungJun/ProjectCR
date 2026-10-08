using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ProjectCR.Tests
{
    /// <summary>
    /// 안쪽 베팅: 스플릿 18배, 스트리트·트리오 12배, 코너·퍼스트 포 9배, 식스 라인 6배(원금 포함).
    /// 걸 수 있는 조합은 번호판에서 실제로 붙어 있는 것뿐이고, 해골은 포함되지 않는다.
    /// </summary>
    public class InsideBetTests
    {
        private const int Skull = RouletteDefinition.SkullNumber;
        private const long Max = 999_999_999_999;

        private TestAssets assets;
        private RouletteDefinition definition;
        private GameBalance balance;
        private GameFlowController flow;

        [SetUp]
        public void SetUp()
        {
            assets = new TestAssets();
            definition = assets.Definition();
            balance = assets.Balance();
            flow = new GameFlowController(definition, balance, seed: 13);
        }

        [TearDown]
        public void TearDown() => assets.DestroyAll();

        private static int Count(BetKind kind) => InsideBets.All.Count(s => s.Target.Kind == kind);

        // ---------- 조합 목록 ----------

        [Test]
        public void Table_Has108InsideBets_WithStandardCounts()
        {
            Assert.AreEqual(108, InsideBets.All.Count);
            Assert.AreEqual(60, Count(BetKind.Split), "가로 33 + 세로 24 + 0과 1·2·3");
            Assert.AreEqual(14, Count(BetKind.Street), "12줄 + 트리오 2");
            Assert.AreEqual(23, Count(BetKind.Corner), "22 + 퍼스트 포");
            Assert.AreEqual(11, Count(BetKind.SixLine));

            Assert.AreEqual(108, InsideBets.All.Select(s => s.Target).Distinct().Count(), "같은 조합이 두 번 나오지 않는다.");
        }

        [Test]
        public void EachKind_CoversRightNumberCount()
        {
            var expected = new Dictionary<BetKind, int>
            {
                { BetKind.Split, 2 }, { BetKind.Street, 3 }, { BetKind.Corner, 4 }, { BetKind.SixLine, 6 },
            };

            foreach (InsideBetSpot spot in InsideBets.All)
            {
                Assert.AreEqual(expected[spot.Target.Kind], spot.Target.Numbers.Count(), spot.Target.ToString());
            }
        }

        [Test]
        public void Splits_AreAdjacentOnTheTable()
        {
            foreach (InsideBetSpot spot in InsideBets.All.Where(s => s.Target.Kind == BetKind.Split))
            {
                int[] n = spot.Target.Numbers.ToArray();
                int a = n[0];
                int b = n[1];

                bool zeroSplit = a == 0 && b >= 1 && b <= 3;
                bool sideBySide = b - a == 3;                  // 옆 열, 같은 줄
                bool sameColumn = b - a == 1 && a % 3 != 0;    // 같은 열, 위아래
                Assert.IsTrue(zeroSplit || sideBySide || sameColumn, spot.Target.ToString());
            }
        }

        [Test]
        public void SpecialZeroBets_Exist()
        {
            Assert.IsTrue(InsideBets.IsValid(BetTarget.Inside(BetKind.Split, 0, 1)));
            Assert.IsTrue(InsideBets.IsValid(BetTarget.Inside(BetKind.Split, 0, 3)));
            Assert.IsTrue(InsideBets.IsValid(BetTarget.Inside(BetKind.Street, 0, 1, 2)));
            Assert.IsTrue(InsideBets.IsValid(BetTarget.Inside(BetKind.Street, 0, 2, 3)));
            Assert.IsTrue(InsideBets.IsValid(BetTarget.Inside(BetKind.Corner, 0, 1, 2, 3)));
            Assert.IsFalse(InsideBets.IsValid(BetTarget.Inside(BetKind.Street, 0, 1, 3)), "0·1·3은 붙어 있지 않다.");
        }

        [Test]
        public void SpotsLieOnTheBoard_AndDoNotOverlap()
        {
            foreach (InsideBetSpot spot in InsideBets.All)
            {
                Assert.That(spot.X, Is.InRange(0f, 12f));
                Assert.That(spot.Y, Is.InRange(0f, 3f));
            }

            int distinctPoints = InsideBets.All.Select(s => (s.X, s.Y)).Distinct().Count();
            Assert.AreEqual(108, distinctPoints, "클릭 점 두 개가 같은 자리에 있으면 하나는 누를 수 없다.");
        }

        [Test]
        public void SpotPositions_MatchTheirNumbers()
        {
            // 위치에서 맞닿는 칸을 거꾸로 구해 조합과 같은지 본다. 번호판: 열 c = 3c+1~3c+3, 위 줄이 3의 배수.
            foreach (InsideBetSpot spot in InsideBets.All)
            {
                var touching = new HashSet<int>();
                for (int c = 0; c < 12; c++)
                {
                    for (int r = 0; r < 3; r++)
                    {
                        bool xTouches = spot.X >= c && spot.X <= c + 1;
                        bool yTouches = spot.Y >= r && spot.Y <= r + 1 || spot.Y >= 3f;
                        if (xTouches && yTouches)
                        {
                            touching.Add(InsideBets.NumberAt(c, r));
                        }
                    }
                }

                if (spot.X == 0f)
                {
                    touching.Add(0);
                }

                CollectionAssert.IsSubsetOf(spot.Target.Numbers, touching, spot.Target.ToString());
            }
        }

        // ---------- 검증 ----------

        [Test]
        public void NonAdjacentCombinations_AreRejected()
        {
            Assert.AreEqual(BetError.InvalidTarget, flow.SelectTarget(BetTarget.Inside(BetKind.Split, 1, 5)));
            Assert.AreEqual(BetError.InvalidTarget, flow.SelectTarget(BetTarget.Inside(BetKind.Split, 3, 4)), "3(위 줄)과 4(아래 줄)는 붙어 있지 않다.");
            Assert.AreEqual(BetError.InvalidTarget, flow.SelectTarget(BetTarget.Inside(BetKind.Street, 2, 3, 4)));
            Assert.AreEqual(BetError.InvalidTarget, flow.SelectTarget(BetTarget.Inside(BetKind.Corner, 1, 2, 3, 4)));
            Assert.AreEqual(BetError.InvalidTarget, flow.SelectTarget(BetTarget.Inside(BetKind.Split, 7, Skull)));
            Assert.IsTrue(flow.Bet.Selection.IsNone);
        }

        [Test]
        public void InsideBets_NeverCoverSkull()
        {
            BallResult skull = SpinResult.FromNumbers(definition, Skull).Balls[0];

            foreach (InsideBetSpot spot in InsideBets.All)
            {
                Assert.IsFalse(spot.Target.Covers(skull), spot.Target.ToString());
            }
        }

        // ---------- 배수 ----------

        [Test]
        public void Payouts_AreStandard_AndKeepSameReturnRate()
        {
            SpinConfig config = SpinConfig.Create(definition, balance);

            Assert.AreEqual(18, config.PayoutOf(PayoutClass.Split));
            Assert.AreEqual(12, config.PayoutOf(PayoutClass.Street));
            Assert.AreEqual(9, config.PayoutOf(PayoutClass.Corner));
            Assert.AreEqual(6, config.PayoutOf(PayoutClass.SixLine));

            foreach (InsideBetSpot spot in InsideBets.All)
            {
                Assert.AreEqual(36, spot.Target.Numbers.Count() * config.PayoutFor(spot.Target), spot.Target.ToString());
            }
        }

        // ---------- 정산 ----------

        [TestCase(BetKind.Split, new[] { 17, 20 }, 20, 180)]
        [TestCase(BetKind.Street, new[] { 7, 8, 9 }, 8, 120)]
        [TestCase(BetKind.Corner, new[] { 17, 18, 20, 21 }, 21, 90)]
        [TestCase(BetKind.SixLine, new[] { 31, 32, 33, 34, 35, 36 }, 31, 60)]
        [TestCase(BetKind.Corner, new[] { 0, 1, 2, 3 }, 0, 90)]
        [TestCase(BetKind.Street, new[] { 0, 1, 2 }, 0, 120)]
        [TestCase(BetKind.Split, new[] { 0, 3 }, 3, 180)]
        [TestCase(BetKind.Street, new[] { 1, 2, 3 }, 0, 0)]
        [TestCase(BetKind.Split, new[] { 17, 20 }, 18, 0)]
        public void Settlement_PaysByKind(BetKind kind, int[] numbers, int result, long expected)
        {
            BetTarget target = BetTarget.Inside(kind, numbers);
            Assert.IsTrue(InsideBets.IsValid(target), target.ToString());

            SpinConfig config = SpinConfig.Create(definition, balance);
            SettlementResult s = PayoutCalculator.Calculate(
                new ConfirmedBet(target, 10), SpinResult.FromNumbers(definition, result), config.PayoutFor(target), Max);

            Assert.AreEqual(expected, s.TotalPayout);
        }

        [Test]
        public void InsideWinWithSkull_PaysZero()
        {
            BetTarget corner = BetTarget.Inside(BetKind.Corner, 0, 1, 2, 3);

            SettlementResult s = PayoutCalculator.Calculate(new ConfirmedBet(corner, 10), SpinResult.FromNumbers(definition, 2, Skull), 9, Max);

            Assert.AreEqual(SpinOutcome.SkullBust, s.Outcome);
            Assert.AreEqual(0, s.TotalPayout);
        }

        // ---------- 진행 흐름 ----------

        [Test]
        public void Flow_SplitBet_ChargesOnce_AndPays18()
        {
            BetTarget split = BetTarget.Inside(BetKind.Split, 26, 29);
            flow.ForcedNumbers = new[] { 29 };
            Assert.AreEqual(BetError.None, flow.SelectTarget(split));
            flow.SetStake(5);

            Assert.AreEqual(BetError.None, flow.TryBeginSpin(out ActiveSpin spin));
            Assert.AreEqual(95, flow.Wallet.Balance);
            Assert.AreEqual(BetError.NotInBettingPhase, flow.SelectTarget(BetTarget.Inside(BetKind.Split, 1, 2)));

            Assert.IsTrue(flow.CompleteSpin(spin.Id, out SettlementResult settlement));
            Assert.AreEqual(90, settlement.TotalPayout);
            Assert.AreEqual(185, flow.Wallet.Balance);
            Assert.AreEqual(split, flow.Bet.Selection);
        }

        [Test]
        public void Labels_DistinguishTrioAndFirstFour()
        {
            Assert.AreEqual("스플릿 17·20", BetTarget.Inside(BetKind.Split, 20, 17).ToKorean(definition));
            Assert.AreEqual("트리오 0·1·2", BetTarget.Inside(BetKind.Street, 0, 1, 2).ToKorean(definition));
            Assert.AreEqual("스트리트 7·8·9", BetTarget.Inside(BetKind.Street, 7, 8, 9).ToKorean(definition));
            Assert.AreEqual("퍼스트 포 0·1·2·3", BetTarget.Inside(BetKind.Corner, 0, 1, 2, 3).ToKorean(definition));
            Assert.AreEqual("식스 라인 1·2·3·4·5·6", BetTarget.Inside(BetKind.SixLine, 1, 2, 3, 4, 5, 6).ToKorean(definition));
        }

        [Test]
        public void InsideFactory_RejectsOutsideKinds()
        {
            Assert.Throws<ArgumentException>(() => BetTarget.Inside(BetKind.Red, 1, 3));
            Assert.Throws<ArgumentException>(() => BetTarget.Outside(BetKind.Split));
        }
    }
}
