using UnityEngine;

namespace ProjectCR
{
    /// <summary>
    /// 실행 시작 때 GameSettings가 만드는 숨은 오브젝트. 첫 프레임에 저장된 음량을 AudioMixer에 넣고 사라진다
    /// (AudioMixer.SetFloat는 첫 프레임 전에는 먹지 않는다).
    /// </summary>
    public class GameSettingsApplier : MonoBehaviour
    {
        private void Start()
        {
            GameSettings.ApplyAudio();
            Destroy(gameObject);
        }
    }
}
