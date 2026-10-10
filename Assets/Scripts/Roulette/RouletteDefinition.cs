using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectCR
{
    [Serializable]
    public struct PocketDefinition
    {
        [SerializeField] private PocketKind kind;
        [SerializeField] private int number;
        [SerializeField] private PocketColor color;

        [Tooltip("추첨 기본 가중치. 0이면 절대 나오지 않는다. 회전마다 SpinConfig로 복사되고, 아이템은 그 복사본만 고친다.")]
        [Min(0)]
        [SerializeField] private int baseWeight;

        public PocketDefinition(int number, PocketColor color, int baseWeight)
        {
            kind = PocketKind.Number;
            this.number = number;
            this.color = color;
            this.baseWeight = baseWeight;
        }

        /// <summary>해골 칸. 번호는 <see cref="RouletteDefinition.SkullNumber"/>, 색은 없다.</summary>
        public static PocketDefinition Skull(int baseWeight)
        {
            return new PocketDefinition(RouletteDefinition.SkullNumber, PocketColor.None, baseWeight) { kind = PocketKind.Skull };
        }

        public PocketKind Kind => kind;
        public bool IsSkull => kind == PocketKind.Skull;

        /// <summary>해골 칸이면 <see cref="RouletteDefinition.SkullNumber"/>. 번호 계산(홀짝 등)에 쓰기 전에 IsSkull부터 볼 것.</summary>
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
    /// 해골 칸은 번호 조회(<see cref="IndexOfNumber"/>, <see cref="ContainsNumber"/>)에서 빠진다.
    /// 그래서 번호를 고르는 경로로는 해골에 베팅할 수 없다.
    ///
    /// 정적 설정이라 런타임에 고치지 않는다. 회전마다 바뀌는 값은 <see cref="SpinConfig"/>에 있다.
    /// </summary>
    [CreateAssetMenu(fileName = "RouletteDefinition", menuName = "ProjectCR/Roulette Definition")]
    public class RouletteDefinition : ScriptableObject
    {
        /// <summary>
        /// 해골 칸의 번호 자리. 베팅 선택 없음(-1)과 겹치지 않게 -2를 쓴다.
        /// 디버그 지정 번호에서도 이 값이 해골을 뜻한다.
        /// </summary>
        public const int SkullNumber = -2;

        /// <summary>유럽식 단일 0 휠의 칸 순서(시계 방향).</summary>
        public static readonly int[] EuropeanOrder =
        {
            0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23, 10,
            5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26,
        };

        /// <summary>
        /// 해골 휠(38칸). 유럽식 순서에서 0의 정반대인 10과 5 사이에 해골을 넣었다.
        /// 해골이 인덱스 19, 0에서 정확히 180°다(콘셉트 시안과 같다).
        /// </summary>
        public static readonly int[] SkullWheelOrder =
        {
            0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23, 10,
            SkullNumber,
            5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26,
        };

        private static readonly HashSet<int> RedNumbers = new HashSet<int>
        {
            1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36,
        };

        [SerializeField] private PocketDefinition[] pockets = Array.Empty<PocketDefinition>();

        // 번호 → 칸 인덱스(번호 칸만). 에셋 값이 바뀌면 OnValidate에서 비운다.
        private Dictionary<int, int> indexByNumber;

        public int PocketCount => pockets.Length;

        public PocketDefinition GetPocket(int pocketIndex) => pockets[pocketIndex];

        public bool IsSkull(int pocketIndex) => pockets[pocketIndex].IsSkull;

        /// <summary>번호 칸 중에서 찾는다. 휠에 없거나 해골이면 -1.</summary>
        public int IndexOfNumber(int number)
        {
            if (indexByNumber == null)
            {
                indexByNumber = new Dictionary<int, int>(pockets.Length);
                for (int i = 0; i < pockets.Length; i++)
                {
                    if (!pockets[i].IsSkull)
                    {
                        indexByNumber[pockets[i].Number] = i;
                    }
                }
            }

            return indexByNumber.TryGetValue(number, out int index) ? index : -1;
        }

        /// <summary>베팅할 수 있는 번호인가. 해골은 false.</summary>
        public bool ContainsNumber(int number) => IndexOfNumber(number) >= 0;

        /// <summary>첫 번째 해골 칸 인덱스. 없으면 -1.</summary>
        public int IndexOfSkull()
        {
            for (int i = 0; i < pockets.Length; i++)
            {
                if (pockets[i].IsSkull)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// 결과를 직접 지정할 때 쓰는 조회. 번호면 그 칸, <see cref="SkullNumber"/>면 해골 칸, 그 밖은 -1.
        /// 베팅 검증에는 쓰지 않는다(해골이 걸리면 안 된다). 베팅은 <see cref="ContainsNumber"/>로 본다.
        /// </summary>
        public int IndexOfPocketFor(int number)
        {
            return number == SkullNumber ? IndexOfSkull() : IndexOfNumber(number);
        }

        public PocketColor ColorOfNumber(int number)
        {
            int index = IndexOfNumber(number);
            return index >= 0 ? pockets[index].Color : PocketColor.None;
        }

        public static PocketColor StandardColorOf(int number)
        {
            if (number == 0)
            {
                return PocketColor.Green;
            }

            return RedNumbers.Contains(number) ? PocketColor.Red : PocketColor.Black;
        }

        [ContextMenu("유럽식 배열로 초기화 (37칸)")]
        public void ResetToEuropean() => ResetToOrder(EuropeanOrder);

        [ContextMenu("해골 휠로 초기화 (38칸)")]
        public void ResetToSkullWheel() => ResetToOrder(SkullWheelOrder);

        /// <summary>테스트와 에디터 셋업용. 메모리에만 있는 인스턴스를 만든다.</summary>
        public static RouletteDefinition CreateEuropean()
        {
            var definition = CreateInstance<RouletteDefinition>();
            definition.name = "RouletteDefinition_European";
            definition.ResetToEuropean();
            return definition;
        }

        /// <summary>테스트와 에디터 셋업용. 메모리에만 있는 인스턴스를 만든다.</summary>
        public static RouletteDefinition CreateSkullWheel()
        {
            var definition = CreateInstance<RouletteDefinition>();
            definition.name = "RouletteDefinition_Skull38";
            definition.ResetToSkullWheel();
            return definition;
        }

        /// <summary>번호 중복, 해골 칸 형식, 음수 가중치, 가중치 합 0을 찾는다. 문제가 없으면 빈 목록.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            var seen = new HashSet<int>();
            long totalWeight = 0;

            for (int i = 0; i < pockets.Length; i++)
            {
                PocketDefinition pocket = pockets[i];

                if (pocket.IsSkull)
                {
                    if (pocket.Number != SkullNumber || pocket.Color != PocketColor.None)
                    {
                        errors.Add($"칸 {i}: 해골 칸은 번호 {SkullNumber}, 색 None이어야 한다.");
                    }
                }
                else
                {
                    if (!seen.Add(pocket.Number))
                    {
                        errors.Add($"칸 {i}: 번호 {pocket.Number}가 중복된다.");
                    }

                    if (pocket.Number < 0 || pocket.Color == PocketColor.None)
                    {
                        errors.Add($"칸 {i}: 번호 칸에 음수 번호나 색 None을 쓸 수 없다.");
                    }
                }

                if (pocket.BaseWeight < 0)
                {
                    errors.Add($"칸 {i}: 가중치가 음수다.");
                }

                totalWeight += Math.Max(0, pocket.BaseWeight);
            }

            if (pockets.Length > 0 && totalWeight <= 0)
            {
                errors.Add("가중치 합이 0이라 추첨할 수 없다.");
            }

            return errors;
        }

        private void ResetToOrder(int[] order)
        {
            pockets = new PocketDefinition[order.Length];
            for (int i = 0; i < order.Length; i++)
            {
                int number = order[i];
                pockets[i] = number == SkullNumber
                    ? PocketDefinition.Skull(1)
                    : new PocketDefinition(number, StandardColorOf(number), 1);
            }

            indexByNumber = null;
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
