using System.Linq;
using NUnit.Framework;

namespace ProjectCR.Tests
{
    /// <summary>
    /// 로봇 선반 음료 툴팁 문구: 종류, 무엇을 하는지(적용 베팅·대상 숫자 수), 예시·수치 변화, 유지·쌓임.
    /// 지급 음료는 배율 변화, 확률 음료는 등장 확률·가중치 변화. 미리보기는 상태를 바꾸지 않는다.
    /// </summary>
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
            BetTarget red = BetTarget.Outside(BetKind.Red);
            flow.Drinks.SetStacks(red, 1);

            DrinkHoverText text = DrinkHoverText.For(DrinkOffer.ForPayout(drink, red), flow);
            Assert.AreEqual(drink.DisplayName, text.Title);
            Assert.AreEqual("지급 강화", text.Kind);
            Assert.AreEqual("빨강 ×1.5 → ×2.25", text.Accent);
            Assert.AreEqual("빨강 베팅의 당첨금 배율에 ×1.5를 곱합니다. 검정은 그대로입니다.", text.Effect);
            Assert.AreEqual("예: 빨강에 10을 걸어 맞히면 30 → 45", text.Detail, "빨강 2배 × 10 × 1.5 → × 2.25 (버림)");
            StringAssert.Contains("빨강 2잔째", text.Duration);
            StringAssert.Contains("이번 게임 내내", text.Duration);
            Assert.AreEqual(1, flow.Drinks.StacksOf(red), "미리보기는 잔 수를 바꾸지 않는다");

            DrinkHoverText even = DrinkHoverText.For(DrinkOffer.ForPayout(catalog.Drinks.First(d => d.Group == DrinkGroup.Parity), BetTarget.Outside(BetKind.Odd)), flow);
            StringAssert.EndsWith("짝수는 그대로입니다.", even.Effect, "받침 없는 말 뒤에는 '는'");

            DrinkHoverText number = DrinkHoverText.For(DrinkOffer.ForPayout(catalog.Drinks.First(d => d.Group == DrinkGroup.Number), BetTarget.Straight(7)), flow);
            Assert.AreEqual("숫자 전체 ×1 → ×1.5", number.Accent, "숫자 지급 음료는 대상 없이 묶음 전체");
            StringAssert.StartsWith("숫자 베팅 전체(", number.Effect);
            StringAssert.Contains("0 포함", number.Effect);
            StringAssert.Contains("10을 걸어 맞히면 360 → 540", number.Detail);
        }

        [Test]
        public void AssetDescription_ReplacesEffectLine_WithPlaceholders()
        {
            DrinkDefinition drink = catalog.Drinks.First(d => d.Group == DrinkGroup.Column);
            typeof(DrinkDefinition).GetField("description", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(drink, "{target}({group}: {bets}) 당첨금이 ×{boost}배가 됩니다.");
            DrinkHoverText text = DrinkHoverText.For(DrinkOffer.ForPayout(drink, BetTarget.Outside(BetKind.Column2)), flow);
            Assert.AreEqual("2열(컬럼: 1열·2열·3열) 당첨금이 ×1.5배가 됩니다.", text.Effect);

            ProbabilityDrinkDefinition probability = catalog.ProbabilityDrinks.First(d => d.Group == DrinkGroup.Color);
            typeof(ProbabilityDrinkDefinition).GetField("description", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(probability, "{group}({bets}) 중 고른 대상 가중치 {boost}");
            DrinkHoverText chance = DrinkHoverText.For(DrinkOffer.ForProbability(probability), flow);
            Assert.AreEqual("색깔(빨강·검정) 중 고른 대상 가중치 +37%", chance.Effect);
        }

        [Test]
        public void NumberChanceDrink_ShowsItsFixedNumber()
        {
            ProbabilityDrinkDefinition drink = catalog.ProbabilityDrinks.First(d => d.Group == DrinkGroup.Number);
            DrinkHoverText text = DrinkHoverText.For(DrinkOffer.ForProbability(drink, BetTarget.Straight(7)), flow);

            Assert.AreEqual("확률 강화", text.Kind);
            StringAssert.StartsWith("7번 2.63% → ", text.Accent);
            StringAssert.StartsWith("7번에 공이 떨어질 확률을 올립니다", text.Effect);
            StringAssert.Contains("7번 1잔째", text.Detail);
        }

        [Test]
        public void ProbabilityDrink_ShowsWeightAndChance()
        {
            ProbabilityDrinkDefinition drink = catalog.ProbabilityDrinks.First(d => d.Group == DrinkGroup.Column);

            DrinkHoverText text = DrinkHoverText.For(DrinkOffer.ForProbability(drink), flow);
            Assert.AreEqual("확률 강화", text.Kind);
            Assert.AreEqual("컬럼 가중치 +26%", text.Accent);
            StringAssert.Contains("1열·2열·3열 중 대상을 골라", text.Effect);
            StringAssert.Contains("당첨금 배율은 그대로", text.Effect);
            StringAssert.StartsWith("예: 1열만 고르면 31.6% → 36.7%", text.Detail);
            StringAssert.Contains("2개 고르면 각각 +13%", text.Detail);
            StringAssert.Contains("나눠 받고", text.Duration);
            Assert.AreEqual(0, flow.Probabilities.TotalStacks, "미리보기는 상태를 바꾸지 않는다");
        }
    }
}
