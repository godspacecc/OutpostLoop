using System;
using UnityEngine;

namespace OutpostLoop.RiverValley
{
    public sealed partial class RiverValleyTownPreview
    {
        private Transform buildingColliders;
        private readonly System.Collections.Generic.List<Footprint> hardstands = new System.Collections.Generic.List<Footprint>();
        private static readonly Color32 Window = Tint("#3e4d4e");
        private static readonly Color32 Trim = Tint("#b5ada0");
        private static readonly Color32 TreeBark = Tint("#595246");
        private static readonly Color32 Canopy = Tint("#576545");

        private void BuildBuildings()
        {
            buildingColliders = Section("建筑实体");
            foreach (var building in layout.buildings) BuildBuilding(building);
            BuildTownFrontage();
        }

        private void BuildBuilding(Building building)
        {
            Vector2 center = Map(building.x, building.y);
            Quaternion rotation = Quaternion.Euler(0, building.angle * Mathf.Rad2Deg, 0);
            center = Setback(center, rotation, building.width, building.depth);
            var random = new System.Random(Mathf.RoundToInt(building.x * 37 + building.y * 11));
            float width = building.width, depth = building.depth;
            bool shop = building.style == "shop";
            bool work = building.style == "work";
            bool farm = building.style == "farm";
            bool glass = building.style == "glass";
            bool civic = building.style == "public";
            bool hospital = civic && Vector2.Distance(center, Map(2280,1308)) < 63 && width > 60;
            float height = glass ? 3 : work ? 5.7f : civic ? 7.4f : farm ? 3.8f : shop ? 3.6f : 4.1f + random.Next(3);
            if (hospital) height = 13;
            float ground = NaturalHeight(center) - .14f;
            var origin = At(center, ground);
            Vector3 Local(float x, float y, float z) => origin + rotation * new Vector3(x, y, z);
            void Box(float x, float y, float z, float w, float h, float d, Color32 color) =>
                scenery.Box(Local(x,y,z), new Vector3(w,h,d), rotation, color);

            Color32 wall = glass ? Tint("#a1b4a4") : work ? Tint("#919a94") : civic ? Tint("#b7b4a5") :
                farm ? Tint("#958572") : Color32.Lerp(Tint("#aa8f78"),Tint("#c4bdad"),(float)random.NextDouble());
            Color32 roof = glass ? Tint("#b7c8b4") : work ? Tint("#686f6a") : civic ? Tint("#656a66") :
                Color32.Lerp(Tint("#645550"),Tint("#8b6350"),(float)random.NextDouble());
            Box(0,height/2,0,width,height,depth,wall);
            Box(0,.20f,0,width + .13f,.4f,depth + .13f,Tint("#7d7c72"));
            Solid($"{building.style} {building.x:0}, {building.y:0}", Local(0,height/2,0),
                new Vector3(width,height,depth), rotation, buildingColliders);
            footprints.Add(new Footprint(center, Mathf.Sqrt(width * width + depth * depth) / 2));

            if (civic || shop)
            {
                Box(0,height + .16f,0,width+.5f,.32f,depth+.5f,roof);
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    Box(0,height+.48f,sign*depth*.5f,width+.5f,.65f,.29f,wall);
                    Box(sign*width*.5f,height+.48f,0,.29f,.65f,depth,wall);
                }
                if (civic)
                {
                    Box(width * .25f,height + .6f,0,3.2f,1.1f,2.6f,Trim);
                    Box(-width * .24f,height + .5f,depth*.19f,2.8f,.87f,2.4f,Trim);
                }
            }
            else
            {
                float ridge = Mathf.Clamp(depth * .17f,.8f,3.2f);
                float x = width/2 + .32f, z = depth/2 + .28f;
                var a = Local(-x,height,-z);
                var b = Local(x,height,-z);
                var c = Local(x,height,z);
                var d = Local(-x,height,z);
                var e = Local(-x,height+ridge,0);
                var f = Local(x,height+ridge,0);
                scenery.Quad(a,e,f,b,roof);
                scenery.Quad(e,d,c,f,roof);
                scenery.Triangle(d,e,a,wall);
                scenery.Triangle(b,f,c,wall);
                if (!glass && !work)
                    Box(width*.24f,height+ridge*.66f,depth*.12f,.69f,1.2f,.64f,Tint("#756d61"));
                if (glass)
                    for (float line = -width / 2 + 1; line < width / 2; line += 2.2f)
                        Box(line,height*.50f,-depth*.502f,.09f,height,.08f,Trim);
            }
            if (glass) return;

