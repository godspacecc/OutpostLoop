using System.Collections.Generic;
using UnityEngine;

namespace OutpostLoop.RiverValley
{
    /// <summary>仅在现有服务台/货架占地内填放物品，沿用同一个遮挡 Renderer 和原碰撞。</summary>
    internal static class RiverValleyShelfDressing
    {
        /// <summary>把原件物品并入柜体网格；水平包围范围保持在旧占地之内。</summary>
        internal static void Fill(Transform target,RiverValleyCityArtCatalog catalog,bool warehouse)
        {
            var groups=new Dictionary<Material,List<CombineInstance>>();
            void Add(Mesh mesh,Material[] materials,Matrix4x4 matrix)
            {
                for(int sub=0;sub<mesh.subMeshCount;sub++)
                {
                    var material=materials[sub];
                    if(!groups.TryGetValue(material,out var list))groups[material]=list=new List<CombineInstance>();
                    list.Add(new CombineInstance {mesh=mesh,subMeshIndex=sub,transform=matrix});
                }
            }
            Add(target.GetComponent<MeshFilter>().sharedMesh,target.GetComponent<MeshRenderer>().sharedMaterials,Matrix4x4.identity);
            var box=catalog.Find("cardboard");
            foreach(float side in new[]{-.24f,.24f})
                Add(box.mesh,box.materials,Matrix4x4.TRS(new Vector3(side,warehouse ? .02f : .55f,0),Quaternion.identity,new Vector3(.3f,.22f,.7f)));
            if(warehouse)
            {
                var barrel=catalog.Find("barrel");
                Add(barrel.mesh,barrel.materials,Matrix4x4.TRS(new Vector3(0,-.16f,0),Quaternion.identity,new Vector3(.28f,.58f,.6f)));
            }
            var parts=new List<Mesh>();var combines=new List<CombineInstance>();var mats=new List<Material>();
            foreach(var group in groups)
            {
                var mesh=new Mesh {indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.CombineMeshes(group.Value.ToArray(),true,true);
                parts.Add(mesh);combines.Add(new CombineInstance {mesh=mesh,transform=Matrix4x4.identity});mats.Add(group.Key);
            }
            var combined=new Mesh {name="City既有设施内陈设",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};combined.CombineMeshes(combines.ToArray(),false,true);
            target.GetComponent<MeshFilter>().sharedMesh=combined;target.GetComponent<MeshRenderer>().sharedMaterials=mats.ToArray();
            target.gameObject.AddComponent<RiverValleyMeshLifetime>();
            foreach(var part in parts)if(Application.isPlaying)Object.Destroy(part);else Object.DestroyImmediate(part);
        }
    }
}
