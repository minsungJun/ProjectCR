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
    /// 강조 중(<see cref="SetHighlighted"/>, 튜토리얼 서비스 안내)에는 버튼 둘레의 금색 빛 테두리(칩 트레이와 같은 ChipTrayGlow)가 천천히 깜빡이고,
    /// 면은 금색 쪽으로 물들었다 돌아오며 글자도 금색으로 깜빡인다.
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

        [Tooltip("강조 때 깜빡이는 금색 빛 테두리(없으면 면·글자만 깜빡인다).")]
        [SerializeField] private ChipTrayGlow highlightGlow;

        private MaterialPropertyBlock block;
        private Color faceColor = Color.white;
        private Color labelColor = Color.white;
        private bool captured;
        private bool interactable = true;
        private bool hovered;
        private bool highlighted;

        // 강조 깜빡임: 주기(초, 빛 테두리와 같다), 면이 금색으로 물드는 정도, 금색.
        private const float BlinkPeriod = 1.8f;
        private const float BlinkFaceTint = 0.55f;
        private static readonly Color BlinkFaceColor = new Color(0.78f, 0.58f, 0.22f);
        private static readonly Color BlinkLabelColor = new Color(1f, 0.82f, 0.36f);

        public ChipTrayGlow HighlightGlow => highlightGlow;

        public void SetHighlightGlow(ChipTrayGlow glow) => highlightGlow = glow;

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

        /// <summary>강조(깜빡임)를 켜고 끈다. 끄면 원래 색으로 돌아간다.</summary>
        public void SetHighlighted(bool value)
        {
            if (highlighted == value && captured)
            {
                return;
            }

            highlighted = value;
            if (highlightGlow != null)
            {
                if (value)
                {
                    highlightGlow.ShowAll();
                }
                else
                {
                    highlightGlow.Hide();
                }
            }

            Apply();
        }

        private void Update()
        {
            if (highlighted)
            {
                Apply();
            }
        }

        private void OnDisable()
        {
            if (highlighted)
            {
                highlighted = false;
                if (highlightGlow != null)
                {
                    highlightGlow.Hide();
                }

                Apply();
            }
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
            float blink = highlighted ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * (2f * Mathf.PI / BlinkPeriod)) : 0f;
            if (face != null)
            {
                // 어두운 면은 곱해도 거의 밝아지지 않아서 금색 쪽으로 섞는다.
                Color c = Color.Lerp(faceColor * shade, BlinkFaceColor, blink * BlinkFaceTint);
                c.a = faceColor.a;
                face.GetPropertyBlock(block);
                block.SetColor(BaseColorId, c);
                face.SetPropertyBlock(block);
            }

            if (label != null)
            {
                Color c = Color.Lerp(labelColor, BlinkLabelColor, blink);
                c.a = interactable ? labelColor.a : labelColor.a * 0.45f;
                label.color = c;
            }
        }
    }
}
