using System;
using System.Collections.Generic;

namespace ProjectCR
{
    public enum ShopError
    {
        None,
        NotInShop,
        InvalidSlot,
        SoldOut,
        LimitReached,
        NeedTarget,
        InvalidTarget,
        InsufficientFunds,
        MaxBalls,

        /// <summary>강화 칸이 다 찼는데 버릴 칸을 고르지 않았다(또는 빈칸을 골랐다).</summary>
        SlotsFull,
    }

    public static class ShopErrorMessages
    {
        public static string ToKorean(this ShopError error)
        {
            switch (error)
            {
                case ShopError.NotInShop:
                    return "지금은 상점을 이용할 수 없습니다.";
                case ShopError.InvalidSlot:
                    return "없는 상품입니다.";
                case ShopError.SoldOut:
                    return "품절";
                case ShopError.LimitReached:
                    return "이번 라운드 구매 완료";
                case ShopError.NeedTarget:
                    return "대상을 먼저 고르세요";
                case ShopError.InvalidTarget:
                    return "고를 수 없는 대상입니다";
                case ShopError.InsufficientFunds:
                    return "잔액 부족";
                case ShopError.MaxBalls:
                    return "공 수가 이미 최대";
                case ShopError.SlotsFull:
                    return "강화 칸이 가득 찼습니다. 버릴 강화를 고르세요";
                default:
                    return string.Empty;
            }
        }
    }

    /// <summary>
    /// 상점 구매. 돈은 Wallet에서만 빠지고, 구매 가능 금액 = 보유 − 판에 놓은 칩 합계(놓은 베팅이 잔액을 넘지 않게).
    ///
    /// 구매 한 번은 한 덩어리로 처리한다:
    /// 상태·상품·재고·제한·대상·가격·가용 금액 검증 → 돈 차감 → 재고 감소 → 효과 적용/아이템 지급.
    /// 소모품도 자동 사용이라 따로 쓰는 단계가 없다(추가 공·회전권은 사는 즉시, 방패·보험은 필요할 때 저절로).
    /// 검증에서 실패하면 돈·재고·효과 모두 그대로다. 집중 강화는 대상이 정해져야 검증을 통과한다(돈을 먼저 빼지 않는다).
    ///
    /// 강화(지속 효과 상품)는 강화 칸(<see cref="GameFlowController.OwnedUpgrades"/>)에 들어간다. 칸이 다 찼으면
    /// 버릴 칸을 함께 넘겨야 산다: 버린 강화의 효과를 빼고, 그 칸에 새 강화를 넣는다(<see cref="NeedsDiscard"/>).
    /// </summary>
    public sealed class ShopController
    {
        private readonly GameFlowController flow;

        public ShopController(GameFlowController flow, ShopCatalog catalog, int seed)
        {
            this.flow = flow;
            State = new ShopRunState(catalog, seed);
        }

        public ShopRunState State { get; }

        public bool HasCatalog => State.HasCatalog;

        /// <summary>구매에 쓸 수 있는 돈 = 보유 − 판에 놓은 칩 합계.</summary>
        public long AvailableFunds => Math.Max(0, flow.Wallet.Balance - flow.Bet.TotalStake);

        public IReadOnlyList<ShopOffer> Offers => State.Offers;

        public long PriceOf(ShopOffer offer) => offer.Item.PriceFor(flow.Round.CurrentDue, State.BoughtTotal(offer.Item));

        /// <summary>구매 버튼 상태 판정(돈은 건드리지 않는다). target은 집중 강화만 본다.</summary>
        public ShopError Check(int slot, BetTarget target)
        {
            if (flow.Phase != GamePhase.Shopping)
            {
                return ShopError.NotInShop;
            }

            if (slot < 0 || slot >= State.Offers.Count)
            {
                return ShopError.InvalidSlot;
            }

            ShopOffer offer = State.Offers[slot];
            if (offer.SoldOut)
            {
                return ShopError.SoldOut;
            }

            if (offer.LimitReached)
            {
                return ShopError.LimitReached;
            }

            if (offer.Item.Effect == ShopEffect.ExtraBall && flow.BuildSpinConfig().BallCount >= flow.GameBalance.MaxBallCount)
            {
                return ShopError.MaxBalls;
            }

            if (offer.Item.NeedsTarget)
            {
                if (target.IsNone)
                {
                    return ShopError.NeedTarget;
                }

                if (!IsValidFocusTarget(offer.Item.Group, target))
                {
                    return ShopError.InvalidTarget;
                }
            }

            return PriceOf(offer) > AvailableFunds ? ShopError.InsufficientFunds : ShopError.None;
        }

