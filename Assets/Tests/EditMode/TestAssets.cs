using System.Collections.Generic;
using UnityEngine;

namespace ProjectCR.Tests
{
    /// <summary>테스트용 설정 에셋. 메모리에만 만들고 TearDown에서 지운다.</summary>
    internal sealed class TestAssets
    {
        private readonly List<Object> created = new List<Object>();

        /// <summary>게임에서 쓰는 38칸 해골 휠.</summary>
        public RouletteDefinition Definition()
        {
            return Track(RouletteDefinition.CreateSkullWheel());
        }

        /// <summary>해골 없는 37칸 유럽식 휠. 비교용.</summary>
        public RouletteDefinition European()
        {
            return Track(RouletteDefinition.CreateEuropean());
        }

        /// <summary>기본값(임시 밸런스): 초기 100, 최소 1, 36배, 공 1, 회전 5, 납부 120 ×2.5 (5단위 올림).</summary>
        public GameBalance Balance()
        {
            return Track(ScriptableObject.CreateInstance<GameBalance>());
        }

        /// <summary>기본 지급 음료 6종(묶음마다 하나, 한 잔 ×1.5), 후보 offerCount종. withProbability면 확률 음료 6종도 넣는다.</summary>
        public DrinkCatalog Drinks(int offerCount = 4, bool withProbability = false)
        {
            var drinks = DrinkCatalog.CreateDefaultDrinks();
            foreach (DrinkDefinition drink in drinks)
            {
                Track(drink);
            }

            ProbabilityDrinkDefinition[] chances = withProbability ? ProbabilityDrinkDefinition.CreateDefaults() : new ProbabilityDrinkDefinition[0];
            foreach (ProbabilityDrinkDefinition drink in chances)
            {
                Track(drink);
            }

            return Track(DrinkCatalog.Create(drinks, offerCount, chances));
        }

        /// <summary>
        /// 기본 상점 41종 목록. supportChance로 납부 지원금 진열 확률을 정한다.
        /// 테스트 편의상 기본은 1라운드에도 열린다(게임 에셋은 1라운드에 닫혀 있다).
        /// </summary>
        public ShopCatalog Shop(float supportChance = 0.5f, bool openOnFirstRound = true)
        {
            var items = ShopItemDefinition.CreateDefaults();
            foreach (ShopItemDefinition item in items)
            {
                Track(item);
            }

            return Track(ShopCatalog.Create(items, supportChance, openOnFirstRound));
        }

        public void DestroyAll()
        {
            foreach (Object asset in created)
            {
                Object.DestroyImmediate(asset);
            }

            created.Clear();
        }

        private T Track<T>(T asset) where T : Object
        {
            created.Add(asset);
            return asset;
        }
    }
}
