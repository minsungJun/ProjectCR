using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectCR
{
    /// <summary>
    /// 일시정지 상태 하나(게임 전체). 켜면 timeScale 0과 AudioListener.pause로 회전·로봇·카메라·대기(WaitForSeconds)·소리가 멈춘다.
    /// 화면 시간(unscaled)으로 도는 연출(튜토리얼 대사 출력)은 <see cref="IsPaused"/>를 직접 보고 멈춘다.
    /// 씬이 바뀌면 항상 풀린다(다음 씬이 멈춘 채 시작하지 않게).
    /// </summary>
    public static class GamePause
    {
        private static float resumeTimeScale = 1f;

        public static bool IsPaused { get; private set; }

        /// <summary>켜고 꺼질 때. 인자는 새 상태.</summary>
        public static event Action<bool> Changed;

        public static void Set(bool paused)
        {
            if (IsPaused == paused)
            {
                return;
            }

            IsPaused = paused;
            if (paused)
            {
                resumeTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
                Time.timeScale = 0f;
            }
            else
            {
                Time.timeScale = resumeTimeScale;
            }

            AudioListener.pause = paused;
            Changed?.Invoke(paused);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // 도메인 리로드 없이 플레이에 들어가도(Enter Play Mode 옵션) 이전 상태가 남지 않게.
            IsPaused = false;
            resumeTimeScale = 1f;
            Changed = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single)
            {
                Set(false);
                Time.timeScale = 1f;
            }
        }
    }
}
