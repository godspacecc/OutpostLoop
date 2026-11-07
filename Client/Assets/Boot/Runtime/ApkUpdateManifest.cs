using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Boot
{
    /// <summary>Android 整包更新描述；只负责候选版本与文件完整性校验。</summary>
    [Serializable]
    public sealed class ApkUpdateManifest
    {
        public int schemaVersion;
        public string platform;
        public string packageName;
        public string versionName;
        public int versionCode;
        public string apkFileName;
        public long sizeBytes;
        public string sha256;

        public static ApkUpdateManifest ParseAvailable(string json, string currentPackage, string currentVersion, int currentCode) =>
            ParseAvailable(json, currentPackage, currentVersion, currentCode, true);

        public static ApkUpdateManifest ParseAvailable(string json, string currentPackage, string currentVersion, int currentCode,
            bool requireNewer)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 65536) return null;
            try
            {
                var data = JsonUtility.FromJson<ApkUpdateManifest>(json);
                if (data == null || data.schemaVersion != 1 || data.platform != "Android" ||
                    data.packageName != currentPackage || data.versionCode <= 0 || (requireNewer && data.versionCode < currentCode) ||
                    data.sizeBytes <= 0 || data.sizeBytes > 8L * 1024 * 1024 * 1024 ||
                    !Regex.IsMatch(data.apkFileName ?? "", @"\A[A-Za-z0-9][A-Za-z0-9._-]{0,150}\.apk\z") ||
                    !Regex.IsMatch(data.sha256 ?? "", @"\A[0-9a-fA-F]{64}\z") ||
                    !TryVersion(data.versionName, out var next) || !TryVersion(currentVersion, out var current) ||
                    (requireNewer && next.CompareTo(current) <= 0)) return null;
                return data;
            }
            catch (ArgumentException) { return null; }
        }

        public bool VerifyFile(string path)
        {
            try
            {
                using (var stream = File.OpenRead(path))
                using (var hash = SHA256.Create())
                {
                    if (stream.Length != sizeBytes) return false;
                    string actual = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
                    return string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private static bool TryVersion(string value, out Version version)
        {
            version = null;
            if (!Regex.IsMatch(value ?? "", @"\A[0-9]+\.[0-9]+\.[0-9]+\z")) return false;
            var parts = value.Split('.');
            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major) ||
                !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor) ||
                !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int patch)) return false;
            version = new Version(major, minor, patch);
            return true;
        }
    }
}
