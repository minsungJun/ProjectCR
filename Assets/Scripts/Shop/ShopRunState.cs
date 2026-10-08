using System;
using System.Collections.Generic;

namespace ProjectCR
{
    /// <summary>진열 칸 하나: 상품, 이번 라운드 남은 재고, 이번 라운드 산 수.</summary>
    public sealed class ShopOffer
    {
        public ShopOffer(ShopItemDefinition item)
        {
            Item = item;
            Stock = item.StockPerRound;
        }

        public ShopItemDefinition Item { get; }
        public int Stock { get; internal set; }
        public int BoughtThisRound { get; internal set; }

        public bool SoldOut => Stock <= 0;
        public bool LimitReached => BoughtThisRound >= Item.BuyLimitPerRound;
    }

    /// <summary>
    /// 이번 게임의 상점 상태: 라운드 진열·재고, 상품별 누적 구매 수(가격 성장용).
    /// 진열은 라운드마다 한 번만 만든다. 상점을 닫았다 다시 열어도 같은 진열·재고다.
    /// 지속 효과 상품(집중·교차 강화, 납부 지원금)은 한 번 사면 이후 진열에 다시 나오지 않는다(<see cref="IsRetired"/>).
    /// 진열 추첨은 결과·보상과 다른 난수를 써서 회전 결과 순서를 바꾸지 않는다.
    /// </summary>
    public sealed class ShopRunState
    {
        private readonly ShopCatalog catalog;
        private readonly Random rng;
        private readonly List<ShopOffer> offers = new List<ShopOffer>();
        private readonly Dictionary<ShopItemDefinition, int> bought = new Dictionary<ShopItemDefinition, int>();

        public ShopRunState(ShopCatalog catalog, int seed)
        {
            this.catalog = catalog;
            rng = new Random(seed);
        }

        public bool HasCatalog => catalog != null && catalog.Items.Count > 0;

        public bool OpenOnFirstRound => catalog != null && catalog.OpenOnFirstRound;

        /// <summary>정적 상품 목록(아이콘 등 표시용).</summary>
        public ShopCatalog Catalog => catalog;

        /// <summary>진열을 만든 라운드. 0이면 아직 없음.</summary>
        public int DisplayRound { get; private set; }

        public IReadOnlyList<ShopOffer> Offers => offers;

        /// <summary>이번 게임에서 이 상품을 산 수(라운드와 무관).</summary>
        public int BoughtTotal(ShopItemDefinition item) => item != null && bought.TryGetValue(item, out int count) ? count : 0;

        /// <summary>지속 효과 상품을 이미 샀다. 다음 진열부터 빠진다(산 라운드에는 "구매 완료"로 남는다).</summary>
        public bool IsRetired(ShopItemDefinition item) => item != null && item.IsPermanent && BoughtTotal(item) > 0;

        internal void RecordPurchase(ShopOffer offer)
        {
            offer.Stock--;
            offer.BoughtThisRound++;
            bought[offer.Item] = BoughtTotal(offer.Item) + 1;
        }

        /// <summary>그 라운드 진열이 아직 없으면 만든다. 이미 있으면 그대로(다시 뽑지 않는다).</summary>
        public IReadOnlyList<ShopOffer> EnsureDisplay(int round)
        {
            if (DisplayRound == round || !HasCatalog)
            {
                return offers;
            }

            offers.Clear();
            DisplayRound = round;

            // 강화 탭: 집중·교차·납부 지원을 한 묶음으로 섞어서 뽑는다(상품마다 같은 확률, 이미 산 것은 빠짐).
            var used = new HashSet<ShopItemDefinition>();
            List<ShopItemDefinition> upgrades = catalog.ItemsOf(ShopCategory.Focus);
            upgrades.AddRange(catalog.ItemsOf(ShopCategory.Cross));
            upgrades.AddRange(catalog.ItemsOf(ShopCategory.PaymentSupport));
            AddFrom(upgrades, catalog.UpgradeSlots, used);

            List<ShopItemDefinition> consumables = catalog.ItemsOf(ShopCategory.Consumable);
            if (catalog.ShowAllConsumables)
            {
                // 소모품 탭: 전부, 목록 순서대로.
                foreach (ShopItemDefinition item in consumables)
                {
                    used.Add(item);
                    offers.Add(new ShopOffer(item));
                }
            }
            else
            {
                if (catalog.KeyConsumableSlot)
                {
                    var key = consumables.FindAll(i => i.Effect == ShopEffect.ExtraBall || i.Effect == ShopEffect.SkullShield);
                    AddFrom(key, 1, used);
                }

                AddFrom(consumables, catalog.ConsumableSlots, used);
            }

            return offers;
        }

        /// <summary>목록에서 아직 진열하지 않은 상품을 count개 뽑아 진열한다(모자라면 있는 만큼).</summary>
        private void AddFrom(List<ShopItemDefinition> source, int count, HashSet<ShopItemDefinition> used)
        {
            var pool = source.FindAll(i => !used.Contains(i) && !IsRetired(i));
            for (int n = 0; n < count && pool.Count > 0; n++)
            {
                int index = rng.Next(pool.Count);
                ShopItemDefinition item = pool[index];
                pool.RemoveAt(index);
                used.Add(item);
                offers.Add(new ShopOffer(item));
            }
        }
    }
}
