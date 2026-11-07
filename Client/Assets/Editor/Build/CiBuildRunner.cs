#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;

namespace Game.Editor
{
    [Serializable]
    public sealed class CiArtifactPath
    {
        public string Role;
        public string Path;
        public bool Exists;
    }
    [Serializable]
    public sealed class CiArtifactFile
    {
        public string Role;
        public string Path;
        public long Length;
        public string Sha256;
    }
    [Serializable]
    public sealed class CiBuildReport
    {
        public int SchemaVersion = 1;
        public string ProjectId = BuildDataStore.ProjectId;
        public string RequestId;
        public string Status = "Running";
        public string Stage = "ParseArguments";
        public int ExitCode = 1;
        public string StartedUtc = DateTime.UtcNow.ToString("O");
        public string CompletedUtc;
        public string ConfigPath;
        public string ConfigSha256;
        public string Commit;
        public string ExpectedCommit;
        public string UnityVersion;
        public string Platform;
        public string BuildType;
        public string BuildStatus = "NotStarted";
        public string PublishStatus = "NotRequested";
        public string CleanupStatus = "NotNeeded";
        public string Error;
        public string CleanupError;
        public string InventoryError;
        public string BaselineId;
        public string AppVersion;
        public int AppVersionCode;
        public string ResourceVersion;
        public string PublishedBundle;
        public string UnityEditor;
        public string GitExecutable;
        public string PowerShell;
        public string InnoCompiler;
        public string AndroidSdk;
        public string AndroidNdk;
        public string JavaHome;
        public string AndroidGradle;
        public string PersistentDataRoot;
        public CiArtifactPath[] Artifacts = Array.Empty<CiArtifactPath>();
        public CiArtifactFile[] Files = Array.Empty<CiArtifactFile>();
    }

    public sealed class CiEnvironmentInfo { public string Commit; public string UnityVersion; }
    /// <summary>明确的四条构建路由；独立检查注入夹具委托，产品入口注入实际只构建方法。</summary>
    public sealed class CiBuildServices
    {
        public Func<CiBuildRequest, CiEnvironmentInfo> Preflight;
        public Func<CiBuildRequest, IDisposable> Configure;
        public Func<BuildArtifacts> PcFirst;
        public Func<string, BuildArtifacts> PcHost;
        public Func<BuildArtifacts> AndroidFirst;
        public Func<string, BuildArtifacts> AndroidHost;
        public Func<BuildArtifacts, string, ResourceServerPublishResult> Publish;

        internal BuildArtifacts Build(CiBuildRequest request)
        {
            if (request.Platform == "PC")
                return request.BuildType == "FirstPackage"
                    ? (PcFirst ?? throw new InvalidOperationException("缺少 PC 首包路由。"))()
                    : (PcHost ?? throw new InvalidOperationException("缺少 PC Host 路由。"))(request.BaselineId);
            return request.BuildType == "FirstPackage"
                ? (AndroidFirst ?? throw new InvalidOperationException("缺少 Android 首包路由。"))()
                : (AndroidHost ?? throw new InvalidOperationException("缺少 Android Host 路由。"))(request.BaselineId);
        }
    }

    /// <summary>当前同步构建的观察点；普通菜单没有观察者，不写 CI 报告。</summary>
    internal static class CiBuildProgress
    {
        internal static Action<string> StageChanged;
        internal static Action<string, string> ArtifactObserved;
        internal static void Stage(string name) => StageChanged?.Invoke(name);
        internal static void Artifact(string role, string path) => ArtifactObserved?.Invoke(role, path);
    }

