using System;

namespace ProjectCR
{
    /// <summary>
    /// 판의 한 칸을 길게 누를 때 칩을 계속 놓는 시간 계산. 입력은 읽지 않고 시간만 잰다(<see cref="TableBetInput"/>가 매 프레임 넘긴다).
    /// 누른 순간의 칩 하나는 TableBetInput이 따로 놓는다. 같은 칸을 delay초 누르고 있으면 그때부터 칩을 놓고,
    /// 간격을 interval에서 fastInterval까지 ramp초에 걸쳐 줄인다. 떼거나 다른 칸으로 옮기면 멈춘다.
    /// </summary>
    public sealed class HoldRepeat
    {
        // 프레임이 크게 끊겨도 한 프레임에 칩이 와르르 놓이지 않게 막는다.
        private const int MaxPerTick = 5;

        private readonly float delay;
        private readonly float interval;
        private readonly float fastInterval;
        private readonly float ramp;

        private float held;
        private float untilNext;

        public HoldRepeat(float delay, float interval, float fastInterval, float ramp)
        {
            this.delay = Math.Max(0.01f, delay);
            this.interval = Math.Max(0.01f, interval);
            this.fastInterval = Math.Max(0.01f, Math.Min(this.interval, fastInterval));
            this.ramp = Math.Max(0f, ramp);
        }

        /// <summary>누르고 있는 칸. 없으면 None.</summary>
        public BetTarget Target { get; private set; } = BetTarget.None;

        /// <summary>delay가 지나 칩을 계속 놓는 중인가.</summary>
        public bool IsRepeating => !Target.IsNone && held >= delay;

        /// <summary>칸을 새로 눌렀다(누른 순간의 칩은 부르는 쪽이 놓는다).</summary>
        public void Press(BetTarget target)
        {
            Target = target;
            held = 0f;
            untilNext = 0f;
        }

        public void Cancel()
        {
            Target = BetTarget.None;
            held = 0f;
            untilNext = 0f;
        }

        /// <summary>
        /// 한 프레임 진행. 같은 칸을 누르고 있지 않으면 멈추고 0. 아니면 이번 프레임에 더 놓을 칩 수(0~5).
        /// </summary>
        public int Tick(BetTarget target, bool pressed, float deltaTime)
        {
            if (Target.IsNone || !pressed || target != Target)
            {
                Cancel();
                return 0;
            }

            float before = held;
            held += Math.Max(0f, deltaTime);
            if (held < delay)
            {
                return 0;
            }

            // delay를 넘긴 프레임에 첫 칩. 넘친 시간만큼 다음 칩이 당겨진다.
            untilNext -= before < delay ? held - delay : deltaTime;
            int count = 0;
            while (untilNext <= 0f && count < MaxPerTick)
            {
                count++;
                untilNext += CurrentInterval();
            }

            if (untilNext < 0f)
            {
                untilNext = 0f;
            }

            return count;
        }

        /// <summary>지금 칩 사이 간격: delay 직후 interval, ramp초 뒤 fastInterval.</summary>
        public float CurrentInterval()
        {
            float t = ramp > 0f ? Math.Min(1f, Math.Max(0f, (held - delay) / ramp)) : 1f;
            return interval + (fastInterval - interval) * t;
        }
    }
}
