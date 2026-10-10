using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace ProjectCR.Editor
{
    /// <summary>
    /// 소리 연결(여러 번 실행해도 같은 결과):
    /// - BGM(Audio/BGM의 곡 전부, 이름순): MainMenu·Roulette 씬에 BgmPlayer(출력 BGM 그룹). 긴 곡이라 스트리밍으로 임포트.
    /// - 효과음: Roulette 씬에 GameSfx(출력 SFX 그룹) — Chip/Bet_Chip(베팅), Chip/Grap_Chip_1·2(칩 고르기), Roulette/Roulette_ball_1(공 굴러감).
    ///   Roulette 폴더의 다른 소리는 쓰지 않는다.
    /// 열려 있지 않은 씬은 잠깐 더해서 고치고 닫는다. 저장 안 한 변경이 있던 씬은 저장하지 않는다.
    /// </summary>
    public static class AudioSetup
    {
        private const string MixerPath = "Assets/Resources/Audio/MainMixer.mixer";
        private const string BgmFolder = "Assets/Audio/BGM";
        private const string BetChip = "Assets/Audio/Chip/Bet_Chip.wav";
        private static readonly string[] GrabChips = { "Assets/Audio/Chip/Grap_Chip_1.wav", "Assets/Audio/Chip/Grap_Chip_2.wav" };
        private const string BallRoll = "Assets/Audio/Roulette/Roulette_ball_1.wav";
        private static readonly string[] Scenes = { "Assets/Scenes/MainMenu.unity", "Assets/Scenes/Roulette.unity" };

        [MenuItem("Tools/ProjectCR/Audio/Setup")]
        public static void SetupMenu()
        {
            Debug.Log("[AudioSetup]\n" + Setup());
        }

        public static string Setup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return "플레이 모드에서는 실행하지 않는다.";
            }

            var log = new StringBuilder();
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            AudioMixerGroup bgmGroup = mixer != null ? mixer.FindMatchingGroups("BGM").FirstOrDefault() : null;
            AudioMixerGroup sfxGroup = mixer != null ? mixer.FindMatchingGroups("SFX").FirstOrDefault() : null;
            if (bgmGroup == null || sfxGroup == null)
            {
                return $"{MixerPath}에 BGM/SFX 그룹이 없다. Tools/ProjectCR/Menus/Setup을 먼저 실행.";
            }

            AudioClip[] tracks = AssetDatabase.FindAssets("t:AudioClip", new[] { BgmFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p)
                .Select(p =>
                {
                    StreamBgm(p, log);
                    return AssetDatabase.LoadAssetAtPath<AudioClip>(p);
                })
                .ToArray();

            foreach (string path in Scenes)
            {
                Scene scene = SceneManager.GetSceneByPath(path);
                bool opened = !scene.isLoaded;
                if (opened)
                {
                    scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                }

                bool wasDirty = scene.isDirty;
                WireBgm(scene, tracks, bgmGroup, log);
                if (path.EndsWith("Roulette.unity"))
                {
                    WireSfx(scene, sfxGroup, log);
                }

                EditorSceneManager.MarkSceneDirty(scene);
                if (!wasDirty)
                {
                    EditorSceneManager.SaveScene(scene);
                }
                else
                {
                    log.AppendLine($"  {scene.name}: 저장 안 한 변경이 있어 저장하지 않음");
                }

                if (opened)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }

            return log.ToString();
        }

        /// <summary>몇 분짜리 곡은 메모리에 풀어 두지 않고 스트리밍한다.</summary>
        private static void StreamBgm(string path, StringBuilder log)
        {
            var importer = (AudioImporter)AssetImporter.GetAtPath(path);
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            if (settings.loadType == AudioClipLoadType.Streaming)
            {
                return;
            }

            settings.loadType = AudioClipLoadType.Streaming;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
            log.AppendLine($"  임포트: {System.IO.Path.GetFileName(path)} 스트리밍");
        }

        private static void WireBgm(Scene scene, AudioClip[] tracks, AudioMixerGroup group, StringBuilder log)
        {
            GameObject go = FindRoot(scene, "BGM") ?? CreateRoot(scene, "BGM");
            GetOrAdd<AudioSource>(go);
            var so = new SerializedObject(GetOrAdd<BgmPlayer>(go));
            SerializedProperty list = so.FindProperty("tracks");
            list.arraySize = tracks.Length;
            for (int i = 0; i < tracks.Length; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = tracks[i];
            }

            so.FindProperty("output").objectReferenceValue = group;
            so.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine($"  {scene.name}: BGM {tracks.Length}곡 번갈아 재생({string.Join(", ", tracks.Select(t => t.name))})");
        }

        private static void WireSfx(Scene scene, AudioMixerGroup group, StringBuilder log)
        {
            GameObject go = FindRoot(scene, "SFX") ?? CreateRoot(scene, "SFX");
            GameSfx sfx = GetOrAdd<GameSfx>(go);
            var so = new SerializedObject(sfx);
            so.FindProperty("output").objectReferenceValue = group;
            so.FindProperty("betChip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(BetChip);
            SerializedProperty grabs = so.FindProperty("grabChips");
            grabs.arraySize = GrabChips.Length;
            for (int i = 0; i < GrabChips.Length; i++)
            {
                grabs.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(GrabChips[i]);
            }

            so.FindProperty("ballRoll").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(BallRoll);
            so.ApplyModifiedPropertiesWithoutUndo();

            RouletteGame game = scene.GetRootGameObjects().Select(r => r.GetComponentInChildren<RouletteGame>(true)).FirstOrDefault(g => g != null);
            if (game != null)
            {
                var gameSo = new SerializedObject(game);
                gameSo.FindProperty("sfx").objectReferenceValue = sfx;
                gameSo.ApplyModifiedPropertiesWithoutUndo();
            }

            log.AppendLine($"  {scene.name}: SFX(칩 베팅·칩 고르기 2종·공 굴러감) 연결{(game != null ? ", RouletteGame 연결" : " — RouletteGame 없음")}");
        }

        private static GameObject FindRoot(Scene scene, string name) => scene.GetRootGameObjects().FirstOrDefault(g => g.name == name);

        private static GameObject CreateRoot(Scene scene, string name)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        // GetComponent가 돌려주는 빈 컴포넌트는 C# null이 아니라서 ??를 쓰지 않는다.
        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T component = go.GetComponent<T>();
            return component != null ? component : go.AddComponent<T>();
        }
    }
}
