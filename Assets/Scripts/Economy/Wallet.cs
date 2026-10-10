using System;

namespace ProjectCR
{
    /// <summary>
    /// 보유 금액. 돈은 이 클래스를 통해서만 바뀐다.
    ///
    /// 자료형은 long이다. 납부금이 라운드마다 곱으로 커지므로 int(약 21억)는 금방 넘친다.
    /// 상한(<see cref="MaxMoney"/>)을 넘는 입금은 상한에서 멈춘다.
    /// </summary>
    public sealed class Wallet
    {
        public Wallet(long initialBalance, long maxMoney)
        {
            MaxMoney = Math.Max(1, maxMoney);
            Balance = Math.Min(Math.Max(0, initialBalance), MaxMoney);
        }

        public long Balance { get; private set; }
        public long MaxMoney { get; }

        public bool CanAfford(long amount) => amount >= 0 && amount <= Balance;

        /// <summary>잔액이 충분할 때만 차감한다. 음수는 거부한다.</summary>
        public bool TrySpend(long amount)
        {
            if (!CanAfford(amount))
            {
                return false;
            }

            Balance -= amount;
            return true;
        }

        /// <summary>입금. 상한을 넘으면 상한까지만 넣고, 실제로 들어간 금액을 돌려준다.</summary>
        public long Add(long amount)
        {
            if (amount <= 0)
            {
                return 0;
            }

            long room = MaxMoney - Balance;
            long added = Math.Min(room, amount);
            Balance += added;
            return added;
        }
    }
}
