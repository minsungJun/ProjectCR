using System;
using System.Collections.Generic;

namespace ProjectCR
{
    /// <summary>베팅 하나의 정산. 공 여러 개 중 몇 개가 이 베팅에 들어갔는지(Hits)와 지급액.</summary>
    public readonly struct BetPayout
    {
        public BetPayout(ConfirmedBet bet, int hits, decimal exactAmount, long maxMoney)
        {
            Bet = bet;
            Hits = hits;
            ExactAmount = exactAmount;
            Amount = PayoutCalculator.ToMoney(exactAmount, maxMoney);
        }

        public ConfirmedBet Bet { get; }

        /// <summary>이 베팅에 들어간 공 수. 해골 회전이면 0.</summary>
        public int Hits { get; }

        /// <summary>버리기 전 지급액(음료 배율 때문에 소수일 수 있다). 회전 합계는 이 값을 더한 뒤 한 번 버린다.</summary>
        public decimal ExactAmount { get; }

        /// <summary>표시용 원금 포함 지급액(버림). 낙첨이면 0. 베팅별 값의 합은 회전 합계보다 작을 수 있다.</summary>
        public long Amount { get; }

        public bool IsWin => ExactAmount > 0m;
    }

    /// <summary>회전 하나의 전체 결과. 해골 낙첨은 일반 낙첨과 이유가 달라서 따로 둔다.</summary>
    public enum SpinOutcome
    {
        /// <summary>지급액이 하나라도 있다. 여러 베팅이면 손익은 마이너스일 수도 있다(<see cref="SettlementResult.Net"/>).</summary>
        Win,

        Lose,

        /// <summary>공 하나라도 해골에 걸려 이 회전의 모든 베팅이 낙첨. 게임 진행은 일반 회전과 같다.</summary>
        SkullBust,
    }

    public sealed class SettlementResult
    {
        public SettlementResult(IReadOnlyList<BetPayout> bets, SpinResult spin, long totalStake, long totalPayout, SpinOutcome outcome)
        {
            Bets = bets;
            Spin = spin;
            TotalStake = totalStake;
            TotalPayout = totalPayout;
            Outcome = outcome;
        }

        /// <summary>놓은 순서대로의 베팅별 정산.</summary>
        public IReadOnlyList<BetPayout> Bets { get; }

        public SpinResult Spin { get; }
        public SpinOutcome Outcome { get; }

        public bool IsSkullBust => Outcome == SpinOutcome.SkullBust;

        /// <summary>이번 회전에 건 금액 합계(회전 시작 때 이미 차감됨).</summary>
        public long TotalStake { get; }

        /// <summary>베팅별 지급액의 합(원금 포함).</summary>
        public long TotalPayout { get; }

        /// <summary>
        /// 해골 벌칙으로 보유 금액에서 빠진 돈(베팅금과 별도). 해골이 아니면 0.
        /// 지갑을 모르는 계산기가 아니라 GameFlowController.CompleteSpin이 정산 뒤에 채운다.
        /// </summary>
        public long SkullPenalty { get; private set; }

        /// <summary>이번 회전의 손익 = 지급액 − 건 금액 − 해골 벌칙.</summary>
        public long Net => TotalPayout - TotalStake - SkullPenalty;

        internal void ApplySkullPenalty(long amount) => SkullPenalty = Math.Max(0, amount);

        /// <summary>(베팅, 공) 적중 수의 합. 베팅 하나면 맞힌 공 수와 같다.</summary>
        public int WinCount
        {
            get
            {
                int count = 0;
                foreach (BetPayout bet in Bets)
                {
                    count += bet.Hits;
                }

                return count;
            }
        }

        public int WinningBetCount
        {
            get
            {
                int count = 0;
                foreach (BetPayout bet in Bets)
                {
                    if (bet.IsWin)
                    {
                        count++;
                    }
                }

                return count;
            }
        }
    }

