using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Boot;
using Game.Editor;
using UnityEditor;
using UnityEngine;

if (args.Length == 2 && args[0] == "--probe-workspace-lock")
{
    try { using var lease = WorkspaceBuildLock.Acquire(args[1]); return 0; }
    catch (IOException) { return 17; }
}
if (Environment.GetEnvironmentVariable("OUTPOSTLOOP_TEST_FAKE_UNITY") == "1") return CiLauncherChecks.FakeUnity(args);

string fixtureParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "OutpostLoopBuildSeparationChecks"));
string root = Path.Combine(fixtureParent, Guid.NewGuid().ToString("N"));
int passed = 0, failed = 0;
void Check(bool value, string error) { if (!value) throw new Exception(error); }
void Reject(Action action)
{
    try { action(); }
    catch (Exception e) when (e is InvalidOperationException || e is ArgumentException || e is IOException) { return; }
    throw new Exception("预期操作拒绝，但操作成功。");
}
void Test(string name, Action action)
{
    try { action(); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception e) { Console.WriteLine("FAIL " + name + ": " + e); failed++; }
}
string Package(BuildTarget target, string version)
{
    string path = Path.Combine(YooAssetBuildUtility.GetPackageRoot(target), version);
    Directory.CreateDirectory(path);
    File.WriteAllText(Path.Combine(path, "OutpostLoop.version"), version);
    File.WriteAllText(Path.Combine(path, "payload.bundle"), version);
    return path;
}
try
{
    Application.dataPath = Path.Combine(root, "Client", "Assets");
    Directory.CreateDirectory(Application.dataPath);
    string template = Path.Combine(root, "Server", "ResourceServer");
    Directory.CreateDirectory(Path.Combine(template, "Notices"));
    foreach (string file in new[] { "start_resource_server.bat", "resource_server.ps1", "README.txt",
        "validate_resource_update_notices.ps1", "validate_resource_update_notices.bat",
        "publish_app_update.ps1", "publish_windows_update.ps1" })
        File.WriteAllText(Path.Combine(template, file), "fixture only");
    File.WriteAllText(Path.Combine(template, "Notices", "resource-update-notices.json"),
        "{\"schemaVersion\":1,\"defaultLanguage\":\"zh-CN\",\"notices\":[]}");
    string apk = Path.Combine(root, "test.apk");
    File.WriteAllBytes(apk, new byte[] { 1, 2, 3 });
    string descriptor = null;
    Test("APK 清单独立生成且不写发布目录", () =>
    {
        descriptor = AndroidApkPublishUtility.GenerateManifest(apk, "0.1.1", 1);
        var value = ApkUpdateManifest.ParseAvailable(File.ReadAllText(descriptor), "com.godspace.outpostloop", "0.0.0", 0);
        Check(value != null && value.VerifyFile(apk), "清单或哈希不正确");
        Check(!Directory.Exists(Path.Combine(root, "Release")), "生成清单触及发布目录");
    });
    Test("清单生成失败保留原清单", () =>
    {
        string before = File.ReadAllText(descriptor);
        Reject(() => AndroidApkPublishUtility.GenerateManifest(apk, "invalid", 2));
        Check(File.ReadAllText(descriptor) == before, "失败覆盖原清单");
    });
    Test("APK 被修改后拒绝发布且不创建目标", () =>
    {
        File.WriteAllBytes(apk, new byte[] { 3, 2, 1 });
        string target = Path.Combine(root, "must-not-exist");
        Reject(() => AndroidApkPublishUtility.PublishPrepared(apk, descriptor, target));
        Check(!Directory.Exists(target), "损坏产物触及目标");
        File.WriteAllBytes(apk, new byte[] { 1, 2, 3 });
    });
    string selected = Package(BuildTarget.Android, "20261017000101");
    Package(BuildTarget.Android, "20261017000201");
    ResourceServerPublishResult initial = null;
    Test("明确源资源不被更新的其他资源替换", () =>
    {
        initial = ResourceServerPublishUtility.PublishBuiltPackage(selected, "20261017000101", BuildTarget.Android);
        Check(initial.Success && initial.SourcePackageDirectory == selected, initial.Error);
        Check(File.ReadAllText(Path.Combine(initial.BundleDirectory, "ResourceFiles", "payload.bundle")) == "20261017000101", "选错源资源");
    });
    Test("已生成清单可独立发布到明确目标", () =>
    {
        string published = AndroidApkPublishUtility.PublishPrepared(apk, descriptor, initial.BundleDirectory);
        Check(File.ReadAllText(published) == File.ReadAllText(descriptor), "发布时改变了清单");
    });
    Test("缺失指定资源不回退最新资源", () =>
    {
        string before = File.ReadAllText(Path.Combine(initial.BundleDirectory, "ResourceFiles", "OutpostLoop.version"));
        Reject(() => ResourceServerPublishUtility.PublishBuiltPackage(Path.Combine(Path.GetDirectoryName(selected), "missing"),
            "missing", BuildTarget.Android, initial.BundleDirectory));
        Check(File.ReadAllText(Path.Combine(initial.BundleDirectory, "ResourceFiles", "OutpostLoop.version")) == before, "失败修改目标");
    });
    Test("版本不匹配和跨平台源均拒绝", () =>
    {
        Reject(() => ResourceServerPublishUtility.PublishBuiltPackage(selected, "wrong", BuildTarget.Android));
        Reject(() => ResourceServerPublishUtility.PublishBuiltPackage(selected, "20261017000101", BuildTarget.StandaloneWindows64));
    });
    Test("明确目标不更新其他发布包", () =>
    {
        string other = Path.Combine(Path.GetDirectoryName(initial.BundleDirectory), "ResourceServerBundle_20990101_000000");
        Directory.CreateDirectory(Path.Combine(other, "ResourceFiles"));
        File.WriteAllText(Path.Combine(other, "ResourceFiles", "OutpostLoop.version"), "sentinel");
        var result = ResourceServerPublishUtility.PublishBuiltPackage(selected, "20261017000101", BuildTarget.Android, initial.BundleDirectory);
        Check(result.Success && result.BundleDirectory == initial.BundleDirectory, result.Error);
        Check(File.ReadAllText(Path.Combine(other, "ResourceFiles", "OutpostLoop.version")) == "sentinel", "误改其他发布包");
    });
    Test("APK 身份不匹配在发布资源前拒绝", () =>
    {
        var artifacts = new BuildArtifacts { Target = BuildTarget.Android, PlayerPath = apk,
            UpdateManifestPath = descriptor, AppVersion = "0.1.2", AppVersionCode = 2 };
        Reject(() => artifacts.Publish());
    });
    Test("Windows 安装器哈希校验拒绝被修改产物", () =>
    {
        string output = Path.Combine(root, "installer");
        Directory.CreateDirectory(output);
        string installer = Path.Combine(output, "setup.exe");
        File.WriteAllBytes(installer, new byte[] { 1, 2, 3 });
        var manifest = new WindowsUpdateManifest { schemaVersion = 1, productId = WindowsUpdateManifest.ProductId,
            platform = "Windows", architecture = "x64", versionName = "0.1.1", releaseNumber = 1,
            installerFileName = "setup.exe", sizeBytes = 3, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(installer))).ToLowerInvariant() };
        File.WriteAllText(Path.Combine(output, "app-update-windows.json"), JsonUtility.ToJson(manifest, true));
        Check(WindowsInstallerPublishUtility.ValidateInstaller(output, 1) == installer, "有效产物被拒绝");
        File.WriteAllBytes(installer, new byte[] { 3, 2, 1 });
        Reject(() => WindowsInstallerPublishUtility.ValidateInstaller(output, 1));
    });
    ResourceServerPublishResult bound = null;
    Test("新宿主创建独立发布包并记录基线归属", () =>
    {
        bound = ResourceServerPublishUtility.PublishBuiltPackage(selected, "20261017000101", BuildTarget.Android,
            null, "managed-host-1", true);
        Check(bound.Success && bound.BundleDirectory != initial.BundleDirectory, bound.Error);
        Check(File.ReadAllText(Path.Combine(bound.BundleDirectory, "host-baseline.id")) == "managed-host-1", "基线归属缺失");
    });
    Test("Host 更新按基线选择目标而非最近发布包", () =>
    {
        var result = ResourceServerPublishUtility.PublishBuiltPackage(selected, "20261017000101", BuildTarget.Android,
            null, "managed-host-1", false);
        Check(result.Success && result.BundleDirectory == bound.BundleDirectory, "Host 更新选错目标");
    });
    Test("Host 基线不同或未发布时拒绝更新其他目标", () =>
    {
        Reject(() => ResourceServerPublishUtility.PublishBuiltPackage(selected, "20261017000101", BuildTarget.Android,
            bound.BundleDirectory, "managed-host-2", false));
        Reject(() => ResourceServerPublishUtility.PublishBuiltPackage(selected, "20261017000101", BuildTarget.Android,
            null, "not-published", false));
        Check(File.ReadAllText(Path.Combine(bound.BundleDirectory, "host-baseline.id")) == "managed-host-1", "目标归属被改变");
    });
    Test("未携带基线的资源不得覆盖已绑定宿主的发布包", () =>
    {
        Reject(() => ResourceServerPublishUtility.PublishBuiltPackage(selected, "20261017000101", BuildTarget.Android, bound.BundleDirectory));
    });
    PersistentBuildChecks.Run(root, Test);
    CiBuildChecks.Run(root, Test);
    CiLauncherChecks.Run(root, Test);
}
finally
{
    string resolved = Path.GetFullPath(root);
    if (resolved.StartsWith(fixtureParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Directory.Exists(resolved))
        Directory.Delete(resolved, true);
}
Console.WriteLine($"Build separation checks: {passed} passed, {failed} failed. No Unity build or service was started.");
return failed == 0 ? 0 : 1;
