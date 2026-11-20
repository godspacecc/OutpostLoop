using System;
using UnityEngine;
using Game;

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

        private const float RegionExtent = 2000f;
        private const float RegionMinCoordinate = 1000f;
        private const float RegionMaxZoom = 16f;
        private static readonly Vector2 RegionMiddle = new Vector2(2000f, 2000f);
        private static readonly Vector2 RegionWorldOrigin = new Vector2(1890f, 1885f);
        private static readonly Color RegionPlayerColor = new Color(1f, .83f, .43f);
        private static readonly Color RegionEntranceColor = new Color(.45f, .86f, .94f);
        private static readonly Color RegionBoundaryColor = new Color(.42f, .78f, .66f);
        private static readonly Color RegionWaterColor = new Color(.25f, .50f, .63f);
        private static readonly Color RegionMainRoadColor = new Color(.77f, .70f, .53f);

        // 视图中心使用图纸米制坐标，缩放和平移仅存在于地图窗口。
        private float regionZoom = 1f;
        private Vector2 regionCenter = RegionMiddle;
        private bool regionDragging;
        private Vector2 regionDragPointer;
        // 从图纸缓存道路和河流图层，标记和比例尺仍按视图单独绘制。
        private RiverValleyRegion regionRouteSource;
        private Texture2D regionRouteTexture;

        /// <summary>每次打开地图先显示完整区域，再由玩家选择要查看的街区。</summary>
        private void OpenRiverValleyRegion()
        {
            regionZoom = 1f;
            regionCenter = RegionMiddle;
            regionDragging = false;
            riverValleySelectedLandmark = -1;
            riverValleyLandmarkScroll = Vector2.zero;
            survivalRegionMapOpen = true;
        }

        /// <summary>将局内平面坐标换算为北朝上的原图坐标。</summary>
        private static Vector2 ToRegionPoint(Vector2 point) =>
            RegionWorldOrigin + new Vector2(point.x, -point.y);

        /// <summary>让图纸填满可用空间，并把地图操作与地标列表放在各自的区域。</summary>
        private void DrawRiverValleyRegion(float width, float height)
        {
            ScavengeFill(new Rect(0, 0, width, height), new Color(.043f, .066f, .075f));
            var map = new Rect(24, 68, width - 314, height - 130);
            var sidebar = new Rect(map.xMax + 14, map.y, 252, map.height);
            var player = ToRegionPoint(scavengeRun.Player);
            var title = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold };
            var caption = new GUIStyle(GUI.skin.label) { fontSize = 15 };

            GUI.Label(new Rect(map.x, 15, 410, 44), Loc.T("RegionMapTitle"), title);
            GUI.Label(new Rect(map.x + 320, 28, 270, 28), Loc.T("RegionMapScale"), caption);
            if (GUI.Button(new Rect(sidebar.x, 19, sidebar.width, 42), Loc.T(survivalPackOpen ? "RegionMapBackPack" : "RegionMapBack")))
            {
                survivalRegionMapOpen = false;
                return;
            }

            float baseScale = Mathf.Min(map.width, map.height) / RegionExtent;
            HandleRegionPointer(map, baseScale);
            if (GUI.Button(new Rect(map.x, map.yMax + 14, 160, 34), Loc.T("RegionMapCenter")))
            {
                regionZoom = Mathf.Max(regionZoom, 5f);
                regionCenter = player;
            }
            if (GUI.Button(new Rect(map.x + 170, map.yMax + 14, 146, 34), Loc.T("RegionMapOverview")))
            {
                regionZoom = 1f;
                regionCenter = RegionMiddle;
            }
            DrawRiverValleyLandmarks(sidebar);
            GUI.Label(new Rect(map.x + 333, map.yMax + 16, 390, 30),
                Loc.T("RegionMapControls", regionZoom), caption);
            float scale = baseScale * regionZoom;
            regionCenter.x = ClampRegionAxis(regionCenter.x, map.width / (2f * scale));
            regionCenter.y = ClampRegionAxis(regionCenter.y, map.height / (2f * scale));
            if (Event.current.type != EventType.Repaint) return;

            GUI.BeginGroup(map);
            try
            {
                DrawRegionContents(new Rect(0, 0, map.width, map.height), scale, player);
            }
            finally { GUI.EndGroup(); }
        }

        /// <summary>以鼠标下的图纸坐标为锚点缩放，仅接收从地图画面开始的拖动。</summary>
        private void HandleRegionPointer(Rect map, float baseScale)
        {
            var input = Event.current;
            if (input.rawType == EventType.MouseUp || input.type == EventType.Ignore)
                regionDragging = false;

            switch (input.type)
            {
                case EventType.ScrollWheel when map.Contains(input.mousePosition):
                    float previousZoom = regionZoom;
                    regionZoom = Mathf.Clamp(previousZoom * Mathf.Pow(1.16f, -input.delta.y), 1f, RegionMaxZoom);
                    regionCenter += (input.mousePosition - map.center) *
                        ((1f / previousZoom - 1f / regionZoom) / baseScale);
                    input.Use();
                    break;
                case EventType.MouseDown when input.button == 0 && map.Contains(input.mousePosition):
                    regionDragging = true;
                    regionDragPointer = input.mousePosition;
                    input.Use();
                    break;
                case EventType.MouseDrag when input.button == 0 && regionDragging:
                    regionCenter -= (input.mousePosition - regionDragPointer) / (baseScale * regionZoom);
                    regionDragPointer = input.mousePosition;
                    input.Use();
                    break;
            }
        }

        /// <summary>整轴可见时居中，放大后将视野限制在图纸边界内。</summary>
        private static float ClampRegionAxis(float center, float halfSpan) =>
            halfSpan >= RegionExtent / 2f ? RegionMiddle.x :
                Mathf.Clamp(center, RegionMinCoordinate + halfSpan, RegionMinCoordinate + RegionExtent - halfSpan);

        /// <summary>用同一投影绘制道路、河流与标记，地图组负责裁剪放大的内容。</summary>
        private void DrawRegionContents(Rect viewport, float scale, Vector2 player)
        {
            Vector2 Project(Vector2 point) => viewport.center + (point - regionCenter) * scale;

            ScavengeFill(viewport, new Color(.071f, .104f, .112f));
            var chartStart = Project(Vector2.one * RegionMinCoordinate);
            var chart = new Rect(chartStart, Vector2.one * RegionExtent * scale);
            ScavengeFill(chart, new Color(.119f, .185f, .173f));
            float gridStep = regionZoom >= 5f ? 100f : 250f;
            Color grid = new Color(.24f, .34f, .31f, .55f);
            for (float offset = 0f; offset <= RegionExtent; offset += gridStep)
            {
                float delta = offset * scale;
                ScavengeFill(new Rect(chart.x + delta, chart.y, 1, chart.height), grid);
                ScavengeFill(new Rect(chart.x, chart.y + delta, chart.width, 1), grid);
            }

            var bounds = scavengeRun.MapBounds;
            var active = new Rect(Project(ToRegionPoint(new Vector2(bounds.xMin, bounds.yMax))), bounds.size * scale);
            ScavengeFill(active, new Color(.35f, .70f, .54f, .07f));
            ScavengeFill(new Rect(active.xMin, active.yMin, active.width, 1), RegionBoundaryColor);
            ScavengeFill(new Rect(active.xMin, active.yMax, active.width, 1), RegionBoundaryColor);
            ScavengeFill(new Rect(active.xMin, active.yMin, 1, active.height), RegionBoundaryColor);
            ScavengeFill(new Rect(active.xMax, active.yMin, 1, active.height), RegionBoundaryColor);

            if (!ReferenceEquals(regionRouteSource, riverValleyRegion)) CacheRegionRoutes();
            if (regionRouteTexture != null) GUI.DrawTexture(chart, regionRouteTexture);

            var playerPin = Project(player);
            var occupied = new System.Collections.Generic.List<Rect>
            {
                new Rect(playerPin - Vector2.one * 12f, Vector2.one * 24f)
            };
            var labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
            if (survivalWorldGeometry != null && regionZoom >= 2.5f)
            {
                var entranceStyle = new GUIStyle(labelStyle) { normal = { textColor = new Color(.04f, .12f, .15f) } };
                var rooms = new SurvivalRoomSet(survivalWorldGeometry.Interiors);
                for (int i = 0; i < 6; i++)
                {
                    Vector2 pin = Project(ToRegionPoint(rooms.Door(i).center));
                    if (!viewport.Contains(pin)) continue;
                    var badge = new Rect(pin - Vector2.one * 9, Vector2.one * 18);
                    if (occupied.Exists(other => other.Overlaps(badge))) continue;
                    occupied.Add(badge);
                    ScavengeFill(badge, RegionEntranceColor);
                    GUI.Label(badge, (i + 1).ToString(), entranceStyle);
                }
            }

            var landmarks = riverValleyRegion?.landmarks;
            if (landmarks != null)
            {
                // 选中地标优先排字，其他地标留出足够间距。
                if (riverValleySelectedLandmark >= 0 && riverValleySelectedLandmark < landmarks.Length)
                {
                    var selected = landmarks[riverValleySelectedLandmark];
                    DrawRegionLandmark(viewport, Project(new Vector2(selected.x, selected.y)), RegionLandmarkName(selected),
                        occupied, labelStyle, true);
                }
                for (int i = 0; i < landmarks.Length; i++)
                {
                    if (i == riverValleySelectedLandmark) continue;
                    var place = landmarks[i];
                    DrawRegionLandmark(viewport, Project(new Vector2(place.x, place.y)), RegionLandmarkName(place),
                        occupied, labelStyle, false);
                }
            }

            ScavengeFill(new Rect(playerPin - Vector2.one * 7, Vector2.one * 14), new Color(.09f, .13f, .13f));
            ScavengeFill(new Rect(playerPin - Vector2.one * 4, Vector2.one * 8), RegionPlayerColor);
            ScavengeFill(new Rect(12, 12, 60, 28), new Color(.06f, .10f, .11f, .94f));
            GUI.Label(new Rect(12, 12, 60, 28), Loc.T("RegionMapNorth") + " ↑", labelStyle);
            int scaleMeters = regionZoom < 2.5f ? 200 : regionZoom < 7f ? 100 : 25;
            float scaleWidth = scaleMeters * scale;
            var scaleBox = new Rect(viewport.xMax - Mathf.Max(82f, scaleWidth + 24f) - 12f, viewport.yMax - 52,
                Mathf.Max(82f, scaleWidth + 24f), 40);
            ScavengeFill(scaleBox, new Color(.06f, .10f, .11f, .94f));
            GUI.Label(scaleBox, Loc.T("RegionMapMeters", scaleMeters), labelStyle);
            ScavengeFill(new Rect(scaleBox.center.x - scaleWidth / 2f, scaleBox.yMax - 7f, scaleWidth, 2), Color.white);
        }

        /// <summary>标注只在视野内显示，避开玩家、入口和已占用的文字区域。</summary>
        private static void DrawRegionLandmark(Rect viewport, Vector2 pin, string name,
            System.Collections.Generic.List<Rect> occupied, GUIStyle style, bool selected)
        {
            if (!viewport.Contains(pin)) return;
            Color color = selected ? new Color(1f, .65f, .34f) : new Color(.75f, .82f, .75f);
            float radius = selected ? 5f : 2f;
            ScavengeFill(new Rect(pin - Vector2.one * radius, Vector2.one * (2f * radius)), color);
            float width = style.CalcSize(new GUIContent(name)).x + 12f;
            float x = Mathf.Clamp(pin.x + 8, 4, viewport.width - width - 4);
            foreach (float y in new[] { pin.y - 11, pin.y + 12, pin.y - 34 })
            {
                var label = new Rect(x, y, width, 22);
                if (y < 4 || label.yMax > viewport.height - 4 || occupied.Exists(other => other.Overlaps(label)))
                    continue;
                occupied.Add(label);
                float edgeX = Mathf.Clamp(pin.x, label.xMin, label.xMax);
                float edgeY = Mathf.Clamp(pin.y, label.yMin, label.yMax);
                ScavengeFill(new Rect(Mathf.Min(pin.x, edgeX), pin.y, Mathf.Abs(edgeX - pin.x), 1), color);
                ScavengeFill(new Rect(edgeX, Mathf.Min(pin.y, edgeY), 1, Mathf.Abs(edgeY - pin.y)), color);
                ScavengeFill(label, selected ? new Color(.29f, .21f, .12f) : new Color(.078f, .13f, .13f, .94f));
                GUI.Label(label, name, style);
                return;
            }
        }

        /// <summary>点选地标后将地图移到对应位置，并在地图旁标明方位和直线距离。</summary>
        private void DrawRiverValleyLandmarks(Rect panel)
        {
            ScavengeFill(panel, new Color(.086f, .126f, .141f));
            var heading = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            var caption = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            float x = panel.x + 14;
            float width = panel.width - 28;
            var places = riverValleyRegion?.landmarks;
            GUI.Label(new Rect(x, panel.y + 10, width, 30), Loc.T("RegionMapLandmarks"), heading);
            if (places != null)
            {
                var list = new Rect(x, panel.y + 49, width, panel.height - 247);
                var rowStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 15,
                    hover = { textColor = RegionEntranceColor },
                    active = { textColor = RegionEntranceColor }
                };
                riverValleyLandmarkScroll = GUI.BeginScrollView(list, riverValleyLandmarkScroll,
                    new Rect(0, 0, width - 20, places.Length * 33));
                try
                {
                    for (int i = 0; i < places.Length; i++)
                    {
                        var row = new Rect(0, i * 33, width - 22, 30);
                        bool selected = riverValleySelectedLandmark == i;
                        if (selected) ScavengeFill(row, new Color(.20f, .30f, .28f));
                        if (selected) ScavengeFill(new Rect(row.x, row.y + 11, 5, 5), RegionEntranceColor);
                        var button = new Rect(row.x + 10, row.y, row.width - 10, row.height);
                        if (GUI.Button(button, RegionLandmarkName(places[i]), rowStyle))
                        {
                            riverValleySelectedLandmark = i;
                            regionZoom = Mathf.Max(3f, regionZoom);
                            regionCenter = new Vector2(places[i].x, places[i].y);
                        }
                    }
                }
                finally { GUI.EndScrollView(); }
            }

            string details = Loc.T("RegionMapSelect");
            if (places != null && riverValleySelectedLandmark >= 0 && riverValleySelectedLandmark < places.Length)
            {
                var place = places[riverValleySelectedLandmark];
                var delta = new Vector2(place.x - RegionWorldOrigin.x, RegionWorldOrigin.y - place.y) - scavengeRun.Player;
                string direction = delta.magnitude < 20 ? Loc.T("RegionMapNear") :
                    (Mathf.Abs(delta.x) > Mathf.Abs(delta.y) * .4f ? delta.x > 0 ? Loc.T("RegionMapEast") : Loc.T("RegionMapWest") : "") +
                    (Mathf.Abs(delta.y) > Mathf.Abs(delta.x) * .4f ? delta.y > 0 ? Loc.T("RegionMapNorth") : Loc.T("RegionMapSouth") : "");
                details = RegionLandmarkName(place) + "\n" + Loc.T("RegionMapDistance", direction, delta.magnitude);
            }
            float footer = panel.yMax - 187;
            ScavengeFill(new Rect(x, footer, width, 1), new Color(.24f, .34f, .35f));
            GUI.Label(new Rect(x, footer + 8, width, 48), details, caption);
            DrawRegionLegend(x, footer + 65, RegionPlayerColor, Loc.T("RegionMapPlayer"), caption);
            DrawRegionLegend(x, footer + 88, RegionBoundaryColor, Loc.T("RegionMapBounds"), caption);
            if (survivalWorldGeometry != null)
                DrawRegionLegend(x, footer + 111, RegionEntranceColor,
                    regionZoom >= 2.5f ? Loc.T("RegionMapEntrances") : Loc.T("RegionMapZoomEntrances"), caption);
            DrawRegionLegend(x, footer + 134, RegionMainRoadColor, Loc.T("RegionMapRoad"), caption);
            DrawRegionLegend(x, footer + 157, RegionWaterColor, Loc.T("RegionMapRiver"), caption);
        }

        /// <summary>区域图上的地标使用图纸名称作为语言表键。</summary>
        private static string RegionLandmarkName(RegionLandmark place)
        {
            string key = "RegionMapLandmark." + place.name;
            string translated = Loc.T(key);
            return translated == key ? place.name : translated;
        }

        /// <summary>用图上的标记色绘制图例。</summary>
        private static void DrawRegionLegend(float x, float y, Color color, string label, GUIStyle style)
        {
            ScavengeFill(new Rect(x + 3, y + 6, 8, 8), color);
            GUI.Label(new Rect(x + 19, y, 200, 23), label, style);
        }

        /// <summary>按图纸统一光栅化道路与水系，让缩放后的地图边缘正确裁剪。</summary>
        private void CacheRegionRoutes()
        {
            ReleaseRegionRoutes();
            regionRouteSource = riverValleyRegion;
            if (regionRouteSource?.routes == null) return;

            const int resolution = 2048;
            const float pixelsPerMeter = resolution / RegionExtent;
            var pixels = new Color32[resolution * resolution];
            foreach (var route in regionRouteSource.routes)
            {
                bool water = route.kind == "river" || route.kind == "stream";
                bool main = route.kind == "main";
                Color32 color = water ? RegionWaterColor : main ? RegionMainRoadColor : new Color(.51f, .52f, .43f);
                float radius = Mathf.Max(1.5f, route.width * pixelsPerMeter / 2f);
                for (int i = 1; i < route.points.Length; i++)
                {
                    Vector2 origin = Vector2.one * RegionMinCoordinate;
                    Vector2 start = (route.points[i - 1] - origin) * pixelsPerMeter;
                    Vector2 end = (route.points[i] - origin) * pixelsPerMeter;
                    RasterRegionSegment(pixels, resolution, start, end, radius, color);
                }
            }
            regionRouteTexture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
            {
                name = "River Valley Map Routes",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            regionRouteTexture.SetPixels32(pixels);
            regionRouteTexture.Apply(false, true);
        }

        /// <summary>将一段线绘成圆头笔划，边缘透明度减少缩放后的锯齿。</summary>
        private static void RasterRegionSegment(Color32[] pixels, int resolution, Vector2 start, Vector2 end,
            float radius, Color32 color)
        {
            Vector2 min = Vector2.Min(start, end) - Vector2.one * (radius + 1f);
            Vector2 max = Vector2.Max(start, end) + Vector2.one * (radius + 1f);
            int left = Mathf.Max(0, Mathf.FloorToInt(min.x));
            int top = Mathf.Max(0, Mathf.FloorToInt(min.y));
            int right = Mathf.Min(resolution, Mathf.CeilToInt(max.x));
            int bottom = Mathf.Min(resolution, Mathf.CeilToInt(max.y));
            Vector2 delta = end - start;
            float inverseLengthSquared = 1f / Mathf.Max(delta.sqrMagnitude, .001f);
            for (int y = top; y < bottom; y++)
            {
                int row = (resolution - 1 - y) * resolution;
                for (int x = left; x < right; x++)
                {
                    var fromStart = new Vector2(x + .5f, y + .5f) - start;
                    float t = Mathf.Clamp01(Vector2.Dot(fromStart, delta) * inverseLengthSquared);
                    float distance = (fromStart - t * delta).magnitude;
                    color.a = (byte)(255f * Mathf.Clamp01(radius + .5f - distance));
                    if (color.a > 0 && color.a >= pixels[row + x].a) pixels[row + x] = color;
                }
            }
        }

        /// <summary>释放地图图层占用的显存。</summary>
        private void ReleaseRegionRoutes()
        {
            if (regionRouteTexture != null) Destroy(regionRouteTexture);
            regionRouteTexture = null;
        }
    }
}
