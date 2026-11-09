using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace OutpostLoop.Editor
{
    /// <summary>从权威配置源同步生成客户端和服务端配置。</summary>
    public static class FoundationTools
    {
        /// <summary>调用配置目录的统一生成脚本，并刷新 Unity 中的生成产物。</summary>
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
    }
}