    public static class CiBuildRunner
    {
        internal static (string config, string result) Arguments(string[] args)
        {
            string config = null, result = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith("-ci", StringComparison.OrdinalIgnoreCase)) continue;
                string key = args[i];
                if (key != "-ciConfig" && key != "-ciResult") throw new ArgumentException("未知 CI 参数：" + key);
                if (++i >= args.Length || args[i].StartsWith("-", StringComparison.Ordinal)) throw new ArgumentException("CI 参数缺少值。");
                if (key == "-ciConfig") { if (config != null) throw new ArgumentException("重复 ciConfig 参数。"); config = args[i]; }
                else { if (result != null) throw new ArgumentException("重复 ciResult 参数。"); result = args[i]; }
            }
            if (config == null || result == null) throw new ArgumentException("必须指定 -ciConfig 和 -ciResult。");
            return (CiBuildRequest.Absolute(config, "ciConfig"), CiBuildRequest.Absolute(result, "ciResult"));
        }

        private static void ResultPath(string path, string clientRoot)
        {
            if (Path.GetExtension(path) != ".json" || BuildDataStore.IsWithin(path, clientRoot) ||
                BuildDataStore.IsWithin(path, Path.Combine(Directory.GetParent(clientRoot).FullName, ".git")))
                throw new ArgumentException("报告必须是工程 Assets/Library 和 Git 数据之外的独立 JSON 文件。");
            if (File.Exists(path) || Directory.Exists(path)) throw new IOException("结果路径已存在，必须为每次请求使用新路径。");
        }

        public static CiBuildReport Run(string[] arguments, string clientRoot, CiBuildServices services)
        {
            var report = new CiBuildReport();
            string resultPath = null;
            bool ownsResult = false;
            IDisposable lease = null, configuration = null;
            var paths = new List<CiArtifactPath>();
            Action<string> previousStage = CiBuildProgress.StageChanged;
            Action<string, string> previousArtifact = CiBuildProgress.ArtifactObserved;
            void Save() { if (ownsResult) BuildDataStore.Write(resultPath, JsonConvert.SerializeObject(report, Formatting.Indented)); }
            void Stage(string name) { report.Stage = name; Save(); }
            void Track(string role, string path)
            {
                if (role == "PublishedBundle") report.PublishedBundle = path;
                if (!string.IsNullOrEmpty(path) && !paths.Any(x => x.Role == role && x.Path == path))
                    paths.Add(new CiArtifactPath { Role = role, Path = Path.GetFullPath(path) });
                report.Artifacts = paths.ToArray();
            }
            try
            {
                // 先保留明确的结果路径，使其他参数错误也可以留下失败报告。
                var resultValues = arguments.Select((x, i) => (x, i)).Where(x => x.x == "-ciResult").ToArray();
                if (resultValues.Length == 1 && resultValues[0].i + 1 < arguments.Length)
                {
                    resultPath = CiBuildRequest.Absolute(arguments[resultValues[0].i + 1], "ciResult");
                    ResultPath(resultPath, clientRoot);
                    Directory.CreateDirectory(Path.GetDirectoryName(resultPath));
                    using (new FileStream(resultPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
                    ownsResult = true;
                    Save();
                }
                var parsed = Arguments(arguments);
                if (!ownsResult) throw new ArgumentException("无法确定唯一结果报告路径。");
                report.ConfigPath = parsed.config;
                if (resultPath == parsed.config) throw new ArgumentException("配置和报告不能使用同一路径。");
                Stage("ReadConfiguration");
                byte[] configBytes = File.ReadAllBytes(parsed.config);
                var request = CiBuildRequest.Parse(Encoding.UTF8.GetString(configBytes).TrimStart('\uFEFF'));
                report.RequestId = request.RequestId;
                report.Platform = request.Platform;
                report.BuildType = request.BuildType;
                report.BaselineId = request.BaselineId;
                report.ExpectedCommit = request.ExpectedCommit;
                report.PublishStatus = request.Publish ? "NotStarted" : "NotRequested";
                using (var hash = SHA256.Create())
                    report.ConfigSha256 = BitConverter.ToString(hash.ComputeHash(configBytes)).Replace("-", "").ToLowerInvariant();
                Stage("ValidateParameters");
                request.Validate(clientRoot, resultPath);
                report.UnityEditor = request.UnityEditor;
                report.GitExecutable = request.GitExecutable;
                report.PowerShell = request.PowerShell;
                report.InnoCompiler = request.InnoCompiler;
                report.AndroidSdk = request.AndroidSdk;
                report.AndroidNdk = request.AndroidNdk;
                report.JavaHome = request.JavaHome;
                report.AndroidGradle = request.AndroidGradle;
                report.PersistentDataRoot = request.PersistentDataRoot;
                Stage("AcquireWorkspaceLock");
                lease = WorkspaceBuildLock.Acquire(clientRoot);
                foreach (string name in new[] { ".outpostloop-ci-active.json", ".outpostloop-ci-needs-recovery.json" })
                    if (File.Exists(Path.Combine(clientRoot, name))) throw new InvalidOperationException("存在未完成构建或恢复标记，须先检查工作区。");
                Stage("Preflight");
                var environment = services.Preflight(request) ?? throw new InvalidOperationException("环境检查未返回结果。");
                report.Commit = environment.Commit;
                report.UnityVersion = environment.UnityVersion;
                if (!string.Equals(report.Commit, request.ExpectedCommit, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("当前 Commit 与请求不符，未执行构建。");
                if (string.IsNullOrWhiteSpace(report.UnityVersion)) throw new InvalidOperationException("环境检查没有返回 Unity 版本。");
                if (request.ValidateOnly) { report.Status = "Validated"; report.ExitCode = 0; }
                else
                {
                    Stage("Configure");
                    configuration = services.Configure(request) ?? throw new InvalidOperationException("配置作用域未创建。");
                    report.CleanupStatus = "Pending";
                    report.BuildStatus = "Running";
                    string phase = "Build";
                    CiBuildProgress.StageChanged = name => Stage(phase + "/" + name);
                    CiBuildProgress.ArtifactObserved = Track;
                    Stage("Build");
                    var artifacts = services.Build(request) ?? throw new InvalidOperationException("构建入口未执行或未返回产物。");
                    report.BaselineId = artifacts.HostBaselineId;
                    report.ResourceVersion = artifacts.ResourceVersion;
                    report.AppVersion = artifacts.AppVersion;
                    report.AppVersionCode = artifacts.AppVersionCode;
                    Stage("ValidateArtifacts");
                    if (artifacts.Target != request.Target() || string.IsNullOrEmpty(artifacts.HostBaselineId) ||
                        (request.BuildType == "HostUpdate" && (artifacts.HostBaselineId != request.BaselineId || !string.IsNullOrEmpty(artifacts.PlayerPath))) ||
                        (request.BuildType == "FirstPackage" && string.IsNullOrEmpty(artifacts.PlayerPath)))
                        throw new InvalidOperationException("构建产物的平台、类型或基线归属与请求不符。");
                    artifacts.Validate();
                    Track("Resources", artifacts.PackageDirectory);
                    if (!string.IsNullOrEmpty(artifacts.PlayerPath))
                        Track("Player", artifacts.Target == BuildTarget.StandaloneWindows64 ? Path.GetDirectoryName(artifacts.PlayerPath) : artifacts.PlayerPath);
                    if (!string.IsNullOrEmpty(artifacts.UpdateManifestPath)) Track("UpdateManifest", artifacts.UpdateManifestPath);
                    if (!string.IsNullOrEmpty(artifacts.InstallerDirectory))
                    {
                        var manifest = Boot.WindowsUpdateManifest.ParseAvailable(File.ReadAllText(artifacts.UpdateManifestPath), 0);
                        Track("Installer", Path.Combine(artifacts.InstallerDirectory, manifest.installerFileName));
                    }
                    string baseline = Path.Combine(HostBaselineArchive.BaselineDirectory(request.PersistentDataRoot, request.Target().ToString(), artifacts.HostBaselineId), "manifest.json");
                    if (!File.Exists(baseline)) throw new InvalidOperationException("结果缺少已归档宿主基线。");
                    Track("BaselineManifest", baseline);
                    report.Files = Inventory(paths);
                    report.BuildStatus = "Succeeded";
                    Save(); // 结果不能可靠保存时，不推进发布。
                    if (request.Publish)
                    {
                        phase = "Publish";
                        report.PublishStatus = "Running";
                        Stage("Publish");
                        var published = services.Publish(artifacts, request.PublishBundle);
                        if (published == null || !published.Success) throw new InvalidOperationException("发布未完成：" + published?.Error);
                        report.PublishedBundle = published.BundleDirectory;
                        report.PublishStatus = "Succeeded";
                    }
                    report.Status = "Succeeded";
                    report.ExitCode = 0;
                }
            }
            catch (Exception e)
            {
                report.Status = "Failed";
                report.ExitCode = 1;
                report.Error = e.ToString();
                if (report.BuildStatus == "Running") report.BuildStatus = "Failed";
                if (report.PublishStatus == "Running") report.PublishStatus = "Failed";
                if (report.Stage == "Configure" && File.Exists(Path.Combine(clientRoot, ".outpostloop-ci-active.json")))
                {
                    report.CleanupStatus = "Failed";
                    report.CleanupError = "配置注入中断，存在未恢复日志。";
                }
            }
            finally
            {
                CiBuildProgress.StageChanged = previousStage;
                CiBuildProgress.ArtifactObserved = previousArtifact;
                string failureStage = report.Status == "Failed" ? report.Stage : null;
                if (configuration != null)
                {
                    try { report.Stage = "RestoreConfiguration"; configuration.Dispose(); report.CleanupStatus = "Succeeded"; }
                    catch (Exception e)
                    {
                        report.Status = "Failed"; report.ExitCode = 1; report.CleanupStatus = "Failed"; report.CleanupError = e.ToString();
                        try
                        {
                            BuildDataStore.Write(Path.Combine(clientRoot, ".outpostloop-ci-needs-recovery.json"),
                                JsonConvert.SerializeObject(new { report.RequestId, Stage = "RestoreConfiguration", report.CleanupError }, Formatting.Indented));
                        }
                        catch (Exception markerError) { report.CleanupError += "\n恢复标记保存失败：" + markerError; }
                    }
                }
                report.Stage = failureStage ?? (report.Status == "Failed" ? report.Stage : "Complete");
                if (report.Status == "Failed")
                {
                    try { report.Files = Inventory(paths); }
                    catch (Exception e) { report.InventoryError = e.Message; }
                }
                report.CompletedUtc = DateTime.UtcNow.ToString("O");
                try { Save(); }
                catch (Exception e) { report.Status = "Failed"; report.ExitCode = 1; report.Error += "\n结果报告保存失败：" + e; }
                finally { lease?.Dispose(); }
            }
            return report;
        }

        private static CiArtifactFile[] Inventory(IEnumerable<CiArtifactPath> paths)
        {
            var result = new List<CiArtifactFile>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in paths)
            {
                BuildDataStore.NoLinks(item.Path);
                item.Exists = File.Exists(item.Path) || Directory.Exists(item.Path);
                if (!item.Exists) continue;
                foreach (string file in File.Exists(item.Path) ? new[] { item.Path } : HostBaselineArchive.Enumerate(item.Path))
                    if (seen.Add(file)) result.Add(new CiArtifactFile { Role = item.Role, Path = file, Length = new FileInfo(file).Length, Sha256 = BuildDataStore.Hash(file) });
            }
            return result.ToArray();
        }
    }
}
#endif
