using System;
using System.Collections.Generic;

namespace ProjectCR
{
    /// <summary>
    /// 라운드 통과 보상(음료 후보). 후보는 보상 화면을 열 때 <b>한 번만</b> 만들고, 고를 때까지 그대로 둔다.
    /// 화면을 다시 그려도 후보가 바뀌지 않는다.
    ///
    /// 후보 칸마다 계열(지급/확률)을 목록의 비중으로 고르고, 그 계열에서 아직 안 나온 음료를 하나 고른다.
    /// 한 후보 안에 같은 음료는 두 번 나오지 않는다. 이미 마신 음료도 다시 나올 수 있다.
    /// 지급 음료는 이때 배율을 올릴 대상 하나(1열, 빨강, 7번 …)를 정한다. 묶음 안의 대상은 모두 같은 확률이다.
    /// 확률 음료도 이때 대상 하나를 정한다(<see cref="DrawTarget"/>): 그 묶음에서 이미 마신 대상 n개와 "새 대상" 하나가 똑같이 1/(n+1)씩.
    /// 마신 게 없으면 묶음 안에서 아무거나, 1열을 마셨으면 1열 50% · 새 대상(2열·3열 중 하나) 50%,
    /// 숫자 7과 12를 마셨으면 7 · 12 · 새 숫자 1/3씩. 새 대상은 마신 적 없는 대상 중에서 고른다.
    ///
    /// 후보 추첨은 결과 추첨과 다른 난수를 쓴다. 보상을 뽑아도 회전 결과 순서가 바뀌지 않고,
    /// 같은 시드면 같은 후보가 나온다.
    /// </summary>
    public sealed class DrinkRewardController
    {
        private readonly DrinkCatalog catalog;
        private readonly Random rng;
        private List<DrinkOffer> offer;

        /// <summary>이미 마신 확률 음료의 대상(처음 마신 순서, 모든 묶음). GameFlowController가 채운다. 비우면 없음.</summary>
        public Func<IReadOnlyList<BetTarget>> DrunkTargets;

        public DrinkRewardController(DrinkCatalog catalog, int seed)
        {
            this.catalog = catalog;
            rng = new Random(seed);
        }

        public bool HasCatalog => catalog != null && (catalog.Drinks.Count > 0 || catalog.ProbabilityDrinks.Count > 0);

        /// <summary>지금 보여 주는 후보. 없으면 null.</summary>
        public IReadOnlyList<DrinkOffer> CurrentOffer => offer;

        /// <summary>후보를 만든다. 이미 있으면 그대로 돌려준다.</summary>
        public IReadOnlyList<DrinkOffer> CreateOffer()
        {
            if (offer != null)
            {
                return offer;
            }

            var payouts = Distinct(catalog?.Drinks);
            var probabilities = Distinct(catalog?.ProbabilityDrinks);
            int count = Math.Min(catalog != null ? catalog.OfferCount : 0, payouts.Count + probabilities.Count);

            offer = new List<DrinkOffer>(count);
            for (int i = 0; i < count; i++)
            {
                double payoutWeight = payouts.Count > 0 ? Math.Max(0f, catalog.PayoutShare) : 0d;
                double probabilityWeight = probabilities.Count > 0 ? Math.Max(0f, catalog.ProbabilityShare) : 0d;

                // 비중이 둘 다 0이면 남은 쪽에서 고른다.
                bool pickProbability = payoutWeight + probabilityWeight > 0d
                    ? rng.NextDouble() * (payoutWeight + probabilityWeight) >= payoutWeight
                    : payouts.Count == 0;

                if (pickProbability)
                {
                    ProbabilityDrinkDefinition chance = Take(probabilities);
                    offer.Add(DrinkOffer.ForProbability(chance, DrawTarget(chance.Group, DrunkTargets?.Invoke(), rng)));
                }
                else
                {
                    DrinkDefinition drink = Take(payouts);
                    List<BetTarget> targets = drink.Group.Targets();
                    offer.Add(DrinkOffer.ForPayout(drink, targets[rng.Next(targets.Count)]));
                }
            }

            return offer;
        }

        /// <summary>후보 중 index번째를 고른다. 후보가 없거나 범위를 벗어나면 null. 고르면 후보를 비운다.</summary>
        public DrinkOffer Take(int index)
        {
            if (offer == null || index < 0 || index >= offer.Count)
            {
                return null;
            }

            DrinkOffer picked = offer[index];
            offer = null;
            return picked;
        }

        /// <summary>
        /// 확률 음료의 대상: 그 묶음에서 마신 대상(겹치지 않게) n개와 새 대상 자리 하나 중 똑같은 확률로 고른다.
        /// 새 대상은 그 묶음에서 마신 적 없는 대상 중 하나. 묶음의 대상을 다 마셨으면 마신 대상 중에서만.
        /// 다른 묶음의 대상(빨강을 마셨는데 컬럼 음료)은 상관없다.
        /// </summary>
        public static BetTarget DrawTarget(DrinkGroup group, IReadOnlyList<BetTarget> drunk, Random random)
        {
            List<BetTarget> options = group.Targets();
            var known = new List<BetTarget>();
            if (drunk != null)
            {
                foreach (BetTarget target in drunk)
                {
                    if (options.Contains(target) && !known.Contains(target))
                    {
                        known.Add(target);
                    }
                }
            }

            var fresh = options.FindAll(target => !known.Contains(target));

            int slots = known.Count + (fresh.Count > 0 ? 1 : 0);
            int pick = random.Next(slots);
            return pick < known.Count ? known[pick] : fresh[random.Next(fresh.Count)];
        }

        private T Take<T>(List<T> pool)
        {
            int index = rng.Next(pool.Count);
            T item = pool[index];
            pool.RemoveAt(index);
            return item;
        }

        private static List<T> Distinct<T>(IReadOnlyList<T> source) where T : UnityEngine.Object
        {
            var list = new List<T>();
            if (source == null)
            {
                return list;
            }

            foreach (T item in source)
            {
                if (item != null && !list.Contains(item))
                {
                    list.Add(item);
                }
            }

            return list;
        }
    }
}
