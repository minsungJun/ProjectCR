using System.Linq;
using NUnit.Framework;

namespace ProjectCR.Tests
{
    /// <summary>
    /// 칩 트레이: 가장 큰 칩이 납부금의 1/20 이상이 되도록 라운드마다 한 칸씩 위로 밀린다(8칸).
    /// 새 라운드에 지금 칩 단위가 트레이에 없으면 그보다 작은 것 중 가장 큰 칩(없으면 가장 작은 칩)으로 바뀐다.
    /// </summary>
    public class ChipTrayTests
    {
        private TestAssets assets;
        private GameBalance balance;

        [SetUp]
        public void SetUp()
        {
            assets = new TestAssets();
            balance = assets.Balance();
        }

        [TearDown]
        public void TearDown() => assets.DestroyAll();

        [TestCase(1, 1L, 5_000L)]
        [TestCase(5, 1L, 5_000L)]
        [TestCase(6, 5L, 10_000L)]
        [TestCase(7, 10L, 50_000L)]
        [TestCase(8, 50L, 100_000L)]
        [TestCase(9, 100L, 500_000L)]
        [TestCase(10, 1_000L, 5_000_000L)]
        public void Tray_ShiftsUp_WithDue(int round, long lowest, long highest)
        {
            long due = 120L * (long)System.Math.Pow(4, round - 1);
            var tray = balance.TrayChipValues(due);

            Assert.AreEqual(8, tray.Count);
            Assert.AreEqual(lowest, tray[0]);
            Assert.AreEqual(highest, tray[7]);
            CollectionAssert.IsOrdered(tray.ToList());
        }

        [Test]
        public void HugeDue_StopsAtTop()
        {
            var tray = balance.TrayChipValues(999_999_999_999);
            Assert.AreEqual(5_000_000, tray[7], "단위가 모자라면 맨 위에서 멈춘다");
            Assert.AreEqual(8, tray.Count);
        }

        [TestCase(5_000L, 5_000L, "트레이에 있으면 그대로")]
        [TestCase(1L, 5L, "트레이보다 작으면 가장 작은 칩")]
        [TestCase(7L, 5L, "트레이에 없으면 그보다 작은 것 중 가장 큰 칩")]
        public void NewRound_FitsChipValueToTray(long chip, long expected, string because)
        {
            var flow = new GameFlowController(assets.Definition(), balance, 5);
            flow.Wallet.Add(10_000_000);
            flow.SetChipValue(chip);

            // 트레이가 처음으로 한 칸 밀릴 때(1~5,000 → 5~10K)까지 라운드를 넘긴다.
            while (flow.TrayChipValues[0] == 1)
            {
                Assert.IsTrue(flow.ForceRoundEnd());
                Assert.AreEqual(PaymentOutcome.Paid, flow.SubmitPayment());
                Assert.Less(flow.Round.Round, 30);
            }

            Assert.AreEqual(5, flow.TrayChipValues[0]);
            Assert.AreEqual(expected, flow.Bet.ChipValue, because);
        }

        [TestCase("Amount_1", 1L)]
        [TestCase("Amount_5000", 5_000L)]
        [TestCase("Amount_10K", 10_000L)]
        [TestCase("Amount_500k", 500_000L)]
        [TestCase("Amount_5M", 5_000_000L)]
        public void AmountSpriteName_ParsesValue(string name, long expected)
        {
            Assert.IsTrue(TableChipView.TryParseAmount(name, out long amount));
            Assert.AreEqual(expected, amount);
        }

        [TestCase("Amount_")]
        [TestCase("Amount_X")]
        [TestCase("")]
        public void AmountSpriteName_RejectsUnknown(string name)
        {
            Assert.IsFalse(TableChipView.TryParseAmount(name, out _));
        }
    }
}
