using System;
using UnityEngine;

namespace OutpostLoop.RiverValley
{
    /// <summary>只负责杂货店陈设表现；不创建另一套碰撞或交互脚本。</summary>
    internal static class RiverValleyShopVisuals
    {
        internal static GameObject Create(Transform parent,RiverValleyShopInterior plan,RiverValleyCityArtCatalog catalog,
            Func<Vector2,float> ground,Action<Renderer[]> occluder,Material[] zones,
            Action<RiverValleyShopInterior.Fixture, Transform> registerSight = null)
        {
            var root=new GameObject("主街杂货店完整室内");root.transform.SetParent(parent,false);
            // 未安装自有资源的检出仍显示同占地占位物，避免不可见实体或开局空引用。
            if(catalog==null)
            {
                foreach(var fixture in plan.Fixtures)
                {
                    if(fixture.Type==RiverValleyShopInterior.Kind.ExistingBed || fixture.Type==RiverValleyShopInterior.Kind.ExistingCabinet)continue;
                    var box=GameObject.CreatePrimitive(PrimitiveType.Cube);box.name="商店占位 "+fixture.Type;
                    box.transform.SetParent(root.transform,false);
                    box.transform.localPosition=new Vector3(fixture.Area.center.x,ground(fixture.Area.center)+fixture.Height/2,fixture.Area.center.y);
                    box.transform.localScale=new Vector3(fixture.Area.width,fixture.Height,fixture.Area.height);
                    var collider=box.GetComponent<Collider>();
                    if(Application.isPlaying)UnityEngine.Object.Destroy(collider);else UnityEngine.Object.DestroyImmediate(collider);
                    var renderer=box.GetComponent<Renderer>();renderer.sharedMaterial=zones[1];occluder?.Invoke(new Renderer[]{renderer});
                    registerSight?.Invoke(fixture, box.transform);
                }
                return root;
            }
            for(int i=0;i<plan.FloorZones.Length;i++)
            {
                var area=plan.FloorZones[i];var tile=new GameObject("商店分区地面 "+i);tile.transform.SetParent(root.transform,false);
                tile.transform.localPosition=new Vector3(area.center.x,ground(area.center)+.052f,area.center.y);tile.transform.localScale=new Vector3(area.width,1,area.height);
                tile.AddComponent<MeshFilter>().sharedMesh=catalog.Find("floor").mesh;tile.AddComponent<MeshRenderer>().sharedMaterial=zones[i];
            }
            int index=0;
            foreach(var fixture in plan.Fixtures)
            {
                if(fixture.Type==RiverValleyShopInterior.Kind.ExistingBed || fixture.Type==RiverValleyShopInterior.Kind.ExistingCabinet)continue;
                var part=new GameObject("商店"+fixture.Type+" "+index++);part.transform.SetParent(root.transform,false);
                var batch=new RiverValleyCityBatch(part.transform,catalog);var r=fixture.Area;float floor=ground(r.center);
                if(fixture.Type==RiverValleyShopInterior.Kind.Partition)
                {
                    int count=Mathf.CeilToInt(r.height/2.5f);
                    for(int tile=0;tile<count;tile++)batch.Add("wall",new Vector3(r.center.x,floor+fixture.Height/2,r.yMin+r.height*(tile+.5f)/count),new Vector3(r.height/count,fixture.Height,r.width),Quaternion.Euler(0,90,0));
                }
                else if(fixture.Type==RiverValleyShopInterior.Kind.Counter)
                {
                    for(int tile=0;tile<3;tile++)batch.Add("desk",new Vector3(r.xMin+r.width*(tile+.5f)/3,floor+fixture.Height/2,r.center.y),new Vector3(r.width/3,fixture.Height,r.height),Quaternion.Euler(0,180,0));
                }
                else
                {
                    int count=Mathf.Max(2,Mathf.RoundToInt(r.height/1.55f));
                    foreach(int side in new[]{-1,1})
                    for(int tile=0;tile<count;tile++)
                        batch.Add("shelf",new Vector3(r.center.x+side*r.width/4,floor+fixture.Height/2,r.yMin+r.height*(tile+.5f)/count),new Vector3(r.height/count,fixture.Height,r.width/2),Quaternion.Euler(0,side*90,0));
                }
                batch.Finish();occluder?.Invoke(part.GetComponentsInChildren<Renderer>());
                registerSight?.Invoke(fixture, part.transform);
            }
            return root;
        }
    }
}