            // 立面按功能布置开间，重复的墙体和屋面使用同一套模块。
            int stories = Mathf.Max(1, Mathf.FloorToInt((height + .1f) / 2.8f));
            int bays = Mathf.Clamp(Mathf.RoundToInt(width / (work ? 7f : 3.6f)), 2, 26);
            for (int side = -1; side <= 1; side += 2)
            {
                for (int level = 0; level < stories; level++)
                for (int i = 0; i < bays; i++)
                {
                    float x = width * ((i + .5f) / bays - .5f);
                    if (level == 0 && Mathf.Abs(x) < (work ? 4 : 1.45f)) continue;
                    float y = (level + .55f) * (height / stories);
                    float w = work || shop ? 2.15f : 1.4f;
                    Box(x,y,side * (depth / 2 + .025f),w + .18f,1.45f,.12f,Trim);
                    Box(x,y,side * (depth / 2 + .093f),w,1.26f,.095f,
                        random.Next(11) == 0 ? Tint("#706a5b") : Window);
                }
                Box(0,work ? 1.65f : 1.06f,side * (depth / 2 + .08f),work ? 4.5f : 1.4f,
                    work ? 3.3f : 2.12f,.18f,work ? Tint("#6c7775") : Window);
                if (shop || civic)
                {
                    Color32 fascia = civic ? Tint("#73827f") : Color32.Lerp(Tint("#846155"),Tint("#687b72"),(float)random.NextDouble());
                    Box(0,2.82f,side * (depth / 2 + .55f),shop ? width * .73f : 5.6f,.35f,1.35f,fascia);
                }
            }
            if (work)
                Box(0,.15f,-depth/2 - 1.4f,width*.66f,.29f,2.8f,Concrete);
            if (hospital)
            {
                var emergency = Tint("#a65b54");
                Box(0,4,-depth/2-.23f,6.2f,1.4f,.21f,Tint("#dbd3bd"));
                Box(0,4,-depth/2-.36f,.88f,.23f,.12f,emergency);
                Box(0,4,-depth/2-.36f,.23f,.88f,.12f,emergency);
                Box(0,3.15f,-depth/2-1.5f,7.6f,.28f,3.4f,Tint("#87918b"));
                for (int side = -1; side <= 1; side += 2)
                    Box(side*3.45f,1.55f,-depth/2-2.9f,.25f,3.1f,.25f,Trim);
                var forecourt = Flat(Local(0,0,-depth/2-8));
                Pave(forecourt,26,15,rotation,Concrete);
                ConnectDriveway(forecourt,7);
                Vehicle(Flat(Local(-8,0,-depth/2-8)),90,false);
            }
            if (shop && width < 21)
            {
                Box(0,3.23f,-depth/2-.1f,4.5f,.43f,.17f,
                    Color32.Lerp(Tint("#58746c"),Tint("#92614b"),(float)random.NextDouble()));
                Box(-width*.39f,.45f,-depth/2-.34f,.75f,.86f,.62f,Tint("#888579"));
                Box(width*.36f,.51f,-depth/2-.43f,1.4f,1.0f,.79f,Tint("#68756a"));
            }
        }

