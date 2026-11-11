using System.Collections.Generic;
using UnityEngine;

namespace OutpostLoop
{
    /// <summary>大镇与镇郊的固定地形。搜点、交互和模型共用坐标。</summary>
    internal static class ScavengeTown
    {
        internal static readonly Rect Bounds = new Rect(-84, -72, 168, 144);
        internal static readonly Vector2 Start = new Vector2(-63, -52);
        internal static readonly Vector2 Exit = new Vector2(-70, -40);
        internal static readonly Vector2 Survivor = new Vector2(-30, -33);
        internal const int Manifest = 7, MedicineA = 13, MedicineB = 18;
        internal const int PatrolCount = 12;
        internal static bool IsFastPatrol(int index) => index == 3 || index == 7;

        internal static readonly Rect[] Buildings =
        {
            new Rect(-55, -52, 10, 8), new Rect(-54, -39, 12, 10), new Rect(-52, -25, 13, 9),
            new Rect(-37, -51, 9, 10), new Rect(-25, -51, 10, 10), new Rect(-17, -34, 12, 10),
            new Rect(-36, -28, 12, 11), new Rect(-16, -7, 12, 10), new Rect(-39, 1, 18, 11),
            new Rect(-14, 9, 13, 9), new Rect(11, -41, 16, 11), new Rect(14, -22, 16, 10),
            new Rect(38, -42, 14, 12), new Rect(52, -22, 15, 12), new Rect(17, 32, 13, 8),
            new Rect(53, 26, 10, 12), new Rect(54, 47, 14, 10), new Rect(-56, 25, 15, 10),
            new Rect(-27, 43, 10, 8), new Rect(-11, 47, 15, 9)
        };

        internal static readonly Rect[] WarehouseWalls =
        {
            new Rect(19, -4, 4, 1), new Rect(28, -4, 18, 1), new Rect(19, -3, 1, 23),
            new Rect(45, -3, 1, 23), new Rect(19, 19, 17, 1), new Rect(41, 19, 5, 1),
            new Rect(30, 2, 2, 11), new Rect(37, 7, 2, 8)
        };

        internal static readonly Rect[] Walls = CombineWalls();
        internal static readonly Rect[] ShelterGrounds =
        {
            new Rect(-76, -60, 10, 12), new Rect(-39, -12, 16, 10), new Rect(5, -65, 23, 15),
            new Rect(53, 2, 21, 18), new Rect(36, 42, 14, 18)
        };

        internal static readonly Vector2[] Caches =
        {
            new Vector2(-58,-48), new Vector2(-57,-34), new Vector2(-56,-18), new Vector2(-33,-38),
            new Vector2(-21,-38), new Vector2(-20,-29), new Vector2(-33,-15), new Vector2(-12,-9),
            new Vector2(-30,15), new Vector2(-8,21), new Vector2(10,-34), new Vector2(32,-17),
            new Vector2(25,3), new Vector2(43,15), new Vector2(36,3), new Vector2(50,-25),
            new Vector2(8,34), new Vector2(51,31), new Vector2(50,50), new Vector2(-58,29),
            new Vector2(-30,46), new Vector2(-9,44)
        };

        internal static readonly string[] Names =
        {
            "镇口补给箱", "主街药房", "主街杂货店", "住宅后巷工具柜", "后院木料堆", "住宅储藏柜",
            "镇中心诊所", "转运办公室", "镇中心安保岗", "街口检查站", "修车铺零件箱", "仓区外安保柜",
            "仓库装卸架", "北门冷藏柜", "仓库检修台", "运输车队工具箱", "公路维修站", "农场工具棚",
            "农场备用药柜", "林带巡查站", "镇郊木料棚", "农机库"
        };

        internal static readonly Vector2[] Corners = MakeCorners();

        private static Rect[] CombineWalls()
        {
            var result = new List<Rect>(Buildings);
            result.AddRange(WarehouseWalls);
            return result.ToArray();
        }

        private static Vector2[] MakeCorners()
        {
            var result = new List<Vector2>();
            foreach (var wall in Walls)
            {
                var left = wall.xMin - .47f;
                var right = wall.xMax + .47f;
                var bottom = wall.yMin - .47f;
                var top = wall.yMax + .47f;
                result.Add(new Vector2(left, bottom));
                result.Add(new Vector2(left, top));
                result.Add(new Vector2(right, bottom));
                result.Add(new Vector2(right, top));
            }
            return result.ToArray();
        }

        /// <summary>种子重排物资位置，同时保证每趟都有基本补给和两种武器。</summary>
        internal static void FillLoot(ScavengeItem[] loot, System.Random random, out int medicineCache)
        {
            void Place(ScavengeItem item, int first, int second) => loot[random.Next(2) == 0 ? first : second] |= item;
            loot[0] = ScavengeItem.Materials;
            loot[Manifest] = ScavengeItem.Signal;
            Place(ScavengeItem.Supplies, 1, 2);
            Place(ScavengeItem.Supplies, 6, 17);
            Place(ScavengeItem.Crowbar, 3, 5);
            Place(ScavengeItem.Materials, 4, 20);
            Place(ScavengeItem.Pistol, 8, 11);
            Place(ScavengeItem.Ammo, 9, 11);
            Place(ScavengeItem.Ammo, 14, 21);
            Place(ScavengeItem.DefenseParts, 10, 12);
            Place(ScavengeItem.RepairMaterials, 15, 16);
            loot[19] = ScavengeItem.Supplies;
            medicineCache = random.Next(2) == 0 ? MedicineA : MedicineB;
            loot[medicineCache] |= ScavengeItem.ReserveMedicine;
        }

        internal static ScavengePatrol[] CreatePatrols(System.Random random)
        {
            var routes = new (Vector2 from, Vector2 to, bool fast)[]
            {
                (new Vector2(-48,-42),new Vector2(-39,-42),false),
                (new Vector2(-38,-22),new Vector2(-38,-9),false),
                (new Vector2(-22,-37),new Vector2(-8,-37),false),
                (new Vector2(-17,-14),new Vector2(-7,-14),true),
                (new Vector2(-22,21),new Vector2(-8,21),false),
                (new Vector2(6,-26),new Vector2(6,-10),false),
                (new Vector2(24,0),new Vector2(24,16),false),
                (new Vector2(42,1),new Vector2(42,16),true),
                (new Vector2(35,-27),new Vector2(49,-27),false),
                (new Vector2(29,27),new Vector2(46,27),false),
                (new Vector2(47,39),new Vector2(47,55),false),
                (new Vector2(-48,41),new Vector2(-31,41),false)
            };
            var patrols = new ScavengePatrol[routes.Length];
            for (int i = 0; i < patrols.Length; i++)
            {
                var route = routes[i];
                bool reverse = random.Next(2) == 0;
                var start = reverse ? route.to : route.from;
                var end = reverse ? route.from : route.to;
                patrols[i] = new ScavengePatrol(start, end, route.fast)
                    { Position = Vector2.Lerp(start, end, (float)random.NextDouble() * .8f) };
            }
            return patrols;
        }
    }
}
