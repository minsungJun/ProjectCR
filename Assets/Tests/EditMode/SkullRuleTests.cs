using System;
using System.Linq;
using NUnit.Framework;

namespace ProjectCR.Tests
{
    /// <summary>
    /// 해골 칸 규칙. 해골은 베팅할 수 없고, 공 하나라도 걸리면 그 회전의 모든 베팅이 낙첨된다.
    /// 해골은 게임을 끝내지 않는다. 회전 1회를 쓰고 일반 회전과 같은 흐름으로 돌아간다.
    /// </summary>
    public class SkullRuleTests
    {
        private const int Skull = RouletteDefinition.SkullNumber;
        private const long Max = 999_999_999_999;

        private TestAssets assets;
        private RouletteDefinition definition;
        private GameFlowController flow;

        [SetUp]
        public void SetUp()
        {
            assets = new TestAssets();
            definition = assets.Definition();
            flow = new GameFlowController(definition, assets.Balance(), seed: 7);
        }

        [TearDown]
        public void TearDown() => assets.DestroyAll();

        private ActiveSpin Begin(int number, long stake, params int[] forced)
        {
            flow.ForcedNumbers = forced;
            Assert.AreEqual(BetError.None, flow.ClearBets());
            Assert.AreEqual(BetError.None, flow.PlaceBet(BetTarget.Straight(number), stake));
            Assert.AreEqual(BetError.None, flow.TryBeginSpin(out ActiveSpin spin));
            return spin;
        }

        private SettlementResult SpinAndSettle(int number, long stake, params int[] forced)
        {
            ActiveSpin spin = Begin(number, stake, forced);
            Assert.IsTrue(flow.CompleteSpin(spin.Id, out SettlementResult settlement));
            return settlement;
        }

        // ---------- 휠 정의 ----------

        [Test]
        public void SkullWheel_Has38Pockets_Numbers0To36OnceAndOneSkull()
        {
            Assert.AreEqual(38, definition.PocketCount);
            CollectionAssert.IsEmpty(definition.Validate());

            var pockets = Enumerable.Range(0, definition.PocketCount).Select(definition.GetPocket).ToList();
            Assert.AreEqual(1, pockets.Count(p => p.IsSkull));
            CollectionAssert.AreEquivalent(Enumerable.Range(0, 37), pockets.Where(p => !p.IsSkull).Select(p => p.Number));
        }

        [Test]
        public void Skull_IsOppositeZero_AndHasNoColor()
        {
            Assert.AreEqual(0, definition.IndexOfNumber(0));
            Assert.AreEqual(19, definition.IndexOfSkull(), "38칸 중 19칸 거리 = 180°");

            PocketDefinition skull = definition.GetPocket(definition.IndexOfSkull());
            Assert.AreEqual(PocketKind.Skull, skull.Kind);
            Assert.AreEqual(Skull, skull.Number);
            Assert.AreEqual(PocketColor.None, skull.Color);
        }

        [Test]
        public void NumberPockets_KeepEuropeanOrder_AroundTheSkull()
        {
            int[] numbersOnly = Enumerable.Range(0, definition.PocketCount)
                .Select(definition.GetPocket)
                .Where(p => !p.IsSkull)
                .Select(p => p.Number)
                .ToArray();

            CollectionAssert.AreEqual(RouletteDefinition.EuropeanOrder, numbersOnly);
            Assert.AreEqual(10, definition.GetPocket(18).Number);
            Assert.AreEqual(5, definition.GetPocket(20).Number);
        }

        // ---------- 베팅 ----------

        [Test]
        public void CannotBetOnSkull()
        {
            Assert.IsFalse(definition.ContainsNumber(Skull));
            Assert.AreEqual(-1, definition.IndexOfNumber(Skull));

            Assert.AreEqual(BetError.SkullNotBettable, flow.PlaceChip(BetTarget.Straight(Skull)));
            Assert.IsFalse(flow.Bet.HasBets);
        }

