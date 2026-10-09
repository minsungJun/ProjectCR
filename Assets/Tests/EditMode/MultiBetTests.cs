using System.Linq;
using NUnit.Framework;

namespace ProjectCR.Tests
{
    /// <summary>
    /// 한 회전에 여러 베팅. 같은 대상에 다시 걸면 쌓이고, 합계는 회전 시작 때 한 번 차감된다.
    /// 정산은 베팅마다·공마다 판정해 더한다. 해골이면 전부 낙첨.
    /// </summary>
    public class MultiBetTests
    {
        private const int Skull = RouletteDefinition.SkullNumber;
        private const long Max = 999_999_999_999;

        private static readonly BetTarget Red = BetTarget.Outside(BetKind.Red);
        private static readonly BetTarget Black = BetTarget.Outside(BetKind.Black);
        private static readonly BetTarget Dozen1 = BetTarget.Outside(BetKind.Dozen1);

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
            flow = new GameFlowController(definition, balance, seed: 17);
        }

        [TearDown]
        public void TearDown() => assets.DestroyAll();

        private SettlementResult Settle(SpinResult spin, params ConfirmedBet[] bets)
        {
            return PayoutCalculator.Calculate(bets, spin, SpinConfig.Create(definition, balance), Max);
        }

        // ---------- 베팅판 ----------

        [Test]
        public void ChipsOnSameTarget_Stack()
        {
            flow.SetChipValue(10);
            flow.PlaceChip(Red);
            flow.PlaceChip(Red);
            flow.PlaceChip(Red);

            Assert.AreEqual(1, flow.Bet.Count);
            Assert.AreEqual(30, flow.Bet.StakeOn(Red));
            Assert.AreEqual(30, flow.Bet.TotalStake);
        }

        [Test]
        public void Bets_KeepPlacementOrder()
        {
            flow.PlaceBet(BetTarget.Straight(17), 5);
            flow.PlaceBet(Red, 10);
            flow.PlaceBet(BetTarget.Straight(17), 5);

            CollectionAssert.AreEqual(new[] { BetTarget.Straight(17), Red }, flow.Bet.Bets.Select(b => b.Target));
            Assert.AreEqual(10, flow.Bet.StakeOn(BetTarget.Straight(17)));
        }

        [Test]
        public void UndoLastChip_RemovesChipsInReverseOrder()
        {
            flow.PlaceBet(Red, 10);
            flow.PlaceBet(Red, 10);
            flow.PlaceBet(BetTarget.Straight(17), 5);

            flow.UndoLastChip();
            Assert.AreEqual(0, flow.Bet.StakeOn(BetTarget.Straight(17)));
            Assert.AreEqual(1, flow.Bet.Count);

            flow.UndoLastChip();
            Assert.AreEqual(10, flow.Bet.StakeOn(Red));

            flow.UndoLastChip();
            Assert.IsFalse(flow.Bet.HasBets);

            Assert.AreEqual(BetError.None, flow.UndoLastChip(), "빈 판에서 취소해도 오류가 아니다.");
        }

        [Test]
        public void RemoveBet_TakesWholeTarget_AndUndoSkipsIt()
        {
            flow.PlaceBet(Red, 10);
            flow.PlaceBet(BetTarget.Straight(17), 5);
            flow.PlaceBet(Red, 10);

            flow.RemoveBet(Red);
            Assert.AreEqual(1, flow.Bet.Count);
            Assert.AreEqual(5, flow.Bet.TotalStake);

            flow.UndoLastChip();
            Assert.IsFalse(flow.Bet.HasBets);
        }

        [Test]
        public void RemoveChip_TakesOneChipValue_ThenWholeTarget()
        {
            flow.SetChipValue(10);
            flow.PlaceChip(Red);
            flow.PlaceChip(Red);
            flow.PlaceChip(Red);
            flow.PlaceBet(BetTarget.Straight(17), 5);

            Assert.AreEqual(BetError.None, flow.RemoveChip(Red));
            Assert.AreEqual(20, flow.Bet.StakeOn(Red), "칩 하나(10)만 빠진다");
            flow.RemoveChip(Red);
            Assert.AreEqual(10, flow.Bet.StakeOn(Red));
            flow.RemoveChip(Red);
            Assert.AreEqual(0, flow.Bet.StakeOn(Red));
            Assert.AreEqual(1, flow.Bet.Count, "다 빼면 그 대상이 사라진다");
            Assert.AreEqual(BetError.None, flow.RemoveChip(Red), "빈 칸에서 빼도 오류가 아니다");
            Assert.AreEqual(5, flow.Bet.TotalStake);
        }

        [Test]
        public void RemoveChip_LessThanChipLeft_RemovesTarget()
        {
            flow.PlaceBet(Red, 15);
            flow.SetChipValue(10);
            flow.RemoveChip(Red);
            Assert.AreEqual(5, flow.Bet.StakeOn(Red));
            flow.RemoveChip(Red);
            Assert.IsFalse(flow.Bet.HasBets, "칩 금액보다 적게 남았으면 통째로 뺀다");
        }

