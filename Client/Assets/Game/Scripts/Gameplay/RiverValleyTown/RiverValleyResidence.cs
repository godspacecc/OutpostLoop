using UnityEngine;

namespace OutpostLoop.RiverValley
{
    /// <summary>住宅一楼、楼梯、二楼分别占用的可行走表面。</summary>
    internal enum ResidenceSurface { Ground, Stairs, Upper }

    /// <summary>同时供领域移动与场景拼装使用的住宅尺寸。</summary>
    internal sealed class RiverValleyResidence
    {
        internal const int Version = 3, RoomIndex = 1;
        internal const float StoreyHeight = 3f;
        private const float Clearance = .34f;
        internal readonly Rect Footprint, Stairwell;
        private readonly Rect upperWalkArea;

        internal RiverValleyResidence(Rect footprint)
        {
            Footprint = footprint;
            Stairwell = new Rect(footprint.xMin + 3, footprint.yMin + 1.2f, 5.4f, 2.2f);
            const float wallClearance = .28f + Clearance;
            upperWalkArea = Rect.MinMaxRect(footprint.xMin + wallClearance, footprint.yMin + wallClearance,
                footprint.xMax - wallClearance, footprint.yMax - wallClearance);
        }

        /// <summary>只有从两端入口接近，才能进入楼梯。楼梯侧面与上层洞口阻挡横穿。</summary>
        internal Vector2 Move(Vector2 from, Vector2 to, ref ResidenceSurface surface)
        {
            if (surface == ResidenceSurface.Stairs)
            {
                to.y = Mathf.Clamp(to.y, Stairwell.yMin + Clearance, Stairwell.yMax - Clearance);
                if (to.x < Stairwell.xMin) surface = ResidenceSurface.Ground;
                else if (to.x >= Stairwell.xMax) surface = ResidenceSurface.Upper;
                return to;
            }

            if (surface == ResidenceSurface.Upper)
            {
                if (!upperWalkArea.Contains(to)) return from;
                if (!TouchesStairwell(to)) return to;
                if (from.x < Stairwell.xMax || !BetweenRails(from.y) || !BetweenRails(to.y)) return from;
                if (to.x < Stairwell.xMax) surface = ResidenceSurface.Stairs;
                return to;
            }

            if (!TouchesStairwell(to, extendWest: false)) return to;
            if (from.x > Stairwell.xMin || !BetweenRails(from.y) || !BetweenRails(to.y)) return from;
            surface = ResidenceSurface.Stairs;
            return to;
        }

        /// <summary>按楼梯轴线连续计算脚底高度。</summary>
        internal float HeightOffset(Vector2 point, ResidenceSurface surface) =>
            surface == ResidenceSurface.Upper ? StoreyHeight : surface == ResidenceSurface.Stairs
                ? StoreyHeight * Mathf.InverseLerp(Stairwell.xMin, Stairwell.xMax, point.x) : 0f;

        internal bool CanRestore(Vector2 point, ResidenceSurface surface) =>
            surface == ResidenceSurface.Stairs
                ? point.x >= Stairwell.xMin && point.x < Stairwell.xMax && BetweenRails(point.y)
                : surface == ResidenceSurface.Upper && upperWalkArea.Contains(point) && !Stairwell.Contains(point);

        /// <summary>楼上无跨层目标，弹道表现仍止于本层的墙面或楼梯边界。</summary>
        internal bool ContainsShot(Vector2 point, ResidenceSurface surface) =>
            surface == ResidenceSurface.Stairs ? Stairwell.Contains(point) :
                upperWalkArea.Contains(point) && !Stairwell.Contains(point);

        private bool BetweenRails(float y) => y >= Stairwell.yMin + Clearance && y <= Stairwell.yMax - Clearance;

        private bool TouchesStairwell(Vector2 point, bool extendWest = true) =>
            point.x >= Stairwell.xMin - (extendWest ? Clearance : 0) && point.x < Stairwell.xMax + Clearance &&
            point.y >= Stairwell.yMin - Clearance && point.y < Stairwell.yMax + Clearance;
    }
}
