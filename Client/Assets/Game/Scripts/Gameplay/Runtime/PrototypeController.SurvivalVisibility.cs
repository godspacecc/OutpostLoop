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

        /// <summary>建立室内的底图节点，屋盖和迷雾各自控制画面的遮挡。</summary>
        private Transform RegisterSurvivalInterior(int room, Rect area)
        {
            var root = new GameObject("房间内部 " + room).transform;
            root.SetParent(scavengeRoot, false);
            survivalInteriors.Add((area, root));
            return root;
        }

        /// <summary>更新人物所在房间和视线，分别供镜头与迷雾使用。</summary>
        private void SyncSurvivalRoomsVisibility()
        {
            if (survivalSight == null) return;
            int room = SurvivalRoomAt(scavengeRun.Player);
            if (room >= 0 && scavengeRun.Player.x < survivalInteriors[room].Area.xMin + .28f)
                room = -1;
            visibleSurvivalRoom = room;
            SetSurvivalSightView();
            survivalSight.TraceContour();
            SyncSurvivalSightPresentation();
        }

        /// <summary>瞄准和场景表现都从角色当前的位置和朝向取视线。</summary>
        private void SetSurvivalSightView()
        {
            var state = scavengeRun.Survival;
            float daylight = Mathf.Clamp01(Mathf.Min(state.Hour - 5.5f, 19.5f - state.Hour) / 1.5f);
            survivalSight.SetView(ScavengePlayerPosition(0), state.facing, daylight, state.Light);
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
            bool walkableRoof = false)
        {
            if (parts.Length == 0) return;
            survivalShells.Add(new SurvivalShell(room, parts, roof, upperResidence, walkableRoof));
        }

        /// <summary>同一批镜头射线为人物和脚边空间避挡，墙体的透明度与视线朝向无关。</summary>
        private void UpdateSurvivalShells(OcclusionRay[] context)
        {
            float playerHeight = scavengeRun.PlayerHeightOffset;
            foreach (var shell in survivalShells)
            {
                bool inside = shell.Room >= 0 && shell.Room == visibleSurvivalRoom;
                if (inside && (shell.Roof || (shell.UpperResidence && playerHeight < 1.8f) ||
                    (shell.WalkableRoof && playerHeight < RiverValleyResidence.RoofHeight - 1.2f)))
                {
                    shell.Present(true, false, this);
                    continue;
                }
                // 封闭建筑外壳固定，先用包围盒跳过大多数远处建筑。
                if (shell.Room < 0 && !CameraContextBlocked(context, shell.Bounds))
                {
                    shell.Present(false, false, this);
                    continue;
                }
                bool blocked = false;
                foreach (var part in shell.Parts)
                {
                    if (!part.enabled || !part.gameObject.activeInHierarchy) continue;
                    foreach (var ray in context)
                        if (ray.BlockedBy(part)) { blocked = true; break; }
                    if (blocked) break;
                }
                shell.Present(false, blocked, this);
            }
        }

        private static bool CameraContextBlocked(OcclusionRay[] context, Bounds bounds)
        {
            foreach (var ray in context)
                if (ray.BlockedBy(bounds)) return true;
            return false;
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
            ClearSurvivalFog();
            survivalClinicActor = null;
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
