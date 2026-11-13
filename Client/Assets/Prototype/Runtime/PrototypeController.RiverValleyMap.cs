using System;
using UnityEngine;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        // 区域图只解释当前街区与原河谷镇外景的关系，不开放远景交互。
        private bool survivalRegionMapOpen;
        private RiverValleyRegion riverValleyRegion;

        [Serializable] private sealed class RiverValleyRegion
        {
            public RegionRoute[] routes;
            public RegionLandmark[] landmarks;
        }
        [Serializable] private sealed class RegionRoute
        {
            public string kind;
            public float width;
            public Vector2[] points;
        }
        [Serializable] private sealed class RegionLandmark
        {
            public string name;
            public float x, y;
        }

        /// <summary>按原始图纸坐标绘制区域关系，明确当前可玩范围和外围远景。</summary>
        private void DrawRiverValleyRegion(float width, float height)
        {
            ScavengeFill(new Rect(0, 0, width, height), new Color(.04f, .065f, .07f, .98f));
            GUI.Label(new Rect(40, 20, 600, 35), "河谷镇区域图 · 2 × 2 公里");
            float size = Mathf.Min(height - 145, width - 440);
            var map = new Rect(40, 70, size, size);
            ScavengeFill(map, new Color(.20f, .26f, .22f));
            Vector2 Project(Vector2 point) => map.position + (point - Vector2.one * 1000) * (size / 2000);
            if (riverValleyRegion != null)
            {
                foreach (var route in riverValleyRegion.routes)
                {
                    bool water = route.kind == "river" || route.kind == "stream";
                    var color = water ? new Color(.29f, .51f, .62f) : new Color(.65f, .64f, .51f);
                    for (int i = 1; i < route.points.Length; i++)
                        DrawRegionLine(Project(route.points[i - 1]), Project(route.points[i]),
                            Mathf.Max(water ? 3 : 1.5f, route.width * size / 2000), color);
                }
                var labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13 };
                var occupied = new System.Collections.Generic.List<Rect>();
                foreach (var landmark in riverValleyRegion.landmarks)
                {
                    var point = Project(new Vector2(landmark.x, landmark.y));
                    for (int attempt = 0; attempt < 9; attempt++)
                    {
                        float shift = attempt == 0 ? 0 : ((attempt + 1) / 2) * 22 * (attempt % 2 == 0 ? -1 : 1);
                        var label = new Rect(Mathf.Clamp(point.x + 3, map.x, map.xMax - 100),
                            Mathf.Clamp(point.y - 10 + shift, map.y, map.yMax - 22), 100, 22);
                        if (occupied.Exists(other => other.Overlaps(label))) continue;
                        occupied.Add(label);
                        GUI.Label(label, landmark.name, labelStyle);
                        break;
                    }
                }
            }
            // 可玩坐标的零点对应原图 1890,1885，Z 轴与图纸南北方向相反。
            var corner = Project(new Vector2(1890 + ScavengeTown.Bounds.xMin, 1885 - ScavengeTown.Bounds.yMax));
            var active = new Rect(corner, ScavengeTown.Bounds.size * (size / 2000));
            var cyan = new Color(.35f, .93f, .81f);
            DrawRegionLine(active.min, new Vector2(active.xMax, active.yMin), 2, cyan);
            DrawRegionLine(new Vector2(active.xMax, active.yMin), active.max, 2, cyan);
            DrawRegionLine(active.max, new Vector2(active.xMin, active.yMax), 2, cyan);
            DrawRegionLine(new Vector2(active.xMin, active.yMax), active.min, 2, cyan);
            var self = Project(new Vector2(1890 + scavengeRun.Player.x, 1885 - scavengeRun.Player.y));
            ScavengeFill(new Rect(self - Vector2.one * 3, Vector2.one * 6), new Color(1, .94f, .55f));
            float x = map.xMax + 30;
            GUI.Label(new Rect(x, 80, width - x - 40, 220),
                "青绿色方框：当前可玩街区\n黄色点：你的位置\n蓝色：河流与支流\n米色：区域道路\n\n本趟搜索、基地与撤离均在方框内。外围建筑和道路为河谷镇外景，尚未开放交互。\n\n查看区域图时行动暂停。",
                new GUIStyle(GUI.skin.label) { wordWrap = true });
            if (GUI.Button(new Rect(40, height - 58, width - 80, 38), "返回行动  M / Esc")) survivalRegionMapOpen = false;
        }

        /// <summary>以现有 GUI 矩阵绘制线段，结束后恢复缩放与旋转。</summary>
        private static void DrawRegionLine(Vector2 a, Vector2 b, float thickness, Color color)
        {
            var previous = GUI.matrix;
            var delta = b - a;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, a);
            ScavengeFill(new Rect(a.x, a.y - thickness / 2, delta.magnitude, thickness), color);
            GUI.matrix = previous;
        }
    }
}
