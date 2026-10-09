using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ProjectCR.Editor
{
    /// <summary>
    /// 상점 데이터(기본 41종 + 목록)를 만들고 씬의 RouletteGame·HUD에 연결한다.
    /// 이미 있는 상품 에셋은 덮어쓰지 않는다(Inspector에서 고친 N·가격·재고를 지킨다).
    /// Model (CRR)/2. Use In Scene도 마지막에 씬 연결을 부른다.
    /// </summary>
    public static class ShopSetup
    {
        private const string ShopFolder = "Assets/Data/Shop";
        private const string CatalogPath = "Assets/Data/ShopCatalog.asset";
        private const string ScenePath = "Assets/Scenes/Roulette.unity";

        [MenuItem("Tools/ProjectCR/Shop/Create Assets And Wire Scene")]
        public static void SetupAll()
        {
            ShopCatalog catalog = CreateAssets();

            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            WireScene(catalog);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>없는 상품 에셋만 만들고, 목록이 없으면 만든다.</summary>
        public static ShopCatalog CreateAssets()
        {
            var existing = AssetDatabase.LoadAssetAtPath<ShopCatalog>(CatalogPath);
            if (existing != null)
            {
                return existing;
            }

            if (!AssetDatabase.IsValidFolder(ShopFolder))
            {
                AssetDatabase.CreateFolder("Assets/Data", "Shop");
            }

            var saved = new List<ShopItemDefinition>();
            foreach (ShopItemDefinition item in ShopItemDefinition.CreateDefaults())
            {
                string path = $"{ShopFolder}/{item.name}.asset";
                var asset = AssetDatabase.LoadAssetAtPath<ShopItemDefinition>(path);
                if (asset == null)
                {
                    AssetDatabase.CreateAsset(item, path);
                    asset = item;
                }
                else
                {
                    Object.DestroyImmediate(item);
                }

                saved.Add(asset);
            }

            ShopCatalog catalog = ShopCatalog.Create(saved);
            AssetDatabase.CreateAsset(catalog, CatalogPath);
            AssetDatabase.SaveAssets();
            return catalog;
        }

        // 규칙을 기본 정의로 다시 맞출 상품(2026-10-09: 추가 공·해골 방어를 소모품 → 특수 강화로).
        private static readonly string[] RuleSyncIds = { "extra_ball", "skull_shield" };

        /// <summary>
        /// 기존 상점 데이터를 기본 정의에 맞춘다(목록을 다시 만들지 않는다).
        /// - 기본 정의에 있는데 에셋이 없는 상품(딜러의 귀띔, 더블 업 쿠폰 등)은 만들어 목록에 넣는다.
        /// - RuleSyncIds 상품은 분류·효과·이름·값·가격·재고만 기본 정의로 옮긴다(아이콘·색·수동 문구는 그대로).
        /// 다시 돌려도 바뀐 것만 고친다.
        /// </summary>
        [MenuItem("Tools/ProjectCR/Shop/Sync Item Defaults")]
        public static void SyncItemDefaults()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ShopCatalog>(CatalogPath)
                ?? throw new InvalidOperationException($"{CatalogPath}가 없다.");
            var items = new List<ShopItemDefinition>(catalog.Items);
            var log = new List<string>();

            foreach (ShopItemDefinition source in ShopItemDefinition.CreateDefaults())
            {
                string path = $"{ShopFolder}/{source.name}.asset";
                var asset = AssetDatabase.LoadAssetAtPath<ShopItemDefinition>(path);
                if (asset == null)
                {
                    AssetDatabase.CreateAsset(source, path);
                    items.Add(source);
                    log.Add($"+{source.Id}");
                    continue;
                }

                if (Array.IndexOf(RuleSyncIds, source.Id) >= 0 && asset.CopyRulesFrom(source))
                {
                    EditorUtility.SetDirty(asset);
                    log.Add($"~{asset.Id}");
                }

                if (!items.Contains(asset))
                {
                    items.Add(asset);
                    log.Add($"+목록 {asset.Id}");
                }

                Object.DestroyImmediate(source);
            }

            var so = new SerializedObject(catalog);
            SerializedProperty list = so.FindProperty("items");
            list.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log($"[ShopSetup] 상품 기본값 맞춤: {(log.Count > 0 ? string.Join(", ", log) : "바뀐 것 없음")} (목록 {items.Count}종)");
        }

        /// <summary>
        /// 이미 있는 교차 강화 에셋에 기본 정의의 발동 조건(Win/Gain, 기준 비율, 회전당 상한)만 옮긴다.
        /// 상품 ID로 짝을 맞추고 가격·아이콘·재고·강화량·수동 문구·목록은 건드리지 않는다. 다시 돌려도 바뀐 것만 고친다.
        /// </summary>
        [MenuItem("Tools/ProjectCR/Shop/Sync Cross Trigger Rules")]
        public static void SyncCrossTriggerRules()
        {
            var changed = new List<string>();
            foreach (ShopItemDefinition source in ShopItemDefinition.CreateDefaults())
            {
                if (source.Effect == ShopEffect.Cross)
                {
                    var asset = AssetDatabase.LoadAssetAtPath<ShopItemDefinition>($"{ShopFolder}/{source.name}.asset");
                    if (asset != null && asset.CopyCrossTriggerFrom(source))
                    {
                        EditorUtility.SetDirty(asset);
                        changed.Add($"{asset.Id}={asset.CrossTrigger}");
                    }
                }

                Object.DestroyImmediate(source);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[ShopSetup] 교차 발동 조건 갱신 {changed.Count}개: {string.Join(", ", changed)}");
        }

        /// <summary>열린 씬의 RouletteGame에 상점 목록과 상점 창을 연결한다. 창 컴포넌트는 HUD 오브젝트에 붙인다.</summary>
        public static void WireScene(ShopCatalog catalog)
        {
            var game = Object.FindFirstObjectByType<RouletteGame>()
                ?? throw new InvalidOperationException("씬에 RouletteGame이 없다.");
            var hud = Object.FindFirstObjectByType<RouletteHud>()
                ?? throw new InvalidOperationException("씬에 RouletteHud가 없다.");

            ShopView view = hud.GetComponent<ShopView>();
            if (view == null)
            {
                view = hud.gameObject.AddComponent<ShopView>();
            }

            var so = new SerializedObject(game);
            so.FindProperty("shopCatalog").objectReferenceValue = catalog;
            so.FindProperty("shopView").objectReferenceValue = view;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
