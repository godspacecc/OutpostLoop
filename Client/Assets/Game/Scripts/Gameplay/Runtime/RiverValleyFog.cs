using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using OutpostLoop.RiverValley;

namespace OutpostLoop
{
    [Serializable]
    internal sealed class RiverValleyFogPageSave
    {
        public int x, z, surface;
        public byte[] png;
    }

    /// <summary>按世界地块保存已观察范围，迷雾在场景渲染完成后调节实时画面。</summary>
    [RequireComponent(typeof(Camera))]
    public sealed class RiverValleyFog : MonoBehaviour
    {
        // 仅为走过的地块分配记忆。合成时把镜头附近的地块汇集到一个纹理。
        internal const int PageSize = 24, PagePixels = 256;
        private const int AtlasPages = 5;
        private const float ViewRange = 14f;
        private const int MaxKnownObjects = 24;
        private readonly Dictionary<(int Surface, int X, int Z), RenderTexture> pages =
            new Dictionary<(int, int, int), RenderTexture>();
        private Texture2D presence;
        private readonly Color32[] occupied = new Color32[AtlasPages * AtlasPages];
        // 记录实际绘制的已知物件尺寸，使地面遮罩不会截断它们的轮廓。
        private readonly Vector4[] knownCenters = new Vector4[MaxKnownObjects];
        private readonly Vector4[] knownExtents = new Vector4[MaxKnownObjects];
        private int knownCount;
        private Camera viewCamera;
        private Material material;
        private RenderTexture scratch, atlas;
        private Texture2D distances;
        private Vector3 observer;
        private int surface;
        private bool hasView;
        private DepthTextureMode previousDepth;

        /// <summary>建立本趟行动独有的记忆，并恢复可用的世界地块。</summary>
        internal bool Initialize(RiverValleyFogPageSave[] saved)
        {
            var shader = UnityEngine.Resources.Load<Shader>("RiverValleyFog");
            if (!shader || !shader.isSupported ||
                (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) == 0) return false;
            viewCamera = GetComponent<Camera>();
            previousDepth = viewCamera.depthTextureMode;
            viewCamera.depthTextureMode |= DepthTextureMode.Depth;
            material = new Material(shader);
            scratch = MakeTexture(PagePixels, FilterMode.Point);
            atlas = MakeTexture(AtlasPages * PagePixels, FilterMode.Bilinear);
            presence = new Texture2D(AtlasPages, AtlasPages, TextureFormat.RGBA32, false, true)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            material.SetTexture("_FogAtlas", atlas);
            material.SetTexture("_FogPresence", presence);
            material.SetFloat("_PageSize", PageSize);
            material.SetFloat("_AtlasPages", AtlasPages);
            RestorePages(saved);
            return true;
        }

        /// <summary>按楼层记录已看范围，镜头移动时根据当前世界坐标查询。</summary>
        internal void SetView(Vector3 feet, ResidenceSurface floor, Texture2D mask)
        {
            observer = feet;
            surface = (int)floor;
            distances = mask;
            hasView = true;
            knownCount = 0;
        }

        /// <summary>将记忆模型的空间包围盒交给迷雾，盒外仍按世界地块处理。</summary>
        internal void IncludeKnownObject(Vector3 center, Vector3 half, float yaw = 0)
        {
            if (knownCount == MaxKnownObjects || (center - observer).sqrMagnitude > 20 * 20) return;
            float angle = yaw * Mathf.Deg2Rad;
            knownCenters[knownCount] = new Vector4(center.x, center.y, center.z, half.y);
            knownExtents[knownCount++] = new Vector4(half.x, half.z, Mathf.Cos(angle), Mathf.Sin(angle));
        }

        private void OnRenderImage(RenderTexture source, RenderTexture target)
        {
            if (!hasView || !material || !distances)
            {
                Graphics.Blit(source, target);
                return;
            }
            BindCamera();
            StampVisiblePages();
            FillAtlas();
            Graphics.Blit(source, target, material, 1);
        }

        /// <summary>统一使用相机的实际透视投影，绘制和采样地块采用同一个坐标系。</summary>
        private void BindCamera()
        {
            var cameraTransform = viewCamera.transform;
            float halfHeight = Mathf.Tan(viewCamera.fieldOfView * .5f * Mathf.Deg2Rad);
            material.SetVector("_FogCamera", cameraTransform.position);
            material.SetVector("_FogRight", cameraTransform.right);
            material.SetVector("_FogUp", cameraTransform.up);
            material.SetVector("_FogForward", cameraTransform.forward);
            material.SetVector("_FogProjection", new Vector4(2 * halfHeight * viewCamera.aspect, 2 * halfHeight,
                0, observer.y));
            material.SetVector("_FogViewer", new Vector4(observer.x, observer.z, 0, 0));
            material.SetTexture("_Distances", distances);
            material.SetFloat("_KnownCount", knownCount);
            if (knownCount > 0)
            {
                material.SetVectorArray("_KnownCenters", knownCenters);
                material.SetVectorArray("_KnownExtents", knownExtents);
            }
        }

