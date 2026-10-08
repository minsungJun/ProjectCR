using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace ProjectCR.Editor
{
    /// <summary>
    /// 임시 모델과 씬을 코드로 만든다. 아트 모델이 오기 전까지 이걸로 개발한다.
    ///
    /// 칸·앵커·Renderer는 RouletteDefinition의 칸 순서대로 같은 인덱스에 만든다. 오브젝트 이름은 읽기 편하라고 붙일 뿐이다.
    /// 다시 돌려도 된다. 에셋은 있으면 내용만 갱신해서 GUID와 씬 참조가 유지된다.
    ///
    /// 진짜 모델로 바꿀 때 지킬 규약(Docs/ProjectCR 설계.md):
    /// 피벗은 휠 중심·Y 위, 0번 칸 중심이 로컬 +Z, 위에서 볼 때 시계 방향 해골 휠 순서(해골은 0의 정반대), 칸별 Renderer.
    /// </summary>
    public static class RouletteSetup
    {
        private const string DataFolder = "Assets/Data";
        private const string MaterialFolder = "Assets/Materials";
        private const string MeshFolder = "Assets/Art/Generated";
        private const string PrefabFolder = "Assets/Prefabs";
        private const string UIFolder = "Assets/UI";

        private const string DefinitionPath = DataFolder + "/RouletteDefinition_Skull38.asset";
        private const string BalancePath = DataFolder + "/GameBalance.asset";
        private const string MotionPath = DataFolder + "/SpinMotionSettings.asset";
        private const string RoulettePrefabPath = PrefabFolder + "/RouletteRoot.prefab";
        private const string BallPrefabPath = PrefabFolder + "/Ball.prefab";
        private const string PanelSettingsPath = UIFolder + "/RoulettePanelSettings.asset";
        private const string ThemePath = UIFolder + "/ProjectCRTheme.tss";
        private const string HudLayoutPath = UIFolder + "/RouletteHud.uxml";
        private const string ScenePath = "Assets/Scenes/Roulette.unity";

        // ---------- 임시 모델 치수 (RouletteRoot 로컬, 1 = 1m 감각) ----------
        private const float WheelRadius = 0.97f;
        private const float PocketInnerRadius = 0.62f;
        private const float PocketOuterRadius = 0.82f;
        private const float NumberRingOuterRadius = 0.95f;
        private const float PocketCenterRadius = 0.72f;
        private const float NumberRadius = 0.885f;
        private const float SurfaceHeight = 0.005f;
        private const float BallRadius = 0.03f;
        private const float TrackRadius = 1.12f;
        private const float TrackHeight = 0.15f;

        // 해골 칸 라벨. 내장 폰트에 ☠ 글리프가 없으면 비어 보이므로 그때는 "X"로 바꾼다.
        private const string SkullLabel = "☠";

        // 볼 그릇 단면 (반경, 높이). 안쪽 경사 → 볼 트랙 → 벽 → 윗면 → 바깥 옆면.
        private static readonly Vector2[] BowlProfile =
        {
            new Vector2(0.97f, 0.0f),
            new Vector2(1.09f, TrackHeight),
            new Vector2(1.16f, TrackHeight),
            new Vector2(1.18f, 0.25f),
            new Vector2(1.32f, 0.25f),
            new Vector2(1.32f, -0.06f),
        };

        [MenuItem("Tools/ProjectCR/Setup All")]
        public static void SetupAll()
        {
            CreateDataAssets();
            BuildRoulettePrefab();
            SetupScene();
            Debug.Log("[RouletteSetup] 데이터, 임시 모델 프리팹, 씬 구성을 마쳤다.");
        }

        [MenuItem("Tools/ProjectCR/1. Create Data Assets")]
        public static void CreateDataAssets()
        {
            EnsureFolder(DataFolder);
            EnsureFolder(UIFolder);

            LoadOrCreate<RouletteDefinition>(DefinitionPath, definition => definition.ResetToSkullWheel());
            LoadOrCreate<GameBalance>(BalancePath, null);
            LoadOrCreate<SpinMotionSettings>(MotionPath, null);

            var panel = LoadOrCreate<PanelSettings>(PanelSettingsPath, null);
            panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1920, 1080);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.5f;
            panel.clearColor = false;
            EditorUtility.SetDirty(panel);

            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/ProjectCR/2. Build Roulette Prefab")]
        public static void BuildRoulettePrefab()
        {
            var definition = AssetDatabase.LoadAssetAtPath<RouletteDefinition>(DefinitionPath);
            var motion = AssetDatabase.LoadAssetAtPath<SpinMotionSettings>(MotionPath);
            if (definition == null || motion == null)
            {
                throw new InvalidOperationException("데이터 에셋이 없다. 1. Create Data Assets를 먼저 돌릴 것.");
            }

            EnsureFolder(MaterialFolder);
            EnsureFolder(MeshFolder);
            EnsureFolder(PrefabFolder);

            var red = Mat("Pocket_Red", new Color(0.62f, 0.07f, 0.09f), 0.45f);
            var black = Mat("Pocket_Black", new Color(0.05f, 0.05f, 0.06f), 0.45f);
            var green = Mat("Pocket_Green", new Color(0.04f, 0.42f, 0.17f), 0.45f);
            var skull = Mat("Pocket_Skull", new Color(0.02f, 0.02f, 0.025f), 0.3f);
            var wood = Mat("Wheel_Wood", new Color(0.36f, 0.19f, 0.08f), 0.35f);
            var metal = Mat("Wheel_Metal", new Color(0.78f, 0.62f, 0.3f), 0.7f, 0.85f);
            var bowl = Mat("Bowl_Wood", new Color(0.24f, 0.12f, 0.05f), 0.3f);
            var ballMaterial = Mat("Ball", new Color(0.95f, 0.95f, 0.92f), 0.9f);

            Mesh sectorMesh = SaveMesh("PocketSector", BuildSectorMesh(definition.PocketCount));
            Mesh bowlMesh = SaveMesh("Bowl", BuildLatheMesh(BowlProfile, 96));

            Transform ballPrefab = BuildBallPrefab(ballMaterial);

            var root = new GameObject("RouletteRoot");
            try
            {
                // 그릇 (고정)
                var bowlObject = CreateMeshObject("Bowl", root.transform, bowlMesh, bowl);
                CreatePrimitive("BowlBase", PrimitiveType.Cylinder, bowlObject.transform,
                    new Vector3(0f, -0.07f, 0f), new Vector3(2.64f, 0.01f, 2.64f), bowl);

                // 휠 (회전)
                var wheelPivot = new GameObject("WheelPivot").transform;
                wheelPivot.SetParent(root.transform, false);

                var wheelVisual = new GameObject("WheelVisual").transform;
                wheelVisual.SetParent(wheelPivot, false);

                CreatePrimitive("Base", PrimitiveType.Cylinder, wheelVisual,
                    new Vector3(0f, -0.02f, 0f), new Vector3(WheelRadius * 2f, 0.02f, WheelRadius * 2f), wood);
                CreatePrimitive("Hub", PrimitiveType.Cylinder, wheelVisual,
                    new Vector3(0f, 0.03f, 0f), new Vector3(PocketInnerRadius * 2f, 0.03f, PocketInnerRadius * 2f), wood);
                CreatePrimitive("Turret", PrimitiveType.Cylinder, wheelVisual,
                    new Vector3(0f, 0.14f, 0f), new Vector3(0.16f, 0.08f, 0.16f), metal);
                CreatePrimitive("TurretKnob", PrimitiveType.Sphere, wheelVisual,
                    new Vector3(0f, 0.24f, 0f), new Vector3(0.1f, 0.1f, 0.1f), metal);

                var pocketsRoot = new GameObject("Pockets").transform;
                pocketsRoot.SetParent(wheelVisual, false);
                var separatorsRoot = new GameObject("Separators").transform;
                separatorsRoot.SetParent(wheelVisual, false);
                var anchorsRoot = new GameObject("PocketAnchors").transform;
                anchorsRoot.SetParent(wheelPivot, false);

                int count = definition.PocketCount;
                float span = 360f / count;
                var anchors = new PocketAnchor[count];
                var renderers = new Renderer[count];
                Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

                for (int i = 0; i < count; i++)
                {
                    PocketDefinition pocket = definition.GetPocket(i);
                    float angle = i * span;
                    Material material = pocket.IsSkull ? skull
                        : pocket.Color == PocketColor.Red ? red
                        : pocket.Color == PocketColor.Black ? black
                        : green;

                    // 칸 바닥 + 번호 띠 (부채꼴 하나)
                    var pocketObject = CreateMeshObject($"Pocket_{i:00}_n{pocket.Number}", pocketsRoot, sectorMesh, material);
                    pocketObject.transform.localPosition = new Vector3(0f, SurfaceHeight, 0f);
                    pocketObject.transform.localRotation = Quaternion.Euler(0f, angle, 0f);
                    renderers[i] = pocketObject.GetComponent<Renderer>();

                    if (pocket.IsSkull)
                    {
                        // 콘셉트 시안처럼 검은 칸 위 아이보리 해골. 임시 모델이라 아이콘 대신 글자로 둔다.
                        CreateLabel(pocketObject.transform, SkullLabel, font, new Color(0.96f, 0.93f, 0.82f), 1.35f);
                    }
                    else
                    {
                        CreateLabel(pocketObject.transform, pocket.Number.ToString(), font, Color.white, 1f);
                    }

                    // 칸막이: 이 칸과 다음 칸 사이
                    float separatorAngle = angle + span * 0.5f;
                    var separator = CreatePrimitive($"Separator_{i:00}", PrimitiveType.Cube, separatorsRoot,
                        BallTrajectory.LocalPosition(separatorAngle, (PocketInnerRadius + PocketOuterRadius) * 0.5f, 0.02f),
                        new Vector3(0.008f, 0.04f, PocketOuterRadius - PocketInnerRadius), metal);
                    separator.transform.localRotation = Quaternion.Euler(0f, separatorAngle, 0f);

                    // 공 안착점: 칸 중심, 공 중심 높이. 로컬 +Z가 바깥을 향한다.
                    var anchorObject = new GameObject($"Anchor_{i:00}_n{pocket.Number}");
                    anchorObject.transform.SetParent(anchorsRoot, false);
                    anchorObject.transform.localPosition = BallTrajectory.LocalPosition(angle, PocketCenterRadius, SurfaceHeight + BallRadius);
                    anchorObject.transform.localRotation = Quaternion.Euler(0f, angle, 0f);
                    anchors[i] = anchorObject.AddComponent<PocketAnchor>();
                    anchors[i].Configure(i, pocket.Number);
                }

                var ballsRoot = new GameObject("BallsRoot").transform;
                ballsRoot.SetParent(root.transform, false);

                var presenter = root.AddComponent<RoulettePresenter>();
                var so = new SerializedObject(presenter);
                SetReference(so, "definition", definition);
                SetReference(so, "motion", motion);
                SetReference(so, "wheelPivot", wheelPivot);
                SetReference(so, "ballsRoot", ballsRoot);
                SetReference(so, "ballPrefab", ballPrefab);
                SetArray(so, "pocketAnchors", anchors);
                SetArray(so, "pocketRenderers", renderers);
                so.FindProperty("rimRadius").floatValue = TrackRadius;
                so.FindProperty("rimHeight").floatValue = TrackHeight + BallRadius;
                so.FindProperty("ballRadius").floatValue = BallRadius;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, RoulettePrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/ProjectCR/3. Setup Scene")]
        public static void SetupScene()
        {
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null
                ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // 연출 카메라: 휠이 화면 왼쪽 가운데에 오도록 오른쪽으로 비켜서 내려다본다. 오른쪽은 베팅 패널 자리.
            Camera mainCamera = FindOrCreateCamera("Main Camera", true);
            mainCamera.transform.position = new Vector3(0.8f, 3.1f, -2.7f);
            mainCamera.transform.LookAt(new Vector3(0.8f, 0f, -0.2f));
            mainCamera.fieldOfView = 42f;
            ConfigureBackground(mainCamera);

            // 탑다운 정사영 카메라: 번호 일치를 눈으로 볼 때 쓴다. 기본은 꺼 둔다.
            Camera topDown = FindOrCreateCamera("TopDownCamera", false);
            topDown.transform.position = new Vector3(1.07f, 5f, -0.13f);
            topDown.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            topDown.orthographic = true;
            topDown.orthographicSize = 1.85f;
            topDown.enabled = false;
            ConfigureBackground(topDown);

            GameObject light = GameObject.Find("Directional Light");
            if (light != null)
            {
                light.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoulettePrefabPath);
            GameObject rouletteRoot = GameObject.Find("RouletteRoot");
            if (rouletteRoot == null)
            {
                rouletteRoot = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            }

            var hudObject = GameObject.Find("HUD") ?? new GameObject("HUD");
            var document = GetOrAdd<UIDocument>(hudObject);
            document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(HudLayoutPath);
            var hud = GetOrAdd<RouletteHud>(hudObject);

            var gameObject = GameObject.Find("RouletteGame") ?? new GameObject("RouletteGame");
            var game = GetOrAdd<RouletteGame>(gameObject);
            var so = new SerializedObject(game);
            SetReference(so, "definition", AssetDatabase.LoadAssetAtPath<RouletteDefinition>(DefinitionPath));
            SetReference(so, "balance", AssetDatabase.LoadAssetAtPath<GameBalance>(BalancePath));
            SetReference(so, "presenter", rouletteRoot.GetComponent<RoulettePresenter>());
            SetReference(so, "hud", hud);
            SetReference(so, "presentationCamera", mainCamera);
            SetReference(so, "topDownCamera", topDown);
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            // 빌드 목록은 이 씬 하나로 둔다. SampleScene 파일은 지우지 않는다.
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        // ---------- 메시 ----------

        /// <summary>로컬 +Z를 중심으로 한 칸 폭의 부채꼴. 안쪽은 칸 바닥, 바깥쪽은 번호 띠.</summary>
        private static Mesh BuildSectorMesh(int pocketCount)
        {
            const int segments = 6;
            float span = 360f / pocketCount;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            for (int s = 0; s <= segments; s++)
            {
                float angle = -span * 0.5f + span * s / segments;
                vertices.Add(BallTrajectory.LocalPosition(angle, PocketInnerRadius, 0f));
                vertices.Add(BallTrajectory.LocalPosition(angle, NumberRingOuterRadius, 0f));
            }

            for (int s = 0; s < segments; s++)
            {
                int inner = s * 2;
                int outer = inner + 1;
                int nextInner = inner + 2;
                int nextOuter = inner + 3;
                triangles.AddRange(new[] { inner, outer, nextInner, outer, nextOuter, nextInner });
            }

            var mesh = new Mesh { name = "PocketSector" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>단면(반경, 높이)을 Y축으로 돌린 회전체. 단면 구간마다 정점을 따로 둬서 모서리가 각지게 한다.</summary>
        private static Mesh BuildLatheMesh(IReadOnlyList<Vector2> profile, int segments)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            for (int p = 0; p < profile.Count - 1; p++)
            {
                int start = vertices.Count;
                for (int s = 0; s <= segments; s++)
                {
                    float angle = 360f * s / segments;
                    vertices.Add(BallTrajectory.LocalPosition(angle, profile[p].x, profile[p].y));
                    vertices.Add(BallTrajectory.LocalPosition(angle, profile[p + 1].x, profile[p + 1].y));
                }

                for (int s = 0; s < segments; s++)
                {
                    int a = start + s * 2;
                    int b = a + 1;
                    int c = a + 2;
                    int d = a + 3;
                    triangles.AddRange(new[] { a, b, c, b, d, c });
                }
            }

            var mesh = new Mesh { name = "Bowl" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>메시 에셋을 저장한다. 이미 있으면 내용만 바꿔서 GUID를 유지한다.</summary>
        private static Mesh SaveMesh(string name, Mesh source)
        {
            string path = $"{MeshFolder}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(source, path);
                return source;
            }

            existing.Clear();
            existing.SetVertices(source.vertices);
            existing.SetTriangles(source.triangles, 0);
            existing.SetNormals(source.normals);
            existing.RecalculateBounds();
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(source);
            return existing;
        }

        // ---------- 오브젝트 ----------

        private static Transform BuildBallPrefab(Material material)
        {
            var ball = CreatePrimitive("Ball", PrimitiveType.Sphere, null, Vector3.zero, Vector3.one * (BallRadius * 2f), material);
            try
            {
                return PrefabUtility.SaveAsPrefabAsset(ball, BallPrefabPath).transform;
            }
            finally
            {
                Object.DestroyImmediate(ball);
            }
        }

        private static GameObject CreateMeshObject(string name, Transform parent, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        /// <summary>기본 도형. 판정에 물리를 쓰지 않으므로 콜라이더는 뗀다.</summary>
        private static GameObject CreatePrimitive(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        /// <summary>
        /// 번호 라벨. 임시 모델이라 TMP 리소스를 가져오지 않고 내장 TextMesh를 쓴다.
        /// 칸 기준 바깥 번호 띠 위에 눕히고, 글자 위쪽이 휠 바깥을 향하게 한다.
        /// </summary>
        private static void CreateLabel(Transform pocket, string text, Font font, Color color, float scale)
        {
            var label = new GameObject("Label");
            label.transform.SetParent(pocket, false);
            label.transform.localPosition = new Vector3(0f, 0.002f, NumberRadius);
            label.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var mesh = label.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.font = font;
            mesh.fontSize = 64;
            mesh.characterSize = 0.009f * scale;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = color;
            label.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }

        private static Camera FindOrCreateCamera(string name, bool isMain)
        {
            GameObject go = GameObject.Find(name);
            if (go == null)
            {
                go = new GameObject(name);
            }

            var camera = GetOrAdd<Camera>(go);
            if (isMain)
            {
                go.tag = "MainCamera";
                GetOrAdd<AudioListener>(go);
            }

            return camera;
        }

        private static void ConfigureBackground(Camera camera)
        {
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.06f, 0.07f, 0.09f);
        }

        // ---------- 에셋 ----------

        private static Material Mat(string name, Color color, float smoothness, float metallic = 0f)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static T LoadOrCreate<T>(string path, Action<T> initialize) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                return asset;
            }

            asset = ScriptableObject.CreateInstance<T>();
            initialize?.Invoke(asset);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            return go.TryGetComponent(out T component) ? component : go.AddComponent<T>();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }

        private static void SetReference(SerializedObject so, string field, Object value)
        {
            SerializedProperty property = so.FindProperty(field)
                ?? throw new InvalidOperationException($"{so.targetObject.GetType().Name}에 {field} 필드가 없다.");
            property.objectReferenceValue = value;
        }

        private static void SetArray<T>(SerializedObject so, string field, IReadOnlyList<T> values) where T : Object
        {
            SerializedProperty property = so.FindProperty(field)
                ?? throw new InvalidOperationException($"{so.targetObject.GetType().Name}에 {field} 필드가 없다.");
            property.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }
    }
}
