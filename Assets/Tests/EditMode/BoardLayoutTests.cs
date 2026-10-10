using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ProjectCR.Tests
{
    /// <summary>
    /// 3D 테이블 베팅판 좌표 ↔ 베팅 대상. 값은 모델에서 잰 것과 비슷하게 둔다
    /// (0번 칸 −1.03, 열 베팅 끝 12.79, 더즌 줄 아래 3.92, 2배 줄 아래 4.85, 칸 단위).
    /// </summary>
    public class BoardLayoutTests
    {
        private static readonly BoardLayout Layout = new BoardLayout
        {
            zeroLeft = -1.03f,
            columnRight = 12.79f,
            dozenBottom = 3.92f,
            outsideBottom = 4.85f,
            spotRadiusX = 0.22f,
            spotRadiusY = 0.17f,
        };

        private static BetTarget Hit(float x, float y)
        {
            Layout.TryGetTarget(new Vector2(x, y), out BetTarget target);
            return target;
        }

        private static IEnumerable<BetTarget> AllTargets()
        {
            for (int n = 0; n <= 36; n++)
            {
                yield return BetTarget.Straight(n);
            }

            foreach (InsideBetSpot spot in InsideBets.All)
            {
                yield return spot.Target;
            }

            for (BetKind kind = BetKind.Red; kind <= BetKind.Column3; kind++)
            {
                yield return BetTarget.Outside(kind);
            }
        }

        [Test]
        public void EveryTarget_RoundTripsThroughItsChipPosition()
        {
            int count = 0;
            foreach (BetTarget target in AllTargets())
            {
                Vector2 units = Layout.UnitsOf(target);
                Assert.IsTrue(Layout.TryGetTarget(units, out BetTarget hit), target.ToString());
                Assert.AreEqual(target, hit, $"{target} @ {units}");
                count++;
            }

            Assert.AreEqual(37 + 108 + 12, count);
        }

        [Test]
        public void CellCenters_AreStraightBets_InTableLayout()
        {
            Assert.AreEqual(BetTarget.Straight(3), Hit(0.5f, 0.5f), "위 줄 첫 칸 = 3");
            Assert.AreEqual(BetTarget.Straight(1), Hit(0.5f, 2.5f), "아래 줄 첫 칸 = 1");
            Assert.AreEqual(BetTarget.Straight(36), Hit(11.5f, 0.5f));
            Assert.AreEqual(BetTarget.Straight(34), Hit(11.5f, 2.5f));
            Assert.AreEqual(BetTarget.Straight(0), Hit(-0.5f, 1.5f));
        }

        [Test]
        public void NearBoundary_PicksInsideBet_FartherPicksCell()
        {
            Assert.AreEqual(BetTarget.Inside(BetKind.Split, 2, 3), Hit(0.5f, 0.9f), "2·3 경계 근처");
            Assert.AreEqual(BetTarget.Straight(3), Hit(0.5f, 0.75f), "경계에서 0.25칸 떨어지면 칸");
            Assert.AreEqual(BetTarget.Inside(BetKind.Corner, 1, 2, 4, 5), Hit(1.05f, 2.05f));
            Assert.AreEqual(BetTarget.Inside(BetKind.Street, 4, 5, 6), Hit(1.5f, 3.05f), "줄 아랫변 = 스트리트");
            Assert.AreEqual(BetTarget.Inside(BetKind.SixLine, 1, 2, 3, 4, 5, 6), Hit(1.0f, 3.0f));
            Assert.AreEqual(BetTarget.Inside(BetKind.Corner, 0, 1, 2, 3), Hit(0.0f, 3.0f), "퍼스트 포");
            Assert.AreEqual(BetTarget.Inside(BetKind.Split, 0, 2), Hit(0.05f, 1.5f));
        }

        [Test]
        public void OutsideAreas_MapToOutsideBets()
        {
            Assert.AreEqual(BetTarget.Outside(BetKind.Column3), Hit(12.4f, 0.5f));
            Assert.AreEqual(BetTarget.Outside(BetKind.Column1), Hit(12.4f, 2.5f));
            Assert.AreEqual(BetTarget.Outside(BetKind.Dozen1), Hit(2f, 3.5f));
            Assert.AreEqual(BetTarget.Outside(BetKind.Dozen3), Hit(10f, 3.5f));
            Assert.AreEqual(BetTarget.Outside(BetKind.Low), Hit(1f, 4.4f));
            Assert.AreEqual(BetTarget.Outside(BetKind.Even), Hit(3f, 4.4f));
            Assert.AreEqual(BetTarget.Outside(BetKind.Red), Hit(5f, 4.4f));
            Assert.AreEqual(BetTarget.Outside(BetKind.Black), Hit(7f, 4.4f));
            Assert.AreEqual(BetTarget.Outside(BetKind.Odd), Hit(9f, 4.4f));
            Assert.AreEqual(BetTarget.Outside(BetKind.High), Hit(11f, 4.4f));
        }

        // ---------- 호버 강조용 사각형 ----------

        [Test]
        public void CellRects_MatchClickTargets()
        {
            for (int n = 0; n <= 36; n++)
            {
                Rect cell = Layout.CellOf(n);
                Assert.AreEqual(BetTarget.Straight(n), Hit(cell.center.x, cell.center.y), $"{n} 가운데");

                // 경계 근처(안쪽 베팅 점 바깥)도 같은 칸.
                Vector2 nearCorner = cell.min + new Vector2(cell.width * 0.3f, cell.height * 0.3f);
                Assert.AreEqual(BetTarget.Straight(n), Hit(nearCorner.x, nearCorner.y), $"{n} 안쪽 구석");
            }
        }

        [Test]
        public void OutsideAreas_MatchClickTargets()
        {
            for (BetKind kind = BetKind.Red; kind <= BetKind.Column3; kind++)
            {
                BetTarget target = BetTarget.Outside(kind);
                Assert.IsTrue(Layout.TryGetOutsideArea(target, out Rect area), kind.ToString());
                Assert.AreEqual(target, Hit(area.center.x, area.center.y), kind.ToString());
                // 구석은 안쪽 베팅 점(반경 0.22 × 0.17칸) 밖으로 조금 들어간 곳. 점과 겹치면 기존 우선순위대로 안쪽 베팅이 이긴다.
                Assert.AreEqual(target, Hit(area.xMin + 0.25f, area.yMin + 0.2f), $"{kind} 왼쪽 위 안");
                Assert.AreEqual(target, Hit(area.xMax - 0.25f, area.yMax - 0.2f), $"{kind} 오른쪽 아래 안");
            }

            Assert.IsFalse(Layout.TryGetOutsideArea(BetTarget.Straight(5), out _));
            Assert.IsFalse(Layout.TryGetOutsideArea(BetTarget.Inside(BetKind.Split, 2, 3), out _));
        }

        [Test]
        public void InsideSpot_TouchesEveryCoveredCell()
        {
            // 강조하는 칸(포함 숫자)이 클릭 점을 감싼다: 포함 칸들을 합친 사각형 안(경계 포함)에 클릭 점이 있다.
            // 스트리트·식스라인 점은 줄 아랫변에 있어서, 점의 가로 위치가 각 칸의 열 범위 안인지도 본다(0이 든 트리오는 제외).
            foreach (InsideBetSpot spot in InsideBets.All)
            {
                var point = new Vector2(spot.X, spot.Y);
                Rect union = Layout.CellOf(spot.Target.Numbers.First());
                foreach (int n in spot.Target.Numbers)
                {
                    Rect cell = Layout.CellOf(n);
                    union = Rect.MinMaxRect(Mathf.Min(union.xMin, cell.xMin), Mathf.Min(union.yMin, cell.yMin),
                        Mathf.Max(union.xMax, cell.xMax), Mathf.Max(union.yMax, cell.yMax));
                    bool lineBet = spot.Target.Kind == BetKind.Street || spot.Target.Kind == BetKind.SixLine;
                    if (!lineBet || n == 0)
                    {
                        continue;
                    }

                    Assert.IsTrue(point.x >= cell.xMin - 1e-4f && point.x <= cell.xMax + 1e-4f, $"{spot.Target}의 {n} 칸 열");
                }

                var grown = new Rect(union.x - 1e-4f, union.y - 1e-4f, union.width + 2e-4f, union.height + 2e-4f);
                Assert.IsTrue(grown.Contains(point), $"{spot.Target} 점이 강조 영역 밖");
            }
        }

        [Test]
        public void NoPointOnBoard_TargetsTheSkull()
        {
            // 판에는 해골 칸이 없다: 판 전체를 훑어도 숫자(0~36)·안쪽·바깥 베팅만 나온다.
            for (float x = -1.2f; x < 13f; x += 0.05f)
            {
                for (float y = -0.2f; y < 5f; y += 0.05f)
                {
                    BetTarget target = Hit(x, y);
                    if (target.IsNone)
                    {
                        continue;
                    }

                    bool valid = target.Kind == BetKind.Straight ? target.Number >= 0 && target.Number <= 36
                        : target.IsInside ? InsideBets.IsValid(target)
                        : Layout.TryGetOutsideArea(target, out _);
                    Assert.IsTrue(valid, $"{target} @ ({x}, {y})");
                }
            }
        }

        [Test]
        public void OffBoard_IsNothing()
        {
            Assert.IsTrue(Hit(-2f, 1f).IsNone, "0번 칸 왼쪽 밖");
            Assert.IsTrue(Hit(13.5f, 1f).IsNone, "열 베팅 오른쪽 밖");
            Assert.IsTrue(Hit(5f, 5.5f).IsNone, "2배 줄 아래 밖");
            Assert.IsTrue(Hit(12.5f, 4f).IsNone, "열 베팅 아래 빈 곳");
            Assert.IsTrue(Hit(-0.5f, 3.5f).IsNone, "0번 칸 아래 빈 곳");
            Assert.IsTrue(Hit(5f, -0.5f).IsNone, "번호판 위 밖");
        }
    }
}
