using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace ProjectCR.Editor
{
    /// <summary>
    /// 메뉴 준비(여러 번 실행해도 같은 결과, 있으면 갱신):
    /// 1. AudioMixer(Resources/Audio/MainMixer): Master 아래 BGM·SFX 그룹, 볼륨 노출 파라미터 MasterVolume·BGMVolume·SFXVolume.
    /// 2. MainMenu 씬(카메라 + UIDocument MainMenu.uxml + MainMenuView). 빌드 목록 0 = MainMenu, 1 = Roulette.
    /// 3. Roulette 씬: GameMenus(UIDocument GameMenus.uxml, HUD보다 위) + PauseMenuView + GameResultView, RouletteGame에 연결.
    /// 4. 태블릿 [서비스] 버튼에 강조용 금색 빛 테두리(ChipTrayGlow, 칩 트레이와 같은 재질).
    /// </summary>
    public static class MenuSetup
    {
        private const string MixerPath = "Assets/Resources/Audio/MainMixer.mixer";
        private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
        private const string GameScenePath = "Assets/Scenes/Roulette.unity";
        private const string PanelSettingsPath = "Assets/UI/RoulettePanelSettings.asset";
        private const string MainMenuUxml = "Assets/UI/MainMenu.uxml";
        private const string GameMenusUxml = "Assets/UI/GameMenus.uxml";
        private const string GameMenusName = "GameMenus";
        private const string GlowName = "HighlightGlow";

        [MenuItem("Tools/ProjectCR/Menus/Setup")]
        public static void SetupMenu()
        {
            Debug.Log("[MenuSetup]\n" + Setup());
        }

        public static string Setup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return "플레이 모드에서는 실행하지 않는다.";
            }

            var log = new StringBuilder();
            CreateMixer(log);
            CreateMainMenuScene(log);
            SetBuildScenes(log);
            WireGameScene(log);
            return log.ToString();
        }

        // ---------- 1. AudioMixer ----------

        private static void CreateMixer(StringBuilder log)
        {
            if (AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath) != null)
            {
                log.AppendLine($"  믹서: {MixerPath} 이미 있음");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(MixerPath));
            AssetDatabase.Refresh();

            // AudioMixer를 만들고 그룹·노출 파라미터를 다는 공개 API가 없어서 에디터 내부 API(AudioMixerController)를 쓴다.
            Assembly editor = typeof(UnityEditor.Editor).Assembly;
            Type controllerType = editor.GetType("UnityEditor.Audio.AudioMixerController");
            Type groupType = editor.GetType("UnityEditor.Audio.AudioMixerGroupController");
            Type pathType = editor.GetType("UnityEditor.Audio.AudioGroupParameterPath");
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

            object controller = controllerType.GetMethod("CreateMixerControllerAtPath", all).Invoke(null, new object[] { MixerPath });
            object master = controllerType.GetProperty("masterGroup", all).GetValue(controller);
            var groups = new Dictionary<string, object> { { GameSettings.MasterParameter, master } };
            foreach (var (name, parameter) in new[] { ("BGM", GameSettings.BgmParameter), ("SFX", GameSettings.SfxParameter) })
            {
                object group = controllerType.GetMethod("CreateNewGroup", all).Invoke(controller, new object[] { name, false });
                controllerType.GetMethod("AddChildToParent", all).Invoke(controller, new[] { group, master });
                groups[parameter] = group;
            }

            // 그룹 볼륨을 노출하고 이름을 붙인다.
            MethodInfo volumeGuid = groupType.GetMethod("GetGUIDForVolume", all);
            MethodInfo addExposed = controllerType.GetMethod("AddExposedParameter", all);
            var guidToName = new Dictionary<string, string>();
            foreach (KeyValuePair<string, object> pair in groups)
            {
                object guid = volumeGuid.Invoke(pair.Value, null);
                object path = Activator.CreateInstance(pathType, all, null, new[] { pair.Value, guid }, null);
                addExposed.Invoke(controller, new[] { path });
                guidToName[guid.ToString()] = pair.Key;
            }

            PropertyInfo exposedProperty = controllerType.GetProperty("exposedParameters", all);
            var exposed = (Array)exposedProperty.GetValue(controller);
            Type exposedType = exposed.GetType().GetElementType();
            FieldInfo nameField = exposedType.GetField("name", all);
            FieldInfo guidField = exposedType.GetField("guid", all);
            for (int i = 0; i < exposed.Length; i++)
            {
                object item = exposed.GetValue(i);
                if (guidToName.TryGetValue(guidField.GetValue(item).ToString(), out string name))
                {
                    nameField.SetValue(item, name);
                    exposed.SetValue(item, i);
                }
            }

            exposedProperty.SetValue(controller, exposed);
            EditorUtility.SetDirty((Object)controller);
            AssetDatabase.SaveAssets();
            log.AppendLine($"  믹서 생성: {MixerPath} (Master·BGM·SFX, 노출 {string.Join(", ", guidToName.Values)})");
        }

        // ---------- 2. 메인 메뉴 씬 ----------

        private static void CreateMainMenuScene(StringBuilder log)
        {
            if (File.Exists(MainMenuScenePath))
            {
                log.AppendLine($"  메인 메뉴 씬: {MainMenuScenePath} 이미 있음");
                return;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            cameraObject.AddComponent<AudioListener>();
            SceneManager.MoveGameObjectToScene(cameraObject, scene);

            var menuObject = new GameObject("MainMenu");
            UIDocument document = menuObject.AddComponent<UIDocument>();
            document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(MainMenuUxml);
            menuObject.AddComponent<MainMenuView>();
            SceneManager.MoveGameObjectToScene(menuObject, scene);

            EditorSceneManager.SaveScene(scene, MainMenuScenePath);
            EditorSceneManager.CloseScene(scene, true);
            log.AppendLine($"  메인 메뉴 씬 생성: {MainMenuScenePath}");
        }

        private static void SetBuildScenes(StringBuilder log)
        {
            var scenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(MainMenuScenePath, true),
                new EditorBuildSettingsScene(GameScenePath, true),
            };
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.path != MainMenuScenePath && scene.path != GameScenePath)
                {
                    scenes.Add(scene);
                }
            }

            EditorBuildSettings.scenes = scenes.ToArray();
            log.AppendLine("  빌드 목록: 0 MainMenu, 1 Roulette");
        }

        // ---------- 3·4. 게임 씬 ----------

        private static void WireGameScene(StringBuilder log)
        {
            Scene scene = EditorSceneManager.GetActiveScene();
            var game = Object.FindFirstObjectByType<RouletteGame>();
            if (game == null)
            {
                log.AppendLine($"  씬 {scene.name}: RouletteGame 없음 → 게임 메뉴는 Roulette 씬을 열고 다시 실행");
                return;
            }

            bool wasDirty = scene.isDirty;
            GameObject menus = GameObject.Find(GameMenusName);
            if (menus == null)
            {
                menus = new GameObject(GameMenusName);
            }

            UIDocument document = GetOrAdd<UIDocument>(menus);
            document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(GameMenusUxml);
            document.sortingOrder = 10; // HUD(0)보다 위
            PauseMenuView pause = GetOrAdd<PauseMenuView>(menus);
            GameResultView result = GetOrAdd<GameResultView>(menus);

            var pauseSo = new SerializedObject(pause);
            pauseSo.FindProperty("resultView").objectReferenceValue = result;
            pauseSo.ApplyModifiedPropertiesWithoutUndo();

            var gameSo = new SerializedObject(game);
            gameSo.FindProperty("pauseMenu").objectReferenceValue = pause;
            gameSo.FindProperty("resultView").objectReferenceValue = result;
            gameSo.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine($"  씬 {scene.name}: {GameMenusName}(일시정지·결과·옵션) 연결");

            AddServiceGlow(gameSo, log);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!wasDirty)
            {
                EditorSceneManager.SaveScene(scene);
                log.AppendLine("  씬 저장");
            }
            else
            {
                log.AppendLine("  씬에 저장 안 한 변경이 있어 저장하지 않음");
            }
        }

        /// <summary>[서비스] 버튼 면 둘레에 금색 빛 테두리(ChipTrayGlow 한 칸)를 단다. 면과 같은 방향, 크기는 면 메시에서 잰다.</summary>
        private static void AddServiceGlow(SerializedObject gameSo, StringBuilder log)
        {
            var tableView = gameSo.FindProperty("tableView").objectReferenceValue as TableChipView;
            var trayGlow = tableView != null ? new SerializedObject(tableView).FindProperty("trayGlow").objectReferenceValue as ChipTrayGlow : null;
            Material glowMaterial = trayGlow != null ? trayGlow.GetComponent<MeshRenderer>().sharedMaterial : null;
            TabletButton service = Object.FindObjectsByType<TabletButton>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(b => b.Action == TabletAction.Service);
            if (service == null || glowMaterial == null)
            {
                log.AppendLine("  [서비스] 강조 테두리: 버튼 또는 트레이 강조 재질 없음 → 건너뜀");
                return;
            }

            var face = new SerializedObject(service).FindProperty("face").objectReferenceValue as Renderer;
            MeshFilter faceMesh = face != null ? face.GetComponent<MeshFilter>() : null;
            if (faceMesh == null || faceMesh.sharedMesh == null)
            {
                log.AppendLine("  [서비스] 강조 테두리: 버튼 면 메시 없음 → 건너뜀");
                return;
            }

            Transform existing = service.transform.Find(GlowName);
            GameObject glowObject = existing != null ? existing.gameObject : new GameObject(GlowName);
            glowObject.transform.SetParent(service.transform, false);

            // 면과 같은 방향, 크기 1로 둔다(면이 비균등 크기여도 선 두께가 고르게).
            Transform faceTransform = face.transform;
            glowObject.transform.SetPositionAndRotation(faceTransform.position, faceTransform.rotation);
            Vector3 parentScale = service.transform.lossyScale;
            glowObject.transform.localScale = new Vector3(1f / parentScale.x, 1f / parentScale.y, 1f / parentScale.z);

            Bounds local = faceMesh.sharedMesh.bounds;
            Vector3 scaled = Vector3.Scale(local.size, faceTransform.lossyScale);
            Vector3 center = Vector3.Scale(local.center, faceTransform.lossyScale);

            // 가장 얇은 축 = 면의 법선. 나머지 두 축이 테두리 사각형.
            int normal = scaled.x <= scaled.y && scaled.x <= scaled.z ? 0 : scaled.y <= scaled.z ? 1 : 2;
            int first = normal == 0 ? 1 : 0;
            int second = normal == 2 ? 1 : 2;
            Vector3 axisX = Vector3.zero;
            Vector3 axisZ = Vector3.zero;
            Vector3 up = Vector3.zero;
            axisX[first] = 1f;
            axisZ[second] = 1f;
            up[normal] = 1f;

            // 앞면 쪽(카메라 쪽)으로 살짝 띄운다. 면이 카메라를 보는 방향은 월드 위(+Y)에 가까운 쪽으로 고른다.
            Vector3 worldUp = faceTransform.rotation * up;
            float sign = Vector3.Dot(worldUp, Vector3.up) >= 0f ? 1f : -1f;
            Vector3 lift = up * (scaled[normal] * 0.5f + 0.0008f) * sign;

            GetOrAdd<MeshFilter>(glowObject);
            MeshRenderer renderer = GetOrAdd<MeshRenderer>(glowObject);
            renderer.sharedMaterial = glowMaterial;
            ChipTrayGlow glow = GetOrAdd<ChipTrayGlow>(glowObject);
            glow.Configure(new[] { center + lift }, new[] { new Vector2(scaled[first] * 0.5f, scaled[second] * 0.5f) }, axisX, axisZ);
            EditorUtility.SetDirty(glow);

            service.SetHighlightGlow(glow);
            EditorUtility.SetDirty(service);
            log.AppendLine($"  [서비스] 강조 테두리: {scaled[first] * 100f:0.0}×{scaled[second] * 100f:0.0}cm");
        }

        // GetComponent가 돌려주는 빈 컴포넌트는 C# null이 아니라서 ??를 쓰지 않는다.
        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T component = go.GetComponent<T>();
            return component != null ? component : go.AddComponent<T>();
        }
    }
}
