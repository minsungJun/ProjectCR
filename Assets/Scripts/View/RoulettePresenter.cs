using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectCR
{
    /// <summary>
    /// 휠과 공의 움직임, 당첨 칸 강조. 결과는 이미 정해진 SpinResult로 받아서 그 칸에 멈추는 연출만 한다.
    /// 돈과 베팅은 모른다.
    ///
    /// 공은 휠 로컬 좌표로 계산한다(BallTrajectory). 그래서 휠 속도·프레임 속도와 상관없이 목표 칸에 들어간다.
    /// 안착하면 공을 해당 PocketAnchor 자식으로 붙여 이후로는 칸을 따라 돈다.
    ///
    /// 휠은 회전을 시작할 때 정지 각도까지 계획한다(WheelSpinPlan). 결과 칸(공 1번이 든 칸)이
    /// <see cref="SpinMotionSettings.restAngle"/>(기본 6시)에 오도록 멈추고, 다음 회전까지 그대로 서 있는다.
    /// </summary>
    [DisallowMultipleComponent]
    public class RoulettePresenter : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Header("데이터")]
        [SerializeField] private RouletteDefinition definition;
        [SerializeField] private SpinMotionSettings motion;

        [Header("구조")]
        [Tooltip("공·칸 계산에 쓰는 휠 좌표계(Y가 위). 휠과 함께 돌아야 하고, 칸 앵커가 이 아래에 있어야 한다.")]
        [SerializeField] private Transform wheelPivot;

        [Tooltip("실제로 돌리는 회전판. 비우면 wheelPivot을 로컬 Y축으로 돌린다(임시 모델). 모델을 쓸 때는 회전판(예: CRR_Rotor_SPIN_Z)을 넣고, wheelPivot은 그 아래 Y가 위인 자식으로 둔다.")]
        [SerializeField] private Transform rotor;

        [Tooltip("회전판의 회전축(회전판 로컬). CRR_Rotor_SPIN_Z는 로컬 Z(0, 0, 1).")]
        [SerializeField] private Vector3 rotorAxis = Vector3.forward;

        [Tooltip("회전 중인 공이 머무는 곳. 안착하면 칸 앵커로 옮겨 간다.")]
        [SerializeField] private Transform ballsRoot;

        [SerializeField] private Transform ballPrefab;

        [Header("칸 — 배열 인덱스 = RouletteDefinition 칸 인덱스 (빌더가 채움)")]
        [SerializeField] private PocketAnchor[] pocketAnchors = new PocketAnchor[0];
        [SerializeField] private Renderer[] pocketRenderers = new Renderer[0];

        [Header("볼 트랙 (RouletteRoot 로컬, 빌더가 채움)")]
        [SerializeField] private float rimRadius = 1.12f;
        [SerializeField] private float rimHeight = 0.18f;
        [SerializeField] private float ballRadius = 0.03f;

        private readonly List<Transform> balls = new List<Transform>();
        private readonly List<int> highlightedPockets = new List<int>();

        private MaterialPropertyBlock block;
        private Color[] baseColors;
        private float highlightTime;

        private enum WheelState
        {
            /// <summary>첫 회전 전. 천천히 돈다.</summary>
            Idle,

            /// <summary>계획대로 돌다가 결과 칸을 제자리에 세운다.</summary>
            Planned,

            /// <summary>결과 칸을 세운 채 멈춰 있다.</summary>
            Parked,
        }

        private WheelState wheelState = WheelState.Idle;
        private WheelSpinPlan wheelPlan;
        private float wheelElapsed;
        private float wheelAngle;

        // 회전판을 돌릴 때: 처음 자세와, + 각도가 위에서 볼 때 시계 방향이 되도록 맞추는 부호.
        private Quaternion rotorBaseRotation;
        private float rotorAxisSign = 1f;

        /// <summary>연출 배속. 37칸 순회 검증처럼 빨리 돌릴 때 쓴다.</summary>
        public float SpeedMultiplier { get; set; } = 1f;

        public bool IsPlaying { get; private set; }

        /// <summary>지금 연출 단계. 효과음을 붙일 때 쓴다.</summary>
        public SpinStage CurrentStage { get; private set; }

        public int PocketCount => pocketAnchors.Length;

        /// <summary>결과 칸이 멈출 자리(RouletteRoot 기준 각도).</summary>
        public float RestAngle => motion.restAngle;

        public float WheelSpeed
        {
            get
            {
                switch (wheelState)
                {
                    case WheelState.Idle:
                        return motion.idleWheelSpeed;
                    case WheelState.Planned:
                        return wheelPlan.SpeedAt(wheelElapsed);
                    default:
                        return 0f;
                }
            }
        }

        private struct BallPlan
        {
            public BallTrajectory Trajectory;
            public float RimRadius;
            public float RimHeight;
            public float TargetRadius;
            public float TargetHeight;
            public float Duration;
            public PocketAnchor Anchor;
            public Vector3 SlotOffset;
        }

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            CacheBaseColors();
            ValidateLayout();

            if (rotor != null)
            {
                rotorBaseRotation = rotor.localRotation;
                Vector3 axisWorld = rotor.TransformDirection(rotorAxis.normalized);
                rotorAxisSign = Vector3.Dot(axisWorld, transform.up) >= 0f ? 1f : -1f;
                wheelAngle = 0f;
            }
            else
            {
                wheelAngle = wheelPivot.localEulerAngles.y;
            }

            EnsureBalls(1);
            ParkBallsOnTrack(1);
        }

        private void Update()
        {
            float dt = Time.deltaTime * SpeedMultiplier;

            switch (wheelState)
            {
                case WheelState.Idle:
                    wheelAngle = Mathf.Repeat(wheelAngle + motion.idleWheelSpeed * dt, 360f);
                    break;
                case WheelState.Planned:
                    // 경과 시간으로 각도를 구하므로 프레임 속도와 상관없이 정해진 각도에서 멈춘다.
                    wheelElapsed += dt;
                    wheelAngle = wheelPlan.AngleAt(wheelElapsed);
                    if (wheelElapsed >= wheelPlan.Duration)
                    {
                        wheelAngle = Mathf.Repeat(wheelPlan.EndAngle, 360f);
                        wheelState = WheelState.Parked;
                    }

                    break;
            }

            ApplyWheelRotation();

            UpdateHighlight(dt);
        }

        /// <summary>
        /// 결과의 공마다 목표 칸으로 들어가는 연출. 모든 공이 안착하고 휠이 결과 칸을 제자리에 세우면 끝난다.
        /// 코루틴 안에서 yield return으로 기다린다.
        /// </summary>
        public IEnumerator Play(SpinResult result)
        {
            IsPlaying = true;
            ClearHighlight();

            int count = result.Balls.Count;
            EnsureBalls(count);

            var plans = new BallPlan[count];
            var slotsInPocket = new Dictionary<int, int>();

            for (int i = 0; i < count; i++)
            {
                BallResult ball = result.Balls[i];
                slotsInPocket.TryGetValue(ball.PocketIndex, out int slot);
                slotsInPocket[ball.PocketIndex] = slot + 1;

                plans[i] = PlanBall(i, count, pocketAnchors[ball.PocketIndex], slot);

                Transform view = balls[i];
                view.SetParent(ballsRoot, true);
                view.gameObject.SetActive(true);
            }

            for (int i = count; i < balls.Count; i++)
            {
                balls[i].gameObject.SetActive(false);
            }

            float lastLanding = 0f;
            foreach (BallPlan plan in plans)
            {
                lastLanding = Mathf.Max(lastLanding, plan.Duration);
            }

            PlanWheel(result.Balls[0].PocketIndex, lastLanding + motion.wheelStopDelay);

            var landed = new bool[count];
            int landedCount = 0;
            float elapsed = 0f;

            while (landedCount < count)
            {
                elapsed += Time.deltaTime * SpeedMultiplier;
                CurrentStage = motion.StageAt(elapsed);

                for (int i = 0; i < count; i++)
                {
                    if (landed[i])
                    {
                        continue;
                    }

                    float u = BallTrajectory.Progress(elapsed, plans[i].Duration);
                    PlaceBall(balls[i], plans[i], u);

                    if (u >= 1f)
                    {
                        Land(balls[i], plans[i]);
                        landed[i] = true;
                        landedCount++;
                    }
                }

                if (landedCount < count)
                {
                    yield return null;
                }
            }

            // 공은 다 들어갔다. 휠이 결과 칸을 제자리에 세울 때까지 기다린다.
            Highlight(result);
            CurrentStage = SpinStage.WheelStop;
            while (wheelState == WheelState.Planned)
            {
                yield return null;
            }

            CurrentStage = SpinStage.None;
            IsPlaying = false;
        }

        /// <summary>칸의 지금 각도(RouletteRoot 기준, 0~360). 결과 칸이 제자리에 섰는지 확인할 때 쓴다.</summary>
        public float PocketAngleOnTable(int pocketIndex)
        {
            Vector3 local = transform.InverseTransformPoint(pocketAnchors[pocketIndex].transform.position);
            return BallTrajectory.AngleOf(local);
        }

        /// <summary>
        /// 지금 각도·속도에서 출발해, duration 뒤에 targetPocket이 restAngle에 오도록 멈추는 휠 계획을 세운다.
        /// 휠 각도가 +x 늘면 칸의 테이블 위 각도도 +x 는다(회전 부호를 맞춰 두었다). 그래서 지금 칸 각도에서
        /// restAngle까지 남은 만큼 휠을 더 돌리면 된다. 모델 축 방향과 상관없이 실제 위치로 계산한다.
        /// </summary>
        private void PlanWheel(int targetPocket, float duration)
        {
            float startSpeed = WheelSpeed;
            float pocketNow = PocketAngleOnTable(targetPocket);

            wheelAngle = Mathf.Repeat(wheelAngle, 360f);
            wheelPlan = WheelSpinPlan.Create(
                wheelAngle, startSpeed, wheelAngle + motion.restAngle - pocketNow, motion.spinWheelSpeed,
                motion.launchDuration, duration, motion.wheelStopDecelDuration);
            wheelElapsed = 0f;
            wheelState = WheelState.Planned;
        }

        /// <summary>
        /// 새 게임 시작용. Play를 돌리던 코루틴이 멈췄을 때도 상태를 대기로 돌린다.
        /// </summary>
        public void ResetView()
        {
            IsPlaying = false;
            CurrentStage = SpinStage.None;
            wheelState = WheelState.Idle;
            ClearHighlight();

            foreach (Transform ball in balls)
            {
                ball.SetParent(ballsRoot, false);
            }

            ParkBallsOnTrack(1);
        }

        public Vector3 GetBallPosition(int ballIndex) => balls[ballIndex].position;

        /// <summary>세계 좌표에서 수평 거리가 가장 가까운 칸 인덱스. 안착 검증용.</summary>
        public int FindNearestPocketIndex(Vector3 worldPosition)
        {
            int best = -1;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < pocketAnchors.Length; i++)
            {
                Vector3 offset = pocketAnchors[i].transform.position - worldPosition;
                offset.y = 0f;
                float distance = offset.sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>앵커·Renderer 배열이 정의와 같은 순서·번호인지 확인한다. 어긋나면 에러를 남긴다.</summary>
        public bool ValidateLayout()
        {
            bool ok = true;

            if (pocketAnchors.Length != definition.PocketCount)
            {
                Debug.LogError($"[RoulettePresenter] 칸 앵커 {pocketAnchors.Length}개, 정의 {definition.PocketCount}칸. 빌더를 다시 돌릴 것.", this);
                return false;
            }

            if (pocketRenderers.Length != definition.PocketCount)
            {
                Debug.LogError($"[RoulettePresenter] 칸 Renderer {pocketRenderers.Length}개, 정의 {definition.PocketCount}칸.", this);
                ok = false;
            }

            for (int i = 0; i < pocketAnchors.Length; i++)
            {
                PocketAnchor anchor = pocketAnchors[i];
                int expected = definition.GetPocket(i).Number;

                if (anchor == null || anchor.PocketIndex != i || anchor.Number != expected)
                {
                    Debug.LogError($"[RoulettePresenter] 칸 {i}: 앵커가 비었거나 번호가 다르다(기대 {expected}).", this);
                    ok = false;
                }
                else if (!anchor.transform.IsChildOf(wheelPivot))
                {
                    Debug.LogError($"[RoulettePresenter] 칸 {i}: 앵커가 WheelPivot 아래에 없다.", this);
                    ok = false;
                }
            }

            return ok;
        }

        /// <summary>
        /// 휠 각도를 실제 Transform에 반영한다. + 각도 = 위에서 볼 때 시계 방향.
        /// 회전판이 있으면 그 축으로 돌리고(처음 자세 기준), 없으면 wheelPivot을 Y축으로 돌린다.
        /// </summary>
        private void ApplyWheelRotation()
        {
            if (rotor != null)
            {
                rotor.localRotation = rotorBaseRotation * Quaternion.AngleAxis(wheelAngle * rotorAxisSign, rotorAxis);
            }
            else
            {
                wheelPivot.localRotation = Quaternion.Euler(0f, wheelAngle, 0f);
            }
        }

        private BallPlan PlanBall(int ballIndex, int ballCount, PocketAnchor anchor, int slot)
        {
            float lane = motion.multiBallLaneSpacing * ballIndex;
            float launchAngle = motion.launchAngle + 360f * ballIndex / ballCount;

            // 출발점과 목표점을 모두 휠 로컬로 옮겨서 각도·반경·높이를 잰다.
            Vector3 launchWorld = transform.TransformPoint(BallTrajectory.LocalPosition(launchAngle, rimRadius - lane, rimHeight));
            Vector3 launchLocal = wheelPivot.InverseTransformPoint(launchWorld);

            Vector3 slotOffset = PocketAnchor.GetSlotOffset(slot, ballRadius);
            Vector3 targetLocal = wheelPivot.InverseTransformPoint(anchor.transform.TransformPoint(slotOffset));

            return new BallPlan
            {
                Trajectory = BallTrajectory.Create(
                    BallTrajectory.AngleOf(launchLocal),
                    BallTrajectory.AngleOf(targetLocal),
                    motion.ballRevolutions),
                RimRadius = BallTrajectory.RadiusOf(launchLocal),
                RimHeight = launchLocal.y,
                TargetRadius = BallTrajectory.RadiusOf(targetLocal),
                TargetHeight = targetLocal.y,
                Duration = motion.TotalDuration + motion.multiBallStagger * ballIndex,
                Anchor = anchor,
                SlotOffset = slotOffset,
            };
        }

        private void PlaceBall(Transform ball, BallPlan plan, float u)
        {
            float angle = plan.Trajectory.AngleAt(u, motion.angleCurve);
            float radius;
            float height;

            if (u >= 1f)
            {
                radius = plan.TargetRadius;
                height = plan.TargetHeight;
            }
            else
            {
                radius = Mathf.LerpUnclamped(plan.RimRadius, plan.TargetRadius, motion.radiusCurve.Evaluate(u));
                height = Mathf.LerpUnclamped(plan.RimHeight, plan.TargetHeight, motion.heightCurve.Evaluate(u))
                         + Mathf.Max(0f, motion.bounceCurve.Evaluate(u)) * motion.bounceHeight;
            }

            ball.position = wheelPivot.TransformPoint(BallTrajectory.LocalPosition(angle, radius, height));
        }

        private static void Land(Transform ball, BallPlan plan)
        {
            ball.SetParent(plan.Anchor.transform, false);
            ball.localPosition = plan.SlotOffset;
            ball.localRotation = Quaternion.identity;
        }

        private void EnsureBalls(int count)
        {
            while (balls.Count < count)
            {
                Transform ball = Instantiate(ballPrefab, ballsRoot);
                ball.name = $"Ball_{balls.Count}";
                balls.Add(ball);
            }
        }

        private void ParkBallsOnTrack(int count)
        {
            for (int i = 0; i < balls.Count; i++)
            {
                bool active = i < count;
                balls[i].gameObject.SetActive(active);
                if (active)
                {
                    balls[i].position = transform.TransformPoint(
                        BallTrajectory.LocalPosition(motion.launchAngle + 360f * i / count, rimRadius, rimHeight));
                }
            }
        }

        private void CacheBaseColors()
        {
            baseColors = new Color[pocketRenderers.Length];
            for (int i = 0; i < pocketRenderers.Length; i++)
            {
                Material material = pocketRenderers[i] != null ? pocketRenderers[i].sharedMaterial : null;
                baseColors[i] = material != null && material.HasProperty(BaseColorId)
                    ? material.GetColor(BaseColorId)
                    : Color.white;
            }
        }

        /// <summary>칸 하나를 결과 강조와 같은 방식으로 깜빡인다(튜토리얼: 보라색 위험 칸). 다른 강조는 지운다.</summary>
        public void BlinkPocket(int pocketIndex)
        {
            ClearHighlight();
            if (pocketIndex >= 0 && pocketIndex < pocketRenderers.Length && pocketRenderers[pocketIndex] != null)
            {
                highlightedPockets.Add(pocketIndex);
                highlightTime = 0f;
            }
        }

        /// <summary>칸 강조를 모두 끈다.</summary>
        public void StopBlink() => ClearHighlight();

        private void Highlight(SpinResult result)
        {
            foreach (BallResult ball in result.Balls)
            {
                if (!highlightedPockets.Contains(ball.PocketIndex))
                {
                    highlightedPockets.Add(ball.PocketIndex);
                }
            }

            highlightTime = 0f;
        }

        private void UpdateHighlight(float dt)
        {
            if (highlightedPockets.Count == 0)
            {
                return;
            }

            highlightTime += dt;
            float pulse = 0.5f + 0.5f * Mathf.Sin(highlightTime * motion.highlightPulseSpeed * Mathf.PI);

            foreach (int index in highlightedPockets)
            {
                block.SetColor(BaseColorId, Color.Lerp(baseColors[index], motion.highlightColor, 0.35f + 0.65f * pulse));
                pocketRenderers[index].SetPropertyBlock(block);
            }
        }

        private void ClearHighlight()
        {
            foreach (int index in highlightedPockets)
            {
                pocketRenderers[index].SetPropertyBlock(null);
            }

            highlightedPockets.Clear();
        }
    }
}
