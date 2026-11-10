using System;
using System.Collections.Generic;
using UnityEngine;

namespace OutpostLoop
{
    /// <summary>落位、攻击距离和通行共用同一份网格几何。</summary>
    public static class BattleGrid
    {
        public const int CellSize = 2;
        public const int BuildRadius = 24;

        public static Vector2Int Cell(Vector2 point) =>
            new Vector2Int(Mathf.FloorToInt(point.x / CellSize + .5f), Mathf.FloorToInt(point.y / CellSize + .5f));

        public static Vector2 Center(Vector2Int cell) => (Vector2)cell * CellSize;
        public static Vector2 Center(RectInt footprint) => (footprint.min + (Vector2)footprint.size * .5f - Vector2.one * .5f) * CellSize;

        /// <summary>从建筑边缘量距离，单位可以在占地外发起攻击。</summary>
        public static float DistanceTo(RectInt footprint, Vector2 point)
        {
            var offset = point - Center(footprint);
            var half = (Vector2)footprint.size * (CellSize * .5f);
            return new Vector2(Mathf.Max(0, Mathf.Abs(offset.x) - half.x), Mathf.Max(0, Mathf.Abs(offset.y) - half.y)).magnitude;
        }
    }

    /// <summary>按目的地反向搜索。敌群共享基地距离场，建筑变动才使路线失效。</summary>
    internal sealed class BattleNavigation
    {
        private static readonly Vector2Int[] Directions = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
        private readonly float radiusSquared;
        private readonly int extent, side;
        private readonly bool[] blocked;
        public int Revision { get; private set; }

        internal BattleNavigation(float radius)
        {
            radiusSquared = radius * radius;
            extent = Mathf.FloorToInt(radius / BattleGrid.CellSize);
            side = extent * 2 + 1;
            blocked = new bool[side * side];
        }

        internal void Rebuild(IReadOnlyList<ExpansionSite> sites, RectInt? headquarters)
        {
            Array.Clear(blocked, 0, blocked.Length);
            if (headquarters.HasValue) Block(headquarters.Value);
            foreach (var site in sites)
                if (site.Kind != ExpansionKind.Empty) Block(site.Footprint);
            Revision++;
        }

        private void Block(RectInt footprint)
        {
            foreach (var cell in footprint.allPositionsWithin)
                if (InBounds(cell)) blocked[Index(cell)] = true;
        }

        private int Index(Vector2Int cell) => (cell.y + extent) * side + cell.x + extent;
        internal bool InBounds(Vector2Int cell) => Mathf.Abs(cell.x) <= extent && Mathf.Abs(cell.y) <= extent &&
            BattleGrid.Center(cell).sqrMagnitude <= radiusSquared;
        internal bool Walkable(Vector2Int cell) => InBounds(cell) && !blocked[Index(cell)];
        internal int Distance(int[] field, Vector2Int cell) => InBounds(cell) ? field[Index(cell)] : -1;

        /// <summary>沿线检查实际穿过的格子，同时禁止从两格相接的角上穿过占地。</summary>
        internal bool CanTravelStraight(Vector2 start, Vector2 end)
        {
            var cell = BattleGrid.Cell(start);
            var target = BattleGrid.Cell(end);
            if (!Walkable(cell) || !Walkable(target)) return false;
            var offset = end - start;
            int stepX = target.x.CompareTo(cell.x), stepY = target.y.CompareTo(cell.y);
            while (cell != target)
            {
                var center = BattleGrid.Center(cell);
                float crossX = cell.x == target.x ? float.PositiveInfinity
                    : (center.x + stepX * BattleGrid.CellSize * .5f - start.x) / offset.x;
                float crossY = cell.y == target.y ? float.PositiveInfinity
                    : (center.y + stepY * BattleGrid.CellSize * .5f - start.y) / offset.y;
                if (Mathf.Abs(crossX - crossY) <= .00001f)
                {
                    if (!Walkable(cell + new Vector2Int(stepX, 0)) || !Walkable(cell + new Vector2Int(0, stepY)))
                        return false;
                    cell += new Vector2Int(stepX, stepY);
                }
                else if (crossX < crossY) cell.x += stepX;
                else cell.y += stepY;
                if (!Walkable(cell)) return false;
            }
            return true;
        }

