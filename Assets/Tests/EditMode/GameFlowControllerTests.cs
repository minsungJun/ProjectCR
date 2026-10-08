using NUnit.Framework;

namespace ProjectCR.Tests
{
    /// <summary>
    /// 상태 전환, 중복 방지, 라운드·납부. 기본 밸런스(초기 100, 회전 5, 납부 120 → 180 → 270 → 410) 기준.
    /// </summary>
    public class GameFlowControllerTests
    {
        private TestAssets assets;
        private GameFlowController flow;

        [SetUp]
        public void SetUp()
        {
            assets = new TestAssets();
            flow = new GameFlowController(assets.Definition(), assets.Balance(), seed: 42);
        }

        [TearDown]
        public void TearDown() => assets.DestroyAll();

        private ActiveSpin Spin(int number, long stake, int forced)
        {
            flow.ForcedNumbers = new[] { forced };
            Assert.AreEqual(BetError.None, flow.ClearBets());
            Assert.AreEqual(BetError.None, flow.PlaceBet(BetTarget.Straight(number), stake));
            Assert.AreEqual(BetError.None, flow.TryBeginSpin(out ActiveSpin spin));
            return spin;
        }

        private SettlementResult SpinAndSettle(int number, long stake, int forced)
        {
            ActiveSpin spin = Spin(number, stake, forced);
            Assert.IsTrue(flow.CompleteSpin(spin.Id, out SettlementResult settlement));
            return settlement;
        }

        // ---------- 중복 방지 ----------

        [Test]
        public void RepeatedSpinRequests_ChargeAndConsumeOnlyOnce()
        {
            Spin(7, 10, forced: 8);

            Assert.AreEqual(GamePhase.Spinning, flow.Phase);
            Assert.AreEqual(BetError.NotInBettingPhase, flow.TryBeginSpin(out ActiveSpin second));
            Assert.AreEqual(BetError.NotInBettingPhase, flow.TryBeginSpin(out _));
            Assert.IsNull(second);

            Assert.AreEqual(90, flow.Wallet.Balance);
            Assert.AreEqual(4, flow.Round.SpinsRemaining);
        }

        [Test]
        public void BetChangesDuringSpin_AreRejected()
        {
            Spin(7, 10, forced: 8);

            Assert.AreEqual(BetError.NotInBettingPhase, flow.PlaceChip(BetTarget.Straight(9)));
            Assert.AreEqual(BetError.NotInBettingPhase, flow.PlaceBet(BetTarget.Outside(BetKind.Red), 5));
            Assert.AreEqual(BetError.NotInBettingPhase, flow.RemoveBet(BetTarget.Straight(7)));
            Assert.AreEqual(BetError.NotInBettingPhase, flow.UndoLastChip());
            Assert.AreEqual(BetError.NotInBettingPhase, flow.ClearBets());
            Assert.AreEqual(BetError.NotInBettingPhase, flow.SetChipValue(50));
            Assert.AreEqual(1, flow.CurrentSpin.Bets.Count);
            Assert.AreEqual(7, flow.CurrentSpin.Bets[0].Target.Number);
            Assert.AreEqual(10, flow.CurrentSpin.Bets[0].Stake);
        }

        [Test]
        public void Settlement_RunsOnlyOnce()
        {
            ActiveSpin spin = Spin(7, 10, forced: 7);

            Assert.IsTrue(flow.CompleteSpin(spin.Id, out SettlementResult first));
            Assert.IsFalse(flow.CompleteSpin(spin.Id, out SettlementResult second));
            Assert.IsNull(second);

            Assert.AreEqual(360, first.TotalPayout);
            Assert.AreEqual(100 - 10 + 360, flow.Wallet.Balance);
        }

        [Test]
        public void CompleteSpin_WithWrongId_IsIgnored()
        {
            ActiveSpin spin = Spin(7, 10, forced: 7);

            Assert.IsFalse(flow.CompleteSpin(spin.Id + 1, out _));
            Assert.AreEqual(GamePhase.Spinning, flow.Phase);
            Assert.AreEqual(90, flow.Wallet.Balance);
        }

        [Test]
        public void InvalidBet_DoesNotStartSpin()
        {
            Assert.AreEqual(BetError.InsufficientFunds, flow.PlaceBet(BetTarget.Straight(7), 500));

            Assert.AreEqual(BetError.NoBetsPlaced, flow.TryBeginSpin(out _));
            Assert.AreEqual(GamePhase.Betting, flow.Phase);
            Assert.AreEqual(100, flow.Wallet.Balance);
            Assert.AreEqual(5, flow.Round.SpinsRemaining);
        }

        // ---------- 결과 ----------

        [Test]
        public void SettlementUsesResultData()
        {
            SettlementResult lose = SpinAndSettle(7, 10, forced: 0);
            Assert.AreEqual(0, lose.TotalPayout);
            Assert.AreEqual(90, flow.Wallet.Balance);
            Assert.AreEqual(GamePhase.Betting, flow.Phase);
        }

