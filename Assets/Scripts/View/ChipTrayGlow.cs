using System.Collections.Generic;
using UnityEngine;

namespace ProjectCR
{
    /// <summary>
    /// 칩 트레이에서 고른 금액 칸의 테두리를 은은하게 빛나게 한다(가는 금색 선 + 바깥·안쪽으로 번지는 옅은 빛, 천천히 숨쉬듯 밝기 변화).
    ///
    /// 칸 사각형(가운데·반 크기·두 축, 이 오브젝트 로컬)은 빌더(RouletteModelSetup)가 트레이 메시와 칸막이에서 재서 넣는다.
    /// 메시 하나를 다시 만들어 그리고, 숨길 때는 렌더러만 끈다. 다른 머티리얼은 건드리지 않는다(밝기는 MaterialPropertyBlock).
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class ChipTrayGlow : MonoBehaviour
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [Tooltip("칸 가운데(이 오브젝트 로컬, 테두리를 그릴 높이). denominations 순서.")]
        [SerializeField] private Vector3[] centers = new Vector3[0];

        [Tooltip("칸 반 크기(x = 트레이 긴 방향, y = 깊이 방향, 로컬 m).")]
        [SerializeField] private Vector2[] halfSizes = new Vector2[0];

        [SerializeField] private Vector3 axisX = Vector3.right;
        [SerializeField] private Vector3 axisZ = Vector3.forward;

        [SerializeField] private Color glowColor = new Color(1f, 0.84f, 0.42f, 1f);

        [Tooltip("가운데 선 두께(m).")]
        [SerializeField] private float lineWidth = 0.0025f;

        [Tooltip("바깥으로 번지는 빛 폭(m).")]
        [SerializeField] private float outerHalo = 0.009f;

        [Tooltip("안쪽으로 번지는 빛 폭(m).")]
        [SerializeField] private float innerHalo = 0.006f;

        [Tooltip("숨쉬기 주기(초)와 가장 어두울 때 밝기.")]
        [SerializeField] private float pulsePeriod = 1.6f;
        [Range(0f, 1f)]
        [SerializeField] private float pulseMin = 0.65f;

        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<int> triangles = new List<int>();
        private Mesh mesh;
        private MeshRenderer meshRenderer;
        private MaterialPropertyBlock block;
        private int shown = -1;

        public int Count => centers.Length;

        public void Configure(Vector3[] compartmentCenters, Vector2[] compartmentHalfSizes, Vector3 longAxis, Vector3 depthAxis)
        {
            centers = compartmentCenters;
            halfSizes = compartmentHalfSizes;
            axisX = longAxis.normalized;
            axisZ = depthAxis.normalized;
        }

        private void Awake()
        {
            mesh = new Mesh { name = "ChipTrayGlow" };
            mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = GetComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            block = new MaterialPropertyBlock();
            meshRenderer.enabled = false;
        }

        private void OnDestroy()
        {
            if (mesh != null)
            {
                Destroy(mesh);
            }
        }

        private void Update()
        {
            if (!meshRenderer.enabled)
            {
                return;
            }

            // 천천히 밝아졌다 어두워진다(시간 기준이라 프레임과 무관).
            float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / Mathf.Max(0.1f, pulsePeriod));
            float brightness = Mathf.Lerp(pulseMin, 1f, wave);
            meshRenderer.GetPropertyBlock(block);
            block.SetColor(ColorId, new Color(1f, 1f, 1f, brightness));
            meshRenderer.SetPropertyBlock(block);
        }

        /// <summary>칸 index의 테두리를 빛나게 한다. 범위 밖이면 숨긴다.</summary>
        public void Show(int index)
        {
            if (mesh == null || index < 0 || index >= centers.Length || index >= halfSizes.Length)
            {
                Hide();
                return;
            }

            meshRenderer.enabled = true;
            if (shown == index)
            {
                return;
            }

            shown = index;
            vertices.Clear();
            colors.Clear();
            triangles.Clear();

            Vector3 center = centers[index];
            Vector2 half = halfSizes[index];
            float line = lineWidth * 0.5f;
            Color clear = new Color(glowColor.r, glowColor.g, glowColor.b, 0f);
            Color halo = new Color(glowColor.r, glowColor.g, glowColor.b, 0.5f);
            Color innerGlow = new Color(glowColor.r, glowColor.g, glowColor.b, 0.3f);

            // 가운데 선(꽉 찬 색), 바깥 번짐(선 → 투명), 안쪽 번짐(선 → 투명).
            AddRing(Corners(center, half, -line), Corners(center, half, line), glowColor, glowColor);
            AddRing(Corners(center, half, line), Corners(center, half, line + outerHalo), halo, clear);
            AddRing(Corners(center, half, -line), Corners(center, half, -line - innerHalo), innerGlow, clear);

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        public void Hide()
        {
            shown = -1;
            if (meshRenderer != null)
            {
                meshRenderer.enabled = false;
            }
        }

        /// <summary>칸 사각형을 offset만큼 넓힌(음수면 좁힌) 네 모서리.</summary>
        private Vector3[] Corners(Vector3 center, Vector2 half, float offset)
        {
            Vector3 x = axisX * Mathf.Max(0.0005f, half.x + offset);
            Vector3 z = axisZ * Mathf.Max(0.0005f, half.y + offset);
            return new[] { center - x - z, center + x - z, center + x + z, center - x + z };
        }

        /// <summary>두 사각형 사이 띠(네 변). 안쪽 모서리 색 → 바깥 모서리 색으로 번진다.</summary>
        private void AddRing(Vector3[] from, Vector3[] to, Color fromColor, Color toColor)
        {
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                int start = vertices.Count;
                vertices.Add(from[i]);
                vertices.Add(from[j]);
                vertices.Add(to[j]);
                vertices.Add(to[i]);
                colors.Add(fromColor);
                colors.Add(fromColor);
                colors.Add(toColor);
                colors.Add(toColor);

                // 머티리얼이 양면이라 감는 방향은 상관없다.
                triangles.Add(start);
                triangles.Add(start + 1);
                triangles.Add(start + 2);
                triangles.Add(start);
                triangles.Add(start + 2);
                triangles.Add(start + 3);
            }
        }
    }
}
