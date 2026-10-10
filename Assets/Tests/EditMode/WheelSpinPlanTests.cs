using NUnit.Framework;
using UnityEngine;

namespace ProjectCR.Tests
{
    /// <summary>
    /// 휠 정지 계획. 어떤 시작 각도·속도에서도 정해진 시간에 목표 각도(360° 단위)로 멈추고,
    /// 속도는 끊기지 않고 거꾸로 돌지 않으며, 평소 속도에서 크게 벗어나지 않아야 한다.
    /// </summary>
    public class WheelSpinPlanTests
    {
        private const float Nominal = 120f;
        private const float Accel = 0.5f;
        private const float Decel = 3f;

        private static float Error(float angle, float target) => Mathf.Abs(Mathf.DeltaAngle(angle, target));

        [Test]
        public void StopsAtTargetAngle_FromAnyStart()
        {
            float[] durations = { 6f, 7f, 8.5f };
            float[] startSpeeds = { 0f, 12f, 120f };

            for (float start = 0f; start < 360f; start += 37f)
            {
                for (float target = 0f; target < 360f; target += 23f)
                {
                    foreach (float duration in durations)
                    {
                        foreach (float speed in startSpeeds)
                        {
                            WheelSpinPlan plan = WheelSpinPlan.Create(start, speed, target, Nominal, Accel, duration, Decel);

                            Assert.Less(Error(plan.EndAngle, target), 1e-2f, $"start {start}, target {target}, {duration}s, v0 {speed}");
                            Assert.Less(Error(plan.AngleAt(plan.Duration), target), 1e-2f);
                            Assert.AreEqual(duration, plan.Duration, 1e-4f);
                            Assert.That(plan.CruiseSpeed, Is.InRange(Nominal * 0.5f, Nominal * 1.5f));
                        }
                    }
                }
            }
        }

        [Test]
        public void Speed_IsContinuous_NonNegative_AndEndsAtZero()
        {
            WheelSpinPlan plan = WheelSpinPlan.Create(200f, 12f, 33f, Nominal, Accel, 7f, Decel);
            float accelEnd = plan.AccelTime;
            float decelStart = plan.AccelTime + plan.CruiseTime;

            Assert.AreEqual(12f, plan.SpeedAt(0f), 1e-4f);
            Assert.AreEqual(plan.SpeedAt(accelEnd - 1e-4f), plan.SpeedAt(accelEnd + 1e-4f), 0.1f);
            Assert.AreEqual(plan.SpeedAt(decelStart - 1e-4f), plan.SpeedAt(decelStart + 1e-4f), 0.1f);
            Assert.AreEqual(0f, plan.SpeedAt(plan.Duration), 1e-4f);

            float previous = plan.AngleAt(0f);
            for (float t = 0f; t <= plan.Duration; t += 0.01f)
            {
                Assert.GreaterOrEqual(plan.SpeedAt(t), 0f);
                float angle = plan.AngleAt(t);
                Assert.GreaterOrEqual(angle, previous - 1e-3f, "휠이 거꾸로 돌면 안 된다.");
                previous = angle;
            }
        }

        [Test]
        public void Angle_MatchesIntegratedSpeed()
        {
            WheelSpinPlan plan = WheelSpinPlan.Create(10f, 0f, 250f, Nominal, Accel, 6.5f, Decel);

            const float dt = 0.0005f;
            float integrated = plan.StartAngle;
            for (float t = 0f; t < plan.Duration; t += dt)
            {
                integrated += plan.SpeedAt(t + dt * 0.5f) * dt;
            }

            Assert.AreEqual(plan.EndAngle, integrated, 0.5f);
        }

        [TestCase(15)]
        [TestCase(60)]
        [TestCase(240)]
        public void FinalAngle_IsExact_RegardlessOfFrameRate(int fps)
        {
            const float target = 180f;
            WheelSpinPlan plan = WheelSpinPlan.Create(77f, 0f, target, Nominal, Accel, 7f, Decel);

            // 가끔 큰 끊김이 있어도 마지막 프레임은 Duration을 넘기며 정확히 EndAngle이 된다.
            float elapsed = 0f;
            int frame = 0;
            while (elapsed < plan.Duration)
            {
                elapsed += 1f / fps * (frame % 13 == 0 ? 4f : 1f);
                frame++;
            }

            Assert.Less(Error(plan.AngleAt(elapsed), target), 1e-2f);
        }

        [Test]
        public void PocketAtRest_LandsOnRestAngle()
        {
            // 휠 각도 + 칸의 휠 로컬 각도 = 테이블 위 각도. 칸 i(38칸 중)가 6시(180°)에 오게 한다.
            for (int pocket = 0; pocket < 38; pocket++)
            {
                float pocketLocal = pocket * 360f / 38f;
                WheelSpinPlan plan = WheelSpinPlan.Create(123f, 12f, 180f - pocketLocal, Nominal, Accel, 7f, Decel);

                Assert.Less(Error(plan.EndAngle + pocketLocal, 180f), 1e-2f, $"칸 {pocket}");
            }
        }
    }
}
