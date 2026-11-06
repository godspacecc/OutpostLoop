using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.IO;

namespace OutpostLoop.Editor
{
    /// <summary>仅创建独立原型场景及截图，不调用任何资源包或 Player Build。</summary>
    public static class PrototypeSetup
    {
        [MenuItem("OutpostLoop/Create Preview Scene")]
        public static void CreateScene()
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            new GameObject("PrototypeEntry").AddComponent<PrototypeController>();
            Directory.CreateDirectory("Assets/Prototype/Scenes");
            EditorSceneManager.SaveScene(scene,"Assets/Prototype/Scenes/Boot.unity");
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene("Assets/Prototype/Scenes/Boot.unity",true)};
            PlayerSettings.companyName="IndependentPrototype";PlayerSettings.productName="OutpostLoop";
            AssetDatabase.SaveAssets();
            Debug.Log("OUTPOST_SETUP_READY");
        }
        public static void Capture()
        {
            CreateScene();
            var controller=Object.FindObjectOfType<PrototypeController>();controller.Initialize();
            var camera=controller.GetComponentInChildren<Camera>();
            var rt=new RenderTexture(1280,720,24);camera.targetTexture=rt;camera.Render();
            var prior=RenderTexture.active;RenderTexture.active=rt;
            var image=new Texture2D(1280,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();
            string output=Path.GetFullPath("../Backup/Artifacts/Prototype-20261106");Directory.CreateDirectory(output);
            File.WriteAllBytes(Path.Combine(output,"scene-preview.png"),image.EncodeToPNG());
            camera.targetTexture=null;RenderTexture.active=prior;Object.DestroyImmediate(rt);Object.DestroyImmediate(image);
            // 生成的临时几何体不保存进场景，运行时重新建立。
            EditorSceneManager.OpenScene("Assets/Prototype/Scenes/Boot.unity",OpenSceneMode.Single);
            Debug.Log("OUTPOST_CAPTURE_READY");
        }
        /// <summary>临时动态预览采样：定位到第一波前，不作为整局人工通关证据。</summary>
        public static void CaptureMotion()
        {
            CreateScene();
            var controller=Object.FindObjectOfType<PrototypeController>();controller.Initialize();controller.Restart(true);
            controller.World.Run.Tick(119,0);
            var camera=controller.GetComponentInChildren<Camera>();camera.orthographicSize=22;
            string output=Path.GetFullPath("../Backup/Artifacts/Prototype-20261106/frames");Directory.CreateDirectory(output);
            var rt=new RenderTexture(640,360,24);var image=new Texture2D(640,360,TextureFormat.RGB24,false);
            var prior=RenderTexture.active;camera.targetTexture=rt;
            for(int frame=0;frame<60;frame++)
            {
                for(int step=0;step<8;step++)controller.World.Step(0.1f,Vector2.zero,false);
                controller.SyncView();camera.Render();RenderTexture.active=rt;
                image.ReadPixels(new Rect(0,0,640,360),0,0);image.Apply();
                File.WriteAllBytes(Path.Combine(output,frame.ToString("D3")+".png"),image.EncodeToPNG());
            }
            camera.targetTexture=null;RenderTexture.active=prior;Object.DestroyImmediate(rt);Object.DestroyImmediate(image);
            EditorSceneManager.OpenScene("Assets/Prototype/Scenes/Boot.unity",OpenSceneMode.Single);
            Debug.Log("OUTPOST_MOTION_READY frames=60 simulatedSeconds=48");
        }
    }
}
