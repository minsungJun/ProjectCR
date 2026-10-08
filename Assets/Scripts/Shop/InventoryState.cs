using System;
using System.Collections.Generic;

namespace ProjectCR
{
    /// <summary>
    /// 이번 게임의 소모품: 보유 수, 장착한 해골 방패, 다음 회전 보험, 이번 라운드 추가 공.
    /// 효과 값(보험 환급 비율)은 산 상품에서 가져와 둔다.
    /// </summary>
    public sealed class InventoryState
    {
        private readonly Dictionary<ShopEffect, int> counts = new Dictionary<ShopEffect, int>();

        public int CountOf(ShopEffect effect) => counts.TryGetValue(effect, out int count) ? count : 0;

        /// <summary>장착한 해골 방패 수(보유 수 이하). 해골이 나오면 여기서 하나씩 쓴다.</summary>
        public int ShieldsEquipped { get; private set; }

        /// <summary>다음 회전에 보험이 걸려 있다. 회전 시작 때 그 회전으로 넘어가고 꺼진다.</summary>
        public bool InsuranceArmed { get; private set; }

        /// <summary>보험 환급 비율(산 보험 증서의 값).</summary>
        public decimal InsuranceRatio { get; private set; } = 0.5m;

        /// <summary>이번 라운드 남은 회전에 더하는 공 수. 라운드가 바뀌면 0.</summary>
        public int RoundExtraBalls { get; private set; }

        internal void Add(ShopItemDefinition item)
        {
            counts[item.Effect] = CountOf(item.Effect) + 1;
            if (item.Effect == ShopEffect.Insurance)
            {
                InsuranceRatio = item.Value;
            }
        }

        internal bool TryTake(ShopEffect effect)
        {
            int count = CountOf(effect);
            if (count <= 0)
            {
                return false;
            }

            counts[effect] = count - 1;
            if (effect == ShopEffect.SkullShield)
            {
                ShieldsEquipped = Math.Min(ShieldsEquipped, count - 1);
            }

            return true;
        }

        internal bool EquipShield()
        {
            if (ShieldsEquipped >= CountOf(ShopEffect.SkullShield))
            {
                return false;
            }

            ShieldsEquipped++;
            return true;
        }

        internal bool UnequipShield()
        {
            if (ShieldsEquipped <= 0)
            {
                return false;
            }

            ShieldsEquipped--;
            return true;
        }

        /// <summary>장착한 방패를 n개 쓴다(보유와 장착 모두 줄어든다).</summary>
        internal void ConsumeEquippedShields(int used)
        {
            for (int i = 0; i < used && ShieldsEquipped > 0; i++)
            {
                ShieldsEquipped--;
                counts[ShopEffect.SkullShield] = CountOf(ShopEffect.SkullShield) - 1;
            }
        }

        internal void ArmInsurance() => InsuranceArmed = true;

        /// <summary>회전 시작: 보험이 걸려 있으면 이 회전에 쓰고 끈다.</summary>
        internal bool TakeInsuranceForSpin()
        {
            bool armed = InsuranceArmed;
            InsuranceArmed = false;
            return armed;
        }

        internal void AddRoundBall() => RoundExtraBalls++;

        /// <summary>라운드가 끝났다: 추가 공 효과 끝.</summary>
        internal void OnRoundEnded()
        {
            RoundExtraBalls = 0;
        }
    }
}
