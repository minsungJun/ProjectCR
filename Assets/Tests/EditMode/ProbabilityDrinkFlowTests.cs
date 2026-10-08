using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ProjectCR.Tests
{
    /// <summary>
    /// 확률 음료가 보상 후보에 들어가고, 구체적인 대상이 후보를 만들 때 정해지며, 확정 한 번에 그 대상만 한 잔 느는지.
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
        public void OfferTargets_AreConcrete_AndNeverSkull()
        {
            DrinkCatalog catalog = ChanceOnly(6);
            for (int seed = 1; seed <= 300; seed++)
            {
                foreach (DrinkOffer offer in new DrinkRewardController(catalog, seed).CreateOffer())
                {
                    BetTarget t = offer.Target;
                    Assert.IsTrue(offer.IsProbability);
                    Assert.IsFalse(t.IsNone);
                    Assert.AreNotEqual(RouletteDefinition.SkullNumber, t.Number);
                    Assert.AreEqual(offer.Group, DrinkGroups.Of(t.Kind), "대상은 그 음료 묶음의 베팅 하나.");

                    if (t.IsStraight)
                    {
                        Assert.That(t.Number, Is.InRange(0, 36));
                        Assert.AreEqual(0.17m, offer.Boost);
                    }
                    else if (t.IsInside)
                    {
                        Assert.IsTrue(InsideBets.IsValid(t), "베팅판에서 걸 수 있는 조합.");
                        Assert.IsFalse(t.CoversNumber(0, PocketColor.Green), "숫자 묶음에는 0이 없다.");
                        Assert.That(t.Numbers.Count(), Is.InRange(2, 6));
                        Assert.AreEqual(0.18m, offer.Boost);
                    }
                    else if (offer.Group == DrinkGroup.Column || offer.Group == DrinkGroup.Dozen)
                    {
                        Assert.AreEqual(0.26m, offer.Boost);
                    }
                    else
                    {
                        Assert.AreEqual(0.37m, offer.Boost);
                    }
                }
            }
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
        public void OfferTargets_StayFixed_UntilPicked()
        {
            var flow = PassRound(new GameFlowController(definition, balance, 5, ChanceOnly()));
            var before = flow.Rewards.CurrentOffer.Select(o => o.Target).ToList();

            CollectionAssert.AreEqual(before, flow.Rewards.CreateOffer().Select(o => o.Target).ToList());
        }

        [Test]
        public void ConfirmProbabilityDrink_AddsOneStackToThatTarget_Only()
        {
            var flow = PassRound(new GameFlowController(definition, balance, 5, ChanceOnly()));
            DrinkOffer offered = flow.Rewards.CurrentOffer[1];

            // 카드 미리보기와 확정 뒤 실제 확률이 같아야 한다.
            SpinConfig current = flow.BuildSpinConfig();
            double[] previewWeights = PocketWeightCalculator.Compute(definition, flow.Probabilities.WithExtraStack(offered.Target, offered.Boost));
            double preview = PocketWeightCalculator.ProbabilityOf(definition,
                PocketWeightCalculator.Probabilities(definition, previewWeights, current.SkullChance), offered.Target);
            Assert.AreEqual(0, flow.Probabilities.TotalStacks, "미리보기는 상태를 바꾸지 않는다.");

            DrinkOffer drink = flow.ConfirmDrink(1);
            Assert.AreSame(offered, drink);
            Assert.IsNull(flow.ConfirmDrink(1), "두 번 확정되지 않는다.");

            Assert.AreEqual(1, flow.Probabilities.StacksOf(drink.Target));
            Assert.AreEqual(1, flow.Probabilities.TotalStacks);
            Assert.AreEqual(0, flow.Drinks.TotalStacks, "지급 배율 음료는 늘지 않는다.");
            Assert.AreEqual(preview, flow.BuildSpinConfig().ProbabilityOf(drink.Target), 1e-12);
            Assert.AreEqual(1d / 38d, flow.BuildSpinConfig().PocketProbabilities()[definition.IndexOfSkull()], 1e-12);
            Assert.AreEqual(2, flow.Round.Round);
        }

        /// <summary>기본 계수는 "1잔 마시면 그 대상 베팅의 기대 환수율 ≈ 110%" 기준이다.</summary>
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
            targets.AddRange(ProbabilityDrinkDefinition.NumberSets());

            foreach (BetTarget target in targets)
            {
                ProbabilityDrinkDefinition drink = chances.First(d => d.Group == DrinkGroups.Of(target.Kind));
                var one = new GameFlowController(definition, balance, 5, ChanceOnly());
                one.Probabilities.AddStack(target, drink.BoostFor(target));
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
