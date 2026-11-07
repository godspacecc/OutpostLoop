using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Boot;
using Game.Editor;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

internal static class CiBuildChecks
{
    private sealed class OnDispose : IDisposable
    {
        private readonly Action action;
        public OnDispose(Action action) { this.action = action; }
        public void Dispose() => action();
    }

    public static void Run(string root, Action<string, Action> test)
    {
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        void Reject(Action action)
        {
            try { action(); }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is IOException) { return; }
            throw new Exception("预期拒绝操作。");
        }
        string beforeDataPath = Application.dataPath;
        string checkout = Path.Combine(root, "ci-checkout"), client = Path.Combine(checkout, "Client");
        string tools = Path.Combine(root, "ci-tools"), data = Path.Combine(root, "ci-data");
        Directory.CreateDirectory(Path.Combine(client, "Assets", "StreamingAssets"));
        Directory.CreateDirectory(Path.Combine(client, "ProjectSettings"));
        File.WriteAllText(Path.Combine(client, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 2022.3.55f1c1");
        File.WriteAllText(Path.Combine(client, "ProjectSettings", "ProjectSettings.asset"), "original settings");
        string hostConfig = Path.Combine(client, "Assets", "StreamingAssets", "hostConfig.json");
        string originalHost = "{\"resourceServer\":\"http://127.0.0.1:8000\"}";
        File.WriteAllText(hostConfig, originalHost);
        Directory.CreateDirectory(tools);
        foreach (string name in new[] { "Unity.exe", "git.exe", "powershell.exe", "ISCC.exe" }) File.WriteAllText(Path.Combine(tools, name), "fixture tool, never executed");
        string androidTools = Path.Combine(tools, "Data", "PlaybackEngines", "AndroidPlayer");
        foreach (string file in new[] { "SDK/platform-tools/adb.exe", "NDK/ndk-build.cmd", "OpenJDK/bin/java.exe", "Tools/gradle/bin/gradle.bat" })
        {
            string path = Path.Combine(androidTools, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, "fixture");
        }
        int sequence = 0, builds = 0, publishes = 0;
        string route = null;
        CiBuildRequest NewRequest(string platform = "PC", string kind = "FirstPackage") => new CiBuildRequest
        {
            SchemaVersion = 1, ProjectId = BuildDataStore.ProjectId, RequestId = "ci-" + (++sequence),
            ProjectPath = client, Platform = platform, BuildType = kind, ExpectedCommit = new string('a', 40),
            BaselineId = kind == "HostUpdate" ? "existing-host" : null, ResourceServerUrl = "https://resources.example.com/game",
            PersistentDataRoot = data, UnityEditor = Path.Combine(tools, "Unity.exe"), GitExecutable = Path.Combine(tools, "git.exe"),
            PowerShell = Path.Combine(tools, "powershell.exe"), InnoCompiler = Path.Combine(tools, "ISCC.exe"),
            AndroidSdk = Path.Combine(androidTools, "SDK"), AndroidNdk = Path.Combine(androidTools, "NDK"), JavaHome = Path.Combine(androidTools, "OpenJDK"),
            AndroidGradle = Path.Combine(androidTools, "Tools", "gradle")
        };
        BuildArtifacts FakeArtifacts(CiBuildRequest request, string selectedRoute)
        {
            builds++; route = selectedRoute;
            CiBuildProgress.Stage("FixtureBuild");
            string resource = "20261017" + sequence.ToString("D6");
            string package = Path.Combine(YooAssetBuildUtility.GetPackageRoot(request.Target()), resource);
            Directory.CreateDirectory(package);
            File.WriteAllText(Path.Combine(package, "OutpostLoop.version"), resource);
            File.WriteAllText(Path.Combine(package, "payload.bundle"), "fixture resource");
            var artifact = new BuildArtifacts { Target = request.Target(), PackageDirectory = package, ResourceVersion = resource,
                HostBaselineId = request.BuildType == "HostUpdate" ? request.BaselineId : "first-" + request.RequestId,
                AppVersion = "0.1.1", AppVersionCode = 2 };
            CiBuildProgress.Artifact("Resources", package);
            if (request.BuildType == "FirstPackage")
            {
                string output = Path.Combine(checkout, "outputs", request.RequestId);
                Directory.CreateDirectory(output);
                artifact.PlayerPath = Path.Combine(output, request.Platform == "PC" ? "OutpostLoop.exe" : "OutpostLoop.apk");
                File.WriteAllBytes(artifact.PlayerPath, new byte[] { 1, 2, 3 });
                if (request.Platform == "Android") artifact.UpdateManifestPath = AndroidApkPublishUtility.GenerateManifest(artifact.PlayerPath, artifact.AppVersion, artifact.AppVersionCode);
                else
                {
                    artifact.InstallerDirectory = output;
                    string installer = Path.Combine(output, "setup.exe");
                    File.WriteAllBytes(installer, new byte[] { 1, 2, 3 });
                    artifact.UpdateManifestPath = Path.Combine(output, "app-update-windows.json");
                    File.WriteAllText(artifact.UpdateManifestPath, JsonConvert.SerializeObject(new WindowsUpdateManifest
                    {
                        schemaVersion=1, productId=WindowsUpdateManifest.ProductId, platform="Windows", architecture="x64",
                        versionName=artifact.AppVersion, releaseNumber=2, installerFileName="setup.exe", sizeBytes=3,
                        sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(installer))).ToLowerInvariant()
                    }));
                }
            }
            string baseline = HostBaselineArchive.BaselineDirectory(data, artifact.Target.ToString(), artifact.HostBaselineId);
            Directory.CreateDirectory(baseline);
            File.WriteAllText(Path.Combine(baseline, "manifest.json"), "fixture report reference; archive semantics tested separately");
            return artifact;
        }
        CiBuildServices Services(CiBuildRequest request) => new CiBuildServices
        {
            Preflight = _ => new CiEnvironmentInfo { Commit = new string('a', 40), UnityVersion = "2022.3.55f1c1" },
            Configure = r => new CiConfigurationScope(r, _ => { }),
            PcFirst = () => FakeArtifacts(request, "PC/FirstPackage"), PcHost = id => FakeArtifacts(request, "PC/HostUpdate/" + id),
            AndroidFirst = () => FakeArtifacts(request, "Android/FirstPackage"), AndroidHost = id => FakeArtifacts(request, "Android/HostUpdate/" + id),
            Publish = (artifact, bundle) => { publishes++; return new ResourceServerPublishResult { Success = true, BundleDirectory = bundle ?? "fixture-published" }; }
        };
        (CiBuildReport report, string path) Execute(CiBuildRequest request, CiBuildServices services = null, string raw = null, string[] extra = null)
        {
            string output = Path.Combine(checkout, "Artifacts", "CI", request.RequestId);
            Directory.CreateDirectory(output);
            string config = Path.Combine(output, "request.json"), result = Path.Combine(output, "result.json");
            // Product parser rejects explicit null tokens. Real request files omit optional fields.
            File.WriteAllText(config, raw ?? JsonConvert.SerializeObject(request, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
            string[] args = new[] { "Unity.exe", "-batchmode", "-ciConfig", config, "-ciResult", result }.Concat(extra ?? Array.Empty<string>()).ToArray();
            return (CiBuildRunner.Run(args, client, services ?? Services(request)), result);
        }
        try
        {
            Application.dataPath = Path.Combine(client, "Assets");
            test("CI 严格拒绝未知字段、重复字段和错误字段类型", () =>
            {
                Reject(() => CiBuildRequest.Parse("{\"Unknown\":1}"));
                Reject(() => CiBuildRequest.Parse("{\"SchemaVersion\":1,\"SchemaVersion\":1}"));
                Reject(() => CiBuildRequest.Parse("{\"Publish\":\"false\"}"));
            });
            foreach (string platform in new[] { "PC", "Android" })
            foreach (string kind in new[] { "FirstPackage", "HostUpdate" })
            {
                test("CI 四种路由及无发布默认 " + platform + "/" + kind, () =>
                {
                    var request = NewRequest(platform, kind); int count = builds, pub = publishes;
                    var run = Execute(request);
                    Check(run.report.ExitCode == 0 && run.report.Status == "Succeeded" && run.report.BuildStatus == "Succeeded", run.report.Error + run.report.CleanupError);
                    Check(builds == count + 1 && route.StartsWith(platform + "/" + kind) && publishes == pub, "路由或发布默认值错误");
                    Check(run.report.Files.Length > 0 && run.report.Files.All(x => x.Sha256.Length == 64), "缺少哈希报告");
                    Check(JsonConvert.DeserializeObject<CiBuildReport>(File.ReadAllText(run.path)).RequestId == request.RequestId, "结果不属于请求");
                    Check(File.ReadAllText(hostConfig) == originalHost && CiToolConfiguration.Current == null, "临时配置未恢复");
                });
            }
            test("环境验证模式不构建、不注入配置、不预留版本", () =>
            {
                var request = NewRequest(); request.ValidateOnly = true; request.PersistentDataRoot = Path.Combine(root, "never-created-ci-data");
                var services = Services(request); services.Configure = _ => throw new Exception("不得配置"); int before = builds;
                var run = Execute(request, services);
                Check(run.report.Status == "Validated" && run.report.BuildStatus == "NotStarted" && builds == before, run.report.Error);
                Check(!Directory.Exists(request.PersistentDataRoot), "验证环境创建了版本台账目录");
            });
            test("缺参数及损坏配置仍保存明确失败报告", () =>
            {
                var request = NewRequest(); var run = Execute(request, raw: "{broken");
                Check(run.report.ExitCode == 1 && run.report.Stage == "ReadConfiguration" && File.Exists(run.path), "未保存配置错误");
                request = NewRequest(); run = Execute(request, extra: new[] { "-ciUnknown", "value" });
                Check(run.report.ExitCode == 1 && run.report.Stage == "ParseArguments" && File.Exists(run.path), "未保存参数错误");
            });
            test("错误平台、缺基线和缺工具均不执行构建", () =>
            {
                int before = builds;
                var request = NewRequest(); request.Platform = "iOS"; Check(Execute(request).report.Status == "Failed", "未知平台成功");
                request = NewRequest("Android", "HostUpdate"); request.BaselineId = null; Check(Execute(request).report.Status == "Failed", "缺基线成功");
                request = NewRequest(); request.InnoCompiler = Path.Combine(root, "missing", "ISCC.exe"); Check(Execute(request).report.Status == "Failed", "缺工具成功");
                Check(builds == before, "参数失败仍构建");
            });
            test("Commit 不符与未返回产物均不报告成功", () =>
            {
                var request = NewRequest(); request.ExpectedCommit = new string('f', 40); int before = builds;
                var run = Execute(request); Check(run.report.Stage == "Preflight" && run.report.ExitCode == 1 && builds == before, "Commit 不符仍执行");
                request = NewRequest(); var services = Services(request); services.PcFirst = () => null;
                run = Execute(request, services); Check(run.report.ExitCode == 1 && run.report.BuildStatus == "Failed", "未执行被当成成功");
            });
            test("产物的平台或宿主归属不符时拒绝成功", () =>
            {
                var request = NewRequest("PC", "HostUpdate"); var services = Services(request);
                services.PcHost = _ => { var artifact = FakeArtifacts(request, "mismatch"); artifact.HostBaselineId = "other-host"; return artifact; };
                var run = Execute(request, services); Check(run.report.ExitCode == 1 && run.report.Stage == "ValidateArtifacts", "错误基线仍成功");
            });
            test("构建失败保留准确阶段及已知产物哈希", () =>
            {
                var request = NewRequest(); var services = Services(request);
                services.PcFirst = () =>
                {
                    string partial = Path.Combine(root, "retained-player.exe"); File.WriteAllText(partial, "partial fixture");
                    CiBuildProgress.Artifact("Player", partial); CiBuildProgress.Stage("GenerateInstaller"); throw new InvalidOperationException("fixture build failure");
                };
                var run = Execute(request, services);
                Check(run.report.Status == "Failed" && run.report.Stage == "Build/GenerateInstaller" && run.report.Files.Any(x => x.Role == "Player"), "失败阶段或保留产物缺失");
                Check(run.report.CleanupStatus == "Succeeded" && File.ReadAllText(hostConfig) == originalHost, "失败未恢复配置");
            });
            test("发布失败保留构建成功状态并返回非零退出码", () =>
            {
                var request = NewRequest(); request.Publish = true; var services = Services(request);
                services.Publish = (_, _) => { CiBuildProgress.Stage("ReplaceResources"); throw new IOException("fixture publish failure"); };
                var run = Execute(request, services);
                Check(run.report.ExitCode == 1 && run.report.BuildStatus == "Succeeded" && run.report.PublishStatus == "Failed" && run.report.Stage == "Publish/ReplaceResources", "发布失败被掩盖");
            });
            test("显式 Host 发布目标原样传递且独立记录成功", () =>
            {
                var request = NewRequest("Android", "HostUpdate"); request.Publish = true;
                request.PublishBundle = Path.Combine(checkout, "Release", "ResourceServer", "Android", "ResourceServerBundle_20261017_000000");
                var services = Services(request);
                services.Publish = (artifact, bundle) => { Check(bundle == request.PublishBundle && artifact.HostBaselineId == request.BaselineId, "目标或基线被替换"); return new ResourceServerPublishResult { Success = true, BundleDirectory = bundle }; };
                var run = Execute(request, services); Check(run.report.PublishStatus == "Succeeded" && run.report.PublishedBundle == request.PublishBundle, run.report.Error);
            });
            test("配置恢复失败不能返回构建成功", () =>
            {
                var request = NewRequest(); var services = Services(request);
                services.Configure = r => { var scope = new CiConfigurationScope(r, _ => { }); return new OnDispose(() => { scope.Dispose(); throw new IOException("fixture cleanup failure"); }); };
                var run = Execute(request, services);
                Check(run.report.ExitCode == 1 && run.report.BuildStatus == "Succeeded" && run.report.CleanupStatus == "Failed" && run.report.CleanupError.Contains("fixture cleanup"), "清理失败被忽略");
                string marker = Path.Combine(client, ".outpostloop-ci-needs-recovery.json");
                try
                {
                    int count = builds;
                    Check(File.Exists(marker) && Execute(NewRequest()).report.ExitCode == 1 && builds == count, "恢复失败后仍可直接重试");
                }
                finally { if (File.Exists(marker)) File.Delete(marker); }
            });
            test("配置注入失败恢复文件、环境和工具作用域", () =>
            {
                var request = NewRequest(); string before = Environment.GetEnvironmentVariable("OUTPOSTLOOP_BUILD_DATA_ROOT");
                using var lease = WorkspaceBuildLock.Acquire(client);
                Reject(() => new CiConfigurationScope(request, path => { if (path != null) throw new IOException("fixture import failure"); }));
                Check(File.ReadAllText(hostConfig) == originalHost && Environment.GetEnvironmentVariable("OUTPOSTLOOP_BUILD_DATA_ROOT") == before && CiToolConfiguration.Current == null, "注入失败污染环境");
                Check(!File.Exists(Path.Combine(client, ".outpostloop-ci-active.json")), "恢复成功仍留中断标记");
            });
            test("显式 Inno 配置不读取 EditorPrefs 且 Android 工具不得漂移", () =>
            {
                var request = NewRequest(); using var lease = WorkspaceBuildLock.Acquire(client); using var scope = new CiConfigurationScope(request, _ => { });
                Check(WindowsInstallerPublishUtility.EnsureCompiler() == request.InnoCompiler, "没有使用显式编译器");
                Directory.CreateDirectory(Path.Combine(tools, "Modules"));
                var process = new ProcessStartInfo(request.PowerShell) { UseShellExecute = false };
                CiToolConfiguration.ConfigurePowerShellProcess(process, request.PowerShell);
                Check(process.Environment["PSModulePath"] == Path.Combine(tools, "Modules"), "PowerShell 仍依赖用户模块路径");
                CiToolConfiguration.ValidateEmbeddedAndroid(request, Path.Combine(tools, "Data"));
                request.AndroidNdk = tools; Reject(() => CiToolConfiguration.ValidateEmbeddedAndroid(request, Path.Combine(tools, "Data")));
            });
            test("真实文件恢复失败保留日志且仍恢复进程环境", () =>
            {
                var request = NewRequest(); string before = Environment.GetEnvironmentVariable("OUTPOSTLOOP_BUILD_DATA_ROOT");
                string settings = Path.Combine(client, "ProjectSettings", "ProjectSettings.asset");
                string journal = Path.Combine(client, ".outpostloop-ci-active.json");
                using var lease = WorkspaceBuildLock.Acquire(client);
                var scope = new CiConfigurationScope(request, _ => { });
                File.Delete(settings); Directory.CreateDirectory(settings); // Block the original file path with a fixture directory.
                try
                {
                    Reject(() => scope.Dispose());
                    Check(File.Exists(journal) && CiToolConfiguration.Current == null && Environment.GetEnvironmentVariable("OUTPOSTLOOP_BUILD_DATA_ROOT") == before,
                        "恢复故障未保留日志或污染进程环境");
                }
                finally { Directory.Delete(settings); File.WriteAllText(settings, "original settings"); File.Delete(journal); }
            });
            test("工作目录被其他文件句柄占用时不构建", () =>
            {
                var request = NewRequest(); int before = builds;
                using var held = new FileStream(Path.Combine(client, ".outpostloop-build.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                var run = Execute(request); Check(run.report.ExitCode == 1 && run.report.Stage == "AcquireWorkspaceLock" && builds == before, "锁冲突仍构建");
            });
            test("同线程可嵌套工作锁，其他线程被拒绝", () =>
            {
                using var outer = WorkspaceBuildLock.Acquire(client); using (WorkspaceBuildLock.Acquire(client)) { }
                bool rejected = Task.Run(() => { try { using var other = WorkspaceBuildLock.Acquire(client); return false; } catch (IOException) { return true; } }).GetAwaiter().GetResult();
                Check(rejected, "其他线程重入了工作目录锁");
            });
            test("实际子进程不能越过工作目录锁，释放后可重新取得", () =>
            {
                int Probe()
                {
                    var info = new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = false, CreateNoWindow = true };
                    if (Path.GetFileNameWithoutExtension(Environment.ProcessPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
                    info.ArgumentList.Add("--probe-workspace-lock"); info.ArgumentList.Add(client);
                    using var child = Process.Start(info);
                    if (!child.WaitForExit(10000)) { child.Kill(); throw new Exception("锁探测子进程超时"); }
                    return child.ExitCode;
                }
                using (WorkspaceBuildLock.Acquire(client)) Check(Probe() == 17, "子进程绕过锁");
                Check(Probe() == 0, "释放后锁未恢复");
            });
            test("已有结果和中断标记不被自动覆盖或忽略", () =>
            {
                var request = NewRequest(); var run = Execute(request); string before = File.ReadAllText(run.path);
                var second = CiBuildRunner.Run(new[] { "-ciConfig", Path.Combine(Path.GetDirectoryName(run.path), "request.json"), "-ciResult", run.path }, client, Services(request));
                Check(second.ExitCode == 1 && File.ReadAllText(run.path) == before, "已有结果被覆盖");
                string marker = Path.Combine(client, ".outpostloop-ci-needs-recovery.json"); File.WriteAllText(marker, "fixture interrupted");
                try { int count = builds; Check(Execute(NewRequest()).report.ExitCode == 1 && builds == count, "中断标记被忽略"); }
                finally { File.Delete(marker); }
            });
            test("报告写入失败时不推进发布并释放工作锁", () =>
            {
                var request = NewRequest(); request.Publish = true; var services = Services(request); int publishCount = publishes;
                services.PcFirst = () =>
                {
                    var artifact = FakeArtifacts(request, "fixture");
                    string folder = Path.Combine(checkout, "Artifacts", "CI", request.RequestId);
                    File.Delete(Path.Combine(folder, "result.json")); File.Delete(Path.Combine(folder, "request.json")); Directory.Delete(folder); File.WriteAllText(folder, "blocked output");
                    return artifact;
                };
                var run = Execute(request, services);
                Check(run.report.ExitCode == 1 && publishes == publishCount, "报告失败仍发布");
                using var lease = WorkspaceBuildLock.Acquire(client);
            });
        }
        finally { Application.dataPath = beforeDataPath; }
    }
}
