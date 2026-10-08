using System.Collections.Generic;
using UnityEngine;

namespace ProjectCR
{
    /// <summary>
    /// 판매 가능한 상품 목록과 진열 규칙(정적 데이터).
    /// 라운드마다 진열을 한 번 만든다: 분류를 먼저 정하고 그 안에서 뽑아서, 30종인 교차 강화가 진열을 덮지 않게 한다.
    /// 기본 진열 7칸: 집중 2, 교차 2, 고정 소모품 1(추가 공·해골 방패 중 하나), 소모품 1, 납부 지원(확률, 안 나오면 소모품).
    /// </summary>
    [CreateAssetMenu(fileName = "ShopCatalog", menuName = "ProjectCR/Shop Catalog")]
    public class ShopCatalog : ScriptableObject
    {
        [SerializeField] private ShopItemDefinition[] items = new ShopItemDefinition[0];

        [Header("진열")]
        [Min(0)]
        [SerializeField] private int focusSlots = 2;

        [Min(0)]
        [SerializeField] private int crossSlots = 2;

        [Tooltip("추가 공·해골 방패 중 하나를 늘 진열하는 칸.")]
        [SerializeField] private bool keyConsumableSlot = true;

        [Min(0)]
        [SerializeField] private int consumableSlots = 1;

        [Header("열리는 때")]
        [Tooltip("1라운드 시작(첫 회전 전)에도 상점을 연다. 끄면 라운드를 하나 통과한 뒤부터 열린다.")]
        [SerializeField] private bool openOnFirstRound;

        [Tooltip("납부 지원금이 진열될 확률. 안 나오면 소모품 한 칸으로 채운다.")]
        [Range(0f, 1f)]
        [SerializeField] private float paymentSupportChance = 0.5f;

        public IReadOnlyList<ShopItemDefinition> Items => items;
        public int FocusSlots => focusSlots;
        public int CrossSlots => crossSlots;
        public bool KeyConsumableSlot => keyConsumableSlot;
        public int ConsumableSlots => consumableSlots;
        public float PaymentSupportChance => paymentSupportChance;
        public bool OpenOnFirstRound => openOnFirstRound;

        public List<ShopItemDefinition> ItemsOf(ShopCategory category)
        {
            var list = new List<ShopItemDefinition>();
            foreach (ShopItemDefinition item in items)
            {
                if (item != null && item.Category == category && !list.Contains(item))
                {
                    list.Add(item);
                }
            }

            return list;
        }

        /// <summary>코드로 만든다(테스트, 기본 에셋 생성).</summary>
        public static ShopCatalog Create(IEnumerable<ShopItemDefinition> items, float paymentSupportChance = 0.5f, bool openOnFirstRound = false)
        {
            var catalog = CreateInstance<ShopCatalog>();
            catalog.name = "ShopCatalog";
            catalog.items = new List<ShopItemDefinition>(items).ToArray();
            catalog.paymentSupportChance = paymentSupportChance;
            catalog.openOnFirstRound = openOnFirstRound;
            return catalog;
        }
    }
}
