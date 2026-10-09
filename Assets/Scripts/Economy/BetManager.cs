using System.Collections.Generic;

namespace ProjectCR
{
    public enum BetError
    {
        None,
        NotInBettingPhase,
        NoBetsPlaced,
        InvalidNumber,
        SkullNotBettable,
        InvalidTarget,
        NonPositive,
        BelowMinimum,
        InsufficientFunds,
    }

    public static class BetErrorMessages
    {
        public static string ToKorean(this BetError error)
        {
            switch (error)
            {
                case BetError.NotInBettingPhase:
                    return "지금은 베팅할 수 없습니다.";
                case BetError.NoBetsPlaced:
                    return "번호판에 칩을 하나 이상 놓으세요.";
                case BetError.InvalidNumber:
                    return "휠에 없는 번호입니다.";
                case BetError.SkullNotBettable:
                    return "해골에는 베팅할 수 없습니다.";
                case BetError.InvalidTarget:
                    return "번호판에서 걸 수 없는 조합입니다.";
                case BetError.NonPositive:
                    return "칩 금액은 0보다 커야 합니다.";
                case BetError.BelowMinimum:
                    return "최소 베팅 금액보다 적습니다.";
                case BetError.InsufficientFunds:
                    return "보유 금액이 부족합니다.";
                default:
                    return string.Empty;
            }
        }
    }

    /// <summary>베팅 하나: 대상과 금액. 회전 시작 때 확정되면 이 목록 전체의 합이 한 번에 차감된다.</summary>
    public readonly struct ConfirmedBet
    {
        public ConfirmedBet(BetTarget target, long stake)
        {
            Target = target;
            Stake = stake;
        }

        /// <summary>숫자 하나 베팅. 테스트와 디버그에서 짧게 쓰려고 둔다.</summary>
        public ConfirmedBet(int number, long stake)
            : this(BetTarget.Straight(number), stake)
        {
        }

        public BetTarget Target { get; }
        public long Stake { get; }
    }

    /// <summary>
    /// 한 회전에 거는 베팅 목록(베팅판). 대상마다 금액이 하나씩 있고, 같은 대상에 다시 걸면 금액이 쌓인다.
    ///
    /// 회전 시작 때 <see cref="TryConfirm"/>가 전체를 검증하고 합계를 <b>한 번</b> 차감한 뒤 잠근다.
    /// 정산이 끝나면 <see cref="Unlock"/>으로 풀고, 게임 흐름이 판을 비운다(칩 회수, GameFlowController.CompleteSpin).
    /// </summary>
    public sealed class BetManager
    {
        public const int NoSelection = -1;

        private readonly RouletteDefinition definition;
        private readonly long minBet;

        // 놓은 순서를 지킨다. 표시와 "마지막 취소"가 이 순서를 쓴다.
        private readonly List<BetTarget> order = new List<BetTarget>();
        private readonly Dictionary<BetTarget, long> stakes = new Dictionary<BetTarget, long>();

        // 칩 하나 놓을 때마다 기록한다. "마지막 취소"는 칩 단위로 되돌린다.
        private readonly Stack<(BetTarget Target, long Amount)> history = new Stack<(BetTarget, long)>();

        public BetManager(RouletteDefinition definition, long minBet)
        {
            this.definition = definition;
            this.minBet = minBet;
            ChipValue = minBet;
        }

        /// <summary>번호판을 한 번 누를 때 놓이는 금액.</summary>
        public long ChipValue { get; private set; }

        public bool IsLocked { get; private set; }

        public bool HasBets => order.Count > 0;

        public int Count => order.Count;

        public long TotalStake
        {
            get
            {
                long total = 0;
                foreach (long stake in stakes.Values)
                {
                    total += stake;
                }

                return total;
            }
        }

        /// <summary>놓은 순서대로의 베팅 목록.</summary>
        public IReadOnlyList<ConfirmedBet> Bets
        {
            get
            {
                var bets = new List<ConfirmedBet>(order.Count);
                foreach (BetTarget target in order)
                {
                    bets.Add(new ConfirmedBet(target, stakes[target]));
                }

                return bets;
            }
        }

        public long StakeOn(BetTarget target) => stakes.TryGetValue(target, out long stake) ? stake : 0;

