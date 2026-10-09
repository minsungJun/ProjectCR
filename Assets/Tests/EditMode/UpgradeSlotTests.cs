using System.Linq;
using NUnit.Framework;

namespace ProjectCR.Tests
{
    /// <summary>
    /// 보유 강화 칸(최대 6개, 테이블 배지와 1:1): 칸 채우기, 다 찼을 때 하나를 버리고 사기(효과 제거·같은 칸 교체·다시 진열 가능),
    /// 해골 방어를 다 쓰면 칸에서 빠짐(정산 때).
    /// </summary>
    public class UpgradeSlotTests
    {
        private const int Skull = RouletteDefinition.SkullNumber;
        private static readonly BetTarget Red = BetTarget.Outside(BetKind.Red);
        private static readonly BetTarget Dozen1 = BetTarget.Outside(BetKind.Dozen1);

        // 칸 6개를 채울 강화(순서대로 칸 0~5)와 7번째 강화.
        private static readonly string[] FirstSix =
            { "focus_color", "cross_dozen_column", "payment_support", "extra_ball", "skull_shield", "cross_color_number" };

        private const string Seventh = "focus_dozen";

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

        /// <summary>강화 7종이 모두 진열된 상점 안, 돈은 넉넉히.</summary>
        private GameFlowController InShop()
        {
            string[] ids = FirstSix.Append(Seventh).ToArray();
            ShopCatalog catalog = ShopCatalog.Create(full.Items.Where(i => ids.Contains(i.Id)), openOnFirstRound: true, upgradeSlots: ids.Length);
            var flow = new GameFlowController(definition, balance, 5, null, catalog);
            flow.Wallet.Add(100_000);
            Assert.IsTrue(flow.EnterShop());
            Assert.AreEqual(ids.Length, flow.Shop.Offers.Count);
            return flow;
        }

        private static int SlotOf(GameFlowController flow, string id) => flow.Shop.Offers.ToList().FindIndex(o => o.Item.Id == id);

        private static BetTarget TargetFor(string id) => id == "focus_color" ? Red : id == "focus_dozen" ? Dozen1 : BetTarget.None;

        private static GameFlowController BuySix(GameFlowController flow)
        {
            foreach (string id in FirstSix)
            {
                Assert.AreEqual(ShopError.None, flow.Shop.Buy(SlotOf(flow, id), TargetFor(id)), id);
            }

            return flow;
        }

        private static int OwnedIndex(GameFlowController flow, string id) =>
            flow.OwnedUpgrades.Slots.ToList().FindIndex(s => !s.IsEmpty && s.Item.Id == id);

        [Test]
        public void Slots_FillInOrder_UpToSix()
        {
            GameFlowController flow = BuySix(InShop());

            Assert.AreEqual(6, flow.OwnedUpgrades.Capacity, "기본 칸 수 = 테이블 원 6개");
            Assert.AreEqual(6, flow.OwnedUpgrades.Count);
            Assert.IsTrue(flow.OwnedUpgrades.IsFull);
            for (int i = 0; i < FirstSix.Length; i++)
            {
                Assert.AreEqual(FirstSix[i], flow.OwnedUpgrades.Slots[i].Item.Id, "산 순서대로 칸 0부터");
            }

            Assert.AreEqual(Red, flow.OwnedUpgrades.Slots[0].Target, "집중 강화는 고른 대상도 기억한다");
            Assert.IsTrue(flow.OwnedUpgrades.Slots[1].Target.IsNone);
        }

        [Test]
        public void Consumables_DoNotTakeSlots()
        {
            ShopCatalog catalog = ShopCatalog.Create(full.Items.Where(i => i.Id == "insurance"), openOnFirstRound: true);
            var flow = new GameFlowController(definition, balance, 5, null, catalog);
            Assert.IsTrue(flow.EnterShop());
            Assert.AreEqual(ShopError.None, flow.Shop.Buy(0, BetTarget.None));
            Assert.AreEqual(0, flow.OwnedUpgrades.Count);
        }

        [Test]
        public void Full_BuyWithoutDiscard_ChangesNothing()
        {
            GameFlowController flow = BuySix(InShop());
            int slot = SlotOf(flow, Seventh);
            long money = flow.Wallet.Balance;

            Assert.IsTrue(flow.Shop.NeedsDiscard(slot));
            Assert.AreEqual(ShopError.None, flow.Shop.Check(slot, Dozen1), "버튼은 눌린다(누르면 버릴 강화를 고른다)");
            Assert.AreEqual(ShopError.SlotsFull, flow.Shop.Buy(slot, Dozen1));
            Assert.AreEqual(ShopError.SlotsFull, flow.Shop.Buy(slot, Dozen1, 6), "없는 칸");

            Assert.AreEqual(money, flow.Wallet.Balance, "돈 그대로");
            Assert.AreEqual(1, flow.Shop.Offers[slot].Stock, "재고 그대로");
            Assert.AreEqual(0, flow.Upgrades.StacksOf(DrinkGroup.Dozen), "효과 없음");
            Assert.AreEqual(6, flow.OwnedUpgrades.Count);
        }

