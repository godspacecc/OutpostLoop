using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using OutpostLoop.RiverValley;

/// <summary>在独立项目烘焙 City 网格与派生材质；导出内容不依赖原包 Shader Graph 或脚本。</summary>
public static class CityTownArtBuilder
{
    const string Root="Assets/CityTownArtExport";
    static readonly Dictionary<Material,Material> Materials=new Dictionary<Material,Material>();
    static readonly Dictionary<string,string> Names=new Dictionary<string,string> {
        {"roof","SM_Bld_Apartment_Roof_01"},{"shop","SM_Bld_Shop_01"},{"shopAlt","SM_Bld_Shop_03"},{"residential","SM_Bld_Apartment_01"},
        {"wall","SM_Bld_Base_Wall_01"},{"window","SM_Bld_Base_Wall_Window_01"},{"floor","SM_Bld_Base_Floor_01"},{"ceiling","SM_Bld_Base_Ceiling_01"},{"door","SM_Bld_Base_Door_01"},
        {"crate","SM_Gen_Prop_Crate_01"},{"shelf","SM_Prop_ShopInterior_Shelf_01"},{"desk","SM_Prop_ShopInterior_Desk_01"},
        {"road","SM_Env_Road_01"},{"sidewalk","SM_Env_Sidewalk_Straight_01"},{"bridgeRail","SM_Env_Bridge_Wall_01"},{"bridgePillar","SM_Env_Bridge_Pillar_01"},
        {"car","SM_Veh_Car_Sedan_01"},{"van","SM_Veh_Car_Van_01"},{"ambulance","SM_Veh_Car_Ambo_01"},
        {"pine","SM_Gen_Env_Tree_Pine_01"},{"tree","SM_Gen_Env_Tree_01"},{"treeAlt","SM_Gen_Env_Tree_03"},{"bush","SM_Gen_Env_Bush_04"},
        {"bin","SM_Prop_Trashbin_01"},{"lampBase","SM_Prop_LightPole_Base_01"},{"lampArm","SM_Prop_LightPole_Arm_01"},{"lampHead","SM_Prop_LightPole_Lights_01"},
        {"hospitalSign","SM_Prop_Sign_Hospital_01"},{"fence","SM_Env_Fence_01"},{"aircon","SM_Prop_Aircon_01"},{"roofAircon","SM_Prop_Roof_Aircon_01"},{"roofVent","SM_Prop_Vents_Exhaust_01"},{"shopSign","SM_Prop_Sign_DeliPizza_01"},{"coffeeSign","SM_Prop_LargeSign_Coffee_01"},{"warningSign","SM_Prop_Sign_Warning_01"},{"parkingLines","SM_Env_Road_ParkingLines_01"},{"entranceSign","SM_Prop_Sign_Entrance_01"},{"leaves","SM_Gen_Env_Leaves_Pile_01"},{"cardboard","SM_Gen_Prop_Cardboard_Box_Preset_01"},{"barrel","SM_Gen_Prop_Barrel_Metal_01"}};
    public static void Run()
    {
        foreach(var part in new[]{"Meshes","Materials","Textures","Resources/Prototype"})Directory.CreateDirectory(Root+"/"+part);
        AssetDatabase.Refresh();var catalog=ScriptableObject.CreateInstance<RiverValleyCityArtCatalog>();var entries=new List<RiverValleyCityArtCatalog.Entry>();var report=new System.Text.StringBuilder();
        var paths=AssetDatabase.GetAllAssetPaths();
        foreach(var pair in Names) {
            var path=paths.First(p=>p.EndsWith("/"+pair.Value+".prefab"));
            var instance=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));instance.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            var filters=instance.GetComponentsInChildren<MeshFilter>();var renderers=instance.GetComponentsInChildren<MeshRenderer>();
            var bounds=renderers.First().bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
            var scale=new Vector3(1/Mathf.Max(.001f,bounds.size.x),1/Mathf.Max(.001f,bounds.size.y),1/Mathf.Max(.001f,bounds.size.z));
            var normalize=Matrix4x4.Scale(scale)*Matrix4x4.Translate(-bounds.center);
            var groups=new Dictionary<Material,List<CombineInstance>>();
            foreach(var filter in filters) {
                var renderer=filter.GetComponent<MeshRenderer>();if(renderer==null)continue;
                for(int sub=0;sub<filter.sharedMesh.subMeshCount;sub++) {
                    var source=renderer.sharedMaterials[Mathf.Min(sub,renderer.sharedMaterials.Length-1)];var mat=Adapt(source);
                    if(!groups.TryGetValue(mat,out var list))groups[mat]=list=new List<CombineInstance>();
                    list.Add(new CombineInstance {mesh=filter.sharedMesh,subMeshIndex=sub,transform=normalize*filter.transform.localToWorldMatrix});
                }
            }
            var parts=new List<Mesh>();var combines=new List<CombineInstance>();var mats=new List<Material>();
            foreach(var group in groups) {var piece=new Mesh {indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};piece.CombineMeshes(group.Value.ToArray(),true,true);parts.Add(piece);combines.Add(new CombineInstance {mesh=piece,transform=Matrix4x4.identity});mats.Add(group.Key);}
            var mesh=new Mesh {name=pair.Key,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.CombineMeshes(combines.ToArray(),false,true);mesh.RecalculateBounds();
            var meshPath=Root+"/Meshes/"+pair.Key+".asset";AssetDatabase.CreateAsset(mesh,meshPath);foreach(var piece in parts)UnityEngine.Object.DestroyImmediate(piece);
            entries.Add(new RiverValleyCityArtCatalog.Entry {key=pair.Key,mesh=mesh,materials=mats.ToArray(),sourceSize=bounds.size});
            report.AppendLine(pair.Key+" | "+path+" | "+bounds.size+" | vertices="+mesh.vertexCount+" | materials="+mats.Count);UnityEngine.Object.DestroyImmediate(instance);
        }
        // 把侧向门和侧墙烘焙到单位立方体坐标，运行时仍使用旧墙段的 Transform。
        var wall=entries.First(e=>e.key=="wall");var wallSide=UnityEngine.Object.Instantiate(wall.mesh);Rotate(wallSide);AssetDatabase.CreateAsset(wallSide,Root+"/Meshes/wallSide.asset");entries.Add(new RiverValleyCityArtCatalog.Entry {key="wallSide",mesh=wallSide,materials=wall.materials,sourceSize=new Vector3(wall.sourceSize.z,wall.sourceSize.y,wall.sourceSize.x)});
        Rotate(entries.First(e=>e.key=="door").mesh);
        catalog.entries=entries.ToArray();AssetDatabase.CreateAsset(catalog,Root+"/Resources/Prototype/RiverValleyCityArt.asset");AssetDatabase.SaveAssets();
        File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../../"+(Environment.GetEnvironmentVariable("OUTPOST_ART_BATCH")??"CityTownReplacement-20261118")+"/baked-assets.txt")),report.ToString());
    }
    static void Rotate(Mesh mesh)
    {
        var q=Quaternion.Euler(0,90,0);mesh.vertices=mesh.vertices.Select(v=>q*v).ToArray();mesh.normals=mesh.normals.Select(v=>q*v).ToArray();
        mesh.tangents=mesh.tangents.Select(v=>{var t=q*new Vector3(v.x,v.y,v.z);return new Vector4(t.x,t.y,t.z,v.w);}).ToArray();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
    }
    static Material Adapt(Material source)
    {
        if(Materials.TryGetValue(source,out var existing))return existing;
        var mat=new Material(Shader.Find("Standard"));mat.name=source.name+"_CityTown";mat.enableInstancing=true;mat.SetFloat("_Glossiness",.15f);
        var saved=new SerializedObject(source);var slots=saved.FindProperty("m_SavedProperties.m_TexEnvs");
        if(slots!=null)for(int i=0;i<slots.arraySize;i++) {
            var slot=slots.GetArrayElementAtIndex(i);var name=slot.FindPropertyRelative("first").stringValue;
            if(name!="_Albedo_Map"&&name!="_BaseMap"&&name!="_MainTex")continue;
            var texture=slot.FindPropertyRelative("second.m_Texture").objectReferenceValue as Texture;if(texture==null)continue;
            var path=AssetDatabase.GetAssetPath(texture);var dest=Root+"/Textures/"+AssetDatabase.AssetPathToGUID(path).Substring(0,8)+"_"+Path.GetFileName(path);
            if(!File.Exists(dest))AssetDatabase.CopyAsset(path,dest);mat.mainTexture=AssetDatabase.LoadAssetAtPath<Texture>(dest);
        }
        if(source.GetTag("RenderType",false)=="TransparentCutout") {mat.SetFloat("_Mode",1);mat.SetFloat("_Cutoff",.45f);mat.EnableKeyword("_ALPHATEST_ON");mat.renderQueue=2450;}
        if(source.name.IndexOf("Glass",StringComparison.OrdinalIgnoreCase)>=0)mat.color=new Color(.30f,.42f,.45f);
        var outPath=Root+"/Materials/"+AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source)).Substring(0,8)+"_"+source.name+".mat";
        AssetDatabase.CreateAsset(mat,outPath);Materials[source]=mat;return mat;
    }
}