        [Test]
        public void SameSeed_GivesSameResults()
        {
            var other = new GameFlowController(flow.Definition, flow.GameBalance, seed: 42);

            for (int i = 0; i < 4; i++)
            {
                flow.ClearBets();
                flow.PlaceBet(BetTarget.Straight(1), 1);
                other.ClearBets();
                other.PlaceBet(BetTarget.Straight(1), 1);
                flow.TryBeginSpin(out ActiveSpin a);
                other.TryBeginSpin(out ActiveSpin b);
                Assert.AreEqual(a.Result.Balls[0].Number, b.Result.Balls[0].Number);
                flow.CompleteSpin(a.Id, out _);
                other.CompleteSpin(b.Id, out _);
            }
        }

        [Test]
        public void ExtraBallAndMultiplier_ChargeOnceAndSumPayouts()
        {
            flow.ExtraBallCount = 1;
            flow.StraightPayoutOverride = 72;
            flow.ForcedNumbers = new[] { 7, 7 };
            flow.PlaceBet(BetTarget.Straight(7), 10);

            Assert.AreEqual(BetError.None, flow.TryBeginSpin(out ActiveSpin spin));
            Assert.AreEqual(2, spin.Result.Balls.Count);
            Assert.AreEqual(90, flow.Wallet.Balance, "공이 둘이어도 베팅은 한 번만 뺀다.");

            flow.CompleteSpin(spin.Id, out SettlementResult settlement);
            Assert.AreEqual(2 * 720, settlement.TotalPayout);
            Assert.AreEqual(90 + 1440, flow.Wallet.Balance);

            Assert.AreEqual(1, flow.GameBalance.BallCount, "설정 에셋은 바뀌지 않는다.");
            Assert.AreEqual(36, flow.GameBalance.StraightTotalPayout);
        }

        // ---------- 라운드 ----------

        [Test]
        public void Due_GrowsPerRound()
        {
            Assert.AreEqual(120, flow.GameBalance.GetDue(1));
            Assert.AreEqual(180, flow.GameBalance.GetDue(2));
            Assert.AreEqual(270, flow.GameBalance.GetDue(3));
            Assert.AreEqual(410, flow.GameBalance.GetDue(4));
        }

        [Test]
        public void LastSpin_MovesToPayment_AndPaymentCarriesRemainder()
        {
            SpinAndSettle(7, 1, forced: 7); // 100 − 1 + 36 = 135
            for (int i = 0; i < 4; i++)
            {
                SpinAndSettle(7, 1, forced: 8);
            }

            Assert.AreEqual(131, flow.Wallet.Balance);
            Assert.AreEqual(GamePhase.RoundPayment, flow.Phase);
            Assert.AreEqual(BetError.NotInBettingPhase, flow.TryBeginSpin(out _));

            Assert.AreEqual(PaymentOutcome.Paid, flow.SubmitPayment());
            Assert.AreEqual(GamePhase.Betting, flow.Phase);
            Assert.AreEqual(2, flow.Round.Round);
            Assert.AreEqual(5, flow.Round.SpinsRemaining);
            Assert.AreEqual(11, flow.Wallet.Balance);
            Assert.AreEqual(180, flow.Round.CurrentDue);

            Assert.IsNull(flow.SubmitPayment(), "Betting 상태에서는 납부할 수 없다.");
        }

        [Test]
        public void PaymentShortfall_EndsGame_WithoutCharging()
        {
            for (int i = 0; i < 5; i++)
            {
                SpinAndSettle(7, 1, forced: 8);
            }

            Assert.AreEqual(95, flow.Wallet.Balance);
            Assert.AreEqual(PaymentOutcome.Insufficient, flow.SubmitPayment());
            Assert.AreEqual(GamePhase.GameOver, flow.Phase);
            Assert.AreEqual(95, flow.Wallet.Balance);
            Assert.AreEqual(BetError.NotInBettingPhase, flow.TryBeginSpin(out _));
        }

        [Test]
        public void OutOfMoney_ForfeitsRemainingSpins_AndGoesToPayment()
        {
            SpinAndSettle(7, 100, forced: 8);

            Assert.AreEqual(0, flow.Wallet.Balance);
            Assert.AreEqual(GamePhase.RoundPayment, flow.Phase);
            Assert.AreEqual(0, flow.Round.SpinsRemaining);
            Assert.AreEqual(4, flow.Round.ForfeitedSpins);

            Assert.AreEqual(PaymentOutcome.Insufficient, flow.SubmitPayment());
            Assert.AreEqual(GamePhase.GameOver, flow.Phase);
        }

        [Test]
        public void BetsStayAfterSpin_ButCannotExceedBalance()
        {
            SpinAndSettle(7, 60, forced: 8);

            Assert.AreEqual(40, flow.Wallet.Balance);
            Assert.AreEqual(60, flow.Bet.TotalStake, "베팅은 판에 남는다.");
            Assert.AreEqual(BetError.InsufficientFunds, flow.ValidateBet());
            Assert.AreEqual(BetError.InsufficientFunds, flow.TryBeginSpin(out _));
            Assert.AreEqual(40, flow.Wallet.Balance);

            flow.ClearBets();
            Assert.AreEqual(BetError.None, flow.PlaceBet(BetTarget.Straight(7), 40));
            Assert.AreEqual(BetError.None, flow.ValidateBet());
        }
    }
}
