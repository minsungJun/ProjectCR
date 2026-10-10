using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectCR
{
    /// <summary>
    /// 태블릿 면(화면·베젤 메시)을 카메라로 비춘 사각형을 UI Toolkit 패널 좌표로 구한다.
    /// 화면 UI를 그 사각형에 맞춰 놓으면 태블릿에 떠 있는 것처럼 보인다(상점, 마신 음료 화면).
    /// 메시는 런타임에 읽히지 않으므로(Read/Write 꺼짐) 꼭짓점은 에디터에서 <see cref="ReadPoints"/>로 미리 읽어 저장해 둔다.
    /// </summary>
    public static class TabletScreenFit
    {
        /// <summary>메시 꼭짓점(로컬, 겹치는 점 제외). 에디터에서 부른다(OnValidate).</summary>
        public static Vector3[] ReadPoints(MeshFilter mesh)
        {
            if (mesh == null || mesh.sharedMesh == null)
            {
                return new Vector3[0];
            }

            var points = new List<Vector3>();
            foreach (Vector3 vertex in mesh.sharedMesh.vertices)
            {
                if (!points.Contains(vertex))
                {
                    points.Add(vertex);
                }
            }

            return points.ToArray();
        }

        /// <summary>
        /// 점들을 비춘 사각형(패널 좌표, 왼쪽 위 원점, 패널 배율 반영)에서 inset만큼 안쪽. 점이 없거나 카메라 뒤에 있으면 false.
        /// </summary>
        public static bool TryGetPanelRect(Camera view, Transform surface, Vector3[] points, IPanel panel, float inset, out Rect rect)
        {
            rect = default;
            if (view == null || surface == null || points == null || points.Length == 0 || panel == null)
            {
                return false;
            }

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (Vector3 vertex in points)
            {
                Vector3 point = view.WorldToScreenPoint(surface.TransformPoint(vertex));
                if (point.z <= 0f)
                {
                    return false;
                }

                // 화면 좌표(왼쪽 아래 원점) → 패널 좌표(왼쪽 위 원점).
                Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(point.x, Screen.height - point.y));
                minX = Mathf.Min(minX, panelPoint.x);
                maxX = Mathf.Max(maxX, panelPoint.x);
                minY = Mathf.Min(minY, panelPoint.y);
                maxY = Mathf.Max(maxY, panelPoint.y);
            }

            rect = Rect.MinMaxRect(minX + inset, minY + inset, maxX - inset, maxY - inset);
            return rect.width > 1f && rect.height > 1f;
        }

        /// <summary>요소를 사각형 자리에 놓는다(절대 위치).</summary>
        public static void Place(VisualElement element, Rect rect)
        {
            element.style.left = rect.x;
            element.style.top = rect.y;
            element.style.width = rect.width;
            element.style.height = rect.height;
        }
    }
}
