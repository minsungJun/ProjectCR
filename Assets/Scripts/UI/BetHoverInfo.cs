using System.Collections.Generic;

namespace ProjectCR
{
    /// <summary>
    /// 판 위 호버 툴팁 내용: 베팅 이름, 대상 숫자, 기본·최종 배율, 칩 하나를 놓았을 때의 예상 지급액.
    ///
    /// 배율은 정산과 같은 <see cref="SpinConfig.MultiplierFor"/>(기본 × 음료 × (1 + 집중 + 이번 회전 교차))를 쓴다.
    /// SpinConfig는 GameFlowController.BuildSpinConfig가 만든 읽기용 복사본이라, 여기서 계산해도 돈·중첩·교차 효과가 바뀌지 않는다.
    /// 예상 지급액 = 버림(칩 × 최종 배율), 돈 상한에서 자름. <b>공 하나가 이 베팅에 들어갔을 때</b>의 금액이다.
    /// 실제 정산은 모든 베팅·공의 정확한 금액을 더한 뒤 한 번 버리므로, 여러 적중의 합은 툴팁 값들의 합보다 (적중 수 − 1) 이내로 클 수 있다.
    /// </summary>
    public readonly struct BetHoverInfo
    {
        private BetHoverInfo(BetTarget target, string kindName, string targets, int basePayout, decimal multiplier,
            long chipValue, long expectedPayout, int ballCount, bool affordable, string progress)
        {
            Progress = progress ?? string.Empty;
            Target = target;
            KindName = kindName;
            Targets = targets;
            BasePayout = basePayout;
            Multiplier = multiplier;
            ChipValue = chipValue;
            ExpectedPayout = expectedPayout;
            BallCount = ballCount;
            Affordable = affordable;
        }

        public BetTarget Target { get; }

        /// <summary>단독 · 스플릿 · 스트리트(트리오) · 코너(퍼스트 포) · 식스라인 · n번째 COLUMN/DOZEN · 홀수 · 짝수 · 빨강 · 검정 · LOW · HIGH.</summary>
        public string KindName { get; }

        /// <summary>대상 숫자. 안쪽은 전부 나열, 바깥은 범위나 이름.</summary>
        public string Targets { get; }

        /// <summary>기본 원금 포함 배율(강화 전).</summary>
        public int BasePayout { get; }

        /// <summary>강화가 반영된 최종 원금 포함 배율(반올림 전).</summary>
        public decimal Multiplier { get; }

        public bool IsBoosted => Multiplier != BasePayout;

        /// <summary>선택한 칩 한 개의 금액(판에 이미 놓인 칩과 무관).</summary>
        public long ChipValue { get; }

        /// <summary>칩 한 개, 공 하나 적중 기준 지급액(원금 포함, 버림).</summary>
        public long ExpectedPayout { get; }

        public int BallCount { get; }

        /// <summary>지금 이 칩을 한 개 더 놓을 돈이 있는가.</summary>
        public bool Affordable { get; }

        /// <summary>이 베팅 묶음이 발동 조건인 누적형 교차 강화 진행도("COLUMN 강화까지 35 / 60"). 없으면 빈 문자열.</summary>
        public string Progress { get; }

        public static BetHoverInfo Create(BetTarget target, SpinConfig config, long chipValue, long maxMoney, bool affordable,
            string progress = null)
        {
            decimal multiplier = config.MultiplierFor(target);
            long payout = PayoutCalculator.ToMoney(PayoutCalculator.ExactPayout(chipValue, multiplier, maxMoney), maxMoney);
            return new BetHoverInfo(target, KindNameOf(target), TargetsOf(target), config.PayoutFor(target), multiplier,
                chipValue, payout, config.BallCount, affordable, progress);
        }

        /// <summary>
        /// 대상 베팅의 묶음을 발동 조건으로 하는 누적형(Gain) 교차 강화의 이번 라운드 진행도. 읽기만 한다.
        /// 예: DOZEN 베팅 → "COLUMN 강화까지 35 / 60". 여러 개면 " · "로 잇는다.
        /// </summary>
        public static string CrossProgressOf(BetTarget target, CrossBetEffectState cross, long roundDue)
        {
            if (target.IsNone || cross == null)
            {
                return string.Empty;
            }

            DrinkGroup trigger = DrinkGroups.Of(target.Kind);
            var parts = new List<string>();
            for (int g = 0; g < DrinkGroups.Count; g++)
            {
                var goal = (DrinkGroup)g;
                if (cross.CountOf(trigger, goal) > 0 && cross.ModeOf(trigger, goal) == CrossTriggerMode.Gain)
                {
                    long done = (long)decimal.Floor(cross.ProgressOf(trigger, goal));
                    parts.Add($"{ShopItemDefinition.GroupCode(goal)} 강화까지 {done:N0} / {cross.ThresholdOf(trigger, goal, roundDue):N0}");
                }
            }

            return string.Join(" · ", parts);
        }

        public static string KindNameOf(BetTarget target)
        {
            bool hasZero = HasZero(target);
            switch (target.Kind)
            {
                case BetKind.Straight:
                    return "단독";
                case BetKind.Split:
                    return "스플릿";
                case BetKind.Street:
                    return hasZero ? "트리오" : "스트리트";
                case BetKind.Corner:
                    return hasZero ? "퍼스트 포" : "코너";
                case BetKind.SixLine:
                    return "식스라인";
                case BetKind.Column1:
                    return "첫 번째 COLUMN";
                case BetKind.Column2:
                    return "두 번째 COLUMN";
                case BetKind.Column3:
                    return "세 번째 COLUMN";
                case BetKind.Dozen1:
                    return "첫 번째 DOZEN";
                case BetKind.Dozen2:
                    return "두 번째 DOZEN";
                case BetKind.Dozen3:
                    return "세 번째 DOZEN";
                case BetKind.Odd:
                    return "홀수";
                case BetKind.Even:
                    return "짝수";
                case BetKind.Red:
                    return "빨강";
                case BetKind.Black:
                    return "검정";
                case BetKind.Low:
                    return "LOW";
                case BetKind.High:
                    return "HIGH";
                default:
                    return string.Empty;
            }
        }

        public static string TargetsOf(BetTarget target)
        {
            switch (target.Kind)
            {
                case BetKind.Straight:
                case BetKind.Split:
                case BetKind.Street:
                case BetKind.Corner:
                case BetKind.SixLine:
                    return string.Join(" · ", new List<int>(target.Numbers));
                case BetKind.Column1:
                    return "1, 4, 7 … 34";
                case BetKind.Column2:
                    return "2, 5, 8 … 35";
                case BetKind.Column3:
                    return "3, 6, 9 … 36";
                case BetKind.Dozen1:
                    return "1~12";
                case BetKind.Dozen2:
                    return "13~24";
                case BetKind.Dozen3:
                    return "25~36";
                case BetKind.Odd:
                    return "1, 3, 5 … 35";
                case BetKind.Even:
                    return "2, 4, 6 … 36";
                case BetKind.Red:
                    return "빨간 숫자 18개";
                case BetKind.Black:
                    return "검은 숫자 18개";
                case BetKind.Low:
                    return "1~18";
                case BetKind.High:
                    return "19~36";
                default:
                    return string.Empty;
            }
        }

        private static bool HasZero(BetTarget target)
        {
            foreach (int n in target.Numbers)
            {
                if (n == 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
