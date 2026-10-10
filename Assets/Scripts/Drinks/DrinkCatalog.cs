using System.Collections.Generic;
using UnityEngine;

namespace ProjectCR
{
    /// <summary>
    /// 음료 목록과 보상 규칙(정적 데이터). 라운드를 통과할 때마다 여기서 <see cref="OfferCount"/>종을 서로 다르게 뽑아 보여 준다.
    /// 음료는 두 계열이다: 지급 강화(<see cref="Drinks"/>, 묶음 지급 배율 ×)와 확률 강화(<see cref="ProbabilityDrinks"/>, 대상 추첨 가중치 +).
    /// 후보 칸마다 계열을 <see cref="PayoutShare"/>:<see cref="ProbabilityShare"/> 비중으로 고른다.
    /// 지급 강화는 묶음마다 하나씩 있다고 본다. 같은 묶음 음료가 둘이면 앞의 것의 배율을 쓴다.
    /// </summary>
    [CreateAssetMenu(fileName = "DrinkCatalog", menuName = "ProjectCR/Drink Catalog")]
    public class DrinkCatalog : ScriptableObject
    {
        [Tooltip("지급 강화 음료.")]
        [SerializeField] private DrinkDefinition[] drinks = new DrinkDefinition[0];

        [Tooltip("확률 강화 음료.")]
        [SerializeField] private ProbabilityDrinkDefinition[] probabilityDrinks = new ProbabilityDrinkDefinition[0];

        [Tooltip("후보 칸마다 지급 강화 계열이 나올 비중.")]
        [Min(0f)]
        [SerializeField] private float payoutShare = 1f;

        [Tooltip("후보 칸마다 확률 강화 계열이 나올 비중.")]
        [Min(0f)]
        [SerializeField] private float probabilityShare = 1f;

        [Tooltip("라운드 통과 때 보여 주는 음료 수. 목록보다 많으면 목록 전체.")]
        [Min(1)]
        [SerializeField] private int offerCount = 4;

        public IReadOnlyList<DrinkDefinition> Drinks => drinks;
        public IReadOnlyList<ProbabilityDrinkDefinition> ProbabilityDrinks => probabilityDrinks;
        public int OfferCount => offerCount;
        public float PayoutShare => payoutShare;
        public float ProbabilityShare => probabilityShare;

        /// <summary>묶음의 한 잔당 배율. 그 묶음 음료가 없으면 1(강화 없음).</summary>
        public decimal BoostOf(DrinkGroup group)
        {
            DrinkDefinition drink = Find(group);
            return drink != null ? drink.BoostPerStack : 1m;
        }

        public DrinkDefinition Find(DrinkGroup group)
        {
            foreach (DrinkDefinition drink in drinks)
            {
                if (drink != null && drink.Group == group)
                {
                    return drink;
                }
            }

            return null;
        }

        /// <summary>코드로 만든다(테스트, 기본 에셋 생성).</summary>
        public static DrinkCatalog Create(IEnumerable<DrinkDefinition> drinks, int offerCount = 4,
            IEnumerable<ProbabilityDrinkDefinition> probabilityDrinks = null, float payoutShare = 1f, float probabilityShare = 1f)
        {
            var catalog = CreateInstance<DrinkCatalog>();
            catalog.name = "DrinkCatalog";
            catalog.drinks = new List<DrinkDefinition>(drinks).ToArray();
            catalog.probabilityDrinks = probabilityDrinks != null
                ? new List<ProbabilityDrinkDefinition>(probabilityDrinks).ToArray()
                : new ProbabilityDrinkDefinition[0];
            catalog.offerCount = Mathf.Max(1, offerCount);
            catalog.payoutShare = Mathf.Max(0f, payoutShare);
            catalog.probabilityShare = Mathf.Max(0f, probabilityShare);
            return catalog;
        }

        /// <summary>기본 지급 강화 6종(묶음마다 하나, 모두 ×1.5). 이름은 임시, 색은 칵테일 음료 색.</summary>
        public static DrinkDefinition[] CreateDefaultDrinks()
        {
            return new[]
            {
                DrinkDefinition.Create(DrinkGroup.Column, "컬럼 진토닉", DrinkColors.CoralOrange),
                DrinkDefinition.Create(DrinkGroup.Dozen, "더즌 위스키", DrinkColors.Amber),
                DrinkDefinition.Create(DrinkGroup.Parity, "홀짝 하이볼", DrinkColors.Emerald),
                DrinkDefinition.Create(DrinkGroup.Color, "레드 앤 블랙", DrinkColors.Ruby),
                DrinkDefinition.Create(DrinkGroup.Range, "하이로우 에일", DrinkColors.SkyBlue),
                DrinkDefinition.Create(DrinkGroup.Number, "넘버 앱생트", DrinkColors.Violet),
            };
        }
    }
}
