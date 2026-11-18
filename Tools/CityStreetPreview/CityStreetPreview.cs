// PROTOTYPE - NOT FOR PRODUCTION
// Question: City Pack 能否在河谷镇当前镜头下形成可辨认的商住街道？
// Date: 2026-11-18
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;

/// <summary>独立 City 街段视觉样片；原网格不改，材质派生适配内置管线。</summary>
public static class CityStreetPreview
{
    static string Output => Path.GetFullPath(Path.Combine(Application.dataPath,"../../../CityStreetPreview-20261118"));
    static StringBuilder evidence = new StringBuilder();
    static string[] all;
    static System.Collections.Generic.Dictionary<Material,Material> adapted = new System.Collections.Generic.Dictionary<Material,Material>();
    static Bounds BoundsOf(GameObject obj) { var r=obj.GetComponentsInChildren<Renderer>(); var b=r[0].bounds;foreach(var x in r)b.Encapsulate(x.bounds);return b; }
    /// <summary>按原预制体创建；保存派生材质，保留原贴图与尺寸。</summary>
    static GameObject Place(string name,Vector3 at,float angle=0)
    {
        string path=all.First(p=>p.EndsWith("/"+name+".prefab",StringComparison.Ordinal));
        var obj=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));obj.name=name;
        obj.transform.rotation=Quaternion.Euler(0,angle,0);
        foreach(var t in obj.GetComponentsInChildren<Transform>(true))GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
        foreach(var r in obj.GetComponentsInChildren<Renderer>())r.sharedMaterials=r.sharedMaterials.Select(Adapt).ToArray();
        var b=BoundsOf(obj);obj.transform.position+=at-new Vector3(b.center.x,b.min.y,b.center.z);
        evidence.AppendLine(name+" | source="+path+" | size="+b.size+" | colliders="+obj.GetComponentsInChildren<Collider>().Length);
        return obj;
    }
    static Material Adapt(Material source)
    {
        if(source==null)return null;
        if(adapted.TryGetValue(source,out var cached))return cached;
        var file="Assets/CityStreetPreview/Materials/"+source.name+".mat";
        var existing=AssetDatabase.LoadAssetAtPath<Material>(file);if(existing!=null)AssetDatabase.DeleteAsset(file);
        var mat=new Material(Shader.Find("Standard"));mat.name=source.name+"_BuiltInPreview";
        // 从原资源序列化纹理槽恢复贴图，不用单色覆盖原包配色。
        var so=new SerializedObject(source);var tex=so.FindProperty("m_SavedProperties.m_TexEnvs");
        if(tex!=null)for(int i=0;i<tex.arraySize;i++) {var e=tex.GetArrayElementAtIndex(i);var key=e.FindPropertyRelative("first").stringValue;var value=e.FindPropertyRelative("second.m_Texture").objectReferenceValue as Texture;if(value!=null&&(key=="_BaseMap"||key=="_MainTex"||key=="_BaseColorMap"||key=="_Albedo_Map"))mat.mainTexture=value;}
        if(mat.mainTexture==null)mat.mainTexture=source.mainTexture;
        mat.SetFloat("_Glossiness",.16f);
        if(source.name.IndexOf("Glass",StringComparison.OrdinalIgnoreCase)>=0)mat.color=new Color(.3f,.42f,.45f);
        AssetDatabase.CreateAsset(mat,file);adapted[source]=mat;return mat;
    }
    static void Ground(string name,Vector3 p,Vector3 scale,Color color)
    {var o=GameObject.CreatePrimitive(PrimitiveType.Cube);o.name=name;o.transform.position=p;o.transform.localScale=scale;var mat=new Material(Shader.Find("Standard")){color=color};var path="Assets/CityStreetPreview/Materials/"+name+".mat";AssetDatabase.DeleteAsset(path);AssetDatabase.CreateAsset(mat,path);o.GetComponent<Renderer>().sharedMaterial=mat;}
    /// <summary>生成独立场景与三个实际渲染视角，不修改主游戏或构建 Player。</summary>
    public static void Run()
    {
        Directory.CreateDirectory(Output);Directory.CreateDirectory("Assets/CityStreetPreview/Materials");AssetDatabase.Refresh();
        all=AssetDatabase.GetAllAssetPaths();EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        Ground("临时地基",new Vector3(0,-.3f,0),new Vector3(65,.5f,45),new Color(.31f,.34f,.27f));
        Ground("临时道路底面",new Vector3(0,-.03f,-6),new Vector3(65,.12f,9),new Color(.22f,.24f,.24f));
        // 店面与住宅沿同一人行道排布，保留原件尺寸并记录实际包围范围。
        var shop=Place("SM_Bld_Shop_01",new Vector3(-6,.3f,5),180);
        var sb=BoundsOf(shop);
        Place("SM_Bld_Shop_03",new Vector3(-11,.3f,5),180);
        var home=Place("SM_Bld_Apartment_01",new Vector3(sb.max.x+3,.3f,5),180);
        for(int i=-3;i<=3;i++)Place("SM_Env_Sidewalk_Straight_01",new Vector3(i*5,0,1));
        Place("SM_Prop_Trashbin_01",new Vector3(-1,.3f,1));
        Place("SM_Prop_CardboardBox_03",new Vector3(-9,.3f,.7f),20);
        Place("SM_Veh_Car_Small_01",new Vector3(6,.05f,-6),90);
        var actor=Place("Character_Male_Jacket",new Vector3(-5,.3f,1),180);var ab=BoundsOf(actor);actor.transform.localScale*=1.8f/ab.size.y;
        var after=BoundsOf(actor);actor.transform.position+=new Vector3(-5,.3f,1)-new Vector3(after.center.x,after.min.y,after.center.z);
        // 人物只作静态尺度参照，摆放垂臂姿态，不作为动画验收。
        var animator=actor.GetComponentInChildren<Animator>();
        if(animator!=null&&animator.avatar!=null&&animator.isHuman) {
            animator.enabled=false;
            foreach(var side in new[]{new[]{HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm},new[]{HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm}}) {
                var upper=animator.GetBoneTransform(side[0]);var lower=animator.GetBoneTransform(side[1]);
                if(upper!=null&&lower!=null)upper.rotation=Quaternion.FromToRotation(lower.position-upper.position,Vector3.down)*upper.rotation;
            }
        }
        evidence.AppendLine("Scale figure: City Jacket, static arm pose, normalized 1.8m; no animation verification.");
        var sun=new GameObject("阴天主光").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.05f;sun.shadows=LightShadows.Soft;sun.transform.rotation=Quaternion.Euler(48,-35,0);
        RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.63f,.66f,.68f);
        var camera=new GameObject("Main Camera").AddComponent<Camera>();camera.tag="MainCamera";camera.fieldOfView=44;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.43f,.49f,.51f);camera.farClipPlane=200;
        var rotation=Quaternion.Euler(43,-20,0);var focus=new Vector3(-5,0,3)+rotation*Vector3.forward*1.5f;
        camera.transform.SetPositionAndRotation(focus-rotation*Vector3.forward*18.5f,rotation);Capture(camera,"game-camera.png");
        focus=new Vector3(-3,0,2);camera.transform.SetPositionAndRotation(focus-rotation*Vector3.forward*30,rotation);Capture(camera,"street-overview.png");
        camera.transform.SetPositionAndRotation(new Vector3(-6,5.5f,-9),Quaternion.identity);camera.transform.LookAt(new Vector3(-6,2,5));Capture(camera,"shop-front.png");
        // 场景保存为当前游戏镜头，便于编辑器继续检查。
        focus=new Vector3(-5,0,3)+rotation*Vector3.forward*1.5f;camera.transform.SetPositionAndRotation(focus-rotation*Vector3.forward*18.5f,rotation);
        var scenePath="Assets/CityStreetPreview/CityStreetPreview.unity";EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),scenePath);
        AssetDatabase.SaveAssets();File.WriteAllText(Path.Combine(Output,"validation.txt"),evidence.ToString()+"\nIndependent editor render only; not a gameplay or Player Build verification.");
    }
    static void Capture(Camera c,string name)
    {var rt=new RenderTexture(1600,900,24);c.targetTexture=rt;c.Render();RenderTexture.active=rt;var image=new Texture2D(1600,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1600,900),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Output,name),image.EncodeToPNG());c.targetTexture=null;RenderTexture.active=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);}
}
