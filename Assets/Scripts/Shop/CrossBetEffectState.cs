using System;
using System.Collections.Generic;

namespace ProjectCR
{
    /// <summary>교차 강화 발동 한 건(결과 문구용). Gain 발동은 한 회전에 여러 번 달성할 수 있어서 Count와 합산 Increase를 둔다.</summary>
    public readonly struct CrossTrigger
    {
        public CrossTrigger(DrinkGroup trigger, DrinkGroup target, decimal increase, int count = 1, CrossTriggerMode mode = CrossTriggerMode.Win)
        {
            Trigger = trigger;
            Target = target;
            Increase = increase;
            Count = count;
            Mode = mode;
        }

        public DrinkGroup Trigger { get; }
        public DrinkGroup Target { get; }

        /// <summary>이번 발동으로 다음 회전에 더해진 증가량(달성 횟수 × 상품 강화량).</summary>
        public decimal Increase { get; }

        /// <summary>달성 횟수. Win 발동은 늘 1.</summary>
        public int Count { get; }

        public CrossTriggerMode Mode { get; }
    }

    /// <summary>
    /// 상점 교차 강화. 상품(발동 묶음 → 대상 묶음)마다 보유 강화량과 발동 조건을 둔다.
    ///
    /// 발동 조건(<see cref="CrossTriggerMode"/>):
    /// - Win: 정산에서 <b>발동 묶음에 건 베팅이 하나라도 당첨</b>되면 상품마다 회전당 한 번, 다음 회전 대상 묶음 지급 +강화량.
    ///   결과 번호가 그 묶음에 속하는 것만으로는 발동하지 않는다.
    /// - Gain: 발동 묶음 베팅의 원금 포함 지급액(<see cref="BetPayout.ExactAmount"/>, 음료·집중·교차 반영, 버림 전)을
    ///   <b>이번 라운드 동안</b> 누적해서, 목표 N = 올림(납부금 × 기준 비율)에 닿을 때마다 한 번. 달성 횟수 = 버림(누적 / N),
    ///   나머지는 같은 라운드 다음 달성으로 이월한다. 회전당 상한이 있으면 넘친 배수는 버린다. 라운드가 바뀌면 미달 누적액은 버린다.
    ///   산 뒤의 지급액만 센다(산 상품만 누적하므로 소급 없음).
    /// 막지 못한 해골 낙첨이면 어느 조건도 발동·누적하지 않는다. 보험·납부 환급은 지급액이 아니라 들어가지 않는다.
    ///
    /// 적용: 다음 실제 회전 시작 때 그 회전 설정에 들어가고(<see cref="TakePendingForSpin"/>) 바로 비워진다.
    /// 그래서 발동한 회전 자체에는 적용되지 않고, 대상에 베팅하지 않아도 그 회전이 끝나면 사라진다.
    /// 라운드가 바뀌거나 상점·음료 선택을 해도 대기 중인 강화는 비우지 않는다(라운드 마지막 회전에 생기면 다음 라운드 첫 회전까지 간다).
    /// 이 상태를 바꾸는 건 구매(<see cref="Add"/>)·정산(<see cref="OnSettled"/>)·회전 시작·라운드 종료뿐이라, 미리보기·호버·상점 재진입은 진행도를 바꾸지 않는다.
    /// </summary>
    public sealed class CrossBetEffectState
    {
        private readonly decimal[,] owned = new decimal[DrinkGroups.Count, DrinkGroups.Count];
        private readonly int[,] counts = new int[DrinkGroups.Count, DrinkGroups.Count];
        private readonly CrossTriggerMode[,] modes = new CrossTriggerMode[DrinkGroups.Count, DrinkGroups.Count];
        private readonly decimal[,] gainRatios = new decimal[DrinkGroups.Count, DrinkGroups.Count];
        private readonly int[,] gainCaps = new int[DrinkGroups.Count, DrinkGroups.Count];
        private readonly decimal[,] progress = new decimal[DrinkGroups.Count, DrinkGroups.Count];
        private readonly decimal[] pending = new decimal[DrinkGroups.Count];

        /// <summary>그 상품(발동 → 대상)을 산 수.</summary>
        public int CountOf(DrinkGroup trigger, DrinkGroup target) => counts[(int)trigger, (int)target];

        /// <summary>그 상품의 보유 강화량 합.</summary>
        public decimal IncreaseOf(DrinkGroup trigger, DrinkGroup target) => owned[(int)trigger, (int)target];

        public CrossTriggerMode ModeOf(DrinkGroup trigger, DrinkGroup target) => modes[(int)trigger, (int)target];

        /// <summary>Gain 상품의 이번 라운드 누적 지급액(목표 미달분).</summary>
        public decimal ProgressOf(DrinkGroup trigger, DrinkGroup target) => progress[(int)trigger, (int)target];

