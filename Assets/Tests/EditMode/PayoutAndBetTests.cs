using NUnit.Framework;

namespace ProjectCR.Tests
{
    public class PayoutAndBetTests
    {
        private const long Max = 999_999_999_999;

        private TestAssets assets;
        private RouletteDefinition definition;

        [SetUp]
        public void SetUp()
        {
            assets = new TestAssets();
            definition = assets.Definition();
        }

        [TearDown]
        public void TearDown() => assets.DestroyAll();

        // ---------- 정산 ----------

        [Test]
        public void Win_Pays36TimesIncludingStake()
        {
            SettlementResult s = PayoutCalculator.Calculate(new ConfirmedBet(17, 100), SpinResult.FromNumbers(definition, 17), 36, Max);

            Assert.AreEqual(3600, s.TotalPayout);
            Assert.AreEqual(3500, s.Net);
            Assert.AreEqual(1, s.WinCount);
        }

        [Test]
        public void Lose_PaysZero()
        {
            SettlementResult s = PayoutCalculator.Calculate(new ConfirmedBet(17, 100), SpinResult.FromNumbers(definition, 18), 36, Max);

            Assert.AreEqual(0, s.TotalPayout);
            Assert.AreEqual(-100, s.Net);
            Assert.AreEqual(0, s.WinCount);
        }

        [Test]
        public void Zero_IsAnOrdinaryStraightBet()
        {
            Assert.AreEqual(360, PayoutCalculator.Calculate(new ConfirmedBet(0, 10), SpinResult.FromNumbers(definition, 0), 36, Max).TotalPayout);
            Assert.AreEqual(0, PayoutCalculator.Calculate(new ConfirmedBet(1, 10), SpinResult.FromNumbers(definition, 0), 36, Max).TotalPayout);
        }

        [Test]
        public void TwoBalls_PayoutsAreSummedPerBall()
        {
            var bet = new ConfirmedBet(17, 10);

            Assert.AreEqual(360, PayoutCalculator.Calculate(bet, SpinResult.FromNumbers(definition, 17, 4), 36, Max).TotalPayout);
            Assert.AreEqual(720, PayoutCalculator.Calculate(bet, SpinResult.FromNumbers(definition, 17, 17), 36, Max).TotalPayout);
            Assert.AreEqual(0, PayoutCalculator.Calculate(bet, SpinResult.FromNumbers(definition, 3, 4), 36, Max).TotalPayout);

            SettlementResult both = PayoutCalculator.Calculate(bet, SpinResult.FromNumbers(definition, 17, 17), 36, Max);
            Assert.AreEqual(2, both.Balls.Count);
            Assert.AreEqual(2, both.WinCount);
        }

        [Test]
        public void ChangedMultiplier_IsUsed()
        {
            Assert.AreEqual(720, PayoutCalculator.Calculate(new ConfirmedBet(5, 10), SpinResult.FromNumbers(definition, 5), 72, Max).TotalPayout);
        }

        [Test]
        public void Overflow_SaturatesAtMax()
        {
            Assert.AreEqual(Max, PayoutCalculator.ScaledPayout(Max, 36, Max));
            Assert.AreEqual(Max, PayoutCalculator.ScaledPayout(long.MaxValue / 2, 36, Max));
            Assert.AreEqual(Max, PayoutCalculator.Calculate(new ConfirmedBet(1, Max / 40), SpinResult.FromNumbers(definition, 1, 1), 36, Max).TotalPayout);
        }

        // ---------- 지갑 ----------

        [Test]
        public void Wallet_AddSaturatesAndSpendRejectsOverdraft()
        {
            var wallet = new Wallet(100, 1000);

            Assert.IsFalse(wallet.TrySpend(101));
            Assert.IsFalse(wallet.TrySpend(-1));
            Assert.AreEqual(100, wallet.Balance);

            Assert.AreEqual(900, wallet.Add(5000));
            Assert.AreEqual(1000, wallet.Balance);
        }

        // ---------- 베팅 ----------

        [Test]
        public void InvalidBets_AreRejectedWithoutCharging()
        {
            var wallet = new Wallet(100, Max);
            var bet = new BetManager(definition, 5);

            bet.SetStake(10);
            Assert.AreEqual(BetError.NoTargetSelected, bet.TryConfirm(wallet, out _));

            Assert.AreEqual(BetError.InvalidNumber, bet.SelectNumber(37));
            Assert.AreEqual(BetError.InvalidNumber, bet.SelectNumber(-3));

            bet.SelectNumber(7);
            bet.SetStake(0);
            Assert.AreEqual(BetError.NonPositive, bet.TryConfirm(wallet, out _));
            bet.SetStake(-50);
            Assert.AreEqual(BetError.NonPositive, bet.TryConfirm(wallet, out _));
            bet.SetStake(4);
            Assert.AreEqual(BetError.BelowMinimum, bet.TryConfirm(wallet, out _));
            bet.SetStake(101);
            Assert.AreEqual(BetError.InsufficientFunds, bet.TryConfirm(wallet, out _));

            Assert.AreEqual(100, wallet.Balance);
            Assert.IsFalse(bet.IsLocked);
        }

        [Test]
        public void Confirm_ChargesOnce_AndLocksUntilUnlock()
        {
            var wallet = new Wallet(100, Max);
            var bet = new BetManager(definition, 1);
            bet.SelectNumber(7);
            bet.SetStake(100);

            Assert.AreEqual(BetError.None, bet.TryConfirm(wallet, out ConfirmedBet confirmed));
            Assert.AreEqual(7, confirmed.Target.Number);
            Assert.AreEqual(100, confirmed.Stake);
            Assert.AreEqual(0, wallet.Balance);

            Assert.AreEqual(BetError.NotInBettingPhase, bet.TryConfirm(wallet, out _));
            Assert.AreEqual(BetError.NotInBettingPhase, bet.SelectNumber(8));
            Assert.AreEqual(BetError.NotInBettingPhase, bet.SetStake(1));
            Assert.AreEqual(0, wallet.Balance);

            bet.Unlock(40);
            Assert.IsFalse(bet.IsLocked);
            Assert.AreEqual(40, bet.Stake, "잔액보다 큰 금액은 잔액으로 줄인다.");
            Assert.AreEqual(7, bet.SelectedNumber, "번호는 다음 베팅에도 남는다.");
        }
    }
}
