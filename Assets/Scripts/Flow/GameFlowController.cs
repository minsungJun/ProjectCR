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

        /// <summary>회전을 다 썼거나 베팅할 돈이 없다. 마지막 결과를 보여 준 뒤 납부를 판정한다.</summary>
        RoundPayment,

        /// <summary>납부에 성공했고 음료를 고르는 중. 베팅·회전·재납부를 막는다. 고르면 다음 라운드.</summary>
        DrinkSelection,

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
    /// → 라운드 종료 시 <see cref="SubmitPayment"/>(납부 1회) → <see cref="ConfirmDrink"/>(음료 1잔, 다음 라운드).
    /// 음료 목록이 없으면 납부 성공 즉시 다음 라운드로 넘어간다.
    ///
    /// 해골 회전은 모든 베팅 낙첨에 더해 남은 보유 금액이 절반이 된다(<see cref="GameBalance.SkullKeptBalance"/>).
    /// <b>해골은 상태를 바꾸지 않는다.</b> 해골 회전은 지급 0으로 정산되는 정상 회전 종료이고,
    /// 회전 수 1회를 쓰고 일반 회전과 같은 규칙으로 Betting 또는 RoundPayment로 간다.
    /// GameOver는 납부 실패에서만 생긴다. 음료는 해골 판정을 바꾸지 않는다(해골 회전은 음료와 상관없이 지급 0).
    ///
    /// 진행 상태(돈, 라운드, 음료 잔 수)는 전부 이 객체에 있다. 새 게임은 새 객체를 만드는 것으로 초기화한다.
    /// </summary>
    public sealed class GameFlowController
    {
        // 보상 추첨 난수를 결과 추첨 난수와 떼어 놓는 값. 같은 시드면 보상도 같다.
        private const int RewardSeedSalt = 0x5EED5EED;

        private readonly Random rng;
        private int lastSpinId;

        public GameFlowController(RouletteDefinition definition, GameBalance gameBalance, int seed, DrinkCatalog drinkCatalog = null)
        {
            Definition = definition;
            GameBalance = gameBalance;
            Seed = seed != 0 ? seed : NewSeed();
            rng = new Random(Seed);

            Wallet = new Wallet(gameBalance.InitialMoney, gameBalance.MaxMoney);
            Bet = new BetManager(definition, gameBalance.MinBet);
            Round = new RoundManager(gameBalance);
            Drinks = new DrinkRunState(drinkCatalog);
            Probabilities = new ProbabilityDrinkRunState();
            Rewards = new DrinkRewardController(drinkCatalog, Seed ^ RewardSeedSalt);
            Phase = GamePhase.Betting;
        }

        public RouletteDefinition Definition { get; }
        public GameBalance GameBalance { get; }

        /// <summary>실제로 쓴 시드. 로그에 남겨 같은 진행을 재현한다.</summary>
        public int Seed { get; }

        public Wallet Wallet { get; }
        public BetManager Bet { get; }
        public RoundManager Round { get; }

        /// <summary>이번 게임에서 마신 지급 강화 음료(묶음별 잔 수).</summary>
        public DrinkRunState Drinks { get; }

        /// <summary>이번 게임에서 마신 확률 강화 음료(대상별 잔 수).</summary>
        public ProbabilityDrinkRunState Probabilities { get; }

        /// <summary>라운드 통과 보상(음료 후보).</summary>
        public DrinkRewardController Rewards { get; }

        public GamePhase Phase { get; private set; }

        public ActiveSpin CurrentSpin { get; private set; }
        public SettlementResult LastSettlement { get; private set; }

        /// <summary>정산까지 끝난 회전 수(게임 전체).</summary>
        public int SpinsPlayed { get; private set; }

        /// <summary>지급 0으로 끝난 회전이 연속 몇 번인가. 해골 낙첨도 센다. 지급이 하나라도 있으면 0.</summary>
        public int ConsecutiveLosses { get; private set; }
        public PaymentOutcome? LastPayment { get; private set; }

        /// <summary>마지막으로 낸 납부금. 보상 화면 머리글용.</summary>
        public long LastPaidDue { get; private set; }

        /// <summary>마지막으로 마신 음료. 없으면 null.</summary>
        public DrinkOffer LastDrink { get; private set; }

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

            // 해골 벌칙: 베팅금을 뺀 뒤 남은 보유 금액을 절반으로(소수점 반올림). 공이 여러 개 해골이어도 회전당 한 번.
            if (settlement.IsSkullBust)
            {
                long penalty = Wallet.Balance - GameBalance.SkullKeptBalance(Wallet.Balance);
                Wallet.TrySpend(penalty);
                settlement.ApplySkullPenalty(penalty);
            }

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

        /// <summary>
        /// 납부를 판정한다. RoundPayment 상태에서만 동작하고, 부르는 즉시 상태가 바뀌어 두 번째 호출은 null(이중 차감 없음).
        /// 성공하면 납부금을 한 번 빼고 음료 선택(목록이 없으면 바로 다음 라운드), 실패하면 아무것도 빼지 않고 GameOver.
        /// </summary>
        public PaymentOutcome? SubmitPayment()
        {
            if (Phase != GamePhase.RoundPayment)
            {
                return null;
            }

            long due = Round.CurrentDue;
            PaymentOutcome outcome = Round.Pay(Wallet);
            LastPayment = outcome;

            if (outcome == PaymentOutcome.Insufficient)
            {
                Phase = GamePhase.GameOver;
                return outcome;
            }

            LastPaidDue = due;
            if (Rewards.HasCatalog && Rewards.CreateOffer().Count > 0)
            {
                Phase = GamePhase.DrinkSelection;
            }
            else
            {
                StartNextRound();
            }

            return outcome;
        }

        /// <summary>
        /// 음료 후보 중 하나를 마신다. DrinkSelection 상태에서만 동작하고, 성공하면 한 잔을 더한 뒤 다음 라운드로 넘어간다.
        /// 지급 강화는 묶음 잔 수 +1, 확률 강화는 후보에 정해진 대상의 잔 수 +1.
        /// 상태가 맞지 않거나 번호가 틀리면 null이고 아무것도 바뀌지 않는다.
        /// </summary>
        public DrinkOffer ConfirmDrink(int offerIndex)
        {
            if (Phase != GamePhase.DrinkSelection)
            {
                return null;
            }

            DrinkOffer drink = Rewards.Take(offerIndex);
            if (drink == null)
            {
                return null;
            }

            if (drink.IsProbability)
            {
                Probabilities.AddStack(drink.Target, drink.Boost);
            }
            else
            {
                Drinks.AddStack(drink.Group);
            }

            LastDrink = drink;
            StartNextRound();
            return drink;
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

            // 음료: 묶음 배율 = 한 잔 배율^잔 수. 기본 배수에 곱하는 건 SpinConfig.MultiplierFor.
            for (int i = 0; i < DrinkGroups.Count; i++)
            {
                var group = (DrinkGroup)i;
                config.SetGroupBoost(group, Drinks.MultiplierOf(group));
            }

            // 확률 음료: 일반 칸 가중치만 바꾼다. 해골 확률(SkullChance)은 그대로 1/38.
            // 베팅과 상관없이 지금 효과를 그대로 쓴다(건 칸을 따로 유리하게 하지 않는다).
            if (Probabilities.Effects.Count > 0)
            {
                config.SetWeights(PocketWeightCalculator.Compute(Definition, Probabilities.Effects));
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

        /// <summary>
        /// 납부(와 음료)를 마친 뒤. 라운드 번호 +1, 회전 수를 다시 채우고 판을 비운다. 돈과 음료는 그대로.
        /// 새 라운드에서도 최소 베팅을 못 내면 기존 규칙대로 남은 회전을 버리고 납부 판정으로 간다.
        /// </summary>
        private void StartNextRound()
        {
            Round.AdvanceRound();
            Bet.Unlock(Wallet.Balance);
            Bet.ClearBets();
            Phase = NextPhaseAfterSpin();
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
