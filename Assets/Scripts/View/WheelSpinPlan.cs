using UnityEngine;

namespace ProjectCR
{
    /// <summary>
    /// 회전 한 번 동안의 휠 각도 계획: 가속 → 일정 속도 → 감속해 정지. 정지 각도가 목표와 360° 단위로 맞도록
    /// 일정 속도 구간의 속도만 조금 조정한다. 그래서 결과 칸이 항상 같은 자리(예: 6시)에서 멈춘다.
    ///
    /// 공의 착지는 휠 로컬로 계산하므로(BallTrajectory) 휠을 이렇게 움직여도 공이 들어가는 칸은 바뀌지 않는다.
    /// 각도는 경과 시간의 함수라 프레임 속도와 무관하고, 시간이 끝나면 정확히 <see cref="EndAngle"/>이다.
    /// </summary>
    public readonly struct WheelSpinPlan
    {
        private WheelSpinPlan(float startAngle, float startSpeed, float cruiseSpeed,
            float accelTime, float cruiseTime, float decelTime, float distance)
        {
            StartAngle = startAngle;
            StartSpeed = startSpeed;
            CruiseSpeed = cruiseSpeed;
            AccelTime = accelTime;
            CruiseTime = cruiseTime;
            DecelTime = decelTime;
            Distance = distance;
        }

        public float StartAngle { get; }
        public float StartSpeed { get; }

        /// <summary>목표 각도에 맞추려고 조정한 일정 속도(도/초).</summary>
        public float CruiseSpeed { get; }

        public float AccelTime { get; }
        public float CruiseTime { get; }
        public float DecelTime { get; }

        /// <summary>시작부터 정지까지 도는 각도(항상 양수).</summary>
        public float Distance { get; }

        public float Duration => AccelTime + CruiseTime + DecelTime;
        public float EndAngle => StartAngle + Distance;

        /// <param name="startAngle">지금 휠 각도.</param>
        /// <param name="startSpeed">지금 휠 속도(도/초, 0 이상).</param>
        /// <param name="targetEndAngle">멈췄을 때의 휠 각도(360° 단위로만 맞으면 된다).</param>
        /// <param name="nominalSpeed">평소 회전 속도. 실제 일정 속도는 이 근처에서 정해진다.</param>
        /// <param name="accelTime">시작 속도 → 일정 속도까지.</param>
        /// <param name="duration">전체 시간. 이때 정지한다.</param>
        /// <param name="decelTime">일정 속도 → 0까지. 전체 시간을 넘지 않게 줄인다.</param>
        public static WheelSpinPlan Create(float startAngle, float startSpeed, float targetEndAngle, float nominalSpeed,
            float accelTime, float duration, float decelTime)
        {
            startSpeed = Mathf.Max(0f, startSpeed);
            nominalSpeed = Mathf.Max(1f, nominalSpeed);
            accelTime = Mathf.Clamp(accelTime, 0f, duration);
            decelTime = Mathf.Clamp(decelTime, 0.01f, Mathf.Max(0.01f, duration - accelTime));
            float cruiseTime = Mathf.Max(0f, duration - accelTime - decelTime);

            // 거리 = 가속 구간 (시작+일정)/2·a + 일정·c + 감속 구간 일정/2·d
            float accelPart = startSpeed * accelTime * 0.5f;
            float weight = accelTime * 0.5f + cruiseTime + decelTime * 0.5f;

            // 평소 속도로 돌았을 때의 거리에 가장 가까운, 목표 각도와 맞는 거리를 고른다.
            float nominal = accelPart + nominalSpeed * weight;
            float remainder = Mathf.Repeat(targetEndAngle - startAngle, 360f);
            float distance = remainder + 360f * Mathf.Round((nominal - remainder) / 360f);

            // 너무 느려 보이지 않게 일정 속도를 평소의 절반 이상으로 둔다.
            while (distance - accelPart < nominalSpeed * 0.5f * weight)
            {
                distance += 360f;
            }

            float cruiseSpeed = (distance - accelPart) / weight;
            return new WheelSpinPlan(startAngle, startSpeed, cruiseSpeed, accelTime, cruiseTime, decelTime, distance);
        }

        /// <summary>경과 시간 t의 휠 각도. t ≥ Duration이면 정확히 <see cref="EndAngle"/>.</summary>
        public float AngleAt(float t)
        {
            if (t <= 0f)
            {
                return StartAngle;
            }

            if (t >= Duration)
            {
                return EndAngle;
            }

            if (t < AccelTime)
            {
                return StartAngle + StartSpeed * t + (CruiseSpeed - StartSpeed) * t * t / (2f * AccelTime);
            }

            float afterAccel = StartAngle + (StartSpeed + CruiseSpeed) * AccelTime * 0.5f;
            float cruise = t - AccelTime;
            if (cruise < CruiseTime)
            {
                return afterAccel + CruiseSpeed * cruise;
            }

            float tau = cruise - CruiseTime;
            return afterAccel + CruiseSpeed * CruiseTime + CruiseSpeed * tau - CruiseSpeed * tau * tau / (2f * DecelTime);
        }

        public float SpeedAt(float t)
        {
            if (t <= 0f)
            {
                return StartSpeed;
            }

            if (t >= Duration)
            {
                return 0f;
            }

            if (t < AccelTime)
            {
                return Mathf.Lerp(StartSpeed, CruiseSpeed, t / AccelTime);
            }

            float tau = t - AccelTime - CruiseTime;
            return tau < 0f ? CruiseSpeed : CruiseSpeed * (1f - tau / DecelTime);
        }
    }
}
