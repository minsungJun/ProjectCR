using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ProjectCR.Tests
{
    /// <summary>
    /// 확률 음료가 보상 후보에 들어가고, 마실 때 고른 대상(여러 개면 똑같이 나눠)에만 가중치가 붙는지.
    /// </summary>
    public class ProbabilityDrinkFlowTests
    {
        private static readonly BetTarget Red = BetTarget.Outside(BetKind.Red);

        private TestAssets assets;
        private RouletteDefinition definition;
        private GameBalance balance;
        private List<ProbabilityDrinkDefinition> chances;

        [SetUp]
        public void SetUp()
        {
            assets = new TestAssets();
            definition = assets.Definition();
            balance = assets.Balance();
            chances = ProbabilityDrinkDefinition.CreateDefaults().ToList();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (ProbabilityDrinkDefinition drink in chances)
            {
                UnityEngine.Object.DestroyImmediate(drink);
            }

            assets.DestroyAll();
        }

        /// <summary>확률 음료만 나오는 목록(지급 계열 비중 0).</summary>
        private DrinkCatalog ChanceOnly(int offerCount = 4) =>
            DrinkCatalog.Create(new DrinkDefinition[0], offerCount, chances, payoutShare: 0f, probabilityShare: 1f);

        private static GameFlowController PassRound(GameFlowController flow)
        {
            flow.ForcedNumbers = new[] { 7 };
            flow.PlaceBet(BetTarget.Straight(7), 10);
            flow.TryBeginSpin(out ActiveSpin first);
            flow.CompleteSpin(first.Id, out _);
            flow.ForcedNumbers = new[] { 8 };
            for (int i = 0; i < 4; i++)
            {
                flow.PlaceBet(BetTarget.Straight(7), 1);
                flow.TryBeginSpin(out ActiveSpin spin);
                flow.CompleteSpin(spin.Id, out _);
            }

            Assert.AreEqual(PaymentOutcome.Paid, flow.SubmitPayment());
            Assert.AreEqual(GamePhase.DrinkSelection, flow.Phase);
            return flow;
        }

        [Test]
        public void TargetOptions_AreThatGroupsBets_AndNeverSkull()
        {
            foreach (ProbabilityDrinkDefinition drink in chances)
            {
                List<BetTarget> options = ProbabilityDrinkDefinition.TargetOptions(drink.Group);
                int expected = drink.Group == DrinkGroup.Number ? 37
                    : drink.Group == DrinkGroup.Column || drink.Group == DrinkGroup.Dozen ? 3 : 2;
                Assert.AreEqual(expected, options.Count, $"{drink.Group}");
                Assert.AreEqual(options.Count, options.Distinct().Count());
                foreach (BetTarget t in options)
                {
                    Assert.AreEqual(drink.Group, DrinkGroups.Of(t.Kind), "대상은 그 음료 묶음의 베팅 하나.");
                    Assert.AreNotEqual(RouletteDefinition.SkullNumber, t.Number);
                    if (t.IsStraight)
                    {
                        Assert.That(t.Number, Is.InRange(0, 36));
                    }
                }

                decimal boost = drink.Group == DrinkGroup.Number ? 0.17m
                    : drink.Group == DrinkGroup.Column || drink.Group == DrinkGroup.Dozen ? 0.26m : 0.37m;
                Assert.AreEqual(boost, DrinkOffer.ForProbability(drink, options[0]).Boost, $"{drink.Group}");
            }
        }

        /// <summary>모든 확률 음료는 후보에 대상 하나가 정해져 나오고, 마시면 그 대상에만 한 잔이 붙는다.</summary>
        [Test]
        public void ChanceDrink_UsesItsOfferedTarget()
        {
            var flow = PassRound(new GameFlowController(definition, balance, 5, ChanceOnly()));
            foreach (DrinkOffer offered in flow.Rewards.CurrentOffer)
            {
                CollectionAssert.Contains(offered.Group.Targets(), offered.Target, $"{offered.Group}: 대상은 그 묶음의 대상 하나");
            }

            DrinkOffer drink = flow.Rewards.CurrentOffer[0];
            Assert.AreSame(drink, flow.ConfirmDrink(0));
            Assert.IsNull(flow.ConfirmDrink(0), "두 번 확정되지 않는다.");

            Assert.AreEqual(drink.Target, flow.Probabilities.Effects.Single().Target);
            Assert.AreEqual(drink.Boost, flow.Probabilities.Effects.Single().Total);
            CollectionAssert.AreEqual(new[] { drink.Target }, flow.LastDrinkTargets);
            CollectionAssert.AreEqual(new[] { drink.Target }, flow.DrunkTargets());
            Assert.AreEqual(0, flow.Drinks.TotalStacks, "지급 배율 음료는 늘지 않는다.");
            Assert.AreEqual(2, flow.Round.Round);
        }

        /// <summary>한 대상만 고른 잔과 나눠 받은 잔이 쌓이면 증가량이 그대로 더해진다(0.26 + 0.13).</summary>
        [Test]
        public void SplitAndSingleDrinks_AddUp()
        {
            var state = new ProbabilityDrinkRunState();
            BetTarget c1 = BetTarget.Outside(BetKind.Column1);
            BetTarget c2 = BetTarget.Outside(BetKind.Column2);
            state.AddSplit(new[] { c1 }, 0.26m);
            state.AddSplit(new[] { c1, c2 }, 0.26m);

            Assert.AreEqual(0.39m, state.Effects.First(e => e.Target == c1).Total);
            Assert.AreEqual(2, state.StacksOf(c1));
            Assert.AreEqual(0.13m, state.Effects.First(e => e.Target == c2).Total);
        }

        [Test]
        public void Offer_MixesFamilies_WithoutDuplicates()
        {
            DrinkCatalog catalog = assets.Drinks(offerCount: 4, withProbability: true);
            bool sawPayout = false, sawChance = false;
            for (int seed = 1; seed <= 100; seed++)
            {
                IReadOnlyList<DrinkOffer> offer = new DrinkRewardController(catalog, seed).CreateOffer();
                Assert.AreEqual(4, offer.Count);
                Assert.AreEqual(4, offer.Select(o => (object)o.Payout ?? o.Probability).Distinct().Count(), "같은 음료는 한 후보에 한 번.");
                sawPayout |= offer.Any(o => !o.IsProbability);
                sawChance |= offer.Any(o => o.IsProbability);
            }

            Assert.IsTrue(sawPayout && sawChance);
        }

        private static Dictionary<int, int> Draws(IReadOnlyList<BetTarget> drunk, int times) =>
            DrawCounts(DrinkGroup.Number, drunk, times).ToDictionary(p => p.Key.Number, p => p.Value);

        private static Dictionary<BetTarget, int> DrawCounts(DrinkGroup group, IReadOnlyList<BetTarget> drunk, int times)
        {
            var random = new System.Random(1234);
            var counts = new Dictionary<BetTarget, int>();
            for (int i = 0; i < times; i++)
            {
                BetTarget t = DrinkRewardController.DrawTarget(group, drunk, random);
                counts[t] = counts.TryGetValue(t, out int c) ? c + 1 : 1;
            }

            return counts;
        }

        [Test]
        public void ColumnDraw_NothingDrunk_EvenThirds()
        {
            Dictionary<BetTarget, int> counts = DrawCounts(DrinkGroup.Column, new BetTarget[0], 30_000);
            Assert.AreEqual(3, counts.Count);
            foreach (int count in counts.Values)
            {
                Assert.AreEqual(1d / 3d, count / 30_000d, 0.02);
            }
        }

        /// <summary>1열을 마셨으면 1열 50%, 나머지 50%는 2열·3열이 반씩.</summary>
        [Test]
        public void ColumnDraw_OneDrunk_HalfTheTime()
        {
            BetTarget c1 = BetTarget.Outside(BetKind.Column1);
            Dictionary<BetTarget, int> counts = DrawCounts(DrinkGroup.Column, new[] { c1 }, 30_000);
            Assert.AreEqual(0.5, counts[c1] / 30_000d, 0.02);
            Assert.AreEqual(0.25, counts[BetTarget.Outside(BetKind.Column2)] / 30_000d, 0.02);
            Assert.AreEqual(0.25, counts[BetTarget.Outside(BetKind.Column3)] / 30_000d, 0.02);
        }

        /// <summary>다른 묶음에서 마신 대상은 상관없고, 묶음 대상을 다 마셨으면 그 중에서 똑같이.</summary>
        [Test]
        public void Draw_IgnoresOtherGroups_AndAllDrunkStaysEven()
        {
            Dictionary<BetTarget, int> color = DrawCounts(DrinkGroup.Color, new[] { BetTarget.Outside(BetKind.Column1), BetTarget.Straight(7) }, 20_000);
            Assert.AreEqual(0.5, color[Red] / 20_000d, 0.02, "컬럼·숫자를 마셔도 색깔 음료는 반반");

            Dictionary<BetTarget, int> both = DrawCounts(DrinkGroup.Color, new[] { Red, BetTarget.Outside(BetKind.Black) }, 20_000);
            Assert.AreEqual(0.5, both[Red] / 20_000d, 0.02);
        }

        [Test]
        public void NumberDraw_NothingDrunk_AnyOf37()
        {
            Dictionary<int, int> counts = Draws(new BetTarget[0], 37_000);
            Assert.AreEqual(37, counts.Count, "0~36 모두 나온다");
            Assert.That(counts.Values.Min(), Is.GreaterThan(800));
            Assert.That(counts.Values.Max(), Is.LessThan(1200));
        }

        [Test]
        public void NumberDraw_OneDrunk_HalfTheTime()
        {
            Dictionary<int, int> counts = Draws(new[] { BetTarget.Straight(7) }, 20_000);
            Assert.AreEqual(0.5, counts[7] / 20_000d, 0.02, "마신 7이 50%");
            Assert.AreEqual(37, counts.Count, "나머지 50%는 다른 숫자");
        }

        [Test]
        public void NumberDraw_TwoDrunk_ThirdEach()
        {
            Dictionary<int, int> counts = Draws(new[] { BetTarget.Straight(7), BetTarget.Straight(12) }, 30_000);
            Assert.AreEqual(1d / 3d, counts[7] / 30_000d, 0.02);
            Assert.AreEqual(1d / 3d, counts[12] / 30_000d, 0.02);
            int fresh = counts.Where(p => p.Key != 7 && p.Key != 12).Sum(p => p.Value);
            Assert.AreEqual(1d / 3d, fresh / 30_000d, 0.02, "새 숫자 1/3");
        }

        [Test]
        public void NumberDraw_AllDrunk_PicksOnlyThose()
        {
            var all = Enumerable.Range(0, 37).Select(BetTarget.Straight).ToList();
            Assert.AreEqual(37, Draws(all, 2_000).Count);
        }

        /// <summary>기본 계수는 "대상 하나만 골라 1잔 마시면 그 대상 베팅의 기대 환수율 ≈ 110%" 기준이다.</summary>
        [Test]
        public void DefaultBoosts_GiveAbout110PercentReturn_AfterOneDrink()
        {
            var flow = new GameFlowController(definition, balance, 5, ChanceOnly());
            var targets = new List<BetTarget>
            {
                Red, BetTarget.Outside(BetKind.Odd), BetTarget.Outside(BetKind.High),
                BetTarget.Outside(BetKind.Column2), BetTarget.Outside(BetKind.Dozen3),
                BetTarget.Straight(17), BetTarget.Straight(0),
            };

            foreach (BetTarget target in targets)
            {
                ProbabilityDrinkDefinition drink = chances.First(d => d.Group == DrinkGroups.Of(target.Kind));
                var one = new GameFlowController(definition, balance, 5, ChanceOnly());
                one.Probabilities.AddSplit(new[] { target }, drink.Boost);
                SpinConfig config = one.BuildSpinConfig();
                double rtp = config.ProbabilityOf(target) * (double)config.MultiplierFor(target);

                Assert.That(rtp, Is.InRange(1.08, 1.11), $"{target}");
            }

            Assert.AreEqual(0.947, flow.BuildSpinConfig().ProbabilityOf(Red) * 2, 0.001, "강화 전 94.7%");
        }

        [Test]
        public void SameTarget_Stacks_DifferentTarget_IsSeparate()
        {
            var state = new ProbabilityDrinkRunState();
            state.AddStack(Red, 0.1m);
            state.AddStack(Red, 0.1m);
            state.AddStack(BetTarget.Outside(BetKind.Black), 0.1m);

            Assert.AreEqual(2, state.StacksOf(Red));
            Assert.AreEqual(1, state.StacksOf(BetTarget.Outside(BetKind.Black)));
            Assert.AreEqual(2, state.Effects.Count);
        }

        [Test]
        public void ProbabilityDrink_ChangesDraws_ButNotPayout()
        {
            var flow = new GameFlowController(definition, balance, 5, ChanceOnly());
            flow.Probabilities.SetStacks(Red, 0.1m, 10);
            SpinConfig config = flow.BuildSpinConfig();

            Assert.AreEqual(2m, config.MultiplierFor(Red), "확률 음료는 지급 배수를 바꾸지 않는다.");
            Assert.Greater(config.ProbabilityOf(Red), 18d / 38d);
        }
    }
}
