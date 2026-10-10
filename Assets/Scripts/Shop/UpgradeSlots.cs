using System;
using System.Collections.Generic;

namespace ProjectCR
{
    /// <summary>보유 강화 한 칸: 산 상품과 (집중 강화면) 고른 대상.</summary>
    public readonly struct OwnedUpgrade
    {
        public OwnedUpgrade(ShopItemDefinition item, BetTarget target)
        {
            Item = item;
            Target = target;
        }

        public ShopItemDefinition Item { get; }

        /// <summary>집중 강화 대상. 다른 강화는 None.</summary>
        public BetTarget Target { get; }

        public bool IsEmpty => Item == null;
    }

    /// <summary>
    /// 보유 강화(지속 효과 상품: 집중·교차·납부 지원·특수) 칸. 테이블의 원(배지 자리)과 칸 번호가 1:1이다.
    /// 칸은 자리가 고정이다: 새 강화는 첫 빈칸에, 버리고 사면 버린 칸에 들어가고, 빠진 칸은 비워 둔다(다른 배지가 옮겨 가지 않는다).
    /// 칸이 다 차면 강화를 살 때 하나를 골라 버려야 한다(<see cref="ShopController.Buy"/>). 효과를 더하고 빼는 건 ShopController가 한다.
    /// </summary>
    public sealed class UpgradeSlots
    {
        private readonly OwnedUpgrade[] slots;

        public UpgradeSlots(int capacity)
        {
            slots = new OwnedUpgrade[Math.Max(1, capacity)];
        }

        public int Capacity => slots.Length;

        /// <summary>칸 번호 순서(빈칸은 IsEmpty).</summary>
        public IReadOnlyList<OwnedUpgrade> Slots => slots;

        public int Count
        {
            get
            {
                int count = 0;
                foreach (OwnedUpgrade slot in slots)
                {
                    if (!slot.IsEmpty)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public bool IsFull => Count >= Capacity;

        public bool IsOccupied(int index) => index >= 0 && index < slots.Length && !slots[index].IsEmpty;

        /// <summary>그 효과를 가진 첫 칸. 없으면 -1.</summary>
        public int IndexOf(ShopEffect effect)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (!slots[i].IsEmpty && slots[i].Item.Effect == effect)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>첫 빈칸에 넣고 칸 번호를 돌려준다. 다 찼으면 -1.</summary>
        internal int Add(ShopItemDefinition item, BetTarget target)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].IsEmpty)
                {
                    slots[i] = new OwnedUpgrade(item, target);
                    return i;
                }
            }

            return -1;
        }

        /// <summary>그 칸에 넣는다(버린 칸에 새 강화를 둘 때).</summary>
        internal void Set(int index, ShopItemDefinition item, BetTarget target)
        {
            slots[index] = new OwnedUpgrade(item, target);
        }

        /// <summary>그 칸을 비우고 들어 있던 강화를 돌려준다.</summary>
        internal OwnedUpgrade Clear(int index)
        {
            OwnedUpgrade removed = slots[index];
            slots[index] = default;
            return removed;
        }
    }
}
