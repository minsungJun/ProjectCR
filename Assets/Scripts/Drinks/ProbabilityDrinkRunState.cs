using System.Collections.Generic;

namespace ProjectCR
{
    /// <summary>
    /// 이번 게임의 확률 효과(대상별 잔 수). GameFlowController가 가지고 있어서 새 게임이면 새로 만들어진다.
    ///
    /// 대상(빨강, 2번째 더즌, 7번, {1,2,4,5} …)마다 따로 관리하고, 같은 대상을 다시 받으면 횟수와 증가량이 더해진다.
    /// 한 잔을 여러 대상에 나눠 마시면(<see cref="AddSplit"/>) 대상마다 나눈 증가량이 붙는다.
    /// 최종 가중치는 저장하지 않는다. 매 회전 <see cref="PocketWeightCalculator.Compute"/>가 잔 수에서 다시 계산한다.
    /// 잔 수 상한은 두지 않는다(설계 문서 "밸런스"에서 범위를 본다).
    /// </summary>
    public sealed class ProbabilityDrinkRunState
    {
        // 처음 얻은 순서를 지킨다(표시용). 대상이 같으면 같은 칸.
        private readonly List<ProbabilityEffect> effects = new List<ProbabilityEffect>();

        public IReadOnlyList<ProbabilityEffect> Effects => effects;

        public int TotalStacks
        {
            get
            {
                int total = 0;
                foreach (ProbabilityEffect effect in effects)
                {
                    total += effect.Stacks;
                }

                return total;
            }
        }

        public int StacksOf(BetTarget target)
        {
            int index = IndexOf(target);
            return index >= 0 ? effects[index].Stacks : 0;
        }

        /// <summary>대상에 한 번(증가량 boost).</summary>
        public void AddStack(BetTarget target, decimal boost)
        {
            List<ProbabilityEffect> next = WithExtraStack(target, boost);
            effects.Clear();
            effects.AddRange(next);
        }

        /// <summary>한 잔(증가량 total)을 고른 대상들에 똑같이 나눠 붙인다. 대상이 없으면 아무것도 하지 않는다.</summary>
        public void AddSplit(IReadOnlyList<BetTarget> targets, decimal total)
        {
            List<ProbabilityEffect> next = WithSplit(targets, total);
            effects.Clear();
            effects.AddRange(next);
        }

        /// <summary>대상에서 한 번(증가량 boost)을 뺀다. 0번이 되면 목록에서 지운다(집중 강화를 버릴 때).</summary>
        public void RemoveStack(BetTarget target, decimal boost)
        {
            int index = IndexOf(target);
            if (index < 0)
            {
                return;
            }

            ProbabilityEffect effect = effects[index];
            if (effect.Stacks <= 1)
            {
                effects.RemoveAt(index);
            }
            else
            {
                effects[index] = ProbabilityEffect.WithTotal(target, effect.Stacks - 1, System.Math.Max(0m, effect.Total - boost));
            }
        }

        /// <summary>
        /// 대상에 한 번 더 받았을 때의 효과 목록. <b>지금 상태는 바꾸지 않는다</b>(카드 미리보기용).
        /// </summary>
        public List<ProbabilityEffect> WithExtraStack(BetTarget target, decimal boost)
        {
            var result = new List<ProbabilityEffect>(effects);
            Add(result, target, boost);
            return result;
        }

        /// <summary>
        /// 한 잔을 고른 대상들에 나눠 받았을 때의 효과 목록(대상마다 total / 대상 수). <b>지금 상태는 바꾸지 않는다</b>(고르기 미리보기용).
        /// </summary>
        public List<ProbabilityEffect> WithSplit(IReadOnlyList<BetTarget> targets, decimal total)
        {
            var result = new List<ProbabilityEffect>(effects);
            if (targets == null || targets.Count == 0)
            {
                return result;
            }

            decimal each = total / targets.Count;
            foreach (BetTarget target in targets)
            {
                Add(result, target, each);
            }

            return result;
        }

        private static void Add(List<ProbabilityEffect> list, BetTarget target, decimal boost)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Target == target)
                {
                    list[i] = ProbabilityEffect.WithTotal(target, list[i].Stacks + 1, list[i].Total + boost);
                    return;
                }
            }

            list.Add(ProbabilityEffect.WithTotal(target, 1, boost));
        }

        /// <summary>디버그·테스트: 잔 수를 직접 정한다.</summary>
        public void SetStacks(BetTarget target, decimal boostPerStack, int stacks)
        {
            int index = IndexOf(target);
            var effect = new ProbabilityEffect(target, boostPerStack, stacks < 0 ? 0 : stacks);
            if (index >= 0)
            {
                effects[index] = effect;
            }
            else
            {
                effects.Add(effect);
            }
        }

        private int IndexOf(BetTarget target)
        {
            for (int i = 0; i < effects.Count; i++)
            {
                if (effects[i].Target == target)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
