using UnityEngine;

namespace ProjectCR
{
    /// <summary>
    /// 3D 테이블의 베팅판. 세계 좌표 ↔ 판 좌표(칸 단위, <see cref="BoardLayout"/>)를 바꾼다.
    ///
    /// 값은 빌더(RouletteModelSetup)가 모델의 칸 메시(CRT_Bet_Color_01~36)와 격자 메시에서 재서 넣는다.
    /// 좌표는 이 오브젝트의 로컬 기준이다: origin = 3번 칸 왼쪽 위 모서리(0번 칸과 첫 열의 경계, 위 줄 윗변),
    /// columnAxis = 열이 늘어나는 방향, rowAxis = 위 줄(3)에서 아래 줄(1)로 가는 방향.
    /// </summary>
    public class TableBetBoard : MonoBehaviour
    {
        [SerializeField] private Vector3 origin;
        [SerializeField] private Vector3 columnAxis = Vector3.left;
        [SerializeField] private Vector3 rowAxis = Vector3.forward;
        [SerializeField] private float columnPitch = 0.097f;
        [SerializeField] private float rowPitch = 0.13f;
        [SerializeField] private BoardLayout layout;

        public BoardLayout Layout => layout;

        /// <summary>판 표면(세계 좌표).</summary>
        public Plane Surface => new Plane(transform.TransformDirection(Vector3.Cross(rowAxis, columnAxis)).normalized, transform.TransformPoint(origin));

        public void Configure(Vector3 boardOrigin, Vector3 column, Vector3 row, float colPitch, float rPitch, BoardLayout boardLayout)
        {
            origin = boardOrigin;
            columnAxis = column.normalized;
            rowAxis = row.normalized;
            columnPitch = colPitch;
            rowPitch = rPitch;
            layout = boardLayout;
        }

        public Vector2 UnitsFromWorld(Vector3 world)
        {
            Vector3 local = transform.InverseTransformPoint(world) - origin;
            return new Vector2(Vector3.Dot(local, columnAxis) / columnPitch, Vector3.Dot(local, rowAxis) / rowPitch);
        }

        public Vector3 WorldFromUnits(Vector2 units)
        {
            return transform.TransformPoint(origin + columnAxis * (units.x * columnPitch) + rowAxis * (units.y * rowPitch));
        }

        public bool TryGetTarget(Vector3 world, out BetTarget target) => layout.TryGetTarget(UnitsFromWorld(world), out target);

        /// <summary>칩을 놓을 자리(판 표면 위, 세계 좌표).</summary>
        public Vector3 WorldPositionOf(BetTarget target) => WorldFromUnits(layout.UnitsOf(target));

        /// <summary>화면 광선이 판 표면과 만나는 점.</summary>
        public bool Raycast(Ray ray, out Vector3 point)
        {
            point = default;
            if (!Surface.Raycast(ray, out float distance))
            {
                return false;
            }

            point = ray.GetPoint(distance);
            return true;
        }
    }
}
