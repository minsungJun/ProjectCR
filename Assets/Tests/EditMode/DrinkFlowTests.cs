using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ProjectCR.Tests
{
    /// <summary>
    /// 라운드 종료 → 납부 판정(1회) → 음료 선택(성공 때만) → 다음 라운드.
    /// 기본 밸런스: 초기 100, 회전 5, 납부 120 → 300. 기본 음료 6종, 후보 4종.
    /// </summary>
    public class DrinkFlowTests
    {
        private const int Skull = RouletteDefinition.SkullNumber;
        private static readonly BetTarget Red = BetTarget.Outside(BetKind.Red);

        private TestAssets assets;
        private RouletteDefinition definition;
        private GameBalance balance;
        private DrinkCatalog catalog;
        private GameFlowController flow;

        [SetUp]
        public void SetUp()
        {
            assets = new TestAssets();
            definition = assets.Definition();
            balance = assets.Balance();
            catalog = assets.Drinks();
            flow = NewFlow(42, catalog);
        }

        [TearDown]
        public void TearDown() => assets.DestroyAll();

        private GameFlowController NewFlow(int seed, DrinkCatalog drinks) => new GameFlowController(definition, balance, seed, drinks);

        private static void SpinAndSettle(GameFlowController game, BetTarget target, long stake, int forced)
        {
            game.ForcedNumbers = new[] { forced };
            Assert.AreEqual(BetError.None, game.PlaceBet(target, stake));
            Assert.AreEqual(BetError.None, game.TryBeginSpin(out ActiveSpin spin));
            Assert.IsTrue(game.CompleteSpin(spin.Id, out _));
        }

        /// <summary>7번 1 적중(135) 후 4번 낙첨 → 131로 라운드 종료.</summary>
        private static void PlayPassableRound(GameFlowController game)
        {
            SpinAndSettle(game, BetTarget.Straight(7), 1, 7);
            for (int i = 0; i < 4; i++)
            {
                SpinAndSettle(game, BetTarget.Straight(7), 1, 8);
            }

            Assert.AreEqual(GamePhase.RoundPayment, game.Phase);
            Assert.AreEqual(131, game.Wallet.Balance);
        }

        private int OfferIndexOf(DrinkGroup group)
        {
            IReadOnlyList<DrinkOffer> offer = flow.Rewards.CurrentOffer;
            for (int i = 0; i < offer.Count; i++)
            {
                if (offer[i].Group == group)
                {
                    return i;
                }
            }

            return -1;
        }

        // ---------- 납부 ----------

        [Test]
        public void Payment_AfterLastSpin_ChargesOnce_ThenOffersDrinks()
        {
            PlayPassableRound(flow);

            Assert.AreEqual(PaymentOutcome.Paid, flow.SubmitPayment());
            Assert.IsNull(flow.SubmitPayment(), "두 번째 판정은 거절된다.");

            Assert.AreEqual(GamePhase.DrinkSelection, flow.Phase);
            Assert.AreEqual(11, flow.Wallet.Balance, "납부금은 한 번만 빠지고 나머지는 이월.");
            Assert.AreEqual(120, flow.LastPaidDue);
            Assert.AreEqual(1, flow.Round.Round, "음료를 고르기 전에는 라운드가 넘어가지 않는다.");
            Assert.AreEqual(4, flow.Rewards.CurrentOffer.Count);
        }

        [Test]
        public void PaymentFailure_IsGameOver_WithoutDrinks()
        {
            for (int i = 0; i < 5; i++)
            {
                SpinAndSettle(flow, BetTarget.Straight(7), 1, 8);
            }

            Assert.AreEqual(PaymentOutcome.Insufficient, flow.SubmitPayment());
            Assert.AreEqual(GamePhase.GameOver, flow.Phase);
            Assert.AreEqual(95, flow.Wallet.Balance, "실패하면 아무것도 빼지 않는다.");
            Assert.IsNull(flow.Rewards.CurrentOffer, "실패하면 음료 후보가 없다.");
            Assert.IsNull(flow.ConfirmDrink(0));
        }

        [Test]
        public void OutOfMoney_WithSpinsLeft_IsJudgedAtOnce()
        {
            SpinAndSettle(flow, BetTarget.Straight(7), 100, 8);

            Assert.AreEqual(GamePhase.RoundPayment, flow.Phase);
            Assert.AreEqual(4, flow.Round.ForfeitedSpins);
            Assert.AreEqual(PaymentOutcome.Insufficient, flow.SubmitPayment());
            Assert.AreEqual(GamePhase.GameOver, flow.Phase);
        }

        [Test]
        public void TableInput_IsBlocked_DuringDrinkSelection()
        {
            PlayPassableRound(flow);
            flow.SubmitPayment();

            Assert.AreEqual(BetError.NotInBettingPhase, flow.PlaceChip(Red));
            Assert.AreEqual(BetError.NotInBettingPhase, flow.SetChipValue(5));
            Assert.AreEqual(BetError.NotInBettingPhase, flow.TryBeginSpin(out _));
            Assert.IsFalse(flow.Bet.HasBets);
        }

        // ---------- 후보 ----------

        [Test]
        public void Offer_IsFourDistinctDrinksFromCatalog()
        {
            for (int seed = 1; seed <= 200; seed++)
            {
                var rewards = new DrinkRewardController(catalog, seed);
                IReadOnlyList<DrinkOffer> offer = rewards.CreateOffer();

                Assert.AreEqual(4, offer.Count);
                Assert.AreEqual(4, offer.Select(o => o.Payout).Distinct().Count(), $"seed {seed}");
                Assert.IsTrue(offer.All(o => !o.IsProbability && catalog.Drinks.Contains(o.Payout)));
            }
        }

        [Test]
        public void Offer_CoversEveryDrinkOverManySeeds()
        {
            var seen = new HashSet<DrinkGroup>();
            for (int seed = 1; seed <= 50; seed++)
            {
                foreach (DrinkOffer drink in new DrinkRewardController(catalog, seed).CreateOffer())
                {
                    seen.Add(drink.Group);
                }
            }

            Assert.AreEqual(DrinkGroups.Count, seen.Count);
        }

        [Test]
        public void Offer_IsMadeOnce_UntilPicked()
        {
            PlayPassableRound(flow);
            flow.SubmitPayment();

            IReadOnlyList<DrinkOffer> first = flow.Rewards.CurrentOffer;
            CollectionAssert.AreEqual(first.ToList(), flow.Rewards.CreateOffer().ToList(), "다시 열어도 후보가 그대로.");
        }

        [Test]
        public void SameSeed_GivesSameOffer()
        {
            // 확률 음료까지 넣은 목록: 같은 시드면 같은 음료.
            DrinkCatalog mixed = assets.Drinks(withProbability: true);
            var a = new DrinkRewardController(mixed, 99).CreateOffer().Select(o => (object)o.Payout ?? o.Probability).ToList();
            var b = new DrinkRewardController(mixed, 99).CreateOffer().Select(o => (object)o.Payout ?? o.Probability).ToList();
            CollectionAssert.AreEqual(a, b);
        }

        [Test]
        public void OwnedDrink_CanBeOfferedAgain()
        {
            DrinkCatalog all = assets.Drinks(offerCount: 6);
            flow = NewFlow(42, all);

            PlayPassableRound(flow);
            flow.SubmitPayment();
            DrinkOffer picked = flow.ConfirmDrink(OfferIndexOf(DrinkGroup.Color));
            Assert.AreEqual(DrinkGroup.Color, picked.Group);

            // 2라운드: 11에서 시작해 숫자 적중으로 납부금(300)을 넘긴다.
            SpinAndSettle(flow, BetTarget.Straight(7), 10, 7); // 1 + 360 = 361
            for (int i = 0; i < 4; i++)
            {
                SpinAndSettle(flow, BetTarget.Straight(7), 1, 8);
            }

            Assert.AreEqual(PaymentOutcome.Paid, flow.SubmitPayment());
            Assert.IsTrue(flow.Rewards.CurrentOffer.Any(d => d.Group == DrinkGroup.Color), "이미 마신 음료도 후보에 나온다.");
        }

        [Test]
        public void RewardDraws_DoNotChangeSpinResults()
        {
            GameFlowController a = NewFlow(7, catalog);
            GameFlowController b = NewFlow(7, catalog);

            // a만 보상 난수를 여러 번 쓴다.
            for (int i = 0; i < 5; i++)
            {
                a.Rewards.CreateOffer();
                a.Rewards.Take(0);
            }

            for (int i = 0; i < 5; i++)
            {
                a.PlaceBet(Red, 1);
                b.PlaceBet(Red, 1);
                a.TryBeginSpin(out ActiveSpin sa);
                b.TryBeginSpin(out ActiveSpin sb);
                Assert.AreEqual(sb.Result.Balls[0].Number, sa.Result.Balls[0].Number, $"회전 {i + 1}");
                a.CompleteSpin(sa.Id, out _);
                b.CompleteSpin(sb.Id, out _);
            }
        }

        // ---------- 선택 ----------

        [Test]
        public void ConfirmDrink_AddsOneStack_AndStartsNextRound()
        {
            PlayPassableRound(flow);
            flow.SubmitPayment();

            DrinkOffer offered = flow.Rewards.CurrentOffer[2];
            DrinkOffer drink = flow.ConfirmDrink(2);

            Assert.AreSame(offered, drink);
            Assert.AreEqual(1, flow.Drinks.StacksOf(drink.Target));
            Assert.AreEqual(1, flow.Drinks.TotalStacks, "한 번 확정에 대상 하나만 +1.");
            CollectionAssert.AreEqual(new[] { drink.Target }, flow.LastDrinkTargets);
            Assert.AreSame(drink, flow.LastDrink);

            Assert.AreEqual(GamePhase.Betting, flow.Phase);
            Assert.AreEqual(2, flow.Round.Round);
            Assert.AreEqual(5, flow.Round.SpinsRemaining);
            Assert.AreEqual(300, flow.Round.CurrentDue);
            Assert.AreEqual(11, flow.Wallet.Balance, "돈은 그대로 이어진다.");
            Assert.IsNull(flow.Rewards.CurrentOffer);
        }

        [Test]
        public void PayoutOffer_HasOneTargetOfItsGroup_AndEveryTargetCanAppear()
        {
            var seen = new HashSet<BetTarget>();
            for (int seed = 1; seed <= 400; seed++)
            {
                foreach (DrinkOffer offer in new DrinkRewardController(catalog, seed).CreateOffer())
                {
                    Assert.IsFalse(offer.IsProbability);
                    CollectionAssert.Contains(offer.Group.Targets(), offer.Target, "대상은 그 음료 묶음의 대상 하나");
                    seen.Add(offer.Target);
                }
            }

            foreach (DrinkGroup group in new[] { DrinkGroup.Column, DrinkGroup.Dozen, DrinkGroup.Parity, DrinkGroup.Color, DrinkGroup.Range })
            {
                CollectionAssert.IsSubsetOf(group.Targets(), seen, $"{group}의 모든 대상이 나올 수 있다");
            }
        }

        [Test]
        public void PayoutOffer_TargetStaysFixed_UntilPicked()
        {
            PlayPassableRound(flow);
            flow.SubmitPayment();
            var before = flow.Rewards.CurrentOffer.Select(o => o.Target).ToList();

            CollectionAssert.AreEqual(before, flow.Rewards.CreateOffer().Select(o => o.Target).ToList());
        }

        [Test]
        public void ConfirmDrink_Twice_AppliesOnce()
        {
            PlayPassableRound(flow);
            flow.SubmitPayment();

            Assert.IsNotNull(flow.ConfirmDrink(0));
            Assert.IsNull(flow.ConfirmDrink(0));
            Assert.IsNull(flow.ConfirmDrink(1));
            Assert.AreEqual(1, flow.Drinks.TotalStacks);
            Assert.AreEqual(2, flow.Round.Round);
        }

        [Test]
        public void ConfirmDrink_WithBadIndex_ChangesNothing()
        {
            PlayPassableRound(flow);
            flow.SubmitPayment();

            Assert.IsNull(flow.ConfirmDrink(-1));
            Assert.IsNull(flow.ConfirmDrink(4));
            Assert.AreEqual(GamePhase.DrinkSelection, flow.Phase);
            Assert.AreEqual(0, flow.Drinks.TotalStacks);
            Assert.IsNotNull(flow.Rewards.CurrentOffer);
        }

        [Test]
        public void ConfirmDrink_OutsideDrinkSelection_IsRejected()
        {
            Assert.IsNull(flow.ConfirmDrink(0));
            Assert.AreEqual(GamePhase.Betting, flow.Phase);
            Assert.AreEqual(0, flow.Drinks.TotalStacks);
        }

        [Test]
        public void PickedDrink_BoostsNextRoundPayouts_ButNotSkull()
        {
            DrinkCatalog all = assets.Drinks(offerCount: 6);
            flow = NewFlow(42, all);
            PlayPassableRound(flow);
            flow.SubmitPayment();
            DrinkOffer drink = flow.ConfirmDrink(OfferIndexOf(DrinkGroup.Color));

            // 색상 음료는 빨강·검정 중 정해진 하나에만 붙는다. 그 색 숫자 두 개와 반대 색 숫자 하나로 시험한다.
            BetTarget color = drink.Target;
            BetTarget other = BetTarget.Outside(color.Kind == BetKind.Red ? BetKind.Black : BetKind.Red);
            int[] hits = Enumerable.Range(1, 36).Where(n => color.CoversNumber(n, definition.ColorOfNumber(n))).ToArray();
            int miss = Enumerable.Range(1, 36).First(n => other.CoversNumber(n, definition.ColorOfNumber(n)));

            SpinAndSettle(flow, color, 10, hits[0]);
            Assert.AreEqual(11 - 10 + 30, flow.Wallet.Balance, "정해진 색 10 × 2 × 1.5 = 30");

            SpinAndSettle(flow, color, 10, hits[1]);
            Assert.AreEqual(31 - 10 + 30, flow.Wallet.Balance);

            SpinAndSettle(flow, other, 10, miss);
            Assert.AreEqual(51 - 10 + 20, flow.Wallet.Balance, "반대 색은 음료가 붙지 않는다: 10 × 2 = 20");

            flow.ForcedNumbers = new[] { hits[0], Skull };
            flow.ExtraBallCount = 1;
            flow.PlaceBet(color, 10);
            flow.TryBeginSpin(out ActiveSpin spin);
            flow.CompleteSpin(spin.Id, out SettlementResult s);
            Assert.AreEqual(SpinOutcome.SkullBust, s.Outcome);
            Assert.AreEqual(0, s.TotalPayout, "음료가 있어도 해골이면 지급 0.");
        }

        [Test]
        public void PayingEverything_ThenNoMoneyNextRound_EndsAtNextPayment()
        {
            SpinAndSettle(flow, BetTarget.Straight(7), 1, 7); // 135
            SpinAndSettle(flow, BetTarget.Straight(7), 12, 8); // 123
            for (int i = 0; i < 3; i++)
            {
                SpinAndSettle(flow, BetTarget.Straight(7), 1, 8); // 120
            }

            Assert.AreEqual(PaymentOutcome.Paid, flow.SubmitPayment());
            Assert.AreEqual(0, flow.Wallet.Balance);
            flow.ConfirmDrink(0);

            Assert.AreEqual(2, flow.Round.Round);
            Assert.AreEqual(GamePhase.RoundPayment, flow.Phase, "새 라운드에 걸 돈이 없으면 바로 납부 판정.");
            Assert.AreEqual(5, flow.Round.ForfeitedSpins);
            Assert.AreEqual(PaymentOutcome.Insufficient, flow.SubmitPayment());
            Assert.AreEqual(GamePhase.GameOver, flow.Phase);
        }

        [Test]
        public void NewRun_StartsWithoutDrinks()
        {
            PlayPassableRound(flow);
            flow.SubmitPayment();
            flow.ConfirmDrink(0);
            Assert.AreEqual(1, flow.Drinks.TotalStacks);

            GameFlowController restarted = NewFlow(0, catalog);
            Assert.AreEqual(0, restarted.Drinks.TotalStacks);
            Assert.AreEqual(1, restarted.Round.Round);
            Assert.AreEqual(balance.InitialMoney, restarted.Wallet.Balance);
        }

        [Test]
        public void WithoutCatalog_PaymentGoesStraightToNextRound()
        {
            flow = NewFlow(42, null);
            PlayPassableRound(flow);

            Assert.AreEqual(PaymentOutcome.Paid, flow.SubmitPayment());
            Assert.AreEqual(GamePhase.Betting, flow.Phase);
            Assert.AreEqual(2, flow.Round.Round);
            Assert.AreEqual(1m, flow.BuildSpinConfig().MultiplierFor(Red) / 2m);
        }
    }
}
