using NUnit.Framework;

namespace ProjectCR.Tests
{
    /// <summary>판의 한 칸을 길게 누르면 2초 뒤부터 칩을 계속 놓고, 갈수록 빨라지며, 떼거나 칸을 옮기면 멈추는지.</summary>
    public class HoldRepeatTests
    {
        private static readonly BetTarget Red = BetTarget.Outside(BetKind.Red);
        private static readonly BetTarget Black = BetTarget.Outside(BetKind.Black);

        private static HoldRepeat Make() => new HoldRepeat(2f, 0.15f, 0.04f, 2f);

        /// <summary>dt 간격으로 seconds초 동안 누르고 있을 때 더 놓인 칩 수.</summary>
        private static int Hold(HoldRepeat hold, BetTarget target, float seconds, float dt = 1f / 60f)
        {
            int total = 0;
            for (float t = 0f; t < seconds - 1e-4f; t += dt)
            {
                total += hold.Tick(target, true, dt);
            }

            return total;
        }

        [Test]
        public void NothingExtra_BeforeTwoSeconds()
        {
            HoldRepeat hold = Make();
            hold.Press(Red);

            Assert.AreEqual(0, Hold(hold, Red, 1.95f));
            Assert.IsFalse(hold.IsRepeating);
        }

        [Test]
        public void StartsAtTwoSeconds_ThenSpeedsUp()
        {
            HoldRepeat hold = Make();
            hold.Press(Red);
            Hold(hold, Red, 1.95f);

            int first = Hold(hold, Red, 0.55f); // 2.0 ~ 2.5초: 0.15초 간격 언저리
            Assert.IsTrue(hold.IsRepeating);
            Assert.That(first, Is.InRange(3, 5));

            Hold(hold, Red, 1.5f); // 4초까지 빨라진다
            int fast = Hold(hold, Red, 0.5f); // 0.04초 간격
            Assert.That(fast, Is.InRange(11, 14));
            Assert.Greater(fast, first);
        }

        [Test]
        public void Release_Or_MoveToAnotherSpot_Stops()
        {
            HoldRepeat hold = Make();
            hold.Press(Red);
            Hold(hold, Red, 2.5f);
            Assert.AreEqual(0, hold.Tick(Red, false, 0.1f), "떼면 멈춘다");
            Assert.IsTrue(hold.Target.IsNone);
            Assert.AreEqual(0, Hold(hold, Red, 3f), "다시 누르기 전에는 놓지 않는다");

            hold.Press(Red);
            Hold(hold, Red, 2.5f);
            Assert.AreEqual(0, hold.Tick(Black, true, 0.1f), "다른 칸으로 옮기면 멈춘다");
            Assert.AreEqual(0, Hold(hold, Black, 3f));
        }

        [Test]
        public void Cancel_StopsUntilPressedAgain()
        {
            HoldRepeat hold = Make();
            hold.Press(Red);
            Hold(hold, Red, 2.5f);
            hold.Cancel();

            Assert.AreEqual(0, Hold(hold, Red, 3f));
            hold.Press(Red);
            Assert.AreEqual(0, Hold(hold, Red, 1.9f), "다시 누르면 처음부터 2초를 잰다");
        }

        [Test]
        public void HugeFrameHitch_PlacesAtMostFive()
        {
            HoldRepeat hold = Make();
            hold.Press(Red);
            Assert.That(hold.Tick(Red, true, 10f), Is.InRange(1, 5));
        }
    }
}
