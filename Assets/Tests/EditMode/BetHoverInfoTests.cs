using System.Linq;
using NUnit.Framework;

namespace ProjectCR.Tests
{
    /// <summary>판 위 호버 툴팁 내용: 이름·대상·배율·예상 지급액이 정산과 같은 경로로 계산되고, 미리보기가 상태를 바꾸지 않는다.</summary>
    public class BetHoverInfoTests
    {
        private TestAssets assets;
        private RouletteDefinition definition;
        private GameBalance balance;

        private static readonly BetTarget Corner1245 = BetTarget.Inside(BetKind.Corner, 1, 2, 4, 5);

        [SetUp]
        public void SetUp()
        {
            assets = new TestAssets();
            definition = assets.Definition();
            balance = assets.Balance();
        }

        [TearDown]
        public void TearDown() => assets.DestroyAll();

        private GameFlowController Flow(ShopCatalog shop = null) => new GameFlowController(definition, balance, 5, assets.Drinks(), shop);

        private static BetHoverInfo Info(GameFlowController flow, BetTarget target) =>
            BetHoverInfo.Create(target, flow.BuildSpinConfig(), flow.Bet.ChipValue, flow.Wallet.MaxMoney,
                flow.Wallet.CanAfford(flow.Bet.TotalStake + flow.Bet.ChipValue));

        [Test]
        public void KindsAndBasePayouts_MatchTable()
        {
            GameFlowController flow = Flow();
            (BetTarget target, string name, int payout, string targets)[] cases =
            {
                (BetTarget.Straight(7), "단독", 36, "7"),
                (BetTarget.Straight(0), "단독", 36, "0"),
                (BetTarget.Inside(BetKind.Split, 2, 3), "스플릿", 18, "2 · 3"),
                (BetTarget.Inside(BetKind.Street, 4, 5, 6), "스트리트", 12, "4 · 5 · 6"),
                (Corner1245, "코너", 9, "1 · 2 · 4 · 5"),
                (BetTarget.Inside(BetKind.SixLine, 1, 2, 3, 4, 5, 6), "식스라인", 6, "1 · 2 · 3 · 4 · 5 · 6"),
                (BetTarget.Inside(BetKind.Street, 0, 1, 2), "트리오", 12, "0 · 1 · 2"),
                (BetTarget.Inside(BetKind.Corner, 0, 1, 2, 3), "퍼스트 포", 9, "0 · 1 · 2 · 3"),
                (BetTarget.Outside(BetKind.Column1), "첫 번째 컬럼", 3, "1, 4, 7 … 34"),
                (BetTarget.Outside(BetKind.Dozen1), "첫 번째 더즌", 3, "1~12"),
                (BetTarget.Outside(BetKind.Dozen3), "세 번째 더즌", 3, "25~36"),
                (BetTarget.Outside(BetKind.Odd), "홀수", 2, "1, 3, 5 … 35"),
                (BetTarget.Outside(BetKind.Red), "빨강", 2, "빨간 숫자 18개"),
                (BetTarget.Outside(BetKind.High), "HIGH", 2, "19~36"),
            };

            foreach (var c in cases)
            {
                BetHoverInfo info = Info(flow, c.target);
                Assert.AreEqual(c.name, info.KindName, c.target.ToString());
                Assert.AreEqual(c.payout, info.BasePayout, c.target.ToString());
                Assert.AreEqual((decimal)c.payout, info.Multiplier, "강화 없으면 최종 = 기본");
                Assert.IsFalse(info.IsBoosted);
                Assert.AreEqual(c.targets, info.Targets, c.target.ToString());
            }
        }

        [Test]
        public void DrinkBoost_NumberIsWholeGroup_OthersExactTarget()
        {
            GameFlowController flow = Flow();
            flow.Drinks.SetGroupStacks(DrinkGroup.Number, 1);
            flow.SetChipValue(10);

            BetHoverInfo corner = Info(flow, Corner1245);
            Assert.AreEqual(13.5m, corner.Multiplier, "코너 ×9 × 숫자 음료 1.5");
            Assert.AreEqual(135, corner.ExpectedPayout, "칩 10 → 135");
            Assert.IsTrue(corner.IsBoosted);
            Assert.AreEqual(54m, Info(flow, BetTarget.Straight(0)).Multiplier, "0도 숫자 베팅");
            Assert.AreEqual(2m, Info(flow, BetTarget.Outside(BetKind.Red)).Multiplier, "숫자 음료는 색깔 베팅에 붙지 않는다");

            flow.Drinks.SetStacks(BetTarget.Outside(BetKind.Dozen1), 1);
            BetHoverInfo dozen = Info(flow, BetTarget.Outside(BetKind.Dozen1));
            Assert.AreEqual(4.5m, dozen.Multiplier);
            Assert.AreEqual(45, dozen.ExpectedPayout);
            Assert.AreEqual(3m, Info(flow, BetTarget.Outside(BetKind.Dozen2)).Multiplier, "1–12 음료는 13–24에 붙지 않는다");
        }

