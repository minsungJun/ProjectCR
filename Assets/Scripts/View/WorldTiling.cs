using UnityEngine;

namespace ProjectCR
{
    /// <summary>
    /// 바닥처럼 넓은 면의 텍스처를 실제 크기(m) 기준으로 반복한다. 면을 늘리거나 줄여도 타일 한 장의 크기는 tileSize 그대로다.
    ///
    /// 메시 로컬 범위(XZ) × 월드 스케일로 실제 크기를 구해 타일 수를 정하고, 렌더러 MaterialPropertyBlock의 _BaseMap_ST에 넣는다
    /// (URP Lit은 베이스·노멀 등 모든 맵이 이 값을 같이 쓴다). 머티리얼 에셋은 바꾸지 않아서 다른 물체와 같이 써도 된다.
    /// 메시 UV가 면 전체를 0~1로 덮는다고 본다(Plane·Quad). 편집 중에도 크기를 바꾸면 바로 다시 맞춘다.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Renderer))]
    public class WorldTiling : MonoBehaviour
    {
        private static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");

        [Tooltip("타일 한 장의 실제 크기(m). x = 면의 로컬 X 방향, y = 로컬 Z 방향.")]
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
            Vector3 size = target.localBounds.size;
            Vector3 scale = transform.lossyScale;
            float tilesX = Mathf.Abs(size.x * scale.x) / Mathf.Max(0.01f, tileSize.x);
            float tilesZ = Mathf.Abs(size.z * scale.z) / Mathf.Max(0.01f, tileSize.y);

            target.GetPropertyBlock(block);
            block.SetVector(BaseMapStId, new Vector4(Mathf.Max(0.001f, tilesX), Mathf.Max(0.001f, tilesZ), 0f, 0f));
            target.SetPropertyBlock(block);

            lastScale = scale;
            lastTile = tileSize;
        }
    }
}
