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
                Assert.AreEqual(boost, DrinkOffer.ForProbability(drink).Boost, $"{drink.Group}");
            }
        }

        [Test]
        public void ConfirmProbabilityDrink_RejectsMissingOrWrongTargets_AndKeepsOffer()
        {
            var flow = PassRound(new GameFlowController(definition, balance, 5, ChanceOnly()));
            int column = Index(flow, DrinkGroup.Column);

            Assert.IsNull(flow.ConfirmDrink(column), "대상 없이 마실 수 없다");
            Assert.IsNull(flow.ConfirmDrink(column, new BetTarget[0]));
            Assert.IsNull(flow.ConfirmDrink(column, new[] { Red }), "다른 묶음의 대상");
            BetTarget c1 = BetTarget.Outside(BetKind.Column1);
            Assert.IsNull(flow.ConfirmDrink(column, new[] { c1, c1 }), "같은 대상 두 번");
            Assert.AreEqual(GamePhase.DrinkSelection, flow.Phase);
            Assert.IsNotNull(flow.Rewards.CurrentOffer, "후보는 그대로");
            Assert.AreEqual(0, flow.Probabilities.TotalStacks);
        }

        /// <summary>컬럼 +26%를 두 컬럼에 고르면 각각 +13%, 고르지 않은 컬럼은 그대로.</summary>
        [Test]
        public void ConfirmProbabilityDrink_SplitsBoostEvenly_AcrossChosenTargets()
        {
            var flow = PassRound(new GameFlowController(definition, balance, 5, ChanceOnly()));
            int column = Index(flow, DrinkGroup.Column);
            DrinkOffer offered = flow.Rewards.CurrentOffer[column];
            BetTarget c1 = BetTarget.Outside(BetKind.Column1);
            BetTarget c2 = BetTarget.Outside(BetKind.Column2);
            BetTarget c3 = BetTarget.Outside(BetKind.Column3);

            (double now, double after) = flow.PreviewProbabilityDrink(new[] { c1, c2 }, offered.Boost);
            Assert.AreEqual(0, flow.Probabilities.TotalStacks, "미리보기는 상태를 바꾸지 않는다.");

            DrinkOffer drink = flow.ConfirmDrink(column, new[] { c1, c2 });
            Assert.AreSame(offered, drink);
            Assert.IsNull(flow.ConfirmDrink(column, new[] { c1 }), "두 번 확정되지 않는다.");
            CollectionAssert.AreEqual(new[] { c1, c2 }, flow.LastDrinkTargets);

            Assert.AreEqual(0.13m, flow.Probabilities.Effects.First(e => e.Target == c1).Total);
            Assert.AreEqual(0.13m, flow.Probabilities.Effects.First(e => e.Target == c2).Total);
            Assert.AreEqual(0, flow.Probabilities.StacksOf(c3));

            SpinConfig config = flow.BuildSpinConfig();
            Assert.AreEqual(after, config.ProbabilityOf(c1) + config.ProbabilityOf(c2), 1e-12, "미리보기와 같은 확률");
            Assert.Greater(after, now);
            Assert.AreEqual(config.ProbabilityOf(c1), config.ProbabilityOf(c2), 1e-12, "똑같이 나눠 받는다");
            Assert.Less(config.ProbabilityOf(c3), 12d / 38d, "고르지 않은 컬럼은 상대적으로 줄어든다");
            Assert.AreEqual(1d / 38d, config.PocketProbabilities()[definition.IndexOfSkull()], 1e-12);
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

        private static int Index(GameFlowController flow, DrinkGroup group)
        {
            IReadOnlyList<DrinkOffer> offer = flow.Rewards.CurrentOffer;
            for (int i = 0; i < offer.Count; i++)
            {
                if (offer[i].Group == group)
                {
                    return i;
                }
            }

            Assert.Fail($"{group} 음료가 후보에 없다");
            return -1;
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

        [Test]
        public void ConfirmProbabilityDrink_OneTarget_GetsWholeBoost()
        {
            var flow = PassRound(new GameFlowController(definition, balance, 5, ChanceOnly()));
            int number = Index(flow, DrinkGroup.Number);
            BetTarget seven = BetTarget.Straight(7);

            DrinkOffer drink = flow.ConfirmDrink(number, new[] { seven });
            Assert.IsNotNull(drink);
            Assert.AreEqual(0.17m, flow.Probabilities.Effects.Single().Total);
            Assert.AreEqual(1, flow.Probabilities.TotalStacks);
            Assert.AreEqual(0, flow.Drinks.TotalStacks, "지급 배율 음료는 늘지 않는다.");
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
