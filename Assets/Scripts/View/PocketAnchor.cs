using UnityEngine;

namespace ProjectCR
{
    /// <summary>
    /// 칸 하나의 공 안착점. WheelPivot 아래에 있어 휠과 같이 돈다.
    /// 로컬 +Z가 휠 바깥(반경 방향)을 향하도록 놓는다.
    ///
    /// 번호와 칸 인덱스는 빌더가 RouletteDefinition에서 읽어 넣는다. 오브젝트 이름은 읽기 편하라고 붙일 뿐이다.
    /// </summary>
    public class PocketAnchor : MonoBehaviour
    {
        [Tooltip("RouletteDefinition 칸 인덱스. 빌더가 넣는다. 손으로 고치지 않는다.")]
        [SerializeField] private int pocketIndex;

        [Tooltip("검증용 사본. RoulettePresenter가 시작할 때 정의와 맞는지 확인한다.")]
        [SerializeField] private int number;

        public int PocketIndex => pocketIndex;
        public int Number => number;

        public void Configure(int index, int pocketNumber)
        {
            pocketIndex = index;
            number = pocketNumber;
        }

        /// <summary>
        /// 같은 칸의 slot번째 공 위치(앵커 로컬). 0은 중심, 1은 안쪽, 2는 바깥쪽, 그 뒤로는 위로 쌓는다.
        /// </summary>
        public static Vector3 GetSlotOffset(int slot, float ballRadius)
        {
            float step = ballRadius * 2.1f;
            switch (slot)
            {
                case 0:
                    return Vector3.zero;
                case 1:
                    return new Vector3(0f, 0f, -step);
                case 2:
                    return new Vector3(0f, 0f, step);
                default:
                    return new Vector3(0f, step * 0.8f * (slot - 2), 0f);
            }
        }
    }
}
