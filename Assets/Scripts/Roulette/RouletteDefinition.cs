using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectCR
{
    [Serializable]
    public struct PocketDefinition
    {
        [SerializeField] private int number;
        [SerializeField] private PocketColor color;

        [Tooltip("추첨 기본 가중치. 0이면 절대 나오지 않는다. 회전마다 SpinConfig로 복사되고, 아이템은 그 복사본만 고친다.")]
        [Min(0)]
        [SerializeField] private int baseWeight;

        public PocketDefinition(int number, PocketColor color, int baseWeight)
        {
            this.number = number;
            this.color = color;
            this.baseWeight = baseWeight;
        }

        public int Number => number;
        public PocketColor Color => color;
        public int BaseWeight => baseWeight;
    }

    /// <summary>
    /// 휠의 칸 배열. <b>배열 인덱스 = 휠 위의 순서</b>(위에서 볼 때 시계 방향, 0번 칸이 인덱스 0).
    ///
    /// 칸은 어디서나 이 인덱스로 가리킨다. 3D 쪽 PocketAnchor와 칸 Renderer도 같은 인덱스로 연결하고,
    /// 오브젝트 이름에서 번호를 추측하지 않는다. 번호가 필요하면 <see cref="GetPocket"/>으로 읽는다.
    ///
    /// 정적 설정이라 런타임에 고치지 않는다. 회전마다 바뀌는 값은 <see cref="SpinConfig"/>에 있다.
    /// </summary>
    [CreateAssetMenu(fileName = "RouletteDefinition", menuName = "ProjectCR/Roulette Definition")]
    public class RouletteDefinition : ScriptableObject
    {
        /// <summary>유럽식 단일 0 휠의 칸 순서(시계 방향).</summary>
        public static readonly int[] EuropeanOrder =
        {
            0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23, 10,
            5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26,
        };

        private static readonly HashSet<int> RedNumbers = new HashSet<int>
        {
            1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36,
        };

        [SerializeField] private PocketDefinition[] pockets = Array.Empty<PocketDefinition>();

        // 번호 → 칸 인덱스. 에셋 값이 바뀌면 OnValidate에서 비운다.
        private Dictionary<int, int> indexByNumber;

        public int PocketCount => pockets.Length;

        public PocketDefinition GetPocket(int pocketIndex) => pockets[pocketIndex];

        /// <summary>번호가 휠에 없으면 -1.</summary>
        public int IndexOfNumber(int number)
        {
            if (indexByNumber == null)
            {
                indexByNumber = new Dictionary<int, int>(pockets.Length);
                for (int i = 0; i < pockets.Length; i++)
                {
                    indexByNumber[pockets[i].Number] = i;
                }
            }

            return indexByNumber.TryGetValue(number, out int index) ? index : -1;
        }

        public bool ContainsNumber(int number) => IndexOfNumber(number) >= 0;

        public PocketColor ColorOfNumber(int number)
        {
            int index = IndexOfNumber(number);
            return index >= 0 ? pockets[index].Color : PocketColor.Green;
        }

        public static PocketColor StandardColorOf(int number)
        {
            if (number == 0)
            {
                return PocketColor.Green;
            }

            return RedNumbers.Contains(number) ? PocketColor.Red : PocketColor.Black;
        }

        [ContextMenu("유럽식 배열로 초기화")]
        public void ResetToEuropean()
        {
            pockets = new PocketDefinition[EuropeanOrder.Length];
            for (int i = 0; i < EuropeanOrder.Length; i++)
            {
                int number = EuropeanOrder[i];
                pockets[i] = new PocketDefinition(number, StandardColorOf(number), 1);
            }

            indexByNumber = null;
        }

        /// <summary>테스트와 에디터 셋업용. 메모리에만 있는 인스턴스를 만든다.</summary>
        public static RouletteDefinition CreateEuropean()
        {
            var definition = CreateInstance<RouletteDefinition>();
            definition.name = "RouletteDefinition_European";
            definition.ResetToEuropean();
            return definition;
        }

        /// <summary>번호 중복, 음수 가중치, 가중치 합 0을 찾는다. 문제가 없으면 빈 목록.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            var seen = new HashSet<int>();
            long totalWeight = 0;

            for (int i = 0; i < pockets.Length; i++)
            {
                if (!seen.Add(pockets[i].Number))
                {
                    errors.Add($"칸 {i}: 번호 {pockets[i].Number}가 중복된다.");
                }

                if (pockets[i].BaseWeight < 0)
                {
                    errors.Add($"칸 {i}: 가중치가 음수다.");
                }

                totalWeight += Math.Max(0, pockets[i].BaseWeight);
            }

            if (pockets.Length > 0 && totalWeight <= 0)
            {
                errors.Add("가중치 합이 0이라 추첨할 수 없다.");
            }

            return errors;
        }

        private void OnValidate()
        {
            indexByNumber = null;

            foreach (string error in Validate())
            {
                Debug.LogError($"[{name}] {error}", this);
            }
        }
    }
}
