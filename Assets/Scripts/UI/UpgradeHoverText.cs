using System.Globalization;

namespace ProjectCR
{
    /// <summary>
    /// 테이블 배지(보유 강화)에 마우스를 올렸을 때의 툴팁 문구. 상태는 읽기만 한다.
    /// - "빨강 집중 · 집중 강화 · 빨강" / 효과 설명 / 지금 상태(등장 확률, 누적 진행도, 환급 비율…) / 적용 기간
    /// </summary>
    public readonly struct UpgradeHoverText
    {
        public UpgradeHoverText(string title, string accent, string effect, string detail, string duration)
        {
            Title = title;
            Accent = accent;
            Effect = effect;
            Detail = detail;
            Duration = duration;
        }

        public string Title { get; }

        /// <summary>금색 강조: 강화 종류(집중이면 고른 대상까지).</summary>
        public string Accent { get; }

        /// <summary>효과 설명(상품 설명 그대로, 누적 목표는 이번 납부금 기준 금액).</summary>
        public string Effect { get; }

        /// <summary>지금 상태. 보여 줄 게 없으면 빈 문자열.</summary>
        public string Detail { get; }

        public string Duration { get; }

        public static UpgradeHoverText For(OwnedUpgrade owned, GameFlowController flow)
        {
            ShopItemDefinition item = owned.Item;
            long due = flow.Round.CurrentDue;
            return new UpgradeHoverText(item.DisplayName, KindOf(owned, flow.Definition), item.DescriptionFor(due), DetailOf(owned, flow), item.Duration);
        }

        private static string KindOf(OwnedUpgrade owned, RouletteDefinition definition)
        {
            switch (owned.Item.Category)
            {
                case ShopCategory.Focus:
                    return owned.Target.IsNone ? "집중 강화" : $"집중 강화 · 대상 {DrinkOffer.TargetWithNumbers(owned.Target, definition)}";
                case ShopCategory.Cross:
                    return "교차 강화";
                case ShopCategory.PaymentSupport:
                    return "납부 지원";
                default:
                    return "특수 강화";
            }
        }

        private static string DetailOf(OwnedUpgrade owned, GameFlowController flow)
        {
            ShopItemDefinition item = owned.Item;
            switch (item.Effect)
            {
                case ShopEffect.Focus:
                {
                    string payout = $"{item.Group.ToKorean()} 베팅 당첨금 +{Percent(flow.Upgrades.PayoutIncrease(item.Group))}";
                    return owned.Target.IsNone
                        ? payout
                        : $"{payout} · {DrinkOffer.TargetLabel(owned.Target)} 등장 확률 {NumberText.Percent(flow.BuildSpinConfig().ProbabilityOf(owned.Target))}";
                }

                case ShopEffect.Cross:
                {
                    decimal pending = flow.Cross.Pending(item.Group);
                    string waiting = pending > 0m ? $" · 다음 회전 {ShopItemDefinition.GroupCode(item.Group)} +{Percent(pending)} 대기" : string.Empty;
                    if (item.CrossTrigger == CrossTriggerMode.Gain)
                    {
                        decimal progress = flow.Cross.ProgressOf(item.TriggerGroup, item.Group);
                        long goal = flow.Cross.ThresholdOf(item.TriggerGroup, item.Group, flow.Round.CurrentDue);
                        return $"이번 라운드 누적 {progress.ToString("#,0.##", CultureInfo.InvariantCulture)} / {goal:N0}{waiting}";
                    }

                    return waiting.Length > 0 ? waiting.Substring(3) : string.Empty;
                }

                case ShopEffect.PaymentSupport:
                    return $"지금 환급 {Percent(flow.Upgrades.PaymentRefund)}";
                case ShopEffect.ExtraBall:
                    return $"회전당 공 {flow.BuildSpinConfig().BallCount}개 (최대 {flow.GameBalance.MaxBallCount})";
                case ShopEffect.SkullShield:
                    return flow.Inventory.Shields > 0 ? "아직 쓰지 않음" : "사용함";
                default:
                    return string.Empty;
            }
        }

        private static string Percent(decimal value) => (value * 100m).ToString("0.#", CultureInfo.InvariantCulture) + "%";
    }
}
