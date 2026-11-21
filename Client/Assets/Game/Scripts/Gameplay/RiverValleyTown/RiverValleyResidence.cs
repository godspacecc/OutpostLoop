using UnityEngine;

namespace OutpostLoop.RiverValley
{
    /// <summary>住宅各通行表面，枚举值写入续玩档。</summary>
    internal enum ResidenceSurface { Ground = 0, Stairs = 1, Upper = 2, RoofStairs = 3, Roof = 4 }

    /// <summary>同时供领域移动与场景拼装使用的住宅尺寸。</summary>
    internal sealed class RiverValleyResidence
    {
        internal const int UpperFloorVersion = 3, Version = 4, RoomIndex = 1;
        internal const float StoreyHeight = 3f;
        internal const float RoofHeight = 2 * StoreyHeight;
        internal const float WallWidth = .28f;
        private const float Clearance = .34f;
        internal readonly Rect Footprint, Stairwell, RoofStairwell;
        internal readonly bool HasRoof;
        private readonly Rect upperWalkArea;

        internal RiverValleyResidence(Rect footprint, bool hasRoof)
        {
            Footprint = footprint;
            HasRoof = hasRoof;
            Stairwell = new Rect(footprint.xMin + 3, footprint.yMin + 1.2f, 5.4f, 2.2f);
            // 第二段贴另一侧外墙，保留两个楼梯口之间的二楼通道。
            RoofStairwell = new Rect(footprint.xMin + 3, footprint.yMax - 3.4f, 5.4f, 2.2f);
            const float wallClearance = WallWidth + Clearance;
            upperWalkArea = Rect.MinMaxRect(footprint.xMin + wallClearance, footprint.yMin + wallClearance,
                footprint.xMax - wallClearance, footprint.yMax - wallClearance);
        }

        /// <summary>只有从两端入口接近，才能进入楼梯。楼梯侧面与上层洞口阻挡横穿。</summary>
        internal Vector2 Move(Vector2 from, Vector2 to, ref ResidenceSurface surface)
        {
            if (surface == ResidenceSurface.Stairs)
                return MoveOnStairs(to, Stairwell, ResidenceSurface.Ground, ResidenceSurface.Upper, ref surface);
            if (surface == ResidenceSurface.RoofStairs)
                return MoveOnStairs(to, RoofStairwell, ResidenceSurface.Upper, ResidenceSurface.Roof, ref surface);
            if (surface == ResidenceSurface.Ground)
                return ApproachStairs(from, to, Stairwell, ResidenceSurface.Stairs, false, ref surface);
            if (!upperWalkArea.Contains(to)) return from;

            if (surface == ResidenceSurface.Roof)
                return ApproachStairs(from, to, RoofStairwell, ResidenceSurface.RoofStairs, true, ref surface);
            if (HasRoof && TouchesStairwell(to, RoofStairwell, false))
                return ApproachStairs(from, to, RoofStairwell, ResidenceSurface.RoofStairs, false, ref surface);
            return ApproachStairs(from, to, Stairwell, ResidenceSurface.Stairs, true, ref surface);
        }

        private static Vector2 MoveOnStairs(Vector2 to, Rect stair, ResidenceSurface bottom, ResidenceSurface top,
            ref ResidenceSurface surface)
        {
            to.y = Mathf.Clamp(to.y, stair.yMin + Clearance, stair.yMax - Clearance);
            if (to.x < stair.xMin) surface = bottom;
            else if (to.x >= stair.xMax) surface = top;
            return to;
        }

        private static Vector2 ApproachStairs(Vector2 from, Vector2 to, Rect stair, ResidenceSurface stairs,
            bool fromAbove, ref ResidenceSurface surface)
        {
            if (!TouchesStairwell(to, stair, fromAbove)) return to;
            if ((fromAbove ? from.x < stair.xMax : from.x > stair.xMin) ||
                !BetweenRails(from.y, stair) || !BetweenRails(to.y, stair)) return from;
            if (!fromAbove || to.x < stair.xMax) surface = stairs;
            return to;
        }

        /// <summary>按楼梯轴线连续计算脚底高度。</summary>
        internal float HeightOffset(Vector2 point, ResidenceSurface surface) => surface switch
        {
            ResidenceSurface.Stairs => StairHeight(point, Stairwell),
            ResidenceSurface.Upper => StoreyHeight,
            ResidenceSurface.RoofStairs => StoreyHeight + StairHeight(point, RoofStairwell),
            ResidenceSurface.Roof => RoofHeight,
            _ => 0f
        };

        internal bool CanRestore(Vector2 point, ResidenceSurface surface) => ContainsShot(point, surface) &&
            (surface != ResidenceSurface.Stairs || BetweenRails(point.y, Stairwell)) &&
            (surface != ResidenceSurface.RoofStairs || BetweenRails(point.y, RoofStairwell));

        /// <summary>楼上无跨层目标，弹道表现仍止于本层的墙面或楼梯边界。</summary>
        internal bool ContainsShot(Vector2 point, ResidenceSurface surface) => surface switch
        {
            ResidenceSurface.Stairs => Stairwell.Contains(point),
            ResidenceSurface.RoofStairs => HasRoof && RoofStairwell.Contains(point),
            ResidenceSurface.Upper => upperWalkArea.Contains(point) && !Stairwell.Contains(point) &&
                (!HasRoof || !RoofStairwell.Contains(point)),
            ResidenceSurface.Roof => HasRoof && upperWalkArea.Contains(point) && !RoofStairwell.Contains(point),
            _ => false
        };

        private static float StairHeight(Vector2 point, Rect stair) =>
            StoreyHeight * Mathf.InverseLerp(stair.xMin, stair.xMax, point.x);

        private static bool BetweenRails(float y, Rect stair) => y >= stair.yMin + Clearance && y <= stair.yMax - Clearance;

        private static bool TouchesStairwell(Vector2 point, Rect stair, bool extendWest) =>
            point.x >= stair.xMin - (extendWest ? Clearance : 0) && point.x < stair.xMax + Clearance &&
            point.y >= stair.yMin - Clearance && point.y < stair.yMax + Clearance;
    }
}
