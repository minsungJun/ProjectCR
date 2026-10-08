using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectCR
{
    /// <summary>
    /// 상점 창(HUD와 같은 UIDocument의 shop-panel). 상품 탭과 보유 아이템 탭을 그리고, 버튼 입력을 이벤트로 넘긴다.
    /// 구매·사용 검증과 돈 처리는 GameFlowController(ShopController, ItemEffectController)가 한다.
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

        /// <summary>소모품 사용. 두 번째 값은 해골 방패 해제(true)·장착(false).</summary>
        public event Action<ShopEffect, bool> ItemUseRequested;

        // 진열 칸별로 고른 집중 강화 대상. 라운드가 바뀌면(진열이 바뀌면) 비운다.
        private readonly Dictionary<int, BetTarget> chosenTargets = new Dictionary<int, BetTarget>();

        private VisualElement panel;
        private Label money;
        private Label notice;
        private ScrollView scroll;
        private Button goodsTab;
        private Button itemsTab;
        private bool showItems;
        private int targetsRound;
        private GameFlowController lastFlow;

        public bool IsOpen { get; private set; }

        public void Initialize()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            panel = root.Q("shop-panel");
            money = root.Q<Label>("shop-money");
            notice = root.Q<Label>("shop-notice");
            scroll = root.Q<ScrollView>("shop-scroll");
            goodsTab = root.Q<Button>("shop-tab-goods");
            itemsTab = root.Q<Button>("shop-tab-items");

            root.Q<Button>("shop-close").clicked += () => Closed?.Invoke();
            goodsTab.clicked += () => SwitchTab(false);
            itemsTab.clicked += () => SwitchTab(true);
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
            if (notice != null)
            {
                notice.text = text ?? string.Empty;
            }
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

            money.text = $"보유 {flow.Wallet.Balance:N0} · 구매 가능 {flow.Shop.AvailableFunds:N0}" +
                         (flow.Bet.HasBets ? $" (놓은 칩 {flow.Bet.TotalStake:N0} 제외)" : string.Empty) +
                         $" · 납부금 {flow.Round.CurrentDue:N0} · 라운드 {flow.Round.Round}";

            goodsTab.EnableInClassList(ActiveTabClass, !showItems);
            itemsTab.EnableInClassList(ActiveTabClass, showItems);

            scroll.Clear();
            if (showItems)
            {
                BuildItems(flow);
            }
            else
            {
                BuildGoods(flow);
            }
        }

        private void SwitchTab(bool items)
        {
            showItems = items;
            ShowNotice(string.Empty);
            if (lastFlow != null)
            {
                Refresh(lastFlow);
            }
        }

        // ---------- 상품 탭 ----------

        private void BuildGoods(GameFlowController flow)
        {
            IReadOnlyList<ShopOffer> offers = flow.Shop.Offers;

            // 분류 묶음(제목 + 카드)을 옆으로 이어 놓고, 넓이가 모자라면 아래로 줄바꿈한다.
            var groups = new VisualElement();
            groups.AddToClassList("shop-groups");
            scroll.Add(groups);

            foreach (ShopCategory category in new[] { ShopCategory.Focus, ShopCategory.Cross, ShopCategory.PaymentSupport, ShopCategory.Consumable })
            {
                VisualElement row = null;
                for (int i = 0; i < offers.Count; i++)
                {
                    if (offers[i].Item.Category != category)
                    {
                        continue;
                    }

                    if (row == null)
                    {
                        var group = new VisualElement();
                        group.AddToClassList("shop-group");
                        group.Add(MakeLabel(CategoryName(category), "shop-section"));
                        row = new VisualElement();
                        row.AddToClassList("shop-row");
                        group.Add(row);
                        groups.Add(group);
                    }

                    row.Add(BuildCard(flow, i, offers[i]));
                }
            }

            if (offers.Count == 0)
            {
                scroll.Add(MakeLabel("진열된 상품이 없습니다.", "shop-summary"));
            }
        }

        private VisualElement BuildCard(GameFlowController flow, int slot, ShopOffer offer)
        {
            ShopItemDefinition item = offer.Item;
            var card = new VisualElement();
            card.AddToClassList("shop-card");
            if (item.Category == ShopCategory.Cross)
            {
                card.AddToClassList("shop-card--cross");
            }
            else if (item.Category == ShopCategory.Consumable || item.Category == ShopCategory.PaymentSupport)
            {
                card.AddToClassList("shop-card--consumable");
            }

            var top = new VisualElement();
            top.AddToClassList("shop-card__top");
            top.Add(BuildIcon(item));
            var titles = new VisualElement { style = { flexShrink = 1 } };
            titles.Add(MakeLabel(item.DisplayName, "shop-name"));
            titles.Add(MakeLabel(CategoryName(item.Category), "shop-badge"));
            top.Add(titles);
            card.Add(top);

            card.Add(MakeLabel(item.Description, "shop-desc"));
            card.Add(MakeLabel(item.Duration, "shop-duration"));
            card.Add(MakeLabel(OwnedText(flow, item) + $" · 재고 {offer.Stock}", "shop-owned"));

            chosenTargets.TryGetValue(slot, out BetTarget target);
            if (item.NeedsTarget)
            {
                card.Add(BuildTargetPicker(flow, slot, item, target));
            }

            ShopError check = flow.Shop.Check(slot, target);
            long price = flow.Shop.PriceOf(offer);

            var buyRow = new VisualElement();
            buyRow.AddToClassList("shop-buy-row");
            buyRow.Add(MakeLabel($"{price:N0}", "shop-price"));
            var buy = new Button(() => BuyRequested?.Invoke(slot, target)) { text = "구매" };
            buy.AddToClassList("btn");
            buy.AddToClassList("shop-buy");
            buy.SetEnabled(check == ShopError.None);
            buyRow.Add(buy);
            card.Add(buyRow);

            string reason = item.IsPermanent && offer.BoughtThisRound > 0 ? "구매 완료 · 이후 상점에 나오지 않습니다"
                : check != ShopError.None ? check.ToKorean()
                : flow.Wallet.Balance - price < flow.Round.CurrentDue ? "구매 후 납부금이 부족해집니다"
                : string.Empty;
            if (reason.Length > 0)
            {
                card.Add(MakeLabel(reason, "shop-reason"));
            }

            return card;
        }

        /// <summary>집중 강화 대상 고르기. 숫자는 판에서 고른다. 고른 대상의 가중치 증가와 실제 등장 확률(전 → 후)을 보여 준다.</summary>
        private VisualElement BuildTargetPicker(GameFlowController flow, int slot, ShopItemDefinition item, BetTarget chosen)
        {
            var box = new VisualElement();
            var chips = new VisualElement();
            chips.AddToClassList("shop-targets");

            if (item.Group == DrinkGroup.Number)
            {
                var pick = new Button(() => PickTargetRequested?.Invoke(slot)) { text = "판에서 고르기" };
                pick.AddToClassList("shop-target");
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
                box.Add(MakeLabel("대상을 고르면 구매할 수 있습니다", "shop-duration"));
                return box;
            }

            SpinConfig config = flow.BuildSpinConfig();
            var effects = new List<ProbabilityEffect>(flow.Probabilities.Effects);
            effects.AddRange(flow.Upgrades.Probabilities.WithExtraStack(chosen, item.Value));
            double[] weights = PocketWeightCalculator.Compute(flow.Definition, effects);
            double after = PocketWeightCalculator.ProbabilityOf(flow.Definition,
                PocketWeightCalculator.Probabilities(flow.Definition, weights, config.SkullChance), chosen);

            box.Add(MakeLabel(
                $"대상 {DrinkOffer.TargetLabel(chosen)} · 가중치 +{Percent(item.Value)} · 등장 확률 {NumberText.Percent(config.ProbabilityOf(chosen))} → {NumberText.Percent(after)}",
                "shop-owned"));
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

        private static string OwnedText(GameFlowController flow, ShopItemDefinition item)
        {
            switch (item.Effect)
            {
                case ShopEffect.Focus:
                    return $"보유 {flow.Upgrades.StacksOf(item.Group)}개 · {item.Group.ToKorean()} 지급 +{Percent(flow.Upgrades.PayoutIncrease(item.Group))}";
                case ShopEffect.Cross:
                    int count = flow.Cross.CountOf(item.TriggerGroup, item.Group);
                    return $"보유 {count}개 · 발동 시 +{Percent(flow.Cross.IncreaseOf(item.TriggerGroup, item.Group))}";
                case ShopEffect.PaymentSupport:
                    long now = (long)Math.Floor(flow.Round.CurrentDue * (flow.Upgrades.PaymentRefund + item.Value));
                    return $"보유 {flow.Upgrades.PaymentRefundStacks}개 · 지금 환급 {Percent(flow.Upgrades.PaymentRefund)} · 사면 이번 납부 때 +{now:N0}";
                default:
                    return $"보유 {flow.Inventory.CountOf(item.Effect)}개";
            }
        }

        // ---------- 보유 아이템 탭 ----------

        private void BuildItems(GameFlowController flow)
        {
            InventoryState inv = flow.Inventory;
            SpinConfig config = flow.BuildSpinConfig();

            scroll.Add(MakeLabel("소모품", "shop-section"));
            scroll.Add(ItemRow("추가 공", inv.CountOf(ShopEffect.ExtraBall),
                $"이번 라운드 공 {config.BallCount}개 (최대 {flow.GameBalance.MaxBallCount})",
                Use("사용", ShopEffect.ExtraBall, false, inv.CountOf(ShopEffect.ExtraBall) > 0 && config.BallCount < flow.GameBalance.MaxBallCount)));
            scroll.Add(ItemRow("해골 방패", inv.CountOf(ShopEffect.SkullShield),
                $"장착 {inv.ShieldsEquipped} / 보유 {inv.CountOf(ShopEffect.SkullShield)} · 장착한 방패만 해골을 막는다",
                Use("장착", ShopEffect.SkullShield, false, inv.ShieldsEquipped < inv.CountOf(ShopEffect.SkullShield)),
                Use("해제", ShopEffect.SkullShield, true, inv.ShieldsEquipped > 0)));
            scroll.Add(ItemRow("여분의 회전권", inv.CountOf(ShopEffect.SpareSpin),
                $"남은 회전 {flow.Round.SpinsRemaining}",
                Use("사용", ShopEffect.SpareSpin, false, inv.CountOf(ShopEffect.SpareSpin) > 0)));
            scroll.Add(ItemRow("보험 증서", inv.CountOf(ShopEffect.Insurance),
                inv.InsuranceArmed ? "다음 회전 보험 적용 중" : $"다음 회전이 일반 낙첨이면 베팅금 {Percent(inv.InsuranceRatio)} 환급",
                Use("다음 회전에 사용", ShopEffect.Insurance, false, inv.CountOf(ShopEffect.Insurance) > 0 && !inv.InsuranceArmed)));


            scroll.Add(MakeLabel("강화", "shop-section"));
            scroll.Add(MakeLabel(UpgradeSummary(flow), "shop-summary"));
        }

        private Button Use(string text, ShopEffect effect, bool flag, bool enabled)
        {
            var button = new Button(() => ItemUseRequested?.Invoke(effect, flag)) { text = text };
            button.AddToClassList("btn");
            button.AddToClassList("shop-item-button");
            button.SetEnabled(enabled);
            return button;
        }

        private static VisualElement ItemRow(string name, int count, string state, params Button[] buttons)
        {
            var row = new VisualElement();
            row.AddToClassList("shop-item-row");
            var title = MakeLabel($"{name} ×{count}", "shop-name");
            title.style.width = 190;
            row.Add(title);
            row.Add(MakeLabel(state, "shop-desc"));
            foreach (Button button in buttons)
            {
                row.Add(button);
            }

            return row;
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

        private static VisualElement BuildIcon(ShopItemDefinition item)
        {
            var icon = new VisualElement();
            icon.AddToClassList("shop-icon");
            if (item.Icon != null)
            {
                icon.style.backgroundImage = new StyleBackground(item.Icon);
                return icon;
            }

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

        private static string CategoryName(ShopCategory category)
        {
            switch (category)
            {
                case ShopCategory.Focus:
                    return "집중 강화";
                case ShopCategory.Cross:
                    return "교차 강화";
                case ShopCategory.PaymentSupport:
                    return "납부 지원";
                default:
                    return "소모품";
            }
        }

        private static string Percent(decimal value) => (value * 100m).ToString("0.#", CultureInfo.InvariantCulture) + "%";

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