        public BetError SetChipValue(long value)
        {
            if (IsLocked)
            {
                return BetError.NotInBettingPhase;
            }

            ChipValue = value;
            return BetError.None;
        }

        /// <summary>대상에 칩 금액만큼 건다. 이미 건 대상이면 금액이 쌓인다.</summary>
        public BetError PlaceChip(BetTarget target, Wallet wallet) => PlaceBet(target, ChipValue, wallet);

        /// <summary>
        /// 대상에 amount를 건다. 이미 건 대상이면 금액이 쌓인다.
        /// 놓는 순간에도 합계가 보유 금액을 넘지 않게 막는다(실제 차감은 회전 시작 때).
        /// </summary>
        public BetError PlaceBet(BetTarget target, long amount, Wallet wallet)
        {
            if (IsLocked)
            {
                return BetError.NotInBettingPhase;
            }

            BetError error = ValidateTarget(target);
            if (error == BetError.None)
            {
                error = ValidateAmount(amount);
            }

            if (error == BetError.None && !wallet.CanAfford(TotalStake + amount))
            {
                error = BetError.InsufficientFunds;
            }

            if (error != BetError.None)
            {
                return error;
            }

            if (stakes.TryGetValue(target, out long current))
            {
                stakes[target] = current + amount;
            }
            else
            {
                order.Add(target);
                stakes[target] = amount;
            }

            history.Push((target, amount));
            return BetError.None;
        }

        /// <summary>
        /// 대상에서 칩 금액(<see cref="ChipValue"/>)만큼 뺀다(오른쪽 클릭). 남는 금액이 최소 베팅보다 적으면 그 대상을 통째로 뺀다.
        /// 건 게 없으면 아무것도 하지 않는다. "마지막 취소" 기록도 남은 금액에 맞춘다.
        /// </summary>
        public BetError RemoveChip(BetTarget target)
        {
            if (IsLocked)
            {
                return BetError.NotInBettingPhase;
            }

            if (!stakes.TryGetValue(target, out long current))
            {
                return BetError.None;
            }

            long left = current - ChipValue;
            if (left < System.Math.Max(1, minBet))
            {
                stakes.Remove(target);
                order.Remove(target);
            }
            else
            {
                stakes[target] = left;
            }

            RebuildHistory();
            return BetError.None;
        }

        /// <summary>
        /// from에 건 베팅을 통째로 to로 옮긴다(칩 끌기). to에 이미 건 게 있으면 합친다. to가 None이면(판 밖에 놓음) 뺀다.
        /// 합계는 그대로라 잔액 검사는 없다. to가 걸 수 없는 대상이면 오류이고 아무것도 바뀌지 않는다.
        /// "마지막 취소" 기록도 from 칩을 to 칩으로 바꿔 둔다.
        /// </summary>
        public BetError MoveBet(BetTarget from, BetTarget to)
        {
            if (IsLocked)
            {
                return BetError.NotInBettingPhase;
            }

            if (!stakes.TryGetValue(from, out long amount) || from == to)
            {
                return BetError.None;
            }

            if (to.IsNone)
            {
                return RemoveBet(from);
            }

            BetError error = ValidateTarget(to);
            if (error != BetError.None)
            {
                return error;
            }

            stakes.Remove(from);
            order.Remove(from);
            if (stakes.TryGetValue(to, out long current))
            {
                stakes[to] = current + amount;
            }
            else
            {
                order.Add(to);
                stakes[to] = amount;
            }

            var newestFirst = new List<(BetTarget Target, long Amount)>(history);
            history.Clear();
            for (int i = newestFirst.Count - 1; i >= 0; i--)
            {
                (BetTarget target, long chip) = newestFirst[i];
                history.Push((target == from ? to : target, chip));
            }

            return BetError.None;
        }

        /// <summary>대상에 건 베팅을 통째로 뺀다.</summary>
        public BetError RemoveBet(BetTarget target)
        {
            if (IsLocked)
            {
                return BetError.NotInBettingPhase;
            }

            if (stakes.Remove(target))
            {
                order.Remove(target);
                RebuildHistory();
            }

            return BetError.None;
        }

