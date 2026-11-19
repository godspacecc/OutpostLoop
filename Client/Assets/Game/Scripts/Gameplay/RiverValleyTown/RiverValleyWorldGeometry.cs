using System;
using System.Collections.Generic;
using UnityEngine;

namespace OutpostLoop.RiverValley
{
    /// <summary>由实际外景生成的纯几何快照，领域移动与可见地形共用，不依赖场景对象存活。</summary>
    internal sealed class RiverValleyWorldGeometry
    {
        // 原图老镇坐标与领域坐标的共同零点。
        internal static readonly Vector2 Anchor = new Vector2(-110, 115);
        // 整图二维范围，不随当前镜头或搜索位置变化。
        internal readonly Rect Bounds = new Rect(-890, -1115, 2000, 2000);
        // 可见实体对应的不可通行形状。
        internal readonly List<Solid> Solids = new List<Solid>();
        // 水道、桥面和道路使用原图中心线与宽度。
        internal readonly List<Channel> Water = new List<Channel>(), Bridges = new List<Channel>();
        internal readonly List<Channel> Roads = new List<Channel>();
        // 六处实际室内，前两处可设为基地。
        internal readonly Rect[] Interiors = new Rect[6];
        // 幸存者站在诊所搜点另一侧，远离入口与柜台。
        internal Vector2 ClinicSurvivor => Interiors[2].center + Vector2.right * 4;
        // 转运记录附在原有仓库装卸架上，不单独生成搜点或库存物品。
        internal const int TransferNoteCache = 7;
        // 沿用原容器索引，以保持生成规则和旧档格式独立兼容。
        internal readonly Vector2[] Caches = new Vector2[ScavengeTown.Caches.Length];
        internal readonly string[] CacheNames = (string[])ScavengeTown.Names.Clone();
        // 本趟落地与撤离位置。
        internal Vector2 Start, Exit;
        // 建筑外绕行节点，只在构建地图时生成。
        internal Vector2[] Corners;

        internal readonly struct Solid
        {
            // 平面中心、半尺寸与绕 Y 轴的旋转角。
            internal readonly Vector2 Center, Half;
            internal readonly float Angle;
            // 静态建筑旋转只计算一次，避免每次寻路采样反复调用三角函数。
            private readonly float cosine, sine;
            internal Solid(Vector2 center, Vector2 size, float angle)
            { Center = center; Half = size / 2; Angle = angle; cosine = Mathf.Cos(angle); sine = Mathf.Sin(angle); }
            /// <summary>将采样点转换到实体局部坐标，并保留角色半径余量。</summary>
            internal bool Contains(Vector2 point, float padding)
            {
                var delta = point - Center;
                float extent = Half.x + Half.y + padding * 1.5f;
                if (Mathf.Abs(delta.x) > extent || Mathf.Abs(delta.y) > extent) return false;
                return Mathf.Abs(cosine * delta.x - sine * delta.y) < Half.x + padding &&
                    Mathf.Abs(sine * delta.x + cosine * delta.y) < Half.y + padding;
            }
            /// <summary>在线段局部坐标中裁剪两轴，薄隔墙也不能从采样点之间漏过。</summary>
            internal bool Intersects(Vector2 from,Vector2 to,float padding)
            {
                var center=Center;float c=cosine,s=sine;
                Vector2 Local(Vector2 point)
                {
                    var d=point-center;
                    return new Vector2(c*d.x-s*d.y,s*d.x+c*d.y);
                }
                var start=Local(from);var direction=Local(to)-start;
                float enter=0,leave=1;
                for(int axis=0;axis<2;axis++)
                {
                    float extent=Half[axis]+padding;
                    if(Mathf.Abs(direction[axis])<.00001f)
                    {
                        if(Mathf.Abs(start[axis])>=extent)return false;
                        continue;
                    }
                    float a=(-extent-start[axis])/direction[axis],b=(extent-start[axis])/direction[axis];
                    enter=Mathf.Max(enter,Mathf.Min(a,b));leave=Mathf.Min(leave,Mathf.Max(a,b));
                    if(enter>leave)return false;
                }
                return true;
            }
        }

        internal readonly struct Channel
        {
            // 中心线端点和实际全宽。
            internal readonly Vector2 A, B;
            internal readonly float Width;
            internal Channel(Vector2 a, Vector2 b, float width) { A = a; B = b; Width = width; }
            /// <summary>计算到有限中心线段的距离，桥头端点同样参与边界判定。</summary>
            internal float Distance(Vector2 p)
            {
                var axis = B - A;
                return Vector2.Distance(p, A + axis * Mathf.Clamp01(Vector2.Dot(p - A, axis) / Mathf.Max(.001f, axis.sqrMagnitude)));
            }
        }

        // 新布局显式启用一次；旧整图实例保持空值，继续原坐标与空室内。
        internal RiverValleyShopInterior Shop { get; private set; }
        /// <summary>只为新版行动加入一次陈设实体，随后重建搜索位置与绕行节点。</summary>
        internal void EnableShopInterior()
        {
            if(Shop!=null)return;
            var rooms=new SurvivalRoomSet(new[]{Interiors[0],Interiors[1]});
            Shop=new RiverValleyShopInterior(Interiors[0],rooms.Bed(0),rooms.Cabinet(0));
            foreach(var fixture in Shop.Fixtures)Solids.Add(new Solid(fixture.Area.center,fixture.Area.size,0));
            Finish();
        }