        [Test]
        public void Discard_RemovesFocus_AndNewTakesSameSlot()
        {
            GameFlowController flow = BuySix(InShop());
            Assert.AreEqual(0.1m, flow.Upgrades.PayoutIncrease(DrinkGroup.Color));
            Assert.AreEqual(1, flow.Upgrades.Probabilities.StacksOf(Red));
            ShopItemDefinition discarded = flow.OwnedUpgrades.Slots[0].Item;
            long money = flow.Wallet.Balance;
            int slot = SlotOf(flow, Seventh);
            long price = flow.Shop.PriceOf(flow.Shop.Offers[slot]);

            Assert.AreEqual(ShopError.None, flow.Shop.Buy(slot, Dozen1, 0));

            Assert.AreEqual(money - price, flow.Wallet.Balance, "가격 한 번만");
            Assert.AreEqual(0m, flow.Upgrades.PayoutIncrease(DrinkGroup.Color), "버린 집중 강화의 지급 증가가 빠진다");
            Assert.AreEqual(0, flow.Upgrades.StacksOf(DrinkGroup.Color));
            Assert.AreEqual(0, flow.Upgrades.Probabilities.StacksOf(Red), "대상 확률 효과도 빠진다");
            Assert.AreEqual(0.1m, flow.Upgrades.PayoutIncrease(DrinkGroup.Dozen), "새 강화는 적용");
            Assert.AreEqual(1, flow.Upgrades.Probabilities.StacksOf(Dozen1));

            Assert.AreEqual(Seventh, flow.OwnedUpgrades.Slots[0].Item.Id, "버린 칸에 들어간다(다른 배지는 그대로)");
            Assert.AreEqual(Dozen1, flow.OwnedUpgrades.Slots[0].Target);
            Assert.AreEqual(FirstSix[1], flow.OwnedUpgrades.Slots[1].Item.Id);
            Assert.AreEqual(6, flow.OwnedUpgrades.Count);

            Assert.IsFalse(flow.Shop.State.IsRetired(discarded), "버린 강화는 다시 진열될 수 있다");
            Assert.IsTrue(flow.Shop.State.IsRetired(flow.OwnedUpgrades.Slots[0].Item));
            Assert.AreEqual(1, flow.Shop.State.BoughtTotal(discarded), "가격 성장용 산 수는 그대로");
        }

        [TestCase("cross_dozen_column")]
        [TestCase("payment_support")]
        [TestCase("extra_ball")]
        [TestCase("skull_shield")]
        public void Discard_RemovesEachEffect(string id)
        {
            GameFlowController flow = BuySix(InShop());
            int index = OwnedIndex(flow, id);

            Assert.AreEqual(ShopError.None, flow.Shop.Buy(SlotOf(flow, Seventh), Dozen1, index));

            Assert.AreEqual(-1, OwnedIndex(flow, id));
            Assert.AreEqual(id == "cross_dozen_column" ? 0 : 1, flow.Cross.CountOf(DrinkGroup.Dozen, DrinkGroup.Column));
            Assert.AreEqual(id == "payment_support" ? 0m : 0.25m, flow.Upgrades.PaymentRefund);
            Assert.AreEqual(id == "extra_ball" ? 0 : 1, flow.Upgrades.ExtraBalls);
            Assert.AreEqual(id == "skull_shield" ? 0 : 1, flow.Inventory.Shields);
            Assert.AreEqual(1, flow.Cross.CountOf(DrinkGroup.Color, DrinkGroup.Number), "다른 강화는 그대로");
        }

        [Test]
        public void SkullShield_UsedUp_LeavesSlotAtSettlement()
        {
            ShopCatalog catalog = ShopCatalog.Create(full.Items.Where(i => i.Id == "skull_shield"), openOnFirstRound: true);
            var flow = new GameFlowController(definition, balance, 5, null, catalog);
            flow.Wallet.Add(1_000);
            Assert.IsTrue(flow.EnterShop());
            Assert.AreEqual(ShopError.None, flow.Shop.Buy(0, BetTarget.None));
            Assert.AreEqual(0, flow.OwnedUpgrades.IndexOf(ShopEffect.SkullShield));
            flow.CloseShop();

            flow.ForcedNumbers = new[] { Skull };
            Assert.AreEqual(BetError.None, flow.PlaceBet(Red, 10));
            Assert.AreEqual(BetError.None, flow.TryBeginSpin(out ActiveSpin spin));
            Assert.AreEqual(1, spin.ShieldsUsed);
            Assert.AreEqual(0, flow.OwnedUpgrades.IndexOf(ShopEffect.SkullShield), "회전 중에는 배지가 남는다(결과를 미리 보여 주지 않게)");

            Assert.IsTrue(flow.CompleteSpin(spin.Id, out _));
            Assert.AreEqual(-1, flow.OwnedUpgrades.IndexOf(ShopEffect.SkullShield), "정산 때 칸에서 빠진다");
            Assert.AreEqual(0, flow.OwnedUpgrades.Count);
            Assert.IsTrue(flow.Shop.State.IsRetired(catalog.Items[0]), "다 쓴 해골 방어는 다시 진열되지 않는다(게임 전체 1회)");
        }

        [Test]
        public void SkullShield_NotHit_StaysInSlot()
        {
            ShopCatalog catalog = ShopCatalog.Create(full.Items.Where(i => i.Id == "skull_shield"), openOnFirstRound: true);
            var flow = new GameFlowController(definition, balance, 5, null, catalog);
            flow.Wallet.Add(1_000);
            Assert.IsTrue(flow.EnterShop());
            Assert.AreEqual(ShopError.None, flow.Shop.Buy(0, BetTarget.None));
            flow.CloseShop();

            flow.ForcedNumbers = new[] { 7 };
            Assert.AreEqual(BetError.None, flow.PlaceBet(Red, 10));
            Assert.AreEqual(BetError.None, flow.TryBeginSpin(out ActiveSpin spin));
            Assert.IsTrue(flow.CompleteSpin(spin.Id, out _));
            Assert.AreEqual(0, flow.OwnedUpgrades.IndexOf(ShopEffect.SkullShield));
        }
    }
}
