using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        // 一张环形距离图供屋顶和镜头叠层共享；室内记录只缓存渲染器。
        private Texture2D survivalSightMask;
        private Transform survivalSightOverlay;
        private Material survivalSightOverlayMaterial;
        private readonly Dictionary<Renderer[], SightCover> survivalSightCovers = new Dictionary<Renderer[], SightCover>();
        private readonly Dictionary<Transform, Renderer[]> survivalSightProps = new Dictionary<Transform, Renderer[]>();
        private readonly HashSet<Renderer> survivalSightHiddenProps = new HashSet<Renderer>();

        private Texture2D SurvivalSightMask()
        {
            if (survivalSightMask) return survivalSightMask;
            survivalSightMask = new Texture2D(RiverValleySight.RayCount, 1, TextureFormat.RFloat, false, true)
            {
                name = "角色视野距离",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };
            return survivalSightMask;
        }

        /// <summary>用当前建筑的真实覆盖物裁切；所有建筑共用同一视野纹理。</summary>
        private void RegisterSurvivalSightCover(int room, Renderer[] renderers)
        {
            var shader = UnityEngine.Resources.Load<Shader>("RiverValleyPeekRoof");
            if (!shader) return;
            var area = survivalInteriors[room].Area;
            var parts = new List<(Renderer, Material[], Material[])>();
            foreach (var renderer in renderers)
            {
                if (!renderer.enabled) continue;
                var original = renderer.sharedMaterials;
                var materials = System.Array.ConvertAll(original, source =>
                {
                    var material = new Material(shader);
                    if (source && source.HasProperty("_Color")) material.color = source.color;
                    if (source && source.HasProperty("_MainTex"))
                    {
                        material.mainTexture = source.mainTexture;
                        material.mainTextureScale = source.mainTextureScale;
                        material.mainTextureOffset = source.mainTextureOffset;
                    }
                    material.SetTexture("_PeekMask", SurvivalSightMask());
                    material.SetVector("_PeekRoom", new Vector4(area.xMin, area.yMin, area.width, area.height));
                    material.SetFloat("_PeekFloor", SurvivalPosition(area.center, .035f).y);
                    scavengeMaterials.Add(material);
                    return material;
                });
                parts.Add((renderer, original, materials));
            }
            if (parts.Count > 0) survivalSightCovers.Add(renderers, new SightCover(parts));
        }

        private bool PresentSurvivalSightCover(SurvivalShell shell, OcclusionRay torso, OcclusionRay feet)
        {
            if (!survivalSightCovers.TryGetValue(shell.Parts, out var cover)) return false;
            bool outside = shell.Room != visibleSurvivalRoom;
            bool open = outside && survivalInteriors[shell.Room].Root.gameObject.activeSelf;
            bool cutsPlayer = outside && cover.Blocks(torso, feet);
            if (outside) shell.Present(false, false, this);
            var eye = survivalSight.Eye;
            cover.Present(open, cutsPlayer, new Vector4(eye.x, eye.z, survivalSight.FeetHeight, 0));
            return outside;
        }

        /// <summary>切换各房间的可见开口，并让家具和人物各自采用实际表面采样。</summary>
        private void SyncSurvivalSightPresentation(float daylight)
        {
            var mask = SurvivalSightMask();
            mask.SetPixelData(survivalSight.Distances, 0);
            mask.Apply(false);
            survivalSightHiddenProps.Clear();
            for (int i = 0; i < survivalInteriors.Count; i++)
            {
                var (area, root) = survivalInteriors[i];
                bool inside = i == visibleSurvivalRoom;
                bool reveal = inside || survivalSight.Enters(new Rect(area.position + Vector2.one * .29f,
                    area.size - Vector2.one * .58f));
                root.gameObject.SetActive(reveal);
                if (!reveal) continue;
                if (!survivalSightProps.TryGetValue(root, out var props))
                {
                    props = root.GetComponentsInChildren<Renderer>(true);
                    survivalSightProps.Add(root, props);
                }
                foreach (var prop in props)
                {
                    if (!prop || !prop.enabled || prop.bounds.size.y < .14f) continue;
                    // 走进房间后保留家具轮廓；人物仍按视线显隐。
                    bool hidden = (!inside || prop is SkinnedMeshRenderer) && !SeesSurvivalProp(prop);
                    prop.forceRenderingOff = hidden;
                    if (hidden) survivalSightHiddenProps.Add(prop);
                }
            }
            if (!survivalSightOverlay) CreateSurvivalSightOverlay();
            if (!survivalSightOverlayMaterial) return;
            var eye = survivalSight.Eye;
            survivalSightOverlayMaterial.SetVector("_SightViewer", new Vector4(eye.x, eye.z, survivalSight.FeetHeight,
                Mathf.Lerp(.22f, .12f, daylight)));
        }

        private bool SeesSurvivalProp(Renderer renderer)
        {
            var bounds = renderer.bounds;
            var center = bounds.center;
            var toward = survivalSight.Eye - center;
            toward.y = 0;
            toward.Normalize();
            var edge = new Vector3(toward.x * bounds.extents.x, 0, toward.z * bounds.extents.z) * .84f;
            var front = center + edge;
            var top = Vector3.up * bounds.extents.y * .74f;
            return survivalSight.Sees(front, renderer.transform) ||
                survivalSight.Sees(front + top, renderer.transform) ||
                survivalSight.Sees(front - top, renderer.transform);
        }

        /// <summary>在镜头近面轻微压暗视野外的地面投影，HUD 仍由原界面渲染。</summary>
        private void CreateSurvivalSightOverlay()
        {
            var shader = UnityEngine.Resources.Load<Shader>("RiverValleySightOverlay");
            if (!shader) return;
            var overlay = GameObject.CreatePrimitive(PrimitiveType.Quad);
            overlay.name = "角色视野明暗";
            var collider = overlay.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            survivalSightOverlay = overlay.transform;
            survivalSightOverlay.SetParent(gameCamera.transform, false);
            var renderer = overlay.GetComponent<Renderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            survivalSightOverlayMaterial = new Material(shader);
            survivalSightOverlayMaterial.SetTexture("_PeekMask", SurvivalSightMask());
            scavengeMaterials.Add(survivalSightOverlayMaterial);
            renderer.sharedMaterial = survivalSightOverlayMaterial;
        }

        private void PresentSurvivalSightOverlay()
        {
            if (!survivalSightOverlay) return;
            float depth = gameCamera.nearClipPlane + .05f;
            float height = 2 * depth * Mathf.Tan(gameCamera.fieldOfView * .5f * Mathf.Deg2Rad);
            survivalSightOverlay.localPosition = Vector3.forward * depth;
            survivalSightOverlay.localScale = new Vector3(height * gameCamera.aspect, height, 1) * 1.02f;
        }

        private void ClearSurvivalSightPresentation()
        {
            if (survivalSightMask) Destroy(survivalSightMask);
            if (survivalSightOverlay) Destroy(survivalSightOverlay.gameObject);
            survivalSightMask = null;
            survivalSightOverlay = null;
            survivalSightOverlayMaterial = null;
            survivalSightCovers.Clear();
            survivalSightProps.Clear();
            survivalSightHiddenProps.Clear();
        }

        // 只有开口显隐改变时切换屋顶材质，视野坐标每帧更新。
        private sealed class SightCover
        {
            private readonly List<(Renderer Renderer, Material[] Original, Material[] Cut)> parts;
            private bool active;

            internal SightCover(List<(Renderer, Material[], Material[])> parts) => this.parts = parts;

            internal bool Blocks(OcclusionRay torso, OcclusionRay feet)
            {
                foreach (var part in parts)
                    if (torso.BlockedBy(part.Renderer) || feet.BlockedBy(part.Renderer)) return true;
                return false;
            }

            internal void Present(bool open, bool cutsPlayer, Vector4 viewer)
            {
                bool apply = open || cutsPlayer;
                foreach (var part in parts)
                {
                    if (apply != active) part.Renderer.sharedMaterials = apply ? part.Cut : part.Original;
                    if (!apply) continue;
                    foreach (var material in part.Cut)
                    {
                        material.SetVector("_PeekPlayer", viewer);
                        material.SetFloat("_PeekOpen", open ? 1 : 0);
                        material.SetFloat("_PeekKeepPlayerClear", cutsPlayer ? 1 : 0);
                    }
                }
                active = apply;
            }
        }
    }
}
