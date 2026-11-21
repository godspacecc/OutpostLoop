using UnityEngine;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        // 首轮只在整图主街杂货店验证门窗观察。窗洞不写入通行和攻击规则。
        private const int PeekRayCount = 512;
        private const float PeekRange = 13f;
        private const float PeekWindowHalf = .72f, PeekWindowOffset = 3.4f;
        private Texture2D shopPeekMask;
        private float[] shopPeekDistances;
        private Vector2[] shopPeekDirections;
        private (Renderer Renderer, Material[] Original, Material[] Peek)[] shopPeekRoof;
        private Transform shopPeekFloor;
        private Renderer[] shopPeekProps;
        private bool isShopPeeking, shopPeekDoorOpen, shopPeekSampled;
        private Vector2 shopPeekPlayer, shopPeekFacing;

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
                    new Vector3(wall.width, top - bottom, end - start), material);
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

        /// <summary>店门外由视线裁切管理屋顶，避免镜头淡化整片屋顶。</summary>
        private bool AtShopPeekFacade()
        {
            if (shopPeekRoof == null || scavengeRun == null || visibleSurvivalRoom == 0) return false;
            var door = CurrentSurvivalRooms.Door(0);
            var player = scavengeRun.Player;
            return player.x < door.xMin + .28f && player.x > door.xMin - 9 &&
                Mathf.Abs(player.y - door.center.y) < 11;
        }

        /// <summary>屋顶只在角色可见的地面投影上开口，其余部分仍遮住室内。</summary>
        private void CreateShopPeekMask(Transform cover, Transform floor, Rect room)
        {
            var shader = UnityEngine.Resources.Load<Shader>("RiverValleyPeekRoof");
            if (!shader) return;
            shopPeekMask = new Texture2D(PeekRayCount, 1, TextureFormat.RFloat, false, true)
                { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            shopPeekDistances = new float[PeekRayCount];
            shopPeekDirections = new Vector2[PeekRayCount];
            for (int i = 0; i < PeekRayCount; i++)
            {
                float angle = ((i + .5f) / PeekRayCount - .5f) * Mathf.PI;
                shopPeekDirections[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }
            var parts = new System.Collections.Generic.List<(Renderer, Material[], Material[])>();
            foreach (var roof in cover.GetComponentsInChildren<Renderer>())
            {
                if (!roof.enabled) continue;
                var original = roof.sharedMaterials;
                var peek = System.Array.ConvertAll(original, source =>
                {
                    var material = new Material(shader);
                    if (source && source.HasProperty("_Color")) material.color = source.color;
                    if (source && source.HasProperty("_MainTex")) material.mainTexture = source.mainTexture;
                    material.SetTexture("_PeekMask", shopPeekMask);
                    material.SetVector("_PeekRoom", new Vector4(room.xMin, room.yMin, room.width, room.height));
                    material.SetFloat("_PeekFloor", floor.position.y);
                    scavengeMaterials.Add(material);
                    return material;
                });
                parts.Add((roof, original, peek));
            }
            shopPeekRoof = parts.ToArray();
            shopPeekFloor = floor;
        }

        /// <summary>只有穿过开放门洞或窗洞、且在角色前方的射线可以揭开室内。</summary>
        private bool HasShopPeek(Vector2 point)
        {
            var ray = point - shopPeekPlayer;
            float distance = ray.magnitude;
            if (distance <= 0 || distance > PeekRange ||
                !TryShopPeekEntrance(ray / distance, CurrentSurvivalRooms.Door(0), out var inside))
                return false;
            return point.x <= inside.x || scavengeRun.ClearShopPeekLine(inside, point);
        }

        /// <summary>判断朝向和门窗几何，再找出射线进入西墙内侧的位置。</summary>
        private bool TryShopPeekEntrance(Vector2 direction, Rect door, out Vector2 inside)
        {
            inside = default;
            if (direction.x <= 0 || Vector2.Dot(direction, shopPeekFacing) < .5f) return false;
            float entryDistance = (door.xMin - shopPeekPlayer.x) / direction.x;
            if (entryDistance > PeekRange) return false;
            float z = shopPeekPlayer.y + direction.y * entryDistance;
            bool throughDoor = shopPeekDoorOpen && z > door.yMin && z < door.yMax;
            float windowCenter = door.center.y - PeekWindowOffset;
            if (!throughDoor && Mathf.Abs(z - windowCenter) >= PeekWindowHalf - .07f) return false;
            var outside = shopPeekPlayer + direction * (entryDistance - .04f / direction.x);
            inside = shopPeekPlayer + direction * ((door.xMax + .04f - shopPeekPlayer.x) / direction.x);
            return scavengeRun.ClearShopPeekLine(shopPeekPlayer, outside);
        }

        /// <summary>沿同一射线找到最远可见位置，屋顶使用角度插值而非地面格子。</summary>
        private float TraceShopPeek(Vector2 direction, Rect door)
        {
            if (!TryShopPeekEntrance(direction, door, out var inside)) return 0;
            var end = shopPeekPlayer + direction * PeekRange;
            if (end.x <= inside.x) return PeekRange;
            float entry = Vector2.Dot(inside - shopPeekPlayer, direction);
            return Mathf.Lerp(entry, PeekRange, scavengeRun.ShopPeekLineFraction(inside, end));
        }

        /// <summary>室外观察时只渲染露出的室内地面及物件，跨进房间后恢复原表现。</summary>
        private void UpdateShopPeek()
        {
            if (shopPeekRoof == null || scavengeRun?.Survival == null) return;
            var player = scavengeRun.Player;
            var facing = scavengeRun.Survival.facing.normalized;
            var door = CurrentSurvivalRooms.Door(0);
            bool open = !scavengeRun.Survival.rooms[0].Blocks;
            bool canPeek = visibleSurvivalRoom != 0 && player.x < door.xMin - .05f && player.x > door.xMin - 9 &&
                Mathf.Abs(player.y - door.center.y) < 11 && facing.x > -.5f;
            if (!canPeek)
            {
                if (isShopPeeking) SetShopPeek(false);
                shopPeekSampled = false;
                return;
            }
            if (shopPeekSampled && shopPeekDoorOpen == open && (shopPeekPlayer - player).sqrMagnitude < .0016f &&
                Vector2.Dot(shopPeekFacing, facing) > .9998f) return;
            shopPeekSampled = true;
            shopPeekPlayer = player;
            shopPeekFacing = facing;
            shopPeekDoorOpen = open;
            bool visible = false;
            for (int i = 0; i < PeekRayCount; i++)
            {
                float distance = TraceShopPeek(shopPeekDirections[i], door);
                shopPeekDistances[i] = distance;
                visible |= distance > 0;
            }
            if (visible)
            {
                shopPeekMask.SetPixelData(shopPeekDistances, 0);
                shopPeekMask.Apply(false);
                foreach (var part in shopPeekRoof)
                    foreach (var material in part.Peek)
                        material.SetVector("_PeekPlayer", new Vector4(player.x, player.y, 0, 0));
            }
            SetShopPeek(visible);
        }

        private void SetShopPeek(bool active)
        {
            if (isShopPeeking && !active)
                foreach (var part in shopPeekRoof) part.Renderer.sharedMaterials = part.Original;
            isShopPeeking = active;
            var root = survivalInteriors[0].Root;
            root.gameObject.SetActive(active || visibleSurvivalRoom == 0);
            if (!active && shopPeekProps == null) return;
            shopPeekProps ??= root.GetComponentsInChildren<Renderer>(true);
            foreach (var prop in shopPeekProps)
            {
                if (!prop || prop.transform.IsChildOf(shopPeekFloor)) continue;
                var center = new Vector2(prop.bounds.center.x, prop.bounds.center.z);
                var towardPlayer = (shopPeekPlayer - center).normalized;
                prop.forceRenderingOff = active && !HasShopPeek(center + towardPlayer * .55f);
            }
        }

        /// <summary>先让镜头恢复原材质，再替换实际屋顶网格的观察裁切。</summary>
        private void PresentShopPeekRoof()
        {
            foreach (var part in shopPeekRoof)
            {
                foreach (var material in part.Peek) material.SetFloat("_PeekActive", 1);
                part.Renderer.sharedMaterials = part.Peek;
            }
        }

        /// <summary>材质由行动统一回收，动态遮罩单独释放。</summary>
        private void ClearShopPeek()
        {
            if (shopPeekMask) Destroy(shopPeekMask);
            shopPeekMask = null;
            shopPeekRoof = null;
            shopPeekDistances = null;
            shopPeekDirections = null;
            shopPeekProps = null;
            isShopPeeking = shopPeekSampled = false;
        }
    }
}
