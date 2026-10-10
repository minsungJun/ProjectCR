using UnityEngine;

namespace ProjectCR
{
    /// <summary>튜토리얼 안내 묶음. 한 게임에서 각각 한 번만 나온다.</summary>
    public enum TutorialStep
    {
        Intro,
        Drink,
        Service,
        Ending,
    }

    /// <summary>
    /// "튜토리얼을 이미 시작했다"와 "음료·서비스 안내를 본 적 있다"를 게임 밖에 기억하는 곳. 기본은 PlayerPrefs, 테스트는 메모리.
    /// </summary>
    public interface ITutorialStore
    {
        bool HasStarted { get; }
        void MarkStarted();

        /// <summary>이 안내를 (어느 게임에서든) 본 적 있다.</summary>
        bool HasSeen(TutorialStep step);

        void MarkSeen(TutorialStep step);

        /// <summary>시작함·본 안내를 모두 지운다.</summary>
        void Clear();
    }

    /// <summary>PlayerPrefs 저장(버전이 붙은 키). 대사·순서를 크게 바꾸면 키 버전을 올려 다시 보이게 한다.</summary>
    public sealed class PlayerPrefsTutorialStore : ITutorialStore
    {
        public const string Key = "ProjectCR.Tutorial.v1.Started";

        /// <summary>안내별 "본 적 있음" 키(ProjectCR.Tutorial.v1.Seen.Drink 등).</summary>
        public static string SeenKey(TutorialStep step) => $"ProjectCR.Tutorial.v1.Seen.{step}";

        public bool HasStarted => PlayerPrefs.GetInt(Key, 0) == 1;

        public void MarkStarted()
        {
            PlayerPrefs.SetInt(Key, 1);
            PlayerPrefs.Save();
        }

        public bool HasSeen(TutorialStep step) => PlayerPrefs.GetInt(SeenKey(step), 0) == 1;

        public void MarkSeen(TutorialStep step)
        {
            PlayerPrefs.SetInt(SeenKey(step), 1);
            PlayerPrefs.Save();
        }

        public void Clear()
        {
            PlayerPrefs.DeleteKey(Key);
            foreach (TutorialStep step in (TutorialStep[])System.Enum.GetValues(typeof(TutorialStep)))
            {
                PlayerPrefs.DeleteKey(SeenKey(step));
            }

            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// 이번 게임이 튜토리얼 대상인지와 안내별 완료 상태.
    /// 도입·결말은 최초로 시작한 게임 하나만 대상이다: 시작하는 순간 "시작함"을 저장해서, 그 뒤의 새 게임·다시 시작은 대상이 아니다
    /// (도중에 꺼도 다시 나오지 않는다).
    /// 음료·서비스 안내는 게임과 상관없이 <b>처음 그 자리에 왔을 때</b> 나온다: 본 안내를 따로 저장해서,
    /// 첫 게임에서 음료까지 못 가고 끝났어도 다음 게임에서 처음 음료를 고를 때 나온다. 본 뒤로는 다시 나오지 않는다.
    /// 이어하기 저장이 없어서 게임 안 진행 상태는 저장하지 않는다.
    /// </summary>
    public sealed class TutorialProgress
    {
        private readonly bool[] done = new bool[4];
        private readonly ITutorialStore store;

        private TutorialProgress(bool isTutorialRun, ITutorialStore store)
        {
            IsTutorialRun = isTutorialRun;
            this.store = store;
        }

        /// <summary>이번 게임에서 튜토리얼을 보여 준다.</summary>
        public bool IsTutorialRun { get; }

        /// <summary>
        /// 새 게임 하나의 상태를 만든다. 처음이면(또는 force) 대상으로 하고 "시작함"을 바로 저장한다.
        /// </summary>
        public static TutorialProgress BeginRun(ITutorialStore store, bool force = false)
        {
            bool first = force || store == null || !store.HasStarted;
            if (first && store != null)
            {
                store.MarkStarted();
            }

            return new TutorialProgress(first, store);
        }

        /// <summary>튜토리얼이 꺼진 게임(기본값, 컨트롤러가 없을 때 등).</summary>
        public static TutorialProgress None => new TutorialProgress(false, null);

        /// <summary>처음 그 자리에 왔을 때 나오는 안내(음료·서비스). 게임과 상관없이 한 번 보면 끝.</summary>
        public static bool IsFirstTimeGuide(TutorialStep step) => step == TutorialStep.Drink || step == TutorialStep.Service;

        public bool IsDone(TutorialStep step) => done[(int)step];

        /// <summary>
        /// 이번 게임에서 아직 안 나왔고, 대상 게임이거나 (음료·서비스 안내면) 한 번도 본 적 없으면 true.
        /// </summary>
        public bool ShouldShow(TutorialStep step)
        {
            if (IsDone(step))
            {
                return false;
            }

            return IsTutorialRun || (IsFirstTimeGuide(step) && store != null && !store.HasSeen(step));
        }

        /// <summary>보여 주기로 하고 완료로 적는다(음료·서비스는 "본 적 있음"도 저장). 보여 줄 수 없으면 false(아무것도 바뀌지 않는다).</summary>
        public bool TryBegin(TutorialStep step)
        {
            if (!ShouldShow(step))
            {
                return false;
            }

            done[(int)step] = true;
            if (IsFirstTimeGuide(step) && store != null)
            {
                store.MarkSeen(step);
            }

            return true;
        }

        /// <summary>결말이 나면 아직 안 나온 일반 안내(음료·서비스)는 취소한다.</summary>
        public void CancelPendingGuides()
        {
            done[(int)TutorialStep.Drink] = true;
            done[(int)TutorialStep.Service] = true;
        }
    }
}
