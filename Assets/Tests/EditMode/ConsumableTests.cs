using System.Linq;
using NUnit.Framework;

namespace ProjectCR.Tests
{
    /// <summary>소모품(추가 공, 해골 방패, 여분의 회전권, 보험 증서)과 납부 지원금. 보관 → 보유 아이템 탭에서 사용.</summary>
    public class ConsumableTests
    {
        private const int Skull = RouletteDefinition.SkullNumber;
        private static readonly BetTarget Red = BetTarget.Outside(BetKind.Red);

        private TestAssets assets;
        private RouletteDefinition definition;
        private GameBalance balance;
        private ShopCatalog full;

        [SetUp]
        public void SetUp()
        {
            assets = new TestAssets();
            definition = assets.Definition();
            balance = assets.Balance();
            full = assets.Shop();
        }

        [TearDown]
        public void TearDown() => assets.DestroyAll();

        /// <summary>그 상품만 있는 상점에서 n개 사서 보관한 상태(상점 안).</summary>
        private GameFlowController Bought(string id, int count, int seed = 5)
        {
            ShopCatalog only = ShopCatalog.Create(full.Items.Where(i => i.Id == id), paymentSupportChance: 1f, openOnFirstRound: true);
            var flow = new GameFlowController(definition, balance, seed, null, only);
            flow.Wallet.Add(10_000);
            Assert.IsTrue(flow.EnterShop());
            for (int i = 0; i < count; i++)
            {
                Assert.AreEqual(ShopError.None, flow.Shop.Buy(0, BetTarget.None));
            }

            return flow;
        }

        private static SettlementResult Spin(GameFlowController flow, BetTarget target, long stake, params int[] forced)
        {
            flow.ForcedNumbers = forced;
            Assert.AreEqual(BetError.None, flow.PlaceBet(target, stake));
            Assert.AreEqual(BetError.None, flow.TryBeginSpin(out ActiveSpin spin));
            Assert.IsTrue(flow.CompleteSpin(spin.Id, out SettlementResult settlement));
            return settlement;
        }

        private static void FinishRound(GameFlowController flow)
        {
            while (flow.Phase == GamePhase.Betting)
            {
                Spin(flow, BetTarget.Straight(7), 1, 8);
            }

            Assert.AreEqual(PaymentOutcome.Paid, flow.SubmitPayment());
        }

        // ---------- 추가 공 (사는 즉시 적용) ----------

        [Test]
        public void ExtraBall_AppliesOnPurchase_UntilRoundEnd()
        {
            GameFlowController flow = Bought("extra_ball", 2);
            Assert.AreEqual(3, flow.BuildSpinConfig().BallCount, "1 + 2, 사는 즉시");
            Assert.AreEqual(0, flow.Inventory.CountOf(ShopEffect.ExtraBall), "보관하지 않는다.");
            flow.CloseShop();

            SettlementResult s = Spin(flow, Red, 10, 32, 34, 1);
            Assert.AreEqual(3, s.Spin.Balls.Count);
            Assert.AreEqual(60, s.TotalPayout, "공마다 따로 지급");

            FinishRound(flow);
            Assert.AreEqual(1, flow.BuildSpinConfig().BallCount, "라운드가 끝나면 추가 공 효과도 끝.");
        }

        [Test]
        public void ExtraBall_CannotBeBought_AtMax()
        {
            ShopCatalog only = ShopCatalog.Create(full.Items.Where(i => i.Id == "extra_ball"), openOnFirstRound: true);
            var flow = new GameFlowController(definition, balance, 5, null, only);
            flow.Wallet.Add(10_000);
            flow.ExtraBallCount = 2; // 이미 3개(최대)
            flow.EnterShop();
            long before = flow.Wallet.Balance;

            Assert.AreEqual(ShopError.MaxBalls, flow.Shop.Buy(0, BetTarget.None));
            Assert.AreEqual(before, flow.Wallet.Balance, "거절되면 돈이 빠지지 않는다.");
        }

        // ---------- 해골 방패 (해골이 나오면 자동) ----------