        [Test]
        public void RemoveChip_KeepsUndoHistory_InLineWithStakes()
        {
            flow.SetChipValue(10);
            flow.PlaceChip(Red);
            flow.PlaceChip(BetTarget.Straight(17));
            flow.PlaceChip(Red);

            flow.RemoveChip(Red);
            Assert.AreEqual(10, flow.Bet.StakeOn(Red));

            flow.UndoLastChip();
            Assert.AreEqual(0, flow.Bet.StakeOn(BetTarget.Straight(17)), "마지막 기록은 17번 칩");
            flow.UndoLastChip();
            Assert.IsFalse(flow.Bet.HasBets, "빨강에 남은 10은 기록과 맞아 한 번에 빠진다");
        }

        [Test]
        public void MoveBet_MovesWholeStack_AndMergesIntoTarget()
        {
            flow.PlaceBet(Red, 10);
            flow.PlaceBet(Red, 10);
            flow.PlaceBet(Dozen1, 5);

            Assert.AreEqual(BetError.None, flow.MoveBet(Red, Dozen1));
            Assert.AreEqual(0, flow.Bet.StakeOn(Red));
            Assert.AreEqual(25, flow.Bet.StakeOn(Dozen1), "있던 5에 20이 합쳐진다");
            Assert.AreEqual(1, flow.Bet.Count);
            Assert.AreEqual(25, flow.Bet.TotalStake, "합계는 그대로");

            Assert.AreEqual(BetError.None, flow.MoveBet(Dozen1, Black));
            Assert.AreEqual(25, flow.Bet.StakeOn(Black), "빈 칸이면 그대로 옮긴다");
        }

        [Test]
        public void MoveBet_ToNone_RemovesBet_AndInvalidTarget_ChangesNothing()
        {
            flow.PlaceBet(Red, 10);
            flow.PlaceBet(Dozen1, 5);

            Assert.AreEqual(BetError.SkullNotBettable, flow.MoveBet(Red, BetTarget.Straight(RouletteDefinition.SkullNumber)));
            Assert.AreEqual(10, flow.Bet.StakeOn(Red), "걸 수 없는 칸이면 그대로");

            Assert.AreEqual(BetError.None, flow.MoveBet(Red, BetTarget.None));
            Assert.AreEqual(0, flow.Bet.StakeOn(Red), "판 밖에 놓으면 뺀다");
            Assert.AreEqual(5, flow.Bet.TotalStake);
        }

        [Test]
        public void MoveBet_UndoFollowsMovedChips()
        {
            flow.PlaceBet(Red, 10);
            flow.PlaceBet(Dozen1, 5);
            flow.PlaceBet(Red, 10);
            flow.MoveBet(Red, Black);

            flow.UndoLastChip();
            Assert.AreEqual(10, flow.Bet.StakeOn(Black), "마지막 칩(빨강 10)은 이제 검정에 있다");
            flow.UndoLastChip();
            Assert.AreEqual(0, flow.Bet.StakeOn(Dozen1));
            flow.UndoLastChip();
            Assert.IsFalse(flow.Bet.HasBets);
        }

        [Test]
        public void ClearBets_EmptiesBoard()
        {
            flow.PlaceBet(Red, 10);
            flow.PlaceBet(Dozen1, 10);

            flow.ClearBets();

            Assert.IsFalse(flow.Bet.HasBets);
            Assert.AreEqual(BetError.NoBetsPlaced, flow.ValidateBet());
        }

        [Test]
        public void PlacingChips_CannotExceedBalance()
        {
            flow.SetChipValue(60);

            Assert.AreEqual(BetError.None, flow.PlaceChip(Red));
            Assert.AreEqual(BetError.InsufficientFunds, flow.PlaceChip(Black));
            Assert.AreEqual(BetError.InsufficientFunds, flow.PlaceChip(Red), "같은 대상에 쌓을 때도 합계를 본다.");
            Assert.AreEqual(60, flow.Bet.TotalStake);
            Assert.AreEqual(100, flow.Wallet.Balance, "놓기만 해서는 돈이 빠지지 않는다.");
        }

        // ---------- 정산 ----------

        [Test]
        public void EachBet_IsSettled_AndPayoutsAreSummed()
        {
            // 32 = 빨강, 25–36
            SettlementResult s = Settle(SpinResult.FromNumbers(definition, 32),
                new ConfirmedBet(Red, 10), new ConfirmedBet(32, 5), new ConfirmedBet(Dozen1, 10));

            Assert.AreEqual(SpinOutcome.Win, s.Outcome);
            Assert.AreEqual(25, s.TotalStake);
            Assert.AreEqual(20 + 180, s.TotalPayout);
            Assert.AreEqual(175, s.Net);
            Assert.AreEqual(2, s.WinningBetCount);
            CollectionAssert.AreEqual(new long[] { 20, 180, 0 }, s.Bets.Select(b => b.Amount));
        }

