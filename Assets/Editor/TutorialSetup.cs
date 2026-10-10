using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectCR.Editor
{
    /// <summary>
    /// 딜러 튜토리얼 준비: 딜러 아이콘 PNG를 스프라이트로 → 씬에 Tutorial 오브젝트(TutorialController + TutorialDialogueView + TutorialHighlighter)를 두고
    /// HUD UIDocument·대사 데이터·아이콘·강조에 쓸 표시를 연결 → RouletteGame에 연결. 여러 번 실행해도 같은 결과다.
    /// 대사는 Assets/Data/Tutorial/TutorialDialogue.asset에 있다(인스펙터에서 고친다).
    /// </summary>
    public static class TutorialSetup
    {
        private const string DataPath = "Assets/Data/Tutorial/TutorialDialogue.asset";
        private const string IconPath = "Assets/Art/Image/Icon_removebg.png";
        private const string ObjectName = "Tutorial";

        [MenuItem("Tools/ProjectCR/Tutorial/Setup Scene")]
        public static void SetupMenu()
        {
            Debug.Log("[Tutorial] 준비\n" + Setup());
        }

        [MenuItem("Tools/ProjectCR/Tutorial/Reset Saved Progress")]
        public static void ResetMenu()
        {
            new PlayerPrefsTutorialStore().Clear();
            Debug.Log($"[Tutorial] PlayerPrefs '{PlayerPrefsTutorialStore.Key}'를 지웠다. 다음 새 게임부터 튜토리얼이 다시 나온다.");
        }

        public static string Setup()
        {
            var log = new StringBuilder();
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return "플레이 모드에서는 실행하지 않는다.";
            }

            Sprite icon = ConfigureIcon(log);
            var data = AssetDatabase.LoadAssetAtPath<TutorialDialogueData>(DataPath);
            if (data == null)
            {
                log.AppendLine($"  대사 데이터 {DataPath} 없음 → 연결하지 않음");
            }

            var scene = EditorSceneManager.GetActiveScene();
            bool wasDirty = scene.isDirty;
            var game = Object.FindFirstObjectByType<RouletteGame>();
            var hud = Object.FindFirstObjectByType<RouletteHud>();
            if (game == null || hud == null)
            {
                log.AppendLine($"  씬 {scene.name}: RouletteGame 또는 RouletteHud 없음 → 연결하지 않음");
                return log.ToString();
            }

            GameObject go = GameObject.Find(ObjectName);
            if (go == null)
            {
                go = new GameObject(ObjectName);
            }

            TutorialDialogueView view = GetOrAdd<TutorialDialogueView>(go);
            TutorialController controller = GetOrAdd<TutorialController>(go);
            TutorialHighlighter highlighter = GetOrAdd<TutorialHighlighter>(go);

            var viewSo = new SerializedObject(view);
            viewSo.FindProperty("document").objectReferenceValue = hud.GetComponent<UIDocument>();
            viewSo.FindProperty("dealerIcon").objectReferenceValue = icon;
            viewSo.ApplyModifiedPropertiesWithoutUndo();

            var controllerSo = new SerializedObject(controller);
            controllerSo.FindProperty("view").objectReferenceValue = view;
            controllerSo.FindProperty("data").objectReferenceValue = data;
            controllerSo.ApplyModifiedPropertiesWithoutUndo();

            // 강조는 게임이 이미 쓰는 표시를 빌린다: 판·트레이 강조·호버 점(TableChipView), 휠(RoulettePresenter), 카메라 자리(CameraRig).
            var gameSo = new SerializedObject(game);
            gameSo.FindProperty("tutorial").objectReferenceValue = controller;
            gameSo.FindProperty("tutorialHighlighter").objectReferenceValue = highlighter;
            gameSo.ApplyModifiedPropertiesWithoutUndo();

            var tableView = gameSo.FindProperty("tableView").objectReferenceValue as TableChipView;
            var tableSo = tableView != null ? new SerializedObject(tableView) : null;
            var highlighterSo = new SerializedObject(highlighter);
            highlighterSo.FindProperty("board").objectReferenceValue = tableSo?.FindProperty("board").objectReferenceValue;
            highlighterSo.FindProperty("tableView").objectReferenceValue = tableView;
            highlighterSo.FindProperty("trayGlow").objectReferenceValue = tableSo?.FindProperty("trayGlow").objectReferenceValue;
            highlighterSo.FindProperty("cellHighlight").objectReferenceValue = tableSo?.FindProperty("highlight").objectReferenceValue;
            highlighterSo.FindProperty("spotTemplate").objectReferenceValue = tableSo?.FindProperty("hoverMarker").objectReferenceValue;
            highlighterSo.FindProperty("presenter").objectReferenceValue = gameSo.FindProperty("presenter").objectReferenceValue;
            highlighterSo.FindProperty("definition").objectReferenceValue = gameSo.FindProperty("definition").objectReferenceValue;
            highlighterSo.FindProperty("hud").objectReferenceValue = hud;
            highlighterSo.ApplyModifiedPropertiesWithoutUndo();
            if (tableView == null)
            {
                log.AppendLine("  RouletteGame.tableView 없음 → 판·트레이 강조는 연결하지 않음");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            if (!wasDirty)
            {
                EditorSceneManager.SaveScene(scene);
            }

            log.AppendLine($"  씬 {scene.name}: {ObjectName}(컨트롤러·대화창) 연결, 아이콘 {(icon != null ? icon.name : "없음")}, 대사 {(data != null ? data.name : "없음")}" +
                           (wasDirty ? " · 씬에 저장 안 한 변경이 있어 저장하지 않음" : " · 저장"));
            return log.ToString();
        }

        /// <summary>딜러 아이콘 PNG를 UI용 스프라이트로(투명 배경 유지, 밉맵 없음).</summary>
        private static Sprite ConfigureIcon(StringBuilder log)
        {
            var importer = AssetImporter.GetAtPath(IconPath) as TextureImporter;
            if (importer == null)
            {
                log.AppendLine($"  아이콘 {IconPath} 없음");
                return null;
            }

            if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single
                || !importer.alphaIsTransparency || importer.mipmapEnabled)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
                log.AppendLine("  아이콘: 스프라이트로 임포트");
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(IconPath);
        }

        // GetComponent가 돌려주는 빈 컴포넌트는 C# null이 아니라서 ??를 쓰지 않는다.
        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T component = go.GetComponent<T>();
            return component != null ? component : go.AddComponent<T>();
        }
    }
}
