using System;
using UnityEngine;

namespace ProjectCR
{
    /// <summary>
    /// 밸런스 수치를 한곳에 모은 설정. <b>여기 있는 값은 전부 임시값이다.</b>
    /// 아이템과 다른 베팅 종류가 들어온 뒤 다시 잡는다.
    ///
    /// 정적 설정이라 런타임에 고치지 않는다. 회전 단위로 바뀌는 값은 SpinConfig로 복사해서 쓴다.
    /// </summary>
    [CreateAssetMenu(fileName = "GameBalance", menuName = "ProjectCR/Game Balance")]
    public class GameBalance : ScriptableObject
    {
        [Header("돈")]
        [Tooltip("새 게임 시작 자금.")]
        [SerializeField] private long initialMoney = 100;

        [Tooltip("보유 금액 상한. 넘치는 지급은 여기서 잘린다. UI 12자리에 맞춘 값.")]
        [SerializeField] private long maxMoney = 999_999_999_999;

        [Header("베팅")]
        [Tooltip("한 번에 걸 수 있는 최소 금액. 보유 금액이 이보다 적으면 베팅할 수 없다.")]
        [SerializeField] private long minBet = 1;

        [Tooltip("숫자 하나 베팅의 <b>원금 포함 총 지급 배수</b>. 36이면 100을 걸어 맞혔을 때 3,600을 받는다(순이익 3,500).")]
        [SerializeField] private int straightTotalPayout = 36;

        [Tooltip("회전마다 굴리는 공 수. 아이템이 늘리는 값의 기준.")]
        [SerializeField] private int ballCount = 1;

        [Header("라운드")]
        [Tooltip("라운드당 회전 횟수.")]
        [SerializeField] private int spinsPerRound = 5;

        [Tooltip("1라운드 납부금.")]
        [SerializeField] private long firstRoundDue = 120;

        [Tooltip("라운드가 하나 지날 때마다 납부금에 곱하는 값.")]
        [SerializeField] private float dueGrowth = 1.5f;

        [Tooltip("납부금을 이 단위로 올림한다.")]
        [SerializeField] private long dueRoundingUnit = 10;

        public long InitialMoney => initialMoney;
        public long MaxMoney => maxMoney;
        public long MinBet => minBet;
        public int StraightTotalPayout => straightTotalPayout;
        public int BallCount => ballCount;
        public int SpinsPerRound => spinsPerRound;

        /// <summary>round는 1부터. due(n) = 올림(firstRoundDue × dueGrowth^(n−1), 단위).</summary>
        public long GetDue(int round)
        {
            double raw = firstRoundDue * Math.Pow(dueGrowth, Math.Max(0, round - 1));
            long unit = Math.Max(1, dueRoundingUnit);
            double rounded = Math.Ceiling(raw / unit) * unit;
            return rounded >= maxMoney ? maxMoney : (long)rounded;
        }

        private void OnValidate()
        {
            initialMoney = Math.Max(0, initialMoney);
            maxMoney = Math.Max(1, maxMoney);
            minBet = Math.Max(1, minBet);
            straightTotalPayout = Math.Max(1, straightTotalPayout);
            ballCount = Math.Max(1, ballCount);
            spinsPerRound = Math.Max(1, spinsPerRound);
            firstRoundDue = Math.Max(0, firstRoundDue);
            dueGrowth = Mathf.Max(1f, dueGrowth);
            dueRoundingUnit = Math.Max(1, dueRoundingUnit);
        }
    }
}