        /// <summary>点到建筑或圆形边界时，选最近且与单位连通的落脚点。</summary>
        internal Vector2 NearestReachable(Vector2 start, Vector2 destination)
        {
            var field = Field(new RectInt(BattleGrid.Cell(start), Vector2Int.one), 0);
            var best = start;
            float closest = float.PositiveInfinity;
            int shortest = int.MaxValue;
            for (int y = -extent; y <= extent; y++)
            for (int x = -extent; x <= extent; x++)
            {
                var cell = new Vector2Int(x, y);
                int distance = Distance(field, cell);
                if (distance < 0) continue;
                var center = BattleGrid.Center(cell);
                float error = (center - destination).sqrMagnitude;
                if (error > closest || (error == closest && distance >= shortest)) continue;
                closest = error;
                shortest = distance;
                best = center;
            }
            return best;
        }

        /// <summary>用所有可达交互格作为起点，返回四邻域最短距离。</summary>
        internal int[] Field(RectInt target, float reach)
        {
            var distances = new int[blocked.Length];
            Array.Fill(distances, -1);
            var queue = new Queue<Vector2Int>();
            int margin = Mathf.CeilToInt(reach / BattleGrid.CellSize) + 1;
            var candidates = new RectInt(target.min - Vector2Int.one * margin, target.size + Vector2Int.one * (margin * 2));
            foreach (var cell in candidates.allPositionsWithin)
            {
                if (!Walkable(cell) || BattleGrid.DistanceTo(target, BattleGrid.Center(cell)) > reach) continue;
                distances[Index(cell)] = 0;
                queue.Enqueue(cell);
            }
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                foreach (var direction in Directions)
                {
                    var next = cell + direction;
                    if (!Walkable(next) || distances[Index(next)] >= 0) continue;
                    distances[Index(next)] = distances[Index(cell)] + 1;
                    queue.Enqueue(next);
                }
            }
            return distances;
        }

        /// <summary>沿距离场逐格移动，走完格心再转向，避免斜穿建筑拐角。</summary>
        internal bool Follow(int[] field, ref Vector2 position, RouteWalker walker, float travel)
        {
            if (walker.Field != field) { walker.Field = field; walker.Next = null; }
            var cell = BattleGrid.Cell(position);
            int distance = Distance(field, cell);
            if (distance < 0) return false;
            if (!walker.Next.HasValue)
            {
                if (distance == 0)
                {
                    var center = BattleGrid.Center(cell);
                    position = Vector2.MoveTowards(position, center, travel);
                    return position == center;
                }
                // 从当前格心出发，路径变化时也不切过相邻建筑的角。
                walker.Next = BattleGrid.Center(cell);
                if (position == walker.Next.Value)
                {
                    foreach (var direction in Directions)
                    {
                        var next = cell + direction;
                        if (Distance(field, next) != distance - 1) continue;
                        walker.Next = BattleGrid.Center(next);
                        break;
                    }
                }
            }
            position = Vector2.MoveTowards(position, walker.Next.Value, travel);
            if (position == walker.Next.Value) walker.Next = null;
            return false;
        }

        /// <summary>沿共享距离场向前看，跨过能直接通行的中间格，保留绕障所需转角。</summary>
        internal void FollowSmoothed(int[] field, ref Vector2 position, RouteWalker walker, float travel)
        {
            if (walker.Revision != Revision) { walker.Next = null; walker.Revision = Revision; }
            if (!walker.Next.HasValue)
            {
                var cell = BattleGrid.Cell(position);
                int distance = Distance(field, cell);
                if (distance < 0) return;
                var waypoint = BattleGrid.Center(cell);
                // 前视有限格数。移动中的追兵继续当前短段，再以最新目标重新选点。
                for (int lookAhead = 0; lookAhead < 4 && distance > 0; lookAhead++)
                {
                    foreach (var direction in Directions)
                    {
                        var next = cell + direction;
                        if (Distance(field, next) != distance - 1) continue;
                        cell = next;
                        break;
                    }
                    var candidate = BattleGrid.Center(cell);
                    if (!CanTravelStraight(position, candidate)) break;
                    waypoint = candidate;
                    distance--;
                }
                walker.Next = waypoint;
            }
            position = Vector2.MoveTowards(position, walker.Next.Value, travel);
            if (position == walker.Next.Value) walker.Next = null;
        }
    }

    /// <summary>仅保存运动中的下一格，距离场由地图共享。</summary>
    internal sealed class RouteWalker
    {
        internal int[] Field;
        internal Vector2? Next;
        internal int Revision;
    }
}
