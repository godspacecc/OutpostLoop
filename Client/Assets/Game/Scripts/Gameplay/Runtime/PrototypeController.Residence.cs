using OutpostLoop.RiverValley;
using UnityEngine;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        /// <summary>由领域的同一楼梯洞口拼装楼板、墙面和防护栏。</summary>
        private void CreateResidenceUpperFloor(Transform groundInterior, Material wall, Material floor, Material wood)
        {
            var residence = survivalWorldGeometry.Residence;
            var room = residence.Footprint;
            var hole = residence.Stairwell;
            const float height = RiverValleyResidence.StoreyHeight;
            void UpperPart(string name, Rect area, float y, float thickness, Material material)
            {
                var part = ScavengeBox(name, SurvivalPosition(area.center, y),
                    new Vector3(area.width, thickness, area.height), material);
                RegisterSurvivalShell(RiverValleyResidence.RoomIndex, part.GetComponentsInChildren<Renderer>(), upperResidence: true);
            }

            // 四块楼板围住贯通两层的楼梯井。
            UpperPart("住宅二楼楼板", Rect.MinMaxRect(room.xMin, room.yMin, hole.xMin, room.yMax), height - .10f, .2f, floor);
            UpperPart("住宅二楼楼板", Rect.MinMaxRect(hole.xMax, room.yMin, room.xMax, room.yMax), height - .10f, .2f, floor);
            UpperPart("住宅二楼楼板", Rect.MinMaxRect(hole.xMin, room.yMin, hole.xMax, hole.yMin), height - .10f, .2f, floor);
            UpperPart("住宅二楼楼板", Rect.MinMaxRect(hole.xMin, hole.yMax, hole.xMax, room.yMax), height - .10f, .2f, floor);

            const float wallWidth = .28f;
            UpperPart("住宅二楼外墙", new Rect(room.xMin, room.yMin, wallWidth, room.height), height + 1.42f, 2.84f, wall);
            UpperPart("住宅二楼外墙", new Rect(room.xMax - wallWidth, room.yMin, wallWidth, room.height), height + 1.42f, 2.84f, wall);
            UpperPart("住宅二楼外墙", new Rect(room.xMin, room.yMin, room.width, wallWidth), height + 1.42f, 2.84f, wall);
            UpperPart("住宅二楼外墙", new Rect(room.xMin, room.yMax - wallWidth, room.width, wallWidth), height + 1.42f, 2.84f, wall);

            UpperPart("住宅楼梯井护栏", new Rect(hole.xMin, hole.yMin - .08f, hole.width, .08f), height + .48f, .96f, wood);
            UpperPart("住宅楼梯井护栏", new Rect(hole.xMin, hole.yMax, hole.width, .08f), height + .48f, .96f, wood);
            UpperPart("住宅楼梯井护栏", new Rect(hole.xMin - .08f, hole.yMin, .08f, hole.height), height + .48f, .96f, wood);
            CreateResidenceStairs(groundInterior, hole, wood);
        }

        /// <summary>两段通用直跑楼梯沿 X 轴铺设。踏面与连续步行坡面共享起止高度。</summary>
        private void CreateResidenceStairs(Transform parent, Rect hole, Material material)
        {
            var source = UnityEngine.Resources.Load<GameObject>("RiverValleyResidence/SM_Bld_Base_Stairs_01");
            if (source != null)
            {
                const int flights = 2;
                float length = hole.width / flights;
                float rise = RiverValleyResidence.StoreyHeight / flights;
                for (int i = 0; i < flights; i++)
                {
                    var flight = new GameObject("Polygon Generic 住宅楼梯").transform;
                    flight.SetParent(parent, false);
                    var model = Instantiate(source, flight, false);
                    model.transform.localRotation = Quaternion.Euler(0, -90, 0);
                    var renderers = model.GetComponentsInChildren<Renderer>(true);
                    var bounds = renderers[0].bounds;
                    foreach (var renderer in renderers)
                    {
                        bounds.Encapsulate(renderer.bounds);
                        var slots = renderer.sharedMaterials;
                        for (int slot = 0; slot < slots.Length; slot++) slots[slot] = material;
                        renderer.sharedMaterials = slots;
                    }
                    var min = flight.InverseTransformPoint(bounds.min);
                    var top = flight.InverseTransformPoint(bounds.max);
                    // 原件顶踏面高 1.5 米，模型底部另有 0.3266 米的底板。
                    flight.localScale = new Vector3(length / bounds.size.x, rise / bounds.size.y * (1.8266f / 1.5f),
                        hole.height / bounds.size.z);
                    // 将归一化前的包围盒按缩放重新投到目标起点。
                    var origin = SurvivalPosition(new Vector2(hole.xMin + i * length, hole.yMin), (i + 1) * rise);
                    flight.position += origin - new Vector3(flight.TransformPoint(min).x, flight.TransformPoint(top).y, flight.TransformPoint(min).z);
                }
                return;
            }

            // 干净检出缺少选件时，用与领域尺寸相同的预览台阶保持可玩。
            const int steps = 16;
            float tread = hole.width / steps;
            for (int i = 0; i < steps; i++)
            {
                float stepHeight = (i + 1f) / steps * RiverValleyResidence.StoreyHeight;
                var center = new Vector2(hole.xMin + (i + .5f) * tread, hole.center.y);
                ScavengeBox("住宅楼梯踏步", SurvivalPosition(center, stepHeight / 2),
                    new Vector3(tread, stepHeight, hole.height), material, parent);
            }
        }
    }
}
