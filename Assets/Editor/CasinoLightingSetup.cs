using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ProjectCR.Editor
{
    /// <summary>
    /// 카지노 조명(Docs/ClaudeCode_CasinoLighting_Plan.md): Art/Models/lighting.fbx의 기둥 벽등·3단 샹들리에에 URP 재질을 연결하고
    /// 프리팹·확인용 씬·Bloom 프로필을 만든다. 다시 돌려도 재질·프리팹·광원·Volume 수가 늘지 않는다(이름·경로로 찾아 갱신).
    ///
    /// 메뉴: Tools/ProjectCR/Art/Casino Lighting/Apply · Validate · Capture Preview. 배치 호출: <see cref="ApplyAll"/>, <see cref="Validate"/>, <see cref="CapturePreview"/>.
    /// - 재질: Materials/CasinoLighting/CRL_*.mat(URP Lit, Metallic). 투명은 Inspector와 같은 경로(BaseShaderGUI.SetMaterialKeywords + LitGUI)로 키워드·큐·ZWrite를 맞춘다.
    /// - FBX: 재질 이름으로 외부 remap(다른 슬롯 remap은 건드리지 않음). 카메라·라이트·애니메이션 임포트는 이 FBX에 없어서 끈다. 바뀐 게 있을 때만 다시 임포트.
    /// - 프리팹: Prefabs/CasinoLighting/ColumnSconce·CeilingChandelier. 조명 루트와 자식만, 블렌더 작업장 위치 오프셋만 없앤다(축 회전·스케일 유지).
    ///   피벗은 FBX 루트 그대로(벽등 = 기둥 접촉면 하단 중앙, 샹들리에 = 천장 캡 중앙). 실제 Light는 프리팹마다 1개(그림자 끔).
    /// - 이미 씬에 놓인 FBX 인스턴스: 위치·회전·개수는 그대로, 조명 루트마다 Light 1개와 유리 그림자 끔만 더한다.
    /// - 확인용 씬: Scenes/CasinoLightingPreview.unity(기둥 벽등 1, 높이 3m 천장 샹들리에 1(최하단 2.35m), 어두운 방, 반사 프로브 1회 캡처, 전용 Bloom Volume).
    ///   열린 씬에 저장 안 된 변경이 있으면 씬을 바꾸지 않고 확인용 씬 생성·캡처를 건너뛴다.
    /// </summary>
    public static class CasinoLightingSetup
    {
        public const string ModelPath = "Assets/Art/Models/lighting.fbx";
        public const string MaterialFolder = "Assets/Materials/CasinoLighting";
        public const string PrefabFolder = "Assets/Prefabs/CasinoLighting";
        public const string SconcePrefabPath = PrefabFolder + "/ColumnSconce.prefab";
        public const string ChandelierPrefabPath = PrefabFolder + "/CeilingChandelier.prefab";
        public const string PreviewScenePath = "Assets/Scenes/CasinoLightingPreview.unity";
        public const string PreviewFolder = "Assets/Scenes/CasinoLightingPreview";
        public const string VolumeProfilePath = "Assets/Settings/CasinoLightingVolumeProfile.asset";
        public const string CaptureFolder = "Temp/CasinoLightingCaptures";

        public const string SconceRoot = "CRL_Sconce_ROOT_180x130x520";
        public const string ChandelierRoot = "CRL_Chandelier_ROOT_D900_Drop650";
        private const string LightName = "CRL_Light";
        private const string LitShader = "Universal Render Pipeline/Lit";

        private const string WarmCore = "CRL_Warm_Light_Core";
        private static readonly string[] GlassMaterials = { "CRL_Fluted_Opal_Glass", "CRL_Faceted_Crystal" };

        private sealed class MaterialSpec
        {
            public string Name;
            public string Hex;
            public bool Transparent;
            public float Alpha;
            public float Metallic;
            public float Smoothness;
        }

        private static readonly MaterialSpec[] Specs =
        {
            new MaterialSpec { Name = "CRL_Antique_Brass", Hex = "#BC914E", Alpha = 1f, Metallic = 0.9f, Smoothness = 0.75f },
            new MaterialSpec { Name = "CRL_Brass_Recess", Hex = "#49351C", Alpha = 1f, Metallic = 0.8f, Smoothness = 0.55f },
            new MaterialSpec { Name = "CRL_Emerald_Enamel", Hex = "#12523B", Alpha = 1f, Metallic = 0.2f, Smoothness = 0.85f },
            new MaterialSpec { Name = "CRL_Fluted_Opal_Glass", Hex = "#F3DFC0", Transparent = true, Alpha = 0.65f, Metallic = 0f, Smoothness = 0.65f },
            new MaterialSpec { Name = "CRL_Faceted_Crystal", Hex = "#FFF3DE", Transparent = true, Alpha = 0.20f, Metallic = 0f, Smoothness = 0.95f },
            new MaterialSpec { Name = WarmCore, Hex = "#FFE1AA", Alpha = 1f, Metallic = 0f, Smoothness = 0.30f },
        };

        // 내부 전구 발광: HDR 기본색 #FFD39A, 노출 +2 stops(선형 RGB ×4).
        private const string EmissionHex = "#FFD39A";
        private const float EmissionStops = 2f;

        // 약 2700K 느낌의 따뜻한 빛(명시 색, 색온도 모드 사용 안 함).
        private static readonly Color WarmLightColor = new Color(1f, 0.72f, 0.42f);

        private sealed class LightSpec
        {
            public float Range;
            public float Intensity;
        }

        // 벽등은 기둥 바로 앞이라 1.6이면 기둥 면과 유리관이 하얗게 뜬다(캡처 확인) → 1.0.
        private static readonly LightSpec SconceLight = new LightSpec { Range = 2f, Intensity = 1f };
        private static readonly LightSpec ChandelierLight = new LightSpec { Range = 4f, Intensity = 3f };

        // ---------- 메뉴 ----------

        [MenuItem("Tools/ProjectCR/Art/Casino Lighting/Apply")]
        public static void ApplyMenu()
        {
            Debug.Log("[CasinoLighting] Apply\n" + ApplyAll());
        }

        [MenuItem("Tools/ProjectCR/Art/Casino Lighting/Rebuild Preview Scene")]
        public static void RebuildPreviewMenu()
        {
            Debug.Log("[CasinoLighting] Apply (확인용 씬 새로 만들기)\n" + ApplyAll(rebuildPreview: true));
        }

        [MenuItem("Tools/ProjectCR/Art/Casino Lighting/Validate")]
        public static void ValidateMenu()
        {
            string report = Validate(out int failures);
            if (failures > 0)
            {
                Debug.LogWarning($"[CasinoLighting] Validate: 실패 {failures}개\n{report}");
            }
            else
            {
                Debug.Log($"[CasinoLighting] Validate: 모두 통과\n{report}");
            }
        }

        [MenuItem("Tools/ProjectCR/Art/Casino Lighting/Capture Preview")]
        public static void CaptureMenu()
        {
            Debug.Log("[CasinoLighting] Capture\n" + CapturePreview());
        }

        // ---------- 적용 ----------

        /// <summary>전체 적용. 단계별 결과를 돌려준다. rebuildPreview면 확인용 씬과 반사 프로브를 새로 만든다(평소에는 있으면 그대로).</summary>
        public static string ApplyAll(bool rebuildPreview = false)
        {
            var log = new StringBuilder();
            EnsureFolder(MaterialFolder);
            EnsureFolder(PrefabFolder);

            Dictionary<string, Material> materials = CreateOrUpdateMaterials(log);
            ConfigureImporter(materials, log);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath)
                ?? throw new InvalidOperationException($"{ModelPath}가 없다.");
            BuildPrefab(model, SconceRoot, "ColumnSconce", SconcePrefabPath, SconceLight, log);
            BuildPrefab(model, ChandelierRoot, "CeilingChandelier", ChandelierPrefabPath, ChandelierLight, log);

            VolumeProfile profile = CreateOrUpdateVolumeProfile(log);
            UpdatePlacedInstances(log);
            BuildPreviewScene(profile, log, rebuildPreview);

            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        private static Dictionary<string, Material> CreateOrUpdateMaterials(StringBuilder log)
        {
            Shader lit = Shader.Find(LitShader) ?? throw new InvalidOperationException($"{LitShader} 셰이더가 없다.");
            var result = new Dictionary<string, Material>();
            foreach (MaterialSpec spec in Specs)
            {
                string path = $"{MaterialFolder}/{spec.Name}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                bool created = material == null;
                if (created)
                {
                    material = new Material(lit) { name = spec.Name };
                    AssetDatabase.CreateAsset(material, path);
                }
                else if (material.shader != lit)
                {
                    material.shader = lit;
                }

                ApplySpec(material, spec);
                EditorUtility.SetDirty(material);
                result[spec.Name] = material;
                log.AppendLine($"  재질 {(created ? "생성" : "갱신")}: {path}");
            }

            AssetDatabase.SaveAssets();
            return result;
        }

        /// <summary>URP Lit 재질 값을 맞추고 Inspector와 같은 함수로 키워드·렌더 큐·블렌드·ZWrite를 정리한다.</summary>
        private static void ApplySpec(Material material, MaterialSpec spec)
        {
            material.SetFloat("_WorkflowMode", 1f); // Metallic
            Color baseColor = Hex(spec.Hex);
            baseColor.a = spec.Alpha;
            material.SetColor("_BaseColor", baseColor);
            material.SetFloat("_Metallic", spec.Metallic);
            material.SetFloat("_Smoothness", spec.Smoothness);

            material.SetFloat("_Surface", spec.Transparent ? (float)BaseShaderGUI.SurfaceType.Transparent : (float)BaseShaderGUI.SurfaceType.Opaque);
            material.SetFloat("_Blend", (float)BaseShaderGUI.BlendMode.Premultiply);
            material.SetFloat("_Cull", (float)BaseShaderGUI.RenderFace.Front);
            material.SetFloat("_AlphaClip", 0f);

            if (spec.Name == WarmCore)
            {
                material.SetColor("_EmissionColor", EmissionColor());
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive; // 나중에 베이크할 수 있게(지금은 베이크 안 함)
            }
            else
            {
                material.SetColor("_EmissionColor", Color.black);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }

            // LitShader.ValidateMaterial과 같은 경로(디테일 맵은 쓰지 않는다).
            BaseShaderGUI.SetMaterialKeywords(material, LitGUI.SetMaterialKeywords);
        }

        /// <summary>HDR 발광색: 선형 공간에서 기본색 × 2^stops, 재질에는 감마로 저장(엔진이 선형으로 되돌린다). 선형 변환을 두 번 하지 않는다.</summary>
        private static Color EmissionColor()
        {
            Color linear = Hex(EmissionHex).linear * Mathf.Pow(2f, EmissionStops);
            return PlayerSettings.colorSpace == ColorSpace.Linear ? linear.gamma : Hex(EmissionHex) * Mathf.Pow(2f, EmissionStops);
        }

        private static void ConfigureImporter(Dictionary<string, Material> materials, StringBuilder log)
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter
                ?? throw new InvalidOperationException($"{ModelPath} 임포터가 없다.");
            bool changed = false;

            Dictionary<AssetImporter.SourceAssetIdentifier, Object> map = importer.GetExternalObjectMap();
            foreach (KeyValuePair<string, Material> pair in materials)
            {
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key);
                if (!map.TryGetValue(id, out Object current) || current != pair.Value)
                {
                    importer.AddRemap(id, pair.Value);
                    changed = true;
                }
            }

            // 이 FBX에는 카메라·라이트·애니메이션이 없다(점검 결과). 블렌더 촬영용이 섞여 들어오지 않게 끈다.
            if (importer.importCameras)
            {
                importer.importCameras = false;
                changed = true;
            }

            if (importer.importLights)
            {
                importer.importLights = false;
                changed = true;
            }

            bool hasClips = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().Any(c => !c.name.StartsWith("__preview"));
            if (importer.importAnimation && !hasClips)
            {
                importer.importAnimation = false;
                changed = true;
            }

            if (changed)
            {
                importer.SaveAndReimport();
                log.AppendLine("  FBX: 재질 remap·임포트 설정 갱신 후 다시 임포트");
            }
            else
            {
                log.AppendLine("  FBX: 바뀐 것 없음(다시 임포트 안 함)");
            }
        }

        private static void BuildPrefab(GameObject model, string rootName, string prefabName, string path, LightSpec lightSpec, StringBuilder log)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            {
                UpdatePrefab(rootName, path, lightSpec, log);
                return;
            }

            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                Transform source = instance.transform.Find(rootName)
                    ?? throw new InvalidOperationException($"lighting.fbx에 {rootName}이 없다.");

                var root = new GameObject(prefabName);
                SceneManager.MoveGameObjectToScene(root, scene);
                source.SetParent(root.transform, false);
                source.localPosition = Vector3.zero; // 블렌더 작업장 배치 오프셋만 없앤다(축 회전·스케일·부품 상대 위치 유지).

                SetGlassShadows(source);
                Bounds bounds = WorldBounds(source);
                EnsureLight(root.transform, LightPosition(rootName, bounds), lightSpec, out _);

                PrefabUtility.SaveAsPrefabAsset(root, path);
                log.AppendLine($"  프리팹: {path} (크기 {bounds.size.x * 1000:F0}×{bounds.size.y * 1000:F0}×{bounds.size.z * 1000:F0}mm)");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        /// <summary>이미 있는 프리팹: 내용을 열어 Light·유리 그림자만 맞춘다(오브젝트를 새로 만들지 않아 내부 ID와 사용자가 더한 것이 유지된다).</summary>
        private static void UpdatePrefab(string rootName, string path, LightSpec lightSpec, StringBuilder log)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Transform source = root.transform.Find(rootName)
                    ?? throw new InvalidOperationException($"{path}에 {rootName}이 없다. 프리팹을 지우고 다시 실행하면 새로 만든다.");
                int shadows = SetGlassShadows(source);
                bool lightChanged = EnsureLight(root.transform, LightPosition(rootName, WorldBounds(source)), lightSpec, out _);
                if (shadows > 0 || lightChanged)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    log.AppendLine($"  프리팹 갱신: {path} (유리 그림자 {shadows}, Light {(lightChanged ? "갱신" : "그대로")})");
                }
                else
                {
                    log.AppendLine($"  프리팹: {path} 바뀐 것 없음");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>벽등: 기둥 밖 앞쪽(-Z), 유리 높이. 샹들리에: 최하단 5cm 아래.</summary>
        private static Vector3 LightPosition(string rootName, Bounds bounds)
        {
            return rootName == SconceRoot
                ? new Vector3(bounds.center.x, bounds.center.y + 0.05f, bounds.min.z - 0.12f)
                : new Vector3(bounds.center.x, bounds.min.y - 0.05f, bounds.center.z);
        }

        /// <summary>parent 바로 아래에 Light 1개(이름 CRL_Light)를 두고 값을 맞춘다. 이미 있고 값이 같으면 건드리지 않는다. 바뀌었으면 true.</summary>
        private static bool EnsureLight(Transform parent, Vector3 worldPosition, LightSpec spec, out Light light)
        {
            Transform existing = parent.Find(LightName);
            bool changed = existing == null;
            GameObject go = existing != null ? existing.gameObject : new GameObject(LightName);
            if (existing == null)
            {
                go.transform.SetParent(parent, false);
                Undo.RegisterCreatedObjectUndo(go, "Casino light");
            }

            light = go.GetComponent<Light>();
            if (light == null)
            {
                light = Undo.AddComponent<Light>(go);
                changed = true;
            }

            bool same = Vector3.Distance(go.transform.position, worldPosition) < 0.0005f
                        && Quaternion.Angle(go.transform.rotation, Quaternion.identity) < 0.01f
                        && light.type == LightType.Point && Mathf.Approximately(light.range, spec.Range)
                        && Mathf.Approximately(light.intensity, spec.Intensity) && light.color == WarmLightColor
                        && !light.useColorTemperature && light.shadows == LightShadows.None
                        && light.lightmapBakeType == LightmapBakeType.Mixed;
            if (same && !changed)
            {
                return false;
            }

            Undo.RecordObject(go.transform, "Casino light");
            Undo.RecordObject(light, "Casino light");
            go.transform.SetPositionAndRotation(worldPosition, Quaternion.identity);
            light.type = LightType.Point;
            light.range = spec.Range;
            light.intensity = spec.Intensity;
            light.color = WarmLightColor;
            light.useColorTemperature = false;
            light.shadows = LightShadows.None;
            light.lightmapBakeType = LightmapBakeType.Mixed; // 정적인 실제 씬에서 나중에 베이크할 수 있게(지금 베이크하지 않음)
            PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(light);
            return true;
        }

        /// <summary>유리 전용 렌더러(모든 슬롯이 유리)만 그림자 투사를 끈다. 황동 등은 그대로.</summary>
        private static int SetGlassShadows(Transform root)
        {
            int count = 0;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                bool glassOnly = materials.Length > 0 && materials.All(m => m != null && GlassMaterials.Contains(m.name));
                if (glassOnly && renderer.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off)
                {
                    Undo.RecordObject(renderer, "Glass shadows off");
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                    count++;
                }
            }

            return count;
        }

        private static VolumeProfile CreateOrUpdateVolumeProfile(StringBuilder log)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumeProfilePath);
            }

            if (!profile.TryGet(out Bloom bloom))
            {
                bloom = profile.Add<Bloom>(false);
                bloom.name = "Bloom";
                AssetDatabase.AddObjectToAsset(bloom, profile);
            }

            bloom.active = true;
            bloom.threshold.Override(1f);
            bloom.intensity.Override(0.2f);
            bloom.scatter.Override(0.5f);
            EditorUtility.SetDirty(bloom);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            log.AppendLine($"  Volume 프로필: {VolumeProfilePath} (Bloom threshold 1, intensity 0.2, scatter 0.5)");
            return profile;
        }

        /// <summary>
        /// 열린 씬에 이미 놓인 lighting.fbx 인스턴스: 위치·회전·개수는 그대로 두고, 조명 루트마다 Light 1개와 유리 그림자 끔만 더한다.
        /// 재질은 FBX remap으로 연결된다. 씬이 원래 저장된 상태였을 때만 저장한다(사용자의 미저장 변경을 대신 저장하지 않는다).
        /// </summary>
        private static void UpdatePlacedInstances(StringBuilder log)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || scene.path == PreviewScenePath)
                {
                    continue;
                }

                bool wasDirty = scene.isDirty;
                int touched = 0;
                foreach (GameObject top in scene.GetRootGameObjects())
                {
                    foreach (Transform t in top.GetComponentsInChildren<Transform>(true))
                    {
                        if (!PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject)
                            || AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject)) != ModelPath)
                        {
                            continue;
                        }

                        foreach (string rootName in new[] { SconceRoot, ChandelierRoot })
                        {
                            Transform root = t.Find(rootName);
                            if (root == null)
                            {
                                continue;
                            }

                            int shadows = SetGlassShadows(root);
                            Bounds bounds = WorldBounds(root);
                            bool lightChanged = EnsureLight(root, LightPosition(rootName, bounds), rootName == SconceRoot ? SconceLight : ChandelierLight, out _);
                            if (shadows > 0 || lightChanged)
                            {
                                log.AppendLine($"  씬 {scene.name}: {t.name}/{rootName} Light {(lightChanged ? "추가/갱신" : "그대로")}, 유리 그림자 끔 {shadows}");
                                touched++;
                            }
                            else
                            {
                                log.AppendLine($"  씬 {scene.name}: {t.name}/{rootName} 바뀐 것 없음");
                            }
                        }
                    }
                }

                if (touched > 0)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!wasDirty)
                    {
                        EditorSceneManager.SaveScene(scene);
                        log.AppendLine($"  씬 {scene.path} 저장");

                        // Undo 기록이 한 프레임 뒤에 씬을 다시 더럽힌다(내용은 이미 저장됨) → 다음 갱신에서 한 번 더 저장.
                        string path = scene.path;
                        EditorApplication.delayCall += () =>
                        {
                            Scene again = SceneManager.GetSceneByPath(path);
                            if (again.IsValid() && again.isLoaded && again.isDirty)
                            {
                                EditorSceneManager.SaveScene(again);
                            }
                        };
                    }
                    else
                    {
                        log.AppendLine($"  씬 {scene.path}: 원래 저장 안 된 변경이 있어 저장하지 않음(직접 저장 필요)");
                    }
                }
            }
        }

        // ---------- 확인용 씬 ----------

        private static bool AnyLoadedSceneDirty()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).isDirty)
                {
                    return true;
                }
            }

            return false;
        }

        private static void BuildPreviewScene(VolumeProfile profile, StringBuilder log, bool rebuild)
        {
            if (!rebuild && File.Exists(PreviewScenePath))
            {
                log.AppendLine($"  확인용 씬: {PreviewScenePath} 이미 있음(프리팹 인스턴스라 재질·Light 변경은 저절로 반영)");
                return;
            }

            if (AnyLoadedSceneDirty())
            {
                log.AppendLine("  확인용 씬: 열린 씬에 저장 안 된 변경이 있어 건너뜀");
                return;
            }

            SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                RenderSettings.skybox = null;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.035f, 0.032f, 0.03f);
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.fog = false;

                Material floor = PreviewMaterial("CRL_Preview_Floor", "#2A2420", 0.35f);
                Material wall = PreviewMaterial("CRL_Preview_Wall", "#3B302A", 0.2f);
                Material column = PreviewMaterial("CRL_Preview_Column", "#4A3E36", 0.45f);
                Material ceiling = PreviewMaterial("CRL_Preview_Ceiling", "#24201D", 0.1f);

                var room = new GameObject("Room");
                Primitive(PrimitiveType.Plane, "Floor", room.transform, new Vector3(0f, 0f, 0f), Vector3.zero, new Vector3(1.2f, 1f, 1.2f), floor);
                Primitive(PrimitiveType.Plane, "Ceiling", room.transform, new Vector3(0f, 3f, 0f), new Vector3(180f, 0f, 0f), new Vector3(1.2f, 1f, 1.2f), ceiling);
                Primitive(PrimitiveType.Cube, "BackWall", room.transform, new Vector3(0f, 1.5f, 2.6f), Vector3.zero, new Vector3(12f, 3f, 0.2f), wall);
                Primitive(PrimitiveType.Cube, "Column", room.transform, new Vector3(-1.4f, 1.5f, 0.6f), Vector3.zero, new Vector3(0.5f, 3f, 0.5f), column);

                // 벽등: 기둥 앞면(z = 0.35)에 붙여 -Z(카메라 쪽)를 향한다. 샹들리에: 천장 3m, 최하단 2.35m.
                var sconce = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(SconcePrefabPath), scene);
                sconce.transform.position = new Vector3(-1.4f, 1.45f, 0.35f);
                var chandelier = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ChandelierPrefabPath), scene);
                chandelier.transform.position = new Vector3(0.8f, 3f, 0f);

                var cameraGo = new GameObject("PreviewCamera");
                var camera = cameraGo.AddComponent<Camera>();
                camera.fieldOfView = 45f;
                camera.allowHDR = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.02f, 0.02f, 0.02f);
                camera.nearClipPlane = 0.05f;
                var cameraData = cameraGo.AddComponent<UniversalAdditionalCameraData>();
                cameraData.renderPostProcessing = true;
                cameraGo.tag = "MainCamera";
                cameraGo.transform.position = new Vector3(-0.2f, 1.55f, -4.2f);
                cameraGo.transform.LookAt(new Vector3(-0.2f, 1.95f, 0.3f));

                var volumeGo = new GameObject("CasinoLightingVolume");
                var volume = volumeGo.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.sharedProfile = profile;

                // 반사 프로브 1개: 방 전체를 덮고 매 프레임 갱신하지 않는다(한 번 구워 둔 큐브맵).
                var probeGo = new GameObject("ReflectionProbe");
                probeGo.transform.position = new Vector3(0f, 1.5f, 0f);
                var probe = probeGo.AddComponent<ReflectionProbe>();
                probe.size = new Vector3(12f, 3.2f, 6f);
                probe.mode = ReflectionProbeMode.Custom;
                probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
                probe.boxProjection = true;

                EditorSceneManager.SaveScene(scene, PreviewScenePath);
                EnsureFolder(PreviewFolder);
                string cubemapPath = $"{PreviewFolder}/ReflectionProbe-0.exr";
                if (Lightmapping.BakeReflectionProbe(probe, cubemapPath))
                {
                    AssetDatabase.ImportAsset(cubemapPath);
                    probe.customBakedTexture = AssetDatabase.LoadAssetAtPath<Texture>(cubemapPath);
                    log.AppendLine($"  반사 프로브 1회 캡처: {cubemapPath}");
                }
                else
                {
                    log.AppendLine("  반사 프로브 캡처 실패");
                }

                EditorSceneManager.SaveScene(scene, PreviewScenePath);
                log.AppendLine($"  확인용 씬: {PreviewScenePath}");
            }
            finally
            {
                EditorSceneManager.RestoreSceneManagerSetup(setup);
            }
        }

        private static Material PreviewMaterial(string name, string hex, float smoothness)
        {
            string folder = $"{MaterialFolder}/Preview";
            EnsureFolder(folder);
            string path = $"{folder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find(LitShader)) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", Hex(hex));
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", 0f);
            BaseShaderGUI.SetMaterialKeywords(material, LitGUI.SetMaterialKeywords);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void Primitive(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 euler, Vector3 scale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(euler));
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(go.GetComponent<Collider>());
        }

        // ---------- 캡처 ----------

        /// <summary>
        /// 확인용 씬을 잠시 열어 실제 URP 렌더(카메라 → RenderTexture)를 PNG로 저장한다. 열린 씬에 저장 안 된 변경이 있으면 하지 않는다.
        /// 원래 열려 있던 씬 구성은 끝나면 되돌린다. 비교용: 기본(발광+Bloom+Light), Bloom 끔, Light 끔, 가까이(벽등·샹들리에), 옆·아래 각도.
        /// </summary>
        public static string CapturePreview()
        {
            if (AnyLoadedSceneDirty())
            {
                return "열린 씬에 저장 안 된 변경이 있어 캡처하지 않음";
            }

            var log = new StringBuilder();
            Directory.CreateDirectory(CaptureFolder);
            SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
            bool asyncCompile = ShaderUtil.allowAsyncCompilation;
            try
            {
                // 셰이더를 백그라운드에서 컴파일하는 동안은 투명 재질이 빠진 채 찍힌다 → 캡처 동안 동기 컴파일 + 버리는 한 장.
                ShaderUtil.allowAsyncCompilation = false;
                EditorSceneManager.OpenScene(PreviewScenePath, OpenSceneMode.Single);
                Camera camera = GameObject.Find("PreviewCamera").GetComponent<Camera>();
                Volume volume = GameObject.Find("CasinoLightingVolume").GetComponent<Volume>();
                Light[] lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
                Vector3 homePosition = camera.transform.position;
                Quaternion homeRotation = camera.transform.rotation;

                Vector3 sconce = new Vector3(-1.4f, 1.71f, 0.29f);
                Vector3 chandelier = new Vector3(0.8f, 2.67f, 0f);

                Shot(camera, null, "_warmup", homePosition, homeRotation);
                Shot(camera, log, "01_overview_game_distance", homePosition, homeRotation);
                volume.enabled = false;
                Shot(camera, log, "02_overview_no_bloom", homePosition, homeRotation);
                volume.enabled = true;
                foreach (Light light in lights)
                {
                    light.enabled = false;
                }

                Shot(camera, log, "03_overview_lights_off_emission_only", homePosition, homeRotation);
                foreach (Light light in lights)
                {
                    light.enabled = true;
                }

                Shot(camera, log, "04_sconce_close", Look(new Vector3(-0.95f, 1.75f, -0.45f), sconce));
                Shot(camera, log, "05_chandelier_close", Look(new Vector3(0.8f, 2.25f, -1.35f), chandelier));
                Shot(camera, log, "06_chandelier_below_side", Look(new Vector3(2.1f, 1.3f, -0.9f), chandelier));
                Shot(camera, log, "07_sconce_side", Look(new Vector3(-2.3f, 1.5f, -0.6f), sconce));
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = asyncCompile;
                EditorSceneManager.RestoreSceneManagerSetup(setup);
            }

            return log.ToString();
        }

        private static (Vector3, Quaternion) Look(Vector3 from, Vector3 at) => (from, Quaternion.LookRotation(at - from, Vector3.up));

        private static void Shot(Camera camera, StringBuilder log, string name, (Vector3 position, Quaternion rotation) pose) =>
            Shot(camera, log, name, pose.position, pose.rotation);

        private static void Shot(Camera camera, StringBuilder log, string name, Vector3 position, Quaternion rotation)
        {
            camera.transform.SetPositionAndRotation(position, rotation);
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture.active = rt;
            var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            texture.Apply();
            string path = Path.Combine(CaptureFolder, name + ".png");
            File.WriteAllBytes(path, texture.EncodeToPNG());
            RenderTexture.active = null;
            camera.targetTexture = null;
            Object.DestroyImmediate(texture);
            rt.Release();
            Object.DestroyImmediate(rt);
            log?.AppendLine($"  {Path.GetFullPath(path)}");
        }

        // ---------- 검증 ----------

        /// <summary>실제 재질 상태·FBX remap·렌더러 슬롯·프리팹 크기·피벗·광원·Volume·씬 인스턴스를 검사한다.</summary>
        public static string Validate(out int failures)
        {
            var report = new StringBuilder();
            int fail = 0;

            void Check(bool ok, string what)
            {
                report.AppendLine((ok ? "  OK   " : "  FAIL ") + what);
                if (!ok)
                {
                    fail++;
                }
            }

            // 1. 재질
            var materials = new Dictionary<string, Material>();
            foreach (MaterialSpec spec in Specs)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/{spec.Name}.mat");
                Check(material != null, $"재질 {spec.Name} 있음");
                if (material == null)
                {
                    continue;
                }

                materials[spec.Name] = material;
                Check(material.shader != null && material.shader.name == LitShader, $"{spec.Name}: URP Lit");
                Check(Mathf.Approximately(material.GetFloat("_WorkflowMode"), 1f), $"{spec.Name}: Metallic workflow");
                Color expected = Hex(spec.Hex);
                Color actual = material.GetColor("_BaseColor");
                Check(Approx(actual.r, expected.r) && Approx(actual.g, expected.g) && Approx(actual.b, expected.b) && Approx(actual.a, spec.Alpha),
                    $"{spec.Name}: 기본색 {spec.Hex} 알파 {spec.Alpha}");
                bool transparent = material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT");
                Check(transparent == spec.Transparent, $"{spec.Name}: {(spec.Transparent ? "Transparent" : "Opaque")} 키워드");
                if (spec.Transparent)
                {
                    // URP 17.3: Premultiply는 키워드가 아니라 블렌드 계수(One, OneMinusSrcAlpha)로 구현된다.
                    // _ALPHAPREMULTIPLY_ON은 Alpha/Additive의 Preserve Specular 전용이라 여기선 꺼져 있어야 Inspector와 같다.
                    Check(Mathf.Approximately(material.GetFloat("_Blend"), (float)BaseShaderGUI.BlendMode.Premultiply)
                          && Mathf.Approximately(material.GetFloat("_SrcBlend"), (float)BlendMode.One)
                          && Mathf.Approximately(material.GetFloat("_DstBlend"), (float)BlendMode.OneMinusSrcAlpha),
                        $"{spec.Name}: Premultiply 블렌드(One, OneMinusSrcAlpha)");
                    Check(material.renderQueue == (int)RenderQueue.Transparent, $"{spec.Name}: 렌더 큐 3000 (지금 {material.renderQueue})");
                    Check(material.GetTag("RenderType", false) == "Transparent", $"{spec.Name}: RenderType Transparent");
                    Check(Mathf.Approximately(material.GetFloat("_ZWrite"), 0f), $"{spec.Name}: ZWrite 끔");
                    Check(Mathf.Approximately(material.GetFloat("_Cull"), (float)BaseShaderGUI.RenderFace.Front), $"{spec.Name}: Render Face Front");
                    Check(!material.IsKeywordEnabled("_ALPHATEST_ON"), $"{spec.Name}: Alpha Clipping 끔");
                }
                else
                {
                    Check(material.renderQueue == (int)RenderQueue.Geometry, $"{spec.Name}: 렌더 큐 2000 (지금 {material.renderQueue})");
                }

                bool emissive = spec.Name == WarmCore;
                Check(material.IsKeywordEnabled("_EMISSION") == emissive, $"{spec.Name}: _EMISSION {(emissive ? "켬" : "끔")}");
                if (emissive)
                {
                    Color e = material.GetColor("_EmissionColor");
                    Color baseLinear = e.linear / Mathf.Pow(2f, EmissionStops);
                    Color expectedBase = Hex(EmissionHex);
                    Check(Approx(baseLinear.gamma.r, expectedBase.r, 0.01f) && Approx(baseLinear.gamma.g, expectedBase.g, 0.01f) && Approx(baseLinear.gamma.b, expectedBase.b, 0.01f),
                        $"{spec.Name}: 발광 {EmissionHex} +{EmissionStops} stops");
                    Check((material.globalIlluminationFlags & MaterialGlobalIlluminationFlags.EmissiveIsBlack) == 0
                          && (material.globalIlluminationFlags & MaterialGlobalIlluminationFlags.BakedEmissive) != 0,
                        $"{spec.Name}: GI 플래그 BakedEmissive(EmissiveIsBlack 아님)");
                }

                // Inspector를 열었다 닫는 것과 같은 정리를 복제본에 다시 해도 상태가 같아야 한다.
                var clone = new Material(material);
                BaseShaderGUI.SetMaterialKeywords(clone, LitGUI.SetMaterialKeywords);
                bool stable = clone.shaderKeywords.OrderBy(k => k).SequenceEqual(material.shaderKeywords.OrderBy(k => k))
                              && clone.renderQueue == material.renderQueue
                              && clone.GetTag("RenderType", false) == material.GetTag("RenderType", false)
                              && clone.globalIlluminationFlags == material.globalIlluminationFlags
                              && Mathf.Approximately(clone.GetFloat("_SrcBlend"), material.GetFloat("_SrcBlend"))
                              && Mathf.Approximately(clone.GetFloat("_DstBlend"), material.GetFloat("_DstBlend"))
                              && Mathf.Approximately(clone.GetFloat("_ZWrite"), material.GetFloat("_ZWrite"));
                Object.DestroyImmediate(clone);
                Check(stable, $"{spec.Name}: Inspector 재검증 후에도 상태 같음");
            }

            // 2. FBX remap
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            Check(importer != null, "FBX 임포터");
            if (importer != null)
            {
                Dictionary<AssetImporter.SourceAssetIdentifier, Object> map = importer.GetExternalObjectMap();
                foreach (KeyValuePair<string, Material> pair in materials)
                {
                    map.TryGetValue(new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key), out Object remapped);
                    Check(remapped == pair.Value, $"FBX remap {pair.Key}");
                }

                Check(!importer.importCameras && !importer.importLights, "FBX 카메라·라이트 임포트 끔");
            }

            // 3. FBX 렌더러 슬롯(다시 임포트된 결과)
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var unknown = new HashSet<string>();
            var slotCounts = new Dictionary<string, int>();
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    string name = material != null ? material.name : "null";
                    if (material == null || !materials.TryGetValue(name, out Material ours) || ours != material)
                    {
                        unknown.Add(name);
                    }

                    slotCounts[name] = (slotCounts.TryGetValue(name, out int c) ? c : 0) + 1;
                }
            }

            Check(unknown.Count == 0, "FBX 모든 슬롯이 조명 재질 6개에 연결" + (unknown.Count > 0 ? $" (누락/다른 이름: {string.Join(", ", unknown)})" : ""));
            report.AppendLine("       슬롯 수: " + string.Join(", ", slotCounts.Select(p => $"{p.Key}×{p.Value}")));

            // 4. 프리팹
            ValidatePrefab(SconcePrefabPath, SconceRoot, new Vector3(0.18f, 0.52f, 0.13f), materials, Check, report, sconce: true);
            ValidatePrefab(ChandelierPrefabPath, ChandelierRoot, new Vector3(0.9f, 0.65f, 0.9f), materials, Check, report, sconce: false);

            // 5. Volume
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            Check(profile != null && profile.TryGet(out Bloom bloom) && bloom.active
                  && bloom.threshold.overrideState && Mathf.Approximately(bloom.threshold.value, 1f)
                  && bloom.intensity.overrideState && Mathf.Approximately(bloom.intensity.value, 0.2f)
                  && bloom.scatter.overrideState && Mathf.Approximately(bloom.scatter.value, 0.5f),
                "Volume 프로필 Bloom(1 / 0.2 / 0.5, override 켬)");
            Check(profile != null && profile.components.Count(c => c is Bloom) == 1, "Bloom 1개(중복 없음)");

            // 6. 열린 씬의 배치 인스턴스
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                foreach (GameObject top in scene.GetRootGameObjects())
                {
                    foreach (Transform t in top.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name != SconceRoot && t.name != ChandelierRoot)
                        {
                            continue;
                        }

                        int lightCount = t.GetComponentsInChildren<Light>(true).Length;
                        bool ok = t.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).All(m => m != null && materials.ContainsValue(m));
                        Check(ok && lightCount == 1, $"씬 {scene.name}: {t.parent?.name}/{t.name} 재질 연결, Light {lightCount}개");
                    }
                }
            }

            Check(File.Exists(PreviewScenePath), $"확인용 씬 {PreviewScenePath}");
            failures = fail;
            return report.ToString();
        }

        private static void ValidatePrefab(string path, string rootName, Vector3 expectedSize, Dictionary<string, Material> materials,
            Action<bool, string> check, StringBuilder report, bool sconce)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            check(prefab != null, $"프리팹 {path}");
            if (prefab == null)
            {
                return;
            }

            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                Transform root = instance.transform.Find(rootName);
                check(root != null && instance.transform.childCount == 2, $"{path}: 조명 루트 + Light만");
                if (root == null)
                {
                    return;
                }

                Bounds bounds = WorldBounds(root);
                Vector3 size = bounds.size;
                check(Vector3.Distance(size, expectedSize) < 0.01f,
                    $"{path}: 크기 {size.x * 1000:F0}×{size.y * 1000:F0}×{size.z * 1000:F0}mm (기대 {expectedSize.x * 1000:F0}×{expectedSize.y * 1000:F0}×{expectedSize.z * 1000:F0})");

                // 피벗: 벽등 = 기둥 접촉면(+Z 끝) 하단 중앙, 샹들리에 = 천장 캡(맨 위) 중앙. 위아래가 뒤집히지 않았는지도 본다.
                bool pivot = sconce
                    ? Approx(bounds.min.y, 0f, 0.005f) && Approx(bounds.max.z, 0f, 0.005f) && Approx(bounds.center.x, 0f, 0.005f)
                    : Approx(bounds.max.y, 0f, 0.005f) && Approx(bounds.center.x, 0f, 0.005f) && Approx(bounds.center.z, 0f, 0.005f);
                check(pivot, $"{path}: 피벗 위치 (범위 min {bounds.min:F3} max {bounds.max:F3})");

                Light[] lights = instance.GetComponentsInChildren<Light>(true);
                check(lights.Length == 1 && lights[0].type == LightType.Point && lights[0].shadows == LightShadows.None,
                    $"{path}: Point Light 1개, 그림자 끔 (range {(lights.Length > 0 ? lights[0].range : 0)})");
                if (lights.Length == 1)
                {
                    Vector3 p = lights[0].transform.position;
                    bool placed = sconce ? p.z < bounds.min.z : p.y < bounds.min.y;
                    check(placed, $"{path}: Light 위치 {p:F3} ({(sconce ? "기둥 밖 앞쪽" : "최하단 아래")})");
                }

                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                check(renderers.SelectMany(r => r.sharedMaterials).All(m => m != null && materials.ContainsValue(m)), $"{path}: 렌더러 {renderers.Length}개 모두 조명 재질");
                bool glassShadows = renderers.Where(r => r.sharedMaterials.All(m => m != null && GlassMaterials.Contains(m.name)))
                    .All(r => r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off);
                bool brassShadows = renderers.Where(r => r.sharedMaterials.Any(m => m != null && m.name == "CRL_Antique_Brass"))
                    .All(r => r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off);
                check(glassShadows && brassShadows, $"{path}: 유리 그림자 끔, 황동 그림자 유지");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        // ---------- 공통 ----------

        private static Bounds WorldBounds(Transform root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer r in renderers)
            {
                bounds.Encapsulate(r.bounds);
            }

            return bounds;
        }

        private static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out Color color);
            return color;
        }

        private static bool Approx(float a, float b, float tolerance = 0.002f) => Mathf.Abs(a - b) <= tolerance;

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
