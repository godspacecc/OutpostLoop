using System.Collections.Generic;
using UnityEngine;

namespace OutpostLoop
{
    /// <summary>每趟行动自己的房间几何；旧存档与整图行动不共享可变全局布局。</summary>
    internal sealed class SurvivalRoomSet
    {
        // 空值表示旧地图；整图房间由本趟几何提供。
        private readonly Rect[] areas;
        internal SurvivalRoomSet(Rect[] areas = null) { this.areas = areas; }
        /// <summary>取得当前版本房间占地。</summary>
        internal Rect Footprint(int i) => areas == null ? SurvivalRooms.Footprint(i) : areas[i];
        /// <summary>门洞固定在西侧，模型与碰撞共用该矩形。</summary>
        internal Rect Door(int i) { var r = Footprint(i); return new Rect(r.xMin, r.center.y - .8f, .28f, 1.6f); }
        /// <summary>床位留在室内远端，避开入口通道。</summary>
        internal Vector2 Bed(int i) { var r = Footprint(i); return new Vector2(r.xMax - 1.25f, r.yMax - 1.5f); }
        /// <summary>储物柜使用同一房间的南侧位置。</summary>
        internal Vector2 Cabinet(int i) { var r = Footprint(i); return new Vector2(r.xMax - 1.2f, r.yMin + 1.2f); }
        /// <summary>西墙分段留出真实门洞。</summary>
        internal Rect[] Walls(int i)
        {
            var r = Footprint(i); var door = Door(i);
            return new[] { new Rect(r.xMin,r.yMin,.28f,door.yMin-r.yMin), new Rect(r.xMin,door.yMax,.28f,r.yMax-door.yMax),
                new Rect(r.xMax-.28f,r.yMin,.28f,r.height), new Rect(r.xMin,r.yMin,r.width,.28f), new Rect(r.xMin,r.yMax-.28f,r.width,.28f) };
        }
        /// <summary>关闭的门加入碰撞；旧地图仍按旧建筑和仓库布局恢复。</summary>
        internal Rect[] Collision(SurvivalRoom[] state, bool legacy)
        {
            if (legacy) return SurvivalRooms.Collision(state);
            var walls = new List<Rect>();
            for (int i = 0; i < 2; i++) { walls.AddRange(Walls(i)); if (state[i].Blocks) walls.Add(Door(i)); }
            return walls.ToArray();
        }
    }
}
