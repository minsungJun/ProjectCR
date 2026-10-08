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