        /// <summary>河水不可直接穿越；桥面宽度扣除护栏余量后才可通行。</summary>
        internal bool Walkable(Vector2 point, float padding = .34f)
        {
            if (point.x < Bounds.xMin || point.x > Bounds.xMax || point.y < Bounds.yMin || point.y > Bounds.yMax) return false;
            return ClearBody(point, padding) && TerrainWalkable(point, padding);
        }

        /// <summary>建筑和道具阻挡视线；水面本身不等同墙体。</summary>
        internal bool ClearBody(Vector2 point, float padding)
        {
            foreach (var solid in Solids) if (solid.Contains(point, padding)) return false;
            return true;
        }

        /// <summary>新版室内使用精确线段判定，同时覆盖旋转建筑与薄隔墙。</summary>
        internal bool ClearSegment(Vector2 from,Vector2 to,float padding)
        {
            foreach(var solid in Solids)if(solid.Intersects(from,to,padding))return false;
            return true;
        }

        /// <summary>通行额外要求陆地或足够宽的桥面；与视线判定分开。</summary>
        internal bool TerrainWalkable(Vector2 point, float padding)
        {
            if (WaterMargin(point) >= padding + 1) return true;
            foreach (var bridge in Bridges) if (bridge.Distance(point) <= bridge.Width / 2 - padding - .3f) return true;
            return false;
        }

        /// <summary>返回离岸余量；远处干地截断为 190 米，避免无意义的远河段精算。</summary>
        internal float WaterMargin(Vector2 point)
        {
            float distance = 190;
            foreach (var river in Water)
            {
                float bound = river.Width / 2 + distance;
                if (Mathf.Abs(point.x - river.A.x) > bound + Mathf.Abs(river.B.x - river.A.x) ||
                    Mathf.Abs(point.y - river.A.y) > bound + Mathf.Abs(river.B.y - river.A.y)) continue;
                distance = Mathf.Min(distance, river.Distance(point) - river.Width / 2);
            }
            return distance;
        }

        /// <summary>模型高度取实际河床、地面和桥面；二维领域仍以米记录平面位置。</summary>
        internal float Height(Vector2 point)
        {
            foreach (var interior in Interiors)
                if (interior.Contains(point)) return RiverValleyTownPreview.NaturalHeight(interior.center + Anchor) + .04f;
            float natural = RiverValleyTownPreview.NaturalHeight(point + Anchor);
            foreach (var bridge in Bridges) if (bridge.Distance(point) <= bridge.Width / 2) return natural + .19f;
            foreach (var road in Roads) if (road.Distance(point) <= road.Width / 2) return natural + .19f;
            float dry = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-6, 10, WaterMargin(point)));
            return Mathf.Lerp(-2.15f, natural, dry);
        }

        /// <summary>以确定顺序搜索附近干地，找不到入口时显式报告地图配置错误。</summary>
        internal Vector2 FindWalkable(Vector2 preferred)
        {
            if (Walkable(preferred, .5f)) return preferred;
            for (int radius = 1; radius <= 40; radius++)
                for (int direction = 0; direction < 16; direction++)
                {
                    float angle = direction * Mathf.PI / 8;
                    var point = preferred + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    if (Walkable(point, .5f)) return point;
                }
            throw new InvalidOperationException("河谷镇入口附近没有可用地面。");
        }

        /// <summary>沿原有 12 个活动容器索引布置整图搜点，不改变物资生成数量。</summary>
        internal void Finish()
        {
            var rooms = new SurvivalRoomSet(new[] { Interiors[0], Interiors[1] });
            Start = FindWalkable(rooms.Door(0).center + new Vector2(-5, -3));
            Exit = FindWalkable(Start + Vector2.left * 7);
            for (int i = 0; i < Caches.Length; i++) Caches[i] = Start;
            Caches[0] = Start + Vector2.up;
            Caches[1] = Interiors[0].center; Caches[2] = FindWalkable(rooms.Door(0).center + Vector2.left * 3);
            Caches[3] = Interiors[1].center; Caches[4] = FindWalkable(rooms.Door(1).center + Vector2.left * 3);
            Caches[5] = Interiors[1].center + Vector2.up * 2;
            Caches[6] = Interiors[2].center;
            Caches[TransferNoteCache] = Interiors[3].center; Caches[8] = Interiors[3].center + Vector2.up * 2;
            Caches[10] = Interiors[3].center + Vector2.down * 2;
            Caches[12] = Interiors[4].center; Caches[14] = Interiors[5].center;
            string[] names = { "镇口补给箱", "主街杂货店", "主街门廊", "住宅储藏柜", "住宅后院", "住宅床边柜",
                "社区诊所", "仓库装卸架", "仓库安保柜", "", "仓库检修台", "", "医院入口物资柜", "", "西岸桥头店铺" };
            foreach (int index in ScavengeSurvival.ActiveCaches) CacheNames[index] = names[index];
            if (Shop != null) Caches[1] = Shop.CheckoutTarget;
            var corners = new List<Vector2>();
            foreach (var solid in Solids)
                for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                    {
                        var delta = new Vector2(x * (solid.Half.x + .55f), y * (solid.Half.y + .55f));
                        float c = Mathf.Cos(solid.Angle), s = Mathf.Sin(solid.Angle);
                        var point = solid.Center + new Vector2(c * delta.x + s * delta.y, -s * delta.x + c * delta.y);
                        if (Walkable(point)) corners.Add(point);
                    }
            Corners = corners.ToArray();
        }
    }
}