        /// <summary>이 진열 칸을 사려면 강화 칸 하나를 버려야 한다(강화이고 칸이 다 찼다).</summary>
        public bool NeedsDiscard(int slot) =>
            slot >= 0 && slot < State.Offers.Count && State.Offers[slot].Item.IsPermanent && flow.OwnedUpgrades.IsFull;

        /// <summary>
        /// 산다. 성공하면 None. 실패하면 아무것도 바뀌지 않는다.
        /// discardSlot: 강화 칸이 다 찼을 때 버릴 칸 번호(<see cref="NeedsDiscard"/>). 아니면 무시한다.
        /// </summary>
        public ShopError Buy(int slot, BetTarget target, int discardSlot = -1)
        {
            ShopError error = Check(slot, target);
            if (error != ShopError.None)
            {
                return error;
            }

            bool replace = NeedsDiscard(slot);
            if (replace && !flow.OwnedUpgrades.IsOccupied(discardSlot))
            {
                return ShopError.SlotsFull;
            }

            ShopOffer offer = State.Offers[slot];
            long price = PriceOf(offer);
            if (!flow.Wallet.TrySpend(price))
            {
                return ShopError.InsufficientFunds;
            }

            State.RecordPurchase(offer);
            ShopItemDefinition item = offer.Item;
            if (item.IsPermanent)
            {
                if (replace)
                {
                    Discard(discardSlot);
                    flow.OwnedUpgrades.Set(discardSlot, item, item.NeedsTarget ? target : BetTarget.None);
                }
                else
                {
                    flow.OwnedUpgrades.Add(item, item.NeedsTarget ? target : BetTarget.None);
                }
            }

            switch (item.Effect)
            {
                case ShopEffect.Focus:
                    flow.Upgrades.AddFocus(item.Group, target, item.Value);
                    break;
                case ShopEffect.Cross:
                    flow.Cross.Add(item.TriggerGroup, item.Group, item.Value, item.CrossTrigger, item.GainThresholdRatio, item.GainTriggerCapPerSpin);
                    break;
                case ShopEffect.PaymentSupport:
                    flow.Upgrades.AddPaymentRefund(item.Value);
                    break;

                // 특수 강화 추가 공: 이번 게임 동안 매 회전 공 +1. 해골 방어는 1회분을 보관(아래 default).
                case ShopEffect.ExtraBall:
                    flow.Upgrades.AddExtraBall();
                    break;

                // 소모품은 자동 사용: 회전권은 사는 즉시, 보험·귀띔·더블 업은 보관했다가 필요할 때 저절로.
                case ShopEffect.SpareSpin:
                    flow.Round.AddSpin();
                    break;
                default:
                    flow.Inventory.Add(item);
                    break;
            }

            return ShopError.None;
        }

        /// <summary>강화 칸 하나를 비우고 그 강화의 효과를 뺀다. 버린 상품은 다음 진열부터 다시 나올 수 있다.</summary>
        private void Discard(int index)
        {
            OwnedUpgrade removed = flow.OwnedUpgrades.Clear(index);
            ShopItemDefinition item = removed.Item;
            State.Release(item);
            switch (item.Effect)
            {
                case ShopEffect.Focus:
                    flow.Upgrades.RemoveFocus(item.Group, removed.Target, item.Value);
                    break;
                case ShopEffect.Cross:
                    flow.Cross.Remove(item.TriggerGroup, item.Group, item.Value);
                    break;
                case ShopEffect.PaymentSupport:
                    flow.Upgrades.RemovePaymentRefund(item.Value);
                    break;
                case ShopEffect.ExtraBall:
                    flow.Upgrades.RemoveExtraBall();
                    break;
                case ShopEffect.SkullShield:
                    flow.Inventory.ConsumeShields(1);
                    break;
            }
        }

        /// <summary>
        /// 집중 강화 대상: 그 묶음의 베팅 하나. 숫자는 숫자 하나(0 포함) 또는 0이 없는 유효한 조합(음료와 같은 규칙).
        /// 해골은 베팅 대상이 아니라서 들어올 수 없다.
        /// </summary>
        public static bool IsValidFocusTarget(DrinkGroup group, BetTarget target)
        {
            if (target.IsNone || DrinkGroups.Of(target.Kind) != group)
            {
                return false;
            }

            if (target.IsStraight)
            {
                return target.Number >= 0 && target.Number <= 36;
            }

            if (target.IsInside)
            {
                return InsideBets.IsValid(target) && !target.CoversNumber(0, PocketColor.Green);
            }

            return true;
        }
    }
}
