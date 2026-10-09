using System.Collections.Generic;
using UnityEngine;

namespace ProjectCR
{
    /// <summary>
    /// 보유 강화(GameFlowController.OwnedUpgrades)를 테이블의 원(CRT_ObjectSlots_6) 위 배지로 보여 준다. 강화 칸 i = 원 i.
    /// 칸에 강화가 들어오면 그 원에 배지가 내려앉고, 빠지면(교체로 버림, 해골 방어 사용) 사라진다.
    /// 마우스를 올린 배지는 조금 떠오른다(툴팁은 RouletteGame이 HUD에 띄운다).
    /// 상태를 읽어 그리기만 한다. 원 자리는 원 렌더러의 경계 가운데(바닥 인쇄 높이)에서 매번 읽어서, 씬에서 원을 옮기면 그대로 따른다.
    /// </summary>
    public class UpgradeBadgeView : MonoBehaviour
    {
        [Tooltip("배지를 놓을 원(칸 번호 순서). CRT_ObjectSlot_01_Ring … 06.")]
        [SerializeField] private Renderer[] slotRings = new Renderer[0];

        [Tooltip("배지 프리팹(루트에 UpgradeBadge·콜라이더, 피벗 = 배지 바닥 가운데).")]
        [SerializeField] private UpgradeBadge badgePrefab;

        [Tooltip("아이콘이 없을 때 쓰는 기호 글꼴. 비우면 기본 글꼴.")]
        [SerializeField] private Font symbolFont;

        [Tooltip("마우스를 올렸을 때 떠오르는 높이(m).")]
        [Min(0f)]
        [SerializeField] private float hoverLift = 0.006f;

        [Tooltip("새 배지가 나타나는 시간과 떠오르기·내려앉기 시간(초).")]
        [Min(0.01f)]
        [SerializeField] private float animTime = 0.18f;

        private readonly List<UpgradeBadge> badges = new List<UpgradeBadge>();
        private readonly List<float> lift = new List<float>();
        private readonly List<float> appear = new List<float>();
        private int hovered = -1;

        public int SlotCount => slotRings.Length;

        /// <summary>보유 강화에 맞춰 배지를 놓고 치운다. 원보다 칸이 많으면 넘는 칸은 그리지 않는다.</summary>
        public void Refresh(GameFlowController flow)
        {
            if (flow == null || badgePrefab == null)
            {
                return;
            }

            IReadOnlyList<OwnedUpgrade> slots = flow.OwnedUpgrades.Slots;
            for (int i = 0; i < slotRings.Length; i++)
            {
                OwnedUpgrade owned = i < slots.Count ? slots[i] : default;
                if (owned.IsEmpty)
                {
                    if (i < badges.Count && badges[i] != null && badges[i].gameObject.activeSelf)
                    {
                        badges[i].gameObject.SetActive(false);
                    }

                    continue;
                }

                UpgradeBadge badge = BadgeAt(i);
                if (!badge.gameObject.activeSelf || badge.Item != owned.Item)
                {
                    // 새로 놓이거나 다른 강화로 바뀌면 다시 내려앉는 연출.
                    appear[i] = 0f;
                    lift[i] = 0f;
                    badge.gameObject.SetActive(true);
                }

                badge.Show(i, owned.Item, symbolFont);
            }
        }

        /// <summary>마우스가 올라간 배지(칸 번호). 없으면 -1.</summary>
        public void SetHovered(int slotIndex)
        {
            hovered = slotIndex;
        }

        private UpgradeBadge BadgeAt(int index)
        {
            while (badges.Count <= index)
            {
                badges.Add(null);
                lift.Add(0f);
                appear.Add(0f);
            }

            if (badges[index] == null)
            {
                UpgradeBadge badge = Instantiate(badgePrefab, transform);
                badge.name = $"UpgradeBadge_{index + 1:00}";
                badge.gameObject.SetActive(false);
                badges[index] = badge;
            }

            return badges[index];
        }

        private void LateUpdate()
        {
            float step = Time.deltaTime / animTime;
            for (int i = 0; i < badges.Count; i++)
            {
                UpgradeBadge badge = badges[i];
                if (badge == null || !badge.gameObject.activeSelf || i >= slotRings.Length || slotRings[i] == null)
                {
                    continue;
                }

                appear[i] = Mathf.MoveTowards(appear[i], 1f, step);
                lift[i] = Mathf.MoveTowards(lift[i], i == hovered ? 1f : 0f, step);

                // 원 가운데에 바로 서고, 처음에는 2cm 위에서 내려앉으며 커진다. 호버면 hoverLift만큼 뜬다.
                float drop = (1f - Smooth(appear[i])) * 0.02f;
                Vector3 position = slotRings[i].bounds.center + Vector3.up * (drop + Smooth(lift[i]) * hoverLift);
                badge.transform.SetPositionAndRotation(position, Quaternion.identity);
                badge.transform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, Smooth(appear[i]));
            }
        }

        private static float Smooth(float t) => t * t * (3f - 2f * t);
    }
}
