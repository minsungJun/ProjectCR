using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectCR
{
    /// <summary>
    /// 3D 테이블 마우스 입력. 판 왼쪽 클릭 = 칩 놓기, 오른쪽 클릭(끌지 않고 떼기) = 그 자리 베팅 빼기,
    /// 트레이 칩 클릭 = 칩 금액 고르기, 태블릿 버튼 클릭 = 회전·서비스·전체 회수, 강화 배지 = 호버만(툴팁). 마우스가 HUD 위에 있으면 무시한다.
    /// 입력을 이벤트로 넘기기만 하고, 베팅 가능 여부는 GameFlowController가 판단한다.
    /// <see cref="Interactable"/>이 false면(1인칭으로 둘러보는 중) 아무것도 가리키지 않고 클릭도 보내지 않는다.
    /// </summary>
    public class TableBetInput : MonoBehaviour
    {
        [SerializeField] private TableBetBoard board;
        [SerializeField] private RouletteHud hud;

        [Tooltip("지금 켜져 있는 카메라로 광선을 쏜다. 비우면 Camera.main.")]
        [SerializeField] private Camera[] cameras = new Camera[0];

        [Tooltip("트레이 칩 콜라이더를 찾는 광선 길이(m).")]
        [SerializeField] private float trayRayDistance = 10f;

        public event Action<BetTarget> TargetClicked;
        public event Action<BetTarget> TargetRemoveRequested;
        public event Action<long> ChipSelected;

        /// <summary>태블릿 버튼을 눌렀다. 누를 수 없는 상태(어둡게 그려진 버튼)면 보내지 않는다.</summary>
        public event Action<TabletAction> TabletPressed;

        /// <summary>마우스가 가리키는 베팅 대상이 바뀔 때. 아무 데도 아니면 BetTarget.None.</summary>
        public event Action<BetTarget> HoverChanged;

        /// <summary>마우스가 가리키는 로봇 선반 음료가 바뀔 때. 아무것도 아니면 null.</summary>
        public event Action<ServedDrink> DrinkHoverChanged;

        /// <summary>로봇 선반 음료를 왼쪽 클릭했다.</summary>
        public event Action<ServedDrink> DrinkClicked;

        /// <summary>마우스가 가리키는 강화 배지가 바뀔 때. 아무것도 아니면 null.</summary>
        public event Action<UpgradeBadge> BadgeHoverChanged;

        // 오른쪽 버튼을 이만큼(px) 넘게 끌면 클릭이 아니라 1인칭 시점 돌리기(FirstPersonLook)다.
        private const float RightDragThreshold = 6f;

        private BetTarget hover = BetTarget.None;
        private Vector2 rightPressScreen;
        private bool rightDragged;
        private TabletButton hoveredButton;
        private ServedDrink hoveredDrink;
        private UpgradeBadge hoveredBadge;

        /// <summary>지금 마우스가 가리키는 베팅 대상(클릭하면 이 대상에 놓인다). 판 밖·HUD 위·트레이·태블릿이면 None.</summary>
        public BetTarget CurrentHover => hover;

        /// <summary>마지막으로 읽은 마우스 화면 좌표(Input System, 왼쪽 아래 원점).</summary>
        public Vector2 PointerScreen { get; private set; }

        /// <summary>false면 판·트레이·태블릿·음료를 가리키지도 누르지도 못한다. 끄는 순간 호버도 지운다.</summary>
        public bool Interactable
        {
            get => interactable;
            set
            {
                interactable = value;
                if (!value)
                {
                    ClearHover();
                }
            }
        }

        private bool interactable = true;

        private void Update()
        {
            Mouse mouse = Mouse.current;
            Camera camera = ActiveCamera();
            if (mouse == null || camera == null || board == null)
            {
                return;
            }

            Vector2 screen = mouse.position.ReadValue();
            PointerScreen = screen;
            bool rightClicked = ReadRightClick(mouse, screen);
            if (!interactable || (hud != null && hud.IsPointerOverUi(screen)))
            {
                ClearHover();
                return;
            }

            Ray ray = camera.ScreenPointToRay(screen);
            bool hitSomething = Physics.Raycast(ray, out RaycastHit hit, trayRayDistance);

            // 강화 배지(콜라이더): 가리키기만 한다(누르는 동작 없음).
            UpgradeBadge badge = hitSomething && hit.collider.TryGetComponent(out UpgradeBadge found) ? found : null;
            SetBadgeHover(badge);
            if (badge != null)
            {
                SetHover(BetTarget.None);
                SetButtonHover(null);
                SetDrinkHover(null);
                return;
            }

            // 로봇 선반 음료(콜라이더).
            if (hitSomething && hit.collider.TryGetComponent(out ServedDrink drink))
            {
                SetHover(BetTarget.None);
                SetButtonHover(null);
                SetDrinkHover(drink);
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    DrinkClicked?.Invoke(drink);
                }

                return;
            }

            SetDrinkHover(null);

            // 태블릿 버튼(콜라이더).
            if (hitSomething && hit.collider.TryGetComponent(out TabletButton button))
            {
                SetHover(BetTarget.None);
                SetButtonHover(button);
                if (mouse.leftButton.wasPressedThisFrame && button.Interactable)
                {
                    TabletPressed?.Invoke(button.Action);
                }

                return;
            }

            SetButtonHover(null);

            // 트레이 칩(콜라이더)이 먼저. 판은 콜라이더 없이 평면 계산으로 고른다.
            if (hitSomething && hit.collider.TryGetComponent(out ChipTraySlot slot))
            {
                SetHover(BetTarget.None);
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    ChipSelected?.Invoke(slot.Value);
                }

                return;
            }

            BetTarget target = BetTarget.None;
            if (board.Raycast(ray, out Vector3 point))
            {
                board.TryGetTarget(point, out target);
            }

            SetHover(target);
            if (target.IsNone)
            {
                return;
            }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                TargetClicked?.Invoke(target);
            }
            else if (rightClicked)
            {
                TargetRemoveRequested?.Invoke(target);
            }
        }

        /// <summary>오른쪽 버튼을 끌지 않고 뗀 프레임이면 true. 누르는 순간이 아니라 뗄 때 판정한다(끌기는 시점 돌리기).</summary>
        private bool ReadRightClick(Mouse mouse, Vector2 screen)
        {
            if (mouse.rightButton.wasPressedThisFrame)
            {
                rightPressScreen = screen;
                rightDragged = false;
            }

            if (mouse.rightButton.isPressed && (screen - rightPressScreen).sqrMagnitude > RightDragThreshold * RightDragThreshold)
            {
                rightDragged = true;
            }

            return mouse.rightButton.wasReleasedThisFrame && !rightDragged;
        }

        private void ClearHover()
        {
            SetHover(BetTarget.None);
            SetButtonHover(null);
            SetDrinkHover(null);
            SetBadgeHover(null);
        }

        private void SetBadgeHover(UpgradeBadge badge)
        {
            if (hoveredBadge == badge)
            {
                return;
            }

            hoveredBadge = badge;
            BadgeHoverChanged?.Invoke(badge);
        }

        private void SetDrinkHover(ServedDrink drink)
        {
            if (hoveredDrink == drink)
            {
                return;
            }

            hoveredDrink = drink;
            DrinkHoverChanged?.Invoke(drink);
        }

        private void SetButtonHover(TabletButton button)
        {
            if (hoveredButton == button)
            {
                return;
            }

            if (hoveredButton != null)
            {
                hoveredButton.SetHovered(false);
            }

            hoveredButton = button;
            if (button != null)
            {
                button.SetHovered(true);
            }
        }

        private void SetHover(BetTarget target)
        {
            if (hover == target)
            {
                return;
            }

            hover = target;
            HoverChanged?.Invoke(target);
        }

        private Camera ActiveCamera()
        {
            foreach (Camera camera in cameras)
            {
                if (camera != null && camera.isActiveAndEnabled)
                {
                    return camera;
                }
            }

            return Camera.main;
        }
    }
}
