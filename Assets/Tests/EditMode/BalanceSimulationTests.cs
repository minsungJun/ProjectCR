using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;

namespace ProjectCR.Tests
{
    /// <summary>
    /// 밸런스 확인용 몬테카를로. 정해진 전략 몇 개로 게임을 끝까지 돌려 라운드별 통과율을 표로 낸다.
    /// 실제 GameFlowController(납부·음료·해골 포함)를 그대로 쓴다. 연출은 없다.
    ///
    /// 느려서 평소 테스트에서는 빠진다(Explicit). 수치를 바꾼 뒤 직접 돌린다.
    /// 결과는 Passed 메시지와 콘솔에 남는다.
    /// </summary>
    [Explicit("밸런스 시뮬레이션. 직접 실행할 때만 돈다.")]
    public class BalanceSimulationTests
    {
        private const int Runs = 2000;
        private const int MaxRounds = 12;

        private static readonly BetTarget Red = BetTarget.Outside(BetKind.Red);
        private static readonly BetTarget Dozen1 = BetTarget.Outside(BetKind.Dozen1);
        private static readonly BetTarget Seventeen = BetTarget.Straight(17);

        private TestAssets assets;
        private RouletteDefinition definition;
        private GameBalance balance;
        private DrinkCatalog catalog;
        private ShopCatalog shopCatalog;

        // 상점 전략 통계(마지막 변형 기준).
        private int shopVisits;
        private int supportBought;
        private long shopSpent;

        /// <summary>회전마다 무엇에 얼마를 걸지. 보상 때 어느 묶음을 원하는지.</summary>
        private sealed class Strategy
        {
            public string Name;
            public BetTarget Target;
            public Func<GameFlowController, long> Stake;
        }

        [SetUp]
        public void SetUp()
        {
            assets = new TestAssets();
            definition = assets.Definition();
            balance = assets.Balance();
            catalog = assets.Drinks(withProbability: true);
            shopCatalog = assets.Shop(openOnFirstRound: false); // 게임 규칙: 라운드를 통과한 뒤부터
        }

        [TearDown]
        public void TearDown() => assets.DestroyAll();

        [Test]
        public void PassRateByRound()
        {
            var strategies = new List<Strategy>
            {
                // 남은 회전으로 목표(납부금 × 1.5)까지 가는 데 필요한 만큼만 빨강에 건다.
                new Strategy { Name = "빨강 목표 베팅", Target = Red, Stake = f => TargetStake(f, Red, 1.5m) },
                // 매 회전 보유의 1/4을 빨강에.
                new Strategy { Name = "빨강 1/4", Target = Red, Stake = f => Math.Max(1, f.Wallet.Balance / 4) },
                // 매 회전 보유의 1/3을 1–12에.
                new Strategy { Name = "더즌 1/3", Target = Dozen1, Stake = f => Math.Max(1, f.Wallet.Balance / 3) },
                // 매 회전 보유의 1/10을 숫자 하나에.
                new Strategy { Name = "숫자 1/10", Target = Seventeen, Stake = f => Math.Max(1, f.Wallet.Balance / 10) },
            };

            var report = new StringBuilder();
            report.AppendLine($"[밸런스] {Runs}판, 음료 ×{catalog.BoostOf(DrinkGroup.Color)}, 납부금 {Dues()}");
            report.Append("전략".PadRight(12));
            for (int r = 1; r <= MaxRounds; r++)
            {
                report.Append($"R{r}".PadLeft(6));
            }

            report.AppendLine("   평균 도달");

            foreach (Strategy strategy in strategies)
            {
                foreach ((bool drinks, bool shop) in new[] { (false, false), (true, false), (true, true) })
                {
                    int[] passed = new int[MaxRounds + 1];
                    long reachedSum = 0;
                    shopVisits = supportBought = 0;
                    shopSpent = 0;
                    for (int run = 1; run <= Runs; run++)
                    {
                        int reached = PlayRun(strategy, run, drinks ? catalog : null, shop ? shopCatalog : null);
                        reachedSum += reached;
                        for (int r = 1; r < reached && r <= MaxRounds; r++)
                        {
                            passed[r]++;
                        }
                    }

                    report.Append($"{strategy.Name}{(drinks ? "+음료" : "")}{(shop ? "+상점" : "")}".PadRight(12));
                    for (int r = 1; r <= MaxRounds; r++)
                    {
                        report.Append($"{100.0 * passed[r] / Runs,5:F1}%");
                    }

                    report.Append($"   {(double)reachedSum / Runs:F2}");
                    if (shop && shopVisits > 0)
                    {
                        report.Append($"   지원금 구매 {100.0 * supportBought / shopVisits:F0}% · 방문당 지출 {(double)shopSpent / shopVisits:F0}");
                    }

                    report.AppendLine();
                }
            }

            report.AppendLine();
            report.Append(SafeBetReturnTable());
            report.AppendLine();
            report.Append(FocusReturnTable());

            UnityEngine.Debug.Log(report.ToString());
            Assert.Pass(report.ToString());
        }

