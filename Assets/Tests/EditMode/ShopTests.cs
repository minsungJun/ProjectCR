using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ProjectCR.Tests
{
    /// <summary>상점 데이터(41종), 진열, 진입 상태, 구매(가용 금액·1회 차감·재고·대상), 집중·교차 강화.</summary>
    public class ShopTests
    {
        private const int Skull = RouletteDefinition.SkullNumber;
        private static readonly BetTarget Red = BetTarget.Outside(BetKind.Red);
        private static readonly BetTarget Black = BetTarget.Outside(BetKind.Black);

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

        /// <summary>지정한 상품만 있는 목록(진열에 반드시 나온다).</summary>
        private ShopCatalog Only(params string[] ids) =>
            ShopCatalog.Create(all.Where(i => ids.Contains(i.Id)), paymentSupportChance: 1f, openOnFirstRound: true);

        private GameFlowController Flow(ShopCatalog catalog, int seed = 3) => new GameFlowController(definition, balance, seed, null, catalog);

        private static int SlotOf(GameFlowController flow, string id) =>
            flow.Shop.Offers.ToList().FindIndex(o => o.Item.Id == id);

        private static void Spin(GameFlowController flow, BetTarget target, long stake, params int[] forced)
        {
            flow.ForcedNumbers = forced;
            Assert.AreEqual(BetError.None, flow.PlaceBet(target, stake));
            Assert.AreEqual(BetError.None, flow.TryBeginSpin(out ActiveSpin spin));
            Assert.IsTrue(flow.CompleteSpin(spin.Id, out _));
        }

        // ---------- 데이터 ----------

        [Test]
        public void Catalog_Has41Items_InFourCategories()
        {
            Assert.AreEqual(41, all.Count);
            Assert.AreEqual(6, all.Count(i => i.Category == ShopCategory.Focus));
            Assert.AreEqual(30, all.Count(i => i.Category == ShopCategory.Cross));
            Assert.AreEqual(1, all.Count(i => i.Category == ShopCategory.PaymentSupport));
            Assert.AreEqual(4, all.Count(i => i.Category == ShopCategory.Consumable));
            Assert.AreEqual(41, all.Select(i => i.Id).Distinct().Count());

            var pairs = all.Where(i => i.Category == ShopCategory.Cross).Select(i => (i.TriggerGroup, i.Group)).ToList();
            Assert.AreEqual(30, pairs.Distinct().Count());
            Assert.IsTrue(pairs.All(p => p.TriggerGroup != p.Group));
            Assert.IsTrue(all.Any(i => i.DisplayName == "COLUMN → DOZEN"));
        }

        [Test]
        public void Price_FollowsDue_AndGrowsForPermanents()
        {
            ShopItemDefinition focus = all.First(i => i.Effect == ShopEffect.Focus);
            ShopItemDefinition ball = all.First(i => i.Effect == ShopEffect.ExtraBall);

            Assert.AreEqual(40, focus.PriceFor(120, 0), "120 × 30% = 36 → 5 단위 올림 40");
            Assert.AreEqual(55, focus.PriceFor(120, 1), "× 1.5 = 54 → 5 단위 올림");
            Assert.AreEqual(90, focus.PriceFor(300, 0));
            Assert.AreEqual(45, ball.PriceFor(120, 0), "120 × 35% = 42 → 45");
            Assert.AreEqual(45, ball.PriceFor(120, 3), "소모품은 산 수로 오르지 않는다.");
        }

        // ---------- 진입 ----------

        [Test]
        public void Shop_OpensOnlyWhileBetting_AndKeepsBets()
        {
            GameFlowController flow = Flow(assets.Shop());
            flow.PlaceBet(Red, 10);

            Assert.IsTrue(flow.EnterShop());
            Assert.AreEqual(GamePhase.Shopping, flow.Phase);
            Assert.AreEqual(BetError.NotInBettingPhase, flow.PlaceChip(Red));
            Assert.AreEqual(BetError.NotInBettingPhase, flow.TryBeginSpin(out _));
            Assert.IsFalse(flow.EnterShop(), "이미 상점 안.");

            Assert.IsTrue(flow.CloseShop());
            Assert.AreEqual(GamePhase.Betting, flow.Phase);
            Assert.AreEqual(10, flow.Bet.StakeOn(Red), "놓아 둔 베팅은 그대로.");
            Assert.AreEqual(5, flow.Round.SpinsRemaining, "상점은 회전을 쓰지 않는다.");
            Assert.AreEqual(1, flow.Round.Round);

            flow.TryBeginSpin(out ActiveSpin spin);
            Assert.IsFalse(flow.EnterShop(), "회전 중에는 못 연다.");
            flow.CompleteSpin(spin.Id, out _);
        }

        [Test]
        public void Shop_OpensOnlyAfterRoundEnd_BeforeFirstSpin()
        {
            // 게임 규칙: 1라운드는 닫혀 있고, 라운드를 통과한 뒤 새 라운드 첫 회전 전에만 열린다.
            var flow = new GameFlowController(definition, balance, 3, null, ShopCatalog.Create(all));
            flow.Wallet.Add(1000);
            Assert.IsFalse(flow.CanEnterShop, "1라운드 시작: 끝난 라운드가 없다.");
            Assert.IsFalse(flow.EnterShop());

            while (flow.Phase == GamePhase.Betting)
            {
                Spin(flow, BetTarget.Straight(7), 1, 8);
            }

            flow.SubmitPayment();
            Assert.AreEqual(2, flow.Round.Round);
            Assert.IsTrue(flow.CanEnterShop, "라운드 통과 직후, 첫 회전 전.");

            flow.PlaceBet(Red, 10);
            Assert.IsTrue(flow.CanEnterShop, "칩을 놓아도 첫 회전 전이면 열린다.");
            Assert.IsTrue(flow.EnterShop());
            Assert.IsTrue(flow.CloseShop());
            Assert.IsTrue(flow.EnterShop(), "첫 회전 전에는 다시 들어갈 수 있다.");
            flow.CloseShop();

            flow.TryBeginSpin(out ActiveSpin spin);
            flow.CompleteSpin(spin.Id, out _);
            Assert.AreEqual(GamePhase.Betting, flow.Phase);
            Assert.IsFalse(flow.CanEnterShop, "첫 회전을 시작하면 그 라운드에는 닫힌다.");
            Assert.IsFalse(flow.EnterShop());
        }

        [Test]
        public void Shop_FirstRoundOption_OpensBeforeFirstSpinOnly()
        {
            var flow = new GameFlowController(definition, balance, 3, null, ShopCatalog.Create(all, openOnFirstRound: true));
            Assert.IsTrue(flow.CanEnterShop);
            Spin(flow, BetTarget.Straight(7), 1, 8);
            Assert.IsFalse(flow.CanEnterShop);
        }

        [Test]
        public void Shop_IsClosed_DuringPaymentAndGameOver()
        {
            GameFlowController flow = Flow(assets.Shop());
            Spin(flow, BetTarget.Straight(7), 100, 8);

            Assert.AreEqual(GamePhase.RoundPayment, flow.Phase);
            Assert.IsFalse(flow.EnterShop());
            flow.SubmitPayment();
            Assert.AreEqual(GamePhase.GameOver, flow.Phase);
            Assert.IsFalse(flow.EnterShop());
        }

        [Test]
        public void Display_IsFixedPerRound_AndSameSeedSameDisplay()
        {
            GameFlowController flow = Flow(assets.Shop(), seed: 21);
            flow.EnterShop();
            var first = flow.Shop.Offers.Select(o => o.Item).ToList();
            flow.CloseShop();
            flow.EnterShop();
            CollectionAssert.AreEqual(first, flow.Shop.Offers.Select(o => o.Item).ToList(), "다시 들어와도 같은 진열.");

            GameFlowController twin = Flow(assets.Shop(), seed: 21);
            twin.EnterShop();
            CollectionAssert.AreEqual(first.Select(i => i.Id), twin.Shop.Offers.Select(o => o.Item.Id));

            // 강화 탭: 집중 2, 교차 2, 납부 지원(확률 0~1). 소모품 탭: 소모품 4종 전부(목록 순서).
            int support = first.Count(i => i.Category == ShopCategory.PaymentSupport);
            Assert.LessOrEqual(support, 1);
            Assert.AreEqual(8 + support, first.Count);
            Assert.AreEqual(2, first.Count(i => i.Category == ShopCategory.Focus));
            Assert.AreEqual(2, first.Count(i => i.Category == ShopCategory.Cross));
            CollectionAssert.AreEqual(
                all.Where(i => i.Category == ShopCategory.Consumable).Select(i => i.Id),
                first.Where(i => i.Category == ShopCategory.Consumable).Select(i => i.Id),
                "소모품은 전부, 목록 순서대로.");
            Assert.AreEqual(first.Count, first.Distinct().Count());
        }

        [Test]
        public void CardText_DefaultsMatchShopCards()
        {
            ShopItemDefinition Item(string id) => all.Single(i => i.Id == id);
            Assert.AreEqual("이번 라운드\n공 1개 추가", Item("extra_ball").CardText);
            Assert.AreEqual("해골이 나오면\n해당 공 재회전", Item("skull_shield").CardText);
            Assert.AreEqual("남은 회전\n+1회", Item("spare_spin").CardText);
            Assert.AreEqual("일반 낙첨 시\n베팅금 50% 환급", Item("insurance").CardText);
            Assert.AreEqual("회전권", Item("spare_spin").DisplayName);
        }

        [Test]
        public void Display_RefreshesNextRound()
        {
            GameFlowController flow = Flow(Only("extra_ball", "skull_shield", "insurance", "spare_spin"));
            flow.EnterShop();
            flow.Shop.Buy(SlotOf(flow, flow.Shop.Offers[0].Item.Id), BetTarget.None);
            flow.CloseShop();

            // 1라운드를 통과(음료 목록 없음 → 바로 다음 라운드).
            Spin(flow, BetTarget.Straight(7), 10, 7);
            for (int i = 0; i < 4; i++)
            {
                Spin(flow, BetTarget.Straight(7), 1, 8);
            }

            flow.SubmitPayment();
            Assert.AreEqual(2, flow.Round.Round);
            flow.EnterShop();
            Assert.AreEqual(2, flow.Shop.State.DisplayRound);
            Assert.IsTrue(flow.Shop.Offers.All(o => o.BoughtThisRound == 0 && o.Stock == o.Item.StockPerRound), "다음 라운드에 재고가 다시 찬다.");
        }

        // ---------- 구매 ----------

        [Test]
        public void Buy_UsesAvailableFunds_BalanceMinusPlacedBets()
        {
            GameFlowController flow = Flow(Only("extra_ball"));
            flow.PlaceBet(Red, 80); // 보유 100, 가용 20, 추가 공 45
            flow.EnterShop();

            Assert.AreEqual(20, flow.Shop.AvailableFunds);
            Assert.AreEqual(ShopError.InsufficientFunds, flow.Shop.Buy(0, BetTarget.None));
            Assert.AreEqual(100, flow.Wallet.Balance, "실패하면 돈이 그대로.");
            Assert.AreEqual(2, flow.Shop.Offers[0].Stock, "재고도 그대로.");
            Assert.AreEqual(0, flow.Inventory.CountOf(ShopEffect.ExtraBall));
        }

        [Test]
        public void Buy_ChargesOnce_AndRespectsStockAndLimit()
        {
            GameFlowController flow = Flow(Only("insurance"));
            flow.EnterShop();

            Assert.AreEqual(ShopError.None, flow.Shop.Buy(0, BetTarget.None));
            Assert.AreEqual(85, flow.Wallet.Balance, "120 × 10% = 12 → 15");
            Assert.AreEqual(1, flow.Shop.Offers[0].Stock);
            Assert.AreEqual(1, flow.Inventory.CountOf(ShopEffect.Insurance));

            Assert.AreEqual(ShopError.None, flow.Shop.Buy(0, BetTarget.None));
            Assert.AreNotEqual(ShopError.None, flow.Shop.Buy(0, BetTarget.None), "재고·제한 2개.");
            Assert.AreEqual(70, flow.Wallet.Balance);
            Assert.AreEqual(2, flow.Inventory.CountOf(ShopEffect.Insurance));

            flow.CloseShop();
            Assert.AreEqual(ShopError.NotInShop, flow.Shop.Buy(0, BetTarget.None), "상점 밖에서는 못 산다.");
        }

        [Test]
        public void Focus_NeedsValidTarget_BeforeCharging()
        {
            GameFlowController flow = Flow(Only("focus_number"));
            flow.EnterShop();

            Assert.AreEqual(ShopError.NeedTarget, flow.Shop.Buy(0, BetTarget.None));
            Assert.AreEqual(ShopError.InvalidTarget, flow.Shop.Buy(0, Red), "숫자 집중에 빨강은 안 된다.");
            Assert.AreEqual(ShopError.InvalidTarget, flow.Shop.Buy(0, BetTarget.Inside(BetKind.Split, 0, 1)), "0이 든 조합은 안 된다.");
            Assert.AreEqual(100, flow.Wallet.Balance, "대상이 정해지기 전에는 돈이 빠지지 않는다.");

            Assert.AreEqual(ShopError.None, flow.Shop.Buy(0, BetTarget.Straight(0)), "0 단독은 된다.");
            Assert.AreEqual(1, flow.Upgrades.Probabilities.StacksOf(BetTarget.Straight(0)));
        }

        [Test]
        public void Focus_BoostsGroupPayout_AndTargetWeight_Only()
        {
            GameFlowController flow = Flow(Only("focus_color"));
            flow.EnterShop();
            Assert.AreEqual(ShopError.None, flow.Shop.Buy(0, Red));
            SpinConfig config = flow.BuildSpinConfig();

            Assert.AreEqual(2.2m, config.MultiplierFor(Red), "색상 지급 +10%");
            Assert.AreEqual(2.2m, config.MultiplierFor(Black), "지급 강화는 묶음 전체");
            Assert.AreEqual(3m, config.MultiplierFor(BetTarget.Outside(BetKind.Dozen1)), "다른 묶음은 그대로");
            Assert.AreEqual(1.1d, config.Weights[definition.IndexOfNumber(32)], 1e-12, "빨강 칸 가중치 +10%");
            Assert.AreEqual(1d, config.Weights[definition.IndexOfNumber(2)], 1e-12, "검정 칸은 그대로");
            Assert.AreEqual(1d / 38d, config.PocketProbabilities()[definition.IndexOfSkull()], 1e-12);
        }

        [Test]
        public void Focus_AndDrinks_Combine_AsPlanned()
        {
            DrinkCatalog drinks = assets.Drinks();
            var flow = new GameFlowController(definition, balance, 3, drinks, Only("focus_color"));
            flow.Drinks.AddStack(DrinkGroup.Color);
            flow.Probabilities.SetStacks(Red, 0.37m, 1);
            flow.EnterShop();
            flow.Shop.Buy(0, Red);

            SpinConfig config = flow.BuildSpinConfig();
            Assert.AreEqual(2m * 1.5m * 1.1m, config.MultiplierFor(Red), "기본 × 음료 × (1 + 집중)");
            Assert.AreEqual(1.47d, config.Weights[definition.IndexOfNumber(32)], 1e-9, "음료 +37% + 집중 +10%");
        }

        // ---------- 교차 강화 ----------

        private GameFlowController WithCross(string id)
        {
            GameFlowController flow = Flow(Only(id));
            flow.Wallet.Add(1000);
            flow.EnterShop();
            Assert.AreEqual(ShopError.None, flow.Shop.Buy(0, BetTarget.None));
            flow.CloseShop();
            return flow;
        }

        [Test]
        public void Cross_TriggersOnWinningBet_AppliesNextSpinOnly()
        {
            GameFlowController flow = WithCross("cross_color_column");
            BetTarget column1 = BetTarget.Outside(BetKind.Column1);

            // 34: 빨강, 1열. 같은 회전의 1열 베팅에는 아직 적용되지 않는다.
            flow.ForcedNumbers = new[] { 34 };
            flow.PlaceBet(Red, 10);
            flow.PlaceBet(column1, 10);
            flow.TryBeginSpin(out ActiveSpin first);
            flow.CompleteSpin(first.Id, out SettlementResult s1);
            Assert.AreEqual(30, s1.Bets[1].Amount, "발동한 회전에는 적용하지 않는다.");
            Assert.AreEqual(1, s1.CrossTriggers.Count);
            Assert.AreEqual(0.25m, flow.Cross.Pending(DrinkGroup.Column));

            // 다음 회전: 1열 3 × 1.25 = 3.75 → 10 걸면 37.5.
            flow.ForcedNumbers = new[] { 34 };
            flow.PlaceBet(column1, 10);
            flow.TryBeginSpin(out ActiveSpin second);
            Assert.AreEqual(0m, flow.Cross.Pending(DrinkGroup.Column), "회전 시작 때 넘어가 비워진다.");
            flow.CompleteSpin(second.Id, out SettlementResult s2);
            Assert.AreEqual(37, s2.TotalPayout);

            // 그다음 회전에는 없다(대상에 다시 걸어도).
            Spin(flow, column1, 10, 34);
            Assert.AreEqual(3m, flow.BuildSpinConfig().MultiplierFor(column1));
        }

        [Test]
        public void Cross_NeedsActualBet_NotJustResultNumber()
        {
            GameFlowController flow = WithCross("cross_color_column");
            Spin(flow, BetTarget.Straight(32), 10, 32); // 32는 빨강이지만 색상 베팅이 아니다.
            Assert.AreEqual(0m, flow.Cross.Pending(DrinkGroup.Column));
        }

        [Test]
        public void Cross_OncePerSpin_EvenWithManyBallsOrBets()
        {
            GameFlowController flow = WithCross("cross_color_column");
            flow.ExtraBallCount = 1;
            flow.ForcedNumbers = new[] { 32, 34 };
            flow.PlaceBet(Red, 10);
            flow.PlaceBet(Black, 10);
            flow.TryBeginSpin(out ActiveSpin spin);
            flow.CompleteSpin(spin.Id, out _);

            Assert.AreEqual(0.25m, flow.Cross.Pending(DrinkGroup.Column), "공 2개·당첨 베팅 여러 개여도 상품당 1번.");
        }

        [Test]
        public void Cross_DoesNotTrigger_OnSkullBust()
        {
            GameFlowController flow = WithCross("cross_color_column");
            flow.ExtraBallCount = 1;
            Spin(flow, Red, 10, 32, Skull);
            Assert.AreEqual(0m, flow.Cross.Pending(DrinkGroup.Column));
        }

        [Test]
        public void Cross_Survives_ShopAndRoundEnd()
        {
            GameFlowController flow = WithCross("cross_color_column");
            for (int i = 0; i < 4; i++)
            {
                Spin(flow, BetTarget.Straight(7), 1, 8);
            }

            Spin(flow, Red, 10, 32); // 마지막 회전에서 발동
            Assert.AreEqual(GamePhase.RoundPayment, flow.Phase);
            flow.SubmitPayment();
            Assert.AreEqual(2, flow.Round.Round);

            flow.EnterShop();
            flow.CloseShop();
            Assert.AreEqual(0.25m, flow.Cross.Pending(DrinkGroup.Column), "다음 라운드 첫 회전까지 남는다.");
        }

        [Test]
        public void BoughtPermanent_IsNotDisplayedAgain()
        {
            GameFlowController flow = WithCross("cross_color_column");
            Assert.IsTrue(flow.Shop.State.IsRetired(flow.Shop.Offers[0].Item));

            Spin(flow, BetTarget.Straight(7), 10, 7);
            for (int i = 0; i < 4; i++)
            {
                Spin(flow, BetTarget.Straight(7), 1, 8);
            }

            flow.SubmitPayment();
            flow.EnterShop();
            Assert.AreEqual(2, flow.Shop.State.DisplayRound);
            Assert.AreEqual(0, flow.Shop.Offers.Count, "지속 효과 상품은 한 번 사면 다음 진열에 없다.");
            Assert.AreEqual(1, flow.Cross.CountOf(DrinkGroup.Color, DrinkGroup.Column), "효과는 그대로 유지.");
        }

        [Test]
        public void FullCatalog_SkipsBoughtPermanents_ButNotConsumables()
        {
            var flow = Flow(assets.Shop(supportChance: 1f), seed: 8);
            flow.Wallet.Add(100_000);
            flow.EnterShop();
            var bought = new List<ShopItemDefinition>();
            for (int i = 0; i < flow.Shop.Offers.Count; i++)
            {
                ShopItemDefinition item = flow.Shop.Offers[i].Item;
                BetTarget target = item.NeedsTarget
                    ? (item.Group == DrinkGroup.Number ? BetTarget.Straight(7) : BetTarget.Outside(FirstKindOf(item.Group)))
                    : BetTarget.None;
                Assert.AreEqual(ShopError.None, flow.Shop.Buy(i, target), item.Id);
                bought.Add(item);
            }

            flow.CloseShop();
            for (int round = 0; round < 3; round++)
            {
                while (flow.Phase == GamePhase.Betting)
                {
                    Spin(flow, BetTarget.Straight(7), 1, 8);
                }

                flow.SubmitPayment();
                flow.EnterShop();
                foreach (ShopOffer offer in flow.Shop.Offers)
                {
                    Assert.IsFalse(offer.Item.IsPermanent && bought.Contains(offer.Item), $"{offer.Item.Id}가 다시 나왔다.");
                }

                Assert.IsTrue(flow.Shop.Offers.Any(o => o.Item.Category == ShopCategory.Consumable), "소모품은 계속 나온다.");
                flow.CloseShop();
            }
        }

        private static BetKind FirstKindOf(DrinkGroup group)
        {
            switch (group)
            {
                case DrinkGroup.Column:
                    return BetKind.Column1;
                case DrinkGroup.Dozen:
                    return BetKind.Dozen1;
                case DrinkGroup.Parity:
                    return BetKind.Odd;
                case DrinkGroup.Color:
                    return BetKind.Red;
                default:
                    return BetKind.Low;
            }
        }

        // ---------- 재시작 ----------

        [Test]
        public void NewRun_ClearsShopState()
        {
            GameFlowController flow = Flow(Only("focus_color"));
            flow.EnterShop();
            flow.Shop.Buy(0, Red);

            GameFlowController restarted = Flow(Only("focus_color"), seed: 0);
            Assert.AreEqual(0, restarted.Upgrades.TotalStacks);
            Assert.AreEqual(0, restarted.Cross.TotalOwned);
            Assert.AreEqual(0, restarted.Inventory.CountOf(ShopEffect.ExtraBall));
            Assert.AreEqual(0, restarted.Shop.State.DisplayRound);
            Assert.AreEqual(2m, restarted.BuildSpinConfig().MultiplierFor(Red));
        }
    }
}
