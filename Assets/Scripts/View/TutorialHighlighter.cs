using System.Collections.Generic;
using UnityEngine;

namespace ProjectCR
{
    /// <summary>
    /// 튜토리얼 페이지의 화면 강조(<see cref="TutorialHighlight"/>). 기존 표시를 빌려 쓰고, 끝나면 원래대로 돌린다(<see cref="Clear"/>).
    /// - 칩 트레이: 모든 칸 테두리를 천천히 깜빡인다(ChipTrayGlow.ShowAll).
    /// - 베팅 점: 판 호버 점(HoverMarker)을 복제해 베팅 자리마다 놓고 살짝 커졌다 작아진다.
    ///   숫자 하나·바깥 베팅 / 선·교차점(안쪽 조합) / 0 칸으로 나눈다. 자리는 클릭 판정과 같은 BoardLayout에서 온다.
    ///   칸이 있는 베팅(숫자 하나·바깥·0)은 호버할 때처럼 칸도 노랗게 칠한다(BetHighlightView). 선·교차점은 칸이 아니라서 점만.
    /// - 빨강 호버 안내: 실제 호버와 같은 툴팁·칸 강조를 빨강 자리에 띄운다(칩 10 기준, 대사의 예와 같다).
    /// - 위험 칸: 해골 칸을 결과 강조처럼 깜빡인다. 카메라는 페이지의 카메라 자리(TutorialCamera)가 옮긴다.
    /// 게임 상태(돈·베팅·회전)는 건드리지 않는다.
    /// </summary>
    public class TutorialHighlighter : MonoBehaviour
    {
        // 빨강 호버 안내의 칩 금액. 대사 "10을 걸어 2배로 당첨되면 20"과 맞춘다.
        private const long ExampleChip = 10;

        [SerializeField] private TableBetBoard board;
        [SerializeField] private TableChipView tableView;
        [SerializeField] private ChipTrayGlow trayGlow;

        [Tooltip("판 호버 칸 강조(노란 칠·테두리). 칸이 있는 베팅 강조에 같이 쓴다.")]
        [SerializeField] private BetHighlightView cellHighlight;

        [Tooltip("복제해서 쓸 베팅 점(판 호버 점).")]
        [SerializeField] private Transform spotTemplate;

        [SerializeField] private RoulettePresenter presenter;
        [SerializeField] private RouletteDefinition definition;
        [SerializeField] private RouletteHud hud;

        [Tooltip("선·교차점 점 크기(호버 점 대비). 자리가 촘촘해서 조금 작게.")]
        [Range(0.3f, 1.5f)]
        [SerializeField] private float lineSpotScale = 0.75f;

        [Tooltip("점이 커졌다 작아지는 주기(초)와 폭.")]
        [SerializeField] private float spotPulsePeriod = 1.2f;
        [Range(0f, 0.6f)]
        [SerializeField] private float spotPulseAmount = 0.25f;

        private readonly List<Transform> spots = new List<Transform>();
        private int activeSpots;
        private float spotScale = 1f;
        private TutorialHighlight current;
        private GameFlowController flow;

        public TutorialHighlight Current => current;

        /// <summary>강조를 바꾼다. 이전 강조는 먼저 지운다. None이면 지우기만.</summary>
        public void Show(TutorialHighlight highlight, GameFlowController game)
        {
            Clear();
            flow = game;
            current = highlight;
            switch (highlight)
            {
                case TutorialHighlight.ChipTray:
                    if (trayGlow != null)
                    {
                        trayGlow.ShowAll();
                    }

                    break;
                case TutorialHighlight.NumberAndOutsideSpots:
                    PlaceSpots(NumberAndOutsideTargets(), 1f);
                    ShowCells(NumberAndOutsideTargets());
                    break;
                case TutorialHighlight.LineAndCornerSpots:
                    PlaceSpots(LineAndCornerTargets(), lineSpotScale);
                    break;
                case TutorialHighlight.ZeroSpot:
                    PlaceSpots(new[] { BetTarget.Straight(0) }, 1f);
                    ShowCells(new[] { BetTarget.Straight(0) });
                    break;
                case TutorialHighlight.RedPayoutTooltip:
                    ShowRedTooltip();
                    break;
                case TutorialHighlight.SkullPocket:
                    if (presenter != null && definition != null)
                    {
                        presenter.BlinkPocket(definition.IndexOfSkull());
                    }

                    break;
            }
        }

