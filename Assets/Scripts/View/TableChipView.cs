using System.Collections.Generic;
using UnityEngine;

namespace ProjectCR
{
    /// <summary>
    /// 3D 테이블 위의 표시: 판에 놓인 칩 더미, 트레이에서 고른 칩, 마우스가 가리키는 자리, 잔액 표시.
    /// 회전이 끝나면 게임 흐름이 베팅을 비우므로, 다음 Refresh에서 판의 칩도 바로 사라진다.
    /// 상태는 읽기만 한다. 베팅 규칙은 GameFlowController에 있다.
    /// </summary>
    public class TableChipView : MonoBehaviour
    {
        // 한 자리에 쌓아 보여 주는 칩 수 상한. 금액이 크면 큰 칩부터 이만큼만 쌓는다.
        private const int MaxChipsPerStack = 10;

        [SerializeField] private TableBetBoard board;

        [Tooltip("칩 금액(작은 것부터). templates와 같은 순서.")]
        [SerializeField] private long[] denominations = new long[0];

        [Tooltip("판에 놓을 칩 모양(모델의 CHIP_* 견본). 꺼져 있어도 된다.")]
        [SerializeField] private GameObject[] templates = new GameObject[0];

        [Tooltip("트레이 칸 칩들. denominations와 같은 순서로, 칸마다 칩 묶음의 부모가 아니라 칩들을 그대로 나열한다.")]
        [SerializeField] private Transform[] trayChips = new Transform[0];

        [Tooltip("trayChips 각각이 어느 금액 칸인지(denominations 인덱스).")]
        [SerializeField] private int[] trayChipSlot = new int[0];

        [Tooltip("모델 칩 한 장의 두께(m). 쌓는 간격은 이 값 × chipScale.")]
        [SerializeField] private float chipHeight = 0.0034f;

        [Tooltip("판에 놓인 칩 크기 배율(모델 칩 기준). 트레이 칩은 바꾸지 않는다.")]
        [Min(0.1f)]
        [SerializeField] private float chipScale = 1.4f;

        [SerializeField] private float trayLift = 0.012f;

        [Tooltip("마우스가 가리키는 베팅 자리 표시.")]
        [SerializeField] private Transform hoverMarker;

        [Tooltip("잔액 표시 화면의 글자.")]
        [SerializeField] private TextMesh balanceText;

        private readonly Dictionary<BetTarget, List<GameObject>> stacks = new Dictionary<BetTarget, List<GameObject>>();
        private readonly Dictionary<long, Stack<GameObject>> pool = new Dictionary<long, Stack<GameObject>>();
        private readonly Dictionary<GameObject, long> chipValues = new Dictionary<GameObject, long>();
        private Vector3[] trayBase;

        public TableBetBoard Board => board;

        private void Awake()
        {
            trayBase = new Vector3[trayChips.Length];
            for (int i = 0; i < trayChips.Length; i++)
            {
                trayBase[i] = trayChips[i].localPosition;
            }

            SetHover(false, BetTarget.None);
        }

        /// <summary>베팅판·칩 금액·잔액에 맞춰 다시 그린다.</summary>
        public void Refresh(GameFlowController flow)
        {
            RefreshStacks(flow.Bet);
            RefreshTray(flow.Bet.ChipValue);

            if (balanceText != null)
            {
                balanceText.text = flow.Wallet.Balance.ToString("N0");
            }
        }

        public void SetHover(bool visible, BetTarget target)
        {
            if (hoverMarker == null)
            {
                return;
            }

            hoverMarker.gameObject.SetActive(visible && !target.IsNone);
            if (visible && !target.IsNone)
            {
                hoverMarker.position = board.WorldPositionOf(target) + board.transform.up * 0.0005f;
            }
        }

        private void RefreshStacks(BetManager bet)
        {
            var current = new HashSet<BetTarget>();
            foreach (ConfirmedBet placed in bet.Bets)
            {
                current.Add(placed.Target);
                BuildStack(placed.Target, placed.Stake);
            }

            var removed = new List<BetTarget>();
            foreach (BetTarget target in stacks.Keys)
            {
                if (!current.Contains(target))
                {
                    removed.Add(target);
                }
            }

            foreach (BetTarget target in removed)
            {
                ReleaseAll(stacks[target]);
                stacks.Remove(target);
            }
        }

        /// <summary>금액을 큰 칩부터 나눠 쌓는다(가장 큰 칩이 아래).</summary>
        private void BuildStack(BetTarget target, long amount)
        {
            if (!stacks.TryGetValue(target, out List<GameObject> chips))
            {
                chips = new List<GameObject>();
                stacks[target] = chips;
            }

            ReleaseAll(chips);

            Vector3 basePosition = board.WorldPositionOf(target);
            Vector3 up = board.transform.up;
            long left = amount;

            for (int d = denominations.Length - 1; d >= 0 && chips.Count < MaxChipsPerStack; d--)
            {
                while (left >= denominations[d] && chips.Count < MaxChipsPerStack)
                {
                    left -= denominations[d];
                    GameObject chip = Take(d);
                    // 견본 칩의 기준점이 아랫면이라 칩 두께(배율 반영)만큼씩 올려 쌓는다.
                    chip.transform.position = basePosition + up * (chipHeight * chipScale * chips.Count);
                    chip.transform.rotation = templates[d].transform.rotation;
                    chips.Add(chip);
                }
            }
        }

        private void RefreshTray(long chipValue)
        {
            for (int i = 0; i < trayChips.Length; i++)
            {
                bool selected = denominations[trayChipSlot[i]] == chipValue;
                trayChips[i].localPosition = trayBase[i];
                if (selected)
                {
                    trayChips[i].position += board.transform.up * trayLift;
                }
            }
        }

        private GameObject Take(int denominationIndex)
        {
            long value = denominations[denominationIndex];
            GameObject template = templates[denominationIndex];
            Vector3 scale = template.transform.lossyScale * chipScale;

            if (pool.TryGetValue(value, out Stack<GameObject> free) && free.Count > 0)
            {
                GameObject reused = free.Pop();
                reused.transform.localScale = scale;
                reused.SetActive(true);
                return reused;
            }

            GameObject chip = Instantiate(template, transform);
            chip.name = $"Bet_{template.name}";
            chip.transform.localScale = scale;
            chip.SetActive(true);
            chipValues[chip] = value;
            return chip;
        }

        private void ReleaseAll(List<GameObject> chips)
        {
            foreach (GameObject chip in chips)
            {
                Release(chip);
            }

            chips.Clear();
        }

        private void Release(GameObject chip)
        {
            chip.SetActive(false);
            long value = chipValues[chip];
            if (!pool.TryGetValue(value, out Stack<GameObject> free))
            {
                free = new Stack<GameObject>();
                pool[value] = free;
            }

            free.Push(chip);
        }
    }
}
