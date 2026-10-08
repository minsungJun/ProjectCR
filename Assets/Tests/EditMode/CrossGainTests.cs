using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ProjectCR.Tests
{
    /// <summary>
    /// 누적형 교차 강화(DOZEN → COLUMN): DOZEN 베팅의 원금 포함 지급액을 이번 라운드 동안 누적해 목표(납부금 50%, 올림)마다 다음 회전 COLUMN +25%.
    /// 1라운드 납부금 120 → 목표 60, DOZEN ×3이라 DOZEN 10이 맞으면 지급 30.
    /// </summary>
    public class CrossGainTests
    {
        private const string ItemId = "cross_dozen_column";

        private static readonly BetTarget Dozen1 = BetTarget.Outside(BetKind.Dozen1);
        private static readonly BetTarget Dozen2 = BetTarget.Outside(BetKind.Dozen2);
        private static readonly BetTarget Column1 = BetTarget.Outside(BetKind.Column1);
        private static readonly BetTarget Red = BetTarget.Outside(BetKind.Red);

        private TestAssets assets;
        private RouletteDefinition definition;
        private GameBalance balance;
        private List<ShopItemDefinition> all;

        [SetUp]
        public void SetUp()
        {
            assets = new TestAssets();
            definition = assets.Definition();
            balance = assets.Balance();
            all = assets.Shop().Items.ToList();
        }

        [TearDown]
        public void TearDown() => assets.DestroyAll();

        private ShopItemDefinition Item(string id) => all.Single(i => i.Id == id);

        private GameFlowController NewFlow(params string[] ids) =>
            new GameFlowController(definition, balance, 3, null, ShopCatalog.Create(all.Where(i => ids.Contains(i.Id)), openOnFirstRound: true));

        /// <summary>1라운드 첫 회전 전에 상품을 사 둔 흐름.</summary>
        private GameFlowController WithItem(string id = ItemId)
        {
            GameFlowController flow = NewFlow(id);
            flow.Wallet.Add(100_000);
            flow.EnterShop();
            Assert.AreEqual(ShopError.None, flow.Shop.Buy(0, BetTarget.None));
            flow.CloseShop();
            return flow;
        }

        private static SettlementResult Spin(GameFlowController flow, int[] numbers, params (BetTarget target, long stake)[] bets)
        {
            flow.ExtraBallCount = numbers.Length - 1;
            flow.ForcedNumbers = numbers;
            foreach (var bet in bets)
            {
                Assert.AreEqual(BetError.None, flow.PlaceBet(bet.target, bet.stake));
            }

            Assert.AreEqual(BetError.None, flow.TryBeginSpin(out ActiveSpin spin));
            Assert.IsTrue(flow.CompleteSpin(spin.Id, out SettlementResult settlement));
            return settlement;
        }

        private static decimal Progress(GameFlowController flow) => flow.Cross.ProgressOf(DrinkGroup.Dozen, DrinkGroup.Column);

        private static decimal PendingColumn(GameFlowController flow) => flow.Cross.Pending(DrinkGroup.Column);

        // ---------- 데이터 ----------

        [Test]
        public void Defaults_OnlyDozenColumnUsesGain()
        {
            Assert.AreEqual(CrossTriggerMode.Gain, Item(ItemId).CrossTrigger);
            Assert.AreEqual(0.5m, Item(ItemId).GainThresholdRatio);
            Assert.AreEqual(60, Item(ItemId).GainThreshold(120));
            Assert.AreEqual(150, Item(ItemId).GainThreshold(300));
            Assert.AreEqual(1, Item(ItemId).GainThreshold(1), "올림, 최소 1");
            Assert.IsTrue(all.Where(i => i.Effect == ShopEffect.Cross && i.Id != ItemId).All(i => i.CrossTrigger == CrossTriggerMode.Win),
                "나머지 교차 강화는 당첨 발동 그대로");

            Assert.AreEqual("이번 라운드 DOZEN 누적 지급 60마다\n다음 회전 COLUMN +25%", Item(ItemId).CardTextFor(120));
            StringAssert.Contains("납부금의 50%", Item(ItemId).CardTextFor(0));
        }

        // ---------- 누적 · 목표 ----------

        [Test]
        public void SmallWins_BelowThreshold_DoNotTrigger()
        {
            GameFlowController flow = WithItem();
            for (int i = 0; i < 3; i++)
            {
                SettlementResult s = Spin(flow, new[] { 1 }, (Dozen1, 1));
                Assert.AreEqual(0, s.CrossTriggers.Count);
            }

            Assert.AreEqual(9m, Progress(flow), "지급 3씩 세 번");
            Assert.AreEqual(0m, PendingColumn(flow));
        }

        [Test]
        public void AccumulatesAcrossSpins_TriggersAtThreshold()
        {
            GameFlowController flow = WithItem();
            Spin(flow, new[] { 1 }, (Dozen1, 10));
            Assert.AreEqual(30m, Progress(flow));
            Assert.AreEqual(0m, PendingColumn(flow));

            SettlementResult s = Spin(flow, new[] { 1 }, (Dozen1, 10));
            Assert.AreEqual(0m, Progress(flow), "30 + 30 = 60 → 1회, 나머지 0");
            Assert.AreEqual(0.25m, PendingColumn(flow));
            Assert.AreEqual(1, s.CrossTriggers.Single().Count);
            Assert.AreEqual(CrossTriggerMode.Gain, s.CrossTriggers.Single().Mode);
        }

        [Test]
        public void ExactlyThreshold_TriggersOnce_GrossPayoutNotNet()
        {
            GameFlowController flow = WithItem();
            SettlementResult s = Spin(flow, new[] { 1 }, (Dozen1, 20));
            Assert.AreEqual(40, s.Net, "순이익은 40");
            Assert.AreEqual(1, s.CrossTriggers.Single().Count, "원금 포함 지급 60으로 센다");
            Assert.AreEqual(0m, Progress(flow));
        }

        [Test]
        public void Multiples_GiveCountAndCarryRemainder()
        {
            GameFlowController flow = WithItem();
            Spin(flow, new[] { 1 }, (Dozen1, 17));
            Assert.AreEqual(51m, Progress(flow));

            SettlementResult s = Spin(flow, new[] { 1 }, (Dozen1, 60));
            CrossTrigger trigger = s.CrossTriggers.Single();
            Assert.AreEqual(3, trigger.Count, "51 + 180 = 231 → 3회");
            Assert.AreEqual(0.75m, trigger.Increase);
            Assert.AreEqual(51m, Progress(flow), "231 − 180 = 51 이월");
            Assert.AreEqual(0.75m, PendingColumn(flow), "세 번 강화량을 다음 회전에 합산");
        }

        [Test]
        public void OtherGroups_AreNotCounted()
        {
            GameFlowController flow = WithItem();
            SettlementResult s = Spin(flow, new[] { 1 }, (Column1, 30), (Red, 30));
            Assert.Greater(s.TotalPayout, 0);
            Assert.AreEqual(0m, Progress(flow));
            Assert.AreEqual(0, s.CrossTriggers.Count);
        }

        [Test]
        public void MultipleBetsAndBalls_SumOnce()
        {
            GameFlowController flow = WithItem();

            // 공 2개(1, 13): DOZEN1 5 → 15, DOZEN2 5 → 15. 합계 30만 센다(베팅·공 중복 없음).
            Spin(flow, new[] { 1, 13 }, (Dozen1, 5), (Dozen2, 5));
            Assert.AreEqual(30m, Progress(flow));

            // 공 2개가 모두 DOZEN1(1, 2): 같은 베팅이 두 번 맞음 → 15 × 2 = 30 → 누적 60 → 1회.
            SettlementResult s = Spin(flow, new[] { 1, 2 }, (Dozen1, 5));
            Assert.AreEqual(1, s.CrossTriggers.Single().Count);
            Assert.AreEqual(0m, Progress(flow));
        }

        [Test]
        public void DrinkBoostedPayout_KeepsFractions()
        {
            GameFlowController flow = new GameFlowController(definition, balance, 3, assets.Drinks(),
                ShopCatalog.Create(all.Where(i => i.Id == ItemId), openOnFirstRound: true));
            flow.Wallet.Add(100_000);
            flow.EnterShop();
            flow.Shop.Buy(0, BetTarget.None);
            flow.CloseShop();
            flow.Drinks.SetStacks(DrinkGroup.Dozen, 1);

            // DOZEN ×3 × 1.5 = ×4.5: 3 → 13.5(버림 없이 누적).
            Spin(flow, new[] { 1 }, (Dozen1, 3));
            Assert.AreEqual(13.5m, Progress(flow));
        }

        [Test]
        public void SkullAndInsurance_AddNothing()
        {
            GameFlowController flow = NewFlow(ItemId, "insurance");
            flow.Wallet.Add(100_000);
            flow.EnterShop();
            for (int i = 0; i < flow.Shop.Offers.Count; i++)
            {
                Assert.AreEqual(ShopError.None, flow.Shop.Buy(i, BetTarget.None));
            }

            // 보험은 회전마다 1장씩 자동으로 쓰이니(해골 회전도 소비) 2장 산다.
            int insurance = flow.Shop.Offers.ToList().FindIndex(o => o.Item.Id == "insurance");
            Assert.AreEqual(ShopError.None, flow.Shop.Buy(insurance, BetTarget.None));
            flow.CloseShop();

            SettlementResult skull = Spin(flow, new[] { RouletteDefinition.SkullNumber }, (Dozen1, 30));
            Assert.IsTrue(skull.IsSkullBust);
            Assert.AreEqual(0m, Progress(flow));

            SettlementResult insured = Spin(flow, new[] { 30 }, (Dozen1, 30));
            Assert.Greater(insured.InsuranceRefund, 0, "보험 환급은 있지만");
            Assert.AreEqual(0m, Progress(flow), "누적에는 안 들어간다");
        }

        [Test]
        public void PayoutsBeforePurchase_AreNotCounted()
        {
            GameFlowController flow = NewFlow(ItemId);
            flow.Wallet.Add(100_000);

            // 1라운드: 상품 없이 DOZEN 당첨(지급 90 × 5회) → 라운드 통과.
            while (flow.Phase == GamePhase.Betting)
            {
                Spin(flow, new[] { 1 }, (Dozen1, 30));
            }

            flow.SubmitPayment();
            Assert.AreEqual(2, flow.Round.Round);

            flow.EnterShop();
            Assert.AreEqual(ShopError.None, flow.Shop.Buy(0, BetTarget.None));
            flow.CloseShop();
            Assert.AreEqual(0m, Progress(flow), "산 뒤부터만 센다");
            Assert.AreEqual(0m, PendingColumn(flow));
        }

        // ---------- 적용 ----------

        [Test]
        public void Boost_AppliesToNextSpinOnly()
        {
            GameFlowController flow = WithItem();
            Spin(flow, new[] { 1 }, (Dozen1, 20)); // 60 → 다음 회전 COLUMN +25%

            SettlementResult boosted = Spin(flow, new[] { 1 }, (Column1, 10));
            Assert.AreEqual(37, boosted.TotalPayout, "10 × 3 × 1.25 = 37.5 버림");
            Assert.AreEqual(0m, PendingColumn(flow));

            SettlementResult plain = Spin(flow, new[] { 1 }, (Column1, 10));
            Assert.AreEqual(30, plain.TotalPayout, "그다음 회전은 원래대로");
        }

        [Test]
        public void RoundChange_ResetsShortfall_KeepsPending_UsesNewDue()
        {
            GameFlowController flow = WithItem();
            Spin(flow, new[] { 1 }, (Dozen1, 10)); // 30
            Spin(flow, new[] { 1 }, (Dozen1, 10)); // 60 → 1회
            Spin(flow, new[] { 1 }, (Column1, 1)); // 강화 사용
            Spin(flow, new[] { 1 }, (Dozen1, 10)); // 30
            SettlementResult last = Spin(flow, new[] { 1 }, (Dozen1, 15)); // 30 + 45 = 75 → 1회, 15 남음
            Assert.AreEqual(1, last.CrossTriggers.Single().Count);
            Assert.AreEqual(15m, Progress(flow));
            Assert.AreEqual(GamePhase.RoundPayment, flow.Phase);

            flow.SubmitPayment();
            Assert.AreEqual(2, flow.Round.Round);
            Assert.AreEqual(0m, Progress(flow), "라운드가 바뀌면 미달분은 버린다");
            Assert.AreEqual(0.25m, PendingColumn(flow), "마지막 회전에 얻은 강화는 다음 라운드 첫 회전까지");

            long due = flow.Round.CurrentDue;
            Assert.AreEqual(Item(ItemId).GainThreshold(due), flow.Cross.ThresholdOf(DrinkGroup.Dozen, DrinkGroup.Column, due));
            Assert.AreEqual(150, flow.Cross.ThresholdOf(DrinkGroup.Dozen, DrinkGroup.Column, due), "2라운드 납부금 300의 50%");

            SettlementResult first = Spin(flow, new[] { 1 }, (Column1, 10));
            Assert.AreEqual(37, first.TotalPayout, "다음 라운드 첫 회전에 적용");
        }

        [Test]
        public void PerSpinCap_DiscardsExtraMultiples()
        {
            ShopItemDefinition item = Item(ItemId);
            typeof(ShopItemDefinition).GetField("gainTriggerCapPerSpin",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(item, 2);

            GameFlowController flow = WithItem();
            SettlementResult s = Spin(flow, new[] { 1 }, (Dozen1, 70)); // 210 → 3배수지만 상한 2
            Assert.AreEqual(2, s.CrossTriggers.Single().Count);
            Assert.AreEqual(30m, Progress(flow), "나머지(210 − 180)만 이월, 넘친 배수는 버림");
            Assert.AreEqual(0.5m, PendingColumn(flow));
        }

        // ---------- 부작용 없음 ----------

        [Test]
        public void PreviewAndHover_DoNotChangeProgress()
        {
            GameFlowController flow = WithItem();
            Spin(flow, new[] { 1 }, (Dozen1, 10));

            for (int i = 0; i < 5; i++)
            {
                SpinConfig config = flow.BuildSpinConfig();
                BetHoverInfo.Create(Dozen1, config, 10, flow.Wallet.MaxMoney, true,
                    BetHoverInfo.CrossProgressOf(Dozen1, flow.Cross, flow.Round.CurrentDue));
            }

            Assert.AreEqual(30m, Progress(flow));
            Assert.AreEqual("COLUMN 강화까지 30 / 60", BetHoverInfo.CrossProgressOf(Dozen1, flow.Cross, flow.Round.CurrentDue));
            Assert.AreEqual(string.Empty, BetHoverInfo.CrossProgressOf(Column1, flow.Cross, flow.Round.CurrentDue), "발동 묶음 위에서만");
        }

        [Test]
        public void WinTriggerCross_StillTriggersOnAnyWin()
        {
            GameFlowController flow = WithItem("cross_color_column");
            SettlementResult s = Spin(flow, new[] { 1 }, (Red, 1));
            Assert.AreEqual(1, s.CrossTriggers.Single().Count);
            Assert.AreEqual(CrossTriggerMode.Win, s.CrossTriggers.Single().Mode);
            Assert.AreEqual(0.25m, PendingColumn(flow));
        }
    }
}