    /// <summary>
    /// 모든 공의 결과가 나온 뒤 회전 전체를 한 번에 평가하고 지급액을 계산한다. 공의 3D 위치는 보지 않는다.
    /// 베팅 차감은 회전 시작 때 한 번이라 여기서 다루지 않는다.
    ///
    /// 평가 순서:
    /// 1. <b>해골 판정이 맨 먼저다.</b> 공 하나라도 해골이면 모든 베팅 낙첨, 지급 0. 베팅금은 돌려주지 않는다.
    ///    보유 금액을 절반으로 줄이는 벌칙은 지갑을 가진 GameFlowController.CompleteSpin이 이 결과를 보고 건다.
    ///    지급 배율·당첨 보너스·낙첨 환급은 이 뒤 단계라 해골 회전에는 적용되지 않는다.
    ///    "해골에도 환급" 같은 효과는 나중에 SkullBust 전용 단계로 따로 붙인다.
    /// 2. 해골이 없으면 베팅마다, 공마다 판정해 지급액을 모두 더한다.
    ///    지급액 = 베팅금 × 최종 배수(기본 배수 × 음료 묶음 배율, <see cref="SpinConfig.MultiplierFor"/>).
    ///    중간에는 버리지 않고 decimal로 정확히 더한 뒤, <b>회전 합계에서 한 번만</b> 버림한다.
    ///    돈 상한을 넘으면 상한으로 자른다(합이 상한 이상이면 결과는 어차피 상한이라 중간값도 상한에서 멈춘다).
    /// </summary>
    public static class PayoutCalculator
    {
        public static SettlementResult Calculate(IReadOnlyList<ConfirmedBet> bets, SpinResult spin, SpinConfig config, long maxMoney)
        {
            return Calculate(bets, spin, config.MultiplierFor, maxMoney);
        }

        /// <summary>베팅 하나짜리. payoutMultiplier는 이 베팅의 원금 포함 최종 배수.</summary>
        public static SettlementResult Calculate(ConfirmedBet bet, SpinResult spin, decimal payoutMultiplier, long maxMoney)
        {
            return Calculate(new[] { bet }, spin, _ => payoutMultiplier, maxMoney);
        }

        public static SettlementResult Calculate(IReadOnlyList<ConfirmedBet> bets, SpinResult spin, Func<BetTarget, decimal> multiplierFor, long maxMoney)
        {
            var results = new List<BetPayout>(bets.Count);
            long totalStake = 0;
            foreach (ConfirmedBet bet in bets)
            {
                totalStake += bet.Stake;
            }

            if (spin.HasSkull)
            {
                foreach (ConfirmedBet bet in bets)
                {
                    results.Add(new BetPayout(bet, 0, 0m, maxMoney));
                }

                return new SettlementResult(results, spin, totalStake, 0, SpinOutcome.SkullBust);
            }

            decimal total = 0m;
            foreach (ConfirmedBet bet in bets)
            {
                int hits = 0;
                decimal amount = 0m;
                decimal perHit = ExactPayout(bet.Stake, multiplierFor(bet.Target), maxMoney);

                foreach (BallResult ball in spin.Balls)
                {
                    if (Covers(bet, ball))
                    {
                        hits++;
                        amount = CappedAdd(amount, perHit, maxMoney);
                    }
                }

                results.Add(new BetPayout(bet, hits, amount, maxMoney));
                total = CappedAdd(total, amount, maxMoney);
            }

            long totalPayout = ToMoney(total, maxMoney);
            return new SettlementResult(results, spin, totalStake, totalPayout, total > 0m ? SpinOutcome.Win : SpinOutcome.Lose);
        }

        /// <summary>stake × multiplier를 버림 없이. 상한을 넘거나 decimal이 넘치면 상한.</summary>
        public static decimal ExactPayout(long stake, decimal multiplier, long maxMoney)
        {
            if (stake <= 0 || multiplier <= 0m)
            {
                return 0m;
            }

            try
            {
                return Math.Min(stake * multiplier, maxMoney);
            }
            catch (OverflowException)
            {
                return maxMoney;
            }
        }

        /// <summary>정확한 금액을 돈으로: 버림하고 상한에서 자른다.</summary>
        public static long ToMoney(decimal exact, long maxMoney)
        {
            if (exact <= 0m)
            {
                return 0;
            }

            return exact >= maxMoney ? maxMoney : (long)decimal.Floor(exact);
        }

        /// <summary>베팅이 이 공의 칸을 포함하는가. 해골 칸은 어떤 베팅에도 포함되지 않는다(<see cref="BetTarget.Covers"/>).</summary>
        public static bool Covers(ConfirmedBet bet, BallResult ball) => bet.Target.Covers(ball);

        /// <summary>stake × multiplier. 상한을 넘거나 long이 넘치면 상한으로 자른다.</summary>
        public static long ScaledPayout(long stake, int multiplier, long maxMoney)
        {
            if (stake <= 0 || multiplier <= 0)
            {
                return 0;
            }

            try
            {
                return Math.Min(checked(stake * multiplier), maxMoney);
            }
            catch (OverflowException)
            {
                return maxMoney;
            }
        }

        /// <summary>두 값 모두 0~max라서 decimal 덧셈은 넘치지 않는다.</summary>
        private static decimal CappedAdd(decimal a, decimal b, long max)
        {
            return Math.Min(a + b, max);
        }
    }
}
