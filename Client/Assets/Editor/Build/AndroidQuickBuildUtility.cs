#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>同一轮完整包或启动包已经准备好的输入快照；发布失败仍可用于只重建 APK。</summary>
    [Serializable]
    public sealed class AndroidQuickBuildInputs
    {
        /// <summary>完整远端资源版本，快速入口不得另建版本。</summary>
        public string Version;
        /// <summary>可信记录格式，旧记录不能推断为可靠基线。</summary>
        public int Schema = 2;
        /// <summary>限制使用 Android 本平台记录。</summary>
        public string Platform = "Android";
        /// <summary>保留完整包或启动包类型。</summary>
        public string Kind;
        /// <summary>本项目 Android 完整远端资源目录。</summary>
        public string PackageDirectory;
        /// <summary>这轮生成对应的 Boot 源输入指纹。</summary>
        public string BootFingerprint;
        /// <summary>运行时代码、AOT、生成文件、资源及内置清单的内容指纹。</summary>
        public string Fingerprint;
        /// <summary>归一化构建设置指纹；仅允许 APK 应用版本变化。</summary>
        public string SettingsFingerprint;
        /// <summary>生成此快照时的 Unity 版本。</summary>
        public string UnityVersion;
        /// <summary>从资源内补充元数据复制的不可变 AOT 目录，不受 Player 构建清空现场目录影响。</summary>
        public string AotDirectory;
        /// <summary>冻结底层输入与桥接，允许玩法资源变化。</summary>
        public string HostFingerprint;
        /// <summary>冻结目录内容，检测缺失和篡改。</summary>
        public string FrozenFingerprint;
    }

    /// <summary>快速 APK 的只读复用校验与快照保存；不调用任何构建或发布入口。</summary>
    public static class AndroidQuickBuildUtility
    {
        private const string RebuildHint = "请执行 Tools/打包/Android/完整打包 或 启动包，重新生成匹配的 AOT 和资源。";
        private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        private static string RecordPath => Path.Combine(ProjectRoot, "Library", "OutpostLoopBuild", "AndroidQuickInputs.json");

        /// <summary>完整首包资源验证成功后立即保存，最终 APK 或发布失败也不会丢失这轮输入。</summary>
        public static AndroidQuickBuildInputs Capture(string version, string packageDirectory, IReadOnlyDictionary<string, string> compilationFingerprint, string generatedFingerprint, string kind = "SmallHostBase")
        {
            RequireSameInputs(compilationFingerprint, CaptureCompilationFingerprint());
            if (generatedFingerprint != CaptureGeneratedFingerprint())
                throw new InvalidOperationException("资源构建期间 DLL、AOT 清单或生成桥接发生变化，不能保存准备记录。");
            string aot = PreserveAotMetadata(version, HybridCLRBuildUtility.GetRuntimeListPath(), HybridCLRBuildUtility.GetGameDllDirectory(),
                Path.Combine(ProjectRoot, "Library", "OutpostLoopBuild", "AndroidQuickAot"));
            AndroidQuickAotUtility.Preserve(aot);
            var record = ReadCurrent(version, packageDirectory, aot, kind);
            RequireSameInputs(compilationFingerprint, CaptureCompilationFingerprint());
            if (generatedFingerprint != CaptureGeneratedFingerprint())
                throw new InvalidOperationException("登记快照期间源码、资源源文件或生成输入发生变化。");
            string immutableRecord = aot + ".json";
            string json = JsonUtility.ToJson(record, true);
            if (File.Exists(immutableRecord) && File.ReadAllText(immutableRecord) != json)
                throw new InvalidOperationException("不可变准备记录不允许覆盖。");
            if (!File.Exists(immutableRecord)) File.WriteAllText(immutableRecord, json);
            Directory.CreateDirectory(Path.GetDirectoryName(RecordPath));
            string pending = RecordPath + ".pending";
            try
            {
                File.WriteAllText(pending, JsonUtility.ToJson(record, true));
                if (File.Exists(RecordPath)) File.Replace(pending, RecordPath, null);
                else File.Move(pending, RecordPath);
            }
            finally { if (File.Exists(pending)) File.Delete(pending); }
            return record;
        }

        /// <summary>只接受完整准备阶段保存的可信内容快照，旧流程遗留产物须重新准备。</summary>
        public static AndroidQuickBuildInputs LoadAndValidate()
        {
            if (File.Exists(RecordPath))
            {
                AndroidQuickBuildInputs saved = JsonUtility.FromJson<AndroidQuickBuildInputs>(File.ReadAllText(RecordPath));
                if (saved == null) throw new InvalidOperationException("快速打包快照损坏。" + RebuildHint);
                Validate(saved);
                return saved;
            }

            throw new InvalidOperationException("缺少可信准备记录，不能由时间戳推断可复用性。" + RebuildHint);
        }

        /// <summary>拒绝旧格式、跨平台及渠道记录。</summary>
        public static void ValidateIdentity(int schema, string platform, string kind)
        {
            if (schema != 2 || platform != "Android" || (kind != "HostBase" && kind != "SmallHostBase"))
                throw new InvalidOperationException("准备记录格式、平台或包型不适用于快速测试包。" + RebuildHint);
        }

        /// <summary>记录路径必须属于当前项目、平台和资源版本。</summary>
        public static void ValidatePreparedPaths(string version, string package, string aot, string packageRoot, string cacheRoot)
        {
            if (string.IsNullOrEmpty(version) || version.Any(c => c < '0' || c > '9') ||
                string.IsNullOrEmpty(package) || string.IsNullOrEmpty(aot) ||
                !string.Equals(Path.GetFullPath(package), Path.GetFullPath(Path.Combine(packageRoot, version)), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFullPath(aot), Path.GetFullPath(Path.Combine(cacheRoot, version)), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("准备记录路径与当前平台版本不一致。");
        }
        /// <summary>索引记录必须与该版本不可变原始记录一致。</summary>
        private static void ValidateRecord(AndroidQuickBuildInputs saved)
        {
            ValidateIdentity(saved.Schema, saved.Platform, saved.Kind);
            ValidatePreparedPaths(saved.Version, saved.PackageDirectory, saved.AotDirectory, YooAssetBuildUtility.GetPackageRoot(BuildTarget.Android),
                Path.Combine(ProjectRoot, "Library", "OutpostLoopBuild", "AndroidQuickAot"));
            string immutableRecord = saved.AotDirectory + ".json";
            if (!File.Exists(immutableRecord) || File.ReadAllText(immutableRecord) != JsonUtility.ToJson(saved, true))
                throw new InvalidOperationException("准备记录原件缺失或索引内容已改变。");
        }

        /// <summary>复用 AOT 时只检查不可变底层，不要求玩法和资源保持原样。</summary>
        public static AndroidQuickBuildInputs LoadForAotReuse()
        {
            if (!File.Exists(RecordPath)) throw new InvalidOperationException("没有可信准备记录。" + RebuildHint);
            var saved = JsonUtility.FromJson<AndroidQuickBuildInputs>(File.ReadAllText(RecordPath));
            if (saved == null) throw new InvalidOperationException("准备记录损坏。");
            ValidateRecord(saved);
            if (saved.UnityVersion != Application.unityVersion || saved.SettingsFingerprint != SettingsFingerprint() ||
                saved.HostFingerprint != AndroidQuickAotUtility.HostFingerprint() ||
                saved.FrozenFingerprint != FingerprintFiles(Directory.GetFiles(saved.AotDirectory)))
                throw new InvalidOperationException("底层、桥接或冻结 AOT 已变化。" + RebuildHint);
            return saved;
        }

        /// <summary>在本轮 Game DLL 编译前登记手写输入，正常生成的 HybridCLR 文件单独在资源完成后登记。</summary>
        public static IReadOnlyDictionary<string, string> CaptureCompilationFingerprint() => SnapshotInputs(
            ProjectRoot, RuntimeInputs().Where(path => !IsGeneratedSource(path) &&
                Path.GetFileName(path) != Boot.HostCompatibilityDescriptor.EmbeddedFileName &&
                Path.GetFileName(path) != Boot.HostCompatibilityDescriptor.EmbeddedFileName + ".meta").ToArray());

        /// <summary>单次读取每项输入，用工程相对路径保留可比较的内容摘要。</summary>
        internal static IReadOnlyDictionary<string, string> SnapshotInputs(string root, string[] files)
        {
            var settings = Path.Combine(root, "ProjectSettings");
            var entries = new SortedDictionary<string, string>(StringComparer.Ordinal);
            using var hash = SHA256.Create();
            foreach (var file in files.Concat(Directory.GetFiles(settings, "*", SearchOption.AllDirectories))
                         .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var key = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (key == "ProjectSettings/ProjectSettings.asset")
                    entries[key] = Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(NormalizedPlayerSettings(file))));
                else
                {
                    using var input = File.OpenRead(file);
                    entries[key] = Convert.ToBase64String(hash.ComputeHash(input));
                }
            }
            return entries;
        }

        /// <summary>阻止复用发生变化的输入，同时报告具体路径；重命名显示为删除和新增。</summary>
        internal static void RequireSameInputs(IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after)
        {
            var changes = new List<string>();
            foreach (var item in before)
            {
                if (!after.TryGetValue(item.Key, out var current)) changes.Add("删除：" + item.Key);
                else if (current != item.Value) changes.Add("修改：" + item.Key);
            }
            foreach (var item in after)
                if (!before.ContainsKey(item.Key)) changes.Add("新增：" + item.Key);
            if (changes.Count > 0)
                throw new InvalidOperationException($"构建期间 {changes.Count} 个输入文件发生变化，已停止复用。\n" +
                    string.Join("\n", changes));
        }

        /// <summary>资源构建前绑定实际进入资源包的 DLL、AOT 清单与生成桥接，防止打包期间替换输入。</summary>
        public static string CaptureGeneratedFingerprint() => FingerprintFiles(
            Directory.GetFiles(HybridCLRBuildUtility.GetGameDllDirectory(), "*", SearchOption.AllDirectories)
                .Concat(GeneratedInputs()).ToArray());

        /// <summary>旧构建恢复及快照建立前验证源码不晚于其 DLL；防止编译后、最终 Player 前改码被误登记。</summary>
        private static void ValidateCompiledSourceTimes()
        {
            string directory = Path.Combine(ProjectRoot, "HybridCLRData", "HotUpdateDlls", "Android");
            foreach (var assembly in CompilationPipeline.GetAssemblies(AssembliesType.Player)
                .Where(assembly => !assembly.name.Contains("Test") && (assembly.flags & AssemblyFlags.EditorAssembly) == 0))
            {
                // 热更 Game 使用资源内 DLL 的编译输出；AOT 程序集使用实际 Player 编译缓存，
                // HybridCLR HotUpdateDlls 中的第三方程序集可能是旧缓存，并非最终 Player 使用的版本。
                string dll = assembly.name == "Game" ? Path.Combine(directory, "Game.dll") :
                    Path.Combine(ProjectRoot, "Library", "Bee", "PlayerScriptAssemblies", assembly.name + ".dll");
                if (!File.Exists(dll)) throw new FileNotFoundException("缺少对应的 Android 编译程序集。", dll);
                ValidateRecoveryInputTimes(assembly.sourceFiles.Where(path => !path.Replace('\\', '/').Contains("/HybridCLRGenerate/")).ToArray(),
                    File.GetLastWriteTimeUtc(dll));
            }
            // 编译配置和插件也必须早于本轮热更程序集；新增快照另外使用内容指纹覆盖整个耗时窗口。
            string game = Path.Combine(directory, "Game.dll");
            ValidateRecoveryInputTimes(RuntimeInputs().Where(path => !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
                !path.Replace('\\', '/').Contains("/HybridCLRGenerate/")).ToArray(), File.GetLastWriteTimeUtc(game));
        }

        /// <summary>在构建前及发布前复核输入，禁止将长流程中修改后的源码登记成旧 AOT 基线。</summary>
        public static void Validate(AndroidQuickBuildInputs saved)
        {
            ValidateRecord(saved);
            var current = ReadCurrent(saved.Version, saved.PackageDirectory, saved.AotDirectory, saved.Kind);
            if (saved.HostFingerprint != current.HostFingerprint || saved.FrozenFingerprint != current.FrozenFingerprint || saved.UnityVersion != current.UnityVersion || saved.Fingerprint != current.Fingerprint ||
                saved.BootFingerprint != current.BootFingerprint || saved.SettingsFingerprint != current.SettingsFingerprint)
                throw new InvalidOperationException("AOT、代码、资源或构建设置已变化，不能复用本次结果。" + RebuildHint);
        }

        /// <summary>校验同平台清单、完整资源及 DLL 对应关系后计算内容快照。</summary>
        private static AndroidQuickBuildInputs ReadCurrent(string version, string packageDirectory, string aotDirectory, string kind)
        {
            string expected = Path.GetFullPath(Path.Combine(YooAssetBuildUtility.GetPackageRoot(BuildTarget.Android), version ?? ""));
            if (string.IsNullOrEmpty(version) || version.IndexOfAny(new[] { '/', '\\', '.' }) >= 0 ||
                !string.Equals(Path.GetFullPath(packageDirectory), expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("快速打包资源必须来自本项目 Android 版本目录。");
            if ((kind == "SmallHostBase" && !YooAssetBuildUtility.ValidateManifestOnlyBuiltinPackage(version)) || !YooAssetBuildUtility.ValidateBuildResult(expected, version))
                throw new InvalidOperationException("首包资源或内置清单无效。" + RebuildHint);
            PcBuiltinResourceValidator.CaptureRemote(expected, version);
            string builtin = YooAssetBuildUtility.GetStreamingAssetsDirectory();
            if (kind == "HostBase") PcBuiltinResourceValidator.Capture(builtin, version);
            else PcBuiltinResourceValidator.CaptureStartup(builtin, expected, version);
            if (kind == "HostBase" && !YooAssetBuildUtility.ValidateBuildResult(builtin, version))
                throw new InvalidOperationException("完整包内置资源无效。");
            foreach (string extension in new[] { ".bytes", ".hash" })
                RequireSameFile(Path.Combine(builtin, "OutpostLoop_" + version + extension), Path.Combine(expected, "OutpostLoop_" + version + extension));
            HybridCLRBuildUtility.ValidateGeneratedDlls();
            string aot = aotDirectory;
            if (string.IsNullOrEmpty(aot) || !Directory.Exists(aot))
                throw new InvalidOperationException("不可变 AOT 基线缺失。" + RebuildHint);
            foreach (string name in File.ReadAllLines(HybridCLRBuildUtility.GetRuntimeListPath()).Where(name => !string.IsNullOrWhiteSpace(name)))
                RequireSameFile(Path.Combine(aot, name.Trim()), Path.Combine(HybridCLRBuildUtility.GetGameDllDirectory(), name.Trim() + ".bytes"));
            RequireSameFile(Path.Combine(ProjectRoot, "HybridCLRData", "HotUpdateDlls", "Android", "Game.dll"),
                Path.Combine(HybridCLRBuildUtility.GetGameDllDirectory(), "Game.dll.bytes"));
            string generated = Path.GetFullPath(HybridCLR.Editor.SettingsUtil.GeneratedCppDir);
            if (!File.Exists(Path.Combine(generated, "MethodBridge.cpp")))
                throw new InvalidOperationException("缺少 HybridCLR 桥接代码。" + RebuildHint);
            var files = RuntimeInputs().Concat(Directory.GetFiles(aot, "*.dll"))
                .Concat(GeneratedInputs())
                .Concat(Directory.GetFiles(HybridCLRBuildUtility.GetGameDllDirectory(), "*.bytes"))
                .Concat(Directory.GetFiles(Path.Combine(ProjectRoot, "Assets", "StreamingAssets"), "*", SearchOption.AllDirectories))
                .Concat(Directory.GetFiles(expected, "*", SearchOption.AllDirectories));
            return new AndroidQuickBuildInputs
            {
                Kind = kind, Version = version, PackageDirectory = expected, UnityVersion = Application.unityVersion, AotDirectory = aot,
                BootFingerprint = AndroidBootBuildGuard.ComputeFingerprint(ProjectRoot), Fingerprint = FingerprintFiles(files.ToArray()),
                HostFingerprint = AndroidQuickAotUtility.HostFingerprint(), FrozenFingerprint = FingerprintFiles(Directory.GetFiles(aot)),
                SettingsFingerprint = SettingsFingerprint()
            };
        }

        /// <summary>收集 Player 源码与 Boot 场景依赖，排除编辑器及测试源码，防止改代码后复用旧热更 DLL。</summary>
        private static string[] RuntimeInputs() => SourceAssetInputs()
            .Concat(AndroidQuickAotUtility.CollectHostAssetInputs(ProjectRoot, Array.Empty<string>()))
            .Concat(CompilationPipeline.GetAssemblies(AssembliesType.Player)
            .Where(assembly => !assembly.name.Contains("Test") && (assembly.flags & AssemblyFlags.EditorAssembly) == 0)
            .SelectMany(assembly => assembly.sourceFiles)
            .Concat(Directory.GetFiles(Path.Combine(ProjectRoot, "Assets", "Boot"), "*", SearchOption.AllDirectories))
            .Concat(AssetDatabase.GetDependencies("Assets/Game/Scenes/Boot.unity", true).Where(File.Exists))
            .Concat(Directory.GetFiles(Path.Combine(ProjectRoot, "Assets"), "*", SearchOption.AllDirectories)
                .Where(path => !path.Replace('\\', '/').Contains("/Editor/") && !path.Replace('\\', '/').Contains("/Tests/") &&
                    new[] { ".asmdef", ".asmref", ".rsp", ".dll" }.Contains(Path.GetExtension(path).ToLowerInvariant())))
            .Concat(Directory.GetFiles(Path.Combine(ProjectRoot, "Assets"), "*HybridCLR*.asset", SearchOption.AllDirectories))
            .Concat(new[] { Path.Combine(ProjectRoot, "Packages", "manifest.json"), Path.Combine(ProjectRoot, "Packages", "packages-lock.json") })
            .SelectMany(path => File.Exists(path + ".meta") ? new[] { path, path + ".meta" } : new[] { path })
            .Select(Path.GetFullPath)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        /// <summary>生成目录及根meta由生成器维护，不属于准备前手写输入。</summary>
        private static bool IsGeneratedSource(string path)
        {
            string normalized = path.Replace('\\', '/');
            return normalized.Contains("/HybridCLRGenerate/") || normalized.EndsWith("/HybridCLRGenerate.meta", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>资源源文件变化同样使已准备资源失效；排除构建器正常生成的产物。</summary>
        private static string[] SourceAssetInputs() => Directory.GetFiles(Path.Combine(ProjectRoot, "Assets"), "*", SearchOption.AllDirectories)
            .Where(path => !path.Replace('\\', '/').Contains("/StreamingAssets/") &&
                !path.Replace('\\', '/').EndsWith("/StreamingAssets.meta", StringComparison.OrdinalIgnoreCase) &&
                !IsGeneratedSource(path) &&
                !path.Replace('\\', '/').Contains("/Editor/") && !path.Replace('\\', '/').Contains("/Tests/") &&
                !path.StartsWith(HybridCLRBuildUtility.GetGameDllDirectory(), StringComparison.OrdinalIgnoreCase)).ToArray();

        /// <summary>校验 APK 实际资源的完整清单及字节，拒绝漏文件、额外文件及陈旧文件。</summary>
        public static void ValidateApkResources(string apk, string builtin)
        {
            const string prefix = "assets/yoo/OutpostLoop/";
            var files = Directory.GetFiles(builtin, "*", SearchOption.AllDirectories)
                .Where(p => !p.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)).ToArray();
            using (var archive = ZipFile.OpenRead(apk))
            {
                var entries = archive.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal) && !e.FullName.EndsWith("/")).ToArray();
                if (entries.Length != files.Length) throw new InvalidOperationException("APK 内置资源数量不符。");
                foreach (string file in files)
                {
                    string name = prefix + file.Substring(builtin.TrimEnd(Path.DirectorySeparatorChar).Length + 1).Replace('\\', '/');
                    var entry = entries.SingleOrDefault(e => e.FullName == name);
                    if (entry == null) throw new InvalidOperationException("APK 缺少资源：" + name);
                    using (var input = entry.Open()) using (var buffer = new MemoryStream())
                    {
                        input.CopyTo(buffer);
                        if (!buffer.ToArray().SequenceEqual(File.ReadAllBytes(file))) throw new InvalidOperationException("APK 资源内容不符：" + name);
                    }
                }
            }
        }

        /// <summary>桥接与链接清单也是编译输入，必须随快照校验并参与旧产物恢复门禁。</summary>
        private static string[] GeneratedInputs() => Directory.GetFiles(Path.GetFullPath(HybridCLR.Editor.SettingsUtil.GeneratedCppDir), "*", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(ProjectRoot, "Assets", "HybridCLRGenerate"), "*", SearchOption.AllDirectories)).ToArray();

        /// <summary>保留资源实际携带的补充元数据，而非最终 Player 会重新导出的可变裁剪目录。</summary>
        public static string PreserveAotMetadata(string version, string listPath, string dlls, string cacheRoot)
        {
            if (string.IsNullOrEmpty(version) || version.IndexOfAny(new[] { '/', '\\', '.' }) >= 0)
                throw new InvalidOperationException("AOT 基线资源版本无效。");
            string target = Path.Combine(cacheRoot, version);
            string[] names = File.ReadAllLines(listPath).Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim()).ToArray();
            if (names.Length == 0 || names.Any(name => Path.GetFileName(name) != name || !name.EndsWith(".dll", StringComparison.Ordinal)))
                throw new InvalidOperationException("AOT 基线清单无效。");
            if (Directory.Exists(target))
            {
                foreach (string name in names) RequireSameFile(Path.Combine(target, name), Path.Combine(dlls, name + ".bytes"));
                return target;
            }
            string pending = target + "." + Guid.NewGuid().ToString("N") + ".pending";
            Directory.CreateDirectory(pending);
            try
            {
                foreach (string name in names) File.Copy(Path.Combine(dlls, name + ".bytes"), Path.Combine(pending, name));
                Directory.Move(pending, target);
            }
            finally { if (Directory.Exists(pending)) Directory.Delete(pending, true); }
            return target;
        }

        /// <summary>应用版本与版本码在每次 APK 重建时变化，其余 Player 设置必须与准备阶段一致。</summary>
        private static string SettingsFingerprint() => ComputeSettingsFingerprint(ProjectRoot);

        /// <summary>全部项目设置参与准备窗口校验，只归一化正常递增的应用版本。</summary>
        public static string ComputeSettingsFingerprint(string root)
        {
            string directory = Path.Combine(root, "ProjectSettings");
            string settings = NormalizedPlayerSettings(Path.Combine(directory, "ProjectSettings.asset"));
            using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(settings))) + "|" +
                FingerprintFiles(Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Where(p => Path.GetFileName(p) != "ProjectSettings.asset").ToArray());
        }

        /// <summary>构建时允许递增版本号，其他设置须保持一致。</summary>
        private static string NormalizedPlayerSettings(string path) =>
            System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(path), @"(?m)^  (bundleVersion|AndroidBundleVersionCode):.*\r?\n", "");

        /// <summary>按路径与内容计算哈希，检测同长度修改、文件增删和重命名；不使用修改时间判等。</summary>
        public static string FingerprintFiles(string[] files)
        {
            using (var buffer = new MemoryStream())
            using (var writer = new BinaryWriter(buffer, Encoding.UTF8, true))
            using (var hash = SHA256.Create())
            {
                foreach (string file in files.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.Ordinal))
                {
                    if (!File.Exists(file)) throw new FileNotFoundException("复用输入缺失。", file);
                    writer.Write(file);
                    using (var input = File.OpenRead(file)) writer.Write(hash.ComputeHash(input));
                }
                writer.Flush(); buffer.Position = 0;
                return Convert.ToBase64String(hash.ComputeHash(buffer));
            }
        }

        /// <summary>旧构建没有内容快照时的保守恢复门禁；不得用于替代后续构建的内容指纹。</summary>
        public static void ValidateRecoveryInputTimes(string[] files, DateTime playerStartedUtc)
        {
            foreach (string file in files)
            {
                if (!File.Exists(file)) throw new FileNotFoundException("恢复输入缺失。", file);
                if (File.GetLastWriteTimeUtc(file) > playerStartedUtc)
                    throw new InvalidOperationException("上次 APK 开始后运行时输入已修改：" + file + "。" + RebuildHint);
            }
        }

        /// <summary>必须字节一致，禁止混用其他平台或后续改写过的 AOT 与资源清单。</summary>
        private static void RequireSameFile(string left, string right)
        {
            if (!File.Exists(left) || !File.Exists(right) || !File.ReadAllBytes(left).SequenceEqual(File.ReadAllBytes(right)))
                throw new InvalidOperationException("复用文件不匹配：" + left + " / " + right + "。" + RebuildHint);
        }
    }
}
#endif
