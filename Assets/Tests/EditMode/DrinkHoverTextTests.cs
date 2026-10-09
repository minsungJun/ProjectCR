using System.Linq;
using NUnit.Framework;

namespace ProjectCR.Tests
{
    /// <summary>로봇 선반 음료 툴팁 문구: 지급 음료는 배율 변화, 확률 음료는 가중치·등장 확률 변화. 미리보기는 상태를 바꾸지 않는다.</summary>
    public class DrinkHoverTextTests
    {
        private TestAssets assets;
        private DrinkCatalog catalog;
        private GameFlowController flow;

        [SetUp]
        public void SetUp()
        {
            assets = new TestAssets();
            catalog = assets.Drinks(withProbability: true);
            flow = new GameFlowController(assets.Definition(), assets.Balance(), 5, catalog);
        }

        [TearDown]
        public void TearDown() => assets.DestroyAll();

        [Test]
        public void PayoutDrink_ShowsMultiplierChange()
        {
            DrinkDefinition drink = catalog.Drinks.First(d => d.Group == DrinkGroup.Color);
            flow.Drinks.SetStacks(DrinkGroup.Color, 1);

            DrinkHoverText text = DrinkHoverText.For(DrinkOffer.ForPayout(drink), flow);
            Assert.AreEqual(drink.DisplayName, text.Title);
            Assert.AreEqual("×1.5 → ×2.25", text.Accent);
            StringAssert.Contains("2잔째", text.Effect);
            StringAssert.Contains("3배 → 4.5배", text.Detail);
            Assert.AreEqual(1, flow.Drinks.StacksOf(DrinkGroup.Color), "미리보기는 잔 수를 바꾸지 않는다");
        }

        [Test]
        public void ProbabilityDrink_ShowsWeightAndChance()
        {
            ProbabilityDrinkDefinition drink = catalog.ProbabilityDrinks.First(d => d.Group == DrinkGroup.Color);
            BetTarget red = BetTarget.Outside(BetKind.Red);

            DrinkHoverText text = DrinkHoverText.For(DrinkOffer.ForProbability(drink, red), flow);
            Assert.AreEqual("빨강 확률", text.Accent);
            StringAssert.StartsWith("추첨 가중치 +0% → +", text.Effect);
            StringAssert.Contains("1잔째", text.Effect);
            StringAssert.StartsWith("등장 확률 47.4% → ", text.Detail);
            Assert.AreEqual(0, flow.Probabilities.StacksOf(red));
        }
    }
}
