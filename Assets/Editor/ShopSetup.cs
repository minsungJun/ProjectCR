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
