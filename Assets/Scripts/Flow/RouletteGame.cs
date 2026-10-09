using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace ProjectCR
{
    /// <summary>
    /// 씬 진입점. 설정 에셋으로 GameFlowController를 만들고, HUD 입력 → 상태 전환 → 연출 대기 → 정산 순서로 잇는다.
    /// 규칙은 GameFlowController에 있고 여기는 연결만 한다.
    /// </summary>
    [DisallowMultipleComponent]
    public class RouletteGame : MonoBehaviour
    {
        [Header("데이터")]
        [SerializeField] private RouletteDefinition definition;
        [SerializeField] private GameBalance balance;

        [Tooltip("라운드 통과 보상 음료 목록. 비우면 납부 후 바로 다음 라운드.")]
        [SerializeField] private DrinkCatalog drinkCatalog;

        [Tooltip("상점 상품 목록. 비우면 상점 버튼이 꺼진다.")]
        [SerializeField] private ShopCatalog shopCatalog;

        [Header("연결")]
        [SerializeField] private RoulettePresenter presenter;
        [SerializeField] private RouletteHud hud;

        [Tooltip("음료를 서빙하는 로봇(라운드 통과 보상). 없으면 첫 후보를 바로 마신다.")]
        [SerializeField] private DrinkServerRobot drinkServer;

        [Tooltip("상점 창(HUD와 같은 UIDocument).")]
        [SerializeField] private ShopView shopView;

        [Tooltip("3D 테이블 입력(칩 놓기·빼기·칩 고르기). 없으면 테이블 없이 돈다.")]
        [SerializeField] private TableBetInput tableInput;

        [Tooltip("3D 테이블 표시(놓인 칩, 트레이, 잔액 표시).")]
        [SerializeField] private TableChipView tableView;

        [Tooltip("딜러 튜토리얼(왼쪽 위 대화창). 없으면 튜토리얼 없이 돈다.")]
        [SerializeField] private TutorialController tutorial;

        [Tooltip("효과음(칩 베팅·칩 고르기·공 굴러감). 없으면 소리 없이 돈다.")]
        [SerializeField] private GameSfx sfx;

        [Tooltip("일시정지 메뉴(Esc·[옵션]). 없으면 일시정지 없이 돈다.")]
        [SerializeField] private PauseMenuView pauseMenu;

        [Tooltip("게임 클리어·오버 결과 창([메인 메뉴로]).")]
        [SerializeField] private GameResultView resultView;

        [Tooltip("튜토리얼 페이지별 화면 강조(칩 트레이, 베팅 점, 빨강 안내, 위험 칸).")]
        [SerializeField] private TutorialHighlighter tutorialHighlighter;

        [Tooltip("보유 강화 배지(테이블 원 위). 없으면 배지 없이 돈다.")]
        [SerializeField] private UpgradeBadgeView upgradeBadges;

        [Tooltip("연출 카메라 자리 이동(베팅판 ↔ 룰렛). 없으면 카메라를 움직이지 않는다.")]
        [SerializeField] private CameraRig cameraRig;

        [Tooltip("기울어진 연출 시점.")]
        [SerializeField] private Camera presentationCamera;

        [Tooltip("앉은 자리에서 보는 1인칭 원근 시점(FirstPersonView). 시점 전환 버튼으로 연출 시점과 바꾼다. 고개 돌리기는 FirstPersonLook.")]
        [FormerlySerializedAs("topDownCamera")]
        [SerializeField] private Camera firstPersonCamera;

        [Header("디버그")]
        [Tooltip("0이면 매번 다른 시드. 0이 아니면 같은 시드에서 같은 결과 순서가 나온다. 실제 시드는 콘솔에 찍힌다.")]
        [SerializeField] private int randomSeed;

        [Tooltip("공 i의 결과를 이 번호로 고정한다. 비우면 추첨 결과 그대로. -1은 그 공만 추첨대로, -2는 해골.")]
        [SerializeField] private List<int> forcedNumbers = new List<int>();

        [Tooltip("회전마다 공을 이만큼 더 굴린다. 공 여러 개 연출·정산 확인용.")]
        [Min(0)]
        [SerializeField] private int extraBallCount;

        [Tooltip("0보다 크면 숫자 하나 베팅의 지급 배수를 이 값으로 바꾼다. 배율 변경 확인용.")]
        [Min(0)]
        [FormerlySerializedAs("payoutMultiplierOverride")]
        [SerializeField] private int straightPayoutOverride;

        [Tooltip("새 게임을 이 음료 잔 수로 시작한다(밸런스 시험용). 순서: 1열, 1–12, 홀수, 빨강, 1–18, 0번(묶음 대표 대상).")]
        [SerializeField] private int[] debugStartDrinkStacks = new int[0];

        [Tooltip("연출 배속.")]
        [Min(0.1f)]
        [SerializeField] private float presentationSpeed = 1f;

        [Tooltip("37칸 순회 검증 때의 배속.")]
        [Min(0.1f)]
        [SerializeField] private float verificationSpeed = 8f;

        private GameFlowController flow;
        private bool verifying;
        private bool firstPersonView;

        /// <summary>검증 스크립트가 상태를 읽을 때 쓴다.</summary>
        public GameFlowController Flow => flow;

        public bool IsVerifying => verifying;

        private void Start()
        {
            hud.Initialize(definition);
            hud.DrinkPicker.Confirmed += OnDrinkTargetsConfirmed;
            hud.DrinkPicker.Cancelled += OnDrinkTargetsCancelled;
            GamePause.Changed += OnPauseChanged;
            if (pauseMenu != null)
            {
                hud.OptionsClicked += pauseMenu.Open;
                pauseMenu.RestartRequested += StartNewRun;
            }

            if (shopView != null)
            {
                shopView.Initialize();
                shopView.Closed += OnShopClosed;
                shopView.BuyRequested += OnShopBuy;
                shopView.PickTargetRequested += OnShopPickTarget;
            }
            hud.ViewToggled += ToggleView;


            if (tableInput != null)
            {
                tableInput.TargetClicked += OnTargetClicked;
                tableInput.TargetRemoveRequested += OnTargetRemoveRequested;
                tableInput.ChipSelected += OnChipSelected;
                tableInput.TabletPressed += OnTabletPressed;
                tableInput.DrinkHoverChanged += OnDrinkHover;
                tableInput.DrinkClicked += OnDrinkClicked;
                tableInput.BadgeHoverChanged += OnBadgeHover;
                tableInput.DragStarted += OnChipDragStarted;
                tableInput.DragDropped += OnChipDragDropped;
                tableInput.DragCancelled += OnChipDragCancelled;
                tableInput.HasChipsAt = target =>
                    flow != null && !InputLocked && pickingSlot < 0 && flow.Phase == GamePhase.Betting && flow.Bet.StakeOn(target) > 0;
            }

            if (tutorial != null)
            {
                tutorial.Initialize();
                tutorial.HighlightChanged += OnTutorialHighlight;
                tutorial.CameraRequested += OnTutorialCamera;
            }

            ApplyView();
            if (cameraRig != null)
            {
                // 게임은 시작 자리(좌석에서 홀을 보는 시점)에서 시작한다. 베팅판으로는 OpeningGuide가 옮긴다.
                cameraRig.SnapToDefault();
            }

            StartNewRun();
        }

        private void OnValidate()
        {
            if (Application.isPlaying && flow != null)
            {
                ApplyDebugOptions();
                RefreshViews();
            }
        }

        public void StartNewRun()
        {
            StopAllCoroutines();
            verifying = false;
            presenter.ResetView();
            if (sfx != null)
            {
                sfx.StopBallRoll();
            }

            if (cameraRig != null)
            {
                // 새 게임은 시작 자리에서. 다시 시작이면 지금 자리에서 그쪽으로 옮겨 간다(처음 시작에서는 이미 그 자리다).
                cameraRig.MoveToDefault();
            }

            if (drinkServer != null)
            {
                drinkServer.ResetToEntry();
            }

            drinkHovered = -1;
            drinkSelected = -1;
            badgeHovered = -1;
            CloseDrinkPicker();

            if (shopView != null)
            {
                shopView.Hide();
            }

            pickingSlot = -1;

            // 새 객체라서 돈·라운드·음료 잔 수가 전부 처음으로 돌아간다.
            flow = new GameFlowController(definition, balance, randomSeed, drinkCatalog, shopCatalog);
            for (int i = 0; i < debugStartDrinkStacks.Length && i < DrinkGroups.Count; i++)
            {
                flow.Drinks.SetStacks(((DrinkGroup)i).Representative(), debugStartDrinkStacks[i]);
            }

            ApplyDebugOptions();
            flow.SetChipValue(Math.Max(balance.MinBet, Math.Min(balance.MinBet * 10, flow.Wallet.Balance)));

            Debug.Log($"[RouletteGame] 새 게임. 시드 {flow.Seed}");

            hud.ClearHistory();
            hud.ClearResult();

            // 튜토리얼: 최초로 시작한 게임만 대상. 이전 게임의 대화·강조·결말 대기는 정리한다.
            tutorialActive = false;
            if (resultView != null)
            {
                resultView.Hide();
            }

            hud.SetResultVisible(true);
            if (tutorial != null)
            {
                tutorial.BeginRun();
            }

            openingActive = true;
            ApplyInputLock();
            RefreshViews();
            StartCoroutine(OpeningGuide());
        }

        /// <summary>HUD 회전 버튼과 같은 경로. 상태가 Betting이 아니면 GameFlowController가 거절한다.</summary>
        public void RequestSpin()
        {
            OnSpinClicked();
        }

        private void ApplyDebugOptions()
        {
            flow.ForcedNumbers = forcedNumbers.Count > 0 ? forcedNumbers.ToArray() : null;
            flow.ExtraBallCount = extraBallCount;
            flow.StraightPayoutOverride = straightPayoutOverride;
            presenter.SpeedMultiplier = presentationSpeed;
        }

        private void OnTargetClicked(BetTarget target)
        {
            if (InputLocked)
            {
                return;
            }

            if (pickingSlot >= 0)
            {
                // 대상 고르기 클릭이 길게 누르기로 이어져 칩이 놓이지 않게 한다.
                tableInput.CancelHold();
                FinishPick(target);
                return;
            }

            BetError error = flow.PlaceChip(target);
            if (error == BetError.None && sfx != null)
            {
                sfx.PlayBetChip();
            }
            else if (error != BetError.None && tableInput != null)
            {
                // 길게 눌러 계속 놓다가 막히면(돈 부족 등) 멈춘다. 안내는 한 번만 뜬다.
                tableInput.CancelHold();
            }

            ApplyBetAction(error);
        }

        private void OnTargetRemoveRequested(BetTarget target)
        {
            if (InputLocked)
            {
                return;
            }

            if (pickingSlot >= 0)
            {
                tableInput.CancelHold();
                FinishPick(BetTarget.None);
                return;
            }

            // 칩 하나만큼 뺀다. 길게 눌러 빼다가 그 칸이 비면 멈춘다.
            BetError error = flow.RemoveChip(target);
            if ((error != BetError.None || flow.Bet.StakeOn(target) == 0) && tableInput != null)
            {
                tableInput.CancelHold();
            }

            ApplyBetAction(error);
        }

        // ---------- 칩 끌기 ----------

        private void OnChipDragStarted(BetTarget from)
        {
            if (tableView != null)
            {
                tableView.BeginDrag(from, tableInput.DragPoint);
            }

            if (sfx != null)
            {
                sfx.PlayGrabChip();
            }
        }

        /// <summary>끌던 칩 더미를 놓았다: 다른 칸이면 통째로 옮기고(있으면 합침), 판 밖이면 뺀다. 같은 칸이면 제자리.</summary>
        private void OnChipDragDropped(BetTarget from, BetTarget to)
        {
            if (tableView != null)
            {
                tableView.EndDrag();
            }

            if (InputLocked || from == to)
            {
                RefreshViews();
                return;
            }

            BetError error = flow.MoveBet(from, to);
            if (error == BetError.None && !to.IsNone && sfx != null)
            {
                sfx.PlayBetChip();
            }

            ApplyBetAction(error);
        }

        private void OnChipDragCancelled()
        {
            if (tableView != null)
            {
                tableView.EndDrag();
            }

            RefreshViews();
        }

        /// <summary>테이블 태블릿 버튼: 회전 · 서비스(상점) · 전체 회수(전체 취소). HUD 버튼과 같은 경로라 규칙도 같다.</summary>
        private void OnTabletPressed(TabletAction action)
        {
            if (InputLocked)
            {
                return;
            }

            switch (action)
            {
                case TabletAction.Spin when flow.Phase == GamePhase.DrinkSelection:
                    ConfirmServedDrink();
                    break;
                case TabletAction.Spin:
                    OnSpinClicked();
                    break;
                case TabletAction.Service:
                    OnShopClicked();
                    break;
                default:
                    OnClearClicked();
                    break;
            }
        }

        // ---------- 상점 ----------

        // 숫자 집중 대상을 판에서 고르는 중인 진열 칸. -1이면 아님.
        private int pickingSlot = -1;

        private void OnShopClicked()
        {
            if (shopView == null || verifying || InputLocked || !flow.EnterShop())
            {
                return;
            }

            if (tutorial != null)
            {
                tutorial.EndServiceHighlight();
            }

            shopView.Show(flow);
            RefreshViews();
        }

        private void OnShopClosed()
        {
            pickingSlot = -1;
            if (!flow.CloseShop())
            {
                return;
            }

            shopView.Hide();
            RefreshViews();

            // 상점에서 돈을 다 써서 걸 돈도 칩도 없으면 기존 규칙대로 납부 판정.
            if (flow.Phase == GamePhase.RoundPayment)
            {
                StartCoroutine(ResolvePayment());
            }
        }

        private void OnShopBuy(int slot, BetTarget target, int discardSlot)
        {
            // 버릴 강화 이름은 사기 전에 읽어 둔다(사면 그 칸에 새 강화가 들어간다).
            string discarded = flow.Shop.NeedsDiscard(slot) && flow.OwnedUpgrades.IsOccupied(discardSlot)
                ? flow.OwnedUpgrades.Slots[discardSlot].Item.DisplayName
                : null;
            ShopError error = flow.Shop.Buy(slot, target, discardSlot);
            string bought = flow.Shop.Offers[slot].Item.DisplayName;
            shopView.ShowNotice(error != ShopError.None ? error.ToKorean()
                : discarded != null ? $"'{discarded}' 버리고 '{bought}' 구매" : $"{bought} 구매");
            if (error == ShopError.None)
            {
                Debug.Log($"[RouletteGame] 상점 구매: {flow.Shop.Offers[slot].Item.Id} {target} (버림 {discarded ?? "-"}) → 보유 {flow.Wallet.Balance:N0}");
            }

            shopView.Refresh(flow);
            RefreshViews();
        }

        /// <summary>숫자 집중 대상 고르기: 창을 접고 판 클릭을 기다린다. 오른쪽 클릭은 취소. 돈은 움직이지 않는다.</summary>
        private void OnShopPickTarget(int slot)
        {
            pickingSlot = slot;
            shopView.SetPicking(true);
        }

        private void FinishPick(BetTarget target)
        {
            int slot = pickingSlot;
            pickingSlot = -1;
            shopView.SetPicking(false);

            if (!target.IsNone && slot < flow.Shop.Offers.Count)
            {
                if (ShopController.IsValidFocusTarget(flow.Shop.Offers[slot].Item.Group, target))
                {
                    shopView.SetTarget(slot, target);
                    shopView.ShowNotice($"대상: {DrinkOffer.TargetLabel(target)}");
                }
                else
                {
                    shopView.ShowNotice("숫자 하나나 0이 없는 조합만 고를 수 있습니다");
                }
            }

            shopView.Refresh(flow);
        }

        private void OnClearClicked()
        {
            if (!InputLocked)
            {
                ApplyBetAction(flow.ClearBets());
            }
        }

        /// <summary>트레이에서 고른 칩 금액. 보유보다 큰 칩도 고를 수 있고, 놓을 때 막힌다.</summary>
        private void OnChipSelected(long value)
        {
            if (InputLocked)
            {
                return;
            }

            BetError error = flow.SetChipValue(value);
            if (error == BetError.None && sfx != null)
            {
                sfx.PlayGrabChip();
            }

            ApplyBetAction(error);
        }

        // ---------- 판 위 호버 안내(툴팁 + 칸 강조) ----------

        private BetTarget guideTarget = BetTarget.None;
        private bool guideShown;
        private bool guidePicking;
        private bool guideDirty = true;

        private void LateUpdate()
        {
            UpdateBetGuide();
            UpdateBadgeTooltip();

            // 음료 툴팁은 커서를 따라간다.
            if (drinkHovered >= 0 && flow != null && tableInput != null)
            {
                if (CanPickDrink)
                {
                    hud.MoveBetTooltip(tableInput.PointerScreen);
                }
                else
                {
                    drinkHovered = -1;
                    hud.HideBetTooltip();
                }
            }
        }

        private void OnDisable()
        {
            HideBetGuide();
        }

        /// <summary>
        /// 매 프레임 표시 여부를 정한다: 베팅 중(또는 상점의 숫자 집중 대상 고르기 중)이고, 검증·카메라 이동 중이 아니고, 판 위 대상이 있을 때만.
        /// 그래서 회전·해골 재회전·결과 연출·정산·납부·음료·서비스 창·게임 오버 동안은 숨는다.
        /// 내용은 대상이 바뀌었거나 RefreshViews(칩 금액·베팅·구매·음료)가 있었을 때만 다시 만들고, 위치는 매 프레임 커서를 따라간다.
        /// 대상은 TableBetInput이 클릭에 쓰는 바로 그 값이다.
        /// </summary>
        private void UpdateBetGuide()
        {
            if (flow == null || tableInput == null)
            {
                return;
            }

            BetTarget target = tableInput.CurrentHover;

            // 칩 더미를 끄는 중: 더미가 커서를 따라가고, 놓일 칸만 강조한다(놓기 툴팁은 숨긴다).
            if (tableInput.IsDragging)
            {
                if (tableView != null)
                {
                    tableView.MoveDrag(tableInput.DragPoint);
                    tableView.SetHover(!target.IsNone, target, definition);
                }

                hud.HideBetTooltip();
                guideShown = true;
                guideDirty = true;
                guideTarget = BetTarget.None;
                return;
            }

            bool picking = pickingSlot >= 0;
            bool allowed = !verifying
                           && (cameraRig == null || !cameraRig.IsMoving)
                           && (flow.Phase == GamePhase.Betting || (picking && flow.Phase == GamePhase.Shopping));
            if (!allowed || target.IsNone)
            {
                HideBetGuide();
                return;
            }

            if (!guideShown || guideDirty || target != guideTarget || picking != guidePicking)
            {
                long chip = flow.Bet.ChipValue;
                BetHoverInfo info = BetHoverInfo.Create(target, flow.BuildSpinConfig(), chip, flow.Wallet.MaxMoney,
                    flow.Wallet.CanAfford(flow.Bet.TotalStake + chip),
                    BetHoverInfo.CrossProgressOf(target, flow.Cross, flow.Round.CurrentDue));
                hud.ShowBetTooltip(info, !picking);
                if (tableView != null)
                {
                    tableView.SetHover(true, target, definition);
                }

                guideTarget = target;
                guidePicking = picking;
                guideDirty = false;
                guideShown = true;
            }

            hud.MoveBetTooltip(tableInput.PointerScreen);
        }

        // ---------- 강화 배지 호버 툴팁 ----------

        // 마우스가 올라간 배지의 강화 칸 번호(-1이면 없음)와 그 툴팁 문구.
        private int badgeHovered = -1;
        private UpgradeHoverText badgeText;
        private bool badgeTextDirty = true;

        private void OnBadgeHover(UpgradeBadge badge)
        {
            int previous = badgeHovered;
            badgeHovered = badge != null ? badge.SlotIndex : -1;
            badgeTextDirty = true;
            if (upgradeBadges != null)
            {
                upgradeBadges.SetHovered(badgeHovered);
            }

            if (badgeHovered < 0 && previous >= 0)
            {
                hud.HideBetTooltip();
            }
        }

        /// <summary>
        /// 배지 툴팁은 매 프레임 다시 건다: 판 호버 안내가 같은 툴팁을 숨겨도(판 → 배지로 옮길 때) 바로 되살아난다.
        /// 문구는 배지가 바뀌었거나 RefreshViews(구매·정산 등)가 있었을 때만 다시 만든다.
        /// </summary>
        private void UpdateBadgeTooltip()
        {
            if (badgeHovered < 0 || flow == null || tableInput == null)
            {
                return;
            }

            if (!flow.OwnedUpgrades.IsOccupied(badgeHovered))
            {
                // 보던 배지가 사라졌다(해골 방어 사용 등).
                OnBadgeHover(null);
                return;
            }

            if (badgeTextDirty)
            {
                badgeText = UpgradeHoverText.For(flow.OwnedUpgrades.Slots[badgeHovered], flow);
                badgeTextDirty = false;
            }

            hud.ShowUpgradeTooltip(badgeText);
            hud.MoveBetTooltip(tableInput.PointerScreen);
        }

        private void HideBetGuide()
        {
            if (!guideShown)
            {
                return;
            }

            guideShown = false;
            guideTarget = BetTarget.None;
            if (hud != null)
            {
                hud.HideBetTooltip();
            }

            if (tableView != null)
            {
                tableView.SetHover(false, BetTarget.None);
            }
        }

        private void ApplyBetAction(BetError error)
        {
            RefreshViews();
            if (error != BetError.None)
            {
                Debug.Log($"[RouletteGame] 베팅 거절: {error}");
            }
        }

        private void RefreshViews()
        {
            guideDirty = true;
            hud.Refresh(flow);
            if (tableView != null && tableView.Tablet != null)
            {
                tableView.Tablet.DrinkReady = drinkSelected >= 0 && flow.Phase == GamePhase.DrinkSelection;
            }

            if (tableView != null)
            {
                tableView.Refresh(flow);
            }

            if (upgradeBadges != null)
            {
                upgradeBadges.Refresh(flow);
            }

            badgeTextDirty = true;
        }

        private void OnSpinClicked()
        {
            if (verifying || InputLocked)
            {
                return;
            }

            ApplyDebugOptions();

            BetError error = flow.TryBeginSpin(out ActiveSpin spin);
            if (error != BetError.None)
            {
                Debug.Log($"[RouletteGame] 회전 거절: {error}");
                return;
            }

            Debug.Log($"[RouletteGame] 회전 #{spin.Id}: {Describe(spin.Bets)} → 결과 {Describe(spin.Result)}");
            if (tutorial != null)
            {
                tutorial.EndServiceHighlight();
                tutorial.HideNotice();
            }

            RefreshViews();
            StartCoroutine(RunSpin(spin));
        }

        private IEnumerator RunSpin(ActiveSpin spin)
        {
            // 베팅은 이미 확정·차감됐다. 카메라가 룰렛 자리에 도착한 뒤 휠과 공을 돌린다.
            if (cameraRig != null)
            {
                cameraRig.MoveToRoulette();
                while (cameraRig.IsMoving)
                {
                    yield return null;
                }
            }

            if (sfx != null)
            {
                sfx.PlayBallRoll();
            }

            yield return presenter.Play(spin.Result);

            if (flow.CompleteSpin(spin.Id, out SettlementResult settlement))
            {
                Debug.Log($"[RouletteGame] 회전 #{spin.Id} 정산: {settlement.Outcome}, 지급 {settlement.TotalPayout:N0}, 해골 벌칙 {settlement.SkullPenalty:N0}, 연속 낙첨 {flow.ConsecutiveLosses}");
                hud.ShowSettlement(settlement);
            }

            // 정산과 함께 베팅이 비었으니 판의 칩도 이 갱신에서 사라진다.
            RefreshViews();

            // 결과 칸을 잠시 보여 준 뒤 베팅판으로 돌아간다. 라운드가 끝났으면 룰렛에 머물러 납부를 판정한다.
            if (flow.Phase == GamePhase.RoundPayment)
            {
                yield return ResolvePayment();
            }
            else if (cameraRig != null && flow.Phase == GamePhase.Betting)
            {
                cameraRig.MoveToBetting(cameraRig.ReturnDelay);
            }
        }

        /// <summary>
        /// 마지막 회전 결과를 보여 준 뒤 납부를 한 번 판정한다. 버튼이 없다.
        /// 성공하면 음료 창, 실패하면 게임 오버 창. 이중 차감은 GameFlowController.SubmitPayment가 상태로 막는다.
        /// </summary>
        private IEnumerator ResolvePayment()
        {
            // 시작 안내가 남아 있으면(회전 없이 라운드가 끝난 경우) 납부 연출 전에 닫는다.
            if (tutorial != null)
            {
                tutorial.HideNotice();
            }

            int round = flow.Round.Round;
            long due = flow.Round.CurrentDue;
            yield return new WaitForSeconds(cameraRig != null ? cameraRig.ReturnDelay : 1.5f);

            PaymentOutcome? outcome = flow.SubmitPayment();
            if (outcome == null)
            {
                yield break;
            }

            Debug.Log($"[RouletteGame] 라운드 {round} 납부 {outcome}: 납부금 {due:N0}, 남은 돈 {flow.Wallet.Balance:N0}");

            // 결말(게임 오버·클리어): 튜토리얼 대상 게임이면 딜러 대사를 먼저, 끝나면 결과 창.
            if (flow.Phase == GamePhase.GameOver || flow.Phase == GamePhase.GameClear)
            {
                bool cleared = flow.Phase == GamePhase.GameClear;
                Debug.Log(cleared ? $"[RouletteGame] 게임 클리어: 라운드 {round}까지 납부" : "[RouletteGame] 게임 오버");
                RefreshViews();
                if (tutorial != null && tutorial.WillPlayEnding)
                {
                    yield return RunTutorial(tutorial.PlayEnding(cleared));
                }

                // 결과 창은 결말 대사가 끝난 뒤(튜토리얼이 없으면 바로).
                if (resultView != null)
                {
                    resultView.Show(flow);
                }

                ApplyInputLock();
                yield break;
            }

            if (flow.Phase == GamePhase.DrinkSelection && drinkServer != null)
            {
                // 로봇이 오른쪽으로 와서 선반에 후보를 올려 둔다. 카메라는 로봇 쪽으로.
                drinkHovered = -1;
                drinkSelected = -1;
                if (cameraRig != null)
                {
                    cameraRig.MoveToDrink();
                }

                RefreshViews();
                yield return drinkServer.Serve(flow.Rewards.CurrentOffer);

                // 첫 라운드 통과(튜토리얼): 로봇과 음료가 보이는 상태에서 딜러 안내, 끝나면 음료를 고를 수 있다.
                if (tutorial != null && tutorial.Progress.ShouldShow(TutorialStep.Drink))
                {
                    while (cameraRig != null && cameraRig.IsMoving)
                    {
                        yield return null;
                    }

                    yield return RunTutorial(tutorial.PlayDrink());
                }
            }
            else if (flow.Phase == GamePhase.DrinkSelection)
            {
                // 로봇이 연결되지 않았으면 진행이 막히지 않게 첫 후보를 마신다.
                Debug.LogWarning("[RouletteGame] 서빙 로봇이 연결되지 않아 첫 후보를 고른다.");
                OnDrinkConfirmed(0);
                yield break;
            }
            else if (flow.Phase == GamePhase.Betting)
            {
                MoveCameraToBetting();
            }
            else if (flow.Phase == GamePhase.RoundPayment)
            {
                // 음료 목록이 없고 새 라운드에도 걸 돈이 없다. 다시 판정한다.
                yield return ResolvePayment();
                yield break;
            }

            RefreshViews();
        }

        // ---------- 음료(서빙 로봇) ----------

        // 로봇 선반에서 마우스가 가리키는 잔, 고른 잔(후보 번호). -1이면 없음.
        private int drinkHovered = -1;
        private int drinkSelected = -1;

        private bool CanPickDrink => flow.Phase == GamePhase.DrinkSelection && drinkServer != null && drinkServer.IsServing;

        private void OnDrinkHover(ServedDrink drink)
        {
            drinkHovered = drink != null && CanPickDrink ? drink.OfferIndex : -1;
            if (drinkServer != null)
            {
                drinkServer.SetHovered(drinkHovered);
            }

            ShowDrinkTooltip();
        }

        /// <summary>
        /// 한 번 누르면 고르고(외곽선 고정), 고른 잔을 한 번 더 누르면 마신다. 태블릿 [마시기]도 같다.
        /// 확률 음료는 마시기 전에 대상 고르기 창을 연다.
        /// </summary>
        private void OnDrinkClicked(ServedDrink drink)
        {
            if (drink == null || !CanPickDrink || InputLocked)
            {
                return;
            }

            if (drinkSelected == drink.OfferIndex)
            {
                ConfirmServedDrink();
                return;
            }

            drinkSelected = drink.OfferIndex;
            drinkServer.SetSelected(drinkSelected);
            ShowDrinkTooltip();
            RefreshViews();
        }

        private void ConfirmServedDrink()
        {
            if (drinkSelected < 0 || !CanPickDrink || InputLocked)
            {
                return;
            }

            OnDrinkConfirmed(drinkSelected);
        }

        private void ShowDrinkTooltip()
        {
            IReadOnlyList<DrinkOffer> offer = flow.Rewards.CurrentOffer;
            if (drinkHovered < 0 || offer == null || drinkHovered >= offer.Count)
            {
                hud.HideBetTooltip();
                return;
            }

            string hint = drinkSelected != drinkHovered ? "누르면 고릅니다"
                : offer[drinkHovered].IsProbability ? "한 번 더 누르면 대상을 고릅니다 · 태블릿 [마시기]"
                : "한 번 더 누르면 마십니다 · 태블릿 [마시기]";
            hud.ShowDrinkTooltip(DrinkHoverText.For(offer[drinkHovered], flow), hint);
            hud.MoveBetTooltip(tableInput.PointerScreen);
        }

        // 대상 고르기 창이 열린 확률 음료(후보 번호). -1이면 없음.
        private int pickingDrink = -1;

        private DrinkTargetPicker DrinkPicker => hud != null ? hud.DrinkPicker : null;

        /// <summary>확률 음료 대상 고르기 창을 연다. 열려 있는 동안 3D 입력은 막힌다.</summary>
        private void OpenDrinkPicker(int offerIndex)
        {
            IReadOnlyList<DrinkOffer> offer = flow.Rewards.CurrentOffer;
            if (DrinkPicker == null || offer == null || offerIndex < 0 || offerIndex >= offer.Count)
            {
                return;
            }

            pickingDrink = offerIndex;
            hud.HideBetTooltip();
            DrinkPicker.Open(offer[offerIndex], flow);
            ApplyInputLock();
        }

        private void CloseDrinkPicker()
        {
            pickingDrink = -1;
            if (DrinkPicker != null)
            {
                DrinkPicker.Close();
            }

            ApplyInputLock();
        }

        private void OnDrinkTargetsConfirmed(IReadOnlyList<BetTarget> targets)
        {
            int index = pickingDrink;
            CloseDrinkPicker();
            if (index >= 0)
            {
                OnDrinkConfirmed(index, targets);
            }
        }

        private void OnDrinkTargetsCancelled()
        {
            CloseDrinkPicker();
            RefreshViews();
        }

        /// <summary>
        /// 음료를 마신다. 확률 음료인데 대상이 없으면 고르기 창을 연다(로봇이 없을 때는 첫 대상으로 바로 마신다).
        /// </summary>
        private void OnDrinkConfirmed(int offerIndex, IReadOnlyList<BetTarget> targets = null)
        {
            IReadOnlyList<DrinkOffer> current = flow.Rewards.CurrentOffer;
            if (targets == null && current != null && offerIndex >= 0 && offerIndex < current.Count && current[offerIndex].IsProbability)
            {
                if (drinkServer != null && DrinkPicker != null)
                {
                    OpenDrinkPicker(offerIndex);
                    return;
                }

                targets = new[] { current[offerIndex].TargetOptions()[0] };
            }

            DrinkOffer drink = flow.ConfirmDrink(offerIndex, targets);
            if (drink == null)
            {
                // 이미 확정했거나 상태가 바뀌었다.
                return;
            }

            // 고른 잔은 치우고 로봇은 나머지를 싣고 돌아간다.
            drinkHovered = -1;
            drinkSelected = -1;
            hud.HideBetTooltip();
            Coroutine robotLeaving = null;
            if (drinkServer != null)
            {
                drinkServer.SetSelected(-1);
                robotLeaving = StartCoroutine(drinkServer.Leave(offerIndex));
            }

            if (drink.IsProbability)
            {
                SpinConfig config = flow.BuildSpinConfig();
                var parts = new List<string>();
                foreach (BetTarget target in flow.LastDrinkTargets)
                {
                    parts.Add($"{DrinkOffer.TargetLabel(target)} {config.ProbabilityOf(target):P2}");
                }

                Debug.Log($"[RouletteGame] 확률 음료 {drink.DisplayName}: +{drink.Boost} ÷ {flow.LastDrinkTargets.Count} → {string.Join(", ", parts)}");
            }
            else
            {
                Debug.Log($"[RouletteGame] 음료 {drink.DisplayName}({DrinkOffer.TargetLabel(drink.Target)}) {flow.Drinks.StacksOf(drink.Target)}잔 → ×{flow.Drinks.MultiplierOf(drink.Target)}");
            }
            RefreshViews();

            if (flow.Phase == GamePhase.Betting)
            {
                MoveCameraToBetting();
                StartCoroutine(ServiceGuide(robotLeaving));
            }
            else if (flow.Phase == GamePhase.RoundPayment)
            {
                // 새 라운드에 걸 돈이 없다. 기존 규칙대로 바로 납부 판정.
                StartCoroutine(ResolvePayment());
            }
        }

        private void MoveCameraToBetting()
        {
            if (cameraRig != null)
            {
                cameraRig.MoveToBetting();
            }
        }

        private static string Describe(SpinResult result)
        {
            var numbers = new List<string>(result.Balls.Count);
            foreach (BallResult ball in result.Balls)
            {
                numbers.Add(ball.IsSkull ? "해골" : ball.Number.ToString());
            }

            return string.Join(", ", numbers);
        }

        private string Describe(IReadOnlyList<ConfirmedBet> bets)
        {
            var parts = new List<string>(bets.Count);
            foreach (ConfirmedBet bet in bets)
            {
                parts.Add($"{bet.Target.ToKorean(definition)} {bet.Stake:N0}");
            }

            return string.Join(", ", parts);
        }

        private static string Describe(PocketDefinition pocket)
        {
            return pocket.IsSkull ? "해골" : pocket.Number.ToString();
        }

        private void ToggleView()
        {
            if (InputLocked)
            {
                return;
            }

            firstPersonView = !firstPersonView;
            ApplyView();
        }

        private void ApplyView()
        {
            if (presentationCamera != null)
            {
                presentationCamera.enabled = !firstPersonView;
            }

            if (firstPersonCamera != null)
            {
                firstPersonCamera.enabled = firstPersonView;
            }

            ApplyInputLock();
        }

        // ---------- 튜토리얼 ----------

        // 튜토리얼 대화를 띄우는 중(대화창이 실제로 열리기 전 한 프레임 포함).
        private bool tutorialActive;

        /// <summary>
        /// 게임 입력을 막는 때: 튜토리얼 대화 중(대화창 버튼만 받는다), 일시정지 중(메뉴만 받는다), 결과 창이 떠 있을 때.
        /// 막는 것: 칩·베팅·회전·전체 회수·서비스·음료·시점 전환.
        /// </summary>
        private bool InputLocked => tutorialActive || openingActive || (tutorial != null && tutorial.IsRunning) || GamePause.IsPaused
                                    || (resultView != null && resultView.IsShown) || pickingDrink >= 0;

        /// <summary>일시정지가 바뀌면 3D 입력을 맞추고, 켜질 때는 판·음료 안내를 지운다.</summary>
        private void OnPauseChanged(bool paused)
        {
            ApplyInputLock();
            if (paused)
            {
                HideBetGuide();
            }
        }

        private void OnDestroy()
        {
            GamePause.Changed -= OnPauseChanged;
        }

        /// <summary>
        /// 3D 테이블 입력을 지금 상태에 맞춘다. 1인칭은 둘러보기 전용이고, 대화 중에도 막는다.
        /// 끄는 순간 호버가 지워져 판·음료·배지 툴팁과 강조도 사라진다.
        /// </summary>
        private void ApplyInputLock()
        {
            if (tableInput != null)
            {
                tableInput.Interactable = !firstPersonView && !InputLocked;
            }
        }

        /// <summary>
        /// 대사 한 묶음을 띄운다: 그동안 입력을 막고 결과 안내를 숨긴다. 1인칭이면 연출 시점으로 돌아온다(로봇·음료가 보이게).
        /// 끝나면 지금 상태에 맞는 입력을 되돌린다. 돈·회전·납부는 건드리지 않는다.
        /// </summary>
        private IEnumerator RunTutorial(IEnumerator play)
        {
            tutorialActive = true;
            if (firstPersonView)
            {
                firstPersonView = false;
                ApplyView();
            }

            ApplyInputLock();
            HideBetGuide();
            hud.HideBetTooltip();
            hud.SetResultVisible(false);

            yield return play;

            tutorialActive = false;
            hud.SetResultVisible(true);
            ApplyInputLock();
            RefreshViews();
        }

        /// <summary>튜토리얼 페이지 강조. 강조가 끝나면 화면을 다시 그려 칩 트레이 등 원래 표시로 돌린다.</summary>
        private void OnTutorialHighlight(TutorialHighlight highlight)
        {
            if (tutorialHighlighter == null)
            {
                return;
            }

            tutorialHighlighter.Show(highlight, flow);
            if (highlight == TutorialHighlight.None && flow != null)
            {
                RefreshViews();
            }
        }

        // 게임 시작 연출 중(시작 자리 → 베팅판). 이동이 끝날 때까지 베팅 입력을 막는다.
        private bool openingActive;

        [Header("시작 연출")]
        [Tooltip("튜토리얼이 없는 게임에서 시작 자리를 보여 주는 시간(초). 그 뒤 베팅판으로 옮겨 간다.")]
        [Min(0f)]
        [SerializeField] private float openingHold = 1f;

        /// <summary>
        /// 새 게임 시작: 시작 자리(CameraPostion_Default)에서 출발한다. 튜토리얼 대상 게임이면 도입 안내를 하고(페이지마다 카메라 자리를 옮긴다),
        /// 아니면 시작 자리를 잠깐 보여 준다. 둘 다 끝나면 베팅판으로 옮겨 가고, 도착하면 베팅을 받는다.
        /// </summary>
        private IEnumerator OpeningGuide()
        {
            yield return null;
            while (cameraRig != null && cameraRig.IsMoving)
            {
                yield return null;
            }

            bool tutorialIntro = tutorial != null && tutorial.Progress.ShouldShow(TutorialStep.Intro);
            if (tutorialIntro)
            {
                yield return RunTutorial(tutorial.PlayIntro());
            }
            else
            {
                yield return new WaitForSeconds(openingHold);
            }

            if (cameraRig != null)
            {
                cameraRig.MoveToBetting();
                while (cameraRig.IsMoving)
                {
                    yield return null;
                }
            }

            openingActive = false;
            ApplyInputLock();

            // 튜토리얼 없이 시작한 게임: 딜러가 할 일을 짧게 알려 준다(첫 회전을 시작하면 사라진다). 튜토리얼 게임은 도입에서 이미 설명했다.
            if (!tutorialIntro && tutorial != null)
            {
                tutorial.ShowStartNotice();
            }
        }

        /// <summary>튜토리얼 페이지의 카메라 자리: 시작 자리 / 베팅판 / 룰렛.</summary>
        private void OnTutorialCamera(TutorialCamera place)
        {
            if (cameraRig == null)
            {
                return;
            }

            switch (place)
            {
                case TutorialCamera.Default:
                    cameraRig.MoveToDefault();
                    break;
                case TutorialCamera.Board:
                    cameraRig.MoveToBetting();
                    break;
                case TutorialCamera.Roulette:
                    cameraRig.MoveToRoulette();
                    break;
            }
        }

        /// <summary>
        /// 서비스 안내: 음료를 고른 뒤 로봇이 떠나고 베팅 화면으로 돌아오면, 상점을 열 수 있을 때 한 번.
        /// 그 사이 회전을 시작했거나 상점에 들어갔으면 이번에는 건너뛴다(다음 라운드 통과 때 다시 본다).
        /// </summary>
        private IEnumerator ServiceGuide(Coroutine robotLeaving)
        {
            if (tutorial == null || !tutorial.Progress.ShouldShow(TutorialStep.Service))
            {
                yield break;
            }

            if (robotLeaving != null)
            {
                yield return robotLeaving;
            }

            while (cameraRig != null && cameraRig.IsMoving)
            {
                yield return null;
            }

            if (flow.CanEnterShop && !InputLocked)
            {
                yield return RunTutorial(tutorial.PlayService(tableView != null ? tableView.Tablet : null));
            }
        }

        // ---------- 검증 ----------

        /// <summary>
        /// 모든 칸(해골 포함)을 휠 순서대로 하나씩 강제로 굴려, 공에 가장 가까운 앵커가 목표 칸인지,
        /// 휠이 멈췄을 때 그 칸이 정해진 자리(6시)에 있는지 확인한다.
        /// 돈과 라운드는 건드리지 않는다. 플레이 모드에서만 동작한다.
        /// </summary>
        [ContextMenu("전체 칸 순회 검증")]
        public void StartPocketVerification()
        {
            if (!Application.isPlaying || verifying || presenter.IsPlaying)
            {
                Debug.LogWarning("[검증] 플레이 모드에서, 회전 중이 아닐 때 실행할 것.");
                return;
            }

            StartCoroutine(VerifyAllPockets());
        }

        [ContextMenu("같은 칸에 공 2개 연출")]
        public void PlayDoubleBallSamePocket()
        {
            if (!Application.isPlaying || verifying || presenter.IsPlaying)
            {
                return;
            }

            StartCoroutine(presenter.Play(SpinResult.FromNumbers(definition, 17, 17)));
        }

        private IEnumerator VerifyAllPockets()
        {
            verifying = true;
            float previousSpeed = presenter.SpeedMultiplier;
            presenter.SpeedMultiplier = verificationSpeed;

            int mismatches = 0;
            int restMisses = 0;
            float worstRestError = 0f;
            for (int i = 0; i < definition.PocketCount; i++)
            {
                int number = definition.GetPocket(i).Number;
                yield return presenter.Play(SpinResult.FromNumbers(definition, number));

                int landedIndex = presenter.FindNearestPocketIndex(presenter.GetBallPosition(0));
                if (landedIndex != i)
                {
                    mismatches++;
                    Debug.LogError($"[검증] 목표 {Describe(definition.GetPocket(i))}(칸 {i}) → 실제 {Describe(definition.GetPocket(landedIndex))}(칸 {landedIndex})");
                }

                float restError = Mathf.Abs(Mathf.DeltaAngle(presenter.PocketAngleOnTable(i), presenter.RestAngle));
                worstRestError = Mathf.Max(worstRestError, restError);
                if (restError > 0.5f)
                {
                    restMisses++;
                    Debug.LogError($"[검증] {Describe(definition.GetPocket(i))}(칸 {i})이 {presenter.PocketAngleOnTable(i):F1}°에서 멈춤(목표 {presenter.RestAngle}°)");
                }
            }

            presenter.SpeedMultiplier = previousSpeed;
            verifying = false;

            string frameRate = Application.targetFrameRate > 0 ? Application.targetFrameRate.ToString() : "제한 없음";
            Debug.Log($"[검증] {definition.PocketCount}칸 순회 완료. 안착 불일치 {mismatches}개, 정지 위치 어긋남 {restMisses}개(최대 오차 {worstRestError:F3}°). targetFrameRate {frameRate}, 배속 {verificationSpeed}");
        }
    }
}
