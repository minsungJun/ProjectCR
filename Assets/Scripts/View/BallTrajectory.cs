using UnityEngine;

namespace ProjectCR
{
    /// <summary>
    /// 공의 각도를 <b>휠 로컬 좌표(휠에 대한 상대 각도)</b>로 계산한다.
    ///
    /// 끝 각도가 목표 칸 각도와 360° 단위로 같아지도록 총 이동량을 정하므로,
    /// 휠이 어떤 속도로 돌든, 프레임이 얼마나 끊기든 u = 1에서 공은 목표 칸 위에 있다.
    /// 세계 기준 공 속도는 (휠 속도 + 상대 속도)라서, 초반에 상대 속도가 크게 음수면
    /// 공이 휠 반대 방향으로 도는 것처럼 보이고, 상대 속도가 0이 되면 휠에 실려 안착한다.
    ///
    /// 각도 규약: 위에서 볼 때 시계 방향이 +, 로컬 +Z가 0°. (Quaternion.Euler(0, angle, 0) * forward)
    /// </summary>
    public readonly struct BallTrajectory
    {
        private BallTrajectory(float startAngle, float totalDelta)
        {
            StartAngle = startAngle;
            TotalDelta = totalDelta;
        }

        public float StartAngle { get; }

        /// <summary>끝 − 시작(도). 항상 음수 = 휠 회전(+) 반대 방향.</summary>
        public float TotalDelta { get; }

        public float EndAngle => StartAngle + TotalDelta;

        /// <param name="revolutions">휠에 대한 상대 회전 수. 실제 이동량은 이보다 1바퀴 미만 더 길다.</param>
        public static BallTrajectory Create(float startLocalAngle, float targetLocalAngle, int revolutions)
        {
            float remainder = Mathf.Repeat(startLocalAngle - targetLocalAngle, 360f);
            float delta = -(360f * Mathf.Max(0, revolutions) + remainder);
            return new BallTrajectory(startLocalAngle, delta);
        }

        /// <summary>경과 시간을 0~1 진행도로. 마지막 프레임에 dt가 얼마나 크든 1에서 멈춘다.</summary>
        public static float Progress(float elapsed, float duration)
        {
            return duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
        }

        /// <summary>
        /// 진행도 u에서의 휠 로컬 각도. u ≥ 1이면 커브 끝값과 상관없이 정확히 <see cref="EndAngle"/>.
        /// </summary>
        public float AngleAt(float u, AnimationCurve progressCurve)
        {
            if (u >= 1f)
            {
                return EndAngle;
            }

            return StartAngle + TotalDelta * progressCurve.Evaluate(u);
        }

        public static Vector3 LocalPosition(float angleDegrees, float radius, float height)
        {
            float radians = angleDegrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians) * radius, height, Mathf.Cos(radians) * radius);
        }

        /// <summary><see cref="LocalPosition"/>의 역. 반환 각도는 0~360.</summary>
        public static float AngleOf(Vector3 localPosition)
        {
            return Mathf.Repeat(Mathf.Atan2(localPosition.x, localPosition.z) * Mathf.Rad2Deg, 360f);
        }

        public static float RadiusOf(Vector3 localPosition)
        {
            return new Vector2(localPosition.x, localPosition.z).magnitude;
        }
    }
}