        /// <summary>게임 하나를 끝까지(또는 MaxRounds 통과까지) 돌리고, 도달한 라운드(못 낸 라운드)를 돌려준다.</summary>
        private int PlayRun(Strategy strategy, int seed, DrinkCatalog drinks, ShopCatalog shop = null)
        {
            var flow = new GameFlowController(definition, balance, seed, drinks, shop);
            DrinkGroup wanted = DrinkGroups.Of(strategy.Target.Kind);
            int shoppedRound = 0;

            while (flow.Round.Round <= MaxRounds)
            {
                switch (flow.Phase)
                {
                    case GamePhase.Betting when shop != null && shoppedRound != flow.Round.Round:
                        shoppedRound = flow.Round.Round;
                        VisitShop(flow, strategy.Target);
                        break;
                    case GamePhase.Betting:
                        long stake = Math.Min(flow.Wallet.Balance, Math.Max(balance.MinBet, strategy.Stake(flow)));
                        flow.PlaceBet(strategy.Target, stake);
                        flow.TryBeginSpin(out ActiveSpin spin);
                        flow.CompleteSpin(spin.Id, out _);
                        break;
                    case GamePhase.RoundPayment:
                        flow.SubmitPayment();
                        break;
                    case GamePhase.DrinkSelection:
                        // 자기 전략 묶음의 지급 음료 > 자기 베팅 칸을 올리는 확률 음료 > 첫 후보.
                        IReadOnlyList<DrinkOffer> offer = flow.Rewards.CurrentOffer;
                        int pick = 0, best = 0;
                        for (int i = 0; i < offer.Count; i++)
                        {
                            int score = !offer[i].IsProbability && offer[i].Group == wanted ? 2
                                : offer[i].IsProbability && offer[i].Target == strategy.Target ? 1
                                : 0;
                            if (score > best)
                            {
                                best = score;
                                pick = i;
                            }
                        }

                        flow.ConfirmDrink(pick);
                        break;
                    default:
                        return flow.Round.Round;
                }
            }

            return MaxRounds + 1;
        }

        /// <summary>
        /// 라운드 첫 베팅 때 상점: 납부 지원금(지속 환급)과 자기 베팅 대상의 집중 강화를
        /// 보유의 25% 이하 가격이면 산다. 소모품·교차 강화는 사지 않는다(단순 전략).
        /// </summary>
        private void VisitShop(GameFlowController flow, BetTarget target)
        {
            if (!flow.EnterShop())
            {
                return;
            }

            shopVisits++;
            long before = flow.Wallet.Balance;
            for (int i = 0; i < flow.Shop.Offers.Count; i++)
            {
                ShopItemDefinition item = flow.Shop.Offers[i].Item;
                if (item.Effect == ShopEffect.PaymentSupport && flow.Shop.PriceOf(flow.Shop.Offers[i]) <= flow.Wallet.Balance / 4
                    && flow.Shop.Buy(i, BetTarget.None) == ShopError.None)
                {
                    supportBought++;
                }
                else if (item.Effect == ShopEffect.Focus && item.Group == DrinkGroups.Of(target.Kind)
                         && flow.Shop.PriceOf(flow.Shop.Offers[i]) <= flow.Wallet.Balance / 4)
                {
                    flow.Shop.Buy(i, target);
                }
            }

            shopSpent += Math.Max(0, before - flow.Wallet.Balance);
            flow.CloseShop();
        }