        private void BuildTownFrontage()
        {
            var route = layout.routes[0];
            var color = Tint("#8d8a7e");
            var positions = new[] { 1672, 1694, 1717, 1740, 1763, 1785, 1810, 1832, 1854,
                1876, 1898, 1922, 1944, 1966, 1988, 2010, 2032, 2054, 2076 };
            foreach (int east in positions)
            {
                Vector2 start = Map(east,1880);
                Vector2 point = start, forward = Vector2.right;
                float nearest = float.PositiveInfinity;
                for (int i = 1; i < route.points.Length; i++)
                {
                    var edge = new Edge(Map(route.points[i - 1]),Map(route.points[i]),route.width);
                    var candidate = edge.Nearest(start);
                    float distance = (candidate - start).sqrMagnitude;
                    if (distance >= nearest) continue;
                    nearest = distance;
                    point = candidate;
                    forward = (edge.B - edge.A).normalized;
                }
                var curb = new Vector2(-forward.y,forward.x);
                float yaw = Mathf.Atan2(-forward.y,forward.x);
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    var store = point + curb * (sign * 15.8f);
                    if (!Clear(store, 8.5f, .45f)) continue;
                    float depth = 10.8f + east % 3 * .6f;
                    BuildBuilding(new Building { x = store.x+2000, y = 2000-store.y,
                        width = 13.2f + east % 4, depth = depth, angle = yaw, style = "shop" });
                    var apron = point + curb * (sign * 9.4f);
                    scenery.Box(At(apron,NaturalHeight(apron) + .148f),new Vector3(11,.14f,4.2f),
                        Quaternion.Euler(0,yaw*Mathf.Rad2Deg,0),color);
                    if (east % 3 == 0)
                    {
                        var sidewalk = point + curb * (sign * 8.6f) + forward * 6f;
                        float ground = NaturalHeight(sidewalk);
                        scenery.Box(At(sidewalk,ground+2.32f),new Vector3(.15f,4.64f,.15f),Quaternion.identity,
                            Tint("#646a62"));
                        scenery.Box(At(sidewalk - curb * (sign * .47f),ground+4.52f),
                            new Vector3(.58f,.18f,.83f),Quaternion.Euler(0,yaw*Mathf.Rad2Deg,0),Trim);
                    }
                }
            }
        }

        private Vector2 Setback(Vector2 center, Quaternion rotation, float width, float depth)
        {
            var localX = Flat(rotation * Vector3.right);
            var localZ = Flat(rotation * Vector3.forward);
            // 保留图纸街区；只调整会压到车行道的轮廓。
            for (int attempt = 0; attempt < 6; attempt++)
            {
                float largest = 0;
                var shift = Vector2.zero;
                foreach (var road in roads)
                {
                    Vector2 near = road.Nearest(center);
                    float distance = Vector2.Distance(center, near);
                    if (distance > Mathf.Max(width, depth) + road.Width) continue;
                    Vector2 axis = (road.B - road.A).normalized;
                    if (axis.sqrMagnitude < .5f) continue;
                    Vector2 normal = new Vector2(axis.y, -axis.x);
                    float extent = (Mathf.Abs(Vector2.Dot(localX, normal)) * width +
                        Mathf.Abs(Vector2.Dot(localZ, normal)) * depth) * .5f;
                    float intrusion = road.Width * .5f + extent + 1.7f - distance;
                    if (intrusion <= largest) continue;
                    largest = intrusion;
                    shift = normal * (Vector2.Dot(center - near, normal) < 0 ? -1 : 1) * intrusion;
                }
                if (largest < .1f || (center + shift - Map(2000, 2000)).magnitude > 1430) break;
                center += shift;
            }
            return center;
        }

        private void BuildSites()
        {
            int layer = 0;
            foreach (var site in layout.sites)
            {
                Vector2 point = Map(site.x, site.y);
                scenery.Box(At(point,NaturalHeight(point) + .09f + .012f * layer++),
                    new Vector3(site.width,.08f,site.depth), Quaternion.identity,Tint(site.color));
            }
            Site field = Array.Find(layout.sites, s => s.width == 76);
            if (field != null)
            {
                var center = Map(field.x,field.y);
                for (int side = -1; side <= 1; side += 2)
                {
                    var end = center + Vector2.right * (30 * side);
                    float y = NaturalHeight(end);
                    scenery.Box(At(end,y+1.48f),new Vector3(.17f,2.96f,.17f),Quaternion.identity,Window);
                    scenery.Box(At(end + Vector2.right * -side * .6f,y+2.9f),
                        new Vector3(.12f,1.05f,1.65f),Quaternion.identity,Trim);
                }
                hardstands.Add(new Footprint(center,42));
            }
            Site yard = Array.Find(layout.sites, s => s.width > 50 && s.width < 60);
            if (yard != null)
            {
                var point = Map(yard.x,yard.y);
                ConnectDriveway(point,7);
                Vehicle(point + new Vector2(13,6), 90, true);
                Vehicle(point + new Vector2(-13,-7), 90, true);
                for (int x = -22; x <= 18; x += 8)
                    scenery.Box(At(point+new Vector2(x,10),NaturalHeight(point)+.23f),
                        new Vector3(.15f,.02f,8),Quaternion.identity,Tint("#b8b39b"));
                for (int i = 0; i < 4; i++)
                    scenery.Box(At(point+new Vector2(23,4-i*2.7f),NaturalHeight(point)+.52f),
                        new Vector3(1.2f,.92f,1.4f),Quaternion.identity,Tint("#87745a"));
                hardstands.Add(new Footprint(point,34));
            }
            Site market = Array.Find(layout.sites, s => s.width == 24);
            if (market != null)
            {
                var center = Map(market.x, market.y);
                for (int i = -1; i <= 1; i++)
                {
                    var point = center + new Vector2(i * 6, 0);
                    float y = NaturalHeight(point);
                    scenery.Box(At(point,y+.86f), new Vector3(3.7f,.23f,1.6f),Quaternion.identity,Tint("#8b775a"));
                    scenery.Box(At(point,y+2.2f),new Vector3(4.5f,.16f,2.9f),Quaternion.Euler(6,0,0),
                        i == 0 ? Tint("#a28a60") : Tint("#71837a"));
                    for (int side = -1; side <= 1; side += 2)
                        scenery.Box(At(point + new Vector2(side * 1.85f,0),y+1.06f),new Vector3(.09f,2.12f,.09f),
                            Quaternion.identity,Trim);
                }
            }
            Vector2 pumps = Map(2509,1960);
            var pumpsArea = new[] { pumps, Map(2520,1937), Map(2535,1940), Map(2554,1940), Map(2570,1945) };
            foreach (var candidate in pumpsArea)
            {
                if (!Clear(candidate,8,1)) continue;
                pumps = candidate;
                float y = NaturalHeight(pumps);
                Pave(pumps,22,13,Quaternion.identity,Concrete);
                ConnectDriveway(pumps,5.5f);
                scenery.Box(At(pumps,y+3.9f),new Vector3(14,.41f,6.5f),Quaternion.identity,Tint("#967b61"));
                for (int i = -1; i <= 1; i += 2)
                {
                    var point = pumps + new Vector2(i * 4,0);
                    scenery.Box(At(point,y+1.9f),new Vector3(.24f,3.8f,.24f),Quaternion.identity,Trim);
                    scenery.Box(At(point + Vector2.up,y+.64f),new Vector3(.7f,1.28f,.6f),Quaternion.identity,Tint("#8b9b91"));
                }
                break;
            }
            foreach (var location in new[] { Map(1858,1878), Map(1918,1899), Map(1710,1910), Map(2260,1349), Map(2415,2080) })
                if (Clear(location, 2.5f, .6f)) Vehicle(location, 90, false);

            // 常见的田边设施沿地形放置，避免压住村道和桥口。
            Fence(Map(2265,2648),Map(2298,2642));
            Fence(Map(2359,2690),Map(2332,2721));
            Fence(Map(1110,1750),Map(1135,1750));
            Fence(Map(1674,2634),Map(1694,2634));
            Fence(Map(1617,1397),Map(1594,1410));
        }

        private void ConnectDriveway(Vector2 point, float width)
        {
            Vector2 roadPoint = point;
            float distance = float.PositiveInfinity;
            foreach (var road in roads)
            {
                var near = road.Nearest(point);
                float gap = (near - point).sqrMagnitude;
                if (gap >= distance) continue;
                distance = gap;
                roadPoint = near;
            }
            Vector2 direction = (roadPoint - point).normalized;
            Vector2 side = new Vector2(direction.y,-direction.x);
            int steps = Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(point,roadPoint) / 5));
            for (int i = 0; i < steps; i++)
            {
                Vector2 a = Vector2.Lerp(point,roadPoint,(float)i/steps);
                Vector2 b = Vector2.Lerp(point,roadPoint,(float)(i+1)/steps);
                Ribbon(scenery,a,b,side,side,width,NaturalHeight(a)+.11f,
                    NaturalHeight(b)+.09f,Asphalt,true);
            }
        }

        private void Pave(Vector2 center, float width, float depth, Quaternion rotation, Color32 color)
        {
            Vector3 Corner(float x, float z)
            {
                Vector2 point = center + Flat(rotation * new Vector3(x * width,0,z * depth));
                return At(point,NaturalHeight(point)+.18f);
            }
            scenery.Quad(Corner(-.5f,.5f),Corner(.5f,.5f),Corner(.5f,-.5f),Corner(-.5f,-.5f),color);
            hardstands.Add(new Footprint(center,Mathf.Max(width,depth)/2));
        }

        private void Fence(Vector2 start, Vector2 end)
        {
            Color32 wood = Tint("#746c58");
            Vector2 previous = start;
            bool linked = false;
            int count = Mathf.Max(1, Mathf.RoundToInt(Vector2.Distance(start,end)/3));
            for (int i = 0; i <= count; i++)
            {
                var point = Vector2.Lerp(start,end,(float)i/count);
                if (!Clear(point, .35f, 1.4f))
                {
                    linked = false;
                    continue;
                }
                float y = NaturalHeight(point);
                scenery.Box(At(point,y+.63f),new Vector3(.15f,1.26f,.15f),Quaternion.identity,wood);
                if (linked)
                    foreach (float level in new[] { .46f,.91f })
                        Beam(At(previous,NaturalHeight(previous)+level),At(point,y+level),.11f,.12f,wood);
                linked = true;
                previous = point;
            }
        }

        private void Vehicle(Vector2 point, float yaw, bool truck)
        {
            var rotation = Quaternion.Euler(0,yaw,0);
            var origin = At(point,NaturalHeight(point));
            float length = truck ? 8.2f : 4.2f;
            var color = truck ? Tint("#9e9b89") : Tint("#887269");
            scenery.Box(origin + Vector3.up * .66f,new Vector3(truck ? 2.4f : 1.78f,.75f,length),rotation,color);
            scenery.Box(origin + rotation * new Vector3(0,truck ? 1.9f : 1.18f,truck ? -.75f : -.2f),
                new Vector3(truck ? 2.4f : 1.57f,truck ? 2.55f : .49f,truck ? 5.8f : 2.25f),rotation,truck ? color : Window);
            scenery.Box(origin + rotation * new Vector3(0,truck ? 1.52f : 1.44f,truck ? 3f : -.2f),
                new Vector3(truck ? 2.22f : 1.55f,truck ? 1.25f : .09f,truck ? 1.72f : 1.62f),rotation,color);
            if (truck)
                scenery.Box(origin+rotation*new Vector3(0,1.72f,3.88f),
                    new Vector3(1.75f,.55f,.04f),rotation,Window);
            for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
                scenery.Box(origin + rotation * new Vector3(x * (truck ? 1.06f : .74f),.32f,z * length * .33f),
                    new Vector3(.32f,.63f,.66f),rotation,Tint("#41433c"));
            Solid("停放车辆",origin+Vector3.up*.7f,new Vector3(truck?2.4f:1.8f,1.4f,length),rotation,buildingColliders);
        }

        private void BuildLandscape()
        {
            var random = new System.Random(2711);
            for (int z = -975; z < 1000; z += 15)
            for (int x = -975; x < 1000; x += 15)
            {
                var point = new Vector2(x + random.Next(-6,7),z + random.Next(-6,7));
                float woodland = Mathf.PerlinNoise(5 + point.x/185,8 + point.y/205);
                float margin = WaterMargin(point);
                bool bank = margin > 14 && margin < 28;
                float density = point.magnitude > 690 ? .22f + .65f * woodland : .16f + .18f * woodland;
                if (random.NextDouble() > (bank ? .72f : density * woodland) || InField(point) || !Clear(point,3,3) ||
                    hardstands.Exists(site => (site.Center-point).sqrMagnitude < site.Radius * site.Radius))
                    continue;
                Tree(point,3.6f + (float)random.NextDouble() * 5.4f,woodland);
            }
        }

        private bool Clear(Vector2 point, float radius, float setback)
        {
            if (WaterMargin(point) < radius + 9) return false;
            foreach (var footprint in footprints)
                if ((footprint.Center - point).sqrMagnitude < Mathf.Pow(footprint.Radius + radius,2)) return false;
            foreach (var edge in roads)
                if (edge.Distance(point) < edge.Width * .5f + radius + setback) return false;
            return true;
        }

        private void Tree(Vector2 point, float height, float variation)
        {
            float y = NaturalHeight(point);
            scenery.Box(At(point,y + height*.27f),new Vector3(.24f,height*.56f,.24f),Quaternion.identity,TreeBark);
            var color = Color32.Lerp(Canopy,Tint("#7a8156"),variation);
            scenery.Box(At(point+new Vector2(height*.095f,0),y+height*.51f),
                new Vector3(.15f,height*.33f,.15f),Quaternion.Euler(0,0,-28),TreeBark);
            Crown(point,height*.68f,height*.29f,height*.24f,color);
            Crown(point+new Vector2(height*.13f,height*.04f),height*.62f,height*.20f,height*.18f,
                Color32.Lerp(color,TreeBark,.16f));
            Crown(point+new Vector2(-height*.10f,height*.065f),height*.80f,height*.20f,height*.17f,
                Color32.Lerp(color,Tint("#909363"),.16f));

            void Crown(Vector2 center, float level, float rise, float radius, Color32 leaf)
            {
                Vector3 top = At(center,y+level+rise);
                Vector3 bottom = At(center,y+level-rise*.65f);
                Vector3 Ring(int i)
                {
                    float angle = i * Mathf.PI / 4;
                    float jagged = i % 3 == 0 ? 1.09f : .94f;
                    return At(center+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*(radius*jagged),
                        y+level + rise * (i % 2 == 0 ? .12f : -.10f));
                }
                for (int i = 0; i < 8; i++)
                {
                    scenery.Triangle(top,Ring(i+1),Ring(i),leaf);
                    scenery.Triangle(bottom,Ring(i),Ring(i+1),Color32.Lerp(leaf,TreeBark,.18f));
                }
            }
        }

        private void Solid(string name, Vector3 position, Vector3 size, Quaternion rotation, Transform parent)
        {
            if (gameplayScenery) return;
            var item = new GameObject(name);
            item.transform.SetParent(parent,false);
            item.transform.SetPositionAndRotation(position,rotation);
            item.AddComponent<BoxCollider>().size = size;
        }
    }
}