        /// <summary>玩家附近的地块只写入当前可见范围，旧区域不变。</summary>
        private void StampVisiblePages()
        {
            int xMin = PageAt(observer.x - ViewRange), xMax = PageAt(observer.x + ViewRange);
            int zMin = PageAt(observer.z - ViewRange), zMax = PageAt(observer.z + ViewRange);
            for (int z = zMin; z <= zMax; z++)
            for (int x = xMin; x <= xMax; x++)
            {
                var key = (surface, x, z);
                if (!pages.TryGetValue(key, out var page))
                {
                    page = MakeTexture(PagePixels, FilterMode.Bilinear);
                    Clear(page);
                    pages.Add(key, page);
                }
                material.SetVector("_FogPage", new Vector4(x * PageSize, z * PageSize, 0, 0));
                Graphics.Blit(page, scratch, material, 0);
                Graphics.CopyTexture(scratch, page);
            }
        }

        /// <summary>只汇集当前机位附近、同楼层的地块，缺少记录的地块由迷雾渲染。</summary>
        private void FillAtlas()
        {
            int xMin = PageAt(observer.x) - AtlasPages / 2;
            int zMin = PageAt(observer.z) - AtlasPages / 2;
            for (int z = 0; z < AtlasPages; z++)
            for (int x = 0; x < AtlasPages; x++)
            {
                bool present = pages.TryGetValue((surface, xMin + x, zMin + z), out var page);
                occupied[z * AtlasPages + x] = present ? new Color32(255, 0, 0, 255) : default;
                if (present) Graphics.CopyTexture(page, 0, 0, 0, 0, PagePixels, PagePixels,
                    atlas, 0, 0, x * PagePixels, z * PagePixels);
            }
            presence.SetPixels32(occupied);
            presence.Apply(false);
            material.SetVector("_FogAtlasOrigin", new Vector4(xMin * PageSize, zMin * PageSize, 0, 0));
        }

        /// <summary>保存续玩档时压缩每个已记录地块。老档缺失此字段时从未观察状态开始。</summary>
        internal RiverValleyFogPageSave[] Capture()
        {
            if (pages.Count == 0) return null;
            var saved = new List<RiverValleyFogPageSave>(pages.Count);
            var staging = new Texture2D(PagePixels, PagePixels, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            try
            {
                foreach (var entry in pages)
                {
                    RenderTexture.active = entry.Value;
                    staging.ReadPixels(new Rect(0, 0, PagePixels, PagePixels), 0, 0, false);
                    var (layer, x, z) = entry.Key;
                    saved.Add(new RiverValleyFogPageSave { x = x, z = z, surface = layer, png = staging.EncodeToPNG() });
                }
            }
            finally
            {
                RenderTexture.active = previous;
                Destroy(staging);
            }
            return saved.ToArray();
        }

        private void RestorePages(RiverValleyFogPageSave[] saved)
        {
            if (saved == null) return;
            var staging = new Texture2D(PagePixels, PagePixels, TextureFormat.RGBA32, false);
            foreach (var entry in saved)
            {
                if (entry?.png == null || entry.surface < 0 || entry.surface > (int)ResidenceSurface.Roof) continue;
                var key = (entry.surface, entry.x, entry.z);
                if (pages.ContainsKey(key) || !ImageConversion.LoadImage(staging, entry.png) ||
                    staging.width != PagePixels || staging.height != PagePixels) continue;
                var page = MakeTexture(PagePixels, FilterMode.Bilinear);
                Graphics.Blit(staging, page, material, 2);
                pages.Add(key, page);
            }
            Destroy(staging);
        }

        internal static int PageAt(float coordinate) => Mathf.FloorToInt(coordinate / PageSize);

        private static RenderTexture MakeTexture(int size, FilterMode filter)
        {
            var texture = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32)
            {
                name = "河谷镇已观察范围",
                filterMode = filter,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.Create();
            return texture;
        }

        private static void Clear(RenderTexture target)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = previous;
        }

        /// <summary>退出或重试时回收本趟行动的 GPU 记录，并归还镜头原深度设置。</summary>
        internal void ReleaseView()
        {
            hasView = false;
            if (viewCamera) viewCamera.depthTextureMode = previousDepth;
            viewCamera = null;
            foreach (var page in pages.Values) ReleaseTexture(page);
            pages.Clear();
            ReleaseTexture(scratch);
            ReleaseTexture(atlas);
            scratch = atlas = null;
            if (presence) Destroy(presence);
            if (material) Destroy(material);
            presence = null;
            material = null;
        }

        private static void ReleaseTexture(RenderTexture texture)
        {
            if (!texture) return;
            texture.Release();
            Destroy(texture);
        }

        private void OnDestroy() => ReleaseView();
    }
}
