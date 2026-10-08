namespace ProjectCR
{
    public enum PocketColor
    {
        Green,
        Red,
        Black,
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
                default:
                    return "초록";
            }
        }
    }
}
