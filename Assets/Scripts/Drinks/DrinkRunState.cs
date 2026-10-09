using System.Collections.Generic;

namespace ProjectCR
{
    /// <summary>
    /// 이번 게임에서 마신 지급 음료(대상별 잔 수). GameFlowController가 가지고 있어서 새 게임이면 새로 만들어진다.
    ///
    /// 지급 음료는 후보에 정해진 <b>대상 하나</b>(1열, 빨강, 홀수 …)의 배율만 올린다. 같은 묶음의 다른 대상은 그대로다.
    /// 대상은 베팅과 정확히 같아야 한다(빨강 음료는 빨강 베팅에만).
    /// 숫자 음료만 대상이 없고 <b>숫자 묶음 전체</b>(0을 포함한 숫자 하나·스플릿·스트리트·코너·식스 라인)의 배율을 올린다(<see cref="AddGroupStack"/>).
    ///
    /// 배율은 저장하지 않고 매번 boost^잔수로 다시 계산한다. 이전 결과에 또 곱하는 일이 없다.
    /// 잔 수에는 상한이 없다. 배율은 <see cref="MultiplierCap"/>에서 멈춘다 —
    /// 그 배율이면 최소 베팅 1로도 지급이 돈 상한에 걸리니 더 커져도 결과가 같고, decimal도 넘치지 않는다.
    /// </summary>
    public sealed class DrinkRunState
    {
        /// <summary>배율 상한. 돈 상한(약 10^12)보다 충분히 크다.</summary>
        public const decimal MultiplierCap = 1_000_000_000_000_000m;

        private readonly DrinkCatalog catalog;

        // 처음 마신 순서를 지킨다(표시용).
        private readonly List<BetTarget> targets = new List<BetTarget>();
        private readonly Dictionary<BetTarget, int> stacks = new Dictionary<BetTarget, int>();
        private readonly int[] groupStacks = new int[DrinkGroups.Count];

        /// <summary>catalog가 없으면 모든 대상이 ×1이다.</summary>
        public DrinkRunState(DrinkCatalog catalog)
        {
            this.catalog = catalog;
        }

        /// <summary>한 잔 이상 마신 대상(처음 마신 순서).</summary>
        public IReadOnlyList<BetTarget> Targets => targets;

        public int StacksOf(BetTarget target) => stacks.TryGetValue(target, out int count) ? count : 0;

        public int TotalStacks
        {
            get
            {
                int total = 0;
                foreach (int count in stacks.Values)
                {
                    total += count;
                }

                foreach (int count in groupStacks)
                {
                    total += count;
                }

                return total;
            }
        }

        public void AddStack(BetTarget target)
        {
            int count = StacksOf(target);
            if (count < int.MaxValue)
            {
                SetStacks(target, count + 1);
            }
        }

        /// <summary>디버그·테스트: 잔 수를 직접 정한다(밸런스 시험). 0이면 지운다.</summary>
        public void SetStacks(BetTarget target, int count)
        {
            if (target.IsNone)
            {
                return;
            }

            if (count <= 0)
            {
                stacks.Remove(target);
                targets.Remove(target);
                return;
            }

            if (!stacks.ContainsKey(target))
            {
                targets.Add(target);
            }

            stacks[target] = count;
        }

        /// <summary>묶음 전체 음료(숫자)의 잔 수.</summary>
        public int GroupStacksOf(DrinkGroup group) => groupStacks[(int)group];

        /// <summary>묶음 전체에 한 잔(숫자 음료).</summary>
        public void AddGroupStack(DrinkGroup group)
        {
            if (groupStacks[(int)group] < int.MaxValue)
            {
                groupStacks[(int)group]++;
            }
        }

        /// <summary>디버그·테스트: 묶음 전체 잔 수를 직접 정한다.</summary>
        public void SetGroupStacks(DrinkGroup group, int count) => groupStacks[(int)group] = count < 0 ? 0 : count;

        /// <summary>지금 잔 수 기준 묶음 전체 배율.</summary>
        public decimal GroupMultiplierOf(DrinkGroup group) => GroupMultiplierAt(group, GroupStacksOf(group));

        /// <summary>묶음 전체 잔 수가 count일 때의 배율.</summary>
        public decimal GroupMultiplierAt(DrinkGroup group, int count) => Power(catalog != null ? catalog.BoostOf(group) : 1m, count);

        /// <summary>지금 잔 수 기준 대상 배율.</summary>
        public decimal MultiplierOf(BetTarget target) => MultiplierAt(target, StacksOf(target));

        /// <summary>잔 수가 count일 때의 대상 배율. 카드의 "선택 후" 미리보기도 이걸 쓴다.</summary>
        public decimal MultiplierAt(BetTarget target, int count)
        {
            if (target.IsNone)
            {
                return 1m;
            }

            decimal boost = catalog != null ? catalog.BoostOf(DrinkGroups.Of(target.Kind)) : 1m;
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
