using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectCR
{
    /// <summary>
    /// 확률 강화 음료 한 종류(정적 데이터). 마시면 <b>구체적인 대상 하나</b>의 추첨 가중치가 잔마다 계수만큼 오른다.
    /// 대상은 후보를 만들 때 정한다(<see cref="DrawTarget"/>): 빨강/검정 중 하나, 세 컬럼 중 하나, 숫자 하나 등.
    /// 묶음 전체(색상 전체, 컬럼 전체)를 올리지는 않는다.
    ///
    /// 계수는 가중치 증가다(0.1 = 가중치 +10%). 당첨 확률 +10%p가 아니다.
    /// 기본 계수는 "한 잔 마시면 그 대상 베팅의 기대 환수율 ≈ 110%"(강화 전 94.7%)에 맞췄다(2026-10-09).
    /// 칸 수가 다르면 같은 환수율에 필요한 계수가 달라서 대상 종류마다 계수가 다르다.
    /// </summary>
    [CreateAssetMenu(fileName = "ProbDrink", menuName = "ProjectCR/Probability Drink")]
    public class ProbabilityDrinkDefinition : ScriptableObject
    {
        [SerializeField] private DrinkGroup group;

        [SerializeField] private string displayName = "확률 음료";

        [Tooltip("카드 그림. 비우면 강조색 잔 모양으로 대신 그린다.")]
        [SerializeField] private Sprite icon;

        [Tooltip("카드 강조색, 대체 그림의 음료 색.")]
        [SerializeField] private Color tint = new Color(0.8f, 0.6f, 0.3f);

        [Tooltip("이 음료의 3D 모델(프리팹). 아직 게임 화면에서는 쓰지 않는다.")]
        [SerializeField] private GameObject model;

        [Tooltip("숫자가 아닌 묶음의 한 잔당 가중치 증가. 1잔 110% 기준: 색상·홀짝·구간 0.37, 컬럼·더즌 0.26.")]
        [Min(0f)]
        [SerializeField] private float boostPerStack = 0.37f;

        [Header("숫자 묶음 전용")]
        [Tooltip("숫자 하나(0 포함) 대상의 한 잔당 가중치 증가. 1잔 110% 기준 0.17.")]
        [Min(0f)]
        [SerializeField] private float singleNumberBoost = 0.17f;

        [Tooltip("숫자 2~6개(스플릿·스트리트·코너·식스 라인, 0 제외) 대상의 한 잔당 가중치 증가. 1잔 110% 기준 0.18(2칸 110.7% ~ 6칸 108.6%).")]
        [Min(0f)]
        [SerializeField] private float numberSetBoost = 0.18f;

        [Tooltip("후보를 만들 때 숫자 하나가 대상이 될 확률. 나머지는 숫자 묶음.")]
        [Range(0f, 1f)]
        [SerializeField] private float singleNumberChance = 0.5f;

        // 숫자 묶음 후보: 베팅판에서 걸 수 있는 안쪽 조합 중 0이 없는 것.
        private static List<BetTarget> numberSets;

        public DrinkGroup Group => group;
        public string DisplayName => displayName;
        public Sprite Icon => icon;
        public Color Tint => tint;
        public GameObject Model => model;

        /// <summary>대상 종류별 한 잔당 가중치 증가(decimal로 정확히).</summary>
        public decimal BoostFor(BetTarget target)
        {
            float value = group != DrinkGroup.Number ? boostPerStack
                : target.IsStraight ? singleNumberBoost
                : numberSetBoost;
            return (decimal)Mathf.Max(0f, value);
        }

        /// <summary>후보 카드에 올릴 구체적인 대상을 하나 뽑는다. 해골은 대상이 될 수 없다.</summary>
        public BetTarget DrawTarget(System.Random rng)
        {
            switch (group)
            {
                case DrinkGroup.Color:
                    return BetTarget.Outside(rng.Next(2) == 0 ? BetKind.Red : BetKind.Black);
                case DrinkGroup.Parity:
                    return BetTarget.Outside(rng.Next(2) == 0 ? BetKind.Odd : BetKind.Even);
                case DrinkGroup.Range:
                    return BetTarget.Outside(rng.Next(2) == 0 ? BetKind.Low : BetKind.High);
                case DrinkGroup.Column:
                    return BetTarget.Outside(BetKind.Column1 + rng.Next(3));
                case DrinkGroup.Dozen:
                    return BetTarget.Outside(BetKind.Dozen1 + rng.Next(3));
                default:
                    if (rng.NextDouble() < singleNumberChance)
                    {
                        return BetTarget.Straight(rng.Next(37)); // 0~36
                    }

                    List<BetTarget> sets = NumberSets();
                    return sets[rng.Next(sets.Count)];
            }
        }

        /// <summary>0이 들어가지 않은 안쪽 베팅 조합(숫자 2~6개).</summary>
        public static List<BetTarget> NumberSets()
        {
            if (numberSets == null)
            {
                numberSets = new List<BetTarget>();
                foreach (InsideBetSpot spot in InsideBets.All)
                {
                    if (!spot.Target.CoversNumber(0, PocketColor.Green))
                    {
                        numberSets.Add(spot.Target);
                    }
                }
            }

            return numberSets;
        }

        /// <summary>코드로 만든다(테스트, 기본 에셋 생성). 숫자 묶음 계수는 기본값.</summary>
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

        /// <summary>기본 6종(1잔 ≈ 110% 기준): 색상·홀짝·구간 +37%, 컬럼·더즌 +26%, 숫자 하나 +17%·숫자 묶음 +18%.</summary>
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

    /// <summary>음료 색(칵테일 모델의 음료 머티리얼 색). 묶음별: 컬럼 코랄 오렌지, 더즌 엠버, 홀짝 에메랄드, 색상 루비, 높고 낮음 스카이블루, 숫자 바이올렛.</summary>
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
