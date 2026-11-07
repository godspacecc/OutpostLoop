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
    /// <summary>独立工程配置与诊断入口；打包链路由通用构建工具负责。</summary>
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
            BootMainSceneSetup.Prepare();
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
