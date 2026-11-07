#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game.Editor
{
    /// <summary>仅 CI 作用域内生效的显式工具配置；普通菜单沿用交互配置。</summary>
    public static class CiToolConfiguration
    {
        public static CiBuildRequest Current { get; internal set; }

        internal static void ConfigurePowerShellProcess(ProcessStartInfo process, string executable)
        {
            string modules = Path.Combine(Path.GetDirectoryName(executable), "Modules");
            if (!Directory.Exists(modules)) throw new InvalidOperationException("显式 PowerShell 安装缺少 Modules 目录。");
            process.EnvironmentVariables["PSModulePath"] = modules;
        }

        internal static void ValidateEmbeddedAndroid(CiBuildRequest request, string contentsPath)
        {
            string root = Path.Combine(contentsPath, "PlaybackEngines", "AndroidPlayer");
            foreach (var pair in new[] { (request.AndroidSdk, "SDK"), (request.AndroidNdk, "NDK"), (request.JavaHome, "OpenJDK"), (request.AndroidGradle, "Tools/gradle") })
                if (!string.Equals(Path.GetFullPath(pair.Item1).TrimEnd('\\', '/'), Path.GetFullPath(Path.Combine(root, pair.Item2)).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("第一版 CI 仅支持当前 Unity 配套的嵌入式 SDK、NDK、OpenJDK、Gradle，配置与安装目录不符。");
        }
    }

    [Serializable]
    internal sealed class CiPreservedFile
    {
        public string Path;
        public bool Existed;
        public string Base64;
    }
    [Serializable]
    internal sealed class CiConfigurationJournal
    {
        public int SchemaVersion = 1;
        public string ProjectId = BuildDataStore.ProjectId;
        public string RequestId;
        public CiPreservedFile[] Files;
    }

    /// <summary>记录恢复日志后才注入临时配置；正常退出恢复原文件和进程环境，不回滚外部版本预留。</summary>
    public sealed class CiConfigurationScope : IDisposable
    {
        private readonly CiBuildRequest previousTools;
        private readonly string previousDataRoot;
        private readonly string journalPath;
        private readonly CiPreservedFile[] files;
        private readonly Action<string> synchronize;
        private bool disposed;

        public CiConfigurationScope(CiBuildRequest request, Action<string> synchronize)
        {
            if (!WorkspaceBuildLock.IsHeld(request.ProjectPath)) throw new InvalidOperationException("注入 CI 配置前必须持有工作目录锁。");
            this.synchronize = synchronize ?? throw new ArgumentNullException(nameof(synchronize));
            previousTools = CiToolConfiguration.Current;
            previousDataRoot = Environment.GetEnvironmentVariable("OUTPOSTLOOP_BUILD_DATA_ROOT");
            journalPath = Path.Combine(request.ProjectPath, ".outpostloop-ci-active.json");
            BuildDataStore.NoLinks(journalPath);
            if (File.Exists(journalPath)) throw new InvalidOperationException("存在未完成的 CI 配置恢复日志，须先检查并恢复工作区。");
            string[] relative = {
                "Assets/StreamingAssets/hostConfig.json", "Assets/StreamingAssets/windows-release.json",
                "Assets/Editor/Build/windows-release-version.json", "ProjectSettings/ProjectSettings.asset"
            };
            files = relative.SelectMany(x => new[] { x, x + ".meta" }).Select(x =>
            {
                string path = BuildDataStore.Child(request.ProjectPath, x);
                BuildDataStore.NoLinks(path);
                bool exists = File.Exists(path);
                return new CiPreservedFile { Path = path, Existed = exists,
                    Base64 = exists ? Convert.ToBase64String(File.ReadAllBytes(path)) : null };
            }).ToArray();
            string hostConfig = BuildDataStore.Child(request.ProjectPath, relative[0]);
            var json = JObject.Parse(File.ReadAllText(hostConfig));
            json["resourceServer"] = request.ResourceServerUrl;
            BuildDataStore.Write(journalPath, JsonConvert.SerializeObject(new CiConfigurationJournal { RequestId = request.RequestId, Files = files }, Formatting.Indented));
            try
            {
                CiToolConfiguration.Current = request;
                Environment.SetEnvironmentVariable("OUTPOSTLOOP_BUILD_DATA_ROOT", request.PersistentDataRoot);
                BuildDataStore.Write(hostConfig, json.ToString(Formatting.Indented));
                synchronize(hostConfig);
            }
            catch (Exception original)
            {
                try { Dispose(); }
                catch (Exception cleanup) { throw new AggregateException("CI 配置注入和恢复均失败。", original, cleanup); }
                throw;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            // 失败时保留日志，下一次 CI 不会自动忽略这个工作区。
            Exception error = null;
            try
            {
                foreach (var file in files)
                {
                    try
                    {
                        BuildDataStore.NoLinks(file.Path);
                        if (file.Existed)
                        {
                            string pending = file.Path + "." + Guid.NewGuid().ToString("N") + ".pending";
                            try
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(file.Path));
                                File.WriteAllBytes(pending, Convert.FromBase64String(file.Base64));
                                if (File.Exists(file.Path)) File.Replace(pending, file.Path, null);
                                else File.Move(pending, file.Path);
                            }
                            finally { if (File.Exists(pending)) File.Delete(pending); }
                        }
                        else if (File.Exists(file.Path)) File.Delete(file.Path);
                    }
                    catch (Exception e) { error = error == null ? e : new AggregateException(error, e); }
                }
                try { synchronize(null); }
                catch (Exception e) { error = error == null ? e : new AggregateException(error, e); }
                if (error == null) File.Delete(journalPath);
            }
            finally
            {
                CiToolConfiguration.Current = previousTools;
                Environment.SetEnvironmentVariable("OUTPOSTLOOP_BUILD_DATA_ROOT", previousDataRoot);
                disposed = true;
            }
            if (error != null) throw new InvalidOperationException("临时配置恢复失败，保留恢复日志。", error);
        }
    }
}
#endif