        /// <summary>강조를 모두 지우고 빌려 쓴 표시를 원래대로 돌린다(칩 트레이 강조는 다음 화면 갱신에서 고른 칸으로).</summary>
        public void Clear()
        {
            switch (current)
            {
                case TutorialHighlight.ChipTray:
                    if (trayGlow != null)
                    {
                        trayGlow.Hide();
                    }

                    break;
                case TutorialHighlight.RedPayoutTooltip:
                    if (hud != null)
                    {
                        hud.HideBetTooltip();
                    }

                    if (tableView != null)
                    {
                        tableView.SetHover(false, BetTarget.None);
                    }

                    break;
                case TutorialHighlight.SkullPocket:
                    if (presenter != null)
                    {
                        presenter.StopBlink();
                    }

                    break;
            }

            HideSpots();
            if (cellHighlight != null && (current == TutorialHighlight.NumberAndOutsideSpots || current == TutorialHighlight.ZeroSpot))
            {
                cellHighlight.Hide();
            }

            current = TutorialHighlight.None;
        }

        private void LateUpdate()
        {
            switch (current)
            {
                case TutorialHighlight.ChipTray:
                    // 화면 갱신이 고른 칸 강조로 되돌려도 다시 전부 깜빡이게.
                    if (trayGlow != null)
                    {
                        trayGlow.ShowAll();
                    }

                    break;
                case TutorialHighlight.RedPayoutTooltip:
                    MoveRedTooltip();
                    break;
            }

            if (activeSpots > 0)
            {
                float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / Mathf.Max(0.1f, spotPulsePeriod));
                Vector3 scale = spotTemplate.localScale * (spotScale * (1f + spotPulseAmount * wave));
                for (int i = 0; i < activeSpots; i++)
                {
                    spots[i].localScale = scale;
                }
            }
        }

        private void OnDisable()
        {
            Clear();
        }

        // ---------- 베팅 점 ----------

        /// <summary>숫자 하나(0~36)와 바깥 베팅. 선·교차점(안쪽 조합)은 뺀다.</summary>
        private static IEnumerable<BetTarget> NumberAndOutsideTargets()
        {
            for (int number = 0; number <= 36; number++)
            {
                yield return BetTarget.Straight(number);
            }

            BetKind[] outside =
            {
                BetKind.Red, BetKind.Black, BetKind.Odd, BetKind.Even, BetKind.Low, BetKind.High,
                BetKind.Dozen1, BetKind.Dozen2, BetKind.Dozen3, BetKind.Column1, BetKind.Column2, BetKind.Column3,
            };
            foreach (BetKind kind in outside)
            {
                yield return BetTarget.Outside(kind);
            }
        }

        /// <summary>숫자 사이의 선·교차점: 스플릿·스트리트·코너·식스 라인(0과의 조합 포함).</summary>
        private static IEnumerable<BetTarget> LineAndCornerTargets()
        {
            foreach (InsideBetSpot spot in InsideBets.All)
            {
                yield return spot.Target;
            }
        }

        private void PlaceSpots(IEnumerable<BetTarget> targets, float scale)
        {
            if (board == null || spotTemplate == null)
            {
                return;
            }

            spotScale = scale;
            activeSpots = 0;
            Vector3 up = board.transform.up * 0.0008f;
            foreach (BetTarget target in targets)
            {
                Transform spot = SpotAt(activeSpots++);
                spot.position = board.WorldPositionOf(target) + up;
                spot.rotation = spotTemplate.rotation;
                spot.gameObject.SetActive(true);
            }
        }

        private Transform SpotAt(int index)
        {
            while (spots.Count <= index)
            {
                Transform spot = Instantiate(spotTemplate, spotTemplate.parent);
                spot.name = $"TutorialSpot_{spots.Count:000}";

                // 입력 광선에 걸리지 않게 콜라이더는 뺀다(보여 주기만 한다).
                foreach (Collider collider in spot.GetComponentsInChildren<Collider>(true))
                {
                    Destroy(collider);
                }

                spot.gameObject.SetActive(false);
                spots.Add(spot);
            }

            return spots[index];
        }

        private void ShowCells(IEnumerable<BetTarget> targets)
        {
            if (cellHighlight != null)
            {
                cellHighlight.ShowAreas(targets);
            }
        }

        private void HideSpots()
        {
            for (int i = 0; i < activeSpots && i < spots.Count; i++)
            {
                spots[i].gameObject.SetActive(false);
            }

            activeSpots = 0;
        }

        // ---------- 빨강 호버 안내 ----------

        private void ShowRedTooltip()
        {
            if (hud == null || flow == null)
            {
                return;
            }

            BetTarget red = BetTarget.Outside(BetKind.Red);
            BetHoverInfo info = BetHoverInfo.Create(red, flow.BuildSpinConfig(), ExampleChip, flow.Wallet.MaxMoney, true);
            hud.ShowBetTooltip(info, true);
            if (tableView != null)
            {
                tableView.SetHover(true, red, definition);
            }

            MoveRedTooltip();
        }

        private void MoveRedTooltip()
        {
            Camera view = Camera.main;
            if (hud == null || board == null || view == null)
            {
                return;
            }

            Vector3 screen = view.WorldToScreenPoint(board.WorldPositionOf(BetTarget.Outside(BetKind.Red)));
            hud.MoveBetTooltip(new Vector2(screen.x, screen.y));
        }
    }
}
