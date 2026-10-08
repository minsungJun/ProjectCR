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

        /// <summary>상점 이용 중(베팅 중에만 들어온다). 베팅·회전을 막고, 닫으면 놓아 둔 베팅 그대로 Betting으로 돌아간다.</summary>
        Shopping,

        GameOver,
    }

    /// <summary>진행 중인 회전 하나. 연출이 끝나면 Id로 정산을 요청한다.</summary>
    public sealed class ActiveSpin
    {
        public ActiveSpin(int id, IReadOnlyList<ConfirmedBet> bets, SpinConfig config, SpinResult result,
            bool insured = false, decimal insuranceRatio = 0m, int shieldsUsed = 0)
        {
            Id = id;
            Bets = bets;
            Config = config;
            Result = result;
            Insured = insured;
            InsuranceRatio = insuranceRatio;
            ShieldsUsed = shieldsUsed;
        }

        /// <summary>이 회전에 보험이 걸렸다(시작 때 확정).</summary>
        public bool Insured { get; }

        public decimal InsuranceRatio { get; }

        /// <summary>시작 때 해골을 막느라 쓴 방패 수(결과는 이미 다시 뽑힌 뒤다).</summary>
        public int ShieldsUsed { get; }

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

        // 상점 진열 난수용. 결과·보상 난수와 떨어뜨린다.
        private const int ShopSeedSalt = 0x5A0B5A0B;

        private readonly Random rng;
        private int lastSpinId;

        public GameFlowController(RouletteDefinition definition, GameBalance gameBalance, int seed, DrinkCatalog drinkCatalog = null,
            ShopCatalog shopCatalog = null)
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
            Inventory = new InventoryState();
            Upgrades = new BettingUpgradeState();
            Cross = new CrossBetEffectState();
            Shop = new ShopController(this, shopCatalog, Seed ^ ShopSeedSalt);
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

        /// <summary>상점(진열·재고·구매).</summary>
        public ShopController Shop { get; }

        /// <summary>소모품 보유·장착·이번 라운드 효과.</summary>
        public InventoryState Inventory { get; }

        /// <summary>상점 집중 강화(음료와 따로 저장).</summary>
        public BettingUpgradeState Upgrades { get; }

        /// <summary>상점 교차 강화(보유 + 다음 회전 강화).</summary>
        public CrossBetEffectState Cross { get; }

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

        /// <summary>마지막 납부 때 납부 지원금으로 돌려받은 돈. 없으면 0.</summary>
        public long LastPaymentRefund { get; private set; }

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

            // 이 회전 설정을 확정한다(공 수, 지급 배율, 가중치, 다음 회전 교차 강화). 이후 상점·아이템은 이 회전에 영향이 없다.
            SpinConfig config = BuildSpinConfig();
            Cross.TakePendingForSpin();
            bool insured = Inventory.TakeInsuranceForSpin();

            SpinResult result = SpinResolver.Resolve(config, rng);
            result = ApplyResultOverrides(result);

            // 해골 방패(자동): 가진 방패로 해골 공만 같은 설정으로 다시 뽑는다.
            result = SpinResolver.ShieldSkulls(result, config, rng, Inventory.Shields, out int shieldsUsed);
            Inventory.ConsumeShields(shieldsUsed);

            spin = new ActiveSpin(++lastSpinId, bets, config, result, insured, Inventory.InsuranceRatio, shieldsUsed);
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

            settlement.ApplyShieldsUsed(spin.ShieldsUsed);

            // 보험: 해골이 아닌 일반 낙첨(지급 0)이면 베팅금 일부 환급. 해골 낙첨·당첨에는 없다.
            if (spin.Insured && settlement.Outcome == SpinOutcome.Lose)
            {
                long refund = (long)Math.Floor(settlement.TotalStake * spin.InsuranceRatio);
                settlement.ApplyInsuranceRefund(Wallet.Add(refund));
            }

            // 교차 강화: 발동 묶음 베팅이 당첨됐으면 다음 회전 강화(해골 낙첨이면 없음).
            settlement.ApplyCrossTriggers(Cross.OnSettled(settlement));

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

            // 납부 지원금(지속 효과): 방금 낸 납부금의 일부를 돌려받는다. 납부 판정은 이미 끝났으니 부족분을 메우지는 못한다.
            LastPaymentRefund = Wallet.Add((long)Math.Floor(due * Upgrades.PaymentRefund));
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

        // ---------- 상점 ----------

        /// <summary>
        /// 상점을 열 수 있는 때: 라운드가 끝나고(음료 선택까지 마치고) 새 라운드의 <b>첫 회전을 시작하기 전</b>, 베팅 중일 때만.
        /// 1라운드는 끝난 라운드가 없어서 닫혀 있다(ShopCatalog.openOnFirstRound로 열 수 있다).
        /// </summary>
        public bool CanEnterShop =>
            Phase == GamePhase.Betting && Shop.HasCatalog && Round.SpinsTaken == 0
            && (Round.Round > 1 || Shop.State.OpenOnFirstRound);

        /// <summary>
        /// 상점에 들어간다(<see cref="CanEnterShop"/>일 때만). 놓아 둔 베팅은 그대로 두고, 회전·라운드는 진행되지 않는다.
        /// 이번 라운드 진열이 없으면 만든다(다시 들어와도 같은 진열·재고).
        /// </summary>
        public bool EnterShop()
        {
            if (!CanEnterShop)
            {
                return false;
            }

            Shop.State.EnsureDisplay(Round.Round);
            Phase = GamePhase.Shopping;
            return true;
        }

        /// <summary>
        /// 상점을 닫는다. 베팅 상태로 돌아가되, 상점에서 돈을 다 써서 최소 베팅도 못 하고 놓은 칩도 없으면
        /// 기존 규칙대로 남은 회전을 버리고 납부 판정(RoundPayment)으로 간다.
        /// </summary>
        public bool CloseShop()
        {
            if (Phase != GamePhase.Shopping)
            {
                return false;
            }

            if (Bet.HasBets || CanAffordMinimumBet)
            {
                Phase = GamePhase.Betting;
            }
            else
            {
                Round.ForfeitRemainingSpins();
                Phase = GamePhase.RoundPayment;
            }

            return true;
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

            // 추가 공 아이템(이번 라운드). 최대 공 수를 넘지 않는다(디버그로 더한 공은 그대로 둔다).
            int withItems = config.BallCount + Inventory.RoundExtraBalls;
            config.BallCount = Math.Min(withItems, Math.Max(config.BallCount, GameBalance.MaxBallCount));
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

            // 상점 강화: (1 + 집중 증가 + 다음 회전 교차 증가). 음료 배율 뒤에 곱한다(SpinConfig.MultiplierFor).
            for (int i = 0; i < DrinkGroups.Count; i++)
            {
                var group = (DrinkGroup)i;
                config.SetGroupBonus(group, Upgrades.PayoutIncrease(group) + Cross.Pending(group));
            }

            // 확률(음료 + 상점 집중): 같은 칸이면 증가량을 더한다. 일반 칸 가중치만 바꾸고 해골(SkullChance)은 그대로 1/38.
            // 베팅과 상관없이 지금 효과를 그대로 쓴다(건 칸을 따로 유리하게 하지 않는다).
            if (Probabilities.Effects.Count > 0 || Upgrades.Probabilities.Effects.Count > 0)
            {
                var effects = new List<ProbabilityEffect>(Probabilities.Effects);
                effects.AddRange(Upgrades.Probabilities.Effects);
                config.SetWeights(PocketWeightCalculator.Compute(Definition, effects));
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
            // 추가 공 효과 끝, 쓰지 않은 납부 지원금 만료. 다음 회전 교차 강화는 남긴다(다음 라운드 첫 회전).
            Inventory.OnRoundEnded();
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