        [Test]
        public void ChipValue_ChangesExpectedPayout_NotPlacedStakes()
        {
            GameFlowController flow = Flow();
            flow.SetChipValue(10);
            Assert.AreEqual(BetError.None, flow.PlaceChip(Corner1245));
            Assert.AreEqual(BetError.None, flow.PlaceChip(Corner1245));
            Assert.AreEqual(90, Info(flow, Corner1245).ExpectedPayout, "이미 놓인 칩 20은 더하지 않는다");

            flow.SetChipValue(50);
            Assert.AreEqual(450, Info(flow, Corner1245).ExpectedPayout);
            Assert.AreEqual(50, Info(flow, Corner1245).ChipValue);
        }

        [Test]
        public void Affordable_FollowsAvailableFunds()
        {
            GameFlowController flow = Flow();
            flow.SetChipValue(50);
            Assert.IsTrue(Info(flow, Corner1245).Affordable);

            flow.SetChipValue(flow.Wallet.Balance);
            flow.PlaceChip(BetTarget.Outside(BetKind.Red));
            flow.SetChipValue(10);
            Assert.IsFalse(Info(flow, Corner1245).Affordable, "판의 칩이 잔액을 다 쓰면 한 개 더 놓을 수 없다");
        }

        [Test]
        public void Preview_EqualsActualSettlement_WithFocus()
        {
            GameFlowController flow = Flow();
            flow.Drinks.SetGroupStacks(DrinkGroup.Number, 1);
            flow.Upgrades.AddFocus(DrinkGroup.Number, BetTarget.Straight(1), 0.1m);
            flow.SetChipValue(10);

            BetHoverInfo info = Info(flow, Corner1245);
            Assert.AreEqual(14.85m, info.Multiplier, "9 × 1.5 × (1 + 0.1)");
            Assert.AreEqual(148, info.ExpectedPayout, "148.5 버림");

            flow.ForcedNumbers = new[] { 1 };
            Assert.AreEqual(BetError.None, flow.PlaceChip(Corner1245));
            Assert.AreEqual(BetError.None, flow.TryBeginSpin(out ActiveSpin spin));
            Assert.IsTrue(flow.CompleteSpin(spin.Id, out SettlementResult settlement));
            Assert.AreEqual(info.ExpectedPayout, settlement.TotalPayout);
        }

        [Test]
        public void PendingCross_IsIncluded_AndPreviewChangesNothing()
        {
            ShopItemDefinition cross = assets.Shop().Items.Single(i => i.Id == "cross_color_column");
            GameFlowController flow = Flow(ShopCatalog.Create(new[] { cross }, true));
            flow.Wallet.Add(1000);
            flow.EnterShop();
            Assert.AreEqual(ShopError.None, flow.Shop.Buy(0, BetTarget.None));
            flow.CloseShop();

            BetTarget column1 = BetTarget.Outside(BetKind.Column1);
            flow.SetChipValue(10);
            Assert.AreEqual(3m, Info(flow, column1).Multiplier, "아직 발동 전: 다음 회전 효과를 미리 넣지 않는다");

            // 빨강 당첨 → 교차 발동(다음 회전 COLUMN +25%).
            flow.ForcedNumbers = new[] { 1 };
            flow.PlaceChip(BetTarget.Outside(BetKind.Red));
            flow.TryBeginSpin(out ActiveSpin first);
            flow.CompleteSpin(first.Id, out _);
            Assert.AreEqual(0.25m, flow.Cross.Pending(DrinkGroup.Column));

            // 미리보기를 여러 번 해도 돈·베팅·교차·아이템·강화가 그대로.
            long balanceBefore = flow.Wallet.Balance;
            int spinsBefore = flow.Round.SpinsRemaining;
            BetHoverInfo info = default;
            for (int i = 0; i < 3; i++)
            {
                info = Info(flow, column1);
            }

            Assert.AreEqual(3.75m, info.Multiplier, "이미 확보한 교차 효과는 반영");
            Assert.AreEqual(37, info.ExpectedPayout, "37.5 버림");
            Assert.AreEqual(balanceBefore, flow.Wallet.Balance);
            Assert.AreEqual(spinsBefore, flow.Round.SpinsRemaining);
            Assert.AreEqual(0.25m, flow.Cross.Pending(DrinkGroup.Column));
            Assert.IsFalse(flow.Bet.HasBets);
            Assert.AreEqual(0, flow.Inventory.Shields);
            Assert.AreEqual(0, flow.Upgrades.TotalStacks);

            // 실제 정산도 같은 값.
            flow.ForcedNumbers = new[] { 4 };
            flow.PlaceChip(column1);
            flow.TryBeginSpin(out ActiveSpin second);
            flow.CompleteSpin(second.Id, out SettlementResult settlement);
            Assert.AreEqual(info.ExpectedPayout, settlement.TotalPayout);
        }

        [Test]
        public void MultipleBalls_ShowOneBallPayout()
        {
            GameFlowController flow = Flow();
            flow.SetChipValue(10);
            long single = Info(flow, Corner1245).ExpectedPayout;

            flow.ExtraBallCount = 1;
            BetHoverInfo info = Info(flow, Corner1245);
            Assert.AreEqual(2, info.BallCount);
            Assert.AreEqual(single, info.ExpectedPayout, "공 여러 개여도 공 하나 적중 기준");
        }
    }
}
