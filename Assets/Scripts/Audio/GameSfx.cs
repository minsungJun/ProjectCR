using UnityEngine;
using UnityEngine.Audio;

namespace ProjectCR
{
    /// <summary>
    /// 게임 효과음(Roulette 씬). 출력은 AudioMixer의 SFX 그룹(설정의 효과음·전체 음량). 일시정지(AudioListener.pause) 동안 함께 멈춘다.
    /// - 칩 베팅: 판에 칩을 놓았을 때 Bet_Chip.
    /// - 칩 고르기: 트레이에서 칩 금액을 골랐을 때 Grap_Chip_1·2 중 하나(바로 전과 다른 소리).
    /// - 공 굴러감: 회전 연출이 시작될 때 Roulette_ball_1 한 번(공이 여러 개여도 한 번). 새 회전·새 게임이면 끊고 다시.
    /// </summary>
    public class GameSfx : MonoBehaviour
    {
        [SerializeField] private AudioMixerGroup output;
        [SerializeField] private AudioClip betChip;
        [SerializeField] private AudioClip[] grabChips = new AudioClip[0];
        [SerializeField] private AudioClip ballRoll;

        private AudioSource oneShots;
        private AudioSource ball;
        private int lastGrab = -1;

        private void Awake()
        {
            oneShots = CreateSource("OneShots");
            ball = CreateSource("Ball");
        }

        private AudioSource CreateSource(string name)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.outputAudioMixerGroup = output;
            return source;
        }

        public void PlayBetChip() => PlayOneShot(betChip);

        public void PlayGrabChip()
        {
            if (grabChips.Length == 0)
            {
                return;
            }

            // 같은 소리가 연달아 나지 않게 바로 전과 다른 것을 고른다.
            int index = Random.Range(0, grabChips.Length);
            if (grabChips.Length > 1 && index == lastGrab)
            {
                index = (index + 1) % grabChips.Length;
            }

            lastGrab = index;
            PlayOneShot(grabChips[index]);
        }

        public void PlayBallRoll()
        {
            if (ballRoll == null || ball == null)
            {
                return;
            }

            ball.Stop();
            ball.clip = ballRoll;
            ball.Play();
        }

        public void StopBallRoll()
        {
            if (ball != null)
            {
                ball.Stop();
            }
        }

        private void PlayOneShot(AudioClip clip)
        {
            if (clip != null && oneShots != null)
            {
                oneShots.PlayOneShot(clip);
            }
        }
    }
}
