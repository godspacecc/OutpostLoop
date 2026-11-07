using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Game.Editor;

internal static class CiLauncherChecks
{
    // The helper is this test executable copied as Unity.exe. It never links or calls CiBuildEntry or a Unity build.
    internal static int FakeUnity(string[] arguments)
    {
        if (Environment.GetEnvironmentVariable("OUTPOSTLOOP_TEST_FAKE_MODE") == "missing-report") return 0;
        int index = Array.IndexOf(arguments, "-ciConfig");
        string config = arguments[index + 1];
        var request = CiBuildRequest.Parse(File.ReadAllText(config));
        if (!request.ValidateOnly || request.Publish) throw new InvalidOperationException("Test helper only supports non-building validation.");
        try { File.WriteAllText(config, "must be blocked"); throw new Exception("Request snapshot was writable."); }
        catch (IOException) { Console.WriteLine("SNAPSHOT_WRITE_BLOCKED"); }
        UnityEngine.Application.dataPath = Path.Combine(request.ProjectPath, "Assets");
        var report = CiBuildRunner.Run(arguments, request.ProjectPath, new CiBuildServices
        {
            Preflight = _ => new CiEnvironmentInfo { Commit = request.ExpectedCommit, UnityVersion = "fake-runtime-for-launcher-test" }
        });
        return report.ExitCode;
    }

    internal static void Run(string root, Action<string, Action> test)
    {
        if (!OperatingSystem.IsWindows()) { Console.WriteLine("SKIP Windows-only launcher checks"); return; }
        void Check(bool ok, string error) { if (!ok) throw new Exception(error); }
        string repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string source = Path.Combine(repo, "Tools", "CI", "invoke_build.ps1");
        if (!File.Exists(source)) throw new FileNotFoundException("Cannot locate checked-in CI launcher.", source);
        string checkout = Path.Combine(root, "launcher-checkout"), client = Path.Combine(checkout, "Client"), tools = Path.Combine(root, "launcher-tools");
        string script = Path.Combine(checkout, "Tools", "CI", "invoke_build.ps1");
        Directory.CreateDirectory(Path.GetDirectoryName(script)); File.Copy(source, script);
        Directory.CreateDirectory(Path.Combine(client, "Assets")); Directory.CreateDirectory(Path.Combine(client, "ProjectSettings"));
        File.WriteAllText(Path.Combine(client, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 2022.3.55f1c1");
        Directory.CreateDirectory(tools);
        foreach (string file in Directory.GetFiles(AppContext.BaseDirectory)) File.Copy(file, Path.Combine(tools, Path.GetFileName(file)), true);
        string helper = Path.Combine(AppContext.BaseDirectory, "BuildSeparationValidation.exe");
        File.Copy(helper, Path.Combine(tools, "Unity.exe"), true);
        foreach (string name in new[] { "git.exe", "powershell.exe", "ISCC.exe" }) File.WriteAllText(Path.Combine(tools, name), "never executed fixture tool");
        string powershell = Path.Combine(Environment.GetEnvironmentVariable("SystemRoot"), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        int sequence = 0;
        (int exit, CiBuildReport report, string result) Invoke(bool inputOnly = true, bool missingTool = false, bool badJson = false, string mode = null)
        {
            string id = "launcher-" + (++sequence), directory = Path.Combine(checkout, "Artifacts", "CI", id);
            Directory.CreateDirectory(directory);
            string config = Path.Combine(directory, "input.json"), result = Path.Combine(directory, "result.json");
            var request = new CiBuildRequest
            {
                SchemaVersion=1, ProjectId=BuildDataStore.ProjectId, RequestId=id, ProjectPath=client, Platform="PC", BuildType="FirstPackage",
                ExpectedCommit=new string('a', 40), ValidateOnly=true, ResourceServerUrl="https://resource.example.com/windows",
                PersistentDataRoot=Path.Combine(root, "launcher-data"), UnityEditor=Path.Combine(tools, "Unity.exe"),
                GitExecutable=Path.Combine(tools, "git.exe"), PowerShell=Path.Combine(tools, "powershell.exe"),
                InnoCompiler=Path.Combine(missingTool ? Path.Combine(tools, "missing") : tools, "ISCC.exe")
            };
            File.WriteAllText(config, badJson ? "{broken" : JsonConvert.SerializeObject(request, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
            var info = new ProcessStartInfo(powershell) { UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true, RedirectStandardError=true };
            foreach (string arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-Config", config, "-ResultPath", result }) info.ArgumentList.Add(arg);
            if (inputOnly) info.ArgumentList.Add("-ValidateInputOnly");
            info.Environment["OUTPOSTLOOP_TEST_FAKE_UNITY"] = "1";
            info.Environment["OUTPOSTLOOP_TEST_FAKE_MODE"] = mode ?? "validate";
            using var process = Process.Start(info);
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30000)) { process.Kill(); throw new Exception("Launcher test timed out."); }
            string output = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
            Check(File.Exists(result), "Missing launcher report: " + output);
            return (process.ExitCode, JsonConvert.DeserializeObject<CiBuildReport>(File.ReadAllText(result)), result);
        }
        test("启动器只检查输入不启动 Unity 或构建", () =>
        {
            var run = Invoke(); Check(run.exit == 0 && run.report.Status == "LauncherValidated" && run.report.BuildStatus == "NotStarted", run.report.Error);
        });
        test("启动器缺工具和坏 JSON 均产生失败报告", () =>
        {
            var run = Invoke(missingTool:true); Check(run.exit != 0 && run.report.Status == "Failed", "Missing compiler accepted");
            run = Invoke(badJson:true); Check(run.exit != 0 && run.report.Status == "Failed", "Bad JSON accepted");
        });
        test("启动器进程门锁冲突不启动子进程", () =>
        {
            using var held = new FileStream(Path.Combine(client, ".outpostloop-ci.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var run = Invoke(); Check(run.exit != 0 && run.report.Stage == "LauncherLock", "Launcher lock bypassed");
        });
        test("启动器使用冻结请求并核对辅助进程的终态报告", () =>
        {
            var run = Invoke(inputOnly:false);
            Check(run.exit == 0 && run.report.Status == "Validated" && run.report.BuildStatus == "NotStarted", run.report.Error);
            Check(File.ReadAllText(Path.Combine(Path.GetDirectoryName(run.result), "result.stdout.log")).Contains("SNAPSHOT_WRITE_BLOCKED"), "Frozen snapshot was not protected");
            Check(run.report.ConfigPath.EndsWith("result.request.json", StringComparison.Ordinal) && run.report.ConfigSha256.Length == 64, "Snapshot report reference missing");
        });
        test("子进程退出零但缺报告仍失败并阻止未检查工作区重试", () =>
        {
            string marker = Path.Combine(client, ".outpostloop-ci-needs-recovery.json");
            try
            {
                var run = Invoke(inputOnly:false, mode:"missing-report");
                Check(run.exit != 0 && run.report.Status == "Failed" && File.Exists(marker), "Missing report became success");
                Check(Invoke().exit != 0, "Interrupted workspace was reused");
            }
            finally { if (File.Exists(marker)) File.Delete(marker); }
        });
    }
}
