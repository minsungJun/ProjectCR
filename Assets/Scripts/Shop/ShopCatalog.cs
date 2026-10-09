using System.Collections.Generic;
using UnityEngine;

namespace ProjectCR
{
    /// <summary>
    /// 판매 가능한 상품 목록과 진열 규칙(정적 데이터).
    /// 라운드마다 진열을 한 번 만든다. 상점 창은 [소모품] / [강화] 두 탭이다.
    /// - 소모품 탭: 소모품 전부(showAllConsumables, 기본). 끄면 예전처럼 고정 1칸 + 무작위 칸.
    /// - 강화 탭: 지속 효과 상품(집중·교차·납부 지원·특수 강화)을 분류 구분 없이 모두 섞어 upgradeSlots(4)개를 뽑는다(2026-10-09 사용자 요청).
    ///   상품마다 같은 확률이라 종류가 많은 교차 강화(30종)가 자주 나온다.
    /// </summary>
    [CreateAssetMenu(fileName = "ShopCatalog", menuName = "ProjectCR/Shop Catalog")]
    public class ShopCatalog : ScriptableObject
    {
        [SerializeField] private ShopItemDefinition[] items = new ShopItemDefinition[0];

        [Tooltip("상점 창의 돈(코인) 아이콘. 비우면 금색 원으로 대신 그린다.")]
        [SerializeField] private Sprite moneyIcon;

        [Header("진열")]
        [Tooltip("강화 탭 칸 수. 집중·교차·납부 지원을 모두 섞어서 이만큼 뽑는다(이미 산 지속 효과는 빠진다).")]
        [Min(0)]
        [SerializeField] private int upgradeSlots = 4;

        [Tooltip("소모품 탭에 소모품을 전부 진열한다. 끄면 아래 두 설정(고정 칸, 무작위 칸)을 쓴다.")]
        [SerializeField] private bool showAllConsumables = true;

        [Tooltip("추가 공·해골 방패 중 하나를 늘 진열하는 칸.")]
        [SerializeField] private bool keyConsumableSlot = true;

        [Min(0)]
        [SerializeField] private int consumableSlots = 1;

        [Header("열리는 때")]
        [Tooltip("1라운드 시작(첫 회전 전)에도 상점을 연다. 끄면 라운드를 하나 통과한 뒤부터 열린다.")]
        [SerializeField] private bool openOnFirstRound;

        public IReadOnlyList<ShopItemDefinition> Items => items;
        public Sprite MoneyIcon => moneyIcon;
        public bool ShowAllConsumables => showAllConsumables;
        public int UpgradeSlots => upgradeSlots;
        public bool KeyConsumableSlot => keyConsumableSlot;
        public int ConsumableSlots => consumableSlots;
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
        public static ShopCatalog Create(IEnumerable<ShopItemDefinition> items, bool openOnFirstRound = false, int upgradeSlots = 4)
        {
            var catalog = CreateInstance<ShopCatalog>();
            catalog.name = "ShopCatalog";
            catalog.items = new List<ShopItemDefinition>(items).ToArray();
            catalog.openOnFirstRound = openOnFirstRound;
            catalog.upgradeSlots = upgradeSlots;
            return catalog;
        }
    }
}
