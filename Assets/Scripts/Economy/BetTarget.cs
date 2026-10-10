using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectCR
{
    public enum BetKind
    {
        None,

        /// <summary>숫자 하나(0 포함).</summary>
        Straight,

        /// <summary>붙어 있는 두 숫자. 0-1, 0-2, 0-3 포함.</summary>
        Split,

        /// <summary>가로 한 줄 세 숫자(1·2·3 등). 0-1-2, 0-2-3 트리오 포함.</summary>
        Street,

        /// <summary>네 숫자가 만나는 모서리. 0-1-2-3 퍼스트 포 포함.</summary>
        Corner,

        /// <summary>붙어 있는 두 줄 여섯 숫자.</summary>
        SixLine,

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

        /// <summary>스플릿(2칸). 기본 18배.</summary>
        Split,

        /// <summary>스트리트·트리오(3칸). 기본 12배.</summary>
        Street,

        /// <summary>코너·퍼스트 포(4칸). 기본 9배.</summary>
        Corner,

        /// <summary>식스 라인(6칸). 기본 6배.</summary>
        SixLine,

        /// <summary>더즌·컬럼(12칸). 기본 3배.</summary>
        DozenColumn,

        /// <summary>빨강·검정, 홀·짝, 로우·하이(18칸). 기본 2배.</summary>
        EvenMoney,
    }

    /// <summary>
    /// 무엇에 걸었는가. 숫자 하나, 안쪽 베팅(숫자 묶음) 하나, 또는 바깥 베팅 하나.
    ///
    /// 어느 칸이 이 베팅에 포함되는지는 <see cref="Covers"/> 한곳에서 정한다.
    /// 해골은 어떤 베팅에도 포함되지 않는다. 0은 숫자 0 베팅과 0이 든 안쪽 베팅에만 포함된다
    /// (바깥 베팅은 0에서 진다).
    ///
    /// 숫자 하나·안쪽 베팅의 숫자 집합은 0~36을 비트로 담는다(<see cref="Mask"/>).
    /// 어떤 숫자 묶음이 실제로 걸 수 있는 조합인지는 <see cref="InsideBets"/>가 판정한다.
    /// </summary>
    public readonly struct BetTarget : IEquatable<BetTarget>
    {
        private const int MaxNumber = 36;

        private BetTarget(BetKind kind, int number, ulong mask)
        {
            Kind = kind;
            Number = number;
            Mask = mask;
        }

        public static BetTarget None => new BetTarget(BetKind.None, BetManager.NoSelection, 0);

        public BetKind Kind { get; }

        /// <summary>숫자 하나 베팅의 번호. 그 밖에는 <see cref="BetManager.NoSelection"/>.</summary>
        public int Number { get; }

        /// <summary>숫자 하나·안쪽 베팅이 포함하는 0~36 비트 집합. 바깥 베팅은 0.</summary>
        public ulong Mask { get; }

        public bool IsNone => Kind == BetKind.None;
        public bool IsStraight => Kind == BetKind.Straight;

        public bool IsInside =>
            Kind == BetKind.Split || Kind == BetKind.Street || Kind == BetKind.Corner || Kind == BetKind.SixLine;

        /// <summary>번호 범위를 벗어난 값(해골 -2, 37 등)도 담는다. 걸 수 있는지는 BetManager가 검증한다.</summary>
        public static BetTarget Straight(int number)
        {
            return new BetTarget(BetKind.Straight, number, IsTableNumber(number) ? Bit(number) : 0);
        }

        /// <summary>안쪽 베팅. 숫자 순서는 상관없다. 실제로 걸 수 있는 조합인지는 <see cref="InsideBets.IsValid"/>.</summary>
        public static BetTarget Inside(BetKind kind, params int[] numbers)
        {
            if (kind != BetKind.Split && kind != BetKind.Street && kind != BetKind.Corner && kind != BetKind.SixLine)
            {
                throw new ArgumentException($"안쪽 베팅이 아니다: {kind}");
            }

            ulong mask = 0;
            foreach (int number in numbers)
            {
                if (!IsTableNumber(number))
                {
                    return new BetTarget(kind, BetManager.NoSelection, 0);
                }

                mask |= Bit(number);
            }

            return new BetTarget(kind, BetManager.NoSelection, mask);
        }

        public static BetTarget Outside(BetKind kind)
        {
            if (kind == BetKind.None || kind == BetKind.Straight || kind == BetKind.Split ||
                kind == BetKind.Street || kind == BetKind.Corner || kind == BetKind.SixLine)
            {
                throw new ArgumentException($"바깥 베팅이 아니다: {kind}");
            }

            return new BetTarget(kind, BetManager.NoSelection, 0);
        }

        public PayoutClass PayoutClass
        {
            get
            {
                switch (Kind)
                {
                    case BetKind.Straight:
                        return PayoutClass.Straight;
                    case BetKind.Split:
                        return PayoutClass.Split;
                    case BetKind.Street:
                        return PayoutClass.Street;
                    case BetKind.Corner:
                        return PayoutClass.Corner;
                    case BetKind.SixLine:
                        return PayoutClass.SixLine;
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

        /// <summary>숫자 하나·안쪽 베팅의 숫자들(오름차순). 바깥 베팅은 비어 있다.</summary>
        public IEnumerable<int> Numbers
        {
            get
            {
                for (int n = 0; n <= MaxNumber; n++)
                {
                    if ((Mask & Bit(n)) != 0)
                    {
                        yield return n;
                    }
                }
            }
        }

        /// <summary>이 공이 들어간 칸이 베팅에 포함되는가.</summary>
        public bool Covers(BallResult ball)
        {
            // 해골은 번호가 -2라 아래 계산(홀짝 등)에 들어가면 안 된다. 맨 먼저 거른다.
            return !ball.IsSkull && CoversNumber(ball.Number, ball.Color);
        }

        /// <summary>번호판의 숫자 칸 하나가 베팅에 포함되는가. UI가 포함 칸을 표시할 때도 쓴다.</summary>
        public bool CoversNumber(int number, PocketColor color)
        {
            if (!IsTableNumber(number))
            {
                return false;
            }

            if (Kind == BetKind.Straight || IsInside)
            {
                return (Mask & Bit(number)) != 0;
            }

            // 바깥 베팅은 1–36만 본다. 0은 모든 바깥 베팅에서 진다.
            if (number < 1)
            {
                return false;
            }

            switch (Kind)
            {
                case BetKind.Red:
                    return color == PocketColor.Red;
                case BetKind.Black:
                    return color == PocketColor.Black;
                case BetKind.Odd:
                    return number % 2 == 1;
                case BetKind.Even:
                    return number % 2 == 0;
                case BetKind.Low:
                    return number <= 18;
                case BetKind.High:
                    return number >= 19;
                case BetKind.Dozen1:
                    return number <= 12;
                case BetKind.Dozen2:
                    return number >= 13 && number <= 24;
                case BetKind.Dozen3:
                    return number >= 25;
                case BetKind.Column1:
                    return number % 3 == 1;
                case BetKind.Column2:
                    return number % 3 == 2;
                case BetKind.Column3:
                    return number % 3 == 0;
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
                case BetKind.Split:
                case BetKind.Street:
                case BetKind.Corner:
                case BetKind.SixLine:
                    return $"{InsideLabel()} {JoinNumbers()}";
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

        public bool Equals(BetTarget other) => Kind == other.Kind && Number == other.Number && Mask == other.Mask;

        public override bool Equals(object obj) => obj is BetTarget other && Equals(other);

        public override int GetHashCode() => (((int)Kind * 397) ^ Number) * 397 ^ Mask.GetHashCode();

        public static bool operator ==(BetTarget a, BetTarget b) => a.Equals(b);

        public static bool operator !=(BetTarget a, BetTarget b) => !a.Equals(b);

        public override string ToString()
        {
            if (IsStraight)
            {
                return $"Straight {Number}";
            }

            return IsInside ? $"{Kind} {JoinNumbers()}" : Kind.ToString();
        }

        private string InsideLabel()
        {
            bool hasZero = (Mask & Bit(0)) != 0;
            switch (Kind)
            {
                case BetKind.Split:
                    return "스플릿";
                case BetKind.Street:
                    return hasZero ? "트리오" : "스트리트";
                case BetKind.Corner:
                    return hasZero ? "퍼스트 포" : "코너";
                default:
                    return "식스 라인";
            }
        }

        private string JoinNumbers()
        {
            var text = new StringBuilder();
            foreach (int n in Numbers)
            {
                if (text.Length > 0)
                {
                    text.Append('·');
                }

                text.Append(n);
            }

            return text.ToString();
        }

        private static bool IsTableNumber(int number) => number >= 0 && number <= MaxNumber;

        private static ulong Bit(int number) => 1UL << number;
    }
}
