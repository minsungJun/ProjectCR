using System;
using System.Collections.Generic;

namespace ProjectCR
{
    public readonly struct BallPayout
    {
        public BallPayout(BallResult ball, bool isWin, long amount)
        {
            Ball = ball;
            IsWin = isWin;
            Amount = amount;
        }

        public BallResult Ball { get; }
        public bool IsWin { get; }

        /// <summary>원금 포함 지급액. 낙첨이면 0.</summary>
        public long Amount { get; }
    }

    /// <summary>회전 하나의 전체 결과. 해골 낙첨은 일반 낙첨과 이유가 달라서 따로 둔다.</summary>
    public enum SpinOutcome
    {
        Win,
        Lose,

        /// <summary>공 하나라도 해골에 걸려 이 회전의 모든 베팅이 낙첨. 게임 진행은 일반 회전과 같다.</summary>
        SkullBust,
    }

    public sealed class SettlementResult
    {
        public SettlementResult(ConfirmedBet bet, SpinResult spin, IReadOnlyList<BallPayout> balls, long totalPayout, SpinOutcome outcome)
        {
            Bet = bet;
            Spin = spin;
            Balls = balls;
            TotalPayout = totalPayout;
            Outcome = outcome;
        }

        public ConfirmedBet Bet { get; }
        public SpinResult Spin { get; }
        public IReadOnlyList<BallPayout> Balls { get; }
        public SpinOutcome Outcome { get; }

        public bool IsSkullBust => Outcome == SpinOutcome.SkullBust;

        /// <summary>공별 지급액의 합(원금 포함).</summary>
        public long TotalPayout { get; }

        /// <summary>이번 회전의 손익. 베팅은 이미 차감되었으므로 지급액 − 베팅액.</summary>
        public long Net => TotalPayout - Bet.Stake;

        public int WinCount
        {
            get
            {
                int count = 0;
                foreach (BallPayout ball in Balls)
                {
                    if (ball.IsWin)
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
    /// 2. 해골이 없으면 공별로 판정해 합산한다.
    /// </summary>
    public static class PayoutCalculator
    {
        public static SettlementResult Calculate(ConfirmedBet bet, SpinResult spin, int payoutMultiplier, long maxMoney)
        {
            var balls = new List<BallPayout>(spin.Balls.Count);

            if (spin.HasSkull)
            {
                foreach (BallResult ball in spin.Balls)
                {
                    balls.Add(new BallPayout(ball, false, 0));
                }

                return new SettlementResult(bet, spin, balls, 0, SpinOutcome.SkullBust);
            }

            long total = 0;
            foreach (BallResult ball in spin.Balls)
            {
                bool isWin = Covers(bet, ball);
                long amount = isWin ? StraightPayout(bet.Stake, payoutMultiplier, maxMoney) : 0;
                balls.Add(new BallPayout(ball, isWin, amount));
                total = SaturatingAdd(total, amount, maxMoney);
            }

            return new SettlementResult(bet, spin, balls, total, total > 0 ? SpinOutcome.Win : SpinOutcome.Lose);
        }

        /// <summary>베팅이 이 공의 칸을 포함하는가. 해골 칸은 어떤 베팅에도 포함되지 않는다.</summary>
        public static bool Covers(ConfirmedBet bet, BallResult ball)
        {
            return !ball.IsSkull && ball.Number == bet.Number;
        }

        /// <summary>stake × multiplier. 상한을 넘거나 long이 넘치면 상한으로 자른다.</summary>
        public static long StraightPayout(long stake, int multiplier, long maxMoney)
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
