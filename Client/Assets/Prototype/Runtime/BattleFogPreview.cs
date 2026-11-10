using UnityEngine;

namespace OutpostLoop
{
    /// <summary>战场地面与小地图复用同一张临时迷雾贴图。</summary>
    internal sealed class BattleFogPreview
    {
        private static readonly Color32[] Palette =
        {
            new Color32(5, 8, 10, 255), new Color32(22, 26, 27, 170), new Color32(0, 0, 0, 0)
        };
        private readonly Color32[] pixels;
        private int revision = -1;
        internal Texture2D Texture { get; }
        internal Mesh Mesh { get; }
        internal Material Material { get; }

        internal BattleFogPreview(Transform parent, BattleVisibility vision)
        {
            var view = new GameObject("BattleFog", typeof(MeshFilter), typeof(MeshRenderer));
            view.transform.SetParent(parent, false);
            Texture = new Texture2D(vision.Size, vision.Size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            pixels = new Color32[vision.Size * vision.Size];
            float span = vision.Span;
            Mesh = new Mesh
            {
                vertices = new[] { new Vector3(-span, .025f, -span), new Vector3(span, .025f, -span),
                    new Vector3(span, .025f, span), new Vector3(-span, .025f, span) },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
                triangles = new[] { 0, 2, 1, 0, 3, 2 }
            };
            Material = new Material(UnityEngine.Resources.Load<Shader>("BattleFogPreview")) { mainTexture = Texture };
            view.GetComponent<MeshFilter>().sharedMesh = Mesh;
            var renderer = view.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = Material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        internal void Sync(BattleVisibility vision)
        {
            if (revision == vision.Revision) return;
            revision = vision.Revision;
            for (int y = 0; y < vision.Size; y++)
            for (int x = 0; x < vision.Size; x++)
                pixels[y * vision.Size + x] = Palette[(int)vision.At(x, y)];
            Texture.SetPixels32(pixels);
            Texture.Apply(false);
        }
    }
}
