using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OutpostLoop.RiverValley
{
    /// <summary>按 125 米区域与共享材质合并 City 原件，避免整镇产生逐件更新的对象。</summary>
    internal sealed class RiverValleyCityBatch
    {
        private readonly Transform parent;
        private readonly RiverValleyCityArtCatalog catalog;
        private readonly Dictionary<Vector2Int, Dictionary<Material, List<CombineInstance>>> chunks = new Dictionary<Vector2Int, Dictionary<Material, List<CombineInstance>>>();
        internal RiverValleyCityBatch(Transform parent, RiverValleyCityArtCatalog catalog) { this.parent = parent; this.catalog = catalog; }
        internal void Add(string key, Vector3 center, Vector3 size, Quaternion rotation)
        {
            var entry = catalog.Find(key);
            if (entry == null) return;
            var chunkKey = new Vector2Int(Mathf.FloorToInt(center.x / 125), Mathf.FloorToInt(center.z / 125));
            if (!chunks.TryGetValue(chunkKey, out var groups)) chunks[chunkKey] = groups = new Dictionary<Material, List<CombineInstance>>();
            for (int sub = 0; sub < entry.mesh.subMeshCount; sub++)
            {
                var material = entry.materials[sub];
                if (!groups.TryGetValue(material, out var items)) groups[material] = items = new List<CombineInstance>();
                items.Add(new CombineInstance { mesh = entry.mesh, subMeshIndex = sub, transform = Matrix4x4.TRS(center, rotation, size) });
            }
        }
        internal void Finish()
        {
            foreach (var chunk in chunks)
            foreach (var group in chunk.Value)
            {
                var obj = new GameObject("City 合批 " + chunk.Key);
                obj.transform.SetParent(parent, false);
                var mesh = new Mesh { name = "City场景派生合批", indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(group.Value.ToArray(), true, true);
                obj.AddComponent<MeshFilter>().sharedMesh = mesh;
                obj.AddComponent<MeshRenderer>().sharedMaterial = group.Key;
                obj.AddComponent<RiverValleyMeshLifetime>();
            }
            chunks.Clear();
        }
    }
}
