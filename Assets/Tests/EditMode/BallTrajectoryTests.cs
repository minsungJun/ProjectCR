using NUnit.Framework;
using UnityEngine;

namespace ProjectCR.Tests
{
    /// <summary>
    /// 공 착지 계산. 어떤 시작 각도·회전 수·프레임 간격에서도 마지막 각도가 목표 칸 각도와 같아야 한다.
    /// 실제 3D 안착은 플레이 모드의 37칸 순회 검증으로 따로 본다.
    /// </summary>
    public class BallTrajectoryTests
    {
        private const float Tolerance = 1e-3f;

        private SpinMotionSettings settings;

        private AnimationCurve Curve => settings.angleCurve;

        [OneTimeSetUp]
        public void CreateSettings() => settings = ScriptableObject.CreateInstance<SpinMotionSettings>();

        [OneTimeTearDown]
        public void DestroySettings() => Object.DestroyImmediate(settings);

        private static float AngleDifference(float a, float b) => Mathf.Abs(Mathf.DeltaAngle(a, b));

        [Test]
        public void EndAngle_MatchesEveryPocket_FromAnyStart()
        {
            float[] starts = { 0f, 47.3f, 180f, 359.9f, -720f, 1234.5f };
            int[] revolutions = { 0, 3, 8 };

            for (int pocket = 0; pocket < 37; pocket++)
            {
                float target = pocket * 360f / 37f;
                foreach (float start in starts)
                {
                    foreach (int revs in revolutions)
                    {
                        BallTrajectory trajectory = BallTrajectory.Create(start, target, revs);

                        Assert.Less(AngleDifference(trajectory.EndAngle, target), Tolerance, $"칸 {pocket}, 시작 {start}, {revs}바퀴");
                        Assert.LessOrEqual(trajectory.TotalDelta, -360f * revs, "휠 반대 방향으로 최소 revs바퀴 돈다.");
                        Assert.Greater(trajectory.TotalDelta, -360f * (revs + 1));
                    }
                }
            }
        }

        [TestCase(15)]
        [TestCase(60)]
        [TestCase(144)]
        [TestCase(240)]
        public void FinalAngle_IsExact_RegardlessOfFrameRate(int fps)
        {
            const float duration = 6f;
            float target = 17 * 360f / 37f;
            BallTrajectory trajectory = BallTrajectory.Create(123f, target, 6);

            // 프레임이 고르지 않아도(가끔 큰 끊김) 마지막 프레임은 u = 1로 고정된다.
            float elapsed = 0f;
            float u = 0f;
            int frame = 0;
            while (u < 1f)
            {
                float dt = 1f / fps * (frame % 17 == 0 ? 3.7f : 1f);
                elapsed += dt;
                u = BallTrajectory.Progress(elapsed, duration);
                frame++;
            }

            Assert.Less(AngleDifference(trajectory.AngleAt(u, Curve), target), Tolerance);
        }

        [Test]
        public void HugeFrameHitch_StillLandsOnTarget()
        {
            float target = 0f;
            BallTrajectory trajectory = BallTrajectory.Create(200f, target, 6);

            float u = BallTrajectory.Progress(elapsed: 999f, duration: 6f);

            Assert.AreEqual(1f, u);
            Assert.Less(AngleDifference(trajectory.AngleAt(u, Curve), target), Tolerance);
        }

        [Test]
        public void LocalPosition_And_AngleOf_RoundTrip()
        {
            Vector3 front = BallTrajectory.LocalPosition(0f, 1f, 0.5f);
            Assert.That(front.z, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(front.x, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(front.y, Is.EqualTo(0.5f));

            Vector3 right = BallTrajectory.LocalPosition(90f, 2f, 0f);
            Assert.That(right.x, Is.EqualTo(2f).Within(1e-5f), "시계 방향(위에서 볼 때) 90°는 +X.");

            for (float angle = 0f; angle < 360f; angle += 13.7f)
            {
                Vector3 p = BallTrajectory.LocalPosition(angle, 0.72f, 0.03f);
                Assert.Less(AngleDifference(BallTrajectory.AngleOf(p), angle), Tolerance);
                Assert.That(BallTrajectory.RadiusOf(p), Is.EqualTo(0.72f).Within(1e-5f));
            }
        }

        [Test]
        public void UnityYRotation_MatchesAngleConvention()
        {
            // 빌더는 칸을 Quaternion.Euler(0, angle, 0)으로 돌리고, 공은 LocalPosition으로 놓는다. 둘이 같은 방향이어야 한다.
            for (float angle = 0f; angle < 360f; angle += 30f)
            {
                Vector3 rotated = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Vector3 computed = BallTrajectory.LocalPosition(angle, 1f, 0f);
                Assert.Less((rotated - computed).magnitude, 1e-5f, $"{angle}°");
            }
        }
    }
}
