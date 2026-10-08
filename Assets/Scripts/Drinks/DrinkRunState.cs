using System;

namespace ProjectCR
{
    /// <summary>
    /// 이번 게임에서 마신 음료(묶음별 잔 수). GameFlowController가 가지고 있어서 새 게임이면 새로 만들어진다.
    ///
    /// 배율은 저장하지 않고 매번 boost^잔수로 다시 계산한다. 이전 결과에 또 곱하는 일이 없다.
    /// 잔 수에는 상한이 없다. 배율은 <see cref="MultiplierCap"/>에서 멈춘다 —
    /// 그 배율이면 최소 베팅 1로도 지급이 돈 상한에 걸리니 더 커져도 결과가 같고, decimal도 넘치지 않는다.
    /// </summary>
    public sealed class DrinkRunState
    {
        /// <summary>묶음 배율 상한. 돈 상한(약 10^12)보다 충분히 크다.</summary>
        public const decimal MultiplierCap = 1_000_000_000_000_000m;

        private readonly DrinkCatalog catalog;
        private readonly int[] stacks = new int[DrinkGroups.Count];

        /// <summary>catalog가 없으면 모든 묶음이 ×1이다.</summary>
        public DrinkRunState(DrinkCatalog catalog)
        {
            this.catalog = catalog;
        }

        public int StacksOf(DrinkGroup group) => stacks[(int)group];

        public int TotalStacks
        {
            get
            {
                int total = 0;
                foreach (int count in stacks)
                {
                    total += count;
                }

                return total;
            }
        }

        public void AddStack(DrinkGroup group)
        {
            if (stacks[(int)group] < int.MaxValue)
            {
                stacks[(int)group]++;
            }
        }

        /// <summary>디버그: 잔 수를 직접 정한다(밸런스 시험).</summary>
        public void SetStacks(DrinkGroup group, int count) => stacks[(int)group] = Math.Max(0, count);

        /// <summary>지금 잔 수 기준 묶음 배율.</summary>
        public decimal MultiplierOf(DrinkGroup group) => MultiplierAt(group, StacksOf(group));

        /// <summary>잔 수가 count일 때의 묶음 배율. 카드의 "선택 후" 미리보기도 이걸 쓴다.</summary>
        public decimal MultiplierAt(DrinkGroup group, int count)
        {
            decimal boost = catalog != null ? catalog.BoostOf(group) : 1m;
            return Power(boost, count);
        }

        /// <summary>boost^count. <see cref="MultiplierCap"/>에서 멈춘다.</summary>
        public static decimal Power(decimal boost, int count)
        {
            if (boost <= 1m || count <= 0)
            {
                return 1m;
            }

            decimal result = 1m;
            for (int i = 0; i < count; i++)
            {
                result *= boost;
                if (result >= MultiplierCap)
                {
                    return MultiplierCap;
                }
            }

            return result;
        }
    }
}