        [Test]
        public void PayoutButNoProfit_IsStillAWin()
        {
            SettlementResult s = Settle(SpinResult.FromNumbers(definition, 32), new ConfirmedBet(Red, 10), new ConfirmedBet(Black, 10));

            Assert.AreEqual(SpinOutcome.Win, s.Outcome);
            Assert.AreEqual(20, s.TotalPayout);
            Assert.AreEqual(0, s.Net);
        }

        [Test]
        public void Zero_PaysOnlyBetsThatCoverZero()
        {
            SettlementResult s = Settle(SpinResult.FromNumbers(definition, 0),
                new ConfirmedBet(0, 10), new ConfirmedBet(Red, 10), new ConfirmedBet(BetTarget.Inside(BetKind.Corner, 0, 1, 2, 3), 10));

            CollectionAssert.AreEqual(new long[] { 360, 0, 90 }, s.Bets.Select(b => b.Amount));
        }

        [Test]
        public void Skull_LosesEveryBet()
        {
            SettlementResult s = Settle(SpinResult.FromNumbers(definition, 32, Skull),
                new ConfirmedBet(Red, 10), new ConfirmedBet(32, 5), new ConfirmedBet(Dozen1, 10));

            Assert.AreEqual(SpinOutcome.SkullBust, s.Outcome);
            Assert.AreEqual(0, s.TotalPayout);
            Assert.AreEqual(25, s.TotalStake);
            Assert.AreEqual(-25, s.Net);
            Assert.IsTrue(s.Bets.All(b => b.Amount == 0 && b.Hits == 0));
        }

        [Test]
        public void MultipleBalls_TimesMultipleBets()
        {
            // 공 32(빨강), 17(검정): 빨강은 한 번, 17번은 한 번 맞는다.
            SettlementResult s = Settle(SpinResult.FromNumbers(definition, 32, 17),
                new ConfirmedBet(Red, 10), new ConfirmedBet(17, 10), new ConfirmedBet(Black, 10));

            CollectionAssert.AreEqual(new long[] { 20, 360, 20 }, s.Bets.Select(b => b.Amount));
            CollectionAssert.AreEqual(new[] { 1, 1, 1 }, s.Bets.Select(b => b.Hits));
            Assert.AreEqual(3, s.WinCount);
        }

        // ---------- 진행 흐름 ----------

        [Test]
        public void Spin_ChargesTotalOnce_AndSettlesAllBets()
        {
            flow.ForcedNumbers = new[] { 32 };
            flow.PlaceBet(Red, 10);
            flow.PlaceBet(BetTarget.Straight(32), 5);
            flow.PlaceBet(Dozen1, 10);

            Assert.AreEqual(BetError.None, flow.TryBeginSpin(out ActiveSpin spin));
            Assert.AreEqual(75, flow.Wallet.Balance);
            Assert.AreEqual(3, spin.Bets.Count);
            Assert.AreEqual(4, flow.Round.SpinsRemaining, "베팅이 여러 개여도 회전은 1회.");

            Assert.IsTrue(flow.CompleteSpin(spin.Id, out SettlementResult s));
            Assert.IsFalse(flow.CompleteSpin(spin.Id, out _));
            Assert.AreEqual(200, s.TotalPayout);
            Assert.AreEqual(275, flow.Wallet.Balance);
        }

        [Test]
        public void Bets_AreCollectedAfterSpin()
        {
            flow.ForcedNumbers = new[] { 2 };
            flow.PlaceBet(Red, 10);
            flow.PlaceBet(Dozen1, 10);

            flow.TryBeginSpin(out ActiveSpin first);
            flow.CompleteSpin(first.Id, out _);
            Assert.AreEqual(110, flow.Wallet.Balance, "2(검정, 1–12): 빨강 낙첨, 1–12 30 지급.");

            Assert.IsFalse(flow.Bet.HasBets, "회전이 끝나면 판의 칩을 회수한다.");
            Assert.AreEqual(BetError.NoBetsPlaced, flow.TryBeginSpin(out _));
            Assert.AreEqual(110, flow.Wallet.Balance, "회수는 돈을 바꾸지 않는다.");

            Assert.AreEqual(BetError.None, flow.UndoLastChip(), "회수 뒤 취소할 칩 기록도 없다.");
            Assert.IsFalse(flow.Bet.HasBets);
        }

        [Test]
        public void ConsecutiveLosses_ResetOnAnyPayout()
        {
            flow.PlaceBet(Red, 10);
            flow.PlaceBet(Black, 10);

            flow.ForcedNumbers = new[] { 0 };
            flow.TryBeginSpin(out ActiveSpin a);
            flow.CompleteSpin(a.Id, out _);
            Assert.AreEqual(1, flow.ConsecutiveLosses);

            flow.ForcedNumbers = new[] { 32 };
            flow.PlaceBet(Red, 10);
            flow.PlaceBet(Black, 10);
            flow.TryBeginSpin(out ActiveSpin b);
            flow.CompleteSpin(b.Id, out SettlementResult s);
            Assert.AreEqual(0, s.Net, "빨강·검정 동시 베팅은 손익 0이지만 지급은 있다.");
            Assert.AreEqual(0, flow.ConsecutiveLosses);
        }
    }
}
