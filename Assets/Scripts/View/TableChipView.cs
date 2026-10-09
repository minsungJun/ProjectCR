using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace ProjectCR
{
    /// <summary>
    /// 3D 테이블 위의 표시: 판에 놓인 칩 더미와 더미별 합계 글자, 트레이에서 고른 칩, 마우스가 가리키는 자리,
    /// 잔액 표시(보유 − 판에 놓은 칩 합계).
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

        [Tooltip("고른 금액 칸 테두리를 빛나게 하는 표시. 비우면 칩만 들어 올린다.")]
        [SerializeField] private ChipTrayGlow trayGlow;

        [Tooltip("모델 칩 한 장의 두께(m). 쌓는 간격은 이 값 × chipScale.")]
        [SerializeField] private float chipHeight = 0.0034f;

        [Tooltip("판에 놓인 칩 크기 배율(모델 칩 기준). 트레이 칩은 바꾸지 않는다.")]
        [Min(0.1f)]
        [SerializeField] private float chipScale = 1.4f;

        [Tooltip("고른 금액 칸 칩을 들어 올리는 높이(m).")]
        [SerializeField] private float trayLift = 0.0132f;

        [Tooltip("칩이 올라가고 내려가는 데 걸리는 대략의 시간(초). 0이면 바로.")]
        [Min(0f)]
        [SerializeField] private float trayLiftTime = 0.18f;

        [Tooltip("마우스가 가리키는 베팅 자리 표시.")]
        [SerializeField] private Transform hoverMarker;

        [Tooltip("마우스가 가리키는 베팅의 포함 칸 강조. 비우면 칸 강조 없이 자리 표시만 한다.")]
        [SerializeField] private BetHighlightView highlight;

        [Tooltip("잔액 표시 화면의 글자. 보유 금액에서 판에 놓은 칩 합계를 뺀 값(이번 회전에 쓸 수 있는 돈)을 보여 준다.")]
        [SerializeField] private TextMesh balanceText;

        [Tooltip("목표 금액 글자(태블릿). 이번 라운드 납부금. 비우면 표시하지 않는다.")]
        [FormerlySerializedAs("betText")]
        [SerializeField] private TextMesh dueText;

        [Tooltip("테이블 태블릿. 버튼을 누를 수 있는지 상태에 맞춘다.")]
        [SerializeField] private TabletPanel tablet;

        [Header("칩 더미 금액")]
        [Tooltip("칩 더미 위에 띄우는 합계 글자의 크기(TextMesh characterSize, 글꼴 크기 64 기준).")]
        [Min(0.0001f)]
        [SerializeField] private float amountLabelSize = 0.0042f;

        [Tooltip("맨 위 칩에서 합계 글자까지 띄우는 거리(m). 화면에서 칩 바로 위에 보이도록 카메라의 위 방향으로 띄운다.")]
        [SerializeField] private float amountLabelLift = 0.026f;

        [SerializeField] private Color amountLabelColor = new Color(1f, 0.86f, 0.45f);

        private readonly Dictionary<BetTarget, List<GameObject>> stacks = new Dictionary<BetTarget, List<GameObject>>();
        private readonly Dictionary<long, Stack<GameObject>> pool = new Dictionary<long, Stack<GameObject>>();
        private readonly Dictionary<GameObject, long> chipValues = new Dictionary<GameObject, long>();
        private readonly Dictionary<BetTarget, StackLabel> labels = new Dictionary<BetTarget, StackLabel>();
        private readonly Stack<StackLabel> freeLabels = new Stack<StackLabel>();
        private Vector3[] trayBase;

        // 트레이 칩 들림: 지금 높이, SmoothDamp 속도, 지금 고른 금액(목표). 목표에 닿으면 움직임을 멈춘다.
        private float[] trayLiftNow;
        private float[] trayLiftVelocity;
        private long trayTargetValue = -1;
        private bool trayMoving;

        /// <summary>칩 더미 위 합계 글자. 읽기 쉽게 어두운 그림자 글자를 뒤에 하나 더 둔다.</summary>
        private sealed class StackLabel
        {
            /// <summary>맨 위 칩 윗면(월드). 글자는 여기서 카메라 위 방향으로 띄운다.</summary>
            public Vector3 Anchor;
            public Transform Root;
            public TextMesh Text;
            public TextMesh Shadow;
        }

        public TableBetBoard Board => board;

        /// <summary>테이블 태블릿(없으면 null).</summary>
        public TabletPanel Tablet => tablet;

        private void Awake()
        {
            trayBase = new Vector3[trayChips.Length];
            trayLiftNow = new float[trayChips.Length];
            trayLiftVelocity = new float[trayChips.Length];
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
                // 놓기만 한 칩은 아직 지갑에서 빠지지 않았으니 여기서 빼서 보여 준다.
                // 회전이 시작되면(잠김) 합계가 이미 빠졌으므로 또 빼지 않는다.
                long pending = flow.Bet.IsLocked ? 0 : flow.Bet.TotalStake;
                balanceText.text = Math.Max(0, flow.Wallet.Balance - pending).ToString("N0");
            }

            if (dueText != null)
            {
                dueText.text = flow.Round.CurrentDue.ToString("N0");
            }

            if (tablet != null)
            {
                tablet.Refresh(flow);
            }
        }

        /// <summary>고른 칸 칩은 trayLift까지, 나머지는 0까지 부드럽게 옮긴다(SmoothDamp, 시간 기준이라 프레임과 무관).</summary>
        private void Update()
        {
            if (!trayMoving || trayLiftNow == null)
            {
                return;
            }

            float dt = Time.unscaledDeltaTime;
            bool moving = false;
            for (int i = 0; i < trayChips.Length; i++)
            {
                float target = denominations[trayChipSlot[i]] == trayTargetValue ? trayLift : 0f;
                float now = trayLiftTime > 0f
                    ? Mathf.SmoothDamp(trayLiftNow[i], target, ref trayLiftVelocity[i], trayLiftTime, Mathf.Infinity, dt)
                    : target;
                if (Mathf.Abs(now - target) < 0.00002f && Mathf.Abs(trayLiftVelocity[i]) < 0.0005f)
                {
                    now = target;
                    trayLiftVelocity[i] = 0f;
                }
                else
                {
                    moving = true;
                }

                trayLiftNow[i] = now;
                ApplyTrayLift(i);
            }

            trayMoving = moving;
        }

        private void ApplyTrayLift(int i)
        {
            Transform chip = trayChips[i];
            Vector3 lift = board.transform.up * trayLiftNow[i];
            chip.localPosition = trayBase[i] + (chip.parent != null ? chip.parent.InverseTransformVector(lift) : lift);
        }

        /// <summary>합계 글자가 늘 카메라를 보고 화면에서 칩 위에 있게 한다(시점 전환·카메라 이동 중에도).</summary>
        private void LateUpdate()
        {
            if (labels.Count == 0)
            {
                return;
            }

            Camera view = ActiveCamera();
            if (view == null)
            {
                return;
            }

            foreach (StackLabel label in labels.Values)
            {
                FaceCamera(label, view);
            }
        }

        private void FaceCamera(StackLabel label, Camera view)
        {
            Transform eye = view.transform;
            label.Root.SetPositionAndRotation(label.Anchor + eye.up * amountLabelLift, eye.rotation);
        }

        /// <summary>호버 표시: 칩 놓는 자리 원판 + (정의가 있으면) 포함 칸 강조. 숨기면 둘 다 끈다.</summary>
        public void SetHover(bool visible, BetTarget target, RouletteDefinition definition = null)
        {
            bool show = visible && !target.IsNone;
            if (highlight != null)
            {
                if (show && definition != null)
                {
                    highlight.Show(target, definition);
                }
                else
                {
                    highlight.Hide();
                }
            }

            if (hoverMarker == null)
            {
                return;
            }

            hoverMarker.gameObject.SetActive(show);
            if (show)
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
                ReleaseLabel(target);
            }
        }

        /// <summary>더미 맨 위 칩 위에 합계를 띄운다.</summary>
        private void PlaceLabel(BetTarget target, long amount, int chipCount)
        {
            if (!labels.TryGetValue(target, out StackLabel label))
            {
                label = freeLabels.Count > 0 ? freeLabels.Pop() : CreateLabel();
                labels[target] = label;
            }

            float top = chipHeight * chipScale * Mathf.Max(1, chipCount);
            label.Anchor = board.WorldPositionOf(target) + board.transform.up * top;
            label.Root.position = label.Anchor;
            label.Text.text = amount.ToString("N0");
            label.Shadow.text = label.Text.text;
            label.Root.gameObject.SetActive(true);

            Camera view = ActiveCamera();
            if (view != null)
            {
                FaceCamera(label, view);
            }
        }

        private void ReleaseLabel(BetTarget target)
        {
            if (labels.TryGetValue(target, out StackLabel label))
            {
                label.Root.gameObject.SetActive(false);
                labels.Remove(target);
                freeLabels.Push(label);
            }
        }

        private StackLabel CreateLabel()
        {
            var root = new GameObject("StackLabel").transform;
            root.SetParent(transform, false);

            // 그림자는 글자보다 살짝 뒤(카메라 반대쪽)·아래에 둔다.
            TextMesh shadow = CreateText(root, "Shadow", new Color(0f, 0f, 0f, 0.85f), new Vector3(0.0004f, -0.0004f, 0.0006f));
            TextMesh text = CreateText(root, "Text", amountLabelColor, Vector3.zero);
            return new StackLabel { Root = root, Text = text, Shadow = shadow };
        }

        private TextMesh CreateText(Transform parent, string name, Color color, Vector3 offset)
        {
            Font font = balanceText != null && balanceText.font != null
                ? balanceText.font
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = offset;

            var text = go.AddComponent<TextMesh>();
            text.font = font;
            text.fontSize = 64;
            text.fontStyle = FontStyle.Bold;
            text.characterSize = amountLabelSize;
            text.anchor = TextAnchor.LowerCenter;
            text.alignment = TextAlignment.Center;
            text.color = color;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            return text;
        }

        /// <summary>지금 화면을 그리는 카메라. 시점 전환으로 Main Camera가 꺼져 있으면 켜진 다른 카메라.</summary>
        private static Camera ActiveCamera()
        {
            Camera main = Camera.main;
            if (main != null && main.isActiveAndEnabled)
            {
                return main;
            }

            foreach (Camera camera in Camera.allCameras)
            {
                return camera;
            }

            return null;
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

            PlaceLabel(target, amount, chips.Count);
        }

        private void RefreshTray(long chipValue)
        {
            if (trayGlow != null)
            {
                trayGlow.Show(Array.IndexOf(denominations, chipValue));
            }

            // 높이는 Update가 천천히 맞춘다. 여기서는 목표만 바꾼다.
            if (trayTargetValue != chipValue)
            {
                trayTargetValue = chipValue;
                trayMoving = true;
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
