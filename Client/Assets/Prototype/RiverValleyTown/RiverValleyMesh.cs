using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OutpostLoop.RiverValley
{
    /// <summary>按空间分块合并临时外景，保留视锥裁剪和统一材质。</summary>
    internal sealed class RiverValleyMesh
    {
        private const int ChunkSize = 125;
        private readonly Transform root;
        private readonly Material material;
        private readonly Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();

        private sealed class Chunk
        {
            internal readonly List<Vector3> Vertices = new List<Vector3>();
            internal readonly List<Color32> Colors = new List<Color32>();
            internal readonly List<int> Indices = new List<int>();
        }

        internal RiverValleyMesh(Transform root, Material material)
        {
            this.root = root;
            this.material = material;
        }

        internal void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 color)
        {
            var chunk = GetChunk((a + b + c + d) / 4);
            int start = chunk.Vertices.Count;
            chunk.Vertices.Add(a);
            chunk.Vertices.Add(b);
            chunk.Vertices.Add(c);
            chunk.Vertices.Add(d);
            for (int i = 0; i < 4; i++) chunk.Colors.Add(color);
            chunk.Indices.Add(start);
            chunk.Indices.Add(start + 1);
            chunk.Indices.Add(start + 2);
            chunk.Indices.Add(start);
            chunk.Indices.Add(start + 2);
            chunk.Indices.Add(start + 3);
        }

        internal void Triangle(Vector3 a, Vector3 b, Vector3 c, Color32 color)
        {
            var chunk = GetChunk((a + b + c) / 3);
            int start = chunk.Vertices.Count;
            chunk.Vertices.Add(a);
            chunk.Vertices.Add(b);
            chunk.Vertices.Add(c);
            for (int i = 0; i < 3; i++)
            {
                chunk.Colors.Add(color);
                chunk.Indices.Add(start + i);
            }
        }

        internal void Box(Vector3 center, Vector3 size, Quaternion rotation, Color32 color)
        {
            var half = size * .5f;
            Vector3 At(float x, float y, float z) => center + rotation * Vector3.Scale(half, new Vector3(x, y, z));
            var a = At(-1,-1,-1);
            var b = At(1,-1,-1);
            var c = At(1,-1,1);
            var d = At(-1,-1,1);
            var e = At(-1,1,-1);
            var f = At(1,1,-1);
            var g = At(1,1,1);
            var h = At(-1,1,1);
            Quad(a,e,f,b,color);
            Quad(b,f,g,c,color);
            Quad(c,g,h,d,color);
            Quad(d,h,e,a,color);
            Quad(e,h,g,f,color);
            Quad(a,b,c,d,color);
        }

        internal void Finish(bool collision = false, bool shadows = true)
        {
            foreach (var entry in chunks)
            {
                var data = entry.Value;
                var mesh = new Mesh { name = root.name, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(data.Vertices);
                mesh.SetColors(data.Colors);
                mesh.SetTriangles(data.Indices, 0);
                mesh.RecalculateNormals();
                var section = new GameObject($"{entry.Key.x}, {entry.Key.y}");
                section.transform.SetParent(root, false);
                section.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = section.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                if (!shadows) renderer.shadowCastingMode = ShadowCastingMode.Off;
                if (collision) section.AddComponent<MeshCollider>().sharedMesh = mesh;
                section.AddComponent<RiverValleyMeshLifetime>();
            }
            chunks.Clear();
        }

        private Chunk GetChunk(Vector3 center)
        {
            var key = new Vector2Int(Mathf.FloorToInt(center.x / ChunkSize), Mathf.FloorToInt(center.z / ChunkSize));
            if (!chunks.TryGetValue(key, out var chunk))
            {
                chunk = new Chunk();
                chunks.Add(key, chunk);
            }
            return chunk;
        }
    }
}
