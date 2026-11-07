using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Editor;
using UnityEngine;

internal static class PersistentBuildChecks
{
    public static void Run(string root, Action<string, Action> test)
    {
        void Check(bool value, string error) { if (!value) throw new Exception(error); }
        void Reject(Action action)
        {
            try { action(); }
            catch (Exception e) when (e is InvalidOperationException || e is IOException || e is ArgumentException) { return; }
            throw new Exception("操作应当拒绝。");
        }
        string data = Path.Combine(root, "persistent");
        test("默认数据配置拒绝检出目录内和相对路径", () =>
        {
            string before = Environment.GetEnvironmentVariable("OUTPOSTLOOP_BUILD_DATA_ROOT");
            try
            {
                Environment.SetEnvironmentVariable("OUTPOSTLOOP_BUILD_DATA_ROOT", Path.Combine(root, "Client", "Library", "persistent"));
                Reject(() => { _ = BuildDataStore.Root; });
                Environment.SetEnvironmentVariable("OUTPOSTLOOP_BUILD_DATA_ROOT", "relative-data");
                Reject(() => { _ = BuildDataStore.Root; });
            }
            finally { Environment.SetEnvironmentVariable("OUTPOSTLOOP_BUILD_DATA_ROOT", before); }
        });
        test("同一持久台账跨检出与失败重试不复用应用序号", () =>
        {
            var first = PersistentBuildVersions.ReserveApplication(data, "Android", "0.1.0", 1);
            var second = PersistentBuildVersions.ReserveApplication(data, "Android", "0.1.0", 1);
            Check(first.VersionCode == 2 && second.VersionCode == 3 && second.VersionName == "0.1.2", "源版本重置影响持久版本");
            var pc = PersistentBuildVersions.ReserveApplication(data, "StandaloneWindows64", "0.1.0", 0);
            Check(pc.VersionCode == 1 && pc.VersionName == "0.1.0", "平台版本未隔离");
        });
        test("PC 身份回滚不退还已预留序号", () =>
        {
            string folder = Path.Combine(root, "identity");
            Directory.CreateDirectory(folder);
            string ledger = Path.Combine(folder, "ledger.json"), embedded = Path.Combine(folder, "embedded.json");
            File.WriteAllText(ledger, "{\"schemaVersion\":1,\"productId\":\"com.outpostloop.game\",\"platform\":\"Windows\",\"architecture\":\"x64\",\"versionName\":\"0.1.0\",\"releaseNumber\":0}");
            var reservation = PersistentBuildVersions.ReserveApplication(data, "StandaloneWindows64", "0.1.0", 0);
            using (new WindowsReleaseIdentity(ledger, embedded, _ => { }, reservation)) { }
            Check(!File.Exists(embedded), "本地身份没有回滚");
            var next = PersistentBuildVersions.ReserveApplication(data, "StandaloneWindows64", "0.1.0", 0);
            Check(next.VersionCode == reservation.VersionCode + 1, "失败复用了版本");
        });
        test("资源版本抵抗同秒毫秒差异及系统时钟回退", () =>
        {
            string packages = Path.Combine(root, "versionPackages");
            string first = PersistentBuildVersions.ReserveResource(data, "Android", new DateTime(2026, 10, 17, 1, 0, 0, 500), packages);
            string second = PersistentBuildVersions.ReserveResource(data, "Android", new DateTime(2026, 10, 17, 1, 0, 0, 900), packages);
            string third = PersistentBuildVersions.ReserveResource(data, "Android", new DateTime(2025, 1, 1), packages);
            Check(first == "20261017010000" && second == "20261017010001" && third == "20261017010002", "资源版本重复或回退");
        });
        test("已有资源目录和指定基线版本均构成分配下限", () =>
        {
            string packages = Path.Combine(root, "occupiedVersions");
            Directory.CreateDirectory(Path.Combine(packages, "20271017000001"));
            var version = PersistentBuildVersions.ReserveResource(data, "Android", new DateTime(2026, 1, 1), packages, "20271017000000");
            Check(version == "20271017000002", "未跳过已有版本");
        });
        test("损坏台账不回退源码版本", () =>
        {
            string bad = Path.Combine(root, "badLedger");
            PersistentBuildVersions.ReserveApplication(bad, "Android", "0.1.0", 1);
            File.WriteAllText(Path.Combine(bad, "versions", "Android.json"), "{broken");
            Reject(() => PersistentBuildVersions.ReserveApplication(bad, "Android", "0.1.0", 1));
        });
        test("曾初始化的台账丢失时拒绝重建", () =>
        {
            string lost = Path.Combine(root, "lostLedger");
            PersistentBuildVersions.ReserveApplication(lost, "Android", "0.1.0", 1);
            File.Delete(Path.Combine(lost, "versions", "Android.json"));
            Reject(() => PersistentBuildVersions.ReserveApplication(lost, "Android", "0.1.0", 1));
        });
        test("合法 JSON 中的版本计数被回退也会拒绝", () =>
        {
            string corrupt = Path.Combine(root, "editedLedger");
            PersistentBuildVersions.ReserveApplication(corrupt, "Android", "0.1.0", 20);
            string file = Path.Combine(corrupt, "versions", "Android.json");
            File.WriteAllText(file, File.ReadAllText(file).Replace("\"AppCode\": 21", "\"AppCode\": 1"));
            Reject(() => PersistentBuildVersions.ReserveApplication(corrupt, "Android", "0.1.0", 1));
        });
        test("独占锁阻止并发写入台账", () =>
        {
            using var guard = BuildDataStore.Lock(data);
            Reject(() => PersistentBuildVersions.ReserveApplication(data, "Android", "0.1.0", 1));
        });

        string sourcesRoot = Path.Combine(root, "baselineSources");
        var sources = new Dictionary<string, string>();
        foreach (var pair in new Dictionary<string, string>
        {
            ["Aot/Boot.dll"] = "final player metadata", ["RuntimeAot/Boot.dll"] = "runtime metadata",
            ["Dlls/Boot.dll.bytes"] = "runtime metadata", ["Dlls/Game.dll.bytes"] = "hot code",
            ["Dlls/HybridCLRAssemblyList.bytes"] = "Boot.dll\n", ["Builtin/catalog.bytes"] = "builtin",
            ["Generated/link.xml"] = "link", ["NativeGenerated/MethodBridge.cpp"] = "bridge",
            ["Host/host.apk"] = "fixture host"
        })
        {
            string file = Path.Combine(sourcesRoot, pair.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, pair.Value);
            sources.Add(pair.Key, file);
        }
        string input = Path.Combine(root, "Boot.cs");
        File.WriteAllText(input, "stable boot source");
        BaselineFile[] inputs = { HostBaselineArchive.Describe(input, "Assets/Boot/Boot.cs") };
        HostBaselineManifest Manifest(string id) => new HostBaselineManifest
        {
            Id = id, Platform = "Android", UnityVersion = "2022.3.55f1c1", BuildProfile = "test-profile",
            Commit = new string('a', 40), ResourceVersion = "20261017000000", AppVersion = "0.1.1", AppVersionCode = 2,
            Inputs = inputs, NativeInteropContract = ""
        };
        string archive = null;
        test("归档保存实际文件且禁止覆盖同编号", () =>
        {
            archive = HostBaselineArchive.Create(data, Manifest("host-1"), sources);
            var saved = HostBaselineArchive.Validate(archive, "Android", "host-1");
            Check(saved.Files.Length == sources.Count, "归档文件不完整");
            Reject(() => HostBaselineArchive.Create(data, Manifest("host-1"), sources));
        });
        test("原工作区文件清除后可恢复到另一目录且路径可重定位", () =>
        {
            foreach (string file in sources.Values) File.Delete(file);
            string restored = HostBaselineArchive.Restore(data, "Android", "host-1", Path.Combine(root, "newCheckout", "Library"),
                "2022.3.55f1c1", "test-profile", inputs);
            Check(File.ReadAllText(Path.Combine(restored, "RuntimeAot", "Boot.dll")) == "runtime metadata", "补充 AOT 恢复错误");
            Check(File.ReadAllText(Path.Combine(restored, "Aot", "Boot.dll")) == "final player metadata", "宿主 AOT 恢复错误");
            File.WriteAllText(Path.Combine(restored, "Aot", "Boot.dll"), "local mutation");
            Check(HostBaselineArchive.Validate(archive, "Android", "host-1") != null, "修改恢复副本影响原归档");
        });
        test("Boot 指纹变化在写恢复目录前拒绝", () =>
        {
            string target = Path.Combine(root, "incompatibleRestore");
            var changed = new[] { new BaselineFile { Path = inputs[0].Path, Sha256 = "changed" } };
            Reject(() => HostBaselineArchive.Restore(data, "Android", "host-1", target, "2022.3.55f1c1", "test-profile", changed));
            Check(!Directory.Exists(target), "不兼容仍创建恢复目录");
        });
        test("Unity 平台和构建配置不匹配均拒绝", () =>
        {
            var manifest = HostBaselineArchive.Validate(archive, "Android", "host-1");
            Reject(() => HostBaselineArchive.CheckCompatibility(manifest, "other-unity", "test-profile", inputs));
            Reject(() => HostBaselineArchive.CheckCompatibility(manifest, "2022.3.55f1c1", "other-profile", inputs));
            Reject(() => HostBaselineArchive.Validate(archive, "StandaloneWindows64", "host-1"));
        });
        test("输入文件集合改变要求重打首包", () =>
        {
            var manifest = HostBaselineArchive.Validate(archive, "Android", "host-1");
            Reject(() => HostBaselineArchive.CheckCompatibility(manifest, "2022.3.55f1c1", "test-profile", Array.Empty<BaselineFile>()));
        });
        test("归档 AOT 被篡改后拒绝恢复", () =>
        {
            string file = Path.Combine(archive, "Aot", "Boot.dll");
            string original = File.ReadAllText(file);
            File.WriteAllText(file, "tampered");
            Reject(() => HostBaselineArchive.Restore(data, "Android", "host-1", Path.Combine(root, "corruptRestore"),
                "2022.3.55f1c1", "test-profile", inputs));
            File.WriteAllText(file, original);
        });
        test("缺失或额外归档文件不被接受", () =>
        {
            string extra = Path.Combine(archive, "extra.txt");
            File.WriteAllText(extra, "extra");
            Reject(() => HostBaselineArchive.Validate(archive, "Android", "host-1"));
            File.Delete(extra);
            string file = Path.Combine(archive, "RuntimeAot", "Boot.dll");
            string original = File.ReadAllText(file);
            File.Delete(file);
            Reject(() => HostBaselineArchive.Validate(archive, "Android", "host-1"));
            File.WriteAllText(file, original);
        });
        test("清单损坏与路径穿越均拒绝", () =>
        {
            string file = Path.Combine(archive, "manifest.json");
            string original = File.ReadAllText(file);
            File.WriteAllText(file, original + " ");
            Reject(() => HostBaselineArchive.Validate(archive, "Android", "host-1"));
            File.WriteAllText(file, original);
            Reject(() => BuildDataStore.Child(archive, "../escape"));
            Reject(() => HostBaselineArchive.Restore(data, "Android", "../host-1", Path.Combine(root, "unsafe"),
                "2022.3.55f1c1", "test-profile", inputs));
        });
    }
}
