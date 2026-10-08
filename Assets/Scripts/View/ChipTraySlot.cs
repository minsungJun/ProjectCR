using UnityEngine;

namespace ProjectCR
{
    /// <summary>칩 트레이의 칸 하나. 누르면 이 금액이 칩 금액이 된다. 콜라이더와 같이 둔다.</summary>
    [RequireComponent(typeof(Collider))]
    public class ChipTraySlot : MonoBehaviour
    {
        [SerializeField] private long value;

        public long Value => value;

        public void Configure(long chipValue) => value = chipValue;
    }
}
