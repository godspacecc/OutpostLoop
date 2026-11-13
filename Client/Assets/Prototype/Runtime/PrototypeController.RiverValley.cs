using UnityEngine;
using OutpostLoop.RiverValley;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        /// <summary>以河谷镇的瓦顶、檐口和窗框补齐首批街区的临时建筑，几何仍服从原墙体占地。</summary>
        private void DressRiverValleyBuilding(Rect footprint, float height, bool residential, bool industrial,
            Material roof, Material glass, Material trim)
        {
            var parts = new System.Collections.Generic.List<Renderer>();
            if (!industrial)
            {
                float rise = Mathf.Tan(20 * Mathf.Deg2Rad) * footprint.width / 4;
                for (int side = -1; side <= 1; side += 2)
                {
                    var panel = ScavengeBox("河谷镇坡屋面", At(footprint.center + Vector2.right * (side * footprint.width / 4), height + rise + .15f),
                        new Vector3(footprint.width / (2 * Mathf.Cos(20 * Mathf.Deg2Rad)) + .35f, .16f, footprint.height + .5f), roof);
                    panel.localRotation = Quaternion.Euler(0, 0, -side * 20);
                    parts.Add(panel.GetComponent<Renderer>());
                }
            }
            for (int side = -1; side <= 1; side += 2)
            {
                int bays = Mathf.Max(2, Mathf.FloorToInt(footprint.height / 3));
                for (int i = 0; i < bays; i++)
                {
                    float z = footprint.yMin + footprint.height * (i + .5f) / bays;
                    if (Mathf.Abs(z - footprint.center.y) < 1.3f) continue;
                    var point = new Vector2(side < 0 ? footprint.xMin - .025f : footprint.xMax + .025f, z);
                    parts.Add(ScavengeBox("河谷镇窗框", At(point, 1.7f), new Vector3(.14f, 1.3f, 1.35f), trim).GetComponent<Renderer>());
                    point.x += side * .08f;
                    parts.Add(ScavengeBox("河谷镇窗面", At(point, 1.7f), new Vector3(.035f, 1.12f, 1.17f), glass).GetComponent<Renderer>());
                }
            }
            // 新增装饰随同对应建筑遮挡切除，不引入另一套碰撞。
            RegisterScavengeOccluder(parts.ToArray());
        }

        /// <summary>在河谷镇外景中嵌入已验证的生存街区；所有交互和移动继续使用同一份领域坐标。</summary>
        private void CreateRiverValleySurvivalDistrict(Material ground, Material metal, Material wood)
        {
            var content = UnityEngine.Resources.Load<RiverValleyGameplayContent>("Prototype/RiverValleyGameplayContent");
            if (content == null || content.layout == null || content.surface == null)
                throw new System.InvalidOperationException("河谷镇外景引用缺失，请检查 RiverValleyGameplayContent。");
            RiverValleyTownPreview.CreateScenery(scavengeRoot, content.layout, content.surface,
                new Rect(-110, -97, 220, 194));
            riverValleyRegion = JsonUtility.FromJson<RiverValleyRegion>(content.layout.text);
            // 接缝垫层只用于远景边缘，不能扩大实际可行动区域。
            ScavengeBox("街区外景接缝地面", new Vector3(0, -.28f, 0), new Vector3(240, .2f, 214), ground);
            var bounds = ScavengeTown.Bounds;
            // 领域边界限制角色中心，围栏外移半米，为角色半径留出空间。
            Fence("南侧街区边界", new Vector2(bounds.center.x, bounds.yMin - .5f), bounds.width + 1, false);
            Fence("北侧街区边界", new Vector2(bounds.center.x, bounds.yMax + .5f), bounds.width + 1, false);
            Fence("西侧街区边界", new Vector2(bounds.xMin - .5f, bounds.center.y), bounds.height + 1, true);
            Fence("东侧街区边界", new Vector2(bounds.xMax + .5f, bounds.center.y), bounds.height + 1, true);

            void Fence(string title, Vector2 center, float length, bool vertical)
            {
                var size = vertical ? new Vector3(.16f, .12f, length) : new Vector3(length, .12f, .16f);
                ScavengeBox(title, At(center, .72f), size, wood);
                ScavengeBox(title + "横栏", At(center, 1.12f), size, wood);
                int count = Mathf.CeilToInt(length / 6);
                for (int i = 0; i <= count; i++)
                {
                    var offset = (i / (float)count - .5f) * length;
                    var point = center + (vertical ? Vector2.up : Vector2.right) * offset;
                    ScavengeBox(title + "立柱", At(point, .68f), new Vector3(.22f, 1.36f, .22f), metal);
                }
            }
        }
    }
}
