using System.Globalization;

namespace ProjectCR
{
    /// <summary>화면에 보이는 숫자 문구. <b>표시용 반올림만</b> 하고, 정산은 반올림하지 않은 값을 쓴다.</summary>
    public static class NumberText
    {
        /// <summary>배율. 2 → "2", 2.25 → "2.25", 3.375 → "3.38", 50 이상은 소수 한 자리, 10만 이상은 정수에 쉼표.</summary>
        public static string Multiplier(decimal value)
        {
            if (value >= 100_000m)
            {
                return decimal.Round(value, 0).ToString("N0", CultureInfo.InvariantCulture);
            }

            int digits = value >= 50m ? 1 : 2;
            return decimal.Round(value, digits, System.MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.InvariantCulture);
        }

        /// <summary>확률(0~1)을 퍼센트로. 10% 이상은 소수 한 자리(47.4%), 그 아래는 두 자리(2.63%).</summary>
        public static string Percent(double probability)
        {
            double percent = probability * 100d;
            string format = percent >= 10d ? "0.0" : "0.00";
            return percent.ToString(format, CultureInfo.InvariantCulture) + "%";
        }
    }
}
