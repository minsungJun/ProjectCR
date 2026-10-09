using UnityEngine;

namespace ProjectCR
{
    /// <summary>
    /// 바닥처럼 넓은 면의 텍스처를 실제 크기(m) 기준으로 반복한다. 면을 늘리거나 줄여도 타일 한 장의 크기는 tileSize 그대로다.
    ///
    /// 메시 로컬 범위 × 월드 스케일로 실제 크기를 구해(바닥 Plane은 X·Z, 벽 Quad는 X·Y) 타일 수를 정하고, 렌더러 MaterialPropertyBlock의 _BaseMap_ST에 넣는다
    /// (URP Lit은 베이스·노멀 등 모든 맵이 이 값을 같이 쓴다). 머티리얼 에셋은 바꾸지 않아서 다른 물체와 같이 써도 된다.
    /// 메시 UV가 면 전체를 0~1로 덮는다고 본다(Plane·Quad). 편집 중에도 크기를 바꾸면 바로 다시 맞춘다.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Renderer))]
    public class WorldTiling : MonoBehaviour
    {
        private static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");

        [Tooltip("타일 한 장의 실제 크기(m). x = 면의 가로(로컬 X), y = 세로(바닥은 로컬 Z, 벽은 로컬 Y).")]
        [SerializeField] private Vector2 tileSize = new Vector2(1f, 1f);

        private Renderer target;
        private MaterialPropertyBlock block;
        private Vector3 lastScale;
        private Vector2 lastTile;

        private void OnEnable()
        {
            Apply();
        }

        private void OnValidate()
        {
            Apply();
        }

        private void Update()
        {
            if (transform.lossyScale != lastScale || tileSize != lastTile)
            {
                Apply();
            }
        }

        private void OnDisable()
        {
            if (target != null)
            {
                target.SetPropertyBlock(null);
            }
        }

        private void Apply()
        {
            target ??= GetComponent<Renderer>();
            if (target == null)
            {
                return;
            }

            block ??= new MaterialPropertyBlock();
            // 실제 크기(로컬 범위 × 스케일). U는 늘 로컬 X, V는 면이 놓인 방향을 따른다:
            // Plane처럼 X·Z로 펼쳐진 면(바닥)은 Z, Quad처럼 X·Y로 서 있는 면(벽)은 Y.
            Vector3 size = Vector3.Scale(target.localBounds.size, transform.lossyScale);
            float u = Mathf.Abs(size.x);
            float v = Mathf.Abs(size.y) >= Mathf.Abs(size.z) ? Mathf.Abs(size.y) : Mathf.Abs(size.z);
            float tilesU = u / Mathf.Max(0.01f, tileSize.x);
            float tilesV = v / Mathf.Max(0.01f, tileSize.y);
            Vector3 scale = transform.lossyScale;

            target.GetPropertyBlock(block);
            block.SetVector(BaseMapStId, new Vector4(Mathf.Max(0.001f, tilesU), Mathf.Max(0.001f, tilesV), 0f, 0f));
            target.SetPropertyBlock(block);

            lastScale = scale;
            lastTile = tileSize;
        }
    }
}