        /// <summary>마지막으로 놓은 칩 하나를 되돌린다.</summary>
        public BetError UndoLastChip()
        {
            if (IsLocked)
            {
                return BetError.NotInBettingPhase;
            }

            if (history.Count == 0)
            {
                return BetError.None;
            }

            (BetTarget target, long amount) = history.Pop();
            long left = stakes[target] - amount;
            if (left > 0)
            {
                stakes[target] = left;
            }
            else
            {
                stakes.Remove(target);
                order.Remove(target);
            }

            return BetError.None;
        }

        public BetError ClearBets()
        {
            if (IsLocked)
            {
                return BetError.NotInBettingPhase;
            }

            order.Clear();
            stakes.Clear();
            history.Clear();
            return BetError.None;
        }

        public BetError Validate(Wallet wallet)
        {
            if (IsLocked)
            {
                return BetError.NotInBettingPhase;
            }

            if (order.Count == 0)
            {
                return BetError.NoBetsPlaced;
            }

            foreach (BetTarget target in order)
            {
                BetError error = ValidateTarget(target);
                if (error == BetError.None)
                {
                    error = ValidateAmount(stakes[target]);
                }

                if (error != BetError.None)
                {
                    return error;
                }
            }

            return wallet.CanAfford(TotalStake) ? BetError.None : BetError.InsufficientFunds;
        }

        /// <summary>전체를 검증하고, 통과하면 합계를 지갑에서 한 번 차감하고 잠근다.</summary>
        public BetError TryConfirm(Wallet wallet, out IReadOnlyList<ConfirmedBet> bets)
        {
            bets = null;

            BetError error = Validate(wallet);
            if (error != BetError.None)
            {
                return error;
            }

            if (!wallet.TrySpend(TotalStake))
            {
                return BetError.InsufficientFunds;
            }

            bets = Bets;
            IsLocked = true;
            return BetError.None;
        }

        /// <summary>
        /// 정산 후 호출. 잠금을 풀고, 칩 금액은 잔액을 넘지 않게 줄인다(최소 베팅 아래로는 내리지 않는다).
        /// 판을 비우는 건 호출하는 쪽이 정한다.
        /// </summary>
        public void Unlock(long balance)
        {
            IsLocked = false;
            if (ChipValue > balance)
            {
                ChipValue = balance < minBet ? minBet : balance;
            }
        }

        private BetError ValidateAmount(long amount)
        {
            if (amount <= 0)
            {
                return BetError.NonPositive;
            }

            return amount < minBet ? BetError.BelowMinimum : BetError.None;
        }

        private BetError ValidateTarget(BetTarget target)
        {
            switch (target.Kind)
            {
                case BetKind.None:
                    return BetError.NoBetsPlaced;
                case BetKind.Straight:
                    if (target.Number == RouletteDefinition.SkullNumber)
                    {
                        return BetError.SkullNotBettable;
                    }

                    // 해골은 번호 조회에서 빠져 있어서 여기서도 걸러진다.
                    return definition.ContainsNumber(target.Number) ? BetError.None : BetError.InvalidNumber;
                case BetKind.Split:
                case BetKind.Street:
                case BetKind.Corner:
                case BetKind.SixLine:
                    // 번호판에서 실제로 붙어 있는 조합만 받는다.
                    return InsideBets.IsValid(target) ? BetError.None : BetError.InvalidTarget;
                default:
                    return BetError.None;
            }
        }

        /// <summary>베팅을 통째로 뺀 뒤, 남은 베팅만으로 칩 기록을 다시 맞춘다.</summary>
        private void RebuildHistory()
        {
            // 오래된 칩부터 대상별 남은 금액만큼만 남긴다(빠진 대상은 전부, 줄어든 대상은 최근 칩부터 지워진다).
            var oldestFirst = new List<(BetTarget Target, long Amount)>(history);
            oldestFirst.Reverse();
            var budget = new Dictionary<BetTarget, long>(stakes);
            history.Clear();
            foreach ((BetTarget target, long amount) in oldestFirst)
            {
                if (!budget.TryGetValue(target, out long left) || left <= 0)
                {
                    continue;
                }

                long take = System.Math.Min(amount, left);
                budget[target] = left - take;
                history.Push((target, take));
            }
        }
    }
}
