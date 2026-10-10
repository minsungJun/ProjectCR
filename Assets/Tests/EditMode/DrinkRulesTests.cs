using System;
using System.Linq;
using NUnit.Framework;

namespace ProjectCR.Tests
{
    /// <summary>
    /// 음료 효과 규칙: 베팅 종류 → 묶음, 묶음 배율 = 1.5^잔 수, 최종 배수 = 기본 × 묶음 배율,
    /// 회전 합계를 정확히 더한 뒤 한 번만 버림. 해골은 음료보다 먼저라 지급 0.
    /// </summary>
    public class DrinkRulesTests
    {
        private const int Skull = RouletteDefinition.SkullNumber;
        private const long Max = 999_999_999_999;

        private static readonly BetTarget Red = BetTarget.Outside(BetKind.Red);
        private static readonly BetTarget Column1 = BetTarget.Outside(BetKind.Column1);
        private static readonly BetTarget Dozen1 = BetTarget.Outside(BetKind.Dozen1);

        private TestAssets assets;
        private RouletteDefinition definition;
        private GameFlowController flow;

        [SetUp]
        public void SetUp()
        {
            assets = new TestAssets();
            definition = assets.Definition();
            flow = new GameFlowController(definition, assets.Balance(), seed: 5, assets.Drinks());
        }

        [TearDown]
        public void TearDown() => assets.DestroyAll();

        private SettlementResult Settle(SpinResult spin, params ConfirmedBet[] bets)
        {
            return PayoutCalculator.Calculate(bets, spin, flow.BuildSpinConfig(), Max);
        }

        // ---------- 묶음 ----------

        [TestCase(BetKind.Column1, DrinkGroup.Column)]
        [TestCase(BetKind.Column2, DrinkGroup.Column)]
        [TestCase(BetKind.Column3, DrinkGroup.Column)]
        [TestCase(BetKind.Dozen1, DrinkGroup.Dozen)]
        [TestCase(BetKind.Dozen2, DrinkGroup.Dozen)]
        [TestCase(BetKind.Dozen3, DrinkGroup.Dozen)]
        [TestCase(BetKind.Odd, DrinkGroup.Parity)]
        [TestCase(BetKind.Even, DrinkGroup.Parity)]
        [TestCase(BetKind.Red, DrinkGroup.Color)]
        [TestCase(BetKind.Black, DrinkGroup.Color)]
        [TestCase(BetKind.Low, DrinkGroup.Range)]
        [TestCase(BetKind.High, DrinkGroup.Range)]
        [TestCase(BetKind.Straight, DrinkGroup.Number)]
        [TestCase(BetKind.Split, DrinkGroup.Number)]
        [TestCase(BetKind.Street, DrinkGroup.Number)]
        [TestCase(BetKind.Corner, DrinkGroup.Number)]
        [TestCase(BetKind.SixLine, DrinkGroup.Number)]
        public void EveryBetKind_BelongsToExactlyOneGroup(BetKind kind, DrinkGroup expected)
        {
            Assert.AreEqual(expected, DrinkGroups.Of(kind));
        }

        [Test]
        public void EveryBetKind_IsMapped_AndNoneIsNot()
        {
            foreach (BetKind kind in (BetKind[])Enum.GetValues(typeof(BetKind)))
            {
                if (kind == BetKind.None)
                {
                    Assert.Throws<ArgumentException>(() => DrinkGroups.Of(kind));
                }
                else
                {
                    Assert.DoesNotThrow(() => DrinkGroups.Of(kind), kind.ToString());
                }
            }
        }

        [Test]
        public void DefaultCatalog_HasOneDrinkPerGroup()
        {
            DrinkDefinition[] drinks = DrinkCatalog.CreateDefaultDrinks();
            try
            {
                CollectionAssert.AreEquivalent(Enum.GetValues(typeof(DrinkGroup)), drinks.Select(d => d.Group));
                Assert.IsTrue(drinks.All(d => d.BoostPerStack == 1.5m));
            }
            finally
            {
                foreach (DrinkDefinition drink in drinks)
                {
                    UnityEngine.Object.DestroyImmediate(drink);
                }
            }
        }

        // ---------- 배율 ----------

