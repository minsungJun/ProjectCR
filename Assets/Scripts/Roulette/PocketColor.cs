namespace ProjectCR
{
    public enum PocketColor
    {
        Green,
        Red,
        Black,

        /// <summary>색 없음. 해골 칸 전용이라 색 베팅에 절대 걸리지 않는다.</summary>
        None,
    }

    public enum PocketKind
    {
        /// <summary>0~36 번호 칸. 베팅할 수 있다.</summary>
        Number,

        /// <summary>
        /// 해골 칸. 베팅할 수 없고 어떤 베팅 대상에도 들지 않는다.
        /// 공 하나라도 여기 걸리면 그 회전의 모든 베팅이 낙첨된다. 게임을 끝내지는 않는다.
        /// </summary>
        Skull,
    }

    public static class PocketColorNames
    {
        public static string ToKorean(this PocketColor color)
        {
            switch (color)
            {
                case PocketColor.Red:
                    return "빨강";
                case PocketColor.Black:
                    return "검정";
                case PocketColor.Green:
                    return "초록";
                default:
                    return string.Empty;
            }
        }
    }
}
