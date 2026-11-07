using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using OutpostLoop.Resources.Editor;

namespace OutpostLoop.Editor
{
    /// <summary>独立工程日常入口；不提供旧项目的打包、发布或服务器操作。</summary>
    public static class FoundationTools
    {
        [MenuItem("Tools/配置/生成双端配置")]
        public static void GenerateConfig()
        {
            var root=Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
            var info=new System.Diagnostics.ProcessStartInfo("cmd.exe","/c gen.bat")
            {WorkingDirectory=Path.Combine(root,"Config"),UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            using(var process=System.Diagnostics.Process.Start(info))
            {
                var output=process.StandardOutput.ReadToEnd();var error=process.StandardError.ReadToEnd();process.WaitForExit();
                if(process.ExitCode!=0)throw new InvalidOperationException(output+"\n"+error);
                Debug.Log(output);
            }
            AssetDatabase.Refresh();
        }
        /// <summary>为既有原型补空UI层与唯一生命周期入口，不创建可见控件或套用旧主题。</summary>
        [MenuItem("Tools/开发/准备原型基础场景")]
        public static void Prepare()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("请先退出Play再准备基础场景。");
            var scene=EditorSceneManager.OpenScene("Assets/Prototype/Scenes/Boot.unity");
            var entry=GameObject.Find("PrototypeEntry");
            if(entry==null)throw new InvalidOperationException("缺少 PrototypeEntry。");
            if(entry.GetComponent<Game.FoundationLoop>()==null)entry.AddComponent<Game.FoundationLoop>();
            if(UnityEngine.Object.FindObjectOfType<Game.UIRoot>()==null)
            {
                var root=new GameObject("UIRoot",typeof(RectTransform));root.SetActive(false);
                var canvas=root.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;
                root.AddComponent<CanvasScaler>();root.AddComponent<GraphicRaycaster>();
                foreach(var name in new[]{"HUD","Dialog","Popup","Tips","Top"})
                {
                    var layer=new GameObject(name,typeof(RectTransform));layer.transform.SetParent(root.transform,false);
                    var rect=(RectTransform)layer.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
                }
                var input=new GameObject("EventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule));input.transform.SetParent(root.transform,false);
                root.AddComponent<Game.UIRoot>();root.SetActive(true);
            }
            var settings=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            var handling=settings.FindProperty("activeInputHandler");if(handling!=null){handling.intValue=2;settings.ApplyModifiedPropertiesWithoutUndo();}
            EditorSceneManager.SaveScene(scene);ResourceSourceAudit.EnsureSettings();AssetDatabase.SaveAssets();
            Debug.Log("FOUNDATION_READY：独立空UI层、生命周期、源收集设置已准备，未构建资源包。");
        }
        [MenuItem("Tools/诊断/基础诊断")]
        public static void Diagnostics()
        {
            ResourceSourceAudit.Audit();
            var config=OutpostLoop.Config.PrototypeConfig.Load(address => new OutpostLoop.Resources.EditorSourceResourceLoader().LoadAsset<TextAsset>(address)?.bytes);
            Debug.Log($"OutpostLoop基础：QFramework/UGUI/InputSystem/Luban/YooAsset；源配置局时={config.Duration}，敌人上限={config.MaxEnemies}。当前为编辑器源开发模式，资源包/发布未验证。");
        }
    }
}