        [Test]
        public void Shield_IsUsedAutomatically_RerollingOnlyTheSkullBall()
        {
            GameFlowController flow = Bought("skull_shield", 2);
            flow.CloseShop();
            long before = flow.Wallet.Balance;
            int spins = flow.Round.SpinsRemaining;
            flow.ExtraBallCount = 1;

            SettlementResult s = Spin(flow, Red, 10, 32, Skull);

            Assert.AreEqual(1, s.ShieldsUsed);
            Assert.IsFalse(s.Spin.HasSkull, "해골 공만 다시 뽑았다.");
            Assert.AreEqual(32, s.Spin.Balls[0].Number, "다른 공 결과는 그대로.");
            Assert.AreEqual(spins - 1, flow.Round.SpinsRemaining, "회전 1번만 쓴다.");
            Assert.AreEqual(before - 10 + s.TotalPayout, flow.Wallet.Balance, "추가 베팅금 없음, 해골 벌칙 없음.");
            Assert.AreEqual(1, flow.Inventory.Shields);
        }

        [Test]
        public void Shield_NotUsed_WithoutSkull()
        {
            GameFlowController flow = Bought("skull_shield", 1);
            flow.CloseShop();
            SettlementResult s = Spin(flow, Red, 10, 32);
            Assert.AreEqual(0, s.ShieldsUsed);
            Assert.AreEqual(1, flow.Inventory.Shields, "해골이 안 나오면 아끼어 둔다.");
        }

        [Test]
        public void Shield_NotEnough_StillBusts()
        {
            GameFlowController flow = Bought("skull_shield", 1);
            flow.CloseShop();
            flow.ExtraBallCount = 1;

            // 방패 1개로 해골 공 둘 중 하나만 막는다.
            SettlementResult s = Spin(flow, Red, 10, Skull, Skull);
            Assert.AreEqual(1, s.ShieldsUsed);
            Assert.AreEqual(SpinOutcome.SkullBust, s.Outcome);
            Assert.AreEqual(0, flow.Inventory.Shields);
        }

        // ---------- 여분의 회전권 (사는 즉시) ----------

        [Test]
        public void SpareSpin_AddsOneSpin_OnPurchase()
        {
            GameFlowController flow = Bought("spare_spin", 1);
            Assert.AreEqual(6, flow.Round.SpinsRemaining);
            Assert.AreEqual(0, flow.Inventory.CountOf(ShopEffect.SpareSpin));
        }

        // ---------- 보험 증서 (회전마다 1장 자동) ----------

        [Test]
        public void Insurance_UsesOnePerSpin_RefundsHalfOnPlainLoss()
        {
            GameFlowController flow = Bought("insurance", 2);
            Assert.AreEqual(2, flow.Inventory.Insurances);
            flow.CloseShop();
            long before = flow.Wallet.Balance;

            SettlementResult lose = Spin(flow, Red, 21, 2);
            Assert.AreEqual(10, lose.InsuranceRefund, "21의 50% = 10.5 → 10");
            Assert.AreEqual(before - 21 + 10, flow.Wallet.Balance);
            Assert.AreEqual(1, flow.Inventory.Insurances, "회전마다 1장씩.");

            Assert.AreEqual(10, Spin(flow, Red, 20, 2).InsuranceRefund, "두 번째 장도 자동.");
            Assert.AreEqual(0, Spin(flow, Red, 20, 2).InsuranceRefund, "다 쓰면 환급 없음.");
        }

        [Test]
        public void Insurance_NoRefund_OnWinOrSkull_ButStillUsed()
        {
            GameFlowController flow = Bought("insurance", 2);
            flow.CloseShop();
            Assert.AreEqual(0, Spin(flow, Red, 20, 32).InsuranceRefund, "당첨이면 환급 없음.");
            Assert.AreEqual(1, flow.Inventory.Insurances, "당첨이어도 그 회전에 1장 쓴다.");

            SettlementResult skull = Spin(flow, Red, 20, Skull);
            Assert.AreEqual(SpinOutcome.SkullBust, skull.Outcome);
            Assert.AreEqual(0, skull.InsuranceRefund, "해골 낙첨에는 환급 없음.");
            Assert.AreEqual(0, flow.Inventory.Insurances);
        }

        // ---------- 납부 지원금(지속 효과) ----------

