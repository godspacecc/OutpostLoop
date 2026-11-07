#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HybridCLR.Editor;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace Game.Editor
{
    public sealed class HostInputSnapshot
    {
        public BaselineFile[] Inputs;
        public string Commit;
        public bool SourceDirty;
    }

    /// <summary>将真实工程输入、不可变归档及本次工作目录连接起来，不执行 Player Build。</summary>
    public static class HostBaselineUtility
    {
        private static string ClientRoot => Directory.GetParent(Application.dataPath).FullName;
        private static string Profile(BuildTarget target) => target == BuildTarget.Android
            ? "Android-ARM64-IL2CPP-Development-Host-v1" : "Windows-x64-IL2CPP-Release-v1";

        internal static BaselineFile[] CaptureInputs()
        {
            string client = ClientRoot;
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string path)
            {
                string full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(client, path));
                if (!BuildDataStore.IsWithin(full, client)) throw new InvalidOperationException("宿主依赖位于工程外，需先配置可移植的依赖归档：" + full);
                if (!File.Exists(full)) throw new FileNotFoundException("宿主输入不存在。", full);
                paths.Add(full);
                if (File.Exists(full + ".meta")) paths.Add(full + ".meta");
            }
            foreach (string directory in new[] { "ProjectSettings", "Packages", "Assets/Boot", "Assets/Plugins" })
                foreach (string file in HostBaselineArchive.Enumerate(Path.Combine(client, directory))) Add(file);
            Add("Assets/StreamingAssets/hostConfig.json");
            foreach (string settings in HostBaselineArchive.Enumerate(Path.Combine(client, "ProjectSettings")))
                foreach (Match match in Regex.Matches(File.ReadAllText(settings), @"\b[a-f0-9]{32}\b"))
                {
                    string asset = AssetDatabase.GUIDToAssetPath(match.Value);
                    if (string.IsNullOrEmpty(asset) || !File.Exists(Path.Combine(client, asset))) continue;
                    foreach (string dependency in AssetDatabase.GetDependencies(asset, true))
                        if (File.Exists(Path.Combine(client, dependency))) Add(dependency);
                }
            foreach (string file in AssetDatabase.GetDependencies("Assets/Game/Scenes/Boot.unity", true))
                if (File.Exists(Path.Combine(client, file))) Add(file);
            foreach (var assembly in CompilationPipeline.GetAssemblies(AssembliesType.Player))
            {
                if (assembly.name == "Game") continue;
                foreach (string file in assembly.sourceFiles)
                {
                    string relative = HostBaselineArchive.Relative(client,
                        Path.GetFullPath(Path.IsPathRooted(file) ? file : Path.Combine(client, file)));
                    if (!relative.StartsWith("Assets/HybridCLRGenerate/", StringComparison.Ordinal)) Add(file);
                }
                foreach (string reference in assembly.compiledAssemblyReferences)
                    if (BuildDataStore.IsWithin(reference, client) &&
                        !BuildDataStore.IsWithin(reference, Path.Combine(client, "Library", "ScriptAssemblies"))) Add(reference);
            }
            foreach (string file in HostBaselineArchive.Enumerate(Application.dataPath))
            {
                string extension = Path.GetExtension(file);
                if (extension == ".asmdef" || extension == ".asmref" || extension == ".rsp" ||
                    file.Replace('\\', '/').Contains("/Resources/")) Add(file);
            }
            string packageCache = Path.Combine(client, "Library", "PackageCache");
            if (Directory.Exists(packageCache))
                foreach (string file in HostBaselineArchive.Enumerate(packageCache))
                    if (new[] { ".dll", ".so", ".a", ".dylib", ".bundle" }.Contains(Path.GetExtension(file).ToLowerInvariant())) Add(file);
            return paths.OrderBy(x => x, StringComparer.Ordinal).Select(file =>
            {
                string relative = HostBaselineArchive.Relative(client, file);
                if (relative != "ProjectSettings/ProjectSettings.asset") return HostBaselineArchive.Describe(file, relative);
                // 应用版本由独立台账分配，不影响 Boot/AOT 宿主兼容性；其余设置保守地全部比较。
                string text = Regex.Replace(File.ReadAllText(file).Replace("\r\n", "\n"),
                    @"(?m)^\s*(bundleVersion|AndroidBundleVersionCode):[^\n]*\n?", "");
                using var hash = SHA256.Create();
                return new BaselineFile { Path = relative, Length = text.Length,
                    Sha256 = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant() };
            }).ToArray();
        }

        internal static HostInputSnapshot Capture() => new HostInputSnapshot
        {
            Inputs = CaptureInputs(), Commit = ReadCommit(),
            SourceDirty = !string.IsNullOrEmpty(ReadGit("status --porcelain --untracked-files=all"))
        };

        public static string Archive(BuildArtifacts artifacts, HostInputSnapshot original)
        {
            using var lease = WorkspaceBuildLock.Acquire(ClientRoot);
            if (original == null || artifacts.Target != EditorUserBuildSettings.activeBuildTarget)
                throw new InvalidOperationException("归档要求本次构建的输入快照及正确目标平台。");
            if (string.IsNullOrEmpty(artifacts.PlayerPath)) throw new InvalidOperationException("只有成功首包可建立宿主基线。");
            var manifest = new HostBaselineManifest
            {
                Id = artifacts.Target + "-" + artifacts.AppVersionCode + "-" + Guid.NewGuid().ToString("N"),
                Platform = artifacts.Target.ToString(), UnityVersion = Application.unityVersion,
                BuildProfile = Profile(artifacts.Target), Commit = original.Commit,
                SourceDirty = original.SourceDirty || !string.IsNullOrEmpty(ReadGit("status --porcelain --untracked-files=all")),
                AppVersion = artifacts.AppVersion, AppVersionCode = artifacts.AppVersionCode,
                ResourceVersion = artifacts.ResourceVersion, Inputs = CaptureInputs(),
                NativeInteropContract = HybridCLRBuildUtility.GetNativeInteropContract(
                    Path.Combine(HybridCLRBuildUtility.GetGameDllDirectory(), "Game.dll.bytes"))
            };
            if (ReadCommit() != original.Commit) throw new InvalidOperationException("构建期间代码 Commit 改变，拒绝建立宿主基线。");
            HostBaselineArchive.CheckCompatibility(manifest, Application.unityVersion, Profile(artifacts.Target), original.Inputs);
            var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            void DirectoryFiles(string label, string source)
            {
                foreach (string file in HostBaselineArchive.Enumerate(source))
                    sources.Add(label + "/" + HostBaselineArchive.Relative(source, file), file);
            }
            DirectoryFiles("Aot", HybridCLRBuildUtility.GetAotDirectory(artifacts.Target));
            DirectoryFiles("Dlls", HybridCLRBuildUtility.GetGameDllDirectory());
            // 最终 Player 的裁剪元数据用于兼容检查；安装时携带的补充元数据单独保存，恢复不重写为另一份 DLL。
            foreach (string name in BuildStateStore.ReadRuntimeList(HybridCLRBuildUtility.GetRuntimeListPath()))
                sources.Add("RuntimeAot/" + name, Path.Combine(HybridCLRBuildUtility.GetGameDllDirectory(), name + ".bytes"));
            DirectoryFiles("Builtin", YooAssetBuildUtility.GetStreamingAssetsDirectory());
            DirectoryFiles("Generated", Path.Combine(ClientRoot, "Assets", "HybridCLRGenerate"));
            DirectoryFiles("NativeGenerated", SettingsUtil.GeneratedCppDir);
            sources.Add("Host/" + Path.GetFileName(artifacts.UpdateManifestPath), artifacts.UpdateManifestPath);
            if (artifacts.Target == BuildTarget.Android)
                sources.Add("Host/" + Path.GetFileName(artifacts.PlayerPath), artifacts.PlayerPath);
            else
            {
                var installer = Boot.WindowsUpdateManifest.ParseAvailable(File.ReadAllText(artifacts.UpdateManifestPath), 0);
                sources.Add("Host/" + installer.installerFileName, Path.Combine(artifacts.InstallerDirectory, installer.installerFileName));
                sources.Add("Host/windows-release.json", Path.Combine(Path.GetDirectoryName(artifacts.PlayerPath),
                    "OutpostLoop_Data", "StreamingAssets", "windows-release.json"));
            }
            string archive = HostBaselineArchive.Create(BuildDataStore.Root, manifest, sources);
            artifacts.HostBaselineId = manifest.Id;
            CiBuildProgress.Artifact("BaselineManifest", Path.Combine(archive, "manifest.json"));
            var state = BuildStateStore.Load(out string warning);
            if (!string.IsNullOrEmpty(warning)) throw new InvalidOperationException(warning);
            var record = Record(archive, artifacts.Target, manifest.Id);
            if (artifacts.Target == BuildTarget.Android)
            {
                state.LastAndroidBaselineId = manifest.Id;
                state.LastAndroidAotBaseline = record;
                if (state.LastAndroidHost != null) state.LastAndroidHost.BaselineId = manifest.Id;
            }
            else
            {
                state.LastWindowsBaselineId = manifest.Id;
                state.LastAotBaseline = record;
            }
            BuildStateStore.Save(state);
            return manifest.Id;
        }

        /// <summary>先校验兼容性，再恢复到新的本地目录并重定位状态；缺失时不回退旧残留文件。</summary>
        public static HostBaselineManifest Restore(BuildTarget target, string baselineId = null)
        {
            using var lease = WorkspaceBuildLock.Acquire(ClientRoot);
            if (EditorUserBuildSettings.activeBuildTarget != target || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("恢复基线要求正确目标平台及空闲编辑器。");
            var state = BuildStateStore.Load(out string warning);
            if (!string.IsNullOrEmpty(warning))
            {
                if (string.IsNullOrWhiteSpace(baselineId)) throw new InvalidOperationException(warning);
                UnityEngine.Debug.LogWarning(warning + "；将从明确选择的归档重建本地状态。");
            }
            baselineId = baselineId ?? (target == BuildTarget.Android ? state.LastAndroidBaselineId : state.LastWindowsBaselineId);
            if (string.IsNullOrWhiteSpace(baselineId)) throw new InvalidOperationException("未指定已归档宿主基线，请选择基线编号或先生成首包。");
            string directory = HostBaselineArchive.Restore(BuildDataStore.Root, target.ToString(), baselineId,
                Path.Combine(ClientRoot, "Library", "OutpostLoopBuild", "Restored"), Application.unityVersion, Profile(target), CaptureInputs());
            var manifest = HostBaselineArchive.Validate(directory, target.ToString(), baselineId);
            var record = Record(directory, target, baselineId);
            RestoreBuiltin(Path.Combine(directory, "Builtin"));
            if (target == BuildTarget.Android)
            {
                state.LastAndroidAotBaseline = record;
                state.LastAndroidBaselineId = baselineId;
            }
            else
            {
                state.LastAotBaseline = record;
                state.LastWindowsBaselineId = baselineId;
            }
            BuildStateStore.Save(state);
            AssetDatabase.Refresh();
            return manifest;
        }

        private static AotBaselineRecord Record(string directory, BuildTarget target, string id) => new AotBaselineRecord
        {
            BuildTarget = target.ToString(), BaselineId = id,
            AotDirectory = Path.Combine(directory, "RuntimeAot"), HostAotDirectory = Path.Combine(directory, "Aot"),
            GameDllDirectory = Path.Combine(directory, "Dlls"),
            RuntimeListPath = Path.Combine(directory, "Dlls", "HybridCLRAssemblyList.bytes"), CompletedUtcTicks = DateTime.UtcNow.Ticks
        };

        private static void RestoreBuiltin(string source)
        {
            string target = Path.GetFullPath(YooAssetBuildUtility.GetStreamingAssetsDirectory());
            if (!BuildDataStore.IsWithin(target, Path.Combine(Application.dataPath, "StreamingAssets")))
                throw new InvalidOperationException("内置资源恢复目录越界。");
            BuildDataStore.NoLinks(target);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            string pending = target + "." + Guid.NewGuid().ToString("N") + ".pending";
            string backup = target + "." + Guid.NewGuid().ToString("N") + ".backup";
            try
            {
                Directory.CreateDirectory(pending);
                foreach (string file in HostBaselineArchive.Enumerate(source))
                {
                    string destination = BuildDataStore.Child(pending, HostBaselineArchive.Relative(source, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    File.Copy(file, destination);
                }
                if (Directory.Exists(target)) Directory.Move(target, backup);
                try { Directory.Move(pending, target); }
                catch { if (Directory.Exists(backup)) Directory.Move(backup, target); throw; }
                if (Directory.Exists(backup)) Directory.Delete(backup, true);
            }
            finally { if (Directory.Exists(pending)) Directory.Delete(pending, true); }
        }

        internal static string ReadCommit(string gitExecutable = null)
        {
            string output = ReadGit("rev-parse HEAD", gitExecutable);
            if (!Regex.IsMatch(output, @"\A[0-9a-f]{40,64}\z")) throw new InvalidOperationException("构建 Commit 无效。");
            return output;
        }

        internal static void ValidateCurrentInputs(HostBaselineManifest manifest) =>
            HostBaselineArchive.CheckCompatibility(manifest, Application.unityVersion,
                Profile((BuildTarget)Enum.Parse(typeof(BuildTarget), manifest.Platform)), CaptureInputs());

        internal static string ReadGit(string arguments, string gitExecutable = null)
        {
            var info = new ProcessStartInfo(gitExecutable ?? CiToolConfiguration.Current?.GitExecutable ?? "git", "-C \"" + Directory.GetParent(ClientRoot).FullName + "\" " + arguments)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using var process = Process.Start(info);
            if (process == null) throw new InvalidOperationException("无法读取构建 Commit。");
            string output = process.StandardOutput.ReadToEnd().Trim();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException("读取构建 Commit 失败：" + error);
            return output;
        }
    }
}
#endif