        [Test]
        public void SkullPocket_IsNotCoveredByAnyBet()
        {
            BallResult skullBall = SpinResult.FromNumbers(definition, Skull).Balls[0];

            Assert.IsTrue(skullBall.IsSkull);
            Assert.IsFalse(PayoutCalculator.Covers(new ConfirmedBet(Skull, 10), skullBall));
        }

        // ---------- 추첨 ----------

        [Test]
        public void Skull_IsDrawnAboutOneIn38()
        {
            SpinConfig config = SpinConfig.Create(definition, assets.Balance());
            int skullIndex = definition.IndexOfSkull();
            var rng = new Random(2026);
            int hits = 0;

            for (int i = 0; i < 38000; i++)
            {
                if (SpinResolver.PickPocket(config.Weights, rng) == skullIndex)
                {
                    hits++;
                }
            }

            Assert.That(hits, Is.InRange(800, 1200));
        }

        [Test]
        public void ForcedSkullNumber_MapsToSkullPocket()
        {
            SpinResult forced = SpinResult.FromNumbers(definition, 7, Skull);

            Assert.IsFalse(forced.Balls[0].IsSkull);
            Assert.IsTrue(forced.Balls[1].IsSkull);
            Assert.AreEqual(definition.IndexOfSkull(), forced.Balls[1].PocketIndex);
            Assert.IsTrue(forced.HasSkull);
        }

        // ---------- 정산 ----------

        [Test]
        public void SingleSkullBall_PaysZero_WithSkullReason()
        {
            SettlementResult s = PayoutCalculator.Calculate(new ConfirmedBet(7, 10), SpinResult.FromNumbers(definition, Skull), 36, Max);

            Assert.AreEqual(SpinOutcome.SkullBust, s.Outcome);
            Assert.AreEqual(0, s.TotalPayout);
            Assert.AreEqual(-10, s.Net);
        }

        [TestCase(7, Skull)]
        [TestCase(Skull, 7)]
        public void WinningBallWithSkullBall_PaysZero(int first, int second)
        {
            SettlementResult s = PayoutCalculator.Calculate(new ConfirmedBet(7, 10), SpinResult.FromNumbers(definition, first, second), 36, Max);

            Assert.AreEqual(SpinOutcome.SkullBust, s.Outcome);
            Assert.AreEqual(0, s.TotalPayout);
            Assert.AreEqual(0, s.WinCount, "맞힌 공도 당첨으로 치지 않는다.");
            Assert.IsTrue(s.Bets.All(b => b.Amount == 0));
        }

        [Test]
        public void ChangedMultiplier_IsNotAppliedOnSkullSpin()
        {
            SettlementResult s = PayoutCalculator.Calculate(new ConfirmedBet(7, 10), SpinResult.FromNumbers(definition, 7, Skull), 72, Max);

            Assert.AreEqual(0, s.TotalPayout);
        }

        [Test]
        public void SpinsWithoutSkull_SettleAsBefore()
        {
            var bet = new ConfirmedBet(7, 10);

            SettlementResult win = PayoutCalculator.Calculate(bet, SpinResult.FromNumbers(definition, 7, 7), 36, Max);
            Assert.AreEqual(SpinOutcome.Win, win.Outcome);
            Assert.AreEqual(720, win.TotalPayout);

            SettlementResult lose = PayoutCalculator.Calculate(bet, SpinResult.FromNumbers(definition, 0, 8), 36, Max);
            Assert.AreEqual(SpinOutcome.Lose, lose.Outcome);
            Assert.AreEqual(0, lose.TotalPayout);
        }

        // ---------- 진행 흐름 ----------

        [Test]
        public void SkullSpin_ChargesOnce_NoRefund_UsesOneSpin_AndContinues()
        {
            SettlementResult s = SpinAndSettle(7, 10, Skull);

            Assert.AreEqual(SpinOutcome.SkullBust, s.Outcome);
            Assert.AreEqual(90, flow.Wallet.Balance, "베팅금은 돌려주지 않는다.");
            Assert.AreEqual(4, flow.Round.SpinsRemaining);
            Assert.AreEqual(GamePhase.Betting, flow.Phase, "해골은 게임을 끝내지 않는다.");

            Assert.IsFalse(flow.Bet.HasBets, "해골 회전도 끝나면 칩을 회수한다.");
            Assert.AreEqual(BetError.None, flow.PlaceBet(BetTarget.Straight(7), 10));
            Assert.AreEqual(BetError.None, flow.TryBeginSpin(out _), "남은 회전과 잔액이 있으면 바로 다음 베팅.");
        }

