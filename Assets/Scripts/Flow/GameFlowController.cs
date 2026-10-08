using System;
using System.Collections.Generic;

namespace ProjectCR
{
    public enum GamePhase
    {
        /// <summary>번호와 금액을 고르는 중. 회전을 시작할 수 있는 유일한 상태.</summary>
        Betting,

        /// <summary>베팅이 확정·차감되었고 연출이 도는 중. 베팅 변경과 추가 회전을 막는다.</summary>
        Spinning,

        /// <summary>회전을 다 썼거나 베팅할 돈이 없다. 납부를 기다린다.</summary>
        RoundPayment,

        GameOver,
    }

    /// <summary>진행 중인 회전 하나. 연출이 끝나면 Id로 정산을 요청한다.</summary>
    public sealed class ActiveSpin
    {
        public ActiveSpin(int id, IReadOnlyList<ConfirmedBet> bets, SpinConfig config, SpinResult result)
        {
            Id = id;
            Bets = bets;
            Config = config;
            Result = result;
        }

        public int Id { get; }

        /// <summary>이 회전에 확정된 베팅 전체. 합계는 시작 때 한 번 차감됐다.</summary>
        public IReadOnlyList<ConfirmedBet> Bets { get; }

        public SpinConfig Config { get; }
        public SpinResult Result { get; }
    }

    /// <summary>
    /// 베팅 → 회전 → 정산 → (납부) 상태 전환을 맡는다. MonoBehaviour가 아니라서 EditMode 테스트로 돌린다.
    /// 씬 쪽 연결(연출 대기, UI 갱신)은 RouletteGame이 한다.
    ///
    /// <b>중복 방지는 UI가 아니라 여기서 한다.</b>
    /// 회전은 Betting 상태에서만 시작되고 시작과 동시에 Spinning으로 바뀐다.
    /// 정산은 진행 중인 회전의 Id와 맞을 때 한 번만 실행된다.
    ///
    /// 한 회전의 순서(아이템이 끼어들 자리):
    /// 베팅 검증 → 확정·합계 1회 차감 → <see cref="BuildSpinConfig"/>(회전 전) → 추첨 → <see cref="ApplyResultOverrides"/>(결과 확정 후)
    /// → 모든 공의 연출 완료 → <see cref="CompleteSpin"/>(전체 결과 평가 → 정산 → 기록) → 다음 진행
    /// → 라운드 종료 시 <see cref="SubmitPayment"/>.
    ///
    /// <b>해골은 상태를 바꾸지 않는다.</b> 해골 회전은 지급 0으로 정산되는 정상 회전 종료이고,
    /// 회전 수 1회를 쓰고 일반 회전과 같은 규칙으로 Betting 또는 RoundPayment로 간다.
    /// GameOver는 납부 실패에서만 생긴다.
    /// </summary>
    public sealed class GameFlowController
    {
        private readonly Random rng;
        private int lastSpinId;

        public GameFlowController(RouletteDefinition definition, GameBalance gameBalance, int seed)
        {
            Definition = definition;
            GameBalance = gameBalance;
            Seed = seed != 0 ? seed : NewSeed();
            rng = new Random(Seed);

            Wallet = new Wallet(gameBalance.InitialMoney, gameBalance.MaxMoney);
            Bet = new BetManager(definition, gameBalance.MinBet);
            Round = new RoundManager(gameBalance);
            Phase = GamePhase.Betting;
        }

        public RouletteDefinition Definition { get; }
        public GameBalance GameBalance { get; }

        /// <summary>실제로 쓴 시드. 로그에 남겨 같은 진행을 재현한다.</summary>
        public int Seed { get; }

        public Wallet Wallet { get; }
        public BetManager Bet { get; }
        public RoundManager Round { get; }
        public GamePhase Phase { get; private set; }

        public ActiveSpin CurrentSpin { get; private set; }
        public SettlementResult LastSettlement { get; private set; }

        /// <summary>정산까지 끝난 회전 수(게임 전체).</summary>
        public int SpinsPlayed { get; private set; }

        /// <summary>지급 0으로 끝난 회전이 연속 몇 번인가. 해골 낙첨도 센다. 지급이 하나라도 있으면 0.</summary>
        public int ConsecutiveLosses { get; private set; }
        public PaymentOutcome? LastPayment { get; private set; }

        public bool CanAffordMinimumBet => Wallet.Balance >= GameBalance.MinBet;

        // ---------- 디버그·검증용 (아이템 시스템 전까지 확장성 확인에 쓴다) ----------

        /// <summary>비어 있지 않으면 공 i의 결과를 ForcedNumbers[i]로 덮어쓴다. 음수는 무시.</summary>
        public IReadOnlyList<int> ForcedNumbers { get; set; }

        /// <summary>회전마다 GameBalance.BallCount에 더할 공 수.</summary>
        public int ExtraBallCount { get; set; }

        /// <summary>0보다 크면 숫자 하나 베팅의 지급 배수를 이 값으로 바꾼다.</summary>
        public int StraightPayoutOverride { get; set; }

        // ---------- 베팅 (Betting 상태에서만) ----------

        /// <summary>대상에 칩 금액만큼 건다. 같은 대상이면 쌓인다.</summary>
        public BetError PlaceChip(BetTarget target) => WhenBetting(() => Bet.PlaceChip(target, Wallet));

        /// <summary>대상에 amount를 건다. 같은 대상이면 쌓인다.</summary>
        public BetError PlaceBet(BetTarget target, long amount) => WhenBetting(() => Bet.PlaceBet(target, amount, Wallet));

