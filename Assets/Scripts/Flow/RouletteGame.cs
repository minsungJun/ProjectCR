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

        [Header("연결")]
        [SerializeField] private RoulettePresenter presenter;
        [SerializeField] private RouletteHud hud;

        [Tooltip("기울어진 연출 시점.")]
        [SerializeField] private Camera presentationCamera;

        [Tooltip("위에서 내려다보는 정사영 시점. 번호 일치를 눈으로 확인할 때 쓴다.")]
        [SerializeField] private Camera topDownCamera;

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

        [Tooltip("연출 배속.")]
        [Min(0.1f)]
        [SerializeField] private float presentationSpeed = 1f;

        [Tooltip("37칸 순회 검증 때의 배속.")]
        [Min(0.1f)]
        [SerializeField] private float verificationSpeed = 8f;

        private GameFlowController flow;
        private bool verifying;
        private bool topDownView;

        /// <summary>검증 스크립트가 상태를 읽을 때 쓴다.</summary>
        public GameFlowController Flow => flow;

        public bool IsVerifying => verifying;

        private void Start()
        {
            hud.Initialize(definition);
            hud.TargetClicked += OnTargetClicked;
            hud.TargetRemoveRequested += OnTargetRemoveRequested;
            hud.UndoClicked += OnUndoClicked;
            hud.ClearClicked += OnClearClicked;
            hud.ChipAdjusted += OnChipAdjusted;
            hud.ChipHalved += OnChipHalved;
            hud.ChipDoubled += OnChipDoubled;
            hud.ChipMaxed += OnChipMaxed;
            hud.SpinClicked += OnSpinClicked;
            hud.PayClicked += OnPayClicked;
            hud.RestartClicked += StartNewRun;
            hud.ViewToggled += ToggleView;

            ApplyView();
            StartNewRun();
        }

        private void OnValidate()
        {
            if (Application.isPlaying && flow != null)
            {
                ApplyDebugOptions();
                hud.Refresh(flow);
            }
        }

        public void StartNewRun()
        {
            StopAllCoroutines();
            verifying = false;
            presenter.ResetView();

            flow = new GameFlowController(definition, balance, randomSeed);
            ApplyDebugOptions();
            flow.SetChipValue(Math.Max(balance.MinBet, Math.Min(balance.MinBet * 10, flow.Wallet.Balance)));

            Debug.Log($"[RouletteGame] 새 게임. 시드 {flow.Seed}");

            hud.ClearHistory();
            hud.ShowMessage("칩 금액을 정하고 번호판에 칩을 놓은 뒤 회전을 누르세요.");
            hud.Refresh(flow);
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

        private void OnTargetClicked(BetTarget target) => ApplyBetAction(flow.PlaceChip(target));

        private void OnTargetRemoveRequested(BetTarget target) => ApplyBetAction(flow.RemoveBet(target));

        private void OnUndoClicked() => ApplyBetAction(flow.UndoLastChip());

        private void OnClearClicked() => ApplyBetAction(flow.ClearBets());

        private void ApplyBetAction(BetError error)
        {
            hud.Refresh(flow);
            if (error != BetError.None)
            {
                hud.ShowBetError(error);
            }
        }

        private void OnChipAdjusted(long delta) => SetChipClamped(flow.Bet.ChipValue + delta);

        private void OnChipHalved() => SetChipClamped(flow.Bet.ChipValue / 2);

        private void OnChipDoubled() => SetChipClamped(flow.Bet.ChipValue * 2);

        /// <summary>남은 돈(보유 − 이미 놓은 칩) 전부를 칩 하나로.</summary>
        private void OnChipMaxed() => SetChipClamped(flow.Wallet.Balance - flow.Bet.TotalStake);

        private void SetChipClamped(long value)
        {
            long max = Math.Max(balance.MinBet, flow.Wallet.Balance);
            flow.SetChipValue(Math.Max(balance.MinBet, Math.Min(value, max)));
            hud.Refresh(flow);
        }

        private void OnSpinClicked()
        {
            if (verifying)
            {
                return;
            }

            ApplyDebugOptions();

            BetError error = flow.TryBeginSpin(out ActiveSpin spin);
            if (error != BetError.None)
            {
                hud.ShowBetError(error);
                return;
            }

            Debug.Log($"[RouletteGame] 회전 #{spin.Id}: {Describe(spin.Bets)} → 결과 {Describe(spin.Result)}");

            hud.ShowSpinning();
            hud.Refresh(flow);
            StartCoroutine(RunSpin(spin));
        }

        private IEnumerator RunSpin(ActiveSpin spin)
        {
            yield return presenter.Play(spin.Result);

            if (flow.CompleteSpin(spin.Id, out SettlementResult settlement))
            {
                Debug.Log($"[RouletteGame] 회전 #{spin.Id} 정산: {settlement.Outcome}, 지급 {settlement.TotalPayout:N0}, 연속 낙첨 {flow.ConsecutiveLosses}");
                hud.ShowSettlement(settlement);
            }

            hud.Refresh(flow);
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

        private void OnPayClicked()
        {
            PaymentOutcome? outcome = flow.SubmitPayment();
            if (outcome == PaymentOutcome.Paid)
            {
                hud.ShowMessage($"납부 완료! 라운드 {flow.Round.Round} 시작 · 이월 {flow.Wallet.Balance:N0}");
            }

            hud.Refresh(flow);
        }

        private void ToggleView()
        {
            topDownView = !topDownView;
            ApplyView();
        }

        private void ApplyView()
        {
            if (presentationCamera != null)
            {
                presentationCamera.enabled = !topDownView;
            }

            if (topDownCamera != null)
            {
                topDownCamera.enabled = topDownView;
            }
        }

        // ---------- 검증 ----------

        /// <summary>
        /// 모든 칸(해골 포함)을 휠 순서대로 하나씩 강제로 굴려, 공에 가장 가까운 앵커가 목표 칸인지 확인한다.
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
            }

            presenter.SpeedMultiplier = previousSpeed;
            verifying = false;

            string frameRate = Application.targetFrameRate > 0 ? Application.targetFrameRate.ToString() : "제한 없음";
            Debug.Log($"[검증] {definition.PocketCount}칸 순회 완료. 불일치 {mismatches}개. targetFrameRate {frameRate}, 배속 {verificationSpeed}");
        }
    }
}