        [Test]
        public void TwoBallsWithSkull_UsesOneSpin_ChargesOnce_PaysZero()
        {
            flow.ExtraBallCount = 1;
            flow.StraightPayoutOverride = 72;

            SettlementResult s = SpinAndSettle(7, 10, 7, Skull);

            Assert.AreEqual(2, s.Spin.Balls.Count);
            Assert.AreEqual(0, s.TotalPayout);
            Assert.AreEqual(90, flow.Wallet.Balance);
            Assert.AreEqual(4, flow.Round.SpinsRemaining);
        }

        [Test]
        public void SkullSettlement_RunsOnlyOnce()
        {
            ActiveSpin spin = Begin(7, 10, Skull);

            Assert.AreEqual(BetError.NotInBettingPhase, flow.TryBeginSpin(out _));
            Assert.IsTrue(flow.CompleteSpin(spin.Id, out _));
            Assert.IsFalse(flow.CompleteSpin(spin.Id, out _));

            Assert.AreEqual(90, flow.Wallet.Balance);
            Assert.AreEqual(4, flow.Round.SpinsRemaining);
            Assert.AreEqual(1, flow.SpinsPlayed);
        }

        [Test]
        public void SkullOnLastSpin_GoesToPayment_ThenPaysAsUsual()
        {
            SpinAndSettle(7, 1, 7); // 100 − 1 + 36 = 135
            for (int i = 0; i < 3; i++)
            {
                SpinAndSettle(7, 1, 8);
            }

            Assert.AreEqual(GamePhase.Betting, flow.Phase);
            SpinAndSettle(7, 1, Skull);

            Assert.AreEqual(GamePhase.RoundPayment, flow.Phase);
            Assert.AreEqual(131, flow.Wallet.Balance);
            Assert.AreEqual(PaymentOutcome.Paid, flow.SubmitPayment());
            Assert.AreEqual(2, flow.Round.Round);
            Assert.AreEqual(11, flow.Wallet.Balance);
            Assert.AreEqual(GamePhase.Betting, flow.Phase);
        }

        [Test]
        public void AllSkullRound_EndsOnlyThroughPaymentShortfall()
        {
            for (int i = 0; i < 5; i++)
            {
                Assert.AreNotEqual(GamePhase.GameOver, flow.Phase);
                SpinAndSettle(7, 1, Skull);
            }

            Assert.AreEqual(GamePhase.RoundPayment, flow.Phase, "해골만 나와도 납부 단계까지는 정상 진행.");
            Assert.AreEqual(95, flow.Wallet.Balance);

            Assert.AreEqual(PaymentOutcome.Insufficient, flow.SubmitPayment());
            Assert.AreEqual(GamePhase.GameOver, flow.Phase);
        }

        [Test]
        public void SkullTakingLastMoney_FollowsOutOfMoneyRule()
        {
            SpinAndSettle(7, 100, Skull);

            Assert.AreEqual(0, flow.Wallet.Balance);
            Assert.AreEqual(GamePhase.RoundPayment, flow.Phase, "잔액이 없으면 기존 규칙대로 남은 회전을 버리고 납부로.");
            Assert.AreEqual(4, flow.Round.ForfeitedSpins);
        }

        [Test]
        public void LossStreak_CountsSkull_AndResetsOnWin()
        {
            SpinAndSettle(7, 1, 8);
            SpinAndSettle(7, 1, Skull);
            Assert.AreEqual(2, flow.ConsecutiveLosses);

            SpinAndSettle(7, 1, 7);
            Assert.AreEqual(0, flow.ConsecutiveLosses);
            Assert.AreEqual(3, flow.SpinsPlayed);
        }
    }
}