        [Test]
        public void Stacks_MultiplyFromBase_NotFromPreviousResult()
        {
            Assert.AreEqual(1m, DrinkRunState.Power(1.5m, 0));
            Assert.AreEqual(1.5m, DrinkRunState.Power(1.5m, 1));
            Assert.AreEqual(2.25m, DrinkRunState.Power(1.5m, 2));
            Assert.AreEqual(3.375m, DrinkRunState.Power(1.5m, 3));

            flow.Drinks.AddStack(Red);
            flow.Drinks.AddStack(Red);
            Assert.AreEqual(2.25m, flow.Drinks.MultiplierOf(Red));
            Assert.AreEqual(4.5m, flow.BuildSpinConfig().MultiplierFor(Red));
            Assert.AreEqual(2m, flow.BuildSpinConfig().MultiplierFor(BetTarget.Outside(BetKind.Black)), "같은 묶음의 다른 대상은 그대로.");
            Assert.AreEqual(4.5m, flow.BuildSpinConfig().MultiplierFor(Red), "설정을 다시 만들어도 겹쳐 곱하지 않는다.");
        }

        [Test]
        public void HugeStacks_StopAtCap_WithoutOverflow()
        {
            flow.Drinks.SetStacks(BetTarget.Straight(17), 1_000_000);

            Assert.AreEqual(DrinkRunState.MultiplierCap, flow.Drinks.MultiplierOf(BetTarget.Straight(17)));
            SettlementResult s = Settle(SpinResult.FromNumbers(definition, 17, 17), new ConfirmedBet(17, 1));
            Assert.AreEqual(Max, s.TotalPayout);
        }

        [Test]
        public void ExactPayout_SaturatesOnDecimalOverflow()
        {
            Assert.AreEqual((decimal)Max, PayoutCalculator.ExactPayout(long.MaxValue, DrinkRunState.MultiplierCap, Max));
            Assert.AreEqual(Max, PayoutCalculator.ToMoney(decimal.MaxValue, Max));
        }

        [Test]
        public void ColumnDrink_DoesNotBoostDozen()
        {
            flow.Drinks.AddStack(Column1);
            SpinConfig config = flow.BuildSpinConfig();

            Assert.AreEqual(4.5m, config.MultiplierFor(Column1));
            Assert.AreEqual(3m, config.MultiplierFor(BetTarget.Outside(BetKind.Column2)), "1열 음료는 2열에 붙지 않는다.");
            Assert.AreEqual(3m, config.MultiplierFor(Dozen1));
            Assert.AreEqual(3, config.PayoutFor(Column1), "기본 배수는 그대로.");
        }

        [Test]
        public void ColorDrink_DoesNotBoostNumberBetOnRedNumber()
        {
            flow.Drinks.AddStack(Red);

            // 32는 빨강. 숫자 베팅은 Number 묶음이라 색상 음료가 붙지 않는다.
            SettlementResult s = Settle(SpinResult.FromNumbers(definition, 32), new ConfirmedBet(32, 10), new ConfirmedBet(Red, 10));
            CollectionAssert.AreEqual(new long[] { 360, 30 }, s.Bets.Select(b => b.Amount));
        }

        [Test]
        public void NoDrinks_PaysAsBefore()
        {
            SettlementResult s = Settle(SpinResult.FromNumbers(definition, 32), new ConfirmedBet(32, 5), new ConfirmedBet(Red, 10));
            Assert.AreEqual(180 + 20, s.TotalPayout);
        }

        // ---------- 정산 ----------

        [Test]
        public void Total_IsFlooredOnce_NotPerBet()
        {
            flow.Drinks.AddStack(Red);
            flow.Drinks.AddStack(Red);
            flow.Drinks.AddStack(Column1);

            // 34: 빨강, 1열. 빨강 1 × 4.5 + 1열 1 × 4.5 = 9. 베팅마다 버렸다면 8.
            SettlementResult s = Settle(SpinResult.FromNumbers(definition, 34), new ConfirmedBet(Red, 1), new ConfirmedBet(Column1, 1));

            Assert.AreEqual(9, s.TotalPayout);
            CollectionAssert.AreEqual(new[] { 4.5m, 4.5m }, s.Bets.Select(b => b.ExactAmount));
            CollectionAssert.AreEqual(new long[] { 4, 4 }, s.Bets.Select(b => b.Amount), "베팅별 금액은 표시용 버림.");
        }

