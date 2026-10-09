using System.Collections.Generic;

namespace ProjectCR
{
    /// <summary>
    /// 이번 게임의 확률 효과(대상별 잔 수). GameFlowController가 가지고 있어서 새 게임이면 새로 만들어진다.
    ///
    /// 대상(빨강, 2번째 더즌, 7번, {1,2,4,5} …)마다 따로 관리하고, 같은 대상을 다시 마시면 잔 수만 는다.
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

        /// <summary>대상에 한 잔. 계수는 이번에 마신 음료 값으로 맞춘다(같은 대상이면 같은 음료라 보통 같다).</summary>
        public void AddStack(BetTarget target, decimal boostPerStack)
        {
            List<ProbabilityEffect> next = WithExtraStack(target, boostPerStack);
            effects.Clear();
            effects.AddRange(next);
        }

        /// <summary>대상에서 한 잔을 뺀다. 0잔이 되면 목록에서 지운다(집중 강화를 버릴 때).</summary>
        public void RemoveStack(BetTarget target)
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
                effects[index] = new ProbabilityEffect(target, effect.BoostPerStack, effect.Stacks - 1);
            }
        }

        /// <summary>
        /// 대상에 한 잔을 더했을 때의 효과 목록. <b>지금 상태는 바꾸지 않는다</b>(카드 미리보기용).
        /// </summary>
        public List<ProbabilityEffect> WithExtraStack(BetTarget target, decimal boostPerStack)
        {
            var result = new List<ProbabilityEffect>(effects);
            int index = IndexOf(target);
            if (index >= 0)
            {
                result[index] = new ProbabilityEffect(target, boostPerStack, effects[index].Stacks + 1);
            }
            else
            {
                result.Add(new ProbabilityEffect(target, boostPerStack, 1));
            }

            return result;
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
