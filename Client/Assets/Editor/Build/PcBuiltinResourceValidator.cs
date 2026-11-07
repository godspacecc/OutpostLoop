#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEngine;
using YooAsset;
namespace Game.Editor
{
    /// <summary>冻结本次完整包或启动包资源，用于核对最终游戏与下载资源目录。</summary>
    internal sealed class PcBuiltinResourceValidator
    {
        // 冻结构建前字节指纹，避免构建后源目录变化掩盖错误。
        private readonly Dictionary<string, string> fingerprints = new Dictionary<string, string>(StringComparer.Ordinal);
        // 使用项目实际YooAsset内置目录配置映射到Player。
        private readonly string relativeDirectory;
        // 启动包仅允许清单；同时冻结对应的完整下载资源。
        private bool manifestOnly;
        private PcBuiltinResourceValidator remoteSnapshot;
        private string remoteDirectory;
        // 已安装YooAsset清单类型为internal，沿用反射访问真实序列化器。
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        private PcBuiltinResourceValidator()
        {
            relativeDirectory = Path.GetRelativePath(Application.streamingAssetsPath, YooAssetBuildUtility.GetStreamingAssetsDirectory());
            if (Path.IsPathRooted(relativeDirectory) || relativeDirectory.StartsWith("..", StringComparison.Ordinal))
                throw new InvalidOperationException("内置资源配置不在 StreamingAssets 目录内。");
        }

        /// <summary>确认清单包含的全部资源齐全，再冻结本轮版本、目录与文件内容。</summary>
        internal static PcBuiltinResourceValidator Capture(string directory, string version)
            => CaptureCore(directory, version, false, true);

        /// <summary>远程包没有内置catalog，仍必须逐包校验清单内容。</summary>
        internal static PcBuiltinResourceValidator CaptureRemote(string directory, string version)
            => CaptureCore(directory, version, false, false);

        /// <summary>冻结完整下载资源和启动包清单，保证两者来自同一次资源构建。</summary>
        internal static PcBuiltinResourceValidator CaptureStartup(string builtinDirectory, string packageDirectory, string version)
        {
            var remote = CaptureCore(packageDirectory, version, false, false);
            var startup = CaptureCore(builtinDirectory, version, true, true);
            foreach (string name in new[] { "OutpostLoop.version", "OutpostLoop_" + version + ".bytes", "OutpostLoop_" + version + ".hash" })
                if (startup.fingerprints[name] != remote.fingerprints[name])
                    throw new InvalidOperationException("启动包清单与完整下载资源不一致：" + name);
            startup.remoteSnapshot = remote;
            startup.remoteDirectory = packageDirectory;
            return startup;
        }

        /// <summary>下载资源没有内置目录catalog；启动包有空catalog但不携带Bundle。</summary>
        private static PcBuiltinResourceValidator CaptureCore(string directory, string version, bool manifestOnly, bool includeCatalog)
        {
            const string package = "OutpostLoop";
            var snapshot = new PcBuiltinResourceValidator { manifestOnly = manifestOnly };
            string versionFile = Path.Combine(directory, package + ".version");
            RequireFile(versionFile);
            if (File.ReadAllText(versionFile).Trim() != version)
                throw new InvalidOperationException("内置资源版本不匹配：" + versionFile);
            string manifestName = package + "_" + version + ".bytes";
            string hashName = package + "_" + version + ".hash";
            foreach (string name in new[] { package + ".version", manifestName, hashName })
                snapshot.Add(directory, name);

            var tools = typeof(ResourcePackage).Assembly.GetType("YooAsset.ManifestTools", true);
            byte[] bytes = File.ReadAllBytes(Path.Combine(directory, manifestName));
            string hash = File.ReadAllText(Path.Combine(directory, hashName)).Trim();
            if (!(bool)tools.GetMethod("VerifyManifestData", Flags).Invoke(null, new object[] { bytes, hash }))
                throw new InvalidOperationException("内置清单哈希不匹配：" + manifestName);
            object manifest = tools.GetMethod("DeserializeFromBinary", Flags).Invoke(null, new object[] { bytes, null });
            if (manifest == null || (string)Field(manifest, "PackageName") != package || (string)Field(manifest, "PackageVersion") != version)
                throw new InvalidOperationException("内置清单身份或版本无效：" + manifestName);
            var bundles = ((IEnumerable)Field(manifest, "BundleList")).Cast<object>().ToArray();
            if (bundles.Length == 0) throw new InvalidOperationException("完整内置资源清单不包含资源包。");
            Catalog catalog = null, binaryCatalog = null;
            if (includeCatalog)
            {
                snapshot.Add(directory, "BuildinCatalog.bytes");
                snapshot.Add(directory, "BuildinCatalog.json");
                catalog = JsonUtility.FromJson<Catalog>(File.ReadAllText(Path.Combine(directory, "BuildinCatalog.json")));
                if (catalog?.Wrappers == null) throw new InvalidOperationException("BuildinCatalog.json 无效。");
                try
                {
                    var catalogTools = typeof(ResourcePackage).Assembly.GetType("YooAsset.CatalogTools", true);
                    object decoded = catalogTools.GetMethod("DeserializeFromBinary", Flags).Invoke(null,
                        new object[] { File.ReadAllBytes(Path.Combine(directory, "BuildinCatalog.bytes")) });
                    binaryCatalog = JsonUtility.FromJson<Catalog>(JsonUtility.ToJson(decoded));
                }
                catch (Exception error)
                {
                    throw new InvalidOperationException("BuildinCatalog.bytes 无法解析：" + directory, error);
                }
                if (binaryCatalog?.Wrappers == null || binaryCatalog.PackageName != package || binaryCatalog.PackageVersion != version)
                    throw new InvalidOperationException("BuildinCatalog.bytes 身份或版本不匹配：" + directory);
            }
            if (manifestOnly)
            {
                if (catalog.Wrappers.Count != 0 || binaryCatalog.Wrappers.Count != 0)
                    throw new InvalidOperationException("启动包的 BuildinCatalog 必须为空：" + directory);
                snapshot.ValidateManifestOnlyFiles(directory);
                return snapshot;
            }
            foreach (object bundle in bundles)
            {
                string name = (string)bundle.GetType().GetProperty("FileName", Flags).GetValue(bundle);
                string bundleHash = (string)Field(bundle, "FileHash");
                string path = Path.Combine(directory, name);
                snapshot.Add(directory, name);
                if (new FileInfo(path).Length != (long)Field(bundle, "FileSize") || Fingerprint(path, false) != bundleHash)
                    throw new InvalidOperationException("内置资源长度或哈希与清单不符：" + path);
                if (includeCatalog && !catalog.Wrappers.Any(entry => entry.BundleGUID == bundleHash && entry.FileName == name))
                    throw new InvalidOperationException("BuildinCatalog.json 缺少资源登记：" + name);
                if (includeCatalog && !binaryCatalog.Wrappers.Any(entry => entry.BundleGUID == bundleHash && entry.FileName == name))
                    throw new InvalidOperationException("BuildinCatalog.bytes 缺少资源登记：" + name);
            }
            return snapshot;
        }

