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

        /// <summary>산다. 성공하면 None. 실패하면 아무것도 바뀌지 않는다.</summary>
        public ShopError Buy(int slot, BetTarget target)
        {
            ShopError error = Check(slot, target);
            if (error != ShopError.None)
            {
                return error;
            }

            ShopOffer offer = State.Offers[slot];
            long price = PriceOf(offer);
            if (!flow.Wallet.TrySpend(price))
            {
                return ShopError.InsufficientFunds;
            }

            State.RecordPurchase(offer);
            ShopItemDefinition item = offer.Item;
            switch (item.Effect)
            {
                case ShopEffect.Focus:
                    flow.Upgrades.AddFocus(item.Group, target, item.Value);
                    break;
                case ShopEffect.Cross:
                    flow.Cross.Add(item.TriggerGroup, item.Group, item.Value);
                    break;
                case ShopEffect.PaymentSupport:
                    flow.Upgrades.AddPaymentRefund(item.Value);
                    break;

                // 소모품은 자동 사용: 추가 공·회전권은 사는 즉시, 방패·보험은 보관했다가 필요할 때 저절로.
                case ShopEffect.ExtraBall:
                    flow.Inventory.AddRoundBall();
                    break;
                case ShopEffect.SpareSpin:
                    flow.Round.AddSpin();
                    break;
                default:
                    flow.Inventory.Add(item);
                    break;
            }

            return ShopError.None;
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
