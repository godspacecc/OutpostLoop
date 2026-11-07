#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>检出目录之外的构建数据。锁、原子替换和路径检查不依赖 Unity 构建。</summary>
    public static class BuildDataStore
    {
        public const string ProjectId = "com.outpostloop.game";
        public static string Root
        {
            get
            {
                string configured = Environment.GetEnvironmentVariable("OUTPOSTLOOP_BUILD_DATA_ROOT");
                string path = string.IsNullOrWhiteSpace(configured)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OutpostLoop", "BuildData")
                    : configured;
                if (!Path.IsPathRooted(path)) throw new InvalidOperationException("构建数据目录必须是绝对路径。");
                string root = Path.GetFullPath(path);
                string client = Directory.GetParent(Application.dataPath).FullName;
                string checkout = Directory.GetParent(client).FullName;
                if (IsWithin(root, checkout)) throw new InvalidOperationException("持久构建数据不能放在检出目录内。");
                return root;
            }
        }

        internal static bool IsWithin(string path, string root)
        {
            path = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(path, root, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        internal static string Child(string root, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(":") ||
                relative.Split('/', '\\').Any(x => x == ".." || x == "." || x.Length == 0))
                throw new InvalidOperationException("归档相对路径无效：" + relative);
            string path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsWithin(path, root)) throw new InvalidOperationException("构建文件路径越界。");
            return path;
        }

        internal static void NoLinks(string path)
        {
            for (string current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("构建数据不接受链接目录或文件：" + current);
        }

        internal static FileStream Lock(string root)
        {
            NoLinks(root);
            Directory.CreateDirectory(root);
            return new FileStream(Path.Combine(root, "store.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }

        internal static string Hash(string file)
        {
            NoLinks(file);
            using var stream = File.OpenRead(file);
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        internal static void Write(string path, string text)
        {
            NoLinks(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string pending = path + "." + Guid.NewGuid().ToString("N") + ".pending";
            try
            {
                using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(text);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(pending, path, null);
                else File.Move(pending, path);
            }
            finally { if (File.Exists(pending)) File.Delete(pending); }
        }

        internal static string Platform(string platform)
        {
            if (platform != "StandaloneWindows64" && platform != "Android")
                throw new InvalidOperationException("构建平台无效。");
            return platform;
        }

        internal static void Identifier(string value)
        {
            if (!Regex.IsMatch(value ?? "", @"\A[a-zA-Z0-9][a-zA-Z0-9_-]{0,100}\z"))
                throw new InvalidOperationException("基线编号无效。");
        }
    }

    [Serializable]
    public sealed class ApplicationVersionReservation
    {
        public string VersionName;
        public int VersionCode;
    }

    [Serializable]
    internal sealed class PersistentVersionLedger
    {
        public int SchemaVersion;
        public string ProjectId;
        public long Revision;
        public string Platform;
        public string AppVersion;
        public int AppCode;
        public string ResourceVersion;
    }

    /// <summary>在独占锁内持久预留序号；失败产生空号，绝不回滚预留。</summary>
    public static class PersistentBuildVersions
    {
        private static PersistentVersionLedger Read(string root, string platform)
        {
            string path = Path.Combine(root, "versions", BuildDataStore.Platform(platform) + ".json");
            BuildDataStore.NoLinks(path);
            if (!File.Exists(path))
            {
                // 初始化标记存在但台账丢失时拒绝重建，避免悄悄从源码版本重新开始。
                if (File.Exists(path + ".initialized")) throw new InvalidOperationException("持久版本台账丢失，须从备份恢复。");
                return new PersistentVersionLedger { SchemaVersion = 1, ProjectId = BuildDataStore.ProjectId, Platform = platform };
            }
            if (!File.Exists(path + ".sha256") || File.ReadAllText(path + ".sha256").Trim() != BuildDataStore.Hash(path))
                throw new InvalidOperationException("持久版本台账校验失败，须从备份恢复。");
            PersistentVersionLedger value;
            try { value = JsonUtility.FromJson<PersistentVersionLedger>(File.ReadAllText(path)); }
            catch (ArgumentException exception) { throw new InvalidOperationException("持久版本台账无法解析，须从备份恢复。", exception); }
            if (value == null || value.SchemaVersion != 1 || value.ProjectId != BuildDataStore.ProjectId ||
                value.Platform != platform || value.Revision <= 0 || value.AppCode < 0 ||
                (!string.IsNullOrEmpty(value.AppVersion) && !ValidVersion(value.AppVersion)) ||
                (!string.IsNullOrEmpty(value.ResourceVersion) && !TryTimestamp(value.ResourceVersion, out _)))
                throw new InvalidOperationException("持久版本台账损坏，禁止回退到源码版本。");
            return value;
        }

        private static void Save(string root, PersistentVersionLedger ledger)
        {
            string path = Path.Combine(root, "versions", ledger.Platform + ".json");
            // 先写初始化标记；即使首次分配中途崩溃也必须人工核对，而不是重复分配。
            BuildDataStore.Write(path + ".initialized", BuildDataStore.ProjectId);
            ledger.Revision = checked(ledger.Revision + 1);
            BuildDataStore.Write(path, JsonUtility.ToJson(ledger, true));
            BuildDataStore.Write(path + ".sha256", BuildDataStore.Hash(path));
        }

        private static bool ValidVersion(string value) => Regex.IsMatch(value ?? "", @"\A[0-9]+\.[0-9]+\.[0-9]+\z") &&
            Version.TryParse(value, out _);
        private static bool TryTimestamp(string text, out DateTime value) => DateTime.TryParseExact(text,
            "yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out value);

        public static ApplicationVersionReservation ReserveApplication(string root, string platform,
            string seedVersion, int seedCode)
        {
            using var guard = BuildDataStore.Lock(root);
            var ledger = Read(root, platform);
            if (!ValidVersion(seedVersion) || seedCode < 0) throw new InvalidOperationException("应用版本起点无效。");
            string version = string.IsNullOrEmpty(ledger.AppVersion) || new Version(seedVersion) > new Version(ledger.AppVersion)
                ? seedVersion : ledger.AppVersion;
            int code = checked(Math.Max(seedCode, ledger.AppCode) + 1);
            if (platform == "Android")
            {
                var parts = version.Split('.');
                version = $"{int.Parse(parts[0])}.{int.Parse(parts[1])}.{checked(int.Parse(parts[2]) + 1)}";
            }
            ledger.AppVersion = version;
            ledger.AppCode = code;
            Save(root, ledger);
            return new ApplicationVersionReservation { VersionName = version, VersionCode = code };
        }

        public static string ReserveResource(string root, string platform, DateTime timestamp,
            string packageRoot, string minimumVersion = null)
        {
            using var guard = BuildDataStore.Lock(root);
            var ledger = Read(root, platform);
            DateTime next = timestamp.AddTicks(-(timestamp.Ticks % TimeSpan.TicksPerSecond));
            foreach (string value in new[] { ledger.ResourceVersion, minimumVersion })
            {
                if (string.IsNullOrEmpty(value)) continue;
                if (value.Length == 12) // 兼容旧分钟级 Host 版本。
                {
                    if (!DateTime.TryParseExact(value, "yyyyMMddHHmm", System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out DateTime minute))
                        throw new InvalidOperationException("资源版本起点无效。");
                    if (next <= minute) next = minute.AddMinutes(1);
                }
                else
                {
                    if (!TryTimestamp(value, out DateTime previous)) throw new InvalidOperationException("资源版本起点无效。");
                    if (next <= previous) next = previous.AddSeconds(1);
                }
            }
            string candidate;
            do
            {
                candidate = next.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
                next = next.AddSeconds(1);
            } while (Directory.Exists(Path.Combine(packageRoot, candidate)) || File.Exists(Path.Combine(packageRoot, candidate)));
            ledger.ResourceVersion = candidate;
            Save(root, ledger);
            return candidate;
        }
    }
}
#endif
