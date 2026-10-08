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
    /// </summary>
    [DisallowMultipleComponent]
    public class RoulettePresenter : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Header("데이터")]
        [SerializeField] private RouletteDefinition definition;
        [SerializeField] private SpinMotionSettings motion;

        [Header("구조")]
        [Tooltip("로컬 Y축으로 도는 휠. 칸 앵커가 이 아래에 있어야 한다.")]
        [SerializeField] private Transform wheelPivot;

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
        private float wheelSpeed;
        private float wheelTargetSpeed;
        private float highlightTime;

        /// <summary>연출 배속. 37칸 순회 검증처럼 빨리 돌릴 때 쓴다.</summary>
        public float SpeedMultiplier { get; set; } = 1f;

        public bool IsPlaying { get; private set; }

        /// <summary>지금 연출 단계. 효과음을 붙일 때 쓴다.</summary>
        public SpinStage CurrentStage { get; private set; }

        public int PocketCount => pocketAnchors.Length;

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

            wheelSpeed = motion.idleWheelSpeed;
            wheelTargetSpeed = motion.idleWheelSpeed;

            EnsureBalls(1);
            ParkBallsOnTrack(1);
        }

        private void Update()
        {
            float dt = Time.deltaTime * SpeedMultiplier;

            // 휠은 deltaTime 적분이라 프레임마다 조금씩 다르게 돈다. 공은 휠 로컬로 계산하므로 착지에는 영향이 없다.
            float rate = wheelTargetSpeed > wheelSpeed ? motion.wheelAcceleration : motion.wheelDeceleration;
            wheelSpeed = Mathf.MoveTowards(wheelSpeed, wheelTargetSpeed, rate * dt);
            wheelPivot.Rotate(0f, wheelSpeed * dt, 0f, Space.Self);

            UpdateHighlight(dt);
        }

        /// <summary>
        /// 결과의 공마다 목표 칸으로 들어가는 연출. 모든 공이 안착하면 끝난다.
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

            wheelTargetSpeed = motion.spinWheelSpeed;

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

            wheelTargetSpeed = motion.idleWheelSpeed;
            CurrentStage = SpinStage.None;
            Highlight(result);
            IsPlaying = false;
        }

        /// <summary>
        /// 새 게임 시작용. Play를 돌리던 코루틴이 멈췄을 때도 상태를 대기로 돌린다.
        /// </summary>
        public void ResetView()
        {
            IsPlaying = false;
            CurrentStage = SpinStage.None;
            wheelTargetSpeed = motion.idleWheelSpeed;
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
