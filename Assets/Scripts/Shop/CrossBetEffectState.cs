using System.Collections.Generic;

namespace ProjectCR
{
    /// <summary>교차 강화 발동 한 건(결과 문구용).</summary>
    public readonly struct CrossTrigger
    {
        public CrossTrigger(DrinkGroup trigger, DrinkGroup target, decimal increase)
        {
            Trigger = trigger;
            Target = target;
            Increase = increase;
        }

        public DrinkGroup Trigger { get; }
        public DrinkGroup Target { get; }
        public decimal Increase { get; }
    }

    /// <summary>
    /// 상점 교차 강화. 상품(발동 묶음 → 대상 묶음)마다 보유 강화량을 더해 둔다(같은 상품을 또 사면 강화량이 쌓인다).
    ///
    /// 발동: 정산에서 <b>발동 묶음에 건 베팅이 하나라도 당첨</b>되면 상품마다 회전당 한 번, 다음 회전 대상 묶음 지급 +강화량.
    /// 결과 번호가 그 묶음에 속하는 것만으로는 발동하지 않는다. 막지 못한 해골 낙첨이면 발동하지 않는다.
    ///
    /// 적용: 다음 실제 회전 시작 때 그 회전 설정에 들어가고(<see cref="TakePendingForSpin"/>) 바로 비워진다.
    /// 그래서 발동한 회전 자체에는 적용되지 않고, 대상에 베팅하지 않아도 그 회전이 끝나면 사라진다.
    /// 라운드가 바뀌거나 상점·음료 선택을 해도 비우지 않는다(라운드 마지막 회전에 생기면 다음 라운드 첫 회전까지 간다).
    /// </summary>
    public sealed class CrossBetEffectState
    {
        private readonly decimal[,] owned = new decimal[DrinkGroups.Count, DrinkGroups.Count];
        private readonly int[,] counts = new int[DrinkGroups.Count, DrinkGroups.Count];
        private readonly decimal[] pending = new decimal[DrinkGroups.Count];

        /// <summary>그 상품(발동 → 대상)을 산 수.</summary>
        public int CountOf(DrinkGroup trigger, DrinkGroup target) => counts[(int)trigger, (int)target];

        /// <summary>그 상품의 보유 강화량 합.</summary>
        public decimal IncreaseOf(DrinkGroup trigger, DrinkGroup target) => owned[(int)trigger, (int)target];

        /// <summary>다음 회전에 걸린 대상 묶음 지급 증가.</summary>
        public decimal Pending(DrinkGroup target) => pending[(int)target];

        public bool HasPending
        {
            get
            {
                foreach (decimal value in pending)
                {
                    if (value > 0m)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public int TotalOwned
        {
            get
            {
                int total = 0;
                foreach (int count in counts)
                {
                    total += count;
                }

                return total;
            }
        }

        internal void Add(DrinkGroup trigger, DrinkGroup target, decimal value)
        {
            owned[(int)trigger, (int)target] += value;
            counts[(int)trigger, (int)target]++;
        }

        /// <summary>회전 시작: 다음 회전 강화를 이 회전에 넘기고 비운다. 돌려준 값이 이 회전 설정에 들어간다.</summary>
        internal decimal[] TakePendingForSpin()
        {
            var taken = (decimal[])pending.Clone();
            for (int i = 0; i < pending.Length; i++)
            {
                pending[i] = 0m;
            }

            return taken;
        }

        /// <summary>정산 뒤: 당첨된 베팅의 묶음으로 발동을 판정해 다음 회전 강화에 더한다. 발동 목록을 돌려준다.</summary>
        internal List<CrossTrigger> OnSettled(SettlementResult settlement)
        {
            var triggers = new List<CrossTrigger>();
            if (settlement.IsSkullBust)
            {
                return triggers;
            }

            var won = new bool[DrinkGroups.Count];
            foreach (BetPayout bet in settlement.Bets)
            {
                if (bet.IsWin)
                {
                    won[(int)DrinkGroups.Of(bet.Bet.Target.Kind)] = true;
                }
            }

            for (int trigger = 0; trigger < DrinkGroups.Count; trigger++)
            {
                if (!won[trigger])
                {
                    continue;
                }

                for (int target = 0; target < DrinkGroups.Count; target++)
                {
                    decimal increase = owned[trigger, target];
                    if (increase > 0m)
                    {
                        pending[target] += increase;
                        triggers.Add(new CrossTrigger((DrinkGroup)trigger, (DrinkGroup)target, increase));
                    }
                }
            }

            return triggers;
        }
    }
}
