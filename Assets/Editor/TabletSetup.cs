using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ProjectCR.Editor
{
    /// <summary>
    /// CRT_Tablet 프리팹 정리: 글자 메시(보유 금액, 목표 금액, 회전, 서비스, 전체 회수, 숫자 0)를
    /// 같은 자리·기울기·색·크기의 TextMesh로 바꾸고, 버튼 메시에 콜라이더와 TabletButton, 루트에 TabletPanel을 붙인다.
    /// 다시 돌려도 이미 바꾼 글자는 그대로 둔다. 쓰지 않게 된 글자 메시 에셋은 지운다.
    /// </summary>
    public static class TabletSetup
    {
        public const string TabletPrefabPath = "Assets/Prefabs/CRT_Tablet.prefab";
        private const string MeshFolder = "Assets/Art/Tablet/Meshes";

        private enum Align
        {
            Left,
            Center,
        }

        private sealed class Label
        {
            public string Name;
            public string Text;
            public Align Align;
        }

        private static readonly Label[] Labels =
        {
            new Label { Name = "CRT_Tablet_Label_Balance", Text = "보유 금액", Align = Align.Left },
            new Label { Name = "CRT_Tablet_Label_Bet", Text = "목표 금액", Align = Align.Left },
            new Label { Name = "CRT_Tablet_Value_Balance", Text = "0", Align = Align.Left },
            new Label { Name = "CRT_Tablet_Value_Bet", Text = "0", Align = Align.Left },
            new Label { Name = "CRT_Tablet_Spin_Label", Text = "회전", Align = Align.Center },
            new Label { Name = "CRT_Tablet_Service_Label", Text = "서비스", Align = Align.Center },
            new Label { Name = "CRT_Tablet_RecallAll_Label", Text = "전체 회수", Align = Align.Center },
        };

        [MenuItem("Tools/ProjectCR/Tablet/Convert Labels And Buttons")]
        public static void ConvertTablet()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(TabletPrefabPath);
            var unusedMeshes = new List<Mesh>();
            try
            {
                var texts = new Dictionary<string, TextMesh>();
                foreach (Label label in Labels)
                {
                    texts[label.Name] = ToText(root.transform, label, unusedMeshes);
                }

                var buttons = new[]
                {
                    MakeButton(root.transform, "CRT_Tablet_Spin_Button", TabletAction.Spin, texts["CRT_Tablet_Spin_Label"]),
                    MakeButton(root.transform, "CRT_Tablet_Service_Button", TabletAction.Service, texts["CRT_Tablet_Service_Label"]),
                    MakeButton(root.transform, "CRT_Tablet_RecallAll_Button", TabletAction.RecallAll, texts["CRT_Tablet_RecallAll_Label"]),
                };

                // GetComponent는 없을 때 "가짜 null"을 돌려줘서 ??를 쓰지 않는다.
                TabletPanel panel = root.GetComponent<TabletPanel>();
                if (panel == null)
                {
                    panel = root.AddComponent<TabletPanel>();
                }

                panel.Configure(texts["CRT_Tablet_Value_Balance"], texts["CRT_Tablet_Value_Bet"], buttons);

                PrefabUtility.SaveAsPrefabAsset(root, TabletPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            foreach (Mesh mesh in unusedMeshes)
            {
                string path = AssetDatabase.GetAssetPath(mesh);
                if (path.StartsWith(MeshFolder))
                {
                    AssetDatabase.DeleteAsset(path);
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[TabletSetup] 글자 {Labels.Length}개를 TextMesh로, 버튼 3개 연결");
        }

        public const string DrinksButtonName = "CRT_Tablet_Drinks_Button";
        public const string DrinksOutlineName = "CRT_Tablet_Drinks_Outline";
        public const string DrinksLabelName = "CRT_Tablet_Drinks_Label";

        // 회전 버튼 자리를 회전 73% · 간격 2% · 음료 25%로 나눈다.
        private const float SpinShare = 0.73f;
        private const float DrinksShare = 0.25f;

        /// <summary>
        /// 회전 버튼 자리를 나눠 오른쪽에 [음료] 버튼을 만든다(서비스 버튼 모양·테두리를 복제해 폭·높이를 맞춘다).
        /// 플레이어(+Z)에서 보면 +X가 왼쪽이라, 회전은 +X 쪽 73%, 음료는 -X 쪽 25%다. 콜라이더는 각 버튼 메시에 붙어 있어 겹치지 않는다.
        /// 다시 돌려도 안전하다: 음료 버튼이 이미 있으면 크기·자리는 건드리지 않고 연결만 다시 한다(회전 버튼이 또 작아지지 않는다).
        /// </summary>
        [MenuItem("Tools/ProjectCR/Tablet/Add Drinks Button")]
        public static string AddDrinksButton()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(TabletPrefabPath);
            string result;
            try
            {
                Transform spin = Find(root.transform, "CRT_Tablet_Spin_Button");
                Transform spinLabel = Find(root.transform, "CRT_Tablet_Spin_Label");
                Transform service = Find(root.transform, "CRT_Tablet_Service_Button");
                Transform serviceOutline = Find(root.transform, "CRT_Tablet_Service_Outline");
                Transform serviceLabel = Find(root.transform, "CRT_Tablet_Service_Label");

                Transform drinks = root.transform.Find(DrinksButtonName);
                Transform drinksOutline = root.transform.Find(DrinksOutlineName);
                Transform drinksLabel = root.transform.Find(DrinksLabelName);
                bool created = drinks == null;
                if (created)
                {
                    Bounds full = Bounds(spin);
                    Bounds serviceBounds = Bounds(service);
                    float width = full.size.x;
                    float left = full.max.x;   // 플레이어 기준 왼쪽 끝
                    float right = full.min.x;  // 플레이어 기준 오른쪽 끝

                    // 회전: 왼쪽 끝을 지키고 73%로 줄인다. 글자도 새 가운데로 옮긴다.
                    Vector3 spinCenterBefore = full.center;
                    ScaleLocalX(spin, SpinShare);
                    MoveCenterX(spin, left - width * SpinShare * 0.5f);
                    spinLabel.position += new Vector3(Bounds(spin).center.x - spinCenterBefore.x, 0f, 0f);

                    // 음료: 서비스 버튼·테두리를 복제해 폭은 25%, 높이는 회전 버튼에 맞추고 오른쪽 끝에 놓는다.
                    drinks = Clone(service, DrinksButtonName);
                    drinksOutline = Clone(serviceOutline, DrinksOutlineName);
                    float widthScale = width * DrinksShare / serviceBounds.size.x;
                    float heightScale = full.size.z / serviceBounds.size.z;
                    foreach (Transform part in new[] { drinks, drinksOutline })
                    {
                        Vector3 scale = part.localScale;
                        part.localScale = new Vector3(scale.x * widthScale, scale.y * heightScale, scale.z * heightScale);
                    }

                    Vector3 target = new Vector3(right + width * DrinksShare * 0.5f, full.center.y, full.center.z);
                    Vector3 delta = target - Bounds(drinks).center;
                    drinks.position += delta;
                    drinksOutline.position += delta;

                    // 글자: 서비스 글자가 서비스 버튼에서 떨어진 만큼 음료 버튼 위에 둔다.
                    drinksLabel = Clone(serviceLabel, DrinksLabelName);
                    drinksLabel.position = Bounds(drinks).center + (serviceLabel.position - serviceBounds.center);
                    drinksLabel.GetComponent<TextMesh>().text = "음료";
                }

                // 복제한 버튼에 딸려 온 자식(빛 테두리 등)은 쓰지 않는다.
                for (int i = drinks.childCount - 1; i >= 0; i--)
                {
                    Object.DestroyImmediate(drinks.GetChild(i).gameObject);
                }

                TabletButton drinksButton = drinks.GetComponent<TabletButton>();
                if (drinksButton == null)
                {
                    drinksButton = drinks.gameObject.AddComponent<TabletButton>();
                }

                drinksButton.Configure(TabletAction.Drinks, drinks.GetComponent<Renderer>(), drinksLabel.GetComponent<TextMesh>());
                drinksButton.SetHighlightGlow(null);

                TabletPanel panel = root.GetComponent<TabletPanel>();
                var buttons = new List<TabletButton>();
                foreach (TabletButton button in root.GetComponentsInChildren<TabletButton>(true))
                {
                    if (!buttons.Contains(button))
                    {
                        buttons.Add(button);
                    }
                }

                panel.Configure(panel.BalanceValue, panel.DueValue, buttons.ToArray());
                PrefabUtility.SaveAsPrefabAsset(root, TabletPrefabPath);
                result = created
                    ? $"음료 버튼을 만들었다(회전 {SpinShare:P0} · 음료 {DrinksShare:P0}). 버튼 {buttons.Count}개 연결"
                    : $"음료 버튼이 이미 있어 크기·자리는 그대로 두고 연결만 다시 했다. 버튼 {buttons.Count}개";
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            Debug.Log("[TabletSetup] " + result);
            return result;
        }

        private static Transform Find(Transform root, string name) =>
            root.Find(name) ?? throw new InvalidOperationException($"태블릿에 {name}이 없다.");

        private static Bounds Bounds(Transform t) => t.GetComponent<Renderer>().bounds;

        private static Transform Clone(Transform source, string name)
        {
            GameObject copy = Object.Instantiate(source.gameObject, source.parent);
            copy.name = name;
            copy.transform.SetSiblingIndex(source.GetSiblingIndex() + 1);
            return copy.transform;
        }

        /// <summary>로컬 X(월드 X와 같은 방향)로만 줄인다. 피벗이 모델 원점이라 자리는 MoveCenterX로 다시 맞춘다.</summary>
        private static void ScaleLocalX(Transform t, float factor)
        {
            Vector3 scale = t.localScale;
            t.localScale = new Vector3(scale.x * factor, scale.y, scale.z);
        }

        private static void MoveCenterX(Transform t, float worldCenterX)
        {
            t.position += new Vector3(worldCenterX - Bounds(t).center.x, 0f, 0f);
        }

        /// <summary>글자 메시 오브젝트를 같은 이름의 TextMesh 오브젝트로 바꾼다. 이미 TextMesh면 그대로.</summary>
        private static TextMesh ToText(Transform root, Label spec, List<Mesh> unusedMeshes)
        {
            Transform source = root.Find(spec.Name) ?? throw new InvalidOperationException($"태블릿에 {spec.Name}이 없다.");
            if (source.TryGetComponent(out TextMesh existing))
            {
                return existing;
            }

            var renderer = source.GetComponent<MeshRenderer>();
            Mesh mesh = source.GetComponent<MeshFilter>().sharedMesh;
            Bounds bounds = renderer.bounds;
            Color color = renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_BaseColor")
                ? renderer.sharedMaterial.GetColor("_BaseColor")
                : Color.white;

            // 글자 면의 방향(정점 법선 평균). 글자 위쪽은 플레이어 반대편(-Z).
            Vector3 normal = Vector3.zero;
            foreach (Vector3 n in mesh.normals)
            {
                Vector3 world = source.TransformDirection(n).normalized;
                if (world.y > 0.3f)
                {
                    normal += world;
                }
            }

            normal = normal.sqrMagnitude > 0f ? normal.normalized : Vector3.up;

            int sibling = source.GetSiblingIndex();
            unusedMeshes.Add(mesh);
            Object.DestroyImmediate(source.gameObject);

            var go = new GameObject(spec.Name);
            go.transform.SetParent(root, false);
            go.transform.SetSiblingIndex(sibling);

            // 플레이어(+Z)에서 보면 +X가 왼쪽이라, 왼쪽 정렬 글자는 메시의 +X 끝에서 시작한다.
            Vector3 anchor = spec.Align == Align.Left
                ? new Vector3(bounds.max.x, bounds.center.y, bounds.center.z)
                : bounds.center;
            go.transform.position = anchor + normal * 0.0008f;
            go.transform.rotation = Quaternion.LookRotation(-normal, Vector3.ProjectOnPlane(Vector3.back, normal));

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var text = go.AddComponent<TextMesh>();
            text.text = spec.Text;
            text.font = font;
            text.fontSize = 64;
            text.fontStyle = FontStyle.Bold;
            text.anchor = spec.Align == Align.Left ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
            text.alignment = spec.Align == Align.Left ? TextAlignment.Left : TextAlignment.Center;
            text.color = new Color(color.r, color.g, color.b, 1f);
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;

            // 원래 글자 메시 높이(평면 위 Z 폭)에 맞추고, 폭이 원래보다 넓으면 폭에 맞춰 줄인다.
            const float probe = 0.002f;
            text.characterSize = probe;
            Bounds rendered = go.GetComponent<MeshRenderer>().bounds;
            float size = rendered.size.z > 1e-5f ? probe * bounds.size.z / rendered.size.z : 0.0024f;
            if (rendered.size.x > 1e-5f)
            {
                float widthLimited = probe * bounds.size.x * 1.15f / rendered.size.x;
                if (spec.Text.Length > 1)
                {
                    size = Mathf.Min(size, widthLimited);
                }
            }

            text.characterSize = size;
            return text;
        }

        /// <summary>버튼 면에 콜라이더와 TabletButton을 붙인다(이미 있으면 다시 설정).</summary>
        private static TabletButton MakeButton(Transform root, string faceName, TabletAction action, TextMesh label)
        {
            Transform face = root.Find(faceName) ?? throw new InvalidOperationException($"태블릿에 {faceName}이 없다.");
            if (!face.TryGetComponent(out BoxCollider box))
            {
                box = face.gameObject.AddComponent<BoxCollider>();
            }

            // 얇은 판이라 누르기 쉽게 두께만 조금 키운다(메시 로컬 Z가 두께 방향이 아닐 수 있어 가장 얇은 축을 키운다).
            Vector3 size = box.size;
            int thin = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
            size[thin] = Mathf.Max(size[thin], 0.004f / Mathf.Max(1e-4f, face.lossyScale[thin]));
            box.size = size;

            TabletButton button = face.GetComponent<TabletButton>();
            if (button == null)
            {
                button = face.gameObject.AddComponent<TabletButton>();
            }

            button.Configure(action, face.GetComponent<Renderer>(), label);
            return button;
        }
    }
}
