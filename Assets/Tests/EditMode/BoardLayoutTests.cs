using System.Collections.Generic;
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
