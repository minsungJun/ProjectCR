using UnityEngine;
using UnityEngine.Audio;

namespace ProjectCR
{
    /// <summary>
    /// 배경음: 곡들을 번갈아 재생한다(1 → 2 → 1 …, 처음 곡은 무작위). 메인 메뉴와 게임 씬에 하나씩 두지만
    /// 먼저 생긴 하나만 남아 씬을 옮겨도 끊기지 않는다(DontDestroyOnLoad).
    /// 출력은 AudioMixer의 BGM 그룹(설정의 배경음·전체 음량). 일시정지 메뉴(AudioListener.pause)에서도 음악은 계속 흐른다.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class BgmPlayer : MonoBehaviour
    {
        [Tooltip("번갈아 재생할 곡.")]
        [SerializeField] private AudioClip[] tracks = new AudioClip[0];

        [Tooltip("출력 믹서 그룹(BGM).")]
        [SerializeField] private AudioMixerGroup output;

        [Tooltip("곡 사이 쉬는 시간(초).")]
        [Min(0f)]
        [SerializeField] private float gap = 1.5f;

        private static BgmPlayer instance;
        private AudioSource source;
        private int current = -1;
        private float waitLeft;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.ignoreListenerPause = true;
            source.outputAudioMixerGroup = output;
        }

        private void Start()
        {
            if (instance == this && tracks.Length > 0)
            {
                PlayNext(Random.Range(0, tracks.Length));
            }
        }

        private void Update()
        {
            if (instance != this || tracks.Length == 0 || source.isPlaying)
            {
                return;
            }

            // 곡이 끝나면 잠깐 쉬고 다음 곡. 창이 포커스를 잃어 멈춘 동안에는 넘기지 않는다.
            waitLeft -= Time.unscaledDeltaTime;
            if (waitLeft <= 0f && Application.isFocused)
            {
                PlayNext((current + 1) % tracks.Length);
            }
        }

        private void PlayNext(int index)
        {
            current = index;
            waitLeft = gap;
            source.clip = tracks[index];
            if (source.clip != null)
            {
                source.Play();
            }
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }
    }
}
