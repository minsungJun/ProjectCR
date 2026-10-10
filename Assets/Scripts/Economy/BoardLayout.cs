using UnityEngine;

namespace ProjectCR
{
    /// <summary>
    /// 베팅판 좌표(칸 단위) ↔ 베팅 대상. 번호판 배치와 단위는 <see cref="InsideBets"/>와 같다:
    /// x = 0이 0번 칸과 첫 열의 경계, 열 c는 [c, c+1), y = 0이 위 줄(3·6·…·36) 윗변, 행 r은 [r, r+1).
    /// 번호판 아래에 더즌 줄 [3, dozenBottom), 그 아래 2배 줄 [dozenBottom, outsideBottom),
    /// 왼쪽에 0번 칸 [zeroLeft, 0), 오른쪽 끝에 열 베팅 [12, columnRight).
    ///
    /// 3D 테이블과 2D 판이 같은 규칙으로 베팅 대상을 고르게 하려고 순수 계산으로 둔다.
    /// </summary>
    [System.Serializable]
    public struct BoardLayout
    {
        private static readonly BetKind[] EvenMoneyRow =
        {
            BetKind.Low, BetKind.Even, BetKind.Red, BetKind.Black, BetKind.Odd, BetKind.High,
        };

        [Tooltip("0번 칸 왼쪽 끝(음수, 칸 단위).")]
        public float zeroLeft;

        [Tooltip("열 베팅(2 to 1) 칸 오른쪽 끝(12보다 큼).")]
        public float columnRight;

        [Tooltip("더즌 줄 아랫변(3보다 큼).")]
        public float dozenBottom;

        [Tooltip("2배 줄(1–18 … 19–36) 아랫변.")]
        public float outsideBottom;

        [Tooltip("안쪽 베팅 점을 누른 것으로 보는 거리(칸 단위). 가로·세로 따로.")]
        public float spotRadiusX;
        public float spotRadiusY;

        /// <summary>판 위의 점이 가리키는 베팅 대상. 판 밖이면 false.</summary>
        public bool TryGetTarget(Vector2 units, out BetTarget target)
        {
            target = BetTarget.None;

            // 칸 경계·모서리의 안쪽 베팅이 먼저다. 그 근처가 아니면 칸 자체.
            float best = float.MaxValue;
            foreach (InsideBetSpot spot in InsideBets.All)
            {
                float dx = Mathf.Abs(units.x - spot.X) / spotRadiusX;
                float dy = Mathf.Abs(units.y - spot.Y) / spotRadiusY;
                if (dx <= 1f && dy <= 1f && dx + dy < best)
                {
                    best = dx + dy;
                    target = spot.Target;
                }
            }

            if (!target.IsNone)
            {
                return true;
            }

            float x = units.x;
            float y = units.y;

            if (y >= 0f && y < 3f)
            {
                int row = Mathf.FloorToInt(y);
                if (x >= zeroLeft && x < 0f)
                {
                    target = BetTarget.Straight(0);
                }
                else if (x >= 0f && x < 12f)
                {
                    target = BetTarget.Straight(InsideBets.NumberAt(Mathf.FloorToInt(x), row));
                }
                else if (x >= 12f && x < columnRight)
                {
                    target = BetTarget.Outside(row == 0 ? BetKind.Column3 : row == 1 ? BetKind.Column2 : BetKind.Column1);
                }
            }
            else if (x >= 0f && x < 12f)
            {
                if (y >= 3f && y < dozenBottom)
                {
                    target = BetTarget.Outside(BetKind.Dozen1 + Mathf.FloorToInt(x / 4f));
                }
                else if (y >= dozenBottom && y < outsideBottom)
                {
                    target = BetTarget.Outside(EvenMoneyRow[Mathf.FloorToInt(x / 2f)]);
                }
            }

            return !target.IsNone;
        }

        /// <summary>숫자 칸(0~36)의 판 위 사각형(칸 단위). x는 열 방향, y는 위 줄에서 아래 줄 방향. 판정(<see cref="TryGetTarget"/>)과 같은 경계.</summary>
        public Rect CellOf(int number)
        {
            if (number == 0)
            {
                return new Rect(zeroLeft, 0f, -zeroLeft, 3f);
            }

            int column = (number - 1) / 3;
            int row = 3 * column + 3 - number;
            return new Rect(column, row, 1f, 1f);
        }

        /// <summary>바깥 베팅 영역의 판 위 사각형(칸 단위). 바깥 베팅이 아니면 false. 판정과 같은 경계.</summary>
        public bool TryGetOutsideArea(BetTarget target, out Rect area)
        {
            area = default;
            switch (target.Kind)
            {
                case BetKind.Column3:
                case BetKind.Column2:
                case BetKind.Column1:
                    int row = target.Kind == BetKind.Column3 ? 0 : target.Kind == BetKind.Column2 ? 1 : 2;
                    area = new Rect(12f, row, columnRight - 12f, 1f);
                    return true;

                case BetKind.Dozen1:
                case BetKind.Dozen2:
                case BetKind.Dozen3:
                    area = new Rect(4f * (target.Kind - BetKind.Dozen1), 3f, 4f, dozenBottom - 3f);
                    return true;
            }

            for (int i = 0; i < EvenMoneyRow.Length; i++)
            {
                if (EvenMoneyRow[i] == target.Kind)
                {
                    area = new Rect(2f * i, dozenBottom, 2f, outsideBottom - dozenBottom);
                    return true;
                }
            }

            return false;
        }

        /// <summary>베팅 대상의 판 위 대표 위치(칩을 놓는 자리). 숫자·바깥은 칸 가운데, 안쪽은 경계·모서리 점.</summary>
        public Vector2 UnitsOf(BetTarget target)
        {
            if (target.Kind == BetKind.Straight)
            {
                return CellOf(target.Number).center;
            }

            if (TryGetOutsideArea(target, out Rect area))
            {
                return area.center;
            }

            switch (target.Kind)
            {
                case BetKind.Split:
                case BetKind.Street:
                case BetKind.Corner:
                case BetKind.SixLine:
                    foreach (InsideBetSpot spot in InsideBets.All)
                    {
                        if (spot.Target == target)
                        {
                            return new Vector2(spot.X, spot.Y);
                        }
                    }

                    break;
            }

            return new Vector2(float.NaN, float.NaN);
        }
    }
}
