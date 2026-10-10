using UnityEngine;

namespace ProjectCR
{
    /// <summary>
    /// 지급 음료 한 종류(정적 데이터). 후보에 나올 때 묶음 안의 대상 하나(1열, 빨강, 7번 …)가 정해지고,
    /// 마시면 <b>그 대상</b>의 지급 배율에만 boostPerStack을 한 번 더 곱한다.
    /// 지금 몇 잔 마셨는지는 여기 두지 않는다(<see cref="DrinkRunState"/>).
    /// </summary>
    [CreateAssetMenu(fileName = "Drink", menuName = "ProjectCR/Drink")]
    public class DrinkDefinition : ScriptableObject
    {
        [SerializeField] private DrinkGroup group;

        [SerializeField] private string displayName = "음료";

        [Tooltip("툴팁의 효과 설명 줄. 비우면 자동 문장(\"1열 베팅의 당첨금 배율에 ×1.5를 곱합니다. 2열·3열은 그대로입니다.\").\n" +
                 "{target} = 이번에 정해진 대상(1열, 빨강, 7번 …), {group} = 묶음 이름(컬럼), {bets} = 묶음의 대상(1열·2열·3열), {boost} = 잔당 배율(1.5)로 바뀐다.")]
        [TextArea(2, 4)]
        [SerializeField] private string description;

        [Tooltip("카드 그림. 비우면 강조색 병 모양으로 대신 그린다.")]
        [SerializeField] private Sprite icon;

        [Tooltip("카드 강조색, 대체 그림의 병 색.")]
        [SerializeField] private Color tint = new Color(0.8f, 0.6f, 0.3f);

        [Tooltip("이 음료의 3D 모델(프리팹). 아직 게임 화면에서는 쓰지 않는다.")]
        [SerializeField] private GameObject model;

        [Tooltip("한 잔마다 이 묶음의 지급 배율에 곱하는 값. <b>1.5는 처음 시험하는 값이고 통과율은 검증하지 않았다.</b>")]
        [Min(1f)]
        [SerializeField] private float boostPerStack = 1.5f;

        public DrinkGroup Group => group;
        public string DisplayName => displayName;
        public Sprite Icon => icon;
        public Color Tint => tint;
        public GameObject Model => model;

        /// <summary>인스펙터에 적은 설명(자리 표시 바꿈 전). 비었으면 null.</summary>
        public string CustomDescription => string.IsNullOrWhiteSpace(description) ? null : description.Trim();

        /// <summary>계산은 decimal로 한다. float 1.5는 decimal 1.5로 정확히 바뀐다.</summary>
        public decimal BoostPerStack => (decimal)Mathf.Max(1f, boostPerStack);

        /// <summary>코드로 만든다(테스트, 기본 에셋 생성).</summary>
        public static DrinkDefinition Create(DrinkGroup group, string displayName, Color tint, float boostPerStack = 1.5f)
        {
            var drink = CreateInstance<DrinkDefinition>();
            drink.name = $"Drink_{group}";
            drink.group = group;
            drink.displayName = displayName;
            drink.tint = tint;
            drink.boostPerStack = boostPerStack;
            return drink;
        }
    }
}
