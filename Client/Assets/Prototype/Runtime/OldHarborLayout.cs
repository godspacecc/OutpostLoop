using System;
using UnityEngine;

namespace OutpostLoop
{
    /// <summary>旧港守城地图的固定占地，场景和寻路共用同一组格子。</summary>
    internal static class OldHarborLayout
    {
        internal static readonly RectInt Basin = new RectInt(7, -7, 8, 14);
        internal static readonly RectInt Levee = new RectInt(-8, 5, 13, 2);
        internal static readonly System.Collections.Generic.IReadOnlyList<RectInt> Obstacles =
            Array.AsReadOnly(new[] { Basin, Levee });

        /// <summary>水面和堤身都禁止落位，单位沿两侧干地绕行。</summary>
        internal static bool Blocks(RectInt footprint) => Basin.Overlaps(footprint) || Levee.Overlaps(footprint);
    }
}
