using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ProjectCR.Editor
{
    /// <summary>
    /// 칵테일 모델(cocktail.fbx)의 머티리얼을 부위별 재질 설정으로 만들어 FBX에 연결한다.
    ///
    /// FBX 안 머티리얼(CRD_*, CRR_Brass_Satin) 하나마다 같은 이름의 URP Lit 머티리얼을 Materials/Cocktail에 만든다.
    /// 색(RGB)은 FBX 머티리얼에서 가져오고, 표면 종류·Metallic·Smoothness·Alpha는 아래 부위 표를 따른다.
    /// 연결은 임포터의 외부 머티리얼 매핑(Remap)이라 리임포트해도 유지되고, 씬에 놓인 cocktail에도 그대로 적용된다.
    /// 이미 있는 머티리얼은 다시 돌리면 표 값으로 덮어쓴다(색도 FBX 값으로 돌아간다).
    /// 메뉴: Tools/ProjectCR/Art/Cocktail Materials.
    /// </summary>
    public static class CocktailMaterialSetup
    {
        private const string ModelPath = "Assets/Art/Models/cocktail.fbx";
        private const string MaterialFolder = "Assets/Materials/Cocktail";

        /// <summary>부위 하나의 재질 설정.</summary>
        private sealed class Finish
        {
            public string Name;
            public bool Transparent;
            public float Metallic;
            public float Smoothness;
            public float Alpha = 1f;

            /// <summary>그림자를 드리우는가. 투명 유리·얼음이 진한 그림자를 만들지 않게 끈다.</summary>
            public bool CastShadows = true;
        }

        private static readonly Finish Glass = new Finish { Name = "투명 유리잔", Transparent = true, Smoothness = 0.95f, Alpha = 0.14f, CastShadows = false };
        private static readonly Finish Ice = new Finish { Name = "얼음", Transparent = true, Smoothness = 0.85f, Alpha = 0.32f, CastShadows = false };
        private static readonly Finish ClearDrink = new Finish { Name = "맑은 음료", Transparent = true, Smoothness = 0.90f, Alpha = 0.75f };
        private static readonly Finish OrangeDrink = new Finish { Name = "주황색 음료", Smoothness = 0.65f };
        private static readonly Finish Brass = new Finish { Name = "금색 테두리·픽", Metallic = 1f, Smoothness = 0.80f };
        private static readonly Finish FruitPeel = new Finish { Name = "과일 껍질", Smoothness = 0.35f };
        private static readonly Finish Cherry = new Finish { Name = "체리", Smoothness = 0.75f };
        private static readonly Finish LeafFlower = new Finish { Name = "잎·꽃", Smoothness = 0.25f };

        // 표에 없는 빨대는 무광 플라스틱 정도로 둔다.
        private static readonly Finish Straw = new Finish { Name = "빨대(표 밖)", Smoothness = 0.45f };

        /// <summary>FBX 머티리얼 이름 → 부위. 여기 없는 머티리얼(바닥 CRR_Studio_Charcoal)은 건드리지 않는다.</summary>
        private static readonly Dictionary<string, Finish> Parts = new Dictionary<string, Finish>
        {
            // 유리잔(플루트 거품도 이 머티리얼을 쓴다).
            { "CRD_Clear_Crystal", Glass },
            { "CRD_Ice", Ice },

            // 맑은 음료 5종 + 주황 음료(허리케인).
            { "CRD_Violet", ClearDrink },
            { "CRD_SkyBlue", ClearDrink },
            { "CRD_Emerald", ClearDrink },
            { "CRD_Ruby", ClearDrink },
            { "CRD_Amber", ClearDrink },
            { "CRD_CoralOrange", OrangeDrink },

            // 받침·칼라·픽.
            { "CRR_Brass_Satin", Brass },

            // 과일: 껍질과 과육·속껍질·블루베리를 같은 설정으로.
            { "CRD_Lemon_Peel", FruitPeel },
            { "CRD_Lime_Peel", FruitPeel },
            { "CRD_Orange_Peel", FruitPeel },
            { "CRD_Pineapple_Rind", FruitPeel },
            { "CRD_Lemon_Flesh", FruitPeel },
            { "CRD_Lime_Flesh", FruitPeel },
            { "CRD_Citrus_Pith", FruitPeel },
            { "CRD_Pineapple_Flesh", FruitPeel },
            { "CRD_Blueberry", FruitPeel },

            { "CRD_Cherry_Red", Cherry },
            { "CRD_Cherry_Dark", Cherry },

            { "CRD_Mint_Green", LeafFlower },
            { "CRD_Leaf_Veins", LeafFlower },
            { "CRD_Viola_Purple", LeafFlower },
            { "CRD_Flower_Heart", LeafFlower },

            { "CRD_Dark_Straw", Straw },
        };

        [MenuItem("Tools/ProjectCR/Art/Cocktail Materials")]
        public static void Run()
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter
                ?? throw new InvalidOperationException($"{ModelPath}가 없다.");

            if (!AssetDatabase.IsValidFolder(MaterialFolder))
            {
                AssetDatabase.CreateFolder("Assets/Materials", "Cocktail");
            }

            // FBX 안 머티리얼의 색. 이미 매핑돼 있으면 원래 색은 FBX의 머티리얼 설명에서 다시 읽어야 하니 매핑을 먼저 지운다.
            ClearRemaps(importer);
            Dictionary<string, Color> embedded = EmbeddedColors();

            var log = new StringBuilder("[Cocktail] ");
            var unknown = new List<string>();
            foreach (KeyValuePair<string, Color> source in embedded)
            {
                if (!Parts.TryGetValue(source.Key, out Finish finish))
                {
                    unknown.Add(source.Key);
                    continue;
                }

                Material material = CreateOrUpdate(source.Key, source.Value, finish);
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), source.Key), material);
                log.Append($"{source.Key}→{finish.Name} ");
            }

            importer.SaveAndReimport();
            AssetDatabase.SaveAssets();

            if (unknown.Count > 0)
            {
                log.Append($"| 그대로 둠: {string.Join(", ", unknown)}");
            }

            Debug.Log(log.ToString());
        }

        private static void ClearRemaps(ModelImporter importer)
        {
            var remapped = new List<AssetImporter.SourceAssetIdentifier>();
            foreach (KeyValuePair<AssetImporter.SourceAssetIdentifier, Object> pair in importer.GetExternalObjectMap())
            {
                if (pair.Key.type == typeof(Material) && Parts.ContainsKey(pair.Key.name))
                {
                    remapped.Add(pair.Key);
                }
            }

            if (remapped.Count == 0)
            {
                return;
            }

            foreach (AssetImporter.SourceAssetIdentifier id in remapped)
            {
                importer.RemoveRemap(id);
            }

            importer.SaveAndReimport();
        }

        /// <summary>FBX 안에 들어 있는 머티리얼 이름과 기본색.</summary>
        private static Dictionary<string, Color> EmbeddedColors()
        {
            var colors = new Dictionary<string, Color>();
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
            {
                if (asset is Material material && !colors.ContainsKey(material.name))
                {
                    colors[material.name] = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : material.color;
                }
            }

            return colors;
        }

        private static Material CreateOrUpdate(string name, Color color, Finish finish)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            color.a = finish.Alpha;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_WorkflowMode", 1f); // Metallic
            material.SetFloat("_Metallic", finish.Metallic);
            material.SetFloat("_Smoothness", finish.Smoothness);
            material.SetFloat("_Surface", finish.Transparent ? 1f : 0f);
            material.SetFloat("_Blend", 0f); // Alpha
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_CastShadows", finish.CastShadows ? 1f : 0f);

            // 투명 유리는 알파가 낮아도 반사 하이라이트는 남긴다(URP "Preserve Specular Lighting").
            material.SetFloat("_BlendModePreserveSpecular", 1f);

            // 블렌드 상태·렌더 큐·키워드를 URP 머티리얼 인스펙터와 같은 방식으로 맞춘다.
            BaseShaderGUI.SetupMaterialBlendMode(material);
            BaseShaderGUI.SetMaterialKeywords(material, UnityEditor.Rendering.Universal.ShaderGUI.LitGUI.SetMaterialKeywords);

            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
