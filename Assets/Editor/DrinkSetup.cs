using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ProjectCR.Editor
{
    /// <summary>
    /// 음료 데이터 에셋(기본 6종 + 목록)을 만들고 씬의 RouletteGame·HUD에 연결한다.
    /// 이미 있는 에셋은 덮어쓰지 않는다(Inspector에서 고친 이름·배율·그림을 지킨다).
    /// Model (CRR)/2. Use In Scene도 마지막에 씬 연결을 부른다.
    /// </summary>
    public static class DrinkSetup
    {
        private const string DrinkFolder = "Assets/Data/Drinks";
        private const string CatalogPath = "Assets/Data/DrinkCatalog.asset";
        private const string ScenePath = "Assets/Scenes/Roulette.unity";
        private const string CocktailPrefabFolder = "Assets/Prefabs/Cocktail";

        [MenuItem("Tools/ProjectCR/Drinks/Create Assets And Wire Scene")]
        public static void SetupAll()
        {
            DrinkCatalog catalog = CreateAssets();

            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            WireScene(catalog);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>
        /// 없는 에셋만 만든다. 목록 에셋을 돌려준다.
        /// 목록이 이미 있어도 확률 음료가 비어 있으면 기본 확률 음료 6종을 만들어 넣는다(위스키 프리팹도 연결).
        /// </summary>
        public static DrinkCatalog CreateAssets()
        {
            if (!AssetDatabase.IsValidFolder(DrinkFolder))
            {
                AssetDatabase.CreateFolder("Assets/Data", "Drinks");
            }

            var catalog = AssetDatabase.LoadAssetAtPath<DrinkCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = DrinkCatalog.Create(SaveAll(DrinkCatalog.CreateDefaultDrinks()));
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            if (catalog.ProbabilityDrinks.Count == 0)
            {
                List<ProbabilityDrinkDefinition> chances = SaveAll(ProbabilityDrinkDefinition.CreateDefaults());
                foreach (ProbabilityDrinkDefinition drink in chances)
                {
                    LinkModel(drink);
                }

                var so = new SerializedObject(catalog);
                SerializedProperty list = so.FindProperty("probabilityDrinks");
                list.arraySize = chances.Count;
                for (int i = 0; i < chances.Count; i++)
                {
                    list.GetArrayElementAtIndex(i).objectReferenceValue = chances[i];
                }

                so.ApplyModifiedPropertiesWithoutUndo();
            }

            AssetDatabase.SaveAssets();
            return catalog;
        }

        /// <summary>코드로 만든 음료를 Data/Drinks에 저장한다. 같은 이름 에셋이 있으면 그걸 쓴다(덮어쓰지 않는다).</summary>
        private static List<T> SaveAll<T>(IEnumerable<T> drinks) where T : ScriptableObject
        {
            var saved = new List<T>();
            foreach (T drink in drinks)
            {
                string path = $"{DrinkFolder}/{drink.name}.asset";
                var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset == null)
                {
                    AssetDatabase.CreateAsset(drink, path);
                    asset = drink;
                }
                else
                {
                    Object.DestroyImmediate(drink);
                }

                saved.Add(asset);
            }

            return saved;
        }

        /// <summary>확률 음료 ↔ 같은 색 위스키 프리팹(Prefabs/Cocktail/CRD_Whiskey_*{색}2).</summary>
        private static void LinkModel(ProbabilityDrinkDefinition drink)
        {
            if (drink.Model != null)
            {
                return;
            }

            string color = WhiskeyColor(drink.Group);
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab CRD_Whiskey", new[] { CocktailPrefabFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path).EndsWith(color + "2"))
                {
                    var so = new SerializedObject(drink);
                    so.FindProperty("model").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    so.ApplyModifiedPropertiesWithoutUndo();
                    return;
                }
            }
        }

        private static string WhiskeyColor(DrinkGroup group)
        {
            switch (group)
            {
                case DrinkGroup.Column:
                    return "CoralOrange";
                case DrinkGroup.Dozen:
                    return "Amber";
                case DrinkGroup.Parity:
                    return "Emerald";
                case DrinkGroup.Color:
                    return "Ruby";
                case DrinkGroup.Range:
                    return "SkyBlue";
                default:
                    return "Violet";
            }
        }

        /// <summary>열린 씬의 RouletteGame에 음료 목록을 연결한다. 음료를 고르는 건 서빙 로봇이다(DrinkServeSetup).</summary>
        public static void WireScene(DrinkCatalog catalog)
        {
            var game = Object.FindFirstObjectByType<RouletteGame>()
                ?? throw new InvalidOperationException("씬에 RouletteGame이 없다.");

            var so = new SerializedObject(game);
            so.FindProperty("drinkCatalog").objectReferenceValue = catalog;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
