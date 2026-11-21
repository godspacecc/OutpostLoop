using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using OutpostLoop.RiverValley;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        // 室内物件按场景归组；角色所在房间只负责镜头切顶。
        private readonly List<(Rect Area, Transform Root)> survivalInteriors = new List<(Rect, Transform)>();
        private int visibleSurvivalRoom = -1;
        private RiverValleySight survivalSight;
        // 墙、屋顶与门留在室内组外，供镜头遮挡独立控制。
        private readonly List<SurvivalShell> survivalShells = new List<SurvivalShell>();
        private readonly Dictionary<Material, Material> fadedSurvivalMaterials = new Dictionary<Material, Material>();

        /// <summary>建立对应室内的表现节点。启动时在第一帧同步前保持隐藏。</summary>
        private Transform RegisterSurvivalInterior(int room, Rect area)
        {
            var root = new GameObject("房间内部 " + room).transform;
            root.SetParent(scavengeRoot, false);
            root.gameObject.SetActive(false);
            survivalInteriors.Add((area, root));
            return root;
        }

        /// <summary>更新角色视野，并把屋顶、室内物件和地面明暗绑定到同一帧结果。</summary>
        private void SyncSurvivalRoomsVisibility()
        {
            if (survivalSight == null) return;
            int room = SurvivalRoomAt(scavengeRun.Player);
            if (room >= 0 && scavengeRun.Player.x < survivalInteriors[room].Area.xMin + .28f)
                room = -1;
            visibleSurvivalRoom = room;
            float daylight = SetSurvivalSightView();
            survivalSight.TraceContour();
            SyncSurvivalSightPresentation(daylight);
        }

        /// <summary>瞄准和场景表现都从角色当前的位置和朝向取视线。</summary>
        private float SetSurvivalSightView()
        {
            var state = scavengeRun.Survival;
            float daylight = Mathf.Clamp01(Mathf.Min(state.Hour - 5.5f, 19.5f - state.Hour) / 1.5f);
            survivalSight.SetView(ScavengePlayerPosition(0), state.facing, daylight, state.Light);
            return daylight;
        }

        /// <summary>用同一占地查找会移动或随存档恢复的室内对象。</summary>
        private int SurvivalRoomAt(Vector2 point)
        {
            for (int i = 0; i < survivalInteriors.Count; i++)
                if (survivalInteriors[i].Area.Contains(point)) return i;
            return -1;
        }

        // 检查感染者头和胸口；低家具可以盖住身体下半部分。
        private bool CanSeeSurvivalInteriorAt(Vector2 point)
        {
            if (survivalSight == null) return true;
            if (!survivalSight.Reaches(point)) return false;
            var head = SurvivalPosition(point, 1.34f);
            return survivalSight.Sees(head) || survivalSight.Sees(head - Vector3.up * .29f);
        }

        private bool CanSeeSurvivalCache(int index)
        {
            if (survivalSight == null) return true;
            var point = scavengeRun.CachePoints[index];
            var surface = scavengeRun.Survival.caches[index].surface;
            var shop = survivalWorldGeometry?.Shop;
            if (index == 1 && shop != null)
                return survivalSight.Sees(SurvivalPosition(shop.CounterCrate, 1.34f));
            float offset = survivalWorldGeometry?.Residence?.HeightOffset(point, surface) ?? 0;
            return survivalSight.Sees(SurvivalPosition(point, offset + .66f));
        }

        /// <summary>登记独立墙段、屋顶、房门或一栋封闭建筑的外壳。</summary>
        private void RegisterSurvivalShell(int room, Renderer[] parts, bool roof = false, bool upperResidence = false,
            bool walkableRoof = false, bool sightCover = false)
        {
            if (parts.Length == 0) return;
            survivalShells.Add(new SurvivalShell(room, parts, roof, upperResidence, walkableRoof));
            if (roof || sightCover) RegisterSurvivalSightCover(room, parts);
        }

        /// <summary>室内沿用切除。室外只在实际挡住角色时半透明，搜点射线不能打开别的房间。</summary>
        private void UpdateSurvivalShells(OcclusionRay torso, OcclusionRay feet, OcclusionRay cacheRay, bool hasVisibleCache)
        {
            float playerHeight = scavengeRun.PlayerHeightOffset;
            foreach (var shell in survivalShells)
            {
                bool inside = shell.Room >= 0 && shell.Room == visibleSurvivalRoom;
                if (PresentSurvivalSightCover(shell, torso, feet)) continue;
                if (inside && (shell.Roof || (shell.UpperResidence && playerHeight < 1.8f) ||
                    (shell.WalkableRoof && playerHeight < RiverValleyResidence.RoofHeight - 1.2f)))
                {
                    shell.Present(true, false, this);
                    continue;
                }
                bool cacheInside = inside && hasVisibleCache;
                // 封闭建筑外壳固定，先用包围盒跳过大多数远处建筑。
                if (shell.Room < 0 && !torso.BlockedBy(shell.Bounds) && !feet.BlockedBy(shell.Bounds))
                {
                    shell.Present(false, false, this);
                    continue;
                }
                bool blocked = false;
                foreach (var part in shell.Parts)
                {
                    if (!part.enabled || !part.gameObject.activeInHierarchy) continue;
                    if (torso.BlockedBy(part) || feet.BlockedBy(part) || (cacheInside && cacheRay.BlockedBy(part)))
                    {
                        blocked = true;
                        break;
                    }
                }
                shell.Present(inside && blocked, !inside && blocked, this);
            }
        }

        /// <summary>每种源材质共享行动内的半透明副本，退出行动时统一回收。</summary>
        private Material FadedSurvivalMaterial(Material original)
        {
            if (fadedSurvivalMaterials.TryGetValue(original, out var faded)) return faded;
            faded = new Material(original);
            if (faded.HasProperty("_OcclusionAlpha"))
            {
                faded.SetFloat("_OcclusionAlpha", .26f);
            }
            else
            {
                var color = faded.color;
                color.a *= .26f;
                faded.color = color;
                faded.SetFloat("_Mode", 2);
                faded.DisableKeyword("_ALPHATEST_ON");
                faded.EnableKeyword("_ALPHABLEND_ON");
                faded.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            }
            faded.SetOverrideTag("RenderType", "Transparent");
            faded.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            faded.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            faded.SetInt("_ZWrite", 0);
            faded.renderQueue = (int)RenderQueue.Transparent;
            scavengeMaterials.Add(faded);
            fadedSurvivalMaterials.Add(original, faded);
            return faded;
        }

        /// <summary>离开行动时清除房间与材质索引，新局重新绑定。</summary>
        private void ClearSurvivalVisibility()
        {
            ClearSurvivalSightPresentation();
            survivalSight = null;
            survivalInteriors.Clear();
            survivalShells.Clear();
            fadedSurvivalMaterials.Clear();
            visibleSurvivalRoom = -1;
        }

        /// <summary>同一遮挡部件共享显隐状态，并在视线清空后恢复原材质。</summary>
        private sealed class SurvivalShell
        {
            internal readonly int Room;
            internal readonly bool Roof;
            internal readonly bool UpperResidence;
            internal readonly bool WalkableRoof;
            internal readonly Renderer[] Parts;
            internal readonly Bounds Bounds;
            private Material[][] originals, fadedMaterials;
            private bool hidden, faded;

            internal SurvivalShell(int room, Renderer[] parts, bool roof, bool upperResidence, bool walkableRoof)
            {
                Room = room;
                Roof = roof;
                UpperResidence = upperResidence;
                WalkableRoof = walkableRoof;
                Parts = parts;
                var bounds = parts[0].bounds;
                if (room < 0)
                    foreach (var part in parts) bounds.Encapsulate(part.bounds);
                Bounds = bounds;
            }

            internal void Present(bool hide, bool fade, PrototypeController controller)
            {
                if (hidden != hide)
                    foreach (var part in Parts) part.forceRenderingOff = hide;
                hidden = hide;
                if (faded == fade) return;
                faded = fade;
                if (originals == null)
                {
                    originals = new Material[Parts.Length][];
                    fadedMaterials = new Material[Parts.Length][];
                    for (int i = 0; i < Parts.Length; i++)
                    {
                        originals[i] = Parts[i].sharedMaterials;
                        fadedMaterials[i] = System.Array.ConvertAll(originals[i],
                            material => material == null ? null : controller.FadedSurvivalMaterial(material));
                    }
                }
                for (int i = 0; i < Parts.Length; i++)
                    Parts[i].sharedMaterials = fade ? fadedMaterials[i] : originals[i];
            }
        }
    }
}
