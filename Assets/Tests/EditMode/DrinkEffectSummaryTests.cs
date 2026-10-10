using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ProjectCR.Tests
{
    /// <summary>
    /// 태블릿 "마신 음료" 목록: 마신 효과만, 같은 대상은 한 줄, 확률 → 지급 순서,
    /// 확률은 가중치와 실제 등장 확률을 따로, 지급은 음료 배율만(상점 강화는 더하지 않음). 목록을 만들어도 상태는 그대로.
    /// </summary>
    public class DrinkEffectSummaryTests
    {
        private static readonly BetTarget Red = BetTarget.Outside(BetKind.Red);
        private static readonly BetTarget Dozen2 = BetTarget.Outside(BetKind.Dozen2);

        private TestAssets assets;
        private GameFlowController flow;

        [SetUp]
        public void SetUp()
        {
            assets = new TestAssets();
            flow = new GameFlowController(assets.Definition(), assets.Balance(), 5, assets.Drinks(withProbability: true));
        }

        [TearDown]
        public void TearDown() => assets.DestroyAll();

        [Test]
        public void NoDrinks_EmptyList_AndButtonOff()
        {
            Assert.AreEqual(0, DrinkEffectSummary.For(flow).Count);
            Assert.IsFalse(DrinkEffectSummary.HasAny(flow));
            Assert.IsFalse(TabletPanel.CanPress(flow, TabletAction.Drinks), "마신 음료가 없으면 [음료] 버튼은 꺼진다");
        }

        [Test]
        public void SameTarget_IsOneLine_ChanceFirst_ThenPayout()
        {
            flow.Drinks.AddStack(Dozen2);
            flow.Drinks.AddStack(Dozen2);
            flow.Probabilities.AddStack(Red, 0.2m);
            flow.Probabilities.AddStack(Red, 0.2m);
            flow.Drinks.AddGroupStack(DrinkGroup.Number);

            List<DrinkEffectLine> lines = DrinkEffectSummary.For(flow);
            Assert.AreEqual(3, lines.Count, "같은 대상은 한 줄");

            Assert.AreEqual(DrinkEffectKind.Probability, lines[0].Kind, "확률 음료가 먼저");
            Assert.AreEqual("빨강 등장", lines[0].Title);
            StringAssert.StartsWith("가중치 +40% · 현재 확률 ", lines[0].Effect);
            Assert.AreEqual(2, lines[0].Stacks);

            Assert.AreEqual("13–24 지급", lines[1].Title);
            Assert.AreEqual("음료 배율 ×2.25", lines[1].Effect);
            Assert.AreEqual(2, lines[1].Stacks);

            Assert.AreEqual("숫자 전체 지급", lines[2].Title);
            Assert.AreEqual("음료 배율 ×1.5", lines[2].Effect);
            Assert.IsTrue(TabletPanel.CanPress(flow, TabletAction.Drinks));
        }

        [Test]
        public void Chance_ShowsRealProbability_FromSpinConfig()
        {
            flow.Probabilities.AddStack(Red, 0.37m);
            string expected = NumberText.Percent(flow.BuildSpinConfig().ProbabilityOf(Red));
            StringAssert.EndsWith($"현재 확률 {expected}", DrinkEffectSummary.For(flow).Single().Effect);
        }

        [Test]
        public void Payout_ShowsDrinkMultiplierOnly_NotShopBonus()
        {
            flow.Drinks.AddStack(Red);
            flow.Upgrades.AddFocus(DrinkGroup.Color, Red, 0.1m);

            DrinkEffectLine payout = DrinkEffectSummary.For(flow).Single(l => l.Kind == DrinkEffectKind.Payout);
            Assert.AreEqual("음료 배율 ×1.5", payout.Effect, "상점 집중 +10%는 더하지 않는다");
            Assert.IsFalse(DrinkEffectSummary.For(flow).Any(l => l.Kind == DrinkEffectKind.Probability), "상점 집중의 확률 효과도 넣지 않는다");
        }

        [Test]
        public void Building_DoesNotChangeState()
        {
            flow.Drinks.AddStack(Red);
            flow.Probabilities.AddStack(Red, 0.37m);
            long money = flow.Wallet.Balance;

            DrinkEffectSummary.For(flow);
            Assert.AreEqual(1, flow.Drinks.TotalStacks);
            Assert.AreEqual(1, flow.Probabilities.TotalStacks);
            Assert.AreEqual(money, flow.Wallet.Balance);
        }
    }
}

