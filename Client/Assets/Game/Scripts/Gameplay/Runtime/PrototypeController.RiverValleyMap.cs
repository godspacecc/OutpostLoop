using System;
using UnityEngine;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        // 区域图只解释当前街区与原河谷镇外景的关系，不开放远景交互。
        private bool survivalRegionMapOpen;
        private RiverValleyRegion riverValleyRegion;
        // 导览只选择地标并展示方位，不传送、不改变地图开放状态。
        private int riverValleySelectedLandmark = -1;
        private Vector2 riverValleyLandmarkScroll;

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
                        // 避让后的标签以引线指向真实坐标，避免误读地标所在街区。
                        ScavengeFill(new Rect(point - Vector2.one * 2, Vector2.one * 4), Color.white);
                        var edge = new Vector2(Mathf.Clamp(point.x, label.x, label.xMax),
                            Mathf.Clamp(point.y, label.y, label.yMax));
                        DrawRegionLine(point, edge, 1, new Color(.66f, .73f, .68f));
                        GUI.Label(label, landmark.name, labelStyle);
                        break;
                    }
                }
            }
            // 可玩坐标的零点对应原图 1890,1885，Z 轴与图纸南北方向相反。
            var bounds = scavengeRun.MapBounds;
            var corner = Project(new Vector2(1890 + bounds.xMin, 1885 - bounds.yMax));
            var active = new Rect(corner, bounds.size * (size / 2000));
            var cyan = new Color(.35f, .93f, .81f);
            DrawRegionLine(active.min, new Vector2(active.xMax, active.yMin), 2, cyan);
            DrawRegionLine(new Vector2(active.xMax, active.yMin), active.max, 2, cyan);
            DrawRegionLine(active.max, new Vector2(active.xMin, active.yMax), 2, cyan);
            DrawRegionLine(new Vector2(active.xMin, active.yMax), active.min, 2, cyan);
            var self = Project(new Vector2(1890 + scavengeRun.Player.x, 1885 - scavengeRun.Player.y));
            ScavengeFill(new Rect(self - Vector2.one * 3, Vector2.one * 6), new Color(1, .94f, .55f));
            if (survivalWorldGeometry != null)
            {
                var rooms = new SurvivalRoomSet(survivalWorldGeometry.Interiors);
                for (int i = 0; i < 6; i++)
                {
                    var door = rooms.Door(i).center;
                    var pin = Project(new Vector2(1890 + door.x, 1885 - door.y));
                    ScavengeFill(new Rect(pin - Vector2.one * 4, Vector2.one * 8), new Color(.35f,.8f,1));
                    GUI.Label(new Rect(pin.x + 5,pin.y - 10,25,24),(i+1).ToString());
                }
            }
            float x = map.xMax + 30;
            GUI.Label(new Rect(x, 80, width - x - 40, 190),
                survivalWorldGeometry == null ? "青绿色方框：当前可玩街区\n黄色点：你的位置\n蓝色：河流与支流\n米色：区域道路\n\n旧版续玩：外围尚未开放交互。\n\n查看区域图时行动暂停。" : "青色边框：整图范围\n黄色点：你的位置\n蓝色河道：走桥通过\n蓝色编号：六处重点入口\n米色线：区域道路\n\n陆路可探索，其余建筑封闭。\n查看时行动暂停。",
                new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 17 });
            DrawRiverValleyLandmarks(map, x, width - x - 40, height);
            GUI.Label(new Rect(map.xMax - 60, map.y + 5, 56, 24), "北 ↑");
            float scaleLength = size / 10;
            DrawRegionLine(new Vector2(map.xMax - scaleLength - 12, map.yMax - 15),
                new Vector2(map.xMax - 12, map.yMax - 15), 3, Color.white);
            GUI.Label(new Rect(map.xMax - scaleLength - 12, map.yMax - 42, scaleLength + 10, 24), "200 米");
            if (GUI.Button(new Rect(40, height - 58, width - 80, 38), "返回行动  M / Esc")) survivalRegionMapOpen = false;
        }

        /// <summary>全镇地标可选择定位；直线距离不作为跨河或穿建筑的通行路线。</summary>
        private void DrawRiverValleyLandmarks(Rect map, float x, float width, float height)
        {
            if (riverValleyRegion?.landmarks == null) return;
            var places = riverValleyRegion.landmarks;
            GUI.Label(new Rect(x, 278, width, 27), "河谷镇地标 · 点击定位");
            var list = new Rect(x, 312, width, Mathf.Max(80, height - 474));
            riverValleyLandmarkScroll = GUI.BeginScrollView(list, riverValleyLandmarkScroll,
                new Rect(0, 0, width - 22, places.Length * 35));
            try
            {
                for (int i = 0; i < places.Length; i++)
                    if (GUI.Button(new Rect(0, i * 35, width - 26, 31),
                        (riverValleySelectedLandmark == i ? "● " : "") + places[i].name)) riverValleySelectedLandmark = i;
            }
            finally { GUI.EndScrollView(); }
            string details = "选择地标查看方位。仅作导览，不会传送或开放该区域。";
            if (riverValleySelectedLandmark >= 0 && riverValleySelectedLandmark < places.Length)
            {
                var place = places[riverValleySelectedLandmark];
                var target = new Vector2(place.x - 1890, 1885 - place.y);
                var delta = target - scavengeRun.Player;
                string direction = delta.magnitude < 20 ? "就在附近" :
                    (Mathf.Abs(delta.x) > Mathf.Abs(delta.y) * .4f ? delta.x > 0 ? "东" : "西" : "") +
                    (Mathf.Abs(delta.y) > Mathf.Abs(delta.x) * .4f ? delta.y > 0 ? "北" : "南" : "");
                details = $"{place.name} · {direction} · 直线约 {delta.magnitude:0} 米\n水面需走桥；地标定位不代表所有建筑可进入。";
                var pin = map.position + new Vector2(place.x - 1000, place.y - 1000) * (map.width / 2000);
                var color = new Color(1, .69f, .27f);
                DrawRegionLine(pin + Vector2.left * 7, pin + Vector2.right * 7, 2, color);
                DrawRegionLine(pin + Vector2.down * 7, pin + Vector2.up * 7, 2, color);
            }
            GUI.Label(new Rect(x, height - 150, width, 76), details, new GUIStyle(GUI.skin.label) { wordWrap = true });
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
