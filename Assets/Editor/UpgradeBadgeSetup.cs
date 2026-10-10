using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ProjectCR.Editor
{
    /// <summary>
    /// 보유 강화 배지 준비: Badge.fbx 임포트 정리 → UpgradeBadge 프리팹(피벗 = 배지 바닥 가운데, 콜라이더, 아이콘 면 연결)
    /// → 씬에 UpgradeBadgeView(원 6개 연결)를 두고 RouletteGame에 연결. 여러 번 실행해도 같은 결과다(있으면 갱신).
    /// </summary>
    public static class UpgradeBadgeSetup
    {
        private const string ModelPath = "Assets/Art/Models/Badge.fbx";
        private const string PrefabPath = "Assets/Prefabs/UpgradeBadge.prefab";
        private const string SlotsName = "CRT_ObjectSlots_6";
        private const string ViewName = "UpgradeBadges";

        // 배지가 얇아서(약 4mm) 마우스로 잡기 쉽게 콜라이더는 이 높이 이상으로 둔다.
        private const float MinColliderHeight = 0.012f;

        [MenuItem("Tools/ProjectCR/Table/Setup Upgrade Badges")]
        public static void SetupMenu()
        {
            Debug.Log("[UpgradeBadge] 준비\n" + Setup());
        }

        public static string Setup()
        {
            var log = new StringBuilder();
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return "플레이 모드에서는 실행하지 않는다.";
            }

            ConfigureImporter(log);
            UpgradeBadge prefab = BuildPrefab(log);
            if (prefab != null)
            {
                WireScene(prefab, log);
            }

            return log.ToString();
        }

        /// <summary>모델에 딸려 온 카메라·라이트·애니메이션은 쓰지 않는다.</summary>
        private static void ConfigureImporter(StringBuilder log)
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
            {
                log.AppendLine($"  {ModelPath} 없음");
                return;
            }

            if (importer.importCameras || importer.importLights || importer.importAnimation)
            {
                importer.importCameras = false;
                importer.importLights = false;
                importer.importAnimation = false;
                importer.SaveAndReimport();
                log.AppendLine("  임포트: 카메라·라이트·애니메이션 끔");
            }
        }

        private static UpgradeBadge BuildPrefab(StringBuilder log)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                log.AppendLine($"  {ModelPath} 없음 → 프리팹을 만들지 않음");
                return null;
            }

            // 새로 만들 때는 미리보기 씬에서 조립한다(열린 씬을 더럽히지 않게).
            bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
            UnityEngine.SceneManagement.Scene preview = exists ? default : EditorSceneManager.NewPreviewScene();
            GameObject root = exists ? PrefabUtility.LoadPrefabContents(PrefabPath) : new GameObject("UpgradeBadge");
            if (!exists)
            {
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, preview);
            }

            try
            {
                Transform body = root.transform.Cast<Transform>().FirstOrDefault(t => PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject) == model);
                if (body == null)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.scene);
                    instance.transform.SetParent(root.transform, false);
                    body = instance.transform;
                }

                // 피벗을 배지 바닥 가운데로: 모델의 회전·크기는 FBX 그대로 두고 위치만 민다.
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                root.transform.localScale = Vector3.one;
                // 이미 맞으면 건드리지 않는다(다시 실행할 때 부동소수 오차로 파일이 바뀌지 않게).
                Bounds bounds = WorldBounds(body);
                var offset = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                if (offset.sqrMagnitude > 1e-10f)
                {
                    body.localPosition -= offset;
                    bounds = WorldBounds(body);
                }

                BoxCollider collider = GetOrAdd<BoxCollider>(root);
                float height = Mathf.Max(bounds.size.y, MinColliderHeight);
                collider.center = new Vector3(0f, height * 0.5f, 0f);
                collider.size = new Vector3(bounds.size.x, height, bounds.size.z);

                UpgradeBadge badge = GetOrAdd<UpgradeBadge>(root);
                Renderer image = body.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name.EndsWith("_Image"));
                var so = new SerializedObject(badge);
                so.FindProperty("image").objectReferenceValue = image;
                so.ApplyModifiedPropertiesWithoutUndo();

                // 배지는 그림자를 받기만 한다(얇아서 드리우면 원 인쇄가 지저분해진다).
                foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                log.AppendLine($"  프리팹 {(exists ? "갱신" : "생성")}: {PrefabPath} (지름 {bounds.size.x * 100f:0.0}cm, 높이 {bounds.size.y * 1000f:0.0}mm, 아이콘 면 {(image != null ? image.name : "없음")})");
            }
            finally
            {
                if (exists)
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
                else
                {
                    Object.DestroyImmediate(root);
                    EditorSceneManager.ClosePreviewScene(preview);
                }
            }

            return AssetDatabase.LoadAssetAtPath<UpgradeBadge>(PrefabPath);
        }

        /// <summary>씬: 원 6개를 칸 번호 순서로 연결한 UpgradeBadgeView를 두고 RouletteGame에 연결한다. 씬이 깨끗했을 때만 저장.</summary>
        private static void WireScene(UpgradeBadge prefab, StringBuilder log)
        {
            var scene = EditorSceneManager.GetActiveScene();
            bool wasDirty = scene.isDirty;
            GameObject slots = GameObject.Find(SlotsName);
            var game = Object.FindFirstObjectByType<RouletteGame>();
            if (slots == null || game == null)
            {
                log.AppendLine($"  씬 {scene.name}: {(slots == null ? SlotsName : "RouletteGame")} 없음 → 연결하지 않음");
                return;
            }

            Renderer[] rings = slots.GetComponentsInChildren<Renderer>(true)
                .Where(r => r.name.Contains("_Ring"))
                .OrderBy(r => r.name)
                .ToArray();

            GameObject viewObject = GameObject.Find(ViewName) ?? new GameObject(ViewName);
            viewObject.transform.SetParent(null, false);
            viewObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            viewObject.transform.localScale = Vector3.one;
            UpgradeBadgeView view = GetOrAdd<UpgradeBadgeView>(viewObject);

            var viewSo = new SerializedObject(view);
            SerializedProperty ringsProperty = viewSo.FindProperty("slotRings");
            ringsProperty.arraySize = rings.Length;
            for (int i = 0; i < rings.Length; i++)
            {
                ringsProperty.GetArrayElementAtIndex(i).objectReferenceValue = rings[i];
            }

            viewSo.FindProperty("badgePrefab").objectReferenceValue = prefab;
            viewSo.ApplyModifiedPropertiesWithoutUndo();

            var gameSo = new SerializedObject(game);
            gameSo.FindProperty("upgradeBadges").objectReferenceValue = view;
            gameSo.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!wasDirty)
            {
                EditorSceneManager.SaveScene(scene);
            }

            log.AppendLine($"  씬 {scene.name}: {ViewName} 원 {rings.Length}개 연결({string.Join(", ", rings.Select(r => r.name))}), RouletteGame 연결" +
                           (wasDirty ? " · 씬에 저장 안 한 변경이 있어 저장하지 않음" : " · 저장"));
        }

        // GetComponent가 돌려주는 빈 컴포넌트는 C# null이 아니라서 ??를 쓰지 않는다.
        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T component = go.GetComponent<T>();
            return component != null ? component : go.AddComponent<T>();
        }

        private static Bounds WorldBounds(Transform root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers)
            {
                bounds.Encapsulate(renderer.bounds);
            }

            return bounds;
        }
    }
}
