using System.Collections.Generic;

namespace ProjectCR
{
    /// <summary>
    /// 이번 게임의 소모품. <b>모두 자동으로 쓰인다</b>(사용 버튼 없음).
    /// - 추가 공·여분의 회전권: 사는 순간 적용(상점은 라운드 첫 회전 전에만 열리므로 그 라운드 전체에 걸린다).
    /// - 해골 방패: 보관해 두면 해골이 나왔을 때 1개씩 자동으로 쓴다.
    /// - 보험 증서: 보관해 두면 다음 회전부터 회전마다 1장씩 자동으로 쓴다.
    /// 효과 값(보험 환급 비율)은 산 상품에서 가져와 둔다.
    /// </summary>
    public sealed class InventoryState
    {
        private readonly Dictionary<ShopEffect, int> counts = new Dictionary<ShopEffect, int>();

        /// <summary>보관 중인 수(해골 방패, 보험 증서).</summary>
        public int CountOf(ShopEffect effect) => counts.TryGetValue(effect, out int count) ? count : 0;

        public int Shields => CountOf(ShopEffect.SkullShield);
        public int Insurances => CountOf(ShopEffect.Insurance);

        /// <summary>보험 환급 비율(산 보험 증서의 값).</summary>
        public decimal InsuranceRatio { get; private set; } = 0.5m;

        /// <summary>이번 라운드 남은 회전에 더하는 공 수. 라운드가 바뀌면 0.</summary>
        public int RoundExtraBalls { get; private set; }

        /// <summary>보관하는 소모품(해골 방패, 보험 증서)을 하나 더한다.</summary>
        internal void Add(ShopItemDefinition item)
        {
            counts[item.Effect] = CountOf(item.Effect) + 1;
            if (item.Effect == ShopEffect.Insurance)
            {
                InsuranceRatio = item.Value;
            }
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

        internal void AddRoundBall() => RoundExtraBalls++;

        /// <summary>라운드가 끝났다: 추가 공 효과 끝.</summary>
        internal void OnRoundEnded()
        {
            RoundExtraBalls = 0;
        }
    }
}
