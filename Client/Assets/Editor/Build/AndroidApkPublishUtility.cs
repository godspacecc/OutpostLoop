#if UNITY_EDITOR
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Boot;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>把已验证 APK 和原子版本描述发布到 Android 资源服。</summary>
    public static class AndroidApkPublishUtility
    {
        public static string PublishToLatestBundle(string apkPath, string releaseRoot, string versionName, int versionCode)
        {
            if (!ResourceServerPublishUtility.TryFindLatestBundle(releaseRoot, out string bundle, out string error))
                throw new InvalidOperationException(error);
            return Publish(apkPath, bundle, versionName, versionCode);
        }

        private static ApkUpdateManifest CreateManifest(string apkPath, string versionName, int versionCode)
        {
            if (!Regex.IsMatch(versionName ?? "", @"\A[0-9]+\.[0-9]+\.[0-9]+\z") ||
                !Version.TryParse(versionName, out _) || versionName.Length > 60 || versionCode <= 0)
                throw new ArgumentException("APK 版本必须为有效的三段数字版本，版本码必须大于零。");
            if (!File.Exists(apkPath)) throw new FileNotFoundException("待发布 APK 不存在。", apkPath);
            long size = new FileInfo(apkPath).Length;
            if (size <= 0 || size > 8L * 1024 * 1024 * 1024) throw new InvalidOperationException("APK 大小无效。");
            var manifest = new ApkUpdateManifest
            {
                schemaVersion = 1,
                platform = "Android",
                packageName = "com.godspace.outpostloop",
                versionName = versionName,
                versionCode = versionCode,
                apkFileName = $"OutpostLoop-{versionName}-{versionCode}.apk",
                sizeBytes = size
            };
            using (var stream = File.OpenRead(apkPath))
            using (var hash = SHA256.Create())
                manifest.sha256 = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();

            return manifest;
        }

        /// <summary>APK 生成后在其旁边保存更新清单；不创建发布目录或同步服务器文件。</summary>
        public static string GenerateManifest(string apkPath, string versionName, int versionCode)
        {
            var manifest = CreateManifest(apkPath, versionName, versionCode);
            string descriptor = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(apkPath)), "app-update.json");
            string pending = descriptor + "." + Guid.NewGuid().ToString("N") + ".pending";
            try
            {
                File.WriteAllText(pending, JsonUtility.ToJson(manifest, true), new UTF8Encoding(false));
                if (File.Exists(descriptor)) File.Replace(pending, descriptor, null);
                else File.Move(pending, descriptor);
            }
            finally
            {
                if (File.Exists(pending)) File.Delete(pending);
            }
            return descriptor;
        }

        /// <summary>兼容既有调用，清单生成与发布共用同一套文件验证。</summary>
        public static string Publish(string apkPath, string bundleDirectory, string versionName, int versionCode)
        {
            return PublishManifest(apkPath, bundleDirectory, CreateManifest(apkPath, versionName, versionCode));
        }

        /// <summary>消费只构建阶段保存的清单，拒绝产物被修改后继续发布。</summary>
        public static string PublishPrepared(string apkPath, string manifestPath, string bundleDirectory)
        {
            var manifest = ApkUpdateManifest.ParseAvailable(File.ReadAllText(manifestPath),
                "com.godspace.outpostloop", "0.0.0", 0, false);
            if (manifest == null || !manifest.VerifyFile(apkPath))
                throw new InvalidOperationException("APK 更新清单无效或产物已改变。");
            return PublishManifest(apkPath, bundleDirectory, manifest);
        }

        private static string PublishManifest(string apkPath, string bundleDirectory, ApkUpdateManifest manifest)
        {
            using var lease = WorkspaceBuildLock.Acquire(Directory.GetParent(UnityEngine.Application.dataPath).FullName);
            if (!manifest.VerifyFile(apkPath)) throw new InvalidOperationException("APK 与更新清单不一致。");
            if (!File.Exists(Path.Combine(bundleDirectory, "ResourceFiles", "OutpostLoop.version")))
                throw new InvalidOperationException("资源服尚未发布有效资源版本。");
            ResourceServerPublishUtility.SyncServerSupportFiles(bundleDirectory);

            string directory = Path.GetFullPath(Path.Combine(bundleDirectory, "AppUpdates", "Android"));
            Directory.CreateDirectory(directory);
            string destination = Path.Combine(directory, manifest.apkFileName);
            string descriptor = Path.Combine(directory, "app-update.json");
            string token = Guid.NewGuid().ToString("N");
            string pendingApk = Path.Combine(directory, token + ".apk.pending");
            string pendingDescriptor = Path.Combine(directory, token + ".json.pending");
            try
            {
                if (File.Exists(destination))
                {
                    if (!manifest.VerifyFile(destination))
                        throw new InvalidOperationException("该版本 APK 已发布且内容不同，请使用新版本号。");
                }
                else
                {
                    File.Copy(apkPath, pendingApk);
                    if (!manifest.VerifyFile(pendingApk)) throw new IOException("APK 复制后完整性校验失败。");
                    File.Move(pendingApk, destination);
                }
                File.WriteAllText(pendingDescriptor, JsonUtility.ToJson(manifest, true), new UTF8Encoding(false));
                if (File.Exists(descriptor)) File.Replace(pendingDescriptor, descriptor, null);
                else File.Move(pendingDescriptor, descriptor);
                return descriptor;
            }
            finally
            {
                if (File.Exists(pendingApk)) File.Delete(pendingApk);
                if (File.Exists(pendingDescriptor)) File.Delete(pendingDescriptor);
            }
        }
    }
}
#endif
