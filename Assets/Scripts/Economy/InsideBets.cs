using System.Collections.Generic;

namespace ProjectCR
{
    /// <summary>번호판 위 안쪽 베팅 하나와 그 클릭 지점.</summary>
    public readonly struct InsideBetSpot
    {
        public InsideBetSpot(BetTarget target, float x, float y)
        {
            Target = target;
            X = x;
            Y = y;
        }

        public BetTarget Target { get; }

        /// <summary>번호판 좌표(칸 단위). 0 = 0번 칸과 첫 열의 경계, 열 c의 가운데 = c + 0.5. 12 = 마지막 열 오른쪽.</summary>
        public float X { get; }

        /// <summary>번호판 좌표(칸 단위). 0 = 위 줄 윗변, 행 r의 가운데 = r + 0.5, 3 = 아래 줄 아랫변.</summary>
        public float Y { get; }
    }

    /// <summary>
    /// 걸 수 있는 안쪽 베팅 전체(108개)와 번호판 위 위치. 번호판 배치(위 줄 3·6·…·36, 아래 줄 1·4·…·34)에서 만든다.
    ///
    /// 스플릿 60(가로 33, 세로 24, 0과 1·2·3 각각 3), 스트리트 14(12줄 + 트리오 0·1·2, 0·2·3),
    /// 코너 23(22 + 퍼스트 포 0·1·2·3), 식스 라인 11.
    /// 위치는 칸 사이 경계에 둔다: 두 칸 사이 = 스플릿, 네 칸이 만나는 점 = 코너, 줄 아랫변 = 스트리트,
    /// 두 줄 사이 아랫변 = 식스 라인. 0과의 조합은 0번 칸과 첫 열의 경계(x = 0)에 있다.
    /// </summary>
    public static class InsideBets
    {
        private static List<InsideBetSpot> spots;
        private static HashSet<BetTarget> valid;

        public static IReadOnlyList<InsideBetSpot> All
        {
            get
            {
                Build();
                return spots;
            }
        }

        public static bool IsValid(BetTarget target)
        {
            Build();
            return valid.Contains(target);
        }

        /// <summary>열 c(0~11, 왼쪽부터), 행 r(0~2, 위부터)의 숫자.</summary>
        public static int NumberAt(int column, int row) => column * 3 + (3 - row);

        private static void Build()
        {
            if (spots != null)
            {
                return;
            }

            spots = new List<InsideBetSpot>(108);

            for (int c = 0; c < 12; c++)
            {
                // 세로로 붙은 두 칸(같은 열, 위·아래 행)
                for (int r = 0; r < 2; r++)
                {
                    Add(BetKind.Split, c + 0.5f, r + 1f, NumberAt(c, r), NumberAt(c, r + 1));
                }

                // 줄 아랫변: 스트리트
                Add(BetKind.Street, c + 0.5f, 3f, NumberAt(c, 0), NumberAt(c, 1), NumberAt(c, 2));

                if (c == 11)
                {
                    continue;
                }

                // 가로로 붙은 두 칸(옆 열)
                for (int r = 0; r < 3; r++)
                {
                    Add(BetKind.Split, c + 1f, r + 0.5f, NumberAt(c, r), NumberAt(c + 1, r));
                }

                // 네 칸이 만나는 점
                for (int r = 0; r < 2; r++)
                {
                    Add(BetKind.Corner, c + 1f, r + 1f,
                        NumberAt(c, r), NumberAt(c, r + 1), NumberAt(c + 1, r), NumberAt(c + 1, r + 1));
                }

                // 두 줄 사이 아랫변: 식스 라인
                Add(BetKind.SixLine, c + 1f, 3f,
                    NumberAt(c, 0), NumberAt(c, 1), NumberAt(c, 2),
                    NumberAt(c + 1, 0), NumberAt(c + 1, 1), NumberAt(c + 1, 2));
            }

            // 0번 칸과 첫 열의 경계
            for (int r = 0; r < 3; r++)
            {
                Add(BetKind.Split, 0f, r + 0.5f, 0, NumberAt(0, r));
            }

            Add(BetKind.Street, 0f, 1f, 0, NumberAt(0, 0), NumberAt(0, 1)); // 0·2·3
            Add(BetKind.Street, 0f, 2f, 0, NumberAt(0, 1), NumberAt(0, 2)); // 0·1·2
            Add(BetKind.Corner, 0f, 3f, 0, 1, 2, 3);

            valid = new HashSet<BetTarget>();
            foreach (InsideBetSpot spot in spots)
            {
                valid.Add(spot.Target);
            }
        }

        private static void Add(BetKind kind, float x, float y, params int[] numbers)
        {
            spots.Add(new InsideBetSpot(BetTarget.Inside(kind, numbers), x, y));
        }
    }
}
