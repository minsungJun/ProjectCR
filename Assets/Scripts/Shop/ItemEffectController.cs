using System;

namespace ProjectCR
{
    public enum ItemError
    {
        None,
        NotInShop,
        NoItem,
        MaxBalls,
        AlreadyArmed,
    }

    public static class ItemErrorMessages
    {
        public static string ToKorean(this ItemError error)
        {
            switch (error)
            {
                case ItemError.NotInShop:
                    return "지금은 아이템을 쓸 수 없습니다.";
                case ItemError.NoItem:
                    return "보유한 아이템이 없습니다.";
                case ItemError.MaxBalls:
                    return "공 수가 이미 최대입니다.";
                case ItemError.AlreadyArmed:
                    return "다음 회전에 이미 보험이 걸려 있습니다.";
                default:
                    return string.Empty;
            }
        }
    }

    /// <summary>
    /// 소모품 사용. 보유 아이템 탭(상점 안, Shopping 상태)에서만 쓴다. 라운드 진행 중이 보장된다
    /// (납부·라운드 종료가 확정되면 Shopping으로 올 수 없다).
    /// 회전 설정은 회전 시작 때 확정되므로, 여기서 바꾼 값은 다음 회전부터 적용된다.
    /// </summary>
    public sealed class ItemEffectController
    {
        private readonly GameFlowController flow;

        public ItemEffectController(GameFlowController flow)
        {
            this.flow = flow;
        }

        private InventoryState Inventory => flow.Inventory;

        /// <summary>지금 공 수(기본 + 디버그 + 이번 라운드 추가).</summary>
        public int CurrentBallCount => flow.BuildSpinConfig().BallCount;

        /// <summary>추가 공: 이번 라운드 남은 회전에 공 +1. 최대 공 수를 넘지 않는다.</summary>
        public ItemError UseExtraBall()
        {
            ItemError error = Ready(ShopEffect.ExtraBall);
            if (error != ItemError.None)
            {
                return error;
            }

            if (CurrentBallCount >= flow.GameBalance.MaxBallCount)
            {
                return ItemError.MaxBalls;
            }

            Inventory.TryTake(ShopEffect.ExtraBall);
            Inventory.AddRoundBall();
            return ItemError.None;
        }

        /// <summary>여분의 회전권: 이번 라운드 남은 회전 +1.</summary>
        public ItemError UseSpareSpin()
        {
            ItemError error = Ready(ShopEffect.SpareSpin);
            if (error != ItemError.None)
            {
                return error;
            }

            Inventory.TryTake(ShopEffect.SpareSpin);
            flow.Round.AddSpin();
            return ItemError.None;
        }

        /// <summary>보험 증서: 다음 회전에 보험. 이미 걸려 있으면 거절(중복 장착 없음).</summary>
        public ItemError UseInsurance()
        {
            ItemError error = Ready(ShopEffect.Insurance);
            if (error != ItemError.None)
            {
                return error;
            }

            if (Inventory.InsuranceArmed)
            {
                return ItemError.AlreadyArmed;
            }

            Inventory.TryTake(ShopEffect.Insurance);
            Inventory.ArmInsurance();
            return ItemError.None;
        }

        /// <summary>해골 방패 장착 +1(보유 안에서).</summary>
        public ItemError EquipShield()
        {
            ItemError error = Ready(ShopEffect.SkullShield);
            if (error != ItemError.None)
            {
                return error;
            }

            return Inventory.EquipShield() ? ItemError.None : ItemError.NoItem;
        }

        public ItemError UnequipShield()
        {
            if (flow.Phase != GamePhase.Shopping)
            {
                return ItemError.NotInShop;
            }

            return Inventory.UnequipShield() ? ItemError.None : ItemError.NoItem;
        }

        private ItemError Ready(ShopEffect effect)
        {
            if (flow.Phase != GamePhase.Shopping)
            {
                return ItemError.NotInShop;
            }

            return Inventory.CountOf(effect) > 0 ? ItemError.None : ItemError.NoItem;
        }
    }
}
