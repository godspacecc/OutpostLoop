using UnityEngine;

namespace OutpostLoop.RiverValley
{
    public sealed partial class RiverValleyTownPreview
    {
        private static readonly Color32 Grass = Tint("#5b6650");
        private static readonly Color32 Bank = Tint("#8f8e73");
        private static readonly Color32 FieldSoil = Tint("#847556");
        private static readonly Color32 FieldRows = Tint("#77774c");
        private static readonly Color32 DryGrass = Tint("#8b8e6b");
        private static readonly Color32 Asphalt = Tint("#555856");
        private static readonly Color32 Shoulder = Tint("#918975");
        private static readonly Color32 Water = Tint("#527678");
        private static readonly Color32 Concrete = Tint("#929087");

        private void IndexRoutes()
        {
            foreach (var route in layout.routes)
            {
                bool channel = IsChannel(route);
                for (int i = 1; i < route.points.Length; i++)
                {
                    var edge = new Edge(Map(route.points[i - 1]), Map(route.points[i]), route.width);
                    if (channel) water.Add(edge);
                    else if (route.kind != "trail") roads.Add(edge);
                }
            }
        }

        private static bool IsChannel(Route route) => route.kind == "river" || route.kind == "stream";

        private float WaterMargin(Vector2 point)
        {
            float closest = 190;
            foreach (var edge in water)
            {
                float bound = edge.Width * .5f + closest;
                if (Mathf.Abs(point.x - edge.A.x) > bound + Mathf.Abs(edge.B.x - edge.A.x) ||
                    Mathf.Abs(point.y - edge.A.y) > bound + Mathf.Abs(edge.B.y - edge.A.y)) continue;
                closest = Mathf.Min(closest, edge.Distance(point) - edge.Width * .5f);
            }
            return closest;
        }

        private static float NaturalHeight(Vector2 point)
        {
            float hill = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(480, 1120, point.magnitude));
            float broad = Mathf.PerlinNoise(8.3f + point.x / 540, 10.4f + point.y / 510);
            float texture = Mathf.PerlinNoise(8.9f + point.x / 155, 2.3f + point.y / 165);
            return 1.15f + broad * 1.7f + texture * .42f + hill * broad * 13f;
        }

