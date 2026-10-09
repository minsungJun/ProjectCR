using System.Collections.Generic;
using UnityEngine;

namespace ProjectCR
{
    /// <summary>
    /// 확률 강화 음료 한 종류(정적 데이터). 마실 때 <b>플레이어가 묶음 안에서 대상을 고른다</b>:
    /// 코랄은 1열·2열·3열, 엠버는 1–12·13–24·25–36, 에메랄드는 홀·짝, 루비는 빨강·검정, 스카이는 1–18·19–36, 바이올렛은 숫자(0~36).
    /// 한 잔의 가중치 증가(<see cref="Boost"/>)를 고른 대상 수로 똑같이 나눠 붙인다(컬럼 +30%에 두 개를 고르면 각각 +15%).
    ///
    /// 계수는 가중치 증가다(0.1 = 가중치 +10%). 당첨 확률 +10%p가 아니다.
    /// 기본 계수는 "대상 하나만 골라 한 잔 마시면 그 대상 베팅의 기대 환수율 ≈ 110%"(강화 전 94.7%)에 맞췄다(2026-10-09).
    /// 칸 수가 다르면 같은 환수율에 필요한 계수가 달라서 묶음마다 계수가 다르다.
    /// </summary>
    [CreateAssetMenu(fileName = "ProbDrink", menuName = "ProjectCR/Probability Drink")]
    public class ProbabilityDrinkDefinition : ScriptableObject
    {
        [SerializeField] private DrinkGroup group;

        [SerializeField] private string displayName = "확률 음료";

        [Tooltip("툴팁의 효과 설명 줄. 비우면 자동 문장(\"컬럼(1열·2열·3열) 중 고른 대상이 나올 확률을 올립니다. …\").\n" +
                 "{group} = 묶음 이름(컬럼), {bets} = 고를 수 있는 대상(1열·2열·3열), {boost} = 한 잔 가중치(+26%)로 바뀐다.")]
        [TextArea(2, 4)]
        [SerializeField] private string description;

        [Tooltip("카드 그림. 비우면 강조색 잔 모양으로 대신 그린다.")]
        [SerializeField] private Sprite icon;

        [Tooltip("카드 강조색, 대체 그림의 음료 색.")]
        [SerializeField] private Color tint = new Color(0.8f, 0.6f, 0.3f);

        [Tooltip("이 음료의 3D 모델(프리팹). 아직 게임 화면에서는 쓰지 않는다.")]
        [SerializeField] private GameObject model;

        [Tooltip("숫자가 아닌 묶음의 한 잔 가중치 증가. 고른 대상 수로 나눠 붙는다. 대상 하나 110% 기준: 색깔·홀짝·하이로우 0.37, 컬럼·더즌 0.26.")]
        [Min(0f)]
        [SerializeField] private float boostPerStack = 0.37f;

        [Header("숫자 묶음 전용")]
        [Tooltip("숫자 음료의 한 잔 가중치 증가. 고른 숫자 수로 나눠 붙는다. 숫자 하나 110% 기준 0.17.")]
        [Min(0f)]
        [SerializeField] private float singleNumberBoost = 0.17f;

        public DrinkGroup Group => group;
        public string DisplayName => displayName;

        /// <summary>인스펙터에 적은 설명(자리 표시 바꿈 전). 비었으면 null.</summary>
        public string CustomDescription => string.IsNullOrWhiteSpace(description) ? null : description.Trim();

        public Sprite Icon => icon;
        public Color Tint => tint;
        public GameObject Model => model;

        /// <summary>한 잔의 가중치 증가 전체(decimal로 정확히). 고른 대상 수로 나눠 붙는다.</summary>
        public decimal Boost => (decimal)Mathf.Max(0f, group == DrinkGroup.Number ? singleNumberBoost : boostPerStack);

        /// <summary>대상 n개를 골랐을 때 대상 하나에 붙는 증가량.</summary>
        public decimal BoostEach(int count) => count > 0 ? Boost / count : 0m;

        /// <summary>이 묶음에서 고를 수 있는 대상(숫자 묶음은 0~36 숫자 하나씩). 해골은 없다.</summary>
        public static List<BetTarget> TargetOptions(DrinkGroup group) => group.Targets();

        /// <summary>고른 대상 목록이 맞는지: 하나 이상, 모두 이 묶음의 대상, 겹치지 않음.</summary>
        public static bool AreValidTargets(DrinkGroup group, IReadOnlyList<BetTarget> targets)
        {
            if (targets == null || targets.Count == 0)
            {
                return false;
            }

            List<BetTarget> options = TargetOptions(group);
            for (int i = 0; i < targets.Count; i++)
            {
                if (!options.Contains(targets[i]))
                {
                    return false;
                }

                for (int j = 0; j < i; j++)
                {
                    if (targets[j] == targets[i])
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>코드로 만든다(테스트, 기본 에셋 생성). 숫자 계수는 기본값.</summary>
        public static ProbabilityDrinkDefinition Create(DrinkGroup group, string displayName, Color tint, float boostPerStack)
        {
            var drink = CreateInstance<ProbabilityDrinkDefinition>();
            drink.name = $"ProbDrink_{group}";
            drink.group = group;
            drink.displayName = displayName;
            drink.tint = tint;
            drink.boostPerStack = boostPerStack;
            return drink;
        }

        /// <summary>기본 6종(대상 하나 1잔 ≈ 110% 기준): 색깔·홀짝·하이로우 +37%, 컬럼·더즌 +26%, 숫자 +17%.</summary>
        public static ProbabilityDrinkDefinition[] CreateDefaults()
        {
            return new[]
            {
                Create(DrinkGroup.Column, "코랄 위스키", DrinkColors.CoralOrange, 0.26f),
                Create(DrinkGroup.Dozen, "엠버 위스키", DrinkColors.Amber, 0.26f),
                Create(DrinkGroup.Parity, "에메랄드 위스키", DrinkColors.Emerald, 0.37f),
                Create(DrinkGroup.Color, "루비 위스키", DrinkColors.Ruby, 0.37f),
                Create(DrinkGroup.Range, "스카이 위스키", DrinkColors.SkyBlue, 0.37f),
                Create(DrinkGroup.Number, "바이올렛 위스키", DrinkColors.Violet, 0f),
            };
        }
    }

    /// <summary>음료 색(칵테일 모델의 음료 머티리얼 색). 묶음별: 컬럼 코랄 오렌지, 더즌 엠버, 홀짝 에메랄드, 색깔 루비, 하이로우 스카이블루, 숫자 바이올렛.</summary>
    public static class DrinkColors
    {
        public static readonly Color CoralOrange = new Color32(0xF9, 0x69, 0x2C, 0xFF);
        public static readonly Color Amber = new Color32(0xD1, 0x81, 0x24, 0xFF);
        public static readonly Color Emerald = new Color32(0x16, 0xA8, 0x4B, 0xFF);
        public static readonly Color Ruby = new Color32(0xC4, 0x10, 0x27, 0xFF);
        public static readonly Color SkyBlue = new Color32(0x1D, 0xB3, 0xDA, 0xFF);
        public static readonly Color Violet = new Color32(0x7C, 0x16, 0xA2, 0xFF);

        public static Color Of(DrinkGroup group)
        {
            switch (group)
            {
                case DrinkGroup.Column:
                    return CoralOrange;
                case DrinkGroup.Dozen:
                    return Amber;
                case DrinkGroup.Parity:
                    return Emerald;
                case DrinkGroup.Color:
                    return Ruby;
                case DrinkGroup.Range:
                    return SkyBlue;
                default:
                    return Violet;
            }
        }
    }
}
