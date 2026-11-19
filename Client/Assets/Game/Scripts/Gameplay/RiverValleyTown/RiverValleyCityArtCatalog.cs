using System;
using UnityEngine;

namespace OutpostLoop.RiverValley
{
    /// <summary>从已确认 City 资源派生的表现目录；只保存网格材质，不包含供应者玩法代码。</summary>
    public sealed class RiverValleyCityArtCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string key;
            public Mesh mesh;
            public Material[] materials;
            public Vector3 sourceSize;
        }
        public Entry[] entries;
        /// <summary>按稳定资源键查找派生模型。</summary>
        public Entry Find(string key) => Array.Find(entries, entry => entry.key == key);
        /// <summary>加载显式资源目录，构建内容不依赖编辑器扫描。</summary>
        public static RiverValleyCityArtCatalog Load() => Game.ResourceLoader.LoadAsset<RiverValleyCityArtCatalog>(Game.GameplayArtAddress.CityCatalog);
        /// <summary>按原墙段长度拼 2.5 米模块，保持原 Renderer 和门洞空隙。</summary>
        public bool ReplaceWall(Transform target, bool alongZ)
        {
            var key = alongZ ? "wallSide" : "wall";
            var entry = Find(key);
            if (!Replace(target,key)) return false;
            int count = Mathf.Max(1,Mathf.CeilToInt((alongZ ? target.localScale.z : target.localScale.x) / 2.5f));
            if (count == 1) return true;
            var parts = new System.Collections.Generic.List<Mesh>();
            var merged = new CombineInstance[entry.mesh.subMeshCount];
            for (int sub = 0; sub < entry.mesh.subMeshCount; sub++)
            {
                var tiles = new CombineInstance[count];
                for (int i = 0; i < count; i++)
                {
                    float offset = -.5f + (i+.5f) / count;
                    tiles[i] = new CombineInstance { mesh=entry.mesh,subMeshIndex=sub,transform=Matrix4x4.TRS(alongZ?new Vector3(0,0,offset):new Vector3(offset,0,0),Quaternion.identity,alongZ?new Vector3(1,1,1f/count):new Vector3(1f/count,1,1)) };
                }
                var part = new Mesh { indexFormat=UnityEngine.Rendering.IndexFormat.UInt32 };part.CombineMeshes(tiles,true,true);parts.Add(part);
                merged[sub] = new CombineInstance {mesh=part,transform=Matrix4x4.identity};
            }
            var mesh = new Mesh {name="City室内墙段",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.CombineMeshes(merged,false,true);
            target.GetComponent<MeshFilter>().sharedMesh=mesh;target.gameObject.AddComponent<RiverValleyMeshLifetime>();
            foreach(var part in parts) UnityEngine.Object.Destroy(part);
            return true;
        }
        /// <summary>保留交互对象坐标、大小、碰撞与遮挡 Renderer，仅换归一化网格。</summary>
        public bool Replace(Transform target, string key)
        {
            var entry = Find(key);
            if (entry == null || !target.TryGetComponent<MeshFilter>(out var filter) || !target.TryGetComponent<MeshRenderer>(out var renderer)) return false;
            filter.sharedMesh = entry.mesh;
            renderer.sharedMaterials = entry.materials;
            return true;
        }
    }
}
