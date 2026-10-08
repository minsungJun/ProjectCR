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

        /// <summary>기본값(임시 밸런스): 초기 100, 최소 1, 36배, 공 1, 회전 5, 납부 120 ×1.5 (10단위 올림).</summary>
        public GameBalance Balance()
        {
            return Track(ScriptableObject.CreateInstance<GameBalance>());
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
