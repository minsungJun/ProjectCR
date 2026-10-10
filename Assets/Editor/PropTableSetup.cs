using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ProjectCR.Editor
{
    /// <summary>
    /// 레벨 장식용 룰렛 테이블(메뉴 Tools/ProjectCR/Model (CRR)/Build Prop Table).
    /// 게임 테이블 프리팹(RouletteRoot_CRR)을 복사해 모양은 그대로 두고 게임 부품을 모두 뺀 Prefabs/RouletteTable_Prop.prefab을 만든다.
    /// - 뺀다: ProjectCR 스크립트 전부(판 표시·입력 대상·태블릿·연출), 콜라이더 전부(클릭 판정에 걸리지 않게),
    ///   카메라·조명(원래 꺼져 있음), 빌더가 만든 표시용 오브젝트(HoverMarker, BetHighlight, ChipTrayGlow, ChipSlot_*).
    /// - 태블릿 숫자는 고정 값(보유 1,000 · 목표 120)으로 두고, 라벨은 게임 테이블과 같은 "목표 금액".
    /// 원본 테이블을 고친 뒤 다시 돌리면 같은 경로에 덮어쓴다(씬에 놓은 프롭은 그대로 갱신된다). 원본·씬은 건드리지 않는다.
    /// </summary>
    public static class PropTableSetup
    {
        private const string SourcePath = "Assets/Prefabs/RouletteRoot_CRR.prefab";
        private const string PropPath = "Assets/Prefabs/RouletteTable_Prop.prefab";

        private static readonly string[] GeneratedObjects = { "HoverMarker", "BetHighlight", "ChipTrayGlow" };

        [MenuItem("Tools/ProjectCR/Model (CRR)/Build Prop Table")]
        public static void Build()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(SourcePath);
            try
            {
                // 중첩 프리팹(모델 FBX, 태블릿)까지 평범한 오브젝트로 풀어서 자유롭게 지운다.
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t != null && PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject))
                    {
                        PrefabUtility.UnpackPrefabInstance(t.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    }
                }

                int scripts = 0, colliders = 0, cameras = 0, lights = 0, objects = 0;

                // 스크립트가 콜라이더를 필수로 잡고 있으니(TabletButton 등) 스크립트부터.
                foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour != null && behaviour.GetType().Namespace != null && behaviour.GetType().Namespace.StartsWith("ProjectCR"))
                    {
                        Object.DestroyImmediate(behaviour);
                        scripts++;
                    }
                }

                foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                {
                    Object.DestroyImmediate(collider);
                    colliders++;
                }

                // URP 추가 데이터(카메라·조명을 필수로 잡고 있다)를 먼저. 어셈블리 참조 없이 이름으로 찾는다.
                foreach (Component component in root.GetComponentsInChildren<Component>(true))
                {
                    string type = component != null ? component.GetType().Name : string.Empty;
                    if (type == "UniversalAdditionalCameraData" || type == "UniversalAdditionalLightData")
                    {
                        Object.DestroyImmediate(component);
                    }
                }

                foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                {
                    Object.DestroyImmediate(camera);
                    cameras++;
                }

                foreach (Light light in root.GetComponentsInChildren<Light>(true))
                {
                    Object.DestroyImmediate(light);
                    lights++;
                }

                var doomed = new List<GameObject>();
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (System.Array.IndexOf(GeneratedObjects, t.name) >= 0 || t.name.StartsWith("ChipSlot_"))
                    {
                        doomed.Add(t.gameObject);
                    }
                }

                foreach (GameObject go in doomed)
                {
                    if (go != null)
                    {
                        Object.DestroyImmediate(go);
                        objects++;
                    }
                }

                // 태블릿 숫자: 고정 값.
                foreach (TextMesh text in root.GetComponentsInChildren<TextMesh>(true))
                {
                    if (text.name == "CRT_Tablet_Value_Balance")
                    {
                        text.text = "1,000";
                    }
                    else if (text.name == "CRT_Tablet_Value_Bet")
                    {
                        text.text = "120";
                    }
                    else if (text.name == "CRT_Tablet_Label_Bet")
                    {
                        text.text = "목표 금액"; // 게임 테이블은 씬에서 바꾼 문구
                    }
                }

                root.name = "RouletteTable_Prop";
                PrefabUtility.SaveAsPrefabAsset(root, PropPath);
                Debug.Log($"[PropTableSetup] {PropPath}: 스크립트 {scripts}, 콜라이더 {colliders}, 카메라 {cameras}, 조명 {lights}, 표시용 오브젝트 {objects}개 제거");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
