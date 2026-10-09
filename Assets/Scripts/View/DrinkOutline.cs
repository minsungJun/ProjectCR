using System.Collections.Generic;
using UnityEngine;

namespace ProjectCR
{
    /// <summary>
    /// 음료 잔 외곽선. 잔(이름에 Glass가 든 메시, 없으면 모든 메시)마다 같은 메시를 쓰는 자식을 만들어
    /// 외곽선 머티리얼(ProjectCR/OutlineHull)로 그린다. 원래 렌더러·머티리얼은 그대로 둔다.
    /// 호버면 은은하게 숨쉬고, 선택이면 더 밝게 고정. 끄면 자식 렌더러만 끈다.
    /// </summary>
    public class DrinkOutline : MonoBehaviour
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private readonly List<Renderer> hulls = new List<Renderer>();
        private MaterialPropertyBlock block;
        private Color color = new Color(1f, 0.84f, 0.42f, 1f);
        private bool hovered;
        private bool selected;

        /// <summary>외곽선 자식을 만든다. 한 번만 부른다.</summary>
        public void Build(Material outlineMaterial, Color glowColor)
        {
            color = glowColor;
            block = new MaterialPropertyBlock();

            var sources = new List<MeshFilter>(GetComponentsInChildren<MeshFilter>(true));
            List<MeshFilter> glass = sources.FindAll(f => f.name.Contains("Glass"));
            foreach (MeshFilter source in glass.Count > 0 ? glass : sources)
            {
                if (source.sharedMesh == null)
                {
                    continue;
                }

                var hull = new GameObject(source.name + "_Outline", typeof(MeshFilter), typeof(MeshRenderer));
                hull.transform.SetParent(source.transform, false);
                hull.GetComponent<MeshFilter>().sharedMesh = source.sharedMesh;
                var renderer = hull.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = outlineMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.enabled = false;
                hulls.Add(renderer);
            }
        }

        public void SetState(bool isHovered, bool isSelected)
        {
            hovered = isHovered;
            selected = isSelected;
            bool visible = hovered || selected;
            foreach (Renderer hull in hulls)
            {
                hull.enabled = visible;
            }

            Apply();
        }

        private void Update()
        {
            if (hovered && !selected)
            {
                Apply();
            }
        }

        private void Apply()
        {
            block ??= new MaterialPropertyBlock();

            // 선택: 밝게 고정. 호버: 1.4초 주기로 55~95% 숨쉬기.
            float alpha = selected ? 1f : Mathf.Lerp(0.55f, 0.95f, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / 1.4f));
            foreach (Renderer hull in hulls)
            {
                hull.GetPropertyBlock(block);
                block.SetColor(ColorId, new Color(color.r, color.g, color.b, alpha));
                hull.SetPropertyBlock(block);
            }
        }
    }
}