        /// <summary>빨강 기대 환수율: 상점 집중 강화(색상, 빨강 대상) s개 × 음료 조합.</summary>
        private string FocusReturnTable()
        {
            var text = new StringBuilder();
            text.AppendLine("[빨강 기대 환수율 + 상점 집중] 행 = 색상 집중 개수(+10%씩), 열 = 음료 없음 / 지급 1잔 / 지급 1잔 + 빨강 확률 3잔");
            for (int s = 0; s <= 5; s += s < 1 ? 1 : 2)
            {
                text.Append($"s={s}".PadRight(6));
                foreach ((int m, int k) in new[] { (0, 0), (1, 0), (1, 3) })
                {
                    var flow = new GameFlowController(definition, balance, 1, catalog);
                    flow.Drinks.SetStacks(DrinkGroup.Color, m);
                    flow.Probabilities.SetStacks(Red, 0.37m, k);
                    for (int i = 0; i < s; i++)
                    {
                        flow.Upgrades.AddFocus(DrinkGroup.Color, Red, 0.1m);
                    }

                    SpinConfig config = flow.BuildSpinConfig();
                    text.Append($"{config.ProbabilityOf(Red) * (double)config.MultiplierFor(Red) * 100,9:F1}%");
                }

                text.AppendLine();
            }

            return text.ToString();
        }

        /// <summary>
        /// 안전한 베팅(빨강)의 기대 환수율 = 등장 확률 × 2 × 지급 음료 배율. 지급 음료 m잔 × 확률 음료(빨강, 목록 계수) k잔.
        /// 100%를 넘으면 걸수록 이득이다.
        /// </summary>
        private string SafeBetReturnTable()
        {
            var text = new StringBuilder();
            decimal redBoost = 0m;
            foreach (ProbabilityDrinkDefinition drink in catalog.ProbabilityDrinks)
            {
                if (drink.Group == DrinkGroup.Color)
                {
                    redBoost = drink.BoostFor(Red);
                }
            }

            text.AppendLine($"[빨강 기대 환수율] 행 = 지급 음료 잔 수, 열 = 빨강 확률 음료(+{redBoost * 100m:0.#}%/잔) 잔 수");
            int[] chanceStacks = { 0, 1, 3, 5, 10, 20 };
            text.Append("".PadRight(6));
            foreach (int k in chanceStacks)
            {
                text.Append($"k={k}".PadLeft(9));
            }

            text.AppendLine();
            for (int m = 0; m <= 3; m++)
            {
                text.Append($"m={m}".PadRight(6));
                foreach (int k in chanceStacks)
                {
                    var flow = new GameFlowController(definition, balance, 1, catalog);
                    flow.Drinks.SetStacks(DrinkGroup.Color, m);
                    flow.Probabilities.SetStacks(Red, redBoost, k);
                    SpinConfig config = flow.BuildSpinConfig();
                    double rtp = config.ProbabilityOf(Red) * (double)config.MultiplierFor(Red);
                    text.Append($"{rtp * 100,8:F1}%");
                }

                text.AppendLine();
            }

            return text.ToString();
        }

        /// <summary>남은 회전 수로 목표 금액(납부금 × goal)까지 가려면 이번 회전에 얼마를 걸어야 하나. 목표를 넘었으면 최소 베팅.</summary>
        private static long TargetStake(GameFlowController flow, BetTarget target, decimal goal)
        {
            long want = (long)Math.Ceiling(flow.Round.CurrentDue * goal);
            long gap = want - flow.Wallet.Balance;
            if (gap <= 0)
            {
                return flow.GameBalance.MinBet;
            }

            decimal profitPerUnit = flow.BuildSpinConfig().MultiplierFor(target) - 1m;
            return (long)Math.Ceiling(gap / profitPerUnit);
        }

        private string Dues()
        {
            var dues = new List<string>();
            for (int r = 1; r <= 8; r++)
            {
                dues.Add(balance.GetDue(r).ToString("N0"));
            }

            return string.Join(" / ", dues);
        }
    }
}
