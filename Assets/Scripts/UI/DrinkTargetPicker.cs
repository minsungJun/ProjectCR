using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine.UIElements;

namespace ProjectCR
{
    /// <summary>
    /// 확률 음료를 마실 때 대상을 고르는 창(HUD UIDocument의 drink-picker). 여러 개를 고를 수 있고,
    /// 한 잔 가중치를 고른 수로 똑같이 나눠 받는다. 고른 대상 합계 등장 확률을 바로 보여 준다.
    /// 상태는 바꾸지 않는다: [마시기]를 누르면 <see cref="Confirmed"/>로 고른 대상을 넘기고, 실제 적용은 RouletteGame → GameFlowController.ConfirmDrink.
    /// 창 전체에 blocks-input이 붙어 열려 있는 동안 3D 입력이 막힌다.
    /// </summary>
    public sealed class DrinkTargetPicker
    {
        private const string SelectedClass = "shop-target--selected";

        public event Action<IReadOnlyList<BetTarget>> Confirmed;
        public event Action Cancelled;

        private readonly VisualElement panel;
        private readonly Label title;
        private readonly Label hint;
        private readonly VisualElement options;
        private readonly Label preview;
        private readonly Button drinkButton;
        private readonly List<BetTarget> chosen = new List<BetTarget>();
        private readonly Dictionary<BetTarget, Button> chips = new Dictionary<BetTarget, Button>();

        private DrinkOffer offer;
        private GameFlowController flow;

        public DrinkTargetPicker(VisualElement root)
        {
            panel = root.Q("drink-picker");
            title = root.Q<Label>("drink-picker-title");
            hint = root.Q<Label>("drink-picker-hint");
            options = root.Q("drink-picker-options");
            preview = root.Q<Label>("drink-picker-preview");
            drinkButton = root.Q<Button>("drink-picker-drink");
            drinkButton.clicked += () =>
            {
                if (chosen.Count > 0)
                {
                    Confirmed?.Invoke(new List<BetTarget>(chosen));
                }
            };
            root.Q<Button>("drink-picker-cancel").clicked += () => Cancelled?.Invoke();
            Close();
        }

        public bool IsOpen { get; private set; }

        /// <summary>고른 대상(누른 순서).</summary>
        public IReadOnlyList<BetTarget> Chosen => chosen;

        public void Open(DrinkOffer drink, GameFlowController game)
        {
            offer = drink;
            flow = game;
            chosen.Clear();
            chips.Clear();
            options.Clear();

            DrinkGroup group = drink.Group;
            title.text = $"{drink.DisplayName} · 대상 고르기";
            hint.text = $"{DrinkHoverText.Choices(group)} 중 확률을 올릴 대상을 고르세요. 여러 개 고르면 가중치 +{Percent(drink.Boost)}를 똑같이 나눠 받습니다";
            options.EnableInClassList("drink-picker__options--numbers", group == DrinkGroup.Number);
            foreach (BetTarget target in drink.TargetOptions())
            {
                BetTarget value = target;
                var chip = new Button(() => Toggle(value)) { text = Label(value) };
                chip.AddToClassList("shop-target");
                chip.AddToClassList("drink-pick");
                if (value.IsStraight)
                {
                    chip.AddToClassList("drink-pick--number");
                    PocketColor color = game.Definition.ColorOfNumber(value.Number);
                    chip.AddToClassList(color == PocketColor.Red ? "drink-pick--red" : color == PocketColor.Black ? "drink-pick--black" : "drink-pick--green");
                }

                chips[value] = chip;
                options.Add(chip);
            }

            IsOpen = true;
            panel.style.display = DisplayStyle.Flex;
            Refresh();
        }

        public void Close()
        {
            IsOpen = false;
            offer = null;
            flow = null;
            if (panel != null)
            {
                panel.style.display = DisplayStyle.None;
            }
        }

        /// <summary>대상 하나를 고르거나 푼다.</summary>
        public void Toggle(BetTarget target)
        {
            if (!chips.ContainsKey(target))
            {
                return;
            }

            if (!chosen.Remove(target))
            {
                chosen.Add(target);
            }

            Refresh();
        }

        private void Refresh()
        {
            foreach (KeyValuePair<BetTarget, Button> pair in chips)
            {
                pair.Value.EnableInClassList(SelectedClass, chosen.Contains(pair.Key));
            }

            drinkButton.SetEnabled(chosen.Count > 0);
            if (chosen.Count == 0)
            {
                preview.text = "대상을 하나 이상 고르세요";
                return;
            }

            (double now, double after) = flow.PreviewProbabilityDrink(chosen, offer.Boost);
            string names = chosen.Count <= 4
                ? string.Join("·", chosen.ConvertAll(DrinkOffer.TargetLabel))
                : $"{DrinkOffer.TargetLabel(chosen[0])}·{DrinkOffer.TargetLabel(chosen[1])} 외 {chosen.Count - 2}개";
            preview.text = $"{names} · {(chosen.Count > 1 ? "각각 " : string.Empty)}가중치 +{Percent(offer.Boost / chosen.Count)}"
                           + $" · {(chosen.Count > 1 ? "합계 " : string.Empty)}등장 확률 {NumberText.Percent(now)} → {NumberText.Percent(after)}";
        }

        private static string Label(BetTarget target) =>
            target.IsStraight ? target.Number.ToString(CultureInfo.InvariantCulture) : DrinkOffer.TargetLabel(target);

        private static string Percent(decimal value) => (value * 100m).ToString("0.#", CultureInfo.InvariantCulture) + "%";
    }
}
