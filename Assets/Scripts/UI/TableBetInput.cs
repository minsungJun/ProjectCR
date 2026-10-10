using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectCR
{
    /// <summary>
    /// 3D 테이블 마우스 입력. 판 왼쪽 클릭 = 칩 하나 놓기, 오른쪽 클릭 = 칩 하나 빼기.
    /// 같은 칸을 <see cref="holdDelay"/>초 누르고 있으면 계속 놓거나(왼쪽) 계속 뺀다(오른쪽).
    /// 칩 끌기: 칩이 있는 칸(<see cref="HasChipsAt"/>)을 왼쪽으로 누르면 바로 놓지 않고 기다린다.
    ///   <see cref="dragThreshold"/>px 넘게 움직이면 그 칸 칩 더미를 끌고(놓은 칸으로 통째로 옮김, 판 밖이면 뺌),
    ///   움직이지 않고 떼면 그때 칩 하나를 놓고, 움직이지 않고 holdDelay초 누르면 계속 놓기로 넘어간다(그 뒤로는 끌기가 되지 않는다).
    ///   빈 칸은 끌 게 없으니 누르는 순간 바로 놓는다.
    /// 트레이 칩 클릭 = 칩 금액 고르기, 마우스 휠 = 칩 금액 한 단계 위(휠 올림)·아래(휠 내림), 태블릿 버튼 클릭 = 회전·서비스·전체 회수, 강화 배지 = 호버만(툴팁). 마우스가 HUD 위에 있으면 무시한다.
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

        [Header("길게 눌러 계속 걸기")]
        [Tooltip("판의 같은 칸을 이만큼(초) 누르고 있으면 칩을 계속 놓기 시작한다.")]
        [Min(0.1f)]
        [SerializeField] private float holdDelay = 1.5f;

        [Tooltip("계속 놓기 시작할 때 칩 사이 간격(초).")]
        [Min(0.01f)]
        [SerializeField] private float repeatInterval = 0.15f;

        [Tooltip("가장 빨라졌을 때 칩 사이 간격(초).")]
        [Min(0.01f)]
        [SerializeField] private float fastRepeatInterval = 0.04f;

        [Tooltip("계속 놓기 시작한 뒤 가장 빠른 간격까지 걸리는 시간(초).")]
        [Min(0f)]
        [SerializeField] private float rampSeconds = 2f;

        [Header("칩 끌기")]
        [Tooltip("칩이 있는 칸을 누른 채 이만큼(px) 넘게 움직이면 칩 놓기가 아니라 끌기다.")]
        [Min(1f)]
        [SerializeField] private float dragThreshold = 8f;

        public event Action<BetTarget> TargetClicked;
        public event Action<BetTarget> TargetRemoveRequested;
        public event Action<long> ChipSelected;

        /// <summary>마우스 휠로 칩 금액을 한 단계 바꾼다(+1 = 윗단위, -1 = 아랫단위). HUD 위(상점·음료 목록 스크롤)에서는 보내지 않는다.</summary>
        public event Action<int> ChipStepped;

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

        /// <summary>칩 더미를 끌기 시작했다(끄는 칸).</summary>
        public event Action<BetTarget> DragStarted;

        /// <summary>끌던 칩 더미를 놓았다(끌던 칸, 놓은 칸). 판 밖·HUD 위에 놓으면 놓은 칸이 None.</summary>
        public event Action<BetTarget, BetTarget> DragDropped;

        /// <summary>끌기가 취소됐다(입력이 막힘 등). 칩은 제자리다.</summary>
        public event Action DragCancelled;

        /// <summary>이 칸에 끌 수 있는 칩이 있는가. RouletteGame이 정한다(베팅 중이고 그 칸에 칩이 있을 때). 비우면 끌기 없음.</summary>
        public Func<BetTarget, bool> HasChipsAt;

        private BetTarget hover = BetTarget.None;

        // 왼쪽(놓기)·오른쪽(빼기) 버튼을 누르고 있는 칸의 시간 계산. 칸을 벗어나거나 떼면 멈춘다.
        private HoldRepeat hold;
        private HoldRepeat removeHold;

        // 칩이 있는 칸을 눌러 놓기·끌기를 아직 정하지 않은 상태(누른 칸, 누른 화면 좌표).
        private bool pressPending;
        private BetTarget pressTarget = BetTarget.None;
        private Vector2 pressScreen;

        // 휠 한 칸에 여러 프레임 값이 들어오는 장치(터치패드)에서 너무 빨리 넘어가지 않게 단계 사이 최소 간격(초).
        private const float ChipStepInterval = 0.08f;
        private float lastChipStep = -1f;

        private TabletButton hoveredButton;
        private ServedDrink hoveredDrink;
        private UpgradeBadge hoveredBadge;

        /// <summary>지금 마우스가 가리키는 베팅 대상(클릭하면 이 대상에 놓인다). 판 밖·HUD 위·트레이·태블릿이면 None.</summary>
        public BetTarget CurrentHover => hover;

        /// <summary>길게 눌러 칩을 계속 놓는 중인가.</summary>
        public bool IsRepeating => (hold != null && hold.IsRepeating) || (removeHold != null && removeHold.IsRepeating);

        /// <summary>길게 누르기를 멈춘다(돈이 모자라 더 못 놓을 때, 다 빼서 남은 칩이 없을 때 등). 버튼을 다시 눌러야 새로 시작한다.</summary>
        public void CancelHold()
        {
            hold?.Cancel();
            removeHold?.Cancel();
        }

        private void Awake()
        {
            hold = new HoldRepeat(holdDelay, repeatInterval, fastRepeatInterval, rampSeconds);
            removeHold = new HoldRepeat(holdDelay, repeatInterval, fastRepeatInterval, rampSeconds);
        }

        /// <summary>마지막으로 읽은 마우스 화면 좌표(Input System, 왼쪽 아래 원점).</summary>
        public Vector2 PointerScreen { get; private set; }

        /// <summary>칩 더미를 끄는 중인가.</summary>
        public bool IsDragging { get; private set; }

        /// <summary>끄는 칩 더미의 원래 칸.</summary>
        public BetTarget DragFrom { get; private set; } = BetTarget.None;

        /// <summary>끄는 칩 더미가 있을 자리(판 평면 위 커서 위치, 월드).</summary>
        public Vector3 DragPoint { get; private set; }

        /// <summary>끌기를 취소한다(칩은 제자리). 끄는 중이 아니면 아무것도 하지 않는다.</summary>
        public void CancelDrag()
        {
            pressPending = false;
            if (!IsDragging)
            {
                return;
            }

            IsDragging = false;
            DragFrom = BetTarget.None;
            DragCancelled?.Invoke();
        }

        /// <summary>false면 판·트레이·태블릿·음료를 가리키지도 누르지도 못한다. 끄는 순간 호버도 지운다.</summary>
        public bool Interactable
        {
            get => interactable;
            set
            {
                interactable = value;
                if (!value)
                {
                    CancelDrag();
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
            if (!interactable)
            {
                CancelDrag();
                ClearHover();
                return;
            }

            Ray ray = camera.ScreenPointToRay(screen);
            if (IsDragging)
            {
                UpdateDrag(mouse, screen, ray);
                return;
            }

            if (pressPending && UpdatePendingPress(mouse, screen))
            {
                return;
            }

            if (hud != null && hud.IsPointerOverUi(screen))
            {
                ClearHover();
                return;
            }

            ReadChipWheel(mouse);

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
                hold.Press(target);
                removeHold.Cancel();
                if (HasChipsAt != null && HasChipsAt(target))
                {
                    // 칩이 있는 칸: 끌기인지 놓기인지 정할 때까지 기다린다(UpdatePendingPress).
                    pressPending = true;
                    pressTarget = target;
                    pressScreen = screen;
                    return;
                }

                TargetClicked?.Invoke(target);
                return;
            }

            if (mouse.rightButton.wasPressedThisFrame)
            {
                removeHold.Press(target);
                hold.Cancel();
                TargetRemoveRequested?.Invoke(target);
                return;
            }

            // 길게 누르기: 일시정지(timeScale 0)와 상관없이 실제 시간으로 잰다. 막혀 있으면 Interactable이 꺼져 여기까지 오지 않는다.
            // 놓다가·빼다가 더 못 하면 RouletteGame이 CancelHold를 불러 Target이 None이 된다.
            int repeats = hold.Tick(target, mouse.leftButton.isPressed, Time.unscaledDeltaTime);
            if (repeats > 0 && pressPending)
            {
                // 칩이 있는 칸을 움직이지 않고 오래 눌렀다: 미뤄 둔 첫 칩도 지금 놓고, 이제 끌기는 하지 않는다.
                pressPending = false;
                repeats++;
            }

            for (int i = 0; i < repeats && hold.Target == target; i++)
            {
                TargetClicked?.Invoke(target);
            }

            int removes = removeHold.Tick(target, mouse.rightButton.isPressed, Time.unscaledDeltaTime);
            for (int i = 0; i < removes && removeHold.Target == target; i++)
            {
                TargetRemoveRequested?.Invoke(target);
            }
        }

        /// <summary>
        /// 칩이 있는 칸을 누른 뒤: 많이 움직이면 끌기 시작, 움직이지 않고 떼면 칩 하나 놓기. 이번 프레임을 여기서 끝냈으면 true.
        /// 계속 놓기(holdDelay)로 넘어가면 pressPending이 꺼져 여기 오지 않는다.
        /// </summary>
        private bool UpdatePendingPress(Mouse mouse, Vector2 screen)
        {
            if (!mouse.leftButton.isPressed)
            {
                pressPending = false;
                hold.Cancel();
                TargetClicked?.Invoke(pressTarget);
                return true;
            }

            if ((screen - pressScreen).sqrMagnitude <= dragThreshold * dragThreshold)
            {
                return false;
            }

            pressPending = false;
            hold.Cancel();
            IsDragging = true;
            DragFrom = pressTarget;
            DragPoint = board.WorldPositionOf(pressTarget);
            DragStarted?.Invoke(pressTarget);
            return true;
        }

        /// <summary>끄는 중: 판 위 커서 자리와 놓일 칸을 갱신하고, 버튼을 떼면 놓는다(판 밖·HUD 위면 None).</summary>
        private void UpdateDrag(Mouse mouse, Vector2 screen, Ray ray)
        {
            SetButtonHover(null);
            SetDrinkHover(null);
            SetBadgeHover(null);

            BetTarget over = BetTarget.None;
            bool overUi = hud != null && hud.IsPointerOverUi(screen);
            if (board.Raycast(ray, out Vector3 point))
            {
                DragPoint = point;
                if (!overUi)
                {
                    board.TryGetTarget(point, out over);
                }
            }

            SetHover(over);
            if (mouse.leftButton.isPressed)
            {
                return;
            }

            BetTarget from = DragFrom;
            IsDragging = false;
            DragFrom = BetTarget.None;
            DragDropped?.Invoke(from, over);
        }

        /// <summary>휠을 올리면 윗단위, 내리면 아랫단위 칩. 한 번 넘긴 뒤 잠깐은 더 넘기지 않는다.</summary>
        private void ReadChipWheel(Mouse mouse)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Approximately(scroll, 0f) || Time.unscaledTime - lastChipStep < ChipStepInterval)
            {
                return;
            }

            lastChipStep = Time.unscaledTime;
            ChipStepped?.Invoke(scroll > 0f ? 1 : -1);
        }

        private void ClearHover()
        {
            CancelHold();
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
            // 누르던 칸을 벗어나면(다른 칸·트레이·태블릿·음료) 길게 누르기를 멈춘다.
            if (hold != null && hold.Target != target)
            {
                hold.Cancel();
            }

            if (removeHold != null && removeHold.Target != target)
            {
                removeHold.Cancel();
            }

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
