using System;
using System.Collections.Generic;

namespace ProjectCR
{
    /// <summary>
    /// 음료가 강화하는 베팅 묶음. 베팅 하나는 정확히 한 묶음에 속한다(<see cref="DrinkGroups.Of"/>).
    /// 결과 번호가 아니라 <b>베팅 종류</b>로 정한다. 빨간 숫자에 건 숫자 베팅은 Number일 뿐 Color가 아니다.
    /// 해골은 베팅할 수 없으니 어느 묶음에도 없다.
    /// </summary>
    public enum DrinkGroup
    {
        /// <summary>1열·2열·3열.</summary>
        Column,

        /// <summary>1–12, 13–24, 25–36.</summary>
        Dozen,

        /// <summary>홀·짝.</summary>
        Parity,

        /// <summary>빨강·검정.</summary>
        Color,

        /// <summary>1–18·19–36.</summary>
        Range,

        /// <summary>숫자 하나(0 포함), 스플릿, 스트리트·트리오, 코너·퍼스트 포, 식스 라인.</summary>
        Number,
    }

    public static class DrinkGroups
    {
        public static readonly int Count = Enum.GetValues(typeof(DrinkGroup)).Length;

        /// <summary>베팅 종류가 속한 음료 묶음. None은 베팅이 아니라서 예외.</summary>
        public static DrinkGroup Of(BetKind kind)
        {
            switch (kind)
            {
                case BetKind.Column1:
                case BetKind.Column2:
                case BetKind.Column3:
                    return DrinkGroup.Column;
                case BetKind.Dozen1:
                case BetKind.Dozen2:
                case BetKind.Dozen3:
                    return DrinkGroup.Dozen;
                case BetKind.Odd:
                case BetKind.Even:
                    return DrinkGroup.Parity;
                case BetKind.Red:
                case BetKind.Black:
                    return DrinkGroup.Color;
                case BetKind.Low:
                case BetKind.High:
                    return DrinkGroup.Range;
                case BetKind.Straight:
                case BetKind.Split:
                case BetKind.Street:
                case BetKind.Corner:
                case BetKind.SixLine:
                    return DrinkGroup.Number;
                default:
                    throw new ArgumentException($"음료 묶음이 없는 베팅: {kind}");
            }
        }

        public static string ToKorean(this DrinkGroup group)
        {
            switch (group)
            {
                case DrinkGroup.Column:
                    return "컬럼";
                case DrinkGroup.Dozen:
                    return "더즌";
                case DrinkGroup.Parity:
                    return "홀짝";
                case DrinkGroup.Color:
                    return "색상";
                case DrinkGroup.Range:
                    return "높고 낮음";
                default:
                    return "숫자";
            }
        }

        /// <summary>이 묶음에 들어가는 베팅을 짧게. 카드 설명용.</summary>
        public static string BetsKorean(this DrinkGroup group)
        {
            switch (group)
            {
                case DrinkGroup.Column:
                    return "1열·2열·3열";
                case DrinkGroup.Dozen:
                    return "1–12·13–24·25–36";
                case DrinkGroup.Parity:
                    return "홀·짝";
                case DrinkGroup.Color:
                    return "빨강·검정";
                case DrinkGroup.Range:
                    return "1–18·19–36";
                default:
                    return "숫자·스플릿·스트리트·코너·식스 라인";
            }
        }

        /// <summary>
        /// 이 묶음에서 음료 대상이 될 수 있는 베팅(숫자 묶음은 0~36 숫자 한 칸씩). 해골은 없다.
        /// 지급 음료는 후보를 만들 때 이 중 하나를 뽑고, 확률 음료는 마실 때 플레이어가 이 중에서 고른다.
        /// </summary>
        public static List<BetTarget> Targets(this DrinkGroup group)
        {
            var options = new List<BetTarget>();
            switch (group)
            {
                case DrinkGroup.Color:
                    options.Add(BetTarget.Outside(BetKind.Red));
                    options.Add(BetTarget.Outside(BetKind.Black));
                    break;
                case DrinkGroup.Parity:
                    options.Add(BetTarget.Outside(BetKind.Odd));
                    options.Add(BetTarget.Outside(BetKind.Even));
                    break;
                case DrinkGroup.Range:
                    options.Add(BetTarget.Outside(BetKind.Low));
                    options.Add(BetTarget.Outside(BetKind.High));
                    break;
                case DrinkGroup.Column:
                    options.Add(BetTarget.Outside(BetKind.Column1));
                    options.Add(BetTarget.Outside(BetKind.Column2));
                    options.Add(BetTarget.Outside(BetKind.Column3));
                    break;
                case DrinkGroup.Dozen:
                    options.Add(BetTarget.Outside(BetKind.Dozen1));
                    options.Add(BetTarget.Outside(BetKind.Dozen2));
                    options.Add(BetTarget.Outside(BetKind.Dozen3));
                    break;
                default:
                    for (int number = 0; number <= 36; number++)
                    {
                        options.Add(BetTarget.Straight(number));
                    }

                    break;
            }

            return options;
        }

        /// <summary>이 묶음의 대표 베팅. 카드에 "기본 → 강화" 배율을 보여 줄 때 쓴다.</summary>
        public static BetTarget Representative(this DrinkGroup group)
        {
            switch (group)
            {
                case DrinkGroup.Column:
                    return BetTarget.Outside(BetKind.Column1);
                case DrinkGroup.Dozen:
                    return BetTarget.Outside(BetKind.Dozen1);
                case DrinkGroup.Parity:
                    return BetTarget.Outside(BetKind.Odd);
                case DrinkGroup.Color:
                    return BetTarget.Outside(BetKind.Red);
                case DrinkGroup.Range:
                    return BetTarget.Outside(BetKind.Low);
                default:
                    return BetTarget.Straight(0);
            }
        }
    }
}
