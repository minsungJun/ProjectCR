using System;
using UnityEngine;

namespace ProjectCR
{
    /// <summary>튜토리얼 페이지가 떠 있는 동안 화면에서 강조할 것(TutorialHighlighter가 그린다).</summary>
    public enum TutorialHighlight
    {
        None,

        /// <summary>칩 트레이의 모든 칸 테두리가 천천히 깜빡인다.</summary>
        ChipTray,

        /// <summary>숫자 하나·바깥 베팅(빨강/검정, 홀짝, 높낮이, 더즌, 컬럼)의 베팅 점. 선·교차점은 빼고.</summary>
        NumberAndOutsideSpots,

        /// <summary>숫자 사이의 선과 교차점(스플릿·스트리트·코너·식스 라인)의 베팅 점.</summary>
        LineAndCornerSpots,

        /// <summary>빨강 베팅의 호버 안내(배율·지급)를 띄운다.</summary>
        RedPayoutTooltip,

        /// <summary>초록색 0 칸의 베팅 점.</summary>
        ZeroSpot,

        /// <summary>보라색 위험(해골) 칸이 깜빡인다(룰렛이 보이게 페이지 카메라를 Roulette로 둔다).</summary>
        SkullPocket,
    }

    /// <summary>튜토리얼 페이지가 뜰 때 연출 카메라를 옮길 자리. Keep이면 그대로 둔다.</summary>
    public enum TutorialCamera
    {
        Keep,

        /// <summary>게임 시작 자리(CameraPostion_Default, 좌석에서 홀을 보는 시점).</summary>
        Default,

        /// <summary>베팅판 자리.</summary>
        Board,

        /// <summary>룰렛 자리.</summary>
        Roulette,
    }

    /// <summary>튜토리얼 대사 한 페이지(1~3문장). 질문이면 [다음] 대신 [네]/[아니오]를 보인다.</summary>
    [Serializable]
    public class TutorialPage
    {
        [TextArea(2, 5)]
        public string text;

        [Tooltip("네/아니오 질문. 대사 출력이 끝나면 두 버튼이 나온다.")]
        public bool isQuestion;

        [Tooltip("이 페이지가 떠 있는 동안 화면에서 강조할 것.")]
        public TutorialHighlight highlight;

        [Tooltip("이 페이지가 뜰 때 카메라를 옮길 자리(Keep = 그대로).")]
        public TutorialCamera camera;

        public TutorialPage()
        {
        }

        public TutorialPage(string text, bool isQuestion = false)
        {
            this.text = text;
            this.isQuestion = isQuestion;
        }
    }

    /// <summary>
    /// 딜러 튜토리얼 대사(왼쪽 위 대화창). 사용자가 쓴 대사를 그대로 담는다. 순서·분기는 TutorialController가 정한다.
    /// - 최초 진입: intro(마지막 페이지가 "규칙을 아십니까?" 질문) → 네: knowsRules / 아니오: rouletteRules → commonRules
    /// - 첫 라운드 통과(음료): drink, 그 뒤 서비스 안내: service
    /// - 결말: gameOver / gameClear
    /// 대사 안의 {dealer}는 딜러 이름으로 바뀐다.
    /// </summary>
    [CreateAssetMenu(fileName = "TutorialDialogue", menuName = "ProjectCR/Tutorial Dialogue")]
    public class TutorialDialogueData : ScriptableObject
    {
        public const string DealerToken = "{dealer}";
        public const string FallbackDealerName = "딜러";

        [Tooltip("대화창 이름표와 대사의 {dealer} 자리에 쓰는 딜러 이름. 비우면 \"딜러\".")]
        [SerializeField] private string dealerName = "**";

        [Header("최초 진입")]
        [SerializeField] private TutorialPage[] intro = new TutorialPage[0];
        [SerializeField] private TutorialPage[] knowsRules = new TutorialPage[0];
        [SerializeField] private TutorialPage[] rouletteRules = new TutorialPage[0];
        [SerializeField] private TutorialPage[] commonRules = new TutorialPage[0];

        [Header("진행 중")]
        [SerializeField] private TutorialPage[] drink = new TutorialPage[0];
        [SerializeField] private TutorialPage[] service = new TutorialPage[0];

        [Header("안내(버튼 없이 잠깐 뜨는 딜러 말)")]
        [Tooltip("튜토리얼이 없는 새 게임에서 베팅판에 도착하면 띄운다. 첫 회전을 시작하면 사라진다.")]
        [TextArea(2, 3)]
        [SerializeField] private string startNotice = "트레이에서 칩을 고르고 테이블 베팅판에 놓은 뒤 ‘회전’을 누르십시오.";

        [Header("결말")]
        [SerializeField] private TutorialPage[] gameOver = new TutorialPage[0];
        [SerializeField] private TutorialPage[] gameClear = new TutorialPage[0];

        public string DealerName => string.IsNullOrWhiteSpace(dealerName) ? FallbackDealerName : dealerName.Trim();

        public TutorialPage[] Intro => intro;
        public TutorialPage[] KnowsRules => knowsRules;
        public TutorialPage[] RouletteRules => rouletteRules;
        public TutorialPage[] CommonRules => commonRules;
        public TutorialPage[] Drink => drink;
        public TutorialPage[] Service => service;
        public TutorialPage[] GameOver => gameOver;
        public TutorialPage[] GameClear => gameClear;
        public string StartNotice => startNotice;

        /// <summary>화면에 쓸 문장: {dealer}를 딜러 이름으로.</summary>
        public string Format(string text) => (text ?? string.Empty).Replace(DealerToken, DealerName);

        /// <summary>에디터 데이터 채우기·테스트용.</summary>
        public void Set(string name, TutorialPage[] introPages, TutorialPage[] knows, TutorialPage[] rules, TutorialPage[] common,
            TutorialPage[] drinkPages, TutorialPage[] servicePages, TutorialPage[] over, TutorialPage[] clear)
        {
            dealerName = name;
            intro = introPages;
            knowsRules = knows;
            rouletteRules = rules;
            commonRules = common;
            drink = drinkPages;
            service = servicePages;
            gameOver = over;
            gameClear = clear;
        }
    }
}
