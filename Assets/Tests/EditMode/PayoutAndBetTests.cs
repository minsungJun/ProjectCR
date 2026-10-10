using System.Collections.Generic;
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
            Assert.AreEqual(2, both.Spin.Balls.Count);
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

            Assert.AreEqual(BetError.NoBetsPlaced, bet.TryConfirm(wallet, out _));

            Assert.AreEqual(BetError.InvalidNumber, bet.PlaceBet(BetTarget.Straight(37), 10, wallet));
            Assert.AreEqual(BetError.InvalidNumber, bet.PlaceBet(BetTarget.Straight(-3), 10, wallet));
            Assert.AreEqual(BetError.NonPositive, bet.PlaceBet(BetTarget.Straight(7), 0, wallet));
            Assert.AreEqual(BetError.NonPositive, bet.PlaceBet(BetTarget.Straight(7), -50, wallet));
            Assert.AreEqual(BetError.BelowMinimum, bet.PlaceBet(BetTarget.Straight(7), 4, wallet));
            Assert.AreEqual(BetError.InsufficientFunds, bet.PlaceBet(BetTarget.Straight(7), 101, wallet));

            bet.SetChipValue(4);
            Assert.AreEqual(BetError.BelowMinimum, bet.PlaceChip(BetTarget.Straight(7), wallet));

            Assert.IsFalse(bet.HasBets);
            Assert.AreEqual(100, wallet.Balance);
            Assert.IsFalse(bet.IsLocked);
        }

        [Test]
        public void Confirm_ChargesOnce_AndLocksUntilUnlock()
        {
            var wallet = new Wallet(100, Max);
            var bet = new BetManager(definition, 1);
            bet.SetChipValue(80);
            Assert.AreEqual(BetError.None, bet.PlaceBet(BetTarget.Straight(7), 60, wallet));
            Assert.AreEqual(BetError.None, bet.PlaceBet(BetTarget.Outside(BetKind.Red), 40, wallet));
            Assert.AreEqual(BetError.InsufficientFunds, bet.PlaceBet(BetTarget.Straight(8), 1, wallet), "합계가 보유 금액을 넘으면 놓을 수 없다.");

            Assert.AreEqual(BetError.None, bet.TryConfirm(wallet, out IReadOnlyList<ConfirmedBet> confirmed));
            Assert.AreEqual(2, confirmed.Count);
            Assert.AreEqual(7, confirmed[0].Target.Number);
            Assert.AreEqual(60, confirmed[0].Stake);
            Assert.AreEqual(BetKind.Red, confirmed[1].Target.Kind);
            Assert.AreEqual(0, wallet.Balance, "합계 100을 한 번에 뺀다.");

            Assert.AreEqual(BetError.NotInBettingPhase, bet.TryConfirm(wallet, out _));
            Assert.AreEqual(BetError.NotInBettingPhase, bet.PlaceChip(BetTarget.Straight(8), wallet));
            Assert.AreEqual(BetError.NotInBettingPhase, bet.ClearBets());
            Assert.AreEqual(BetError.NotInBettingPhase, bet.UndoLastChip());
            Assert.AreEqual(BetError.NotInBettingPhase, bet.RemoveBet(BetTarget.Straight(7)));
            Assert.AreEqual(BetError.NotInBettingPhase, bet.SetChipValue(1));
            Assert.AreEqual(0, wallet.Balance);

            bet.Unlock(40);
            Assert.IsFalse(bet.IsLocked);
            Assert.AreEqual(2, bet.Count, "베팅은 다음 회전에도 판에 남는다.");
            Assert.AreEqual(40, bet.ChipValue, "잔액보다 큰 칩 금액은 잔액으로 줄인다.");
        }
    }
}
