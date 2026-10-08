using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ProjectCR.Editor
{
    /// <summary>
    /// 아트 모델(roulette_table_G.fbx)로 RouletteRoot_CRR 프리팹(룰렛 + 테이블)을 만들고 씬에 넣는다.
    /// 바닥·모델 안의 카메라와 조명·정지 공·칩 견본은 이 프리팹 안에서만 꺼 둔다(FBX 설정은 건드리지 않는다).
    ///
    /// 테이블: 베팅판 좌표(TableBetBoard)는 칸 메시 CRT_Bet_Color_01~36과 격자 메시에서 재고,
    /// 트레이 칩 칸마다 콜라이더(ChipTraySlot)를 두고, 잔액 표시 화면에 글자를 띄운다(TableChipView).
    ///
    /// - 회전판: CRR_Rotor_SPIN_Z를 로컬 Z축으로 돌린다(RoulettePresenter.rotor).
    /// - 칸: CRR_Pocket_{인덱스}_{번호} 메시. 이름의 인덱스·번호가 RouletteDefinition과 다르면 빌드를 멈춘다.
    ///   앵커 위치는 이름이 아니라 칸 메시의 실제 정점(중심 각도·반경·바닥 높이)에서 잰다.
    /// - 회전판 아래에 Y가 위인 PocketFrame을 만들어 앵커를 둔다. 공 계산(BallTrajectory)이 Y가 위인 좌표계를 쓰기 때문이다.
    /// - 볼 트랙 반경·높이와 공 크기는 모델에 놓인 공(CRR_Ball_20mm)의 자리에서 잰다.
    /// - 모델의 앞(칩 트레이·잔액 표시가 있는 +Z 쪽)에 카메라를 두고, 결과 칸도 그쪽(6시)에 세운다.
    /// </summary>
    public static class RouletteModelSetup
    {
        private const string ModelPath = "Assets/Art/roulette_table_G.fbx";
        private const string DefinitionPath = "Assets/Data/RouletteDefinition_Skull38.asset";
        private const string MotionPath = "Assets/Data/SpinMotionSettings_CRR.asset";
        private const string RoulettePrefabPath = "Assets/Prefabs/RouletteRoot_CRR.prefab";
        private const string BallPrefabPath = "Assets/Prefabs/Ball_CRR.prefab";
        private const string ScenePath = "Assets/Scenes/Roulette.unity";

        private const string RotorName = "CRR_Rotor_SPIN_Z";
        private const string ModelBallName = "CRR_Ball_20mm";
        private const string GroundName = "CRR_Studio_Ground";
        private const string TempRouletteName = "RouletteRoot";
        private const string GridName = "CRT_Bet_Grid_Merged_Zero";
        private const string DisplayScreenName = "CRT_Display_Black_Screen";
        private const string HoverMaterialPath = "Assets/Materials/Table_Hover.mat";

        // 잔액 표시 장치를 태블릿으로 바꾼다(2026-10-09). 태블릿은 새 모델에서 뽑아 낸 독립 프리팹이고,
        // 놓는 자리는 그 모델에서 잰 태블릿 아랫면 가운데(모델 로컬 좌표).
        private const string TabletPrefabPath = "Assets/Prefabs/CRT_Tablet.prefab";
        private const string BalanceDisplayRootName = "CRT_Balance_Display_ROOT";
        private const string TabletObjectName = "CRT_Tablet";
        private static readonly Vector3 TabletPositionInModel = new Vector3(-0.6600f, 0.8010f, 0.4053f);

        // 해골 칸 색(사용자 지정 #735198). 회전판 칸과 바깥 번호 띠 칸.
        private const string SkullPocketMaterialPath = "Assets/Materials/CRR_Skull_Pocket.mat";
        private static readonly Color SkullPocketColor = new Color32(0x73, 0x51, 0x98, 0xFF);
        private static readonly string[] SkullPocketNames = { "CRR_Pocket_19_SKULL", "CRR_Sector_19_SKULL" };

        // 카메라 자리 오브젝트(사용자가 씬에 만든 이름 그대로).
        private const string BettingPoseName = "CameraPostion_BettingBoard";
        private const string RoulettePoseName = "CameraPostion_Roulette";

        // 모델 칩 금액(CHIP_0001_* ~ CHIP_5000_*, 트레이 CRT_Chip_0001_* ~).
        private static readonly long[] Denominations = { 1, 5, 10, 50, 100, 500, 1000, 5000 };
        private const string ModelRouletteName = "RouletteRoot_CRR";

        [MenuItem("Tools/ProjectCR/Model (CRR)/Setup All")]
        public static void SetupAll()
        {
            BuildRoulettePrefab();
            UseInScene();
            Debug.Log("[RouletteModelSetup] 모델 룰렛 프리팹을 만들고 씬에 넣었다.");
        }

        [MenuItem("Tools/ProjectCR/Model (CRR)/1. Build Roulette Prefab")]
        public static void BuildRoulettePrefab()
        {
            var definition = AssetDatabase.LoadAssetAtPath<RouletteDefinition>(DefinitionPath)
                ?? throw new InvalidOperationException($"{DefinitionPath}가 없다. Tools/ProjectCR/1. Create Data Assets를 먼저 돌릴 것.");
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath)
                ?? throw new InvalidOperationException($"{ModelPath}가 없다.");

            // 크기가 작은 모델(볼 직경 0.8m)에 맞춘 연출 값. 처음 만들 때만 넣고, 이후 수정은 에셋에서 한다.
            SpinMotionSettings motion = LoadOrCreate<SpinMotionSettings>(MotionPath, m =>
            {
                m.bounceHeight = 0.015f;
                m.multiBallLaneSpacing = 0.022f;
                m.restAngle = 0f;
            });

            var root = new GameObject(ModelRouletteName);
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
                model.transform.SetParent(root.transform, false);

                Transform rotor = FindDeep(model.transform, RotorName)
                    ?? throw new InvalidOperationException($"모델에 {RotorName}이 없다.");

                // 회전판 중심이 RouletteRoot_CRR의 원점에 오게 모델을 옮긴다.
                model.transform.localPosition = -root.transform.InverseTransformPoint(rotor.position);

                foreach (Camera camera in model.GetComponentsInChildren<Camera>(true))
                {
                    camera.gameObject.SetActive(false);
                }

                foreach (Light light in model.GetComponentsInChildren<Light>(true))
                {
                    light.gameObject.SetActive(false);
                }

                SetActive(model.transform, GroundName, false);

                // 모델에 놓인 공: 트랙 위치와 공 크기를 재고 숨긴다. 실제 공은 프리팹으로 따로 만든다.
                Transform modelBall = FindDeep(model.transform, ModelBallName)
                    ?? throw new InvalidOperationException($"모델에 {ModelBallName}이 없다.");
                Vector3 ballOnTrack = root.transform.InverseTransformPoint(modelBall.position);
                float ballRadius = modelBall.GetComponent<Renderer>().bounds.extents.y;
                Transform ballPrefab = BuildBallPrefab(modelBall);
                modelBall.gameObject.SetActive(false);

                // 회전판을 따라 도는, Y가 위인 칸 좌표계.
                var frame = new GameObject("PocketFrame").transform;
                frame.SetPositionAndRotation(rotor.position, root.transform.rotation);
                frame.SetParent(rotor, true);

                var anchorsRoot = new GameObject("PocketAnchors").transform;
                anchorsRoot.SetParent(frame, false);

                int count = definition.PocketCount;
                var anchors = new PocketAnchor[count];
                var renderers = new Renderer[count];

                for (int i = 0; i < count; i++)
                {
                    PocketDefinition pocket = definition.GetPocket(i);
                    MeshFilter pocketMesh = FindPocket(rotor, i, pocket);
                    MeasurePocket(pocketMesh, frame, out float angle, out float radius, out float floor);

                    var anchor = new GameObject($"Anchor_{i:00}_{(pocket.IsSkull ? "SKULL" : pocket.Number.ToString())}");
                    anchor.transform.SetParent(anchorsRoot, false);
                    anchor.transform.localPosition = BallTrajectory.LocalPosition(angle, radius, floor + ballRadius);
                    anchor.transform.localRotation = Quaternion.Euler(0f, angle, 0f);
                    anchors[i] = anchor.AddComponent<PocketAnchor>();
                    anchors[i].Configure(i, pocket.Number);
                    renderers[i] = pocketMesh.GetComponent<Renderer>();
                }

                var ballsRoot = new GameObject("BallsRoot").transform;
                ballsRoot.SetParent(root.transform, false);

                // 공이 출발하는 자리 = 모델에 공이 놓여 있던 자리.
                motion.launchAngle = BallTrajectory.AngleOf(ballOnTrack);
                EditorUtility.SetDirty(motion);

                var presenter = root.AddComponent<RoulettePresenter>();
                var so = new SerializedObject(presenter);
                Set(so, "definition", definition);
                Set(so, "motion", motion);
                Set(so, "wheelPivot", frame);
                Set(so, "rotor", rotor);
                so.FindProperty("rotorAxis").vector3Value = Vector3.forward;
                Set(so, "ballsRoot", ballsRoot);
                Set(so, "ballPrefab", ballPrefab);
                SetArray(so, "pocketAnchors", anchors);
                SetArray(so, "pocketRenderers", renderers);
                so.FindProperty("rimRadius").floatValue = BallTrajectory.RadiusOf(ballOnTrack);
                so.FindProperty("rimHeight").floatValue = ballOnTrack.y;
                so.FindProperty("ballRadius").floatValue = ballRadius;
                so.ApplyModifiedPropertiesWithoutUndo();

                BuildTable(model.transform);
                ApplySkullPocketMaterial(model.transform);

                PrefabUtility.SaveAsPrefabAsset(root, RoulettePrefabPath);
                Debug.Log($"[RouletteModelSetup] 칸 {count}개, 트랙 반경 {BallTrajectory.RadiusOf(ballOnTrack):F3} 높이 {ballOnTrack.y:F3}, 공 반지름 {ballRadius:F3}");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// 이미 만든 RouletteRoot_CRR 프리팹에서 해골 칸 색만 바꾼다. 프리팹을 다시 빌드하지 않아 씬 오버라이드가 그대로 남는다.
        /// </summary>
        [MenuItem("Tools/ProjectCR/Model (CRR)/Skull Pocket Color")]
        public static void UpdateSkullPocketColor()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(RoulettePrefabPath);
            try
            {
                int changed = ApplySkullPocketMaterial(root.transform);
                PrefabUtility.SaveAsPrefabAsset(root, RoulettePrefabPath);
                Debug.Log($"[RouletteModelSetup] 해골 칸 색 #{ColorUtility.ToHtmlStringRGB(SkullPocketColor)} 렌더러 {changed}개");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// 해골 칸(회전판 칸 + 번호 띠 칸)에 전용 머티리얼을 건다. 모델에서는 검은 칸과 같은 CRR_Enamel_Obsidian을 함께 써서
        /// 그 머티리얼 색을 바꾸면 검은 칸까지 바뀌기 때문이다. 해골 그림(눈·코·이 홈)은 그대로 검정.
        /// 당첨 강조는 이 머티리얼 색에서 시작한다(RoulettePresenter가 원래 색을 머티리얼에서 읽는다).
        /// </summary>
        private static int ApplySkullPocketMaterial(Transform model)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(SkullPocketMaterialPath);
            if (material == null)
            {
                Renderer source = FindDeep(model, SkullPocketNames[0])?.GetComponent<Renderer>()
                    ?? throw new InvalidOperationException($"모델에 {SkullPocketNames[0]}이 없다.");
                material = new Material(source.sharedMaterial) { name = "CRR_Skull_Pocket" };
                AssetDatabase.CreateAsset(material, SkullPocketMaterialPath);
            }

            material.SetColor("_BaseColor", SkullPocketColor);
            EditorUtility.SetDirty(material);

            int changed = 0;
            foreach (string name in SkullPocketNames)
            {
                Renderer renderer = FindDeep(model, name)?.GetComponent<Renderer>()
                    ?? throw new InvalidOperationException($"모델에 {name}이 없다.");
                renderer.sharedMaterial = material;
                changed++;
            }

            return changed;
        }

        [MenuItem("Tools/ProjectCR/Model (CRR)/2. Use In Scene")]
        public static void UseInScene()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoulettePrefabPath)
                ?? throw new InvalidOperationException("RouletteRoot_CRR 프리팹이 없다. 1. Build Roulette Prefab을 먼저 돌릴 것.");

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            // 임시 모델 룰렛은 씬에서 뺀다(프리팹 파일은 남긴다).
            GameObject temp = GameObject.Find(TempRouletteName);
            if (temp != null)
            {
                Object.DestroyImmediate(temp);
            }

            GameObject roulette = GameObject.Find(ModelRouletteName)
                ?? (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);

            var game = Object.FindFirstObjectByType<RouletteGame>()
                ?? throw new InvalidOperationException("씬에 RouletteGame이 없다. Tools/ProjectCR/3. Setup Scene을 먼저 돌릴 것.");
            var so = new SerializedObject(game);
            Set(so, "presenter", roulette.GetComponent<RoulettePresenter>());
            so.ApplyModifiedPropertiesWithoutUndo();

            // 연출 카메라: 테이블 앞(+Z)에서 테이블 전체를 내려다본다. 화면 왼쪽에 룰렛, 오른쪽에 베팅판.
            // RouletteRoot_CRR의 원점은 룰렛 중심이고, 테이블 가운데는 그보다 -X로 0.7m.
            Camera main = GameObject.Find("Main Camera").GetComponent<Camera>();
            main.transform.position = new Vector3(-0.66f, 2.05f, 1.38f);
            main.transform.LookAt(new Vector3(-0.66f, 0f, 0.12f));
            main.fieldOfView = 40f;
            main.nearClipPlane = 0.05f;

            // 사용자가 씬에 둔 카메라 자리 오브젝트가 있으면 그 자리를 쓰고, 베팅판 ↔ 룰렛 이동을 연결한다.
            GameObject bettingPose = GameObject.Find(BettingPoseName);
            GameObject roulettePose = GameObject.Find(RoulettePoseName);
            if (bettingPose != null && roulettePose != null)
            {
                main.transform.SetPositionAndRotation(bettingPose.transform.position, bettingPose.transform.rotation);

                var rig = GetOrAdd<CameraRig>(main.gameObject);
                var rigSo = new SerializedObject(rig);
                Set(rigSo, "cameraTransform", main.transform);
                Set(rigSo, "bettingPose", bettingPose.transform);
                Set(rigSo, "roulettePose", roulettePose.transform);
                rigSo.ApplyModifiedPropertiesWithoutUndo();
                Set(so, "cameraRig", rig);
            }

            // 3D 테이블 입력은 씬의 RouletteGame 오브젝트에 둔다.
            var input = GetOrAdd<TableBetInput>(game.gameObject);
            var inputSo = new SerializedObject(input);
            Set(inputSo, "board", roulette.GetComponentInChildren<TableBetBoard>(true));
            Set(inputSo, "hud", Object.FindFirstObjectByType<RouletteHud>());
            SerializedProperty cameras = inputSo.FindProperty("cameras");
            cameras.arraySize = 2;
            cameras.GetArrayElementAtIndex(0).objectReferenceValue = main;
            cameras.GetArrayElementAtIndex(1).objectReferenceValue = GameObject.Find("TopDownCamera").GetComponent<Camera>();
            inputSo.ApplyModifiedPropertiesWithoutUndo();

            Set(so, "tableInput", input);
            Set(so, "tableView", roulette.GetComponentInChildren<TableChipView>(true));
            so.ApplyModifiedPropertiesWithoutUndo();

            // 탑다운: 화면 아래가 +Z(앞)가 되게 Y를 180° 돌린다.
            Camera top = GameObject.Find("TopDownCamera").GetComponent<Camera>();
            top.transform.position = new Vector3(-0.32f, 2f, 0.04f);
            top.transform.rotation = Quaternion.Euler(90f, 180f, 0f);
            top.orthographicSize = 0.56f;
            top.nearClipPlane = 0.05f;

            // 빛은 카메라 쪽 위에서 비춘다. 휠 뒤에서 카메라 쪽으로 비추면 광택 칸이 하얗게 번쩍인다.
            GameObject sun = GameObject.Find("Directional Light");
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(50f, 150f, 0f);
            }

            // 라운드 통과 보상(음료 목록·선택 창).
            DrinkSetup.WireScene(DrinkSetup.CreateAssets());

            // 상점(상품 목록·상점 창).
            ShopSetup.WireScene(ShopSetup.CreateAssets());

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>
        /// 테이블 베팅판·트레이·잔액 표시. 모델 루트 바로 아래 TableBoard(모델 로컬 좌표)에 둔다.
        /// 판 좌표: 3번 칸 왼쪽 위 모서리가 원점, 열 방향 = 1번 칸 → 34번 칸, 행 방향 = 3번 칸 → 1번 칸.
        /// </summary>
        private static void BuildTable(Transform model)
        {
            var boardObject = new GameObject("TableBoard");
            Transform board = boardObject.transform;
            board.SetParent(model, false);

            Vector3 Cell(int number) =>
                board.InverseTransformPoint(WorldBounds(Require(model, $"CRT_Bet_Color_{number:00}")).center);

            Vector3 one = Cell(1);
            Vector3 three = Cell(3);
            Vector3 thirtyFour = Cell(34);
            Vector3 columnVector = (thirtyFour - one) / 11f;
            Vector3 rowVector = (one - three) / 2f;
            Vector3 origin = three - columnVector * 0.5f - rowVector * 0.5f;
            float columnPitch = columnVector.magnitude;
            float rowPitch = rowVector.magnitude;
            Vector3 columnAxis = columnVector / columnPitch;
            Vector3 rowAxis = rowVector / rowPitch;

            Vector2 Units(Vector3 local)
            {
                Vector3 d = local - origin;
                return new Vector2(Vector3.Dot(d, columnAxis) / columnPitch, Vector3.Dot(d, rowAxis) / rowPitch);
            }

            // 격자 바깥 테두리 = 0번 칸 왼쪽 끝, 열 베팅 오른쪽 끝, 2배 줄 아랫변.
            Transform grid = Require(model, GridName);
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            foreach (Vector3 vertex in grid.GetComponent<MeshFilter>().sharedMesh.vertices)
            {
                Vector2 u = Units(board.InverseTransformPoint(grid.TransformPoint(vertex)));
                minX = Mathf.Min(minX, u.x);
                maxX = Mathf.Max(maxX, u.x);
                maxY = Mathf.Max(maxY, u.y);
            }

            // 더즌 글자가 더즌 줄 가운데에 있다.
            float dozenCenter = Units(board.InverseTransformPoint(WorldBounds(Require(model, "CRT_Bet_Dozen_0")).center)).y;

            var layout = new BoardLayout
            {
                zeroLeft = minX,
                columnRight = maxX,
                dozenBottom = 2f * dozenCenter - 3f,
                outsideBottom = maxY,
                spotRadiusX = 0.22f,
                spotRadiusY = 0.17f,
            };

            var tableBoard = boardObject.AddComponent<TableBetBoard>();
            tableBoard.Configure(origin, columnAxis, rowAxis, columnPitch, rowPitch, layout);
            Debug.Log($"[RouletteModelSetup] 베팅판: 열 {columnPitch:F4}m, 행 {rowPitch:F4}m, 0번 {layout.zeroLeft:F2}, 열 베팅 끝 {layout.columnRight:F2}, " +
                      $"더즌 아래 {layout.dozenBottom:F2}, 2배 줄 아래 {layout.outsideBottom:F2}, " +
                      $"빨강 표시 x {Units(board.InverseTransformPoint(WorldBounds(Require(model, "CRT_Bet_RED_Diamond")).center)).x:F2}(기대 5.0)");

            // 칩 견본(테이블 밖에 떠 있다)은 숨기고 판에 놓을 칩 모양으로 쓴다.
            var templates = new GameObject[Denominations.Length];
            var trayChips = new System.Collections.Generic.List<Transform>();
            var traySlot = new System.Collections.Generic.List<int>();
            var trayBounds = new Bounds?[Denominations.Length];

            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            {
                for (int d = 0; d < Denominations.Length; d++)
                {
                    if (t.name.StartsWith($"CHIP_{Denominations[d]:0000}_"))
                    {
                        templates[d] = t.gameObject;
                        t.gameObject.SetActive(false);
                    }
                    else if (t.name.StartsWith($"CRT_Chip_{Denominations[d]:0000}_"))
                    {
                        trayChips.Add(t);
                        traySlot.Add(d);
                        Bounds b = WorldBounds(t);
                        if (trayBounds[d].HasValue)
                        {
                            Bounds merged = trayBounds[d].Value;
                            merged.Encapsulate(b);
                            trayBounds[d] = merged;
                        }
                        else
                        {
                            trayBounds[d] = b;
                        }
                    }
                }
            }

            for (int d = 0; d < Denominations.Length; d++)
            {
                if (templates[d] == null || !trayBounds[d].HasValue)
                {
                    throw new InvalidOperationException($"모델에 {Denominations[d]} 칩 견본이나 트레이 칩이 없다.");
                }

                // 트레이 칸 클릭 영역.
                Bounds b = trayBounds[d].Value;
                var slot = new GameObject($"ChipSlot_{Denominations[d]}");
                slot.transform.SetParent(board, false);
                slot.transform.position = b.center;
                slot.AddComponent<BoxCollider>().size = b.size + new Vector3(0.012f, 0.02f, 0.012f);
                slot.AddComponent<ChipTraySlot>().Configure(Denominations[d]);
            }

            // 마우스가 가리키는 자리 표시: 얇은 금색 원판.
            var hover = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            hover.name = "HoverMarker";
            Object.DestroyImmediate(hover.GetComponent<Collider>());
            hover.transform.SetParent(board, false);
            hover.transform.localScale = new Vector3(0.022f, 0.0006f, 0.022f);
            hover.GetComponent<Renderer>().sharedMaterial = HoverMaterial();

            // 태블릿 프리팹이 있으면 옛 잔액 표시 장치를 끄고 태블릿을 쓴다(보유 금액 + 이번 판 베팅 + 버튼).
            TabletPanel tablet = AssetDatabase.LoadAssetAtPath<GameObject>(TabletPrefabPath) != null ? BuildTablet(model) : null;
            TextMesh balanceText = tablet != null ? tablet.BalanceValue : BuildBalanceText(model, board);

            var view = boardObject.AddComponent<TableChipView>();
            var so = new SerializedObject(view);
            Set(so, "board", tableBoard);
            SerializedProperty values = so.FindProperty("denominations");
            values.arraySize = Denominations.Length;
            for (int d = 0; d < Denominations.Length; d++)
            {
                values.GetArrayElementAtIndex(d).longValue = Denominations[d];
            }

            SetArray(so, "templates", templates);
            SetArray(so, "trayChips", trayChips.ToArray());
            SerializedProperty slots = so.FindProperty("trayChipSlot");
            slots.arraySize = traySlot.Count;
            for (int i = 0; i < traySlot.Count; i++)
            {
                slots.GetArrayElementAtIndex(i).intValue = traySlot[i];
            }

            so.FindProperty("chipHeight").floatValue = WorldBounds(templates[0].transform).size.y;
            Set(so, "hoverMarker", hover.transform);
            Set(so, "balanceText", balanceText);
            Set(so, "dueText", tablet != null ? tablet.DueValue : null);
            Set(so, "tablet", tablet);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 이미 만든 RouletteRoot_CRR 프리팹의 잔액 표시 장치(CRT_Balance_Display_ROOT)를 태블릿으로 바꾼다.
        /// 프리팹을 다시 빌드하지 않아 다른 씬 오버라이드는 그대로 남는다. 다시 돌려도 태블릿은 하나만 생긴다.
        /// </summary>
        [MenuItem("Tools/ProjectCR/Model (CRR)/Use Tablet Display")]
        public static void UseTabletDisplay()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(RoulettePrefabPath);
            try
            {
                TableChipView view = root.GetComponentInChildren<TableChipView>(true)
                    ?? throw new InvalidOperationException("RouletteRoot_CRR에 TableChipView가 없다.");
                Transform model = view.transform.parent;

                Transform old = model.Find(TabletObjectName);
                if (old != null)
                {
                    Object.DestroyImmediate(old.gameObject);
                }

                var so = new SerializedObject(view);
                if (so.FindProperty("balanceText").objectReferenceValue is TextMesh oldText && oldText.name == "BalanceText")
                {
                    Object.DestroyImmediate(oldText.gameObject);
                }

                TabletPanel tablet = BuildTablet(model);
                Set(so, "balanceText", tablet.BalanceValue);
                Set(so, "dueText", tablet.DueValue);
                Set(so, "tablet", tablet);
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, RoulettePrefabPath);
                Debug.Log("[RouletteModelSetup] 잔액 표시 장치를 태블릿으로 교체");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// 옛 잔액 표시 장치를 끄고 태블릿을 모델 아래에 놓는다. 숫자 글자와 버튼은 태블릿 프리팹(TabletPanel)에 들어 있다
        /// (Tools/ProjectCR/Tablet/Convert Labels And Buttons로 만든다).
        /// </summary>
        private static TabletPanel BuildTablet(Transform model)
        {
            Transform display = FindDeep(model, BalanceDisplayRootName);
            if (display != null)
            {
                display.gameObject.SetActive(false);
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TabletPrefabPath)
                ?? throw new InvalidOperationException($"{TabletPrefabPath}가 없다.");
            if (prefab.GetComponent<TabletPanel>() == null)
            {
                TabletSetup.ConvertTablet();
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TabletPrefabPath);
            }

            var tablet = (GameObject)PrefabUtility.InstantiatePrefab(prefab, model.gameObject.scene);
            tablet.name = TabletObjectName;
            tablet.transform.SetParent(model, false);
            tablet.transform.localPosition = TabletPositionInModel;
            tablet.transform.localRotation = Quaternion.identity;
            return tablet.GetComponent<TabletPanel>();
        }

        /// <summary>잔액 표시 화면 위에 눕힌 글자. 화면 면의 방향(정점 법선 평균)에 맞추고, 글자 위쪽은 플레이어 반대편(-Z).</summary>
        private static TextMesh BuildBalanceText(Transform model, Transform board)
        {
            Transform screen = Require(model, DisplayScreenName);
            Mesh mesh = screen.GetComponent<MeshFilter>().sharedMesh;

            Vector3 normal = Vector3.zero;
            foreach (Vector3 n in mesh.normals)
            {
                Vector3 world = screen.TransformDirection(n).normalized;
                if (world.y > 0.3f)
                {
                    normal += world;
                }
            }

            normal = normal.sqrMagnitude > 0f ? normal.normalized : Vector3.up;
            Bounds bounds = WorldBounds(screen);

            var label = new GameObject("BalanceText");
            label.transform.SetParent(board, false);
            label.transform.position = bounds.center + normal * (bounds.extents.y + 0.001f);
            label.transform.rotation = Quaternion.LookRotation(-normal, Vector3.ProjectOnPlane(Vector3.back, normal));

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var text = label.AddComponent<TextMesh>();
            text.text = "0";
            text.font = font;
            text.fontSize = 64;
            text.characterSize = 0.0035f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = new Color(1f, 0.74f, 0.3f);
            label.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            return text;
        }

        private static Material HoverMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(HoverMaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, HoverMaterialPath);
            }

            material.SetColor("_BaseColor", new Color(1f, 0.82f, 0.25f));
            material.SetFloat("_Smoothness", 0.6f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Bounds WorldBounds(Transform t)
        {
            Vector3[] vertices = t.GetComponent<MeshFilter>().sharedMesh.vertices;
            var bounds = new Bounds(t.TransformPoint(vertices[0]), Vector3.zero);
            foreach (Vector3 vertex in vertices)
            {
                bounds.Encapsulate(t.TransformPoint(vertex));
            }

            return bounds;
        }

        private static Transform Require(Transform parent, string name)
        {
            return FindDeep(parent, name) ?? throw new InvalidOperationException($"모델에 {name}이 없다.");
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            return go.TryGetComponent(out T component) ? component : go.AddComponent<T>();
        }

        /// <summary>회전판 아래 CRR_Pocket_{i:00}_{번호 또는 SKULL}. 이름이 정의와 맞지 않으면 멈춘다.</summary>
        private static MeshFilter FindPocket(Transform rotor, int index, PocketDefinition pocket)
        {
            string expected = $"CRR_Pocket_{index:00}_{(pocket.IsSkull ? "SKULL" : pocket.Number.ToString())}";
            string prefix = $"CRR_Pocket_{index:00}_";

            foreach (MeshFilter filter in rotor.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.name.StartsWith(prefix))
                {
                    continue;
                }

                if (filter.name != expected)
                {
                    throw new InvalidOperationException($"모델 칸 {filter.name}이 정의(칸 {index} = {expected})와 다르다. 휠 순서를 확인할 것.");
                }

                return filter;
            }

            throw new InvalidOperationException($"모델에 {expected}이 없다.");
        }

        /// <summary>칸 메시 정점을 frame 좌표로 옮겨 중심 각도, 중심 반경, 바닥 높이(높이 범위의 가운데)를 잰다.</summary>
        private static void MeasurePocket(MeshFilter pocket, Transform frame, out float angle, out float radius, out float floor)
        {
            Vector3[] vertices = pocket.sharedMesh.vertices;
            double sumX = 0;
            double sumZ = 0;
            float minR = float.MaxValue;
            float maxR = 0f;
            float minY = float.MaxValue;
            float maxY = float.MinValue;

            foreach (Vector3 vertex in vertices)
            {
                Vector3 p = frame.InverseTransformPoint(pocket.transform.TransformPoint(vertex));
                sumX += p.x;
                sumZ += p.z;
                float r = BallTrajectory.RadiusOf(p);
                minR = Mathf.Min(minR, r);
                maxR = Mathf.Max(maxR, r);
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
            }

            angle = BallTrajectory.AngleOf(new Vector3((float)(sumX / vertices.Length), 0f, (float)(sumZ / vertices.Length)));
            radius = (minR + maxR) * 0.5f;
            floor = (minY + maxY) * 0.5f;
        }

        /// <summary>모델 공 메시·머티리얼로 공 프리팹을 만든다. 크기는 모델 안에서의 세계 크기를 그대로 쓴다.</summary>
        private static Transform BuildBallPrefab(Transform modelBall)
        {
            var ball = new GameObject("Ball_CRR");
            try
            {
                ball.AddComponent<MeshFilter>().sharedMesh = modelBall.GetComponent<MeshFilter>().sharedMesh;
                ball.AddComponent<MeshRenderer>().sharedMaterials = modelBall.GetComponent<MeshRenderer>().sharedMaterials;
                ball.transform.localScale = modelBall.lossyScale;
                return PrefabUtility.SaveAsPrefabAsset(ball, BallPrefabPath).transform;
            }
            finally
            {
                Object.DestroyImmediate(ball);
            }
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name)
                {
                    return t;
                }
            }

            return null;
        }

        private static void SetActive(Transform parent, string name, bool active)
        {
            Transform target = FindDeep(parent, name) ?? throw new InvalidOperationException($"모델에 {name}이 없다.");
            target.gameObject.SetActive(active);
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

        private static void Set(SerializedObject so, string field, Object value)
        {
            SerializedProperty property = so.FindProperty(field)
                ?? throw new InvalidOperationException($"{so.targetObject.GetType().Name}에 {field} 필드가 없다.");
            property.objectReferenceValue = value;
        }

        private static void SetArray<T>(SerializedObject so, string field, T[] values) where T : Object
        {
            SerializedProperty property = so.FindProperty(field)
                ?? throw new InvalidOperationException($"{so.targetObject.GetType().Name}에 {field} 필드가 없다.");
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }
    }
}
