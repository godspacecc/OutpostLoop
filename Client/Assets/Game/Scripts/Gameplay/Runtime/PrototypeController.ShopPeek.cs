using UnityEngine;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        // 首轮只在整图主街杂货店验证门窗观察。窗洞不写入通行和攻击规则。
        private const int PeekWidth = 80, PeekHeight = 64;
        private const float PeekWindowHalf = .72f, PeekWindowOffset = 3.4f;
        private Texture2D shopPeekMask;
        private Color32[] shopPeekPixels;
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
            return true;
        }

        /// <summary>屋顶只在角色可见的地面投影上开口，其余部分仍遮住室内。</summary>
        private void CreateShopPeekMask(Transform cover, Transform floor, Rect room)
        {
            var shader = UnityEngine.Resources.Load<Shader>("RiverValleyPeekRoof");
            if (!shader) return;
            shopPeekMask = new Texture2D(PeekWidth, PeekHeight, TextureFormat.RGBA32, false, true)
                { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            shopPeekPixels = new Color32[PeekWidth * PeekHeight];
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
            if (ray.x <= 0 || distance > 13 || Vector2.Dot(ray, shopPeekFacing) < distance * .5f) return false;
            var door = CurrentSurvivalRooms.Door(0);
            float z = shopPeekPlayer.y + ray.y * ((door.xMin - shopPeekPlayer.x) / ray.x);
            bool throughDoor = shopPeekDoorOpen && z > door.yMin && z < door.yMax;
            float windowCenter = door.center.y - PeekWindowOffset;
            if (!throughDoor && Mathf.Abs(z - windowCenter) >= PeekWindowHalf - .07f) return false;
            var outside = shopPeekPlayer + ray * ((door.xMin - .04f - shopPeekPlayer.x) / ray.x);
            var inside = shopPeekPlayer + ray * ((door.xMax + .04f - shopPeekPlayer.x) / ray.x);
            return scavengeRun.ClearShopPeekLine(shopPeekPlayer, outside) &&
                (point.x <= inside.x || scavengeRun.ClearShopPeekLine(inside, point));
        }

        /// <summary>室外观察时只渲染露出的室内地面及物件，跨进房间后恢复原表现。</summary>
        private void UpdateShopPeek()
        {
            if (shopPeekRoof == null || scavengeRun?.Survival == null) return;
            var room = survivalInteriors[0];
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
            if (shopPeekSampled && shopPeekDoorOpen == open && (shopPeekPlayer - player).sqrMagnitude < .012f &&
                Vector2.Dot(shopPeekFacing, facing) > .999f) return;
            shopPeekSampled = true;
            shopPeekPlayer = player;
            shopPeekFacing = facing;
            shopPeekDoorOpen = open;
            bool visible = false;
            float end = Mathf.Min(room.Area.xMax, player.x + 13);
            int columns = Mathf.Clamp(Mathf.CeilToInt((end - room.Area.xMin) / room.Area.width * PeekWidth), 0, PeekWidth);
            System.Array.Clear(shopPeekPixels, 0, shopPeekPixels.Length);
            for (int y = 0; y < PeekHeight; y++)
            for (int x = 0; x < columns; x++)
            {
                var point = new Vector2(room.Area.xMin + (x + .5f) / PeekWidth * room.Area.width,
                    room.Area.yMin + (y + .5f) / PeekHeight * room.Area.height);
                if (!HasShopPeek(point)) continue;
                shopPeekPixels[y * PeekWidth + x] = Color.white;
                visible = true;
            }
            if (visible)
            {
                shopPeekMask.SetPixels32(shopPeekPixels);
                shopPeekMask.Apply(false);
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
            shopPeekPixels = null;
            shopPeekProps = null;
            isShopPeeking = shopPeekSampled = false;
        }
    }
}
