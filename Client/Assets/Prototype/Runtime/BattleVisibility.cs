using System.Collections.Generic;
using UnityEngine;

namespace OutpostLoop
{
    public enum FogCell : byte { Unexplored, Remembered, Visible }

    /// <summary>由一个己方单位或完工建筑提供的地面视野。</summary>
    internal readonly struct SightSource
    {
        internal readonly Vector2 Position;
        internal readonly float Radius;

        internal SightSource(Vector2 position, float radius)
        {
            Position = position;
            Radius = radius;
        }

        internal bool Sees(Vector2 point) => (Position - point).sqrMagnitude <= Radius * Radius;
    }

    /// <summary>保存每局探索记录，并提供战斗规则及地图使用的共同视野。</summary>
    public sealed class BattleVisibility
    {
        private readonly List<SightSource> sources = new List<SightSource>();
        private readonly FogCell[] cells;
        private readonly bool[] lit;
        public int Span { get; }
        public int Size => Span * 2;
        public int Revision { get; private set; }

        internal BattleVisibility(float mapRadius)
        {
            Span = Mathf.CeilToInt(mapRadius + 2);
            cells = new FogCell[Size * Size];
            lit = new bool[cells.Length];
        }

        /// <summary>用实际坐标判定动态对象和建造格，避免显示分辨率影响规则。</summary>
        public bool IsVisible(Vector2 point)
        {
            foreach (var source in sources)
                if (source.Sees(point)) return true;
            return false;
        }

        public FogCell At(int x, int y) => cells[y * Size + x];

        public bool IsExplored(Vector2 point)
        {
            int x = Mathf.FloorToInt(point.x) + Span;
            int y = Mathf.FloorToInt(point.y) + Span;
            return x >= 0 && x < Size && y >= 0 && y < Size && At(x, y) != FogCell.Unexplored;
        }

        internal bool Covers(RectInt footprint)
        {
            foreach (var part in footprint.allPositionsWithin)
                if (!IsVisible(BattleGrid.Center(part))) return false;
            return true;
        }

        /// <summary>己方视野变化时刷新一米一格的地图记录，离开视野的格子保留探索状态。</summary>
        internal void Observe(List<SightSource> next)
        {
            bool changed = next.Count != sources.Count;
            for (int i = 0; !changed && i < next.Count; i++)
                changed = !next[i].Position.Equals(sources[i].Position) || next[i].Radius != sources[i].Radius;
            if (!changed) return;

            sources.Clear();
            sources.AddRange(next);
            System.Array.Clear(lit, 0, lit.Length);
            foreach (var source in sources)
            {
                int left = Mathf.Max(0, Mathf.FloorToInt(source.Position.x - source.Radius) + Span);
                int right = Mathf.Min(Size - 1, Mathf.FloorToInt(source.Position.x + source.Radius) + Span);
                int bottom = Mathf.Max(0, Mathf.FloorToInt(source.Position.y - source.Radius) + Span);
                int top = Mathf.Min(Size - 1, Mathf.FloorToInt(source.Position.y + source.Radius) + Span);
                for (int y = bottom; y <= top; y++)
                for (int x = left; x <= right; x++)
                    if (source.Sees(new Vector2(x - Span + .5f, y - Span + .5f))) lit[y * Size + x] = true;
            }

            changed = false;
            for (int i = 0; i < cells.Length; i++)
            {
                var state = lit[i] ? FogCell.Visible : cells[i] == FogCell.Visible ? FogCell.Remembered : cells[i];
                changed |= state != cells[i];
                cells[i] = state;
            }
            if (changed) Revision++;
        }
    }
}
