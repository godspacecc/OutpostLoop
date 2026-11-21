using UnityEngine;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        // 窗洞属于这栋建筑的布局。分出的每段实体与其他墙体共用视野。
        private const float PeekWindowHalf = .72f, PeekWindowOffset = 3.4f;

        /// <summary>在现有西墙分出窗台、过梁和真实开口。</summary>
        private bool TryCreateShopWindow(int room, Rect wall, Rect door, Material walls, Material wood)
        {
            if (room != 0 || survivalWorldGeometry == null || wall.width > .3f || wall.yMax > door.yMin + .01f)
                return false;
            float center = door.center.y - PeekWindowOffset;
            float lower = center - PeekWindowHalf, upper = center + PeekWindowHalf;
            if (lower <= wall.yMin || upper >= wall.yMax) return false;

            void Segment(float start, float end, float bottom, float top, Material material, string label)
            {
                var part = ScavengeBox(label, SurvivalPosition(new Vector2(wall.center.x, (start + end) / 2), (top + bottom) / 2),
                    new Vector3(wall.width, top - bottom, end - start), material, sightBox: true);
                if (material == walls && survivalCityArt != null)
                    survivalCityArt.ReplaceWall(part, alongZ: true);
                RegisterSurvivalShell(room, part.GetComponentsInChildren<Renderer>());
            }

            Segment(wall.yMin, lower, 0, 2.84f, walls, "窗侧外墙");
            Segment(upper, wall.yMax, 0, 2.84f, walls, "窗侧外墙");
            Segment(lower, upper, 0, .92f, walls, "窗下墙");
            Segment(lower, upper, 2.04f, 2.84f, walls, "窗上墙");
            Segment(lower, upper, .92f, 1.02f, wood, "窗台");
            Segment(lower, upper, 1.96f, 2.04f, wood, "窗上框");
            Segment(lower, lower + .07f, 1.02f, 1.96f, wood, "窗侧框");
            Segment(upper - .07f, upper, 1.02f, 1.96f, wood, "窗侧框");
            Segment(door.yMin, door.yMax, 2.04f, 2.84f, walls, "门洞过梁");
            return true;
        }

    }
}
