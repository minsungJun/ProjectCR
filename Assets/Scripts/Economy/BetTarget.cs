using System;

namespace ProjectCR
{
    public enum BetKind
    {
        None,

        /// <summary>숫자 하나(0 포함).</summary>
        Straight,

        Red,
        Black,
        Odd,
        Even,

        /// <summary>1–18.</summary>
        Low,

        /// <summary>19–36.</summary>
        High,

        /// <summary>1–12.</summary>
        Dozen1,

        /// <summary>13–24.</summary>
        Dozen2,

        /// <summary>25–36.</summary>
        Dozen3,

        /// <summary>1, 4, 7, …, 34 (번호판 아래 줄).</summary>
        Column1,

        /// <summary>2, 5, 8, …, 35 (가운데 줄).</summary>
        Column2,

        /// <summary>3, 6, 9, …, 36 (위 줄).</summary>
        Column3,
    }

    /// <summary>지급 배수 묶음. 배수 값은 GameBalance에 있고 회전마다 SpinConfig로 복사된다.</summary>
    public enum PayoutClass
    {
        /// <summary>숫자 하나. 기본 36배.</summary>
        Straight,

        /// <summary>더즌·컬럼(12칸). 기본 3배.</summary>
        DozenColumn,

        /// <summary>빨강·검정, 홀·짝, 로우·하이(18칸). 기본 2배.</summary>
        EvenMoney,
    }

    /// <summary>
    /// 무엇에 걸었는가. 숫자 하나 또는 바깥 베팅 하나.
    ///
    /// 어느 칸이 이 베팅에 포함되는지는 <see cref="Covers"/> 한곳에서 정한다.
    /// 해골은 어떤 베팅에도 포함되지 않고, 0은 숫자 0 베팅에만 포함된다(바깥 베팅은 0에서 진다).
    /// </summary>
    public readonly struct BetTarget : IEquatable<BetTarget>
    {
        private BetTarget(BetKind kind, int number)
        {
            Kind = kind;
            Number = number;
        }

        public static BetTarget None => new BetTarget(BetKind.None, BetManager.NoSelection);

        public BetKind Kind { get; }

        /// <summary>숫자 베팅의 번호. 바깥 베팅이면 <see cref="BetManager.NoSelection"/>.</summary>
        public int Number { get; }

        public bool IsNone => Kind == BetKind.None;
        public bool IsStraight => Kind == BetKind.Straight;

        public static BetTarget Straight(int number) => new BetTarget(BetKind.Straight, number);

        public static BetTarget Outside(BetKind kind)
        {
            if (kind == BetKind.None || kind == BetKind.Straight)
            {
                throw new ArgumentException($"바깥 베팅이 아니다: {kind}");
            }

            return new BetTarget(kind, BetManager.NoSelection);
        }

        public PayoutClass PayoutClass
        {
            get
            {
                switch (Kind)
                {
                    case BetKind.Straight:
                        return PayoutClass.Straight;
                    case BetKind.Dozen1:
                    case BetKind.Dozen2:
                    case BetKind.Dozen3:
                    case BetKind.Column1:
                    case BetKind.Column2:
                    case BetKind.Column3:
                        return PayoutClass.DozenColumn;
                    default:
                        return PayoutClass.EvenMoney;
                }
            }
        }

        /// <summary>이 공이 들어간 칸이 베팅에 포함되는가.</summary>
        public bool Covers(BallResult ball)
        {
            // 해골은 번호가 -2라 아래 계산(홀짝 등)에 들어가면 안 된다. 맨 먼저 거른다.
            if (ball.IsSkull)
            {
                return false;
            }

            int n = ball.Number;
            if (Kind == BetKind.Straight)
            {
                return n == Number;
            }

            // 바깥 베팅은 1–36만 본다. 0은 모든 바깥 베팅에서 진다.
            if (n < 1 || n > 36)
            {
                return false;
            }

            switch (Kind)
            {
                case BetKind.Red:
                    return ball.Color == PocketColor.Red;
                case BetKind.Black:
                    return ball.Color == PocketColor.Black;
                case BetKind.Odd:
                    return n % 2 == 1;
                case BetKind.Even:
                    return n % 2 == 0;
                case BetKind.Low:
                    return n <= 18;
                case BetKind.High:
                    return n >= 19;
                case BetKind.Dozen1:
                    return n <= 12;
                case BetKind.Dozen2:
                    return n >= 13 && n <= 24;
                case BetKind.Dozen3:
                    return n >= 25;
                case BetKind.Column1:
                    return n % 3 == 1;
                case BetKind.Column2:
                    return n % 3 == 2;
                case BetKind.Column3:
                    return n % 3 == 0;
                default:
                    return false;
            }
        }

        public string ToKorean(RouletteDefinition definition)
        {
            switch (Kind)
            {
                case BetKind.None:
                    return "—";
                case BetKind.Straight:
                    return $"{Number} ({definition.ColorOfNumber(Number).ToKorean()})";
                default:
                    return OutsideLabel(Kind);
            }
        }

        public static string OutsideLabel(BetKind kind)
        {
            switch (kind)
            {
                case BetKind.Red:
                    return "빨강";
                case BetKind.Black:
                    return "검정";
                case BetKind.Odd:
                    return "홀수";
                case BetKind.Even:
                    return "짝수";
                case BetKind.Low:
                    return "1–18";
                case BetKind.High:
                    return "19–36";
                case BetKind.Dozen1:
                    return "1–12";
                case BetKind.Dozen2:
                    return "13–24";
                case BetKind.Dozen3:
                    return "25–36";
                case BetKind.Column1:
                    return "1열";
                case BetKind.Column2:
                    return "2열";
                case BetKind.Column3:
                    return "3열";
                default:
                    return kind.ToString();
            }
        }

        public bool Equals(BetTarget other) => Kind == other.Kind && Number == other.Number;

        public override bool Equals(object obj) => obj is BetTarget other && Equals(other);

        public override int GetHashCode() => ((int)Kind * 397) ^ Number;

        public static bool operator ==(BetTarget a, BetTarget b) => a.Equals(b);

        public static bool operator !=(BetTarget a, BetTarget b) => !a.Equals(b);

        public override string ToString() => IsStraight ? $"Straight {Number}" : Kind.ToString();
    }
}
