using UnityEngine;

namespace ProjectCR
{
    /// <summary>
    /// 테이블 원 위에 놓이는 보유 강화 배지 하나(프리팹 UpgradeBadge의 루트). 배지 가운데 면(image)에 상품 아이콘을 그린다.
    /// 아이콘이 없으면 상품 색(Tint)으로 칠하고 그 위에 기호(PlaceholderSymbol)를 눕혀 쓴다(상점 카드와 같은 규칙).
    /// 마우스를 올리면 조금 떠오른다. 콜라이더는 루트에 있어서 TableBetInput이 이 컴포넌트로 알아본다.
    /// 위치·칸 번호는 <see cref="UpgradeBadgeView"/>가 정한다.
    /// </summary>
    public class UpgradeBadge : MonoBehaviour
    {
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Tooltip("아이콘을 그릴 가운데 면(UV가 있는 메시).")]
        [SerializeField] private Renderer image;

        [Tooltip("아이콘이 없을 때 쓰는 기호의 높이(m).")]
        [Min(0.001f)]
        [SerializeField] private float symbolHeight = 0.03f;

        private MaterialPropertyBlock block;
        private TextMesh symbol;
        private ShopItemDefinition shown;

        /// <summary>이 배지가 놓인 강화 칸 번호.</summary>
        public int SlotIndex { get; private set; } = -1;

        public ShopItemDefinition Item => shown;

        /// <summary>배지 면의 높이(루트 기준, m). 기호를 그 위에 띄운다.</summary>
        private float FaceHeight => image != null ? image.bounds.max.y - transform.position.y : 0.003f;

        /// <summary>칸 번호와 상품을 정한다. 같은 상품이면 다시 그리지 않는다.</summary>
        public void Show(int slotIndex, ShopItemDefinition item, Font font)
        {
            SlotIndex = slotIndex;
            if (shown == item)
            {
                return;
            }

            shown = item;
            ApplyIcon(item, font);
        }

        private void ApplyIcon(ShopItemDefinition item, Font font)
        {
            if (image != null)
            {
                block ??= new MaterialPropertyBlock();
                image.GetPropertyBlock(block);
                Sprite icon = item.Icon;
                if (icon != null)
                {
                    // 스프라이트가 아틀라스 안에 있어도 그 부분만 쓰도록 UV 범위를 맞춘다.
                    Texture2D texture = icon.texture;
                    Rect rect = icon.textureRect;
                    block.SetTexture(BaseMapId, texture);
                    block.SetVector(BaseMapStId, new Vector4(rect.width / texture.width, rect.height / texture.height,
                        rect.x / texture.width, rect.y / texture.height));
                    block.SetColor(BaseColorId, Color.white);
                }
                else
                {
                    block.SetTexture(BaseMapId, Texture2D.whiteTexture);
                    block.SetVector(BaseMapStId, new Vector4(1f, 1f, 0f, 0f));
                    block.SetColor(BaseColorId, item.Tint);
                }

                image.SetPropertyBlock(block);
            }

            SetSymbol(item.Icon == null ? item.PlaceholderSymbol : null, font);
        }

        /// <summary>
        /// 기호를 배지 면 위에 눕힌다. 플레이어는 +Z 쪽에서 -Z를 보고 앉아 있어서,
        /// 글자 가로 = 월드 -X, 글자 위쪽 = 월드 -Z(테이블 안쪽), 글자 앞면 = 월드 +Y가 되게 돌린다.
        /// </summary>
        private void SetSymbol(string text, Font font)
        {
            if (string.IsNullOrEmpty(text))
            {
                if (symbol != null)
                {
                    symbol.gameObject.SetActive(false);
                }

                return;
            }

            if (symbol == null)
            {
                font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                var go = new GameObject("Symbol");
                go.transform.SetParent(transform, false);
                symbol = go.AddComponent<TextMesh>();
                symbol.font = font;
                symbol.fontSize = 96;
                symbol.fontStyle = FontStyle.Bold;
                symbol.anchor = TextAnchor.MiddleCenter;
                symbol.alignment = TextAlignment.Center;
                symbol.color = new Color(0.99f, 0.96f, 0.87f);
                go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }

            // TextMesh 글자 높이 ≈ characterSize × fontSize / 10 (m).
            symbol.characterSize = symbolHeight * 10f / symbol.fontSize;
            symbol.text = text;
            symbol.transform.SetLocalPositionAndRotation(new Vector3(0f, FaceHeight + 0.0006f, 0f),
                Quaternion.LookRotation(Vector3.down, Vector3.back));
            symbol.gameObject.SetActive(true);
        }
    }
}
