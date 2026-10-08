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

    public sealed class SettlementResult
    {
        public SettlementResult(ConfirmedBet bet, SpinResult spin, IReadOnlyList<BallPayout> balls, long totalPayout)
        {
            Bet = bet;
            Spin = spin;
            Balls = balls;
            TotalPayout = totalPayout;
        }

        public ConfirmedBet Bet { get; }
        public SpinResult Spin { get; }
        public IReadOnlyList<BallPayout> Balls { get; }

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
    /// 확정된 결과 데이터로 당첨을 판정하고 지급액을 계산한다. 공의 3D 위치는 보지 않는다.
    /// 공 여러 개면 공별 지급액을 합산한다. 베팅 차감은 회전당 한 번이라 여기서 다루지 않는다.
    /// </summary>
    public static class PayoutCalculator
    {
        public static SettlementResult Calculate(ConfirmedBet bet, SpinResult spin, int payoutMultiplier, long maxMoney)
        {
            var balls = new List<BallPayout>(spin.Balls.Count);
            long total = 0;

            foreach (BallResult ball in spin.Balls)
            {
                bool isWin = ball.Number == bet.Number;
                long amount = isWin ? StraightPayout(bet.Stake, payoutMultiplier, maxMoney) : 0;
                balls.Add(new BallPayout(ball, isWin, amount));
                total = SaturatingAdd(total, amount, maxMoney);
            }

            return new SettlementResult(bet, spin, balls, total);
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
