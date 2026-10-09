using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ProjectCR.Tests
{
    /// <summary>
    /// 튜토리얼 대상 판정(최초로 시작한 게임만, 안내마다 한 번, 결말이 나면 남은 일반 안내 취소),
    /// 대사 데이터({dealer} 치환, 이름 없으면 "딜러"), 게임 클리어(마지막 라운드 납부 성공).
    /// </summary>
    public class TutorialTests
    {
        private sealed class MemoryStore : ITutorialStore
        {
            public bool HasStarted { get; private set; }
            public void MarkStarted() => HasStarted = true;
            public void Clear() => HasStarted = false;
        }

        // ---------- 대상 판정 ----------

        [Test]
        public void FirstRunOnly_LaterRunsAreNotTutorial()
        {
            var store = new MemoryStore();
            TutorialProgress first = TutorialProgress.BeginRun(store);
            Assert.IsTrue(first.IsTutorialRun);
            Assert.IsTrue(store.HasStarted, "시작하는 순간 저장한다(도중에 꺼도 다시 나오지 않는다)");

            TutorialProgress second = TutorialProgress.BeginRun(store);
            Assert.IsFalse(second.IsTutorialRun, "새 게임·다시 시작에서는 반복하지 않는다");
            Assert.IsFalse(second.TryBegin(TutorialStep.Intro));
            Assert.IsFalse(second.TryBegin(TutorialStep.Drink));

            Assert.IsTrue(TutorialProgress.BeginRun(store, force: true).IsTutorialRun, "개발용 강제 표시");
            store.Clear();
            Assert.IsTrue(TutorialProgress.BeginRun(store).IsTutorialRun, "저장 초기화 후에는 다시 대상");
        }

        [Test]
        public void EachGuide_ShowsOncePerRun_AndIntroDoesNotEndTheRest()
        {
            TutorialProgress run = TutorialProgress.BeginRun(new MemoryStore());
            Assert.IsTrue(run.TryBegin(TutorialStep.Intro));
            Assert.IsFalse(run.TryBegin(TutorialStep.Intro), "같은 안내는 한 번만");

            Assert.IsTrue(run.ShouldShow(TutorialStep.Drink), "도입이 끝나도 음료·서비스 안내는 남는다");
            Assert.IsTrue(run.TryBegin(TutorialStep.Drink));
            Assert.IsFalse(run.ShouldShow(TutorialStep.Drink), "다음 라운드부터는 음료 안내 없음");
            Assert.IsTrue(run.TryBegin(TutorialStep.Service));
            Assert.IsTrue(run.TryBegin(TutorialStep.Ending));
            Assert.IsFalse(run.TryBegin(TutorialStep.Ending), "결말은 실제 난 하나만");
        }

        [Test]
        public void Ending_CancelsPendingGuides()
        {
            TutorialProgress run = TutorialProgress.BeginRun(new MemoryStore());
            run.TryBegin(TutorialStep.Intro);
            run.CancelPendingGuides();
            Assert.IsFalse(run.ShouldShow(TutorialStep.Drink));
            Assert.IsFalse(run.ShouldShow(TutorialStep.Service));
            Assert.IsTrue(run.ShouldShow(TutorialStep.Ending));
        }

        [Test]
        public void NoneProgress_ShowsNothing()
        {
            Assert.IsFalse(TutorialProgress.None.TryBegin(TutorialStep.Intro));
        }

        // ---------- 대사 데이터 ----------

        [Test]
        public void Data_ReplacesDealerToken_AndFallsBackWhenNameEmpty()
        {
            var data = ScriptableObject.CreateInstance<TutorialDialogueData>();
            try
            {
                var empty = new TutorialPage[0];
                data.Set("**", empty, empty, empty, empty, empty, empty, empty, empty);
                Assert.AreEqual("이곳의 딜러 **입니다.", data.Format("이곳의 딜러 {dealer}입니다."));
                Assert.AreEqual("**", data.DealerName);

                data.Set("  ", empty, empty, empty, empty, empty, empty, empty, empty);
                Assert.AreEqual(TutorialDialogueData.FallbackDealerName, data.DealerName);
                Assert.AreEqual("행복한 삶이었기를…", data.Format("행복한 삶이었기를…"), "문장부호·줄임표는 그대로");
            }
            finally
            {
                Object.DestroyImmediate(data);
            }
        }

        // ---------- 게임 클리어 ----------

        private TestAssets assets;

        [SetUp]
        public void SetUp() => assets = new TestAssets();

        [TearDown]
        public void TearDown() => assets.DestroyAll();

        private GameBalance BalanceWithFinalRound(int finalRound)
        {
            GameBalance balance = assets.Balance();
            typeof(GameBalance).GetField("finalRound", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(balance, finalRound);
            return balance;
        }

        /// <summary>남은 회전을 모두 1원씩 걸고 낙첨시켜 납부 판정 상태로 만든다.</summary>
        private static void FinishRound(GameFlowController flow)
        {
            while (flow.Phase == GamePhase.Betting)
            {
                flow.ForcedNumbers = new[] { 8 };
                Assert.AreEqual(BetError.None, flow.PlaceBet(BetTarget.Straight(7), 1));
                Assert.AreEqual(BetError.None, flow.TryBeginSpin(out ActiveSpin spin));
                Assert.IsTrue(flow.CompleteSpin(spin.Id, out _));
            }

            Assert.AreEqual(GamePhase.RoundPayment, flow.Phase);
        }

        [Test]
        public void DefaultFinalRound_IsTen()
        {
            Assert.AreEqual(10, assets.Balance().FinalRound);
        }

        [Test]
        public void PayingFinalRound_ClearsTheGame()
        {
            var flow = new GameFlowController(assets.Definition(), BalanceWithFinalRound(2), 7);
            flow.Wallet.Add(100_000);

            FinishRound(flow);
            Assert.AreEqual(PaymentOutcome.Paid, flow.SubmitPayment());
            Assert.AreEqual(GamePhase.Betting, flow.Phase, "음료 목록이 없으면 바로 다음 라운드");
            Assert.AreEqual(2, flow.Round.Round);

            FinishRound(flow);
            long before = flow.Wallet.Balance;
            Assert.AreEqual(PaymentOutcome.Paid, flow.SubmitPayment());
            Assert.AreEqual(GamePhase.GameClear, flow.Phase, "마지막 라운드 납부 성공 = 클리어");
            Assert.AreEqual(2, flow.Round.Round, "다음 라운드로 넘어가지 않는다");
            Assert.AreEqual(before - flow.LastPaidDue, flow.Wallet.Balance, "납부금은 한 번만 빠진다");
            Assert.IsNull(flow.SubmitPayment(), "클리어 뒤에는 다시 납부하지 않는다");
            Assert.AreNotEqual(BetError.None, flow.PlaceChip(BetTarget.Straight(7)), "클리어 뒤에는 베팅하지 않는다");
        }

        [Test]
        public void FailingFinalRound_IsGameOver()
        {
            var flow = new GameFlowController(assets.Definition(), BalanceWithFinalRound(1), 7);
            FinishRound(flow);
            Assert.AreEqual(PaymentOutcome.Insufficient, flow.SubmitPayment(), "초기 100 - 5 < 납부금 120");
            Assert.AreEqual(GamePhase.GameOver, flow.Phase);
        }

        [Test]
        public void FinalRoundZero_IsEndless()
        {
            var flow = new GameFlowController(assets.Definition(), BalanceWithFinalRound(0), 7);
            flow.Wallet.Add(100_000);
            FinishRound(flow);
            Assert.AreEqual(PaymentOutcome.Paid, flow.SubmitPayment());
            Assert.AreEqual(GamePhase.Betting, flow.Phase);
            Assert.AreEqual(2, flow.Round.Round);
        }
    }
}