        public BetError RemoveBet(BetTarget target) => WhenBetting(() => Bet.RemoveBet(target));

        public BetError UndoLastChip() => WhenBetting(Bet.UndoLastChip);

        public BetError ClearBets() => WhenBetting(Bet.ClearBets);

        public BetError SetChipValue(long value) => WhenBetting(() => Bet.SetChipValue(value));

        public BetError ValidateBet()
        {
            return Phase == GamePhase.Betting ? Bet.Validate(Wallet) : BetError.NotInBettingPhase;
        }

        // ---------- 회전 ----------

        /// <summary>
        /// 베팅을 확정·차감하고 결과를 추첨한다. 성공하면 Spinning으로 바뀌고,
        /// 그동안 다시 불러도 NotInBettingPhase로 거절된다.
        /// </summary>
        public BetError TryBeginSpin(out ActiveSpin spin)
        {
            spin = null;

            if (Phase != GamePhase.Betting || !Round.HasSpins)
            {
                return BetError.NotInBettingPhase;
            }

            BetError error = Bet.TryConfirm(Wallet, out IReadOnlyList<ConfirmedBet> bets);
            if (error != BetError.None)
            {
                return error;
            }

            Round.TryConsumeSpin();

            SpinConfig config = BuildSpinConfig();
            SpinResult result = SpinResolver.Resolve(config, rng);
            result = ApplyResultOverrides(result);

            spin = new ActiveSpin(++lastSpinId, bets, config, result);
            CurrentSpin = spin;
            Phase = GamePhase.Spinning;
            return BetError.None;
        }

        /// <summary>
        /// 모든 공의 연출이 끝난 뒤 한 번 부른다. 진행 중인 회전이 아니거나 이미 정산했으면 false.
        /// </summary>
        public bool CompleteSpin(int spinId, out SettlementResult settlement)
        {
            settlement = null;

            if (Phase != GamePhase.Spinning || CurrentSpin == null || CurrentSpin.Id != spinId)
            {
                return false;
            }

            // 전체 결과 평가 → 정산. 해골이면 지급 0이고 이미 뺀 베팅금은 돌려주지 않는다.
            ActiveSpin spin = CurrentSpin;
            settlement = PayoutCalculator.Calculate(spin.Bets, spin.Result, spin.Config, Wallet.MaxMoney);
            Wallet.Add(settlement.TotalPayout);

            LastSettlement = settlement;
            SpinsPlayed++;
            ConsecutiveLosses = settlement.Outcome == SpinOutcome.Win ? 0 : ConsecutiveLosses + 1;
            CurrentSpin = null;
            Bet.Unlock(Wallet.Balance);

            // 회전이 끝나면 판의 칩을 없앤다. 베팅금은 시작 때 빠졌고 당첨금은 원금 포함으로 들어왔으니 돈은 그대로다.
            Bet.ClearBets();

            Phase = NextPhaseAfterSpin();
            return true;
        }

        // ---------- 라운드 ----------

        /// <summary>RoundPayment 상태에서만 동작한다. 성공하면 다음 라운드, 실패하면 GameOver.</summary>
        public PaymentOutcome? SubmitPayment()
        {
            if (Phase != GamePhase.RoundPayment)
            {
                return null;
            }

            PaymentOutcome outcome = Round.SubmitPayment(Wallet);
            LastPayment = outcome;

            if (outcome == PaymentOutcome.Insufficient)
            {
                Phase = GamePhase.GameOver;
                return outcome;
            }

            Bet.Unlock(Wallet.Balance);
            Phase = NextPhaseAfterSpin();
            return outcome;
        }

        // ---------- 아이템이 끼어들 자리 ----------

        /// <summary>
        /// 회전 전. 정의 에셋을 복사한 설정에 공 수·가중치·배율 보정을 건다.
        /// UI 미리보기도 이걸 불러서 화면의 배율과 실제 계산이 같은 값을 쓰게 한다.
        /// </summary>
        public SpinConfig BuildSpinConfig()
        {
            SpinConfig config = SpinConfig.Create(Definition, GameBalance);
            config.BallCount += ExtraBallCount;
            if (StraightPayoutOverride > 0)
            {
                config.StraightPayout = StraightPayoutOverride;
            }

            return config;
        }

        /// <summary>결과 확정 후. 재추첨·결과 변환이 들어올 자리. 지금은 디버그 지정 번호만 처리한다.</summary>
        private SpinResult ApplyResultOverrides(SpinResult result)
        {
            return SpinResolver.OverrideNumbers(result, Definition, ForcedNumbers);
        }

        /// <summary>
        /// 회전이 남았고 최소 베팅을 낼 수 있으면 다시 Betting.
        /// 회전이 남았어도 베팅할 돈이 없으면 남은 회전을 버리고 납부 판정으로 간다.
        /// 납부금 할인 같은 효과가 생길 수 있어 바로 GameOver로 보내지 않는다.
        /// </summary>
        private GamePhase NextPhaseAfterSpin()
        {
            if (Round.HasSpins && CanAffordMinimumBet)
            {
                return GamePhase.Betting;
            }

            Round.ForfeitRemainingSpins();
            return GamePhase.RoundPayment;
        }

        private BetError WhenBetting(Func<BetError> action)
        {
            return Phase == GamePhase.Betting ? action() : BetError.NotInBettingPhase;
        }

        private static int NewSeed()
        {
            int seed = Environment.TickCount ^ Guid.NewGuid().GetHashCode();
            return seed != 0 ? seed : 1;
        }
    }
}
