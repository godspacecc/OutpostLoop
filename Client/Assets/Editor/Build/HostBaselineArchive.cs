#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Game.Editor
{
    [Serializable]
    public sealed class BaselineFile
    {
        public string Path;
        public long Length;
        public string Sha256;
    }

    [Serializable]
    public sealed class HostBaselineManifest
    {
        public int SchemaVersion = 1;
        public string ProjectId = BuildDataStore.ProjectId;
        public string Id;
        public string Platform;
        public string UnityVersion;
        public string BuildProfile;
        public string Commit;
        public bool SourceDirty;
        public string ResourceVersion;
        public string AppVersion;
        public int AppVersionCode;
        public string NativeInteropContract;
        public BaselineFile[] Inputs;
        public BaselineFile[] Files;
    }

    /// <summary>不可变首包归档；只复制明确文件，校验完成后才原子公开归档或恢复目录。</summary>
    public static class HostBaselineArchive
    {
        internal static string BaselineDirectory(string root, string platform, string id)
        {
            BuildDataStore.Identifier(id);
            return Path.Combine(root, "baselines", BuildDataStore.Platform(platform), id);
        }

        internal static BaselineFile Describe(string file, string relative) => new BaselineFile
        {
            Path = relative.Replace('\\', '/'), Length = new FileInfo(file).Length, Sha256 = BuildDataStore.Hash(file)
        };

        public static string Create(string root, HostBaselineManifest manifest, IDictionary<string, string> sources)
        {
            using var guard = BuildDataStore.Lock(root);
            string destination = BaselineDirectory(root, manifest.Platform, manifest.Id);
            BuildDataStore.NoLinks(destination);
            if (Directory.Exists(destination) || File.Exists(destination)) throw new InvalidOperationException("宿主基线已存在，禁止覆盖。");
            string parent = Path.GetDirectoryName(destination);
            Directory.CreateDirectory(parent);
            string pending = Path.Combine(parent, ".pending-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(pending);
            try
            {
                foreach (var pair in sources.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    if (pair.Key == "manifest.json" || pair.Key == "manifest.sha256")
                        throw new InvalidOperationException("归档文件名保留。");
                    string target = BuildDataStore.Child(pending, pair.Key);
                    BuildDataStore.NoLinks(pair.Value);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(pair.Value, target, false);
                }
                manifest.Files = Enumerate(pending).Select(file => Describe(file, Relative(pending, file))).ToArray();
                ValidateRequired(manifest);
                BuildDataStore.Write(Path.Combine(pending, "manifest.json"), JsonUtility.ToJson(manifest, true));
                BuildDataStore.Write(Path.Combine(pending, "manifest.sha256"), BuildDataStore.Hash(Path.Combine(pending, "manifest.json")));
                Validate(pending, manifest.Platform, manifest.Id);
                Directory.Move(pending, destination);
                return destination;
            }
            finally { if (Directory.Exists(pending)) Directory.Delete(pending, true); }
        }

        private static void ValidateRequired(HostBaselineManifest manifest)
        {
            if (manifest.SchemaVersion != 1 || manifest.ProjectId != BuildDataStore.ProjectId ||
                string.IsNullOrWhiteSpace(manifest.UnityVersion) || string.IsNullOrWhiteSpace(manifest.BuildProfile) ||
                string.IsNullOrWhiteSpace(manifest.Commit) || string.IsNullOrWhiteSpace(manifest.ResourceVersion) ||
                string.IsNullOrWhiteSpace(manifest.AppVersion) || manifest.AppVersionCode <= 0 ||
                manifest.Inputs == null || manifest.Inputs.Length == 0 || manifest.Files == null || manifest.Files.Length == 0)
                throw new InvalidOperationException("宿主归档元数据不完整。");
            if (manifest.Files.Any(x => x == null || string.IsNullOrWhiteSpace(x.Path) || string.IsNullOrWhiteSpace(x.Sha256)) ||
                manifest.Inputs.Any(x => x == null || string.IsNullOrWhiteSpace(x.Path) || string.IsNullOrWhiteSpace(x.Sha256)))
                throw new InvalidOperationException("宿主归档文件或输入清单无效。");
            string[] required = { "Dlls/Game.dll.bytes", "Dlls/HybridCLRAssemblyList.bytes" };
            foreach (string name in required)
                if (!manifest.Files.Any(x => x.Path == name && x.Length > 0)) throw new InvalidOperationException("宿主归档缺少 " + name);
            if (!manifest.Files.Any(x => x.Path.StartsWith("Aot/", StringComparison.Ordinal) && x.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) ||
                !manifest.Files.Any(x => x.Path.StartsWith("Host/", StringComparison.Ordinal)) ||
                !manifest.Files.Any(x => x.Path.StartsWith("Builtin/", StringComparison.Ordinal)) ||
                !manifest.Files.Any(x => x.Path.StartsWith("Generated/", StringComparison.Ordinal)) ||
                !manifest.Files.Any(x => x.Path.StartsWith("NativeGenerated/", StringComparison.Ordinal)))
                throw new InvalidOperationException("宿主归档缺少 AOT、安装包、内置资源或 HybridCLR 生成文件。");
        }

        public static HostBaselineManifest Validate(string directory, string platform, string id)
        {
            BuildDataStore.Platform(platform);
            BuildDataStore.Identifier(id);
            BuildDataStore.NoLinks(directory);
            string manifestPath = Path.Combine(directory, "manifest.json");
            if (File.ReadAllText(Path.Combine(directory, "manifest.sha256")).Trim() != BuildDataStore.Hash(manifestPath))
                throw new InvalidOperationException("基线清单哈希不一致。");
            var value = JsonUtility.FromJson<HostBaselineManifest>(File.ReadAllText(manifestPath));
            if (value == null || value.Id != id || value.Platform != platform) throw new InvalidOperationException("基线项目或平台不匹配。");
            ValidateRequired(value);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in value.Files)
            {
                if (file == null || !names.Add(file.Path ?? "")) throw new InvalidOperationException("基线文件路径重复。");
                string path = BuildDataStore.Child(directory, file.Path);
                if (!File.Exists(path) || new FileInfo(path).Length != file.Length || BuildDataStore.Hash(path) != file.Sha256)
                    throw new InvalidOperationException("基线文件缺失或损坏：" + file.Path);
            }
            var actual = Enumerate(directory).Select(file => Relative(directory, file))
                .Where(file => file != "manifest.json" && file != "manifest.sha256").ToArray();
            if (actual.Length != names.Count || actual.Any(file => !names.Contains(file)))
                throw new InvalidOperationException("基线文件集合与清单不符。");
            ValidateAotList(directory);
            return value;
        }

        private static void ValidateAotList(string directory)
        {
            var entries = File.ReadAllLines(Path.Combine(directory, "Dlls", "HybridCLRAssemblyList.bytes"))
                .Select(x => x.Trim()).Where(x => x.Length > 0 && !x.StartsWith("//", StringComparison.Ordinal)).ToArray();
            if (entries.Length == 0 || entries.Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Length)
                throw new InvalidOperationException("基线 AOT 清单为空或重复。");
            foreach (string entry in entries)
            {
                if (entry != Path.GetFileName(entry) || !entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                    entry.Equals("Game.dll", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("基线 AOT 清单条目无效。");
                string hostAot = BuildDataStore.Child(directory, "Aot/" + entry);
                if (!File.Exists(hostAot)) throw new InvalidOperationException("宿主裁剪 AOT 缺失：" + entry);
                string aot = BuildDataStore.Child(directory, "RuntimeAot/" + entry);
                string bytes = BuildDataStore.Child(directory, "Dlls/" + entry + ".bytes");
                if (BuildDataStore.Hash(aot) != BuildDataStore.Hash(bytes))
                    throw new InvalidOperationException("基线 AOT 与内置 DLL 不一致：" + entry);
            }
        }

        public static string Restore(string root, string platform, string id, string restoreRoot,
            string unityVersion, string profile, BaselineFile[] currentInputs)
        {
            using var guard = BuildDataStore.Lock(root);
            string source = BaselineDirectory(root, platform, id);
            var value = Validate(source, platform, id);
            CheckCompatibility(value, unityVersion, profile, currentInputs);
            BuildDataStore.NoLinks(restoreRoot);
            if (BuildDataStore.IsWithin(restoreRoot, root) || BuildDataStore.IsWithin(root, restoreRoot))
                throw new InvalidOperationException("恢复目录不能与持久构建目录重叠。");
            Directory.CreateDirectory(restoreRoot);
            string pending = Path.Combine(restoreRoot, ".pending-" + Guid.NewGuid().ToString("N"));
            string destination = Path.Combine(restoreRoot, id + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(pending);
            try
            {
                foreach (string file in Enumerate(source))
                {
                    string target = BuildDataStore.Child(pending, Relative(source, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(file, target);
                }
                Validate(pending, platform, id);
                Directory.Move(pending, destination);
                return destination;
            }
            finally { if (Directory.Exists(pending)) Directory.Delete(pending, true); }
        }

        public static void CheckCompatibility(HostBaselineManifest baseline, string unityVersion,
            string profile, BaselineFile[] currentInputs)
        {
            if (baseline.UnityVersion != unityVersion || baseline.BuildProfile != profile)
                throw new InvalidOperationException("Unity 版本或宿主构建配置变化，必须重新打首包。");
            var old = baseline.Inputs.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
            var current = currentInputs.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
            if (old.Length != current.Length) throw new InvalidOperationException("宿主输入文件集合变化，必须重新打首包。");
            for (int i = 0; i < old.Length; i++)
                if (old[i].Path != current[i].Path || old[i].Sha256 != current[i].Sha256)
                    throw new InvalidOperationException("宿主输入不兼容，必须重新打首包：" + current[i].Path);
        }

        internal static string Relative(string root, string path)
        {
            root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            path = Path.GetFullPath(path);
            if (!BuildDataStore.IsWithin(path, root) || path.Length <= root.Length) throw new InvalidOperationException("文件不在指定目录内。");
            return path.Substring(root.Length + 1).Replace('\\', '/');
        }

        internal static IEnumerable<string> Enumerate(string root)
        {
            BuildDataStore.NoLinks(root);
            foreach (string file in Directory.GetFiles(root).OrderBy(x => x, StringComparer.Ordinal))
            {
                BuildDataStore.NoLinks(file);
                yield return file;
            }
            foreach (string dir in Directory.GetDirectories(root).OrderBy(x => x, StringComparer.Ordinal))
                foreach (string file in Enumerate(dir)) yield return file;
        }
    }
}
#endif