        [Test]
        public void PaymentSupport_RefundsShareOfPaidDue_EveryRound()
        {
            GameFlowController flow = Bought("payment_support", 1);
            long afterBuy = flow.Wallet.Balance;
            Assert.AreEqual(0.25m, flow.Upgrades.PaymentRefund);
            Assert.AreEqual(0, flow.Inventory.CountOf(ShopEffect.PaymentSupport), "소모품이 아니라 바로 적용된다.");
            flow.CloseShop();

            // 1라운드: 회전 5번(−5) 후 120 납부 → 30 환급.
            FinishRound(flow);
            Assert.AreEqual(120, flow.LastPaidDue);
            Assert.AreEqual(30, flow.LastPaymentRefund, "낸 납부금 120의 25%");
            Assert.AreEqual(afterBuy - 5 - 120 + 30, flow.Wallet.Balance);
            Assert.AreEqual(2, flow.Round.Round);

            // 2라운드에도 이어진다: 300 납부 → 75 환급.
            FinishRound(flow);
            Assert.AreEqual(300, flow.LastPaidDue);
            Assert.AreEqual(75, flow.LastPaymentRefund);
        }

        [Test]
        public void PaymentSupport_BoughtOnce_ThenNotOffered()
        {
            GameFlowController flow = Bought("payment_support", 1);
            Assert.AreNotEqual(ShopError.None, flow.Shop.Buy(0, BetTarget.None), "같은 라운드에 또 살 수 없다.");
            flow.CloseShop();
            FinishRound(flow);

            flow.EnterShop();
            Assert.AreEqual(0, flow.Shop.Offers.Count, "한 번 산 지속 효과는 다시 진열되지 않는다.");
            Assert.AreEqual(0.25m, flow.Upgrades.PaymentRefund, "효과는 그대로.");
        }

        [Test]
        public void PaymentSupport_NoRefund_WhenPaymentFails_OrWithoutIt()
        {
            ShopCatalog only = ShopCatalog.Create(full.Items.Where(i => i.Id == "payment_support"), paymentSupportChance: 1f, openOnFirstRound: true);
            var flow = new GameFlowController(definition, balance, 5, null, only);
            flow.EnterShop();
            flow.Shop.Buy(0, BetTarget.None);
            flow.CloseShop();

            Spin(flow, BetTarget.Straight(7), flow.Wallet.Balance, 8);
            Assert.AreEqual(PaymentOutcome.Insufficient, flow.SubmitPayment());
            Assert.AreEqual(0, flow.LastPaymentRefund, "납부에 실패하면 환급도 없다.");
            Assert.AreEqual(0, flow.Wallet.Balance);

            var plain = new GameFlowController(definition, balance, 5);
            plain.Wallet.Add(1000);
            FinishRound(plain);
            Assert.AreEqual(0, plain.LastPaymentRefund, "지원금이 없으면 환급 0.");
        }

        [Test]
        public void PaymentSupport_PriceGrows_LikeOtherPermanents()
        {
            ShopItemDefinition support = full.Items.First(i => i.Id == "payment_support");
            Assert.IsTrue(support.IsPermanent);
            Assert.AreEqual(20, support.PriceFor(120, 0), "120 × 15% = 18 → 20");
            Assert.AreEqual(30, support.PriceFor(120, 1), "× 1.5 = 27 → 30");
        }

        // ---------- 상점에서 돈을 다 쓴 경우 ----------

        [Test]
        public void SpendingAllMoney_ThenClosing_GoesToPayment()
        {
            ShopCatalog only = ShopCatalog.Create(full.Items.Where(i => i.Id == "extra_ball"), openOnFirstRound: true);
            var flow = new GameFlowController(definition, balance, 5, null, only);
            flow.Wallet.TrySpend(55); // 보유 45 = 추가 공 가격
            flow.EnterShop();
            Assert.AreEqual(ShopError.None, flow.Shop.Buy(0, BetTarget.None));
            Assert.AreEqual(0, flow.Wallet.Balance);

            flow.CloseShop();
            Assert.AreEqual(GamePhase.RoundPayment, flow.Phase, "걸 돈도 칩도 없으면 기존 규칙대로 납부 판정.");
            Assert.AreEqual(5, flow.Round.ForfeitedSpins);
        }
    }
}