        /// <summary>Gain 상품의 이번 라운드 목표 금액(납부금 기준).</summary>
        public long ThresholdOf(DrinkGroup trigger, DrinkGroup target, long roundDue) =>
            ShopItemDefinition.ThresholdOf(roundDue, gainRatios[(int)trigger, (int)target]);

        /// <summary>다음 회전에 걸린 대상 묶음 지급 증가.</summary>
        public decimal Pending(DrinkGroup target) => pending[(int)target];

        public bool HasPending
        {
            get
            {
                foreach (decimal value in pending)
                {
                    if (value > 0m)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public int TotalOwned
        {
            get
            {
                int total = 0;
                foreach (int count in counts)
                {
                    total += count;
                }

                return total;
            }
        }

        /// <summary>구매. Gain이면 기준 비율과 회전당 달성 상한(0 = 없음)도 기억한다.</summary>
        internal void Add(DrinkGroup trigger, DrinkGroup target, decimal value,
            CrossTriggerMode mode = CrossTriggerMode.Win, decimal gainRatio = 0.5m, int gainCapPerSpin = 0)
        {
            int t = (int)trigger;
            int g = (int)target;
            owned[t, g] += value;
            counts[t, g]++;
            modes[t, g] = mode;
            gainRatios[t, g] = gainRatio;
            gainCaps[t, g] = Math.Max(0, gainCapPerSpin);
        }

        /// <summary>회전 시작: 다음 회전 강화를 이 회전에 넘기고 비운다. 돌려준 값이 이 회전 설정에 들어간다.</summary>
        internal decimal[] TakePendingForSpin()
        {
            var taken = (decimal[])pending.Clone();
            for (int i = 0; i < pending.Length; i++)
            {
                pending[i] = 0m;
            }

            return taken;
        }

        /// <summary>
        /// 정산 뒤 한 번: 당첨 베팅의 묶음별 당첨 여부와 지급액으로 발동을 판정해 다음 회전 강화에 더한다. 발동 목록을 돌려준다.
        /// roundDue는 이 회전이 속한 라운드의 납부금(Gain 목표 계산용).
        /// </summary>
        internal List<CrossTrigger> OnSettled(SettlementResult settlement, long roundDue)
        {
            var triggers = new List<CrossTrigger>();
            if (settlement.IsSkullBust)
            {
                return triggers;
            }

            // 묶음별 당첨 여부와 원금 포함 지급액(베팅·공 여러 개를 버림 없이 합산).
            var won = new bool[DrinkGroups.Count];
            var gained = new decimal[DrinkGroups.Count];
            foreach (BetPayout bet in settlement.Bets)
            {
                if (bet.IsWin)
                {
                    int group = (int)DrinkGroups.Of(bet.Bet.Target.Kind);
                    won[group] = true;
                    gained[group] += bet.ExactAmount;
                }
            }

            for (int trigger = 0; trigger < DrinkGroups.Count; trigger++)
            {
                for (int target = 0; target < DrinkGroups.Count; target++)
                {
                    decimal increase = owned[trigger, target];
                    if (increase <= 0m)
                    {
                        continue;
                    }

                    if (modes[trigger, target] == CrossTriggerMode.Gain)
                    {
                        int times = AccumulateGain(trigger, target, gained[trigger], roundDue);
                        if (times > 0)
                        {
                            decimal total = increase * times;
                            pending[target] += total;
                            triggers.Add(new CrossTrigger((DrinkGroup)trigger, (DrinkGroup)target, total, times, CrossTriggerMode.Gain));
                        }
                    }
                    else if (won[trigger])
                    {
                        pending[target] += increase;
                        triggers.Add(new CrossTrigger((DrinkGroup)trigger, (DrinkGroup)target, increase));
                    }
                }
            }

            return triggers;
        }

        /// <summary>라운드가 바뀌면 Gain 미달 누적액을 버린다. 이미 얻은 다음 회전 강화(pending)는 그대로.</summary>
        internal void OnRoundEnded()
        {
            Array.Clear(progress, 0, progress.Length);
        }

        /// <summary>누적에 더하고 목표 달성 횟수(몫)를 돌려준다. 나머지는 이월, 상한을 넘은 배수는 버린다.</summary>
        private int AccumulateGain(int trigger, int target, decimal amount, long roundDue)
        {
            if (amount <= 0m)
            {
                return 0;
            }

            decimal threshold = ShopItemDefinition.ThresholdOf(roundDue, gainRatios[trigger, target]);
            decimal total = progress[trigger, target] + amount;
            decimal times = decimal.Floor(total / threshold);
            progress[trigger, target] = total - times * threshold;

            int count = times >= int.MaxValue ? int.MaxValue : (int)times;
            int cap = gainCaps[trigger, target];
            return cap > 0 ? Math.Min(count, cap) : count;
        }
    }
}