        private static float GroundHeight(Vector2 point, float margin)
        {
            // 水面固定，浅滩与河床由地形网格挖出。
            float dry = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-6, 10, margin));
            return Mathf.Lerp(-2.15f, NaturalHeight(point), dry);
        }

        private void BuildTerrain()
        {
            const int resolution = 400;
            const float pitch = HalfExtent * 2 / resolution;
            var ground = new RiverValleyMesh(Section("地形与河床"), surface, reservedArea);
            var positions = new Vector3[resolution + 1, resolution + 1];
            var colors = new Color32[resolution, resolution];
            for (int z = 0; z <= resolution; z++)
            for (int x = 0; x <= resolution; x++)
            {
                var point = new Vector2(-HalfExtent + x * pitch, HalfExtent - z * pitch);
                float margin = WaterMargin(point);
                positions[x,z] = At(point, GroundHeight(point, margin));
                if (x == resolution || z == resolution) continue;
                float noise = Mathf.PerlinNoise(4 + point.x / 58, 5 + point.y / 57);
                Color32 color = Color32.Lerp(Grass, DryGrass, noise * .52f);
                if (margin < 13) color = Color32.Lerp(Bank, color, Mathf.Clamp01(margin / 13));
                else if (InField(point)) color = Color32.Lerp(FieldSoil, FieldRows,
                    .35f + .27f * Mathf.Cos(point.x * Mathf.PI * .29f) + .18f * noise);
                colors[x,z] = color;
            }
            for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
                ground.Quad(positions[x,z], positions[x + 1,z], positions[x + 1,z + 1], positions[x,z + 1], colors[x,z]);
            ground.Finish(collision: !gameplayScenery, shadows: false);
        }

        private bool InField(Vector2 point)
        {
            foreach (var lot in layout.lots)
            {
                bool inside = false;
                for (int i = 0, j = lot.points.Length - 1; i < lot.points.Length; j = i++)
                {
                    var a = Map(lot.points[i]);
                    var b = Map(lot.points[j]);
                    if ((a.y > point.y) != (b.y > point.y) &&
                        point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                        inside = !inside;
                }
                if (inside) return true;
            }
            return false;
        }

        private void BuildRoutes()
        {
            var waterSurface = new RiverValleyMesh(Section("主河与支流"), surface, reservedArea);
            var crossings = new RiverValleyMesh(Section("桥面碰撞"), surface, reservedArea);
            foreach (var route in layout.routes)
            {
                bool channel = IsChannel(route);
                bool trail = route.kind == "trail";
                bool main = route.kind == "main";
                Color32 color = channel ? Water : trail ? Shoulder : main ? Asphalt : Tint("#686761");
                float width = route.width;
                Vector2[] points = SampleRoute(route);
                for (int i = 0; i + 1 < points.Length; i++)
                {
                    Vector2 a = points[i];
                    Vector2 b = points[i + 1];
                    if ((a - b).sqrMagnitude < .001f) continue;
                    Vector2 sideA = RoadSide(points, i);
                    Vector2 sideB = RoadSide(points, i + 1);
                    if (channel)
                    {
                        Ribbon(waterSurface, a, b, sideA, sideB, width, -.23f, -.23f, color, false);
                        continue;
                    }
                    if (trail && WaterMargin((a + b) / 2) < width) continue;
                    bool bridge = !trail && IsCrossing((a + b) / 2);
                    float offset = trail ? .065f : main ? .19f : .115f;
                    float heightA = NaturalHeight(a) + offset;
                    float heightB = NaturalHeight(b) + offset;
                    if (bridge)
                    {
                        bridgeDecks.Add(new Edge(a,b,width));
                        Ribbon(crossings, a,b,sideA,sideB,width + .55f,heightA,heightB,Tint("#747670"),false);
                        BridgeSides(a,b,sideA,sideB,width,heightA,heightB);
                    }
                    else
                    {
                        if (!trail)
                            Ribbon(scenery,a,b,sideA,sideB,width + 1.8f,heightA - .025f,heightB - .025f,Shoulder,true);
                        Ribbon(scenery,a,b,sideA,sideB,width,heightA,heightB,color,true);
                        if (main && IsTownFrontage((a + b) / 2))
                            Ribbon(scenery,a,b,sideA,sideB,width + 5f,heightA - .037f,heightB - .037f,Concrete,true);
                    }
                    if (main) RoadPaint(a,b,sideA,sideB,width,heightA,heightB);
                }
            }
            waterSurface.Finish(shadows: false);
            crossings.Finish(collision: !gameplayScenery);
        }

        private static Vector2[] SampleRoute(Route route)
        {
            var result = new System.Collections.Generic.List<Vector2> { Map(route.points[0]) };
            for (int i = 1; i < route.points.Length; i++)
            {
                var a = Map(route.points[i - 1]);
                var b = Map(route.points[i]);
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a,b) / 5f));
                for (int step = 1; step <= steps; step++)
                    result.Add(Vector2.Lerp(a,b,(float)step/steps));
            }
            return result.ToArray();
        }

        private static bool IsTownFrontage(Vector2 p) => p.x > -340 && p.x < 110 && p.y > 98 && p.y < 145;

        private static Vector2 RoadSide(Vector2[] points, int index)
        {
            var direction = (points[Mathf.Min(index + 1, points.Length - 1)] -
                points[Mathf.Max(0, index - 1)]).normalized;
            return new Vector2(direction.y, -direction.x);
        }

        private static void Ribbon(RiverValleyMesh mesh, Vector2 a, Vector2 b, Vector2 sideA,
            Vector2 sideB, float width, float heightA, float heightB, Color32 color, bool followGround)
        {
            float half = width * .5f;
            Vector3 EdgePoint(Vector2 point, Vector2 side, float height, float sign)
            {
                Vector2 edge = point + side * (half * sign);
                if (followGround) height += NaturalHeight(edge) - NaturalHeight(point);
                return At(edge, height);
            }
            mesh.Quad(EdgePoint(a,sideA,heightA,1), EdgePoint(a,sideA,heightA,-1),
                EdgePoint(b,sideB,heightB,-1), EdgePoint(b,sideB,heightB,1), color);
        }

        private bool IsCrossing(Vector2 point)
        {
            if (WaterMargin(point) > 13) return false;
            return (point - Map(1340,1910)).sqrMagnitude < 85 * 85 ||
                (point - Map(1235,2160)).sqrMagnitude < 85 * 85 ||
                (point - Map(2208,1215)).sqrMagnitude < 54 * 54;
        }

        private bool IsOnBridge(Vector2 point)
        {
            foreach (var edge in bridgeDecks)
                if (edge.Distance(point) < edge.Width * .5f - .5f) return true;
            return false;
        }

        private void BridgeSides(Vector2 a, Vector2 b, Vector2 sideA, Vector2 sideB, float width, float ha, float hb)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 start = a + sideA * (width * .5f * side);
                Vector2 end = b + sideB * (width * .5f * side);
                Beam(At(start,ha - .25f), At(end,hb - .25f), .38f, .8f, Concrete);
                Beam(At(start,ha + .78f), At(end,hb + .78f), .16f, .16f, Tint("#737e7c"));
                scenery.Box(At(start,ha + .36f), new Vector3(.2f,.87f,.2f), Quaternion.identity, Concrete);
            }
            if (WaterMargin(a) < -8 && Mathf.RoundToInt(a.x + a.y) % 3 == 0)
                scenery.Box(At(a,ha - 1.6f), new Vector3(width * .7f,3.1f,1.2f), Quaternion.identity, Concrete);
        }

        private void RoadPaint(Vector2 a, Vector2 b, Vector2 sideA, Vector2 sideB, float width, float ha, float hb)
        {
            var axis = b - a;
            float length = axis.magnitude;
            if (length > 1 && Mathf.FloorToInt((a.x + b.x + a.y + b.y) * .115f) % 2 == 0)
                Ribbon(scenery,Vector2.Lerp(a,b,.08f),Vector2.Lerp(a,b,.93f),sideA,sideB,.13f,
                    ha + .028f,hb + .028f,Tint("#c0ae80"),false);
            for (int side = -1; side <= 1; side += 2)
                Ribbon(scenery, a + sideA * ((width * .5f - .38f) * side),
                    b + sideB * ((width * .5f - .38f) * side), sideA,sideB,.11f,ha + .03f,hb + .03f,
                    Tint("#c9c9b1"), true);
        }

        private void Beam(Vector3 a, Vector3 b, float width, float height, Color32 color)
        {
            if ((b-a).sqrMagnitude < .001f) return;
            scenery.Box((a+b)/2,new Vector3(width,height,(b-a).magnitude + .02f),
                Quaternion.LookRotation(b-a),color);
        }
    }
}
