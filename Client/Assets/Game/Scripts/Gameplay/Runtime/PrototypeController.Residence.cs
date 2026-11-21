using OutpostLoop.RiverValley;
using UnityEngine;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        /// <summary>由领域的同一楼梯洞口拼装楼板、墙面和防护栏。</summary>
        private void CreateResidenceUpperFloor(Transform groundInterior, Material wall, Material floor, Material wood, Material roof)
        {
            var residence = survivalWorldGeometry.Residence;
            var room = residence.Footprint;
            var hole = residence.Stairwell;
            const float height = RiverValleyResidence.StoreyHeight;
            void AddPart(string name, Rect area, float y, float thickness, Material material, bool rooftop = false)
            {
                var part = ScavengeBox(name, SurvivalPosition(area.center, y),
                    new Vector3(area.width, thickness, area.height), material, sightBox: true);
                RegisterSurvivalShell(RiverValleyResidence.RoomIndex, part.GetComponentsInChildren<Renderer>(),
                    upperResidence: !rooftop, walkableRoof: rooftop);
            }

            // 四块楼板围住楼梯洞口，楼板顶面与通行高度一致。
            void AddDeck(string name, Rect opening, float level, Material material, bool rooftop)
            {
                AddPart(name, Rect.MinMaxRect(room.xMin, room.yMin, opening.xMin, room.yMax), level - .10f, .2f, material, rooftop);
                AddPart(name, Rect.MinMaxRect(opening.xMax, room.yMin, room.xMax, room.yMax), level - .10f, .2f, material, rooftop);
                AddPart(name, Rect.MinMaxRect(opening.xMin, room.yMin, opening.xMax, opening.yMin), level - .10f, .2f, material, rooftop);
                AddPart(name, Rect.MinMaxRect(opening.xMin, opening.yMax, opening.xMax, room.yMax), level - .10f, .2f, material, rooftop);
            }

            void AddWalls(string name, float level, float wallHeight, bool rooftop)
            {
                const float width = RiverValleyResidence.WallWidth;
                float y = level + wallHeight * .5f;
                AddPart(name, new Rect(room.xMin, room.yMin, width, room.height), y, wallHeight, wall, rooftop);
                AddPart(name, new Rect(room.xMax - width, room.yMin, width, room.height), y, wallHeight, wall, rooftop);
                AddPart(name, new Rect(room.xMin + width, room.yMin, room.width - 2 * width, width), y, wallHeight, wall, rooftop);
                AddPart(name, new Rect(room.xMin + width, room.yMax - width, room.width - 2 * width, width), y, wallHeight, wall, rooftop);
            }

            void AddOpeningGuard(Rect opening, float level, bool rooftop)
            {
                const string name = "住宅楼梯井护栏";
                float y = level + .48f;
                AddPart(name, new Rect(opening.xMin, opening.yMin - .08f, opening.width, .08f), y, .96f, wood, rooftop);
                AddPart(name, new Rect(opening.xMin, opening.yMax, opening.width, .08f), y, .96f, wood, rooftop);
                AddPart(name, new Rect(opening.xMin - .08f, opening.yMin, .08f, opening.height), y, .96f, wood, rooftop);
            }

            AddDeck("住宅二楼楼板", hole, height, floor, false);
            AddWalls("住宅二楼外墙", height, 2.84f, false);
            AddOpeningGuard(hole, height, false);
            CreateResidenceStairs(groundInterior, hole, wood);
            if (!residence.HasRoof) return;

            var roofOpening = residence.RoofStairwell;
            AddDeck("住宅屋顶平台", roofOpening, RiverValleyResidence.RoofHeight, roof, true);
            AddWalls("住宅屋顶矮墙", RiverValleyResidence.RoofHeight, 1f, true);
            AddOpeningGuard(roofOpening, RiverValleyResidence.RoofHeight, true);
            CreateResidenceStairs(groundInterior, roofOpening, wood, height);
            CreateResidenceStairRails(groundInterior, roofOpening, wood);
        }

        /// <summary>露天楼梯两侧的扶手沿步行坡面升高。</summary>
        private void CreateResidenceStairRails(Transform parent, Rect opening, Material wood)
        {
            const float baseHeight = RiverValleyResidence.StoreyHeight;
            for (int side = 0; side < 2; side++)
            {
                float z = side == 0 ? opening.yMin : opening.yMax;
                var bottom = SurvivalPosition(new Vector2(opening.xMin, z), baseHeight + .86f);
                var top = SurvivalPosition(new Vector2(opening.xMax, z), RiverValleyResidence.RoofHeight + .86f);
                var slope = top - bottom;
                var handrail = ScavengeBox("住宅屋顶楼梯扶手", (top + bottom) * .5f,
                    new Vector3(slope.magnitude, .09f, .09f), wood, parent);
                handrail.rotation = Quaternion.FromToRotation(Vector3.right, slope);
                for (int i = 0; i <= 4; i++)
                    ScavengeBox("住宅屋顶楼梯栏杆", Vector3.Lerp(bottom, top, i / 4f) - Vector3.up * .43f,
                        new Vector3(.08f, .86f, .08f), wood, parent);
            }
        }

        /// <summary>两段通用直跑楼梯沿 X 轴铺设。踏面与连续步行坡面共享起止高度。</summary>
        private void CreateResidenceStairs(Transform parent, Rect hole, Material material, float baseHeight = 0f)
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
                    var origin = SurvivalPosition(new Vector2(hole.xMin + i * length, hole.yMin), baseHeight + (i + 1) * rise);
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
                ScavengeBox("住宅楼梯踏步", SurvivalPosition(center, baseHeight + stepHeight / 2),
                    new Vector3(tread, stepHeight, hole.height), material, parent);
            }
        }
    }
}
