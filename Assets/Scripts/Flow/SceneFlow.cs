using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectCR
{
    /// <summary>
    /// 씬 이동(메인 메뉴 ↔ 게임)과 종료. 이동은 한 번만 시작된다(버튼 연타로 두 번 불러도 무시).
    /// 이동 전에 일시정지를 풀어 다음 씬이 멈춘 채 시작하지 않게 한다.
    /// 새 게임은 새 Roulette 씬을 여는 것으로 초기화된다(RouletteGame.Start → StartNewRun). 튜토리얼 기록은 건드리지 않는다.
    /// </summary>
    public static class SceneFlow
    {
        public const string MainMenuScene = "MainMenu";
        public const string GameScene = "Roulette";

        private static bool loading;

        public static bool IsLoading => loading;

        public static void LoadMainMenu() => Load(MainMenuScene);

        public static void LoadGame() => Load(GameScene);

        public static void Quit()
        {
            if (loading)
            {
                return;
            }

            loading = true;
            GamePause.Set(false);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private static void Load(string scene)
        {
            if (loading)
            {
                return;
            }

            loading = true;
            GamePause.Set(false);
            SceneManager.LoadScene(scene, LoadSceneMode.Single);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            loading = false;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            loading = false;
        }
    }
}
