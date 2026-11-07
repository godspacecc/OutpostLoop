#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Game.Editor
{
    /// <summary>Unity -executeMethod Game.Editor.CiBuildEntry.Run 的批处理入口。</summary>
    public static class CiBuildEntry
    {
        public static void Run()
        {
            int exitCode = 1;
            try
            {
                var services = new CiBuildServices
                {
                    Preflight = Preflight, Configure = Configure,
                    PcFirst = BuildUtility.BuildLatestCodeAndResourcesOnly,
                    PcHost = BuildUtility.BuildHostCodeAndResourcesFromBaselineOnly,
                    AndroidFirst = AndroidBuildUtility.BuildSmallHostBaseCodeAndResourcesOnly,
                    AndroidHost = AndroidBuildUtility.BuildHostCodeAndResourcesFromBaselineOnly,
                    Publish = (artifacts, bundle) => artifacts.Publish(bundle)
                };
                var report = CiBuildRunner.Run(Environment.GetCommandLineArgs(), Directory.GetParent(Application.dataPath).FullName, services);
                exitCode = report.ExitCode;
                if (exitCode != 0) Debug.LogError("[CI] " + report.Stage + "\n" + report.Error + "\n" + report.CleanupError);
                else Debug.Log("[CI] " + report.Status + "，RequestId=" + report.RequestId);
            }
            catch (Exception e) { Debug.LogException(e); }
            // 交互误调用不关闭开发者编辑器；批处理始终返回明确进程退出码。
            if (Application.isBatchMode) EditorApplication.Exit(exitCode);
        }

        private static CiEnvironmentInfo Preflight(CiBuildRequest request)
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("CI 入口只允许在 batchmode 执行。");
            if (!string.Equals(Path.GetFullPath(EditorApplication.applicationPath), Path.GetFullPath(request.UnityEditor), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("当前 Unity 进程不是配置指定的可执行文件。");
            string versionFile = File.ReadAllText(Path.Combine(request.ProjectPath, "ProjectSettings", "ProjectVersion.txt"));
            var version = Regex.Match(versionFile, @"(?m)^m_EditorVersion:\s*(\S+)");
            if (!version.Success || version.Groups[1].Value != Application.unityVersion)
                throw new InvalidOperationException("Unity 版本与项目要求不一致。");
            BuildArtifacts.RequireReady(request.Target());
            var group = request.Target() == BuildTarget.Android ? BuildTargetGroup.Android : BuildTargetGroup.Standalone;
            if (!BuildPipeline.IsBuildTargetSupported(group, request.Target()) ||
                PlayerSettings.GetScriptingBackend(group) != ScriptingImplementation.IL2CPP)
                throw new InvalidOperationException("目标平台模块或 IL2CPP 配置不符合构建要求。");
            if (request.Target() == BuildTarget.Android)
            {
                CiToolConfiguration.ValidateEmbeddedAndroid(request, EditorApplication.applicationContentsPath);
                ValidateEffectiveAndroidTools(request);
                AndroidBuildUtility.ValidateAndroidEnvironment();
            }
            ValidatePowerShell(request.PowerShell);
            string commit = HostBaselineUtility.ReadCommit(request.GitExecutable);
            if (!request.AllowDirtySource && !string.IsNullOrEmpty(HostBaselineUtility.ReadGit("status --porcelain --untracked-files=all", request.GitExecutable)))
                throw new InvalidOperationException("CI 工作区有未提交改动，请先准备固定版本的独立检出目录。");
            if (request.BuildType == "HostUpdate")
            {
                HostBaselineArchive.Validate(HostBaselineArchive.BaselineDirectory(request.PersistentDataRoot,
                    request.Target().ToString(), request.BaselineId), request.Target().ToString(), request.BaselineId);
                if (request.Publish)
                {
                    string release = Path.Combine(Directory.GetParent(request.ProjectPath).FullName, "Release", "ResourceServer");
                    if (request.Platform == "Android") release = Path.Combine(release, "Android");
                    if (!string.Equals(Path.GetDirectoryName(request.PublishBundle), release, StringComparison.OrdinalIgnoreCase) ||
                        !Regex.IsMatch(Path.GetFileName(request.PublishBundle), @"\AResourceServerBundle_\d{8}_\d{6}\z") ||
                        File.ReadAllText(Path.Combine(request.PublishBundle, "host-baseline.id")).Trim() != request.BaselineId)
                        throw new InvalidOperationException("发布目标不属于本平台或所选宿主。");
                }
            }
            return new CiEnvironmentInfo { Commit = commit, UnityVersion = Application.unityVersion };
        }

        private static void ValidateEffectiveAndroidTools(CiBuildRequest request)
        {
            // 通过当前安装的 Android 模块查询实际生效路径；不修改构建账号的全局 EditorPrefs。
            string assemblyPath = Path.Combine(EditorApplication.applicationContentsPath,
                "PlaybackEngines", "AndroidPlayer", "UnityEditor.Android.Extensions.dll");
            var type = Assembly.LoadFrom(assemblyPath).GetType("UnityEditor.Android.AndroidExternalToolsSettings", true);
            foreach (var pair in new[] { ("sdkRootPath", request.AndroidSdk), ("ndkRootPath", request.AndroidNdk),
                ("jdkRootPath", request.JavaHome), ("gradlePath", request.AndroidGradle) })
            {
                var property = type.GetProperty(pair.Item1, BindingFlags.Public | BindingFlags.Static);
                string actual = property?.GetValue(null) as string;
                if (string.IsNullOrWhiteSpace(actual) || !string.Equals(Path.GetFullPath(actual).TrimEnd('\\', '/'),
                    Path.GetFullPath(pair.Item2).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Unity 实际 Android 工具路径与显式配置不一致：" + pair.Item1 +
                        "。请为构建账号启用此 Unity 配套工具后重试。");
            }
        }

        private static void ValidatePowerShell(string executable)
        {
            const string arguments = "-NoProfile -NonInteractive -Command \"if ($PSVersionTable.PSVersion -lt [Version]'5.1') {exit 2}; try {Get-Command Get-FileHash,ConvertTo-Json -ErrorAction Stop | Out-Null} catch {exit 3}; exit 0\"";
            var info = new ProcessStartInfo(executable, arguments)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            CiToolConfiguration.ConfigurePowerShellProcess(info, executable);
            using var process = Process.Start(info);
            if (process == null) throw new InvalidOperationException("无法启动显式 PowerShell 工具检查。");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30000)) { process.Kill(); throw new InvalidOperationException("PowerShell 工具检查超时。"); }
            string error = stderr.GetAwaiter().GetResult();
            stdout.GetAwaiter().GetResult();
            if (process.ExitCode != 0) throw new InvalidOperationException("PowerShell 必须为 5.1 或更新版本并提供 Get-FileHash、ConvertTo-Json：" + error);
        }

        private sealed class NativeScope : IDisposable
        {
            private IDisposable scope;
            private readonly string version;
            private readonly int code;
            public NativeScope(IDisposable scope, string version, int code) { this.scope = scope; this.version = version; this.code = code; }
            public void Dispose()
            {
                if (scope == null) return;
                Exception error = null;
                try { PlayerSettings.bundleVersion = version; PlayerSettings.Android.bundleVersionCode = code; AssetDatabase.SaveAssets(); }
                catch (Exception e) { error = e; }
                try { scope.Dispose(); }
                catch (Exception e) { error = error == null ? e : new AggregateException(error, e); }
                scope = null;
                if (error != null) throw new InvalidOperationException("Unity 配置恢复失败。", error);
            }
        }

        private static IDisposable Configure(CiBuildRequest request)
        {
            string version = PlayerSettings.bundleVersion;
            int code = PlayerSettings.Android.bundleVersionCode;
            var scope = new CiConfigurationScope(request, path =>
            {
                if (path == null) AssetDatabase.Refresh();
                else AssetDatabase.ImportAsset("Assets/StreamingAssets/hostConfig.json", ImportAssetOptions.ForceSynchronousImport);
            });
            return new NativeScope(scope, version, code);
        }
    }
}
#endif
