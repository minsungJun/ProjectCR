using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectCR
{
    /// <summary>상점 진열 분류. 진열은 분류를 먼저 고른 뒤 그 안에서 상품을 뽑는다.</summary>
    public enum ShopCategory
    {
        /// <summary>집중 강화: 지정 대상 추첨 가중치 + 그 묶음 지급액. 이번 게임 동안.</summary>
        Focus,

        /// <summary>교차 강화: 발동 묶음 베팅이 당첨되면 다음 회전에 다른 묶음 지급액 +.</summary>
        Cross,

        /// <summary>납부 지원: 이번 게임 동안, 라운드가 끝나 납부할 때마다 낸 납부금의 일부를 돌려받는다.</summary>
        PaymentSupport,

        /// <summary>소모품: 자동 사용(회전권은 사는 즉시, 보험·귀띔·더블 업은 필요할 때 저절로).</summary>
        Consumable,

        /// <summary>특수 강화(지속): 추가 공(매 라운드 공 +1), 해골 방어(게임 전체 1회). 강화 탭에 섞여 나온다.</summary>
        Special,
    }

    public enum ShopEffect
    {
        Focus,
        Cross,
        PaymentSupport,

        /// <summary>특수 강화: 이번 게임 동안 매 회전 공 +1(최대 공 수까지).</summary>
        ExtraBall,

        /// <summary>특수 강화: 게임 전체에서 한 번, 해골이 나온 공 하나를 다시 뽑는다.</summary>
        SkullShield,

        /// <summary>이번 라운드 남은 회전 +1.</summary>
        SpareSpin,

        /// <summary>다음 회전이 일반 낙첨이면 베팅금 일부 환급.</summary>
        Insurance,

        /// <summary>딜러의 귀띔: 상점을 나간 뒤 첫 회전에 나오지 않을 바깥 베팅 하나를 알려 준다(결과를 미리 뽑아 둔다).</summary>
        DealerHint,

        /// <summary>더블 업 쿠폰: 이번 라운드 마지막 회전 지급 ×N(1.2).</summary>
        DoubleUp,
    }

    /// <summary>교차 강화 발동 조건.</summary>
    public enum CrossTriggerMode
    {
        /// <summary>발동 묶음에 건 베팅이 하나라도 당첨되면 회전당 한 번.</summary>
        Win,

        /// <summary>
        /// 발동 묶음 베팅의 원금 포함 지급액을 이번 라운드 동안 누적해서, 목표(납부금 × 기준 비율, 올림)에 닿을 때마다 한 번.
        /// 초과분은 같은 라운드 다음 달성으로 이월하고, 라운드가 바뀌면 미달 누적액은 버린다.
        /// </summary>
        Gain,
    }

    /// <summary>
    /// 상점 상품 하나(정적 데이터). 실행 중 수량·중첩은 여기 두지 않는다(<see cref="ShopRunState"/>, <see cref="InventoryState"/> 등).
    ///
    /// 가격 = 5 단위 올림(현재 납부금 × priceRatio × priceGrowth^이미 산 수), 최소 minPrice.
    /// 납부금을 따라 오른다. <b>N·가격은 첫 시험값이다.</b>
    /// 지속 효과(집중·교차·납부 지원)는 한 번 사면 다시 진열되지 않아서(ShopRunState.IsRetired) priceGrowth는 지금 쓰이지 않는다.
    /// </summary>
    [CreateAssetMenu(fileName = "ShopItem", menuName = "ProjectCR/Shop Item")]
    public class ShopItemDefinition : ScriptableObject
    {
        private const long PriceUnit = 5;

        /// <summary>Gain 발동 기본 기준(납부금의 50%). 첫 시험값.</summary>
        public const float DefaultGainRatio = 0.5f;

        /// <summary>
        /// 기본 상품 정의에서 규칙(분류·효과·이름·값·가격·재고)만 이 에셋에 옮긴다. 아이콘·색·수동 문구는 그대로. 에디터 갱신 메뉴용.
        /// 바뀐 게 있으면 true.
        /// </summary>
        public bool CopyRulesFrom(ShopItemDefinition source)
        {
            bool same = category == source.category && effect == source.effect && displayName == source.displayName
                        && Mathf.Approximately(value, source.value) && Mathf.Approximately(priceRatio, source.priceRatio)
                        && Mathf.Approximately(priceGrowth, source.priceGrowth) && stockPerRound == source.stockPerRound
                        && buyLimitPerRound == source.buyLimitPerRound;
            if (same)
            {
                return false;
            }

            category = source.category;
            effect = source.effect;
            displayName = source.displayName;
            value = source.value;
            priceRatio = source.priceRatio;
            priceGrowth = source.priceGrowth;
            stockPerRound = source.stockPerRound;
            buyLimitPerRound = source.buyLimitPerRound;
            return true;
        }

        /// <summary>기본 상품 정의에서 발동 조건만 이 에셋에 옮긴다(가격·아이콘·문구 등은 그대로). 에디터 갱신 메뉴용.</summary>
        public bool CopyCrossTriggerFrom(ShopItemDefinition source)
        {
            if (source == null || (crossTrigger == source.crossTrigger && Mathf.Approximately(gainThresholdRatio, source.gainThresholdRatio)
                                   && gainTriggerCapPerSpin == source.gainTriggerCapPerSpin))
            {
                return false;
            }

            crossTrigger = source.crossTrigger;
            gainThresholdRatio = source.gainThresholdRatio;
            gainTriggerCapPerSpin = source.gainTriggerCapPerSpin;
            return true;
        }

        [SerializeField] private string id = "item";
        [SerializeField] private string displayName = "상품";

        [Tooltip("자세한 효과 설명. 비우면 효과에서 만든다.")]
        [TextArea(2, 4)]
        [SerializeField] private string description;

        [Tooltip("상점 카드에 쓰는 짧은 설명(두 줄 정도). 비우면 효과에서 만든다.")]
        [TextArea(2, 3)]
        [SerializeField] private string cardText;

        [Tooltip("상점 카드 아이콘. 비우면 강조색 원으로 대신 그린다.")]
        [SerializeField] private Sprite icon;
        [SerializeField] private Color tint = new Color(0.78f, 0.6f, 0.3f);

        [SerializeField] private ShopCategory category;
        [SerializeField] private ShopEffect effect;

        [Tooltip("집중: 강화 묶음. 교차: 강화 대상 묶음.")]
        [SerializeField] private DrinkGroup group;

        [Tooltip("교차: 발동 묶음.")]
        [SerializeField] private DrinkGroup triggerGroup;

        [Tooltip("교차: 발동 조건. Win = 발동 묶음 베팅 당첨 시(회전당 1번), Gain = 발동 묶음 지급액 이번 라운드 누적이 목표에 닿을 때마다.")]
        [SerializeField] private CrossTriggerMode crossTrigger = CrossTriggerMode.Win;

        [Tooltip("교차(Gain): 목표 = 올림(현재 납부금 × 이 비율), 최소 1. 0.5 = 납부금의 50%.")]
        [Min(0.01f)]
        [SerializeField] private float gainThresholdRatio = 0.5f;

        [Tooltip("교차(Gain): 한 회전에 달성할 수 있는 최대 횟수. 0 = 제한 없음. 넘친 배수는 버린다(나머지만 이월).")]
        [Min(0)]
        [SerializeField] private int gainTriggerCapPerSpin;

        [Tooltip("효과 크기 N. 집중 0.1 = 가중치·지급 +10%, 교차 0.25 = 다음 회전 +25%, 납부 지원 0.25 = 납부금의 25%, 보험 0.5 = 베팅금 50%.")]
        [Min(0f)]
        [SerializeField] private float value = 0.1f;

        [Header("가격")]
        [Tooltip("현재 납부금 대비 가격 비율.")]
        [Min(0f)]
        [SerializeField] private float priceRatio = 0.2f;

        [Tooltip("이 상품을 이미 산 수만큼 가격에 곱한다(영구 강화). 1이면 그대로.")]
        [Min(1f)]
        [SerializeField] private float priceGrowth = 1f;

        [Min(1)]
        [SerializeField] private long minPrice = 5;

        [Header("재고")]
        [Tooltip("라운드 진열 때 들어오는 재고.")]
        [Min(1)]
        [SerializeField] private int stockPerRound = 1;

        [Tooltip("한 라운드에 살 수 있는 최대 수.")]
        [Min(1)]
        [SerializeField] private int buyLimitPerRound = 1;

        public string Id => id;
        public string DisplayName => displayName;
        public Sprite Icon => icon;
        public Color Tint => tint;
        public ShopCategory Category => category;
        public ShopEffect Effect => effect;
        public DrinkGroup Group => group;
        public DrinkGroup TriggerGroup => triggerGroup;
        public CrossTriggerMode CrossTrigger => crossTrigger;
        public int GainTriggerCapPerSpin => Mathf.Max(0, gainTriggerCapPerSpin);

        /// <summary>Gain 발동 기준 비율(decimal로 정확히).</summary>
        public decimal GainThresholdRatio => (decimal)Mathf.Max(0.01f, gainThresholdRatio);

        /// <summary>Gain 발동 목표 금액 = 올림(납부금 × 기준 비율), 최소 1.</summary>
        public long GainThreshold(long due) => ThresholdOf(due, GainThresholdRatio);

        public static long ThresholdOf(long due, decimal ratio) => Math.Max(1L, (long)Math.Ceiling(Math.Max(0L, due) * ratio));
        public int StockPerRound => stockPerRound;
        public int BuyLimitPerRound => buyLimitPerRound;

        /// <summary>효과 크기(decimal로 정확히). 0.1f → 0.1.</summary>
        public decimal Value => (decimal)Mathf.Max(0f, value);

        public bool IsPermanent => category == ShopCategory.Focus || category == ShopCategory.Cross || category == ShopCategory.PaymentSupport
                                   || category == ShopCategory.Special;

        /// <summary>대상을 골라야 살 수 있다(집중 강화).</summary>
        public bool NeedsTarget => effect == ShopEffect.Focus;

        public string Description => DescriptionFor(0);

        /// <summary>상점 카드의 짧은 설명(두 줄).</summary>
        public string CardText => CardTextFor(0);

        /// <summary>자세한 설명. due가 있으면 Gain 목표를 실제 금액으로 쓴다(0이면 "납부금의 n%").</summary>
        public string DescriptionFor(long due) => string.IsNullOrWhiteSpace(description) ? DefaultDescription(due) : description;

        /// <summary>카드 설명. due가 있으면 Gain 목표를 실제 금액으로 쓴다(0이면 "납부금의 n%").</summary>
        public string CardTextFor(long due) => string.IsNullOrWhiteSpace(cardText) ? DefaultCardText(due) : cardText;

        /// <summary>Gain 목표 문구: 납부금을 알면 금액, 모르면 비율.</summary>
        private string GainGoalText(long due) =>
            due > 0 ? $"{GainThreshold(due):N0}" : $"납부금의 {(GainThresholdRatio * 100m).ToString("0.#")}%";

        /// <summary>아이콘이 없을 때 색 원 위에 쓰는 기호(상점 카드, 테이블 배지).</summary>
        public string PlaceholderSymbol
        {
            get
            {
                switch (effect)
                {
                    case ShopEffect.Focus:
                        return "▲";
                    case ShopEffect.Cross:
                        return "→";
                    case ShopEffect.PaymentSupport:
                        return "₩";
                    case ShopEffect.ExtraBall:
                        return "●+";
                    case ShopEffect.SkullShield:
                        return "☠";
                    case ShopEffect.SpareSpin:
                        return "+1";
                    case ShopEffect.DealerHint:
                        return "?";
                    case ShopEffect.DoubleUp:
                        return "×1.2";
                    default:
                        return "½";
                }
            }
        }

        /// <summary>적용 기간 문구.</summary>
        public string Duration
        {
            get
            {
                switch (effect)
                {
                    case ShopEffect.Focus:
                        return "이번 게임 동안 · 구매 즉시 적용";
                    case ShopEffect.Cross:
                        return crossTrigger == CrossTriggerMode.Gain
                            ? "이번 게임 동안 보유 · 이번 라운드 누적 · 달성할 때마다 다음 회전 1번"
                            : "이번 게임 동안 보유 · 발동하면 다음 회전 1번";
                    case ShopEffect.PaymentSupport:
                        return "이번 게임 동안 · 구매 즉시 적용 · 납부할 때마다 환급";
                    case ShopEffect.ExtraBall:
                        return "이번 게임 동안 · 구매 즉시 적용";
                    case ShopEffect.SkullShield:
                        return "게임 전체 1회 · 해골이 나오면 자동";
                    case ShopEffect.Insurance:
                        return "자동 사용 · 다음 회전부터 회전마다 1장씩";
                    case ShopEffect.DealerHint:
                        return "자동 사용 · 상점을 나간 뒤 첫 회전";
                    case ShopEffect.DoubleUp:
                        return "자동 사용 · 이번 라운드 마지막 회전";
                    default:
                        return "자동 사용 · 구매 즉시";
                }
            }
        }

        /// <summary>현재 납부금과 이미 산 수에서 가격을 다시 계산한다.</summary>
        /// <remarks>decimal로 계산한다. float 0.3 × 300을 double로 하면 90.0000036이 되어 95로 올림되는 일이 생긴다.</remarks>
        public long PriceFor(long due, int alreadyBought)
        {
            const decimal cap = 1_000_000_000_000_000m;
            decimal raw = Math.Max(0, due) * (decimal)Mathf.Max(0f, priceRatio);
            if (IsPermanent)
            {
                decimal growth = (decimal)Mathf.Max(1f, priceGrowth);
                for (int i = 0; i < alreadyBought && raw < cap; i++)
                {
                    raw *= growth;
                }
            }

            decimal rounded = Math.Ceiling(Math.Min(raw, cap) / PriceUnit) * PriceUnit;
            return Math.Max(Math.Max(1, minPrice), (long)rounded);
        }

        private string DefaultCardText(long due)
        {
            string percent = (Value * 100m).ToString("0.#") + "%";
            switch (effect)
            {
                case ShopEffect.Focus:
                    return $"고른 대상 확률 +{percent}\n{group.ToKorean()} 지급액 +{percent}";
                case ShopEffect.Cross:
                    return crossTrigger == CrossTriggerMode.Gain
                        ? $"이번 라운드 {GroupCode(triggerGroup)} 누적 지급 {GainGoalText(due)}마다\n다음 회전 {GroupCode(group)} +{percent}"
                        : $"{GroupCode(triggerGroup)} 당첨 시\n다음 회전 {GroupCode(group)} +{percent}";
                case ShopEffect.PaymentSupport:
                    return $"납부할 때마다\n납부금 {percent} 환급";
                case ShopEffect.ExtraBall:
                    return "매 라운드\n공 1개 추가";
                case ShopEffect.SkullShield:
                    return "해골이 나오면\n해당 공 재회전(1회)";
                case ShopEffect.SpareSpin:
                    return "남은 회전\n+1회";
                case ShopEffect.DealerHint:
                    return "다음 회전에 안 나올\n베팅 하나 알림";
                case ShopEffect.DoubleUp:
                    return $"이번 라운드 마지막 회전\n지급 ×{Value.ToString("0.##")}";
                default:
                    return $"일반 낙첨 시\n베팅금 {percent} 환급";
            }
        }

        private string DefaultDescription(long due)
        {
            string percent = (Value * 100m).ToString("0.#") + "%";
            switch (effect)
            {
                case ShopEffect.Focus:
                    return $"고른 {group.ToKorean()} 대상 추첨 가중치 +{percent}, {group.BetsKorean()} 베팅 지급액 +{percent}";
                case ShopEffect.Cross:
                    return crossTrigger == CrossTriggerMode.Gain
                        ? $"이번 라운드 {GroupCode(triggerGroup)} 베팅 지급액(원금 포함) 누적이 {GainGoalText(due)}에 닿을 때마다 다음 회전 {GroupCode(group)} 지급액 +{percent}. 초과분은 이월, 라운드가 바뀌면 미달분은 사라집니다"
                        : $"{GroupCode(triggerGroup)} 베팅이 당첨되면 다음 회전 {GroupCode(group)} 지급액 +{percent} (회전당 1번)";
                case ShopEffect.PaymentSupport:
                    return $"라운드가 끝나 납부할 때마다 낸 납부금의 {percent}를 돌려받습니다(납부금은 그대로). 여러 개면 더해집니다";
                case ShopEffect.ExtraBall:
                    return "이번 게임 동안 모든 회전에 공 +1 (최대 공 수까지)";
                case ShopEffect.SkullShield:
                    return "게임 전체에서 한 번, 해골이 나오면 그 공 하나를 다시 굴립니다. 다른 공은 그대로";
                case ShopEffect.SpareSpin:
                    return "이번 라운드 남은 회전 +1";
                case ShopEffect.DealerHint:
                    return "상점을 나간 뒤 첫 회전에 나오지 않을 바깥 베팅 하나(없으면 숫자 하나)를 알려 줍니다. 거짓말은 하지 않습니다";
                case ShopEffect.DoubleUp:
                    return $"이번 라운드 마지막 회전의 지급액 ×{Value.ToString("0.##")}. 라운드가 끝나면 사라집니다";
                default:
                    return $"다음 회전이 모두 일반 낙첨이면 베팅금의 {percent} 환급 (해골 낙첨은 제외)";
            }
        }

        /// <summary>교차 강화 이름에 쓰는 묶음 표기(COLUMN, DOZEN, 홀짝, 색상, 높낮이, 숫자).</summary>
        public static string GroupCode(DrinkGroup group)
        {
            switch (group)
            {
                case DrinkGroup.Column:
                    return "COLUMN";
                case DrinkGroup.Dozen:
                    return "DOZEN";
                case DrinkGroup.Parity:
                    return "홀짝";
                case DrinkGroup.Color:
                    return "색상";
                case DrinkGroup.Range:
                    return "높낮이";
                default:
                    return "숫자";
            }
        }

        /// <summary>코드로 만든다(테스트, 기본 에셋 생성).</summary>
        public static ShopItemDefinition Create(string id, string displayName, ShopCategory category, ShopEffect effect, float value,
            float priceRatio, float priceGrowth, int stockPerRound, int buyLimitPerRound,
            DrinkGroup group = DrinkGroup.Column, DrinkGroup triggerGroup = DrinkGroup.Column)
        {
            var item = CreateInstance<ShopItemDefinition>();
            item.name = $"Shop_{id}";
            item.id = id;
            item.displayName = displayName;
            item.category = category;
            item.effect = effect;
            item.value = value;
            item.priceRatio = priceRatio;
            item.priceGrowth = priceGrowth;
            item.stockPerRound = stockPerRound;
            item.buyLimitPerRound = buyLimitPerRound;
            item.group = group;
            item.triggerGroup = triggerGroup;
            item.tint = category == ShopCategory.Consumable || category == ShopCategory.PaymentSupport || category == ShopCategory.Special
                ? new Color(0.78f, 0.6f, 0.3f)
                : DrinkColors.Of(group);
            return item;
        }

        /// <summary>
        /// 기본 43종(첫 시험값): 집중 6(N 10%, 납부금의 30% × 1.5^산 수), 교차 30(N 25%, 20% × 1.3^산 수),
        /// 납부 지원 1(납부할 때마다 25% 환급, 15% × 1.5^산 수), 특수 강화 2(추가 공 100%, 해골 방어 30%),
        /// 소모품 4(딜러의 귀띔 20%, 더블 업 쿠폰 ×1.2 10%, 회전권 30%, 보험 증서 50% 환급 10%).
        /// </summary>
        public static List<ShopItemDefinition> CreateDefaults()
        {
            var items = new List<ShopItemDefinition>();
            foreach (DrinkGroup group in (DrinkGroup[])Enum.GetValues(typeof(DrinkGroup)))
            {
                items.Add(Create($"focus_{group.ToString().ToLowerInvariant()}", $"{FocusName(group)} 집중",
                    ShopCategory.Focus, ShopEffect.Focus, 0.1f, 0.3f, 1.5f, 1, 1, group));
            }

            foreach (DrinkGroup trigger in (DrinkGroup[])Enum.GetValues(typeof(DrinkGroup)))
            {
                foreach (DrinkGroup target in (DrinkGroup[])Enum.GetValues(typeof(DrinkGroup)))
                {
                    if (target == trigger)
                    {
                        continue;
                    }

                    ShopItemDefinition cross = Create($"cross_{trigger.ToString().ToLowerInvariant()}_{target.ToString().ToLowerInvariant()}",
                        $"{GroupCode(trigger)} → {GroupCode(target)}",
                        ShopCategory.Cross, ShopEffect.Cross, 0.25f, 0.2f, 1.3f, 1, 1, target, trigger);

                    // DOZEN → COLUMN만 누적 지급액 발동(납부금의 50%마다). 나머지는 당첨 발동.
                    if (trigger == DrinkGroup.Dozen && target == DrinkGroup.Column)
                    {
                        cross.crossTrigger = CrossTriggerMode.Gain;
                        cross.gainThresholdRatio = DefaultGainRatio;
                    }

                    items.Add(cross);
                }
            }

            items.Add(Create("payment_support", "납부 지원금", ShopCategory.PaymentSupport, ShopEffect.PaymentSupport, 0.25f, 0.15f, 1.5f, 1, 1));
            // 특수 강화(2026-10-09 소모품에서 옮김, 한 번 사면 다시 진열되지 않음).
            items.Add(Create("extra_ball", "추가 공", ShopCategory.Special, ShopEffect.ExtraBall, 1f, 1f, 1f, 1, 1));
            items.Add(Create("skull_shield", "해골 방어", ShopCategory.Special, ShopEffect.SkullShield, 1f, 0.3f, 1f, 1, 1));

            // 소모품(자동 사용).
            items.Add(Create("dealer_hint", "딜러의 귀띔", ShopCategory.Consumable, ShopEffect.DealerHint, 1f, 0.2f, 1f, 1, 1));
            items.Add(Create("double_up", "더블 업 쿠폰", ShopCategory.Consumable, ShopEffect.DoubleUp, 1.2f, 0.1f, 1f, 1, 1));
            items.Add(Create("spare_spin", "회전권", ShopCategory.Consumable, ShopEffect.SpareSpin, 1f, 0.3f, 1f, 1, 1));
            items.Add(Create("insurance", "보험 증서", ShopCategory.Consumable, ShopEffect.Insurance, 0.5f, 0.1f, 1f, 2, 2));
            return items;
        }

        private static string FocusName(DrinkGroup group)
        {
            switch (group)
            {
                case DrinkGroup.Column:
                    return "컬럼";
                case DrinkGroup.Dozen:
                    return "더즌";
                case DrinkGroup.Parity:
                    return "홀짝";
                case DrinkGroup.Color:
                    return "색상";
                case DrinkGroup.Range:
                    return "높낮이";
                default:
                    return "숫자";
            }
        }
    }
}
