using System;
using System.Collections.Generic;

namespace ProjectCR
{
    /// <summary>베팅 하나의 정산. 공 여러 개 중 몇 개가 이 베팅에 들어갔는지(Hits)와 지급액.</summary>
    public readonly struct BetPayout
    {
        public BetPayout(ConfirmedBet bet, int hits, long amount)
        {
            Bet = bet;
            Hits = hits;
            Amount = amount;
        }

        public ConfirmedBet Bet { get; }

        /// <summary>이 베팅에 들어간 공 수. 해골 회전이면 0.</summary>
        public int Hits { get; }

        /// <summary>원금 포함 지급액. 낙첨이면 0.</summary>
        public long Amount { get; }

        public bool IsWin => Amount > 0;
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

        /// <summary>이번 회전의 손익 = 지급액 − 건 금액.</summary>
        public long Net => TotalPayout - TotalStake;

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
    ///    지급 배율·당첨 보너스·낙첨 환급은 이 뒤 단계라 해골 회전에는 적용되지 않는다.
    ///    "해골에도 환급" 같은 효과는 나중에 SkullBust 전용 단계로 따로 붙인다.
    /// 2. 해골이 없으면 베팅마다, 공마다 판정해 지급액을 모두 더한다.
    /// </summary>
    public static class PayoutCalculator
    {
        public static SettlementResult Calculate(IReadOnlyList<ConfirmedBet> bets, SpinResult spin, SpinConfig config, long maxMoney)
        {
            return Calculate(bets, spin, config.PayoutFor, maxMoney);
        }

        /// <summary>베팅 하나짜리. payoutMultiplier는 이 베팅 종류의 원금 포함 배수.</summary>
        public static SettlementResult Calculate(ConfirmedBet bet, SpinResult spin, int payoutMultiplier, long maxMoney)
        {
            return Calculate(new[] { bet }, spin, _ => payoutMultiplier, maxMoney);
        }

        public static SettlementResult Calculate(IReadOnlyList<ConfirmedBet> bets, SpinResult spin, Func<BetTarget, int> payoutFor, long maxMoney)
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
                    results.Add(new BetPayout(bet, 0, 0));
                }

                return new SettlementResult(results, spin, totalStake, 0, SpinOutcome.SkullBust);
            }

            long total = 0;
            foreach (ConfirmedBet bet in bets)
            {
                int hits = 0;
                long amount = 0;
                long perHit = ScaledPayout(bet.Stake, payoutFor(bet.Target), maxMoney);

                foreach (BallResult ball in spin.Balls)
                {
                    if (Covers(bet, ball))
                    {
                        hits++;
                        amount = SaturatingAdd(amount, perHit, maxMoney);
                    }
                }

                results.Add(new BetPayout(bet, hits, amount));
                total = SaturatingAdd(total, amount, maxMoney);
            }

            return new SettlementResult(results, spin, totalStake, total, total > 0 ? SpinOutcome.Win : SpinOutcome.Lose);
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

        private static long SaturatingAdd(long a, long b, long max)
        {
            return a > max - b ? max : a + b;
        }
    }
}
