using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ProjectCR.Tests
{
    /// <summary>
    /// 바깥 베팅: 빨강·검정, 홀·짝, 1–18·19–36(2배), 더즌·열(3배). 배수는 원금 포함.
    /// 0과 해골은 어떤 바깥 베팅에도 포함되지 않는다.
    /// </summary>
    public class OutsideBetTests
    {
        private const int Skull = RouletteDefinition.SkullNumber;
        private const long Max = 999_999_999_999;

        private static readonly int[] RedNumbers = { 1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36 };

        private static readonly BetKind[] OutsideKinds =
        {
            BetKind.Red, BetKind.Black, BetKind.Odd, BetKind.Even, BetKind.Low, BetKind.High,
            BetKind.Dozen1, BetKind.Dozen2, BetKind.Dozen3, BetKind.Column1, BetKind.Column2, BetKind.Column3,
        };

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
            flow = new GameFlowController(definition, balance, seed: 11);
        }

        [TearDown]
        public void TearDown() => assets.DestroyAll();

        private IEnumerable<int> CoveredNumbers(BetTarget target)
        {
            for (int i = 0; i < definition.PocketCount; i++)
            {
                BallResult ball = BallResult.FromPocket(definition, 0, i);
                if (target.Covers(ball))
                {
                    yield return ball.Number;
                }
            }
        }

        private static IEnumerable<int> Range(int from, int to) => Enumerable.Range(from, to - from + 1);

        // ---------- 포함 칸 ----------

        [Test]
        public void EvenMoneyBets_CoverStandard18Numbers()
        {
            CollectionAssert.AreEquivalent(RedNumbers, CoveredNumbers(BetTarget.Outside(BetKind.Red)));
            CollectionAssert.AreEquivalent(Range(1, 36).Except(RedNumbers), CoveredNumbers(BetTarget.Outside(BetKind.Black)));
            CollectionAssert.AreEquivalent(Range(1, 36).Where(n => n % 2 == 1), CoveredNumbers(BetTarget.Outside(BetKind.Odd)));
            CollectionAssert.AreEquivalent(Range(1, 36).Where(n => n % 2 == 0), CoveredNumbers(BetTarget.Outside(BetKind.Even)));
            CollectionAssert.AreEquivalent(Range(1, 18), CoveredNumbers(BetTarget.Outside(BetKind.Low)));
            CollectionAssert.AreEquivalent(Range(19, 36), CoveredNumbers(BetTarget.Outside(BetKind.High)));
        }

        [Test]
        public void DozenAndColumnBets_CoverStandard12Numbers()
        {
            CollectionAssert.AreEquivalent(Range(1, 12), CoveredNumbers(BetTarget.Outside(BetKind.Dozen1)));
            CollectionAssert.AreEquivalent(Range(13, 24), CoveredNumbers(BetTarget.Outside(BetKind.Dozen2)));
            CollectionAssert.AreEquivalent(Range(25, 36), CoveredNumbers(BetTarget.Outside(BetKind.Dozen3)));

            // 번호판 아래 줄이 1열(1, 4, …, 34), 위 줄이 3열(3, 6, …, 36).
            CollectionAssert.AreEquivalent(Range(0, 11).Select(i => 3 * i + 1), CoveredNumbers(BetTarget.Outside(BetKind.Column1)));
            CollectionAssert.AreEquivalent(Range(0, 11).Select(i => 3 * i + 2), CoveredNumbers(BetTarget.Outside(BetKind.Column2)));
            CollectionAssert.AreEquivalent(Range(0, 11).Select(i => 3 * i + 3), CoveredNumbers(BetTarget.Outside(BetKind.Column3)));
        }

        [Test]
        public void ZeroAndSkull_AreNotCoveredByAnyOutsideBet()
        {
            BallResult zero = SpinResult.FromNumbers(definition, 0).Balls[0];
            BallResult skull = SpinResult.FromNumbers(definition, Skull).Balls[0];

            foreach (BetKind kind in OutsideKinds)
            {
                Assert.IsFalse(BetTarget.Outside(kind).Covers(zero), $"{kind}가 0을 포함");
                Assert.IsFalse(BetTarget.Outside(kind).Covers(skull), $"{kind}가 해골을 포함");
            }
        }

        // ---------- 배수 ----------

        [Test]
        public void PayoutClasses_UseBalanceMultipliers()
        {
            SpinConfig config = SpinConfig.Create(definition, balance);

            Assert.AreEqual(36, config.PayoutFor(BetTarget.Straight(17)));
            foreach (BetKind kind in new[] { BetKind.Dozen1, BetKind.Dozen2, BetKind.Dozen3, BetKind.Column1, BetKind.Column2, BetKind.Column3 })
            {
                Assert.AreEqual(3, config.PayoutFor(BetTarget.Outside(kind)), kind.ToString());
            }

            foreach (BetKind kind in new[] { BetKind.Red, BetKind.Black, BetKind.Odd, BetKind.Even, BetKind.Low, BetKind.High })
            {
                Assert.AreEqual(2, config.PayoutFor(BetTarget.Outside(kind)), kind.ToString());
            }

            Assert.AreEqual(0, config.PayoutFor(BetTarget.None));
        }

        [Test]
        public void EveryBet_HasSameReturnRate_On38PocketWheel()
        {
            // 배수 × 포함 칸 수 = 36으로 맞춰져 있어 어떤 베팅이든 기대 환수율이 36/38이다.
            SpinConfig config = SpinConfig.Create(definition, balance);
            var targets = OutsideKinds.Select(BetTarget.Outside).Append(BetTarget.Straight(0)).Append(BetTarget.Straight(17));

            foreach (BetTarget target in targets)
            {
                int covered = CoveredNumbers(target).Count();
                Assert.AreEqual(36, covered * config.PayoutFor(target), target.ToString());
            }
        }

        // ---------- 정산 ----------

        [Test]
        public void RedWin_PaysTwoTimes_AndZeroLoses()
        {
            var bet = new ConfirmedBet(BetTarget.Outside(BetKind.Red), 50);

            SettlementResult win = PayoutCalculator.Calculate(bet, SpinResult.FromNumbers(definition, 32), 2, Max);
            Assert.AreEqual(SpinOutcome.Win, win.Outcome);
            Assert.AreEqual(100, win.TotalPayout);
            Assert.AreEqual(50, win.Net);

            SettlementResult zero = PayoutCalculator.Calculate(bet, SpinResult.FromNumbers(definition, 0), 2, Max);
            Assert.AreEqual(SpinOutcome.Lose, zero.Outcome);
            Assert.AreEqual(0, zero.TotalPayout);
        }

        [Test]
        public void OutsideWinWithSkull_PaysZero()
        {
            var bet = new ConfirmedBet(BetTarget.Outside(BetKind.Odd), 10);

            SettlementResult s = PayoutCalculator.Calculate(bet, SpinResult.FromNumbers(definition, 7, Skull), 2, Max);

            Assert.AreEqual(SpinOutcome.SkullBust, s.Outcome);
            Assert.AreEqual(0, s.TotalPayout);
        }

        [Test]
        public void TwoBalls_BothCovered_PaySummed()
        {
            var bet = new ConfirmedBet(BetTarget.Outside(BetKind.Dozen1), 10);

            Assert.AreEqual(60, PayoutCalculator.Calculate(bet, SpinResult.FromNumbers(definition, 3, 12), 3, Max).TotalPayout);
            Assert.AreEqual(30, PayoutCalculator.Calculate(bet, SpinResult.FromNumbers(definition, 3, 13), 3, Max).TotalPayout);
        }

        // ---------- 진행 흐름 ----------

        [Test]
        public void Flow_OutsideBet_UsesItsOwnMultiplier()
        {
            flow.ForcedNumbers = new[] { 25 };
            Assert.AreEqual(BetError.None, flow.PlaceBet(BetTarget.Outside(BetKind.Dozen3), 20));

            Assert.AreEqual(BetError.None, flow.TryBeginSpin(out ActiveSpin spin));
            Assert.AreEqual(80, flow.Wallet.Balance);
            Assert.IsTrue(flow.CompleteSpin(spin.Id, out SettlementResult settlement));

            Assert.AreEqual(60, settlement.TotalPayout);
            Assert.AreEqual(140, flow.Wallet.Balance);
            Assert.AreEqual(20, flow.Bet.StakeOn(BetTarget.Outside(BetKind.Dozen3)), "다음 회전에도 베팅이 남는다.");
        }

        [Test]
        public void Flow_StraightOverride_DoesNotChangeOutsidePayout()
        {
            flow.StraightPayoutOverride = 72;

            SpinConfig config = flow.BuildSpinConfig();

            Assert.AreEqual(72, config.StraightPayout);
            Assert.AreEqual(2, config.PayoutFor(BetTarget.Outside(BetKind.Red)));
            Assert.AreEqual(3, config.PayoutFor(BetTarget.Outside(BetKind.Column1)));
        }

        [Test]
        public void Flow_SelectionLockedDuringSpin()
        {
            flow.PlaceBet(BetTarget.Outside(BetKind.Even), 10);
            flow.TryBeginSpin(out _);

            Assert.AreEqual(BetError.NotInBettingPhase, flow.PlaceChip(BetTarget.Outside(BetKind.Odd)));
            Assert.AreEqual(BetError.NotInBettingPhase, flow.PlaceChip(BetTarget.Straight(7)));
            Assert.AreEqual(BetTarget.Outside(BetKind.Even), flow.CurrentSpin.Bets[0].Target);
        }

        [Test]
        public void NumberAndOutsideBets_CanBeCombined()
        {
            flow.PlaceBet(BetTarget.Straight(7), 5);
            flow.PlaceBet(BetTarget.Outside(BetKind.High), 10);

            Assert.AreEqual(2, flow.Bet.Count);
            Assert.AreEqual(15, flow.Bet.TotalStake);
        }

        [Test]
        public void InvalidTargets_AreRejected()
        {
            Assert.AreEqual(BetError.InvalidNumber, flow.PlaceChip(BetTarget.Straight(37)));
            Assert.AreEqual(BetError.SkullNotBettable, flow.PlaceChip(BetTarget.Straight(Skull)));
            Assert.AreEqual(BetError.NoBetsPlaced, flow.PlaceChip(BetTarget.None));
            Assert.IsFalse(flow.Bet.HasBets);
        }
    }
}
