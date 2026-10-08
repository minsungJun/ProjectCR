using System.Collections.Generic;
using UnityEngine;

namespace ProjectCR
{
    /// <summary>
    /// 판 위 호버 강조: 베팅에 포함된 숫자 칸(바깥 베팅이면 그 영역도)을 옅게 칠하고 테두리를 그린다.
    ///
    /// 칸 사각형은 클릭 판정과 같은 <see cref="BoardLayout"/>(CellOf · TryGetOutsideArea)에서 오고,
    /// 포함 숫자는 <see cref="BetTarget.CoversNumber"/>로 고른다. 그래서 강조는 실제로 클릭될 베팅과 일치한다.
    /// 메시 하나(정점 색: 칠 + 테두리)를 판 표면 바로 위에 띄우고, 숨길 때는 렌더러만 끈다.
    /// 다른 머티리얼은 건드리지 않아서 호버가 끝나면 원래 상태 그대로다.
    /// 머티리얼은 정점 색을 쓰는 반투명(깊이 쓰기 없음)이라, 칩은 강조보다 앞에 그려진다.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class BetHighlightView : MonoBehaviour
    {
        [SerializeField] private TableBetBoard board;

        [Tooltip("칸 안쪽 칠 색(알파 = 진하기).")]
        [SerializeField] private Color fillColor = new Color(1f, 0.84f, 0.42f, 0.16f);

        [Tooltip("칸 테두리 색.")]
        [SerializeField] private Color borderColor = new Color(1f, 0.82f, 0.36f, 0.9f);

        [Tooltip("테두리 두께(m).")]
        [Min(0.0001f)]
        [SerializeField] private float borderWidth = 0.0022f;

        [Tooltip("판 표면에서 띄우는 높이(m). 판과 겹쳐 깜빡이지 않을 만큼만.")]
        [SerializeField] private float lift = 0.0004f;

        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<int> triangles = new List<int>();
        private Mesh mesh;
        private MeshRenderer meshRenderer;

        public void Configure(TableBetBoard tableBoard)
        {
            board = tableBoard;
        }

        private void Awake()
        {
            mesh = new Mesh { name = "BetHighlight" };
            mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = GetComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            Hide();
        }

        private void OnDestroy()
        {
            if (mesh != null)
            {
                Destroy(mesh);
            }
        }

        /// <summary>대상 베팅의 포함 칸(바깥이면 영역 + 포함 숫자)을 강조한다. 대상이 없으면 숨긴다.</summary>
        public void Show(BetTarget target, RouletteDefinition definition)
        {
            if (target.IsNone || board == null || definition == null || mesh == null)
            {
                Hide();
                return;
            }

            vertices.Clear();
            colors.Clear();
            triangles.Clear();

            BoardLayout layout = board.Layout;
            if (layout.TryGetOutsideArea(target, out Rect area))
            {
                AddRect(area);
            }

            for (int n = 0; n <= 36; n++)
            {
                if (target.CoversNumber(n, definition.ColorOfNumber(n)))
                {
                    AddRect(layout.CellOf(n));
                }
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            meshRenderer.enabled = vertices.Count > 0;
        }

        public void Hide()
        {
            if (meshRenderer != null)
            {
                meshRenderer.enabled = false;
            }
        }

        /// <summary>판 좌표 사각형 하나: 안쪽 칠 + 테두리 네 줄(월드 두께가 칸 모양과 상관없이 같다).</summary>
        private void AddRect(Rect units)
        {
            // 칩 놓는 자리 표시(HoverMarker)와 같은 방향으로 띄운다.
            Vector3 up = board.transform.up * lift;
            Vector3 a = board.WorldFromUnits(new Vector2(units.xMin, units.yMin)) + up;
            Vector3 b = board.WorldFromUnits(new Vector2(units.xMax, units.yMin)) + up;
            Vector3 c = board.WorldFromUnits(new Vector2(units.xMax, units.yMax)) + up;
            Vector3 d = board.WorldFromUnits(new Vector2(units.xMin, units.yMax)) + up;

            // 테두리 두께는 칸보다 굵어지지 않게 한다.
            float t = Mathf.Min(borderWidth, Vector3.Distance(a, b) * 0.25f, Vector3.Distance(a, d) * 0.25f);
            Vector3 u = (b - a).normalized * t;
            Vector3 v = (d - a).normalized * t;

            Vector3 ai = a + u + v;
            Vector3 bi = b - u + v;
            Vector3 ci = c - u - v;
            Vector3 di = d + u - v;

            AddQuad(ai, bi, ci, di, fillColor);
            AddQuad(a, b, b + v, a + v, borderColor);      // 위 줄
            AddQuad(d - v, c - v, c, d, borderColor);      // 아래 줄
            AddQuad(a + v, ai, di, d - v, borderColor);    // 왼쪽
            AddQuad(bi, b + v, c - v, ci, borderColor);    // 오른쪽
        }

        private void AddQuad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Color color)
        {
            int start = vertices.Count;
            vertices.Add(transform.InverseTransformPoint(p0));
            vertices.Add(transform.InverseTransformPoint(p1));
            vertices.Add(transform.InverseTransformPoint(p2));
            vertices.Add(transform.InverseTransformPoint(p3));
            for (int i = 0; i < 4; i++)
            {
                colors.Add(color);
            }

            // 머티리얼이 양면(Cull Off)이라 감는 방향은 상관없다.
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }
    }
}
