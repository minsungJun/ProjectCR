using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectCR
{
    /// <summary>
    /// 서비스(상점) 창. HUD와 같은 UIDocument의 shop-panel을 쓴다.
    /// - 탭은 [소모품] / [강화] 두 개다. 소모품 탭에는 소모품만, 강화 탭에는 지속 효과(집중·교차·납부 지원)만 나온다.
    /// - 카드 모양: 큰 아이콘, 이름, 금색 짧은 선, 두 줄 설명, 구분선, 코인 + 가격, 전체 폭 [구매] 버튼.
    /// - 아이콘: 상품 아이콘은 ShopItemDefinition.icon, 돈 아이콘은 ShopCatalog.moneyIcon에 넣는다. 비어 있으면 색 원으로 대신 그린다.
    /// 구매 검증과 돈 처리는 GameFlowController(ShopController)가 하고, 여기서는 그리기와 입력 전달만 한다.
    /// 창 전체에 blocks-input이 붙어 열려 있는 동안 3D 테이블 입력이 막힌다.
    /// 숫자 집중 대상은 판에서 고른다: 창을 잠시 접고(<see cref="SetPicking"/>) RouletteGame이 판 클릭을 대상으로 넘겨준다.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class ShopView : MonoBehaviour
    {
        private const string ActiveTabClass = "shop-tab--active";

        public event Action Closed;
        public event Action<int, BetTarget> BuyRequested;
        public event Action<int> PickTargetRequested;

        // 진열 칸별로 고른 집중 강화 대상. 라운드가 바뀌면(진열이 바뀌면) 비운다.
        private readonly Dictionary<int, BetTarget> chosenTargets = new Dictionary<int, BetTarget>();

        private VisualElement panel;
        private VisualElement coin;
        private Label money;
        private Label funds;
        private Label notice;
        private ScrollView scroll;
        private Button consumableTab;
        private Button upgradeTab;
        private bool showUpgrades;
        private int targetsRound;
        private GameFlowController lastFlow;

        public bool IsOpen { get; private set; }

        public void Initialize()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            panel = root.Q("shop-panel");
            coin = root.Q("shop-coin");
            money = root.Q<Label>("shop-money");
            funds = root.Q<Label>("shop-funds");
            notice = root.Q<Label>("shop-notice");
            scroll = root.Q<ScrollView>("shop-scroll");
            consumableTab = root.Q<Button>("shop-tab-consumable");
            upgradeTab = root.Q<Button>("shop-tab-upgrade");

            root.Q<Button>("shop-close").clicked += () => Closed?.Invoke();
            consumableTab.clicked += () => SwitchTab(false);
            upgradeTab.clicked += () => SwitchTab(true);
            Hide();
        }

        public void Show(GameFlowController flow)
        {
            IsOpen = true;
            panel.style.display = DisplayStyle.Flex;
            ShowNotice(string.Empty);
            Refresh(flow);
        }

        public void Hide()
        {
            IsOpen = false;
            if (panel != null)
            {
                panel.style.display = DisplayStyle.None;
            }
        }

        /// <summary>판에서 대상을 고르는 동안 창을 접는다(상점은 열린 상태 그대로).</summary>
        public void SetPicking(bool picking)
        {
            panel.style.display = picking || !IsOpen ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>판에서 고른 숫자 집중 대상.</summary>
        public void SetTarget(int slot, BetTarget target)
        {
            chosenTargets[slot] = target;
        }

        public void ShowNotice(string text)
        {
            if (notice == null)
            {
                return;
            }

            notice.text = text ?? string.Empty;
            notice.style.display = string.IsNullOrEmpty(notice.text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        public void Refresh(GameFlowController flow)
        {
            lastFlow = flow;
            if (!IsOpen)
            {
                return;
            }

            if (targetsRound != flow.Round.Round)
            {
                chosenTargets.Clear();
                targetsRound = flow.Round.Round;
            }

            Sprite moneyIcon = flow.Shop.State.Catalog != null ? flow.Shop.State.Catalog.MoneyIcon : null;
            ApplyCoin(coin, moneyIcon);
            money.text = $"{flow.Wallet.Balance:N0}";
            funds.text = (flow.Bet.HasBets ? $"판의 칩 {flow.Bet.TotalStake:N0} 제외 · 구매 가능 {flow.Shop.AvailableFunds:N0} · " : string.Empty) +
                         $"라운드 {flow.Round.Round} · 납부금 {flow.Round.CurrentDue:N0}";

            consumableTab.EnableInClassList(ActiveTabClass, !showUpgrades);
            upgradeTab.EnableInClassList(ActiveTabClass, showUpgrades);

            scroll.Clear();
            BuildTab(flow, moneyIcon);
        }

        private void SwitchTab(bool upgrades)
        {
            showUpgrades = upgrades;
            ShowNotice(string.Empty);
            if (lastFlow != null)
            {
                Refresh(lastFlow);
            }
        }

        // ---------- 탭 내용 ----------

        private void BuildTab(GameFlowController flow, Sprite moneyIcon)
        {
            IReadOnlyList<ShopOffer> offers = flow.Shop.Offers;
            var cards = new VisualElement();
            cards.AddToClassList("shop-cards");

            // 강화 탭 = 소모품이 아닌 진열(분류를 섞어 뽑은 순서 그대로), 소모품 탭 = 소모품.
            for (int i = 0; i < offers.Count; i++)
            {
                bool consumable = offers[i].Item.Category == ShopCategory.Consumable;
                if (consumable != showUpgrades)
                {
                    cards.Add(BuildCard(flow, i, offers[i], moneyIcon));
                }
            }

            if (cards.childCount == 0)
            {
                scroll.Add(MakeLabel(showUpgrades ? "이번 라운드에 진열된 강화가 없습니다." : "진열된 소모품이 없습니다.", "shop-empty"));
            }
            else
            {
                scroll.Add(cards);
            }

            if (showUpgrades)
            {
                scroll.Add(MakeLabel("보유 강화", "shop-section"));
                scroll.Add(MakeLabel(UpgradeSummary(flow), "shop-summary"));
            }
        }

        private VisualElement BuildCard(GameFlowController flow, int slot, ShopOffer offer, Sprite moneyIcon)
        {
            ShopItemDefinition item = offer.Item;
            chosenTargets.TryGetValue(slot, out BetTarget target);
            ShopError check = flow.Shop.Check(slot, target);
            long price = flow.Shop.PriceOf(offer);
            bool done = item.IsPermanent && offer.BoughtThisRound > 0;

            var card = new VisualElement();
            card.AddToClassList("shop-card");
            card.EnableInClassList("shop-card--done", done || check == ShopError.SoldOut || check == ShopError.LimitReached);

            // 구분선 위: 아이콘, 이름, 짧은 선, 설명, 상태, (집중이면) 대상 고르기.
            var body = new VisualElement();
            body.AddToClassList("shop-card__body");
            body.Add(BuildIcon(item));
            body.Add(MakeLabel(item.DisplayName, "shop-name"));
            body.Add(MakeElement("shop-dash"));
            body.Add(MakeLabel(item.CardText, "shop-desc"));
            body.Add(MakeLabel(StateText(flow, offer), "shop-state"));
            if (item.NeedsTarget)
            {
                body.Add(BuildTargetPicker(flow, slot, item, target));
            }

            card.Add(body);

            // 구분선 아래: 코인 + 가격, 구매 버튼. 구분선이 margin-top: auto라 한 줄의 카드끼리 아래쪽이 맞는다.
            card.Add(MakeElement("shop-sep"));
            var priceRow = new VisualElement();
            priceRow.AddToClassList("shop-price-row");
            var priceCoin = MakeElement("shop-coin");
            ApplyCoin(priceCoin, moneyIcon);
            priceRow.Add(priceCoin);
            priceRow.Add(MakeLabel($"{price:N0}", "shop-price"));
            card.Add(priceRow);

            var buy = new Button(() => BuyRequested?.Invoke(slot, target))
            {
                text = done ? "구매 완료" : ButtonText(check),
            };
            buy.AddToClassList("shop-buy");
            buy.SetEnabled(check == ShopError.None);
            card.Add(buy);

            if (check == ShopError.None && flow.Wallet.Balance - price < flow.Round.CurrentDue)
            {
                card.Add(MakeLabel("사면 이번 납부금이 부족해집니다", "shop-reason"));
            }

            return card;
        }

        /// <summary>구매 버튼 글자. 살 수 없으면 이유를 짧게 쓴다.</summary>
        private static string ButtonText(ShopError check)
        {
            switch (check)
            {
                case ShopError.None:
                    return "구매";
                case ShopError.SoldOut:
                    return "품절";
                case ShopError.LimitReached:
                    return "구매 완료";
                case ShopError.NeedTarget:
                    return "대상을 고르세요";
                case ShopError.InvalidTarget:
                    return "고를 수 없는 대상";
                case ShopError.InsufficientFunds:
                    return "잔액 부족";
                case ShopError.MaxBalls:
                    return "공 최대";
                default:
                    return "구매 불가";
            }
        }

        /// <summary>집중 강화 대상 고르기. 숫자는 판에서 고른다. 고른 대상의 등장 확률(전 → 후)을 보여 준다.</summary>
        private VisualElement BuildTargetPicker(GameFlowController flow, int slot, ShopItemDefinition item, BetTarget chosen)
        {
            var box = new VisualElement();
            var chips = new VisualElement();
            chips.AddToClassList("shop-targets");

            if (item.Group == DrinkGroup.Number)
            {
                var pick = new Button(() => PickTargetRequested?.Invoke(slot))
                {
                    text = chosen.IsNone ? "판에서 고르기" : $"{DrinkOffer.TargetLabel(chosen)} · 다시 고르기",
                };
                pick.AddToClassList("shop-target");
                pick.EnableInClassList("shop-target--selected", !chosen.IsNone);
                chips.Add(pick);
            }
            else
            {
                foreach (BetTarget option in TargetOptions(item.Group))
                {
                    BetTarget value = option;
                    var chip = new Button(() =>
                    {
                        chosenTargets[slot] = value;
                        Refresh(flow);
                    })
                    {
                        text = DrinkOffer.TargetLabel(option),
                    };
                    chip.AddToClassList("shop-target");
                    chip.EnableInClassList("shop-target--selected", option == chosen);
                    chips.Add(chip);
                }
            }

            box.Add(chips);
            if (chosen.IsNone)
            {
                return box;
            }

            SpinConfig config = flow.BuildSpinConfig();
            var effects = new List<ProbabilityEffect>(flow.Probabilities.Effects);
            effects.AddRange(flow.Upgrades.Probabilities.WithExtraStack(chosen, item.Value));
            double[] weights = PocketWeightCalculator.Compute(flow.Definition, effects);
            double after = PocketWeightCalculator.ProbabilityOf(flow.Definition,
                PocketWeightCalculator.Probabilities(flow.Definition, weights, config.SkullChance), chosen);

            box.Add(MakeLabel($"등장 확률 {NumberText.Percent(config.ProbabilityOf(chosen))} → {NumberText.Percent(after)}", "shop-state"));
            return box;
        }

        private static IEnumerable<BetTarget> TargetOptions(DrinkGroup group)
        {
            switch (group)
            {
                case DrinkGroup.Column:
                    return new[] { BetTarget.Outside(BetKind.Column1), BetTarget.Outside(BetKind.Column2), BetTarget.Outside(BetKind.Column3) };
                case DrinkGroup.Dozen:
                    return new[] { BetTarget.Outside(BetKind.Dozen1), BetTarget.Outside(BetKind.Dozen2), BetTarget.Outside(BetKind.Dozen3) };
                case DrinkGroup.Parity:
                    return new[] { BetTarget.Outside(BetKind.Odd), BetTarget.Outside(BetKind.Even) };
                case DrinkGroup.Color:
                    return new[] { BetTarget.Outside(BetKind.Red), BetTarget.Outside(BetKind.Black) };
                case DrinkGroup.Range:
                    return new[] { BetTarget.Outside(BetKind.Low), BetTarget.Outside(BetKind.High) };
                default:
                    return new BetTarget[0];
            }
        }

        /// <summary>카드 아래 작은 상태 줄(보유 수, 지금 효과, 남은 재고).</summary>
        private static string StateText(GameFlowController flow, ShopOffer offer)
        {
            ShopItemDefinition item = offer.Item;
            string stock = item.IsPermanent ? string.Empty : $" · 재고 {offer.Stock}";
            switch (item.Effect)
            {
                case ShopEffect.Focus:
                    return $"보유 {flow.Upgrades.StacksOf(item.Group)}개 · {item.Group.ToKorean()} 지급 +{Percent(flow.Upgrades.PayoutIncrease(item.Group))}";
                case ShopEffect.Cross:
                    return $"보유 {flow.Cross.CountOf(item.TriggerGroup, item.Group)}개 · 발동 시 +{Percent(flow.Cross.IncreaseOf(item.TriggerGroup, item.Group))}";
                case ShopEffect.PaymentSupport:
                    return $"보유 {flow.Upgrades.PaymentRefundStacks}개 · 지금 환급 {Percent(flow.Upgrades.PaymentRefund)}";
                case ShopEffect.ExtraBall:
                    return $"공 {flow.BuildSpinConfig().BallCount}개 (최대 {flow.GameBalance.MaxBallCount}){stock}";
                case ShopEffect.SpareSpin:
                    return $"남은 회전 {flow.Round.SpinsRemaining}{stock}";
                default:
                    return $"보유 {flow.Inventory.CountOf(item.Effect)}개{stock}";
            }
        }

        private static string UpgradeSummary(GameFlowController flow)
        {
            var lines = new List<string>();
            if (flow.Upgrades.PaymentRefund > 0m)
            {
                lines.Add($"납부 지원금 · 납부할 때마다 낸 납부금의 {Percent(flow.Upgrades.PaymentRefund)} 환급 ({flow.Upgrades.PaymentRefundStacks}개)");
            }

            for (int i = 0; i < DrinkGroups.Count; i++)
            {
                var group = (DrinkGroup)i;
                if (flow.Upgrades.StacksOf(group) > 0)
                {
                    lines.Add($"집중 · {group.ToKorean()} 지급 +{Percent(flow.Upgrades.PayoutIncrease(group))}");
                }
            }

            foreach (ProbabilityEffect effect in flow.Upgrades.Probabilities.Effects)
            {
                lines.Add($"집중 · {DrinkOffer.TargetLabel(effect.Target)} 가중치 +{Percent(effect.BoostPerStack * effect.Stacks)} ({effect.Stacks}개)");
            }

            for (int t = 0; t < DrinkGroups.Count; t++)
            {
                for (int g = 0; g < DrinkGroups.Count; g++)
                {
                    var trigger = (DrinkGroup)t;
                    var target = (DrinkGroup)g;
                    if (flow.Cross.CountOf(trigger, target) > 0)
                    {
                        lines.Add($"교차 · {ShopItemDefinition.GroupCode(trigger)} → {ShopItemDefinition.GroupCode(target)} +{Percent(flow.Cross.IncreaseOf(trigger, target))}");
                    }
                }
            }

            for (int g = 0; g < DrinkGroups.Count; g++)
            {
                decimal pending = flow.Cross.Pending((DrinkGroup)g);
                if (pending > 0m)
                {
                    lines.Add($"다음 회전 · {ShopItemDefinition.GroupCode((DrinkGroup)g)} 지급 +{Percent(pending)}");
                }
            }

            return lines.Count > 0 ? string.Join("\n", lines) : "아직 강화가 없습니다.";
        }

        // ---------- 공통 ----------

        /// <summary>코인 자리에 돈 아이콘을 넣는다. 없으면 USS의 금색 원 그대로 둔다.</summary>
        private static void ApplyCoin(VisualElement element, Sprite sprite)
        {
            if (element == null)
            {
                return;
            }

            element.EnableInClassList("shop-coin--sprite", sprite != null);
            element.style.backgroundImage = sprite != null ? new StyleBackground(sprite) : new StyleBackground(StyleKeyword.Null);
        }

        private static VisualElement BuildIcon(ShopItemDefinition item)
        {
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("shop-icon");
            if (item.Icon != null)
            {
                icon.style.backgroundImage = new StyleBackground(item.Icon);
                return icon;
            }

            icon.AddToClassList("shop-icon--placeholder");
            icon.style.backgroundColor = item.Tint;
            icon.Add(MakeLabel(IconText(item), "shop-icon__label"));
            return icon;
        }

        private static string IconText(ShopItemDefinition item)
        {
            switch (item.Effect)
            {
                case ShopEffect.Focus:
                    return "▲";
                case ShopEffect.Cross:
                    return "→";
                case ShopEffect.PaymentSupport:
                    return "₩";
                case ShopEffect.ExtraBall:
                    return "●+";
                case ShopEffect.SkullShield:
                    return "☠";
                case ShopEffect.SpareSpin:
                    return "+1";
                default:
                    return "½";
            }
        }

        private static string Percent(decimal value) => (value * 100m).ToString("0.#", CultureInfo.InvariantCulture) + "%";

        private static VisualElement MakeElement(string className)
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.AddToClassList(className);
            return element;
        }

        private static Label MakeLabel(string text, params string[] classes)
        {
            var label = new Label(text);
            foreach (string name in classes)
            {
                label.AddToClassList(name);
            }

            label.pickingMode = PickingMode.Ignore;
            return label;
        }
    }
}
