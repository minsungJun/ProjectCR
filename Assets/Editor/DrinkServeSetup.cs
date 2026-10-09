using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ProjectCR.Editor
{
    /// <summary>
    /// 음료 서빙 로봇 준비(메뉴 Tools/ProjectCR/Drinks/Setup Serving Robot).
    /// 1. cocktail.fbx(6잔이 한 덩어리)를 잔마다 나눠 Prefabs/Cocktail/CRD_Cocktail_*.prefab으로 만든다(부품은 가장 가까운 잔에 붙인다).
    /// 2. 지급 음료 6종에 색이 맞는 칵테일을 모델로 넣는다(허리케인 COLUMN, 위스키 DOZEN, 하이볼 홀짝, 마티니 색상, 플루트 높낮이, 쿠페 숫자).
    ///    확률 음료는 이미 연결된 모델(…2 프리팹)을 그대로 둔다.
    /// 3. Robot.fbx로 Prefabs/ServingRobot.prefab을 만든다(가운데를 원점으로, 바닥판 숨김, 패드 4개·바퀴 연결).
    /// 4. 씬에 로봇·출발점·정지점·음료 카메라 자리를 놓고(이미 있으면 위치는 그대로) RouletteGame·CameraRig에 연결한다.
    ///    예전 음료 카드 창(DrinkSelectionView) 컴포넌트는 HUD에서 뗀다.
    /// 다시 돌려도 된다. 씬에 이미 있는 자리 오브젝트는 옮기지 않으니 위치는 씬에서 맞추면 된다.
    /// </summary>
    public static class DrinkServeSetup
    {
        private const string CocktailModelPath = "Assets/Art/cocktail.fbx";
        private const string CocktailFolder = "Assets/Prefabs/Cocktail";
        private const string RobotModelPath = "Assets/Art/Robot.fbx";
        private const string RobotPrefabPath = "Assets/Prefabs/ServingRobot.prefab";
        private const string OutlineMaterialPath = "Assets/Materials/Drink_Outline.mat";
        private const string DrinkFolder = "Assets/Data/Drinks";
        private const string ScenePath = "Assets/Scenes/Roulette.unity";

        private const string RobotObjectName = "ServingRobot";
        private const string EntryName = "RobotEntryPoint";
        private const string ServeName = "RobotServePoint";
        private const string CameraName = "CameraPosition_Drink";

        private static readonly (string Glass, DrinkGroup Group)[] Cocktails =
        {
            ("Hurricane", DrinkGroup.Column),
            ("Whiskey", DrinkGroup.Dozen),
            ("Highball", DrinkGroup.Parity),
            ("Martini", DrinkGroup.Color),
            ("Flute", DrinkGroup.Range),
            ("Coupe", DrinkGroup.Number),
        };

        // 위 왼쪽, 위 오른쪽, 아래 왼쪽, 아래 오른쪽(후보 순서).
        private static readonly string[] PadNames =
        {
            "CRP_Upper_Left_Pad_D110", "CRP_Upper_Right_Pad_D110", "CRP_Lower_Left_Pad_D110", "CRP_Lower_Right_Pad_D110",
        };

        [MenuItem("Tools/ProjectCR/Drinks/Setup Serving Robot")]
        public static void SetupAll()
        {
            Dictionary<DrinkGroup, GameObject> cocktails = BuildCocktailPrefabs();
            AssignPayoutModels(cocktails);
            GameObject robotPrefab = BuildRobotPrefab();
            WireScene(robotPrefab);
        }

        // ---------- 1. 칵테일 나누기 ----------

        private static Dictionary<DrinkGroup, GameObject> BuildCocktailPrefabs()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(CocktailModelPath)
                ?? throw new InvalidOperationException($"{CocktailModelPath}가 없다.");
            GameObject clone = Object.Instantiate(model);
            var result = new Dictionary<DrinkGroup, GameObject>();
            try
            {
                // 잔 위치(바닥 가운데)와 부품 묶음.
                var glasses = new List<(string Glass, DrinkGroup Group, Bounds Bounds, List<Transform> Parts)>();
                foreach (var (glass, group) in Cocktails)
                {
                    Transform glassPart = clone.transform.Find($"CRD_{glass}_Glass")
                        ?? throw new InvalidOperationException($"cocktail에 CRD_{glass}_Glass가 없다.");
                    glasses.Add((glass, group, glassPart.GetComponent<Renderer>().bounds, new List<Transform>()));
                }

                var parts = new List<Transform>();
                foreach (Transform child in clone.transform)
                {
                    parts.Add(child);
                }

                foreach (Transform part in parts)
                {
                    var renderer = part.GetComponent<Renderer>();
                    if (renderer == null || part.name.Contains("Studio_Ground"))
                    {
                        continue;
                    }

                    Vector3 c = renderer.bounds.center;
                    int best = 0;
                    float bestDistance = float.MaxValue;
                    for (int i = 0; i < glasses.Count; i++)
                    {
                        Vector3 g = glasses[i].Bounds.center;
                        float d = new Vector2(c.x - g.x, c.z - g.z).sqrMagnitude;
                        if (d < bestDistance)
                        {
                            bestDistance = d;
                            best = i;
                        }
                    }

                    glasses[best].Parts.Add(part);
                }

                foreach (var glass in glasses)
                {
                    Bounds all = glass.Bounds;
                    foreach (Transform part in glass.Parts)
                    {
                        all.Encapsulate(part.GetComponent<Renderer>().bounds);
                    }

                    var root = new GameObject($"CRD_Cocktail_{glass.Glass}");
                    root.transform.position = new Vector3(all.center.x, all.min.y, all.center.z);
                    foreach (Transform part in glass.Parts)
                    {
                        part.SetParent(root.transform, true);
                    }

                    // 잔 바닥 가운데가 프리팹 원점이 되게.
                    root.transform.position = Vector3.zero;
                    string path = $"{CocktailFolder}/{root.name}.prefab";
                    result[glass.Group] = PrefabUtility.SaveAsPrefabAsset(root, path);
                    Object.DestroyImmediate(root);
                    Debug.Log($"[DrinkServeSetup] {path} ({glass.Parts.Count}개 부품, {glass.Group})");
                }
            }
            finally
            {
                Object.DestroyImmediate(clone);
            }

            return result;
        }

        // ---------- 2. 지급 음료 모델 ----------

        private static void AssignPayoutModels(Dictionary<DrinkGroup, GameObject> cocktails)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:DrinkDefinition", new[] { DrinkFolder }))
            {
                var drink = AssetDatabase.LoadAssetAtPath<DrinkDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (drink == null || !cocktails.TryGetValue(drink.Group, out GameObject prefab))
                {
                    continue;
                }

                var so = new SerializedObject(drink);
                so.FindProperty("model").objectReferenceValue = prefab;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(drink);
            }

            AssetDatabase.SaveAssets();
        }

        // ---------- 3. 로봇 프리팹 ----------

        private static GameObject BuildRobotPrefab()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(RobotModelPath)
                ?? throw new InvalidOperationException($"{RobotModelPath}가 없다.");

            var root = new GameObject(RobotObjectName);
            try
            {
                var body = (GameObject)PrefabUtility.InstantiatePrefab(model);
                body.transform.SetParent(root.transform, false);

                Transform ground = body.transform.Find("CRR_Studio_Ground");
                if (ground != null)
                {
                    ground.gameObject.SetActive(false);
                }

                // 모델 가운데(바닥 높이 그대로)를 루트 원점에. 모델 앞(+Z)이 로봇 앞.
                Bounds bounds = default;
                bool first = true;
                foreach (Renderer r in body.GetComponentsInChildren<Renderer>())
                {
                    if (first)
                    {
                        bounds = r.bounds;
                        first = false;
                    }
                    else
                    {
                        bounds.Encapsulate(r.bounds);
                    }
                }

                body.transform.localPosition = new Vector3(-bounds.center.x, 0f, -bounds.center.z);

                var pads = new Renderer[PadNames.Length];
                for (int i = 0; i < PadNames.Length; i++)
                {
                    pads[i] = (body.transform.Find(PadNames[i])
                        ?? throw new InvalidOperationException($"로봇에 {PadNames[i]}이 없다.")).GetComponent<Renderer>();
                }

                var wheels = new List<Transform>();
                foreach (Transform t in body.transform)
                {
                    if (t.name.EndsWith("_Wheel") || t.name.EndsWith("_Hub"))
                    {
                        wheels.Add(t);
                    }
                }

                var robot = root.AddComponent<DrinkServerRobot>();
                robot.Configure(pads, wheels.ToArray(), null, null, OutlineMaterial());
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, RobotPrefabPath);
                Debug.Log($"[DrinkServeSetup] {RobotPrefabPath} (패드 {pads.Length}, 바퀴·허브 {wheels.Count})");
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Material OutlineMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(OutlineMaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("ProjectCR/OutlineHull")
                    ?? throw new InvalidOperationException("ProjectCR/OutlineHull 셰이더가 없다."));
                AssetDatabase.CreateAsset(material, OutlineMaterialPath);
            }

            return material;
        }

        // ---------- 4. 씬 연결 ----------

        private static void WireScene(GameObject robotPrefab)
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            var game = Object.FindFirstObjectByType<RouletteGame>()
                ?? throw new InvalidOperationException("씬에 RouletteGame이 없다.");
            var rig = Object.FindFirstObjectByType<CameraRig>();

            // 플레이어 오른쪽(베팅 카메라의 오른쪽, 바닥 높이). 로봇 앞이 플레이어를 본다.
            Transform betting = GameObject.Find("CameraPostion_BettingBoard")?.transform;
            Vector3 eye = betting != null ? betting.position : new Vector3(-1.17f, 1.29f, 0.52f);
            Vector3 right = betting != null ? Vector3.ProjectOnPlane(betting.right, Vector3.up).normalized : Vector3.left;
            Vector3 back = betting != null ? -Vector3.ProjectOnPlane(betting.forward, Vector3.up).normalized : Vector3.forward;
            const float floorY = -0.8f;
            Vector3 serve = new Vector3(eye.x, floorY, eye.z) + right * 0.68f + back * 0.53f;
            Quaternion facePlayer = Quaternion.LookRotation(-right, Vector3.up);

            Transform servePoint = FindOrCreate(ServeName, serve, facePlayer);
            Transform entryPoint = FindOrCreate(EntryName, serve + right * 1.4f, facePlayer);

            // 음료 카메라: 로봇 앞 1.1m, 앉은 눈높이(바닥 + 0.85m)에서 두 선반 가운데를 본다. 아래 선반 잔이 위 선반에 가리지 않는 높이.
            Vector3 shelf = serve + Vector3.up * 0.6f;
            Vector3 camera = serve - right * 1.1f + Vector3.up * 0.85f;
            Transform cameraPose = FindOrCreate(CameraName, camera, Quaternion.LookRotation(shelf - camera, Vector3.up));

            DrinkServerRobot robot = Object.FindFirstObjectByType<DrinkServerRobot>(FindObjectsInactive.Include);
            if (robot == null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(robotPrefab, scene);
                instance.transform.SetPositionAndRotation(entryPoint.position, entryPoint.rotation);
                robot = instance.GetComponent<DrinkServerRobot>();
            }

            var robotSo = new SerializedObject(robot);
            robotSo.FindProperty("entryPoint").objectReferenceValue = entryPoint;
            robotSo.FindProperty("servePoint").objectReferenceValue = servePoint;
            robotSo.ApplyModifiedPropertiesWithoutUndo();

            var gameSo = new SerializedObject(game);
            gameSo.FindProperty("drinkServer").objectReferenceValue = robot;
            gameSo.ApplyModifiedPropertiesWithoutUndo();

            if (rig != null)
            {
                var rigSo = new SerializedObject(rig);
                rigSo.FindProperty("drinkPose").objectReferenceValue = cameraPose;
                rigSo.ApplyModifiedPropertiesWithoutUndo();
            }

            // 예전 음료 카드 창 컴포넌트를 뗀다(UXML 창도 지웠다).
            foreach (MonoBehaviour behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (behaviour != null && behaviour.GetType().Name == "DrinkSelectionView")
                {
                    Object.DestroyImmediate(behaviour);
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[DrinkServeSetup] 씬 연결: 로봇 {robot.name}, 정지점 {servePoint.position:F2}, 출발점 {entryPoint.position:F2}, 카메라 {cameraPose.position:F2}");
        }

        /// <summary>씬 루트에 같은 이름이 있으면 그대로(위치를 바꾸지 않는다), 없으면 만든다.</summary>
        private static Transform FindOrCreate(string name, Vector3 position, Quaternion rotation)
        {
            GameObject existing = GameObject.Find(name);
            if (existing != null)
            {
                return existing.transform;
            }

            var go = new GameObject(name);
            go.transform.SetPositionAndRotation(position, rotation);
            return go.transform;
        }
    }
}
