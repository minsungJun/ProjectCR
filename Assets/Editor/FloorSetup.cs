using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ProjectCR.Editor
{
    /// <summary>
    /// 바닥 머티리얼(메뉴 Tools/ProjectCR/Art/Floor Material).
    /// Art/Textures/Floor.png를 반복 가능하게 임포트(Repeat, 밉맵, 비등방 필터 8)하고, URP Lit 머티리얼 Materials/Floor.mat을 만들어
    /// 씬의 LevelProps/Plane에 넣는다. 크기와 상관없이 타일 한 장이 일정한 실제 크기가 되도록 WorldTiling을 붙인다(기본 1m).
    /// 다시 돌려도 된다(머티리얼 값은 덮어쓰지 않고 텍스처만 다시 연결).
    /// </summary>
    public static class FloorSetup
    {
        private const string TexturePath = "Assets/Art/Textures/Floor.png";
        private const string MaterialPath = "Assets/Materials/Floor.mat";
        private const string FloorParentName = "LevelProps";
        private const string FloorName = "Plane";

        [MenuItem("Tools/ProjectCR/Art/Floor Material")]
        public static void Setup()
        {
            var importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter
                ?? throw new InvalidOperationException($"{TexturePath}가 없다.");
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.anisoLevel = 8; // 비스듬히 볼 때도 흐려지지 않게
            importer.sRGBTexture = true;
            importer.SaveAndReimport();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.SetFloat("_Smoothness", 0.2f);
                AssetDatabase.CreateAsset(material, MaterialPath);
            }

            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();

            Transform parent = GameObject.Find(FloorParentName)?.transform
                ?? throw new InvalidOperationException($"씬에 {FloorParentName}가 없다.");
            Transform floor = parent.Find(FloorName)
                ?? throw new InvalidOperationException($"{FloorParentName} 아래에 {FloorName}이 없다.");

            var renderer = floor.GetComponent<Renderer>();
            Undo.RecordObject(renderer, "Floor material");
            renderer.sharedMaterial = material;
            if (floor.GetComponent<WorldTiling>() == null)
            {
                Undo.AddComponent<WorldTiling>(floor.gameObject);
            }

            EditorSceneManager.MarkSceneDirty(floor.gameObject.scene);
            EditorSceneManager.SaveScene(floor.gameObject.scene);
            Debug.Log($"[FloorSetup] {floor.name}에 {material.name} 적용, 타일 1m (크기 {renderer.bounds.size.x:F1}×{renderer.bounds.size.z:F1}m)");
        }
    }
}
