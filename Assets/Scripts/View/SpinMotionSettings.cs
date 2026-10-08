using UnityEngine;

namespace ProjectCR
{
    public enum SpinStage
    {
        None,

        /// <summary>휠 가속, 공이 외곽 트랙에서 출발.</summary>
        Launch,

        /// <summary>휠과 공이 반대 방향으로 회전.</summary>
        Spin,

        /// <summary>공의 속도·반경·높이가 줄며 안쪽으로 이동.</summary>
        Decelerate,

        /// <summary>당첨 칸 근처에서 작은 바운스 후 정지.</summary>
        Settle,

        /// <summary>공은 칸에 들어갔고, 휠이 감속해 결과 칸을 정해진 자리(6시)에 세우는 중.</summary>
        WheelStop,
    }

    /// <summary>
    /// 회전 연출 수치. 커브의 가로축은 전체 연출 시간 기준 진행도(0~1)다.
    /// 단계 시간을 바꾸면 커브의 키 위치도 같이 맞춰야 한다(기본값은 0.08 / 0.42 / 0.83 경계).
    /// 착지 칸은 커브와 무관하게 보장된다(BallTrajectory).
    /// </summary>
    [CreateAssetMenu(fileName = "SpinMotionSettings", menuName = "ProjectCR/Spin Motion Settings")]
    public class SpinMotionSettings : ScriptableObject
    {
        [Header("단계 시간(초)")]
        [Min(0f)] public float launchDuration = 0.5f;
        [Min(0f)] public float spinDuration = 2f;
        [Min(0f)] public float decelDuration = 2.5f;
        [Min(0f)] public float settleDuration = 1f;

        [Header("공")]
        [Tooltip("휠에 대한 상대 회전 수. 클수록 초반에 빠르게 돈다.")]
        [Min(0)] public int ballRevolutions = 6;

        [Tooltip("공이 출발하는 트랙 위치(RouletteRoot 기준 각도). 180이면 카메라 쪽.")]
        public float launchAngle = 180f;

        [Tooltip("상대 각도 진행도(0→1). 끝부분의 오버슈트가 칸 사이를 튀는 바운스다.")]
        public AnimationCurve angleCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 2.2f, 2.2f),
            new Keyframe(0.42f, 0.66f, 1.15f, 1.15f),
            new Keyframe(0.83f, 0.985f, 0.25f, 0.25f),
            new Keyframe(0.885f, 1.004f, 0f, 0f),
            new Keyframe(0.94f, 0.998f, 0f, 0f),
            new Keyframe(1f, 1f, 0f, 0f));

        [Tooltip("트랙 반경 → 칸 반경 (0→1).")]
        public AnimationCurve radiusCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0f),
            new Keyframe(0.5f, 0f, 0f, 0f),
            new Keyframe(0.72f, 0.45f, 4f, 4f),
            new Keyframe(0.83f, 1f, 0f, 0f),
            new Keyframe(1f, 1f, 0f, 0f));

        [Tooltip("트랙 높이 → 칸 높이 (0→1).")]
        public AnimationCurve heightCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0f),
            new Keyframe(0.55f, 0f, 0f, 0f),
            new Keyframe(0.83f, 1f, 0f, 0f),
            new Keyframe(1f, 1f, 0f, 0f));

        [Tooltip("안착 바운스 높이 배율. 0 아래는 0으로 자른다.")]
        public AnimationCurve bounceCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0f),
            new Keyframe(0.83f, 0f, 0f, 70f),
            new Keyframe(0.857f, 1f, 0f, 0f),
            new Keyframe(0.885f, 0f, -70f, 40f),
            new Keyframe(0.912f, 0.55f, 0f, 0f),
            new Keyframe(0.94f, 0f, -40f, 15f),
            new Keyframe(0.97f, 0.22f, 0f, 0f),
            new Keyframe(1f, 0f, -15f, 0f));

        [Min(0f)] public float bounceHeight = 0.05f;

        [Header("공 여러 개")]
        [Tooltip("공 i는 이만큼(초 × i) 늦게 안착한다. 같은 칸에 차례로 들어가게 한다.")]
        [Min(0f)] public float multiBallStagger = 0.35f;

        [Tooltip("공 i는 트랙에서 이만큼(× i) 안쪽 레인을 돈다.")]
        [Min(0f)] public float multiBallLaneSpacing = 0.07f;

        [Header("휠")]
        [Tooltip("첫 회전 전 대기 중 속도(도/초). 한 번 돌고 나면 휠은 결과 칸을 세운 채 멈춰 있다.")]
        public float idleWheelSpeed = 12f;

        [Tooltip("회전 중 평소 속도(도/초). 결과 칸을 제자리에 세우려고 실제로는 이 값 근처에서 조금 달라진다.")]
        public float spinWheelSpeed = 120f;

        [Tooltip("휠이 멈췄을 때 결과 칸(공 1번이 든 칸)이 올 자리. RouletteRoot 기준 각도, 180 = 6시(카메라 쪽).")]
        public float restAngle = 180f;

        [Tooltip("마지막 공이 안착한 뒤 휠이 완전히 멈출 때까지 걸리는 시간(초). 결과는 휠이 멈춘 뒤 표시된다.")]
        [Min(0f)] public float wheelStopDelay = 1f;

        [Tooltip("휠이 일정 속도에서 0까지 감속하는 시간(초). 공이 안쪽으로 떨어지는 동안 함께 느려진다.")]
        [Min(0.1f)] public float wheelStopDecelDuration = 3f;

        [Header("당첨 강조")]
        public Color highlightColor = new Color(1f, 0.82f, 0.25f, 1f);
        [Min(0f)] public float highlightPulseSpeed = 4f;

        public float TotalDuration => launchDuration + spinDuration + decelDuration + settleDuration;

        public SpinStage StageAt(float elapsed)
        {
            if (elapsed < launchDuration)
            {
                return SpinStage.Launch;
            }

            if (elapsed < launchDuration + spinDuration)
            {
                return SpinStage.Spin;
            }

            if (elapsed < launchDuration + spinDuration + decelDuration)
            {
                return SpinStage.Decelerate;
            }

            return elapsed < TotalDuration ? SpinStage.Settle : SpinStage.None;
        }
    }
}
