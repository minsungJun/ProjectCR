using System.Collections.Generic;

namespace ProjectCR
{
    /// <summary>
    /// 이번 게임의 보관 아이템. <b>모두 자동으로 쓰인다</b>(사용 버튼 없음).
    /// - 회전권: 사는 순간 적용(여기 보관하지 않는다).
    /// - 해골 방어(특수 강화): 1회분. 해골이 나오면 자동으로 쓰고 다시 생기지 않는다(한 번 사면 다시 진열되지 않음).
    /// - 보험 증서: 다음 회전부터 회전마다 1장씩.
    /// - 딜러의 귀띔: 상점을 나갈 때 1장 써서 다음 회전 결과를 미리 뽑고 단서를 준다(GameFlowController).
    /// - 더블 업 쿠폰: 이번 라운드 마지막 회전에 1장 써서 지급 ×값. 라운드가 끝나면 사라진다.
    /// 효과 값(보험 환급 비율, 더블 업 배율)은 산 상품에서 가져와 둔다.
    /// </summary>
    public sealed class InventoryState
    {
        private readonly Dictionary<ShopEffect, int> counts = new Dictionary<ShopEffect, int>();

        /// <summary>보관 중인 수(해골 방패, 보험 증서).</summary>
        public int CountOf(ShopEffect effect) => counts.TryGetValue(effect, out int count) ? count : 0;

        public int Shields => CountOf(ShopEffect.SkullShield);
        public int Insurances => CountOf(ShopEffect.Insurance);
        public int DealerHints => CountOf(ShopEffect.DealerHint);
        public int DoubleUps => CountOf(ShopEffect.DoubleUp);

        /// <summary>더블 업 배율(산 쿠폰의 값).</summary>
        public decimal DoubleUpMultiplier { get; private set; } = 1.2m;

        /// <summary>보험 환급 비율(산 보험 증서의 값).</summary>
        public decimal InsuranceRatio { get; private set; } = 0.5m;

        /// <summary>보관하는 아이템(해골 방어, 보험 증서, 딜러의 귀띔, 더블 업 쿠폰)을 하나 더한다.</summary>
        internal void Add(ShopItemDefinition item)
        {
            counts[item.Effect] = CountOf(item.Effect) + 1;
            if (item.Effect == ShopEffect.Insurance)
            {
                InsuranceRatio = item.Value;
            }
            else if (item.Effect == ShopEffect.DoubleUp)
            {
                DoubleUpMultiplier = item.Value;
            }
        }

        internal void Use(ShopEffect effect)
        {
            counts[effect] = System.Math.Max(0, CountOf(effect) - 1);
        }

        /// <summary>해골을 막느라 쓴 방패 n개를 뺀다.</summary>
        internal void ConsumeShields(int used)
        {
            counts[ShopEffect.SkullShield] = System.Math.Max(0, Shields - used);
        }

        /// <summary>회전 시작: 보험 증서가 있으면 1장 써서 이 회전에 보험을 건다.</summary>
        internal bool TakeInsuranceForSpin()
        {
            if (Insurances <= 0)
            {
                return false;
            }

            counts[ShopEffect.Insurance] = Insurances - 1;
            return true;
        }

        /// <summary>라운드가 끝났다: 쓰지 못한 더블 업 쿠폰·딜러의 귀띔은 사라진다(이번 라운드용).</summary>
        internal void OnRoundEnded()
        {
            counts[ShopEffect.DoubleUp] = 0;
            counts[ShopEffect.DealerHint] = 0;
        }
    }
}
