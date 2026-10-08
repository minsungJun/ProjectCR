using UnityEngine;

namespace ProjectCR
{
    public enum TabletAction
    {
        /// <summary>회전(HUD 회전 버튼과 같다).</summary>
        Spin,

        /// <summary>서비스 = 상점 열기.</summary>
        Service,

        /// <summary>전체 회수 = 판의 칩 모두 빼기(HUD 전체 취소와 같다).</summary>
        RecallAll,
    }

    /// <summary>
    /// 테이블 태블릿의 3D 버튼 하나. 콜라이더를 TableBetInput이 광선으로 맞혀 누름을 알린다.
    /// 누를 수 없을 때는 버튼 면과 글자를 어둡게, 마우스가 올라가면 밝게 그린다(머티리얼은 그대로, PropertyBlock만 바꾼다).
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class TabletButton : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private TabletAction action;

        [Tooltip("눌림 상태에 따라 색을 바꿀 버튼 면.")]
        [SerializeField] private Renderer face;

        [Tooltip("버튼 글자.")]
        [SerializeField] private TextMesh label;

        private MaterialPropertyBlock block;
        private Color faceColor = Color.white;
        private Color labelColor = Color.white;
        private bool captured;
        private bool interactable = true;
        private bool hovered;

        public TabletAction Action => action;
        public bool Interactable => interactable;
        public TextMesh Label => label;

        public void Configure(TabletAction buttonAction, Renderer buttonFace, TextMesh buttonLabel)
        {
            action = buttonAction;
            face = buttonFace;
            label = buttonLabel;
        }

        public void SetInteractable(bool value)
        {
            if (interactable == value && captured)
            {
                return;
            }

            interactable = value;
            Apply();
        }

        public void SetHovered(bool value)
        {
            if (hovered == value && captured)
            {
                return;
            }

            hovered = value;
            Apply();
        }

        private void Apply()
        {
            if (!captured)
            {
                // 원래 색을 처음 한 번 기억한다.
                if (face != null && face.sharedMaterial != null && face.sharedMaterial.HasProperty(BaseColorId))
                {
                    faceColor = face.sharedMaterial.GetColor(BaseColorId);
                }

                if (label != null)
                {
                    labelColor = label.color;
                }

                block = new MaterialPropertyBlock();
                captured = true;
            }

            float shade = !interactable ? 0.45f : hovered ? 1.18f : 1f;
            if (face != null)
            {
                Color c = faceColor * shade;
                c.a = faceColor.a;
                face.GetPropertyBlock(block);
                block.SetColor(BaseColorId, c);
                face.SetPropertyBlock(block);
            }

            if (label != null)
            {
                Color c = labelColor;
                c.a = interactable ? labelColor.a : labelColor.a * 0.45f;
                label.color = c;
            }
        }
    }
}
