#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>用户手动把已有正式 Player 转为安装器；不触发 Unity 构建，不自动发布。</summary>
    public static class WindowsInstallerMenu
    {
        private static bool running;
        private const string CompilerKey = "OutpostLoop.WindowsInstaller.Compiler";

        [MenuItem("Tools/打包/PC/快速打包/生成安装器", false, 150)]
        private static async void Generate()
        {
            if (running) return;
            string exe = EditorUtility.OpenFilePanel("选择正式 Player 的 OutpostLoop.exe", "", "exe");
            if (string.IsNullOrEmpty(exe)) return;
            if (Path.GetFileName(exe) != "OutpostLoop.exe") { UnityEngine.Debug.LogError("请选择 OutpostLoop.exe。"); return; }
            string compiler = EditorPrefs.GetString(CompilerKey, "");
            if (!File.Exists(compiler))
            {
                compiler = EditorUtility.OpenFilePanel("选择 Inno Setup 6 的 ISCC.exe", "", "exe");
                if (string.IsNullOrEmpty(compiler)) return;
                EditorPrefs.SetString(CompilerKey, compiler);
            }
            string parent = EditorUtility.OpenFolderPanel("选择安装器输出父目录（创建独立批次）", Path.GetDirectoryName(exe), "");
            if (string.IsNullOrEmpty(parent)) return;
            string output = Path.Combine(parent, "Installer_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            string repo = Directory.GetParent(Application.dataPath).Parent.FullName;
            string script = Path.Combine(repo, "Tools", "WindowsInstaller", "build_installer.ps1");
            running = true;
            try
            {
                // 参数以独立引号传入脚本文件；不拼接 PowerShell 命令或 cmd 命令字符串。
                var info = new ProcessStartInfo("powershell.exe") {
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -File " + Quote(script) +
                        " -PlayerDirectory " + Quote(Path.GetDirectoryName(exe)) + " -Compiler " + Quote(compiler) +
                        " -OutputDirectory " + Quote(output), UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                using (var process = Process.Start(info))
                {
                    if (process == null) throw new InvalidOperationException("无法启动安装器编译脚本。");
                    var stdout = process.StandardOutput.ReadToEndAsync();
                    var stderr = process.StandardError.ReadToEndAsync();
                    await Task.Run(() => process.WaitForExit());
                    string result = await stdout; string errors = await stderr;
                    if (process.ExitCode != 0) throw new InvalidOperationException(result + "\n" + errors);
                    UnityEngine.Debug.Log(result);
                    EditorUtility.RevealInFinder(output);
                }
            }
            catch (Exception e) { UnityEngine.Debug.LogError("安装器生成失败：" + e.Message); }
            finally { running = false; }
        }

        private static string Quote(string path)
        {
            if (path.Contains("\"") || path.EndsWith("\\")) throw new ArgumentException("路径不能含引号或以反斜线结束。");
            return "\"" + path + "\"";
        }
    }
}
#endif
