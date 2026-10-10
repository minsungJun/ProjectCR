using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectCR
{
    /// <summary>
    /// 1인칭 시점(FirstPersonView 카메라) 고개 돌리기. 오른쪽 버튼을 누른 채 끌면 돈다.
    /// 좌우(Y)는 yawRange 안, 위아래(X)는 pitchRange 안으로만 돈다. 이 카메라가 켜져 있을 때만 움직인다.
    ///
    /// 1인칭에서는 테이블 입력(TableBetInput)이 꺼져 있어 오른쪽 버튼이 베팅 빼기와 겹치지 않는다. HUD 위에서 누른 끌기는 무시한다.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class FirstPersonLook : MonoBehaviour
    {
        [Tooltip("HUD 위에서 시작한 끌기를 무시할 때 쓴다. 비워도 된다.")]
        [SerializeField] private RouletteHud hud;

        [Tooltip("좌우로 돌 수 있는 Y 각도 범위(도, 0~360). 정면이 180.")]
        [SerializeField] private Vector2 yawRange = new Vector2(80f, 280f);

        [Tooltip("위아래로 돌 수 있는 X 각도 범위(도). 음수가 위, 양수가 아래.")]
        [SerializeField] private Vector2 pitchRange = new Vector2(-30f, 60f);

        [Tooltip("마우스 1px당 도는 각도(도).")]
        [Min(0.01f)]
        [SerializeField] private float sensitivity = 0.15f;

        private Camera view;
        private float yaw;
        private float pitch;
        private bool dragging;

        private void Awake()
        {
            view = GetComponent<Camera>();
            Vector3 euler = transform.eulerAngles;
            yaw = Mathf.Clamp(Mathf.Repeat(euler.y, 360f), yawRange.x, yawRange.y); // 범위가 0°를 넘지 않는다고 본다(80~280처럼 뒤쪽 반원).
            pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, euler.x), pitchRange.x, pitchRange.y);
        }

        private void Update()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || !view.enabled || GamePause.IsPaused)
            {
                dragging = false;
                return;
            }

            if (mouse.rightButton.wasPressedThisFrame)
            {
                dragging = hud == null || !hud.IsPointerOverUi(mouse.position.ReadValue());
            }
            else if (!mouse.rightButton.isPressed)
            {
                dragging = false;
            }

            if (!dragging)
            {
                return;
            }

            Look(mouse.delta.ReadValue());
        }

        /// <summary>
        /// 마우스 이동량(px)만큼 돈다. 오른쪽으로 끌면 오른쪽을, 위로 끌면 위를 본다. 범위 밖으로는 안 간다.
        /// </summary>
        public void Look(Vector2 screenDelta)
        {
            // yaw는 늘 범위 안이라 0~360으로 감지 않고 그대로 자른다(크게 끌어도 반대쪽 끝으로 넘어가지 않게).
            yaw = Mathf.Clamp(yaw + screenDelta.x * sensitivity, yawRange.x, yawRange.y);
            pitch = Mathf.Clamp(pitch - screenDelta.y * sensitivity, pitchRange.x, pitchRange.y);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }
    }
}
