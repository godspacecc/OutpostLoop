using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Boot
{
    /// <summary>PC 整包发布描述；只校验身份、递增版本及文件完整性，不负责下载或安装。</summary>
    [Serializable]
    public sealed class WindowsUpdateManifest
    {
        public const string ProductId = "com.outpostloop.game";
        public int schemaVersion;
        public string productId;
        public string platform;
        public string architecture;
        public string versionName;
        /// <summary>PC 独立递增发布序号；允许跳过中间版本，禁止回退。</summary>
        public int releaseNumber;
        /// <summary>服务器固定 Windows 更新目录中的单一 EXE 文件名，禁止外部路径。</summary>
        public string installerFileName;
        /// <summary>安装器完整长度，单位为字节，范围 1 字节至 32 GiB。</summary>
        public long sizeBytes;
        public string sha256;

        public static WindowsUpdateManifest ParseAvailable(string json, int currentRelease) => ParseAvailable(json, currentRelease, true);

        /// <summary>requireNewer=false 仅验证描述完整性，用于区分有效旧版与检查失败。</summary>
        public static WindowsUpdateManifest ParseAvailable(string json, int currentRelease, bool requireNewer)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 65536 || currentRelease < 0) return null;
            try
            {
                var value = JsonUtility.FromJson<WindowsUpdateManifest>(json);
                if (value == null || !value.ValidIdentity || (requireNewer && value.releaseNumber <= currentRelease) ||
                    value.sizeBytes <= 0 || value.sizeBytes > 32L * 1024 * 1024 * 1024 ||
                    !Regex.IsMatch(value.installerFileName ?? "", @"\A[A-Za-z0-9][A-Za-z0-9._-]{0,150}\.exe\z") ||
                    !Regex.IsMatch(value.sha256 ?? "", @"\A[0-9a-fA-F]{64}\z")) return null;
                return value;
            }
            catch (ArgumentException) { return null; }
        }

        public bool ValidIdentity => schemaVersion == 1 && productId == ProductId && platform == "Windows" &&
            architecture == "x64" && releaseNumber > 0 &&
            Regex.IsMatch(versionName ?? "", @"\A[0-9]{1,5}\.[0-9]{1,5}\.[0-9]{1,5}\z");

        /// <summary>缺少或损坏本地身份时不猜测版本，旧散装包由用户重新安装首包。</summary>
        public static WindowsUpdateManifest ReadInstalled()
        {
            try
            {
                string path = Path.Combine(Application.streamingAssetsPath, "windows-release.json");
                if (!File.Exists(path) || new FileInfo(path).Length > 65536) return null;
                var value = JsonUtility.FromJson<WindowsUpdateManifest>(File.ReadAllText(path));
                return value != null && value.ValidIdentity ? value : null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            { return null; }
        }

        public bool VerifyFile(string path)
        {
            try
            {
                using (var stream = File.OpenRead(path))
                using (var hash = SHA256.Create())
                    return stream.Length == sizeBytes && string.Equals(
                        BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", ""), sha256,
                        StringComparison.OrdinalIgnoreCase);
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
}
