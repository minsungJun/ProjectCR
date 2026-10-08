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

        /// <summary>소모품: 자동 사용(추가 공·회전권은 사는 즉시, 방패·보험은 필요할 때 저절로).</summary>
        Consumable,
    }

    public enum ShopEffect
    {
        Focus,
        Cross,
        PaymentSupport,

        /// <summary>이번 라운드 남은 회전에 공 +1.</summary>
        ExtraBall,

        /// <summary>장착해 두면 해골이 나온 공 하나를 다시 뽑는다.</summary>
        SkullShield,

        /// <summary>이번 라운드 남은 회전 +1.</summary>
        SpareSpin,

        /// <summary>다음 회전이 일반 낙첨이면 베팅금 일부 환급.</summary>
        Insurance,
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
        public int StockPerRound => stockPerRound;
        public int BuyLimitPerRound => buyLimitPerRound;

        /// <summary>효과 크기(decimal로 정확히). 0.1f → 0.1.</summary>
        public decimal Value => (decimal)Mathf.Max(0f, value);

        public bool IsPermanent => category == ShopCategory.Focus || category == ShopCategory.Cross || category == ShopCategory.PaymentSupport;

        /// <summary>대상을 골라야 살 수 있다(집중 강화).</summary>
        public bool NeedsTarget => effect == ShopEffect.Focus;

        public string Description => string.IsNullOrWhiteSpace(description) ? DefaultDescription() : description;

        /// <summary>상점 카드의 짧은 설명(두 줄).</summary>
        public string CardText => string.IsNullOrWhiteSpace(cardText) ? DefaultCardText() : cardText;

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
                        return "이번 게임 동안 보유 · 발동하면 다음 회전 1번";
                    case ShopEffect.PaymentSupport:
                        return "이번 게임 동안 · 구매 즉시 적용 · 납부할 때마다 환급";
                    case ShopEffect.ExtraBall:
                        return "자동 사용 · 구매 즉시 이번 라운드 끝까지";
                    case ShopEffect.SkullShield:
                        return "자동 사용 · 해골이 나오면 1개씩";
                    case ShopEffect.Insurance:
                        return "자동 사용 · 다음 회전부터 회전마다 1장씩";
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

        private string DefaultCardText()
        {
            string percent = (Value * 100m).ToString("0.#") + "%";
            switch (effect)
            {
                case ShopEffect.Focus:
                    return $"고른 대상 확률 +{percent}\n{group.ToKorean()} 지급액 +{percent}";
                case ShopEffect.Cross:
                    return $"{GroupCode(triggerGroup)} 당첨 시\n다음 회전 {GroupCode(group)} +{percent}";
                case ShopEffect.PaymentSupport:
                    return $"납부할 때마다\n납부금 {percent} 환급";
                case ShopEffect.ExtraBall:
                    return "이번 라운드\n공 1개 추가";
                case ShopEffect.SkullShield:
                    return "해골이 나오면\n해당 공 재회전";
                case ShopEffect.SpareSpin:
                    return "남은 회전\n+1회";
                default:
                    return $"일반 낙첨 시\n베팅금 {percent} 환급";
            }
        }

        private string DefaultDescription()
        {
            string percent = (Value * 100m).ToString("0.#") + "%";
            switch (effect)
            {
                case ShopEffect.Focus:
                    return $"고른 {group.ToKorean()} 대상 추첨 가중치 +{percent}, {group.BetsKorean()} 베팅 지급액 +{percent}";
                case ShopEffect.Cross:
                    return $"{GroupCode(triggerGroup)} 베팅이 당첨되면 다음 회전 {GroupCode(group)} 지급액 +{percent} (회전당 1번)";
                case ShopEffect.PaymentSupport:
                    return $"라운드가 끝나 납부할 때마다 낸 납부금의 {percent}를 돌려받습니다(납부금은 그대로). 여러 개면 더해집니다";
                case ShopEffect.ExtraBall:
                    return "이번 라운드 남은 모든 회전에 공 +1 (여러 개 쓰면 누적)";
                case ShopEffect.SkullShield:
                    return "해골이 나오면 그 공 하나를 다시 굴립니다(방패 1개 소모). 다른 공은 그대로";
                case ShopEffect.SpareSpin:
                    return "이번 라운드 남은 회전 +1";
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
            item.tint = category == ShopCategory.Consumable || category == ShopCategory.PaymentSupport
                ? new Color(0.78f, 0.6f, 0.3f)
                : DrinkColors.Of(group);
            return item;
        }

        /// <summary>
        /// 기본 41종(첫 시험값): 집중 6(N 10%, 납부금의 30% × 1.5^산 수), 교차 30(N 25%, 20% × 1.3^산 수),
        /// 납부 지원 1(납부할 때마다 25% 환급, 15% × 1.5^산 수), 추가 공(35%), 해골 방패(25%), 여분의 회전권(30%), 보험 증서(50% 환급, 10%).
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

                    items.Add(Create($"cross_{trigger.ToString().ToLowerInvariant()}_{target.ToString().ToLowerInvariant()}",
                        $"{GroupCode(trigger)} → {GroupCode(target)}",
                        ShopCategory.Cross, ShopEffect.Cross, 0.25f, 0.2f, 1.3f, 1, 1, target, trigger));
                }
            }

            items.Add(Create("payment_support", "납부 지원금", ShopCategory.PaymentSupport, ShopEffect.PaymentSupport, 0.25f, 0.15f, 1.5f, 1, 1));
            items.Add(Create("extra_ball", "추가 공", ShopCategory.Consumable, ShopEffect.ExtraBall, 1f, 0.35f, 1f, 2, 2));
            items.Add(Create("skull_shield", "해골 방패", ShopCategory.Consumable, ShopEffect.SkullShield, 1f, 0.25f, 1f, 2, 2));
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