        /// <summary>只检查导出的游戏目录，逐文件核对冻结的指纹；缺包或错包均拒绝发布。</summary>
        internal void ValidatePlayer(string playerPath)
        {
            RequireFile(playerPath);
            string directory = Path.Combine(Path.GetDirectoryName(playerPath), Path.GetFileNameWithoutExtension(playerPath) + "_Data", "StreamingAssets", relativeDirectory);
            ValidateDirectory(directory);
            // Player生成期间源文件也可能变化，发布前复核同一批下载资源。
            if (remoteSnapshot != null) remoteSnapshot.ValidateDirectory(remoteDirectory);
        }

        /// <summary>按冻结结果核对目录，不把工程内正确文件当成导出成功。</summary>
        internal void ValidateDirectory(string directory)
        {
            foreach (var file in fingerprints)
            {
                string path = Path.Combine(directory, file.Key);
                RequireFile(path);
                if (Fingerprint(path, true) != file.Value)
                    throw new InvalidOperationException("资源与本次构建不一致：" + path);
            }
            if (manifestOnly) ValidateManifestOnlyFiles(directory);
        }

        /// <summary>启动包仅允许本轮元数据，拒绝任意名称、任意子目录里的误带资源。</summary>
        private void ValidateManifestOnlyFiles(string directory)
        {
            foreach (string path in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            {
                string name = Path.GetRelativePath(directory, path).Replace('\\', '/');
                if (name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                // YooAsset也可能复制可读JSON清单，但运行时清单仍以二进制指纹为准。
                bool readableManifest = name.EndsWith(".json", StringComparison.Ordinal) &&
                    fingerprints.ContainsKey(name.Substring(0, name.Length - 5) + ".bytes");
                if (!fingerprints.ContainsKey(name) && !readableManifest)
                    throw new InvalidOperationException("启动包不应携带额外资源：" + path);
            }
        }

        /// <summary>仅接受清单中的普通文件名，并记录实际内容。</summary>
        private void Add(string directory, string name)
        {
            if (string.IsNullOrEmpty(name) || name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || name == "." || name == "..")
                throw new InvalidOperationException("内置资源文件名无效：" + name);
            string path = Path.Combine(directory, name);
            RequireFile(path);
            fingerprints[name] = Fingerprint(path, true);
        }

        /// <summary>读取库清单的真实字段。</summary>
        private static object Field(object owner, string name) => owner.GetType().GetField(name, Flags).GetValue(owner);
        /// <summary>阻断缺失或空的产物。</summary>
        private static void RequireFile(string path)
        {
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
                throw new FileNotFoundException("必要文件不存在或为空：" + path, path);
        }
        /// <summary>流式计算指纹；清单用MD5，冻结结果用SHA256。</summary>
        private static string Fingerprint(string path, bool sha256)
        {
            using (HashAlgorithm algorithm = sha256 ? (HashAlgorithm)SHA256.Create() : MD5.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>目录身份及资源登记，适用于二进制目录解码结果与JSON目录。</summary>
        [Serializable] private sealed class Catalog
        {
            public string PackageName; // 包裹名称。
            public string PackageVersion; // 包裹版本。
            public List<Entry> Wrappers; // 内置资源登记。
        }
        /// <summary>资源哈希与实际文件名映射。</summary>
        [Serializable] private sealed class Entry
        {
            public string BundleGUID; // 资源内容标识。
            public string FileName; // 内置文件名。
        }
    }
}
#endif