        [Test]
        public void MultipleBalls_SumExactly_ThenFloor()
        {
            flow.Drinks.AddStack(Red);
            flow.Drinks.AddStack(Red);

            // 빨강 1 × 4.5가 두 번 = 9.
            SettlementResult s = Settle(SpinResult.FromNumbers(definition, 32, 34), new ConfirmedBet(Red, 1));
            Assert.AreEqual(9, s.TotalPayout);
            Assert.AreEqual(2, s.WinCount);

            // 남는 소수는 버린다: 4.5 한 번 → 4.
            Assert.AreEqual(4, Settle(SpinResult.FromNumbers(definition, 32, 2), new ConfirmedBet(Red, 1)).TotalPayout);
        }

        [Test]
        public void NumberDrink_BoostsEveryNumberBet_IncludingZero()
        {
            flow.Drinks.AddGroupStack(DrinkGroup.Number);
            BetTarget split = BetTarget.Inside(BetKind.Split, 17, 20);

            // 숫자 한 칸 ×36 × 1.5, 스플릿 ×18 × 1.5.
            SettlementResult s = Settle(SpinResult.FromNumbers(definition, 17), new ConfirmedBet(17, 2), new ConfirmedBet(split, 2));
            CollectionAssert.AreEqual(new long[] { 108, 54 }, s.Bets.Select(b => b.Amount));

            SpinConfig config = flow.BuildSpinConfig();
            Assert.AreEqual(54m, config.MultiplierFor(BetTarget.Straight(0)), "0도 숫자 베팅");
            Assert.AreEqual(54m, config.MultiplierFor(BetTarget.Straight(20)));
            Assert.AreEqual(2m, config.MultiplierFor(Red), "바깥 베팅에는 붙지 않는다");
        }

        [Test]
        public void Skull_PaysZero_EvenWithDrinks()
        {
            flow.Drinks.SetStacks(Red, 5);
            flow.Drinks.SetStacks(BetTarget.Straight(32), 5);

            SettlementResult s = Settle(SpinResult.FromNumbers(definition, 32, Skull), new ConfirmedBet(Red, 10), new ConfirmedBet(32, 10));

            Assert.AreEqual(SpinOutcome.SkullBust, s.Outcome);
            Assert.AreEqual(0, s.TotalPayout);
            Assert.IsTrue(s.Bets.All(b => b.ExactAmount == 0m && !b.IsWin));
        }

        [Test]
        public void Stake_IsChargedTheSame_WithDrinks()
        {
            flow.Drinks.SetStacks(Red, 3);
            flow.ForcedNumbers = new[] { 32 };
            flow.PlaceBet(Red, 10);

            Assert.AreEqual(BetError.None, flow.TryBeginSpin(out ActiveSpin spin));
            Assert.AreEqual(90, flow.Wallet.Balance, "음료는 베팅금 차감에 영향이 없다.");

            Assert.IsTrue(flow.CompleteSpin(spin.Id, out SettlementResult s));
            Assert.AreEqual(67, s.TotalPayout, "10 × 2 × 3.375 = 67.5 → 67");
            Assert.AreEqual(157, flow.Wallet.Balance);
        }

        [Test]
        public void SpinConfig_KeepsBoostOfTheSpinItWasMadeFor()
        {
            flow.ForcedNumbers = new[] { 32 };
            flow.PlaceBet(Red, 10);
            flow.TryBeginSpin(out ActiveSpin spin);

            // 회전 중에 잔 수가 바뀌어도(디버그) 이미 시작한 회전은 시작 때 설정으로 정산한다.
            flow.Drinks.SetStacks(Red, 4);
            flow.CompleteSpin(spin.Id, out SettlementResult s);
            Assert.AreEqual(20, s.TotalPayout);
        }

        // ---------- 표시 ----------

        [TestCase("2", "2")]
        [TestCase("2.25", "2.25")]
        [TestCase("3.375", "3.38")]
        [TestCase("54", "54")]
        [TestCase("82.0125", "82")]
        [TestCase("123.45", "123.5")]
        [TestCase("1234567.8", "1,234,568")]
        public void MultiplierText_RoundsOnlyForDisplay(string value, string expected)
        {
            Assert.AreEqual(expected, NumberText.Multiplier(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));
        }
    }
}
