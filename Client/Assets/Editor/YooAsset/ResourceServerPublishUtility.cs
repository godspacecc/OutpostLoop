#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// 独立资源服务器发布工具，只复制已存在且已验证的 Host 资源，不触发构建或启动服务。
    /// </summary>
    public sealed class ResourceServerPublishResult
    {
        public bool Success;
        public string Operation;
        public string Version;
        public string PreviousVersion;
        public string SourcePackageDirectory;
        public string BundleDirectory;
        public string Error;
        public bool ResourceFilesSucceeded;
        public bool NoticeSyncSucceeded;
        public string NoticeSyncError;
    }

    /// <summary>
    /// 将最新有效 YooAsset Host 资源发布或更新到可复制运行的局域网资源服务器包。
    /// Windows 与 Android 使用独立产物目录，禁止跨平台混用 AssetBundle。
    /// </summary>
    public static class ResourceServerPublishUtility
    {
        private const string PackageName = "OutpostLoop";
        private const string ResourceFilesName = "ResourceFiles";
        private const string PublishOperation = "发布资源服";
        private const string UpdateOperation = "更新资源服";
        private static readonly Regex BundleNameRegex =
            new Regex(@"^ResourceServerBundle_(\d{8})_(\d{6})$", RegexOptions.Compiled);
        private static readonly string[] TemplateFiles =
        {
            "start_resource_server.bat",
            "resource_server.ps1",
            "README.txt",
            "validate_resource_update_notices.ps1",
            "validate_resource_update_notices.bat",
            "publish_app_update.ps1",
            "publish_windows_update.ps1"
        };

        internal static Action<string> BeforeCommitUpdateForTests;
        internal static Action<string> BeforeCommitServerSupportForTests;
        internal static Action<string, string> AfterBackupServerSupportFileForTests;
        internal static Action<string, string> AfterRestoreServerSupportFileForTests;

        public static ResourceServerPublishResult PublishLatestValidPackage()
        {
            var workspaceRoot = GetWorkspaceRoot();
            return Publish(
                Path.Combine(workspaceRoot, "Client", "Bundles", "StandaloneWindows64", PackageName),
                Path.Combine(workspaceRoot, "Server", "ResourceServer"),
                Path.Combine(workspaceRoot, "Release", "ResourceServer"),
                DateTime.Now);
        }

        public static ResourceServerPublishResult UpdateLatestBundle()
        {
            var workspaceRoot = GetWorkspaceRoot();
            return Update(
                Path.Combine(workspaceRoot, "Client", "Bundles", "StandaloneWindows64", PackageName),
                Path.Combine(workspaceRoot, "Release", "ResourceServer"));
        }

        /// <summary>发布最新 Android Host 包到 Android 专属资源服目录。</summary>
        public static ResourceServerPublishResult PublishLatestValidAndroidPackage()
        {
            var workspaceRoot = GetWorkspaceRoot();
            return Publish(
                Path.Combine(workspaceRoot, "Client", "Bundles", "Android", PackageName),
                Path.Combine(workspaceRoot, "Server", "ResourceServer"),
                Path.Combine(workspaceRoot, "Release", "ResourceServer", "Android"),
                DateTime.Now);
        }

        /// <summary>以最新 Android Host 包原子替换最近发布的 Android ResourceFiles。</summary>
        public static ResourceServerPublishResult UpdateLatestAndroidBundle()
        {
            var workspaceRoot = GetWorkspaceRoot();
            return Update(
                Path.Combine(workspaceRoot, "Client", "Bundles", "Android", PackageName),
                Path.Combine(workspaceRoot, "Release", "ResourceServer", "Android"));
        }

        /// <summary>已有 Android 发布包时原子更新，否则创建首个发布包。</summary>
        public static ResourceServerPublishResult PublishOrUpdateLatestAndroidPackage()
        {
            var workspaceRoot = GetWorkspaceRoot();
            string packageRoot = Path.Combine(workspaceRoot, "Client", "Bundles", "Android", PackageName);
            string releaseRoot = Path.Combine(workspaceRoot, "Release", "ResourceServer", "Android");
            if (TryFindLatestBundle(releaseRoot, out _, out _))
                return Update(packageRoot, releaseRoot);

            return Publish(
                packageRoot,
                Path.Combine(workspaceRoot, "Server", "ResourceServer"),
                releaseRoot,
                DateTime.Now);
        }

        /// <summary>PC 安装器随最新 Host 资源发布；已有资源服包时保留更新目录并原子更新资源。</summary>
        public static ResourceServerPublishResult PublishOrUpdateLatestPackage()
        {
            string workspace = GetWorkspaceRoot();
            string release = Path.Combine(workspace, "Release", "ResourceServer");
            return TryFindLatestBundle(release, out _, out _) ? UpdateLatestBundle() : PublishLatestValidPackage();
        }

        /// <summary>发布本次明确的构建资源；目标为空时沿用菜单的最近发布包选择规则。</summary>
        public static ResourceServerPublishResult PublishBuiltPackage(
            string packageDirectory, string version, BuildTarget target, string bundleDirectory = null,
            string hostBaselineId = null, bool newHost = false)
        {
            using var lease = WorkspaceBuildLock.Acquire(Directory.GetParent(Application.dataPath).FullName);
            if (target != BuildTarget.StandaloneWindows64 && target != BuildTarget.Android)
                throw new ArgumentException("不支持的发布平台。", nameof(target));
            string packageRoot = YooAssetBuildUtility.GetPackageRoot(target);
            packageDirectory = FullPath(packageDirectory);
            EnsureWithin(packageDirectory, packageRoot, "本次资源目录");
            if (ReadPackageVersion(packageDirectory) != version ||
                !YooAssetBuildUtility.ValidateBuildResult(packageDirectory, version))
                throw new InvalidOperationException("本次资源版本或产物校验失败。");
            string workspace = GetWorkspaceRoot();
            string releaseRoot = Path.Combine(workspace, "Release", "ResourceServer");
            if (target == BuildTarget.Android) releaseRoot = Path.Combine(releaseRoot, "Android");
            if (bundleDirectory != null)
            {
                bundleDirectory = FullPath(bundleDirectory);
                if (!string.Equals(Path.GetDirectoryName(bundleDirectory), FullPath(releaseRoot), StringComparison.OrdinalIgnoreCase) ||
                    !BundleNameRegex.IsMatch(Path.GetFileName(bundleDirectory)))
                    throw new InvalidOperationException("目标发布包不属于所选平台的发布目录。");
            }
            if (!string.IsNullOrEmpty(hostBaselineId))
            {
                BuildDataStore.Identifier(hostBaselineId);
                if (bundleDirectory == null)
                {
                    if (Directory.Exists(releaseRoot))
                        bundleDirectory = Directory.GetDirectories(releaseRoot)
                            .Where(path => BundleNameRegex.IsMatch(Path.GetFileName(path)) && ReadHostBaseline(path) == hostBaselineId)
                            .OrderByDescending(path => path, StringComparer.Ordinal).FirstOrDefault();
                    if (bundleDirectory == null && newHost)
                        return PublishCore(packageRoot, Path.Combine(workspace, "Server", "ResourceServer"),
                            releaseRoot, AvailableBundleTimestamp(releaseRoot), packageDirectory, hostBaselineId);
                    if (bundleDirectory == null)
                        throw new InvalidOperationException("所选宿主尚未发布资源服，禁止更新其他宿主发布包。");
                }
                if (ReadHostBaseline(bundleDirectory) != hostBaselineId)
                    throw new InvalidOperationException("发布目标的宿主基线与本次产物不一致。");
            }
            else
            {
                if (bundleDirectory == null) TryFindLatestBundle(releaseRoot, out bundleDirectory, out _);
                if (bundleDirectory != null && !string.IsNullOrEmpty(ReadHostBaseline(bundleDirectory)))
                    throw new InvalidOperationException("无基线归属的产物不能覆盖已绑定宿主的发布包。");
            }
            if (bundleDirectory != null)
                return UpdateCore(packageRoot, releaseRoot, packageDirectory, bundleDirectory);
            return PublishCore(packageRoot, Path.Combine(workspace, "Server", "ResourceServer"),
                releaseRoot, AvailableBundleTimestamp(releaseRoot), packageDirectory);
        }

        private static DateTime AvailableBundleTimestamp(string releaseRoot)
        {
            DateTime timestamp = DateTime.Now;
            while (Directory.Exists(Path.Combine(releaseRoot, $"ResourceServerBundle_{timestamp:yyyyMMdd_HHmmss}")) ||
                File.Exists(Path.Combine(releaseRoot, $"ResourceServerBundle_{timestamp:yyyyMMdd_HHmmss}")))
                timestamp = timestamp.AddSeconds(1);
            return timestamp;
        }

        private static string ReadHostBaseline(string bundle)
        {
            string path = Path.Combine(bundle, "host-baseline.id");
            BuildDataStore.NoLinks(path);
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }

        internal static ResourceServerPublishResult Publish(
            string packageRoot,
            string templateRoot,
            string releaseRoot,
            DateTime timestamp)
        {
            return PublishCore(packageRoot, templateRoot, releaseRoot, timestamp, null);
        }

        private static ResourceServerPublishResult PublishCore(
            string packageRoot, string templateRoot, string releaseRoot, DateTime timestamp, string selectedPackage,
            string hostBaselineId = null)
        {
            using var lease = WorkspaceBuildLock.Acquire(Directory.GetParent(Application.dataPath).FullName);
            string source = null;
            string target = null;
            string temp = null;
            try
            {
                packageRoot = FullPath(packageRoot);
                templateRoot = FullPath(templateRoot);
                releaseRoot = FullPath(releaseRoot);
                EnsureDirectoryExists(packageRoot, "Host 资源根目录");
                EnsureDirectoryExists(templateRoot, "资源服务器模板目录");
                EnsureNoticeFile(templateRoot);
                ValidateTemplate(templateRoot);
                ValidateNoticeFile(Path.Combine(templateRoot, "Notices", "resource-update-notices.json"));

                if (!TrySelectPackage(packageRoot, selectedPackage, out source, out var version, out var findError))
                {
                    return Fail(PublishOperation, "查找资源", packageRoot, releaseRoot,
                        $"未找到有效 Host 资源，请先执行对应平台的 Host 代码和资源构建。{findError}");
                }

                source = FullPath(source);
                EnsureWithin(source, packageRoot, "Host 资源目录");
                if (!YooAssetBuildUtility.ValidateBuildResult(source, version))
                {
                    return Fail(PublishOperation, "验证源资源", source, releaseRoot, "源 Host 资源校验失败");
                }

                Directory.CreateDirectory(releaseRoot);
                target = FullPath(Path.Combine(releaseRoot, $"ResourceServerBundle_{timestamp:yyyyMMdd_HHmmss}"));
                EnsureWithin(target, releaseRoot, "发布包目录");
                if (Directory.Exists(target) || File.Exists(target))
                {
                    return Fail(PublishOperation, "创建发布包", source, target, "目标发布包已存在，拒绝覆盖");
                }

                temp = FullPath(Path.Combine(releaseRoot, $".ResourceServerBundle_{timestamp:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.tmp"));
                EnsureTempUnderRelease(temp, releaseRoot);
                Directory.CreateDirectory(temp);
                CopyTemplate(templateRoot, temp);
                CopyDirectorySafe(source, Path.Combine(temp, ResourceFilesName));

                var tempResources = Path.Combine(temp, ResourceFilesName);
                if (!YooAssetBuildUtility.ValidateBuildResult(tempResources, version))
                {
                    throw new InvalidOperationException("临时 ResourceFiles 校验失败");
                }

                ValidateTemplate(temp);
                ValidateNoticeFile(Path.Combine(temp, "Notices", "resource-update-notices.json"));
                if (!string.IsNullOrEmpty(hostBaselineId))
                    File.WriteAllText(Path.Combine(temp, "host-baseline.id"), hostBaselineId, new System.Text.UTF8Encoding(false));
                Directory.Move(temp, target);
                temp = null;

                return new ResourceServerPublishResult
                {
                    Success = true,
                    Operation = PublishOperation,
                    Version = version,
                    SourcePackageDirectory = source,
                    BundleDirectory = target,
                    ResourceFilesSucceeded = true,
                    NoticeSyncSucceeded = true
                };
            }
            catch (Exception exception)
            {
                CleanupPublishTemp(temp, releaseRoot);
                return Fail(PublishOperation, "发布事务", source, target, exception.Message);
            }
        }

        internal static ResourceServerPublishResult Update(
            string packageRoot,
            string releaseRoot)
        {
            return UpdateCore(packageRoot, releaseRoot, null, null);
        }

        private static ResourceServerPublishResult UpdateCore(
            string packageRoot, string releaseRoot, string selectedPackage, string selectedBundle)
        {
            using var lease = WorkspaceBuildLock.Acquire(Directory.GetParent(Application.dataPath).FullName);
            string source = null;
            string bundle = null;
            string resourceFiles = null;
            string newDirectory = null;
            string backupDirectory = null;
            try
            {
                packageRoot = FullPath(packageRoot);
                releaseRoot = FullPath(releaseRoot);
                EnsureDirectoryExists(packageRoot, "Host 资源根目录");
                bundle = selectedBundle;
                string bundleError = null;
                if (bundle == null && !TryFindLatestBundle(releaseRoot, out bundle, out bundleError))
                {
                    return Fail(UpdateOperation, "查找发布包", packageRoot, releaseRoot, bundleError);
                }
                if (selectedPackage == null && !string.IsNullOrEmpty(ReadHostBaseline(bundle)))
                    return Fail(UpdateOperation, "校验宿主", packageRoot, bundle, "发布包已绑定宿主，请使用指定基线的构建产物发布。");

                resourceFiles = FullPath(Path.Combine(bundle, ResourceFilesName));
                EnsureDirectoryExists(resourceFiles, "当前 ResourceFiles");
                var previousVersion = ReadPackageVersion(resourceFiles);
                if (!YooAssetBuildUtility.ValidateBuildResult(resourceFiles, previousVersion))
                {
                    return Fail(UpdateOperation, "验证旧资源", resourceFiles, bundle, "旧 ResourceFiles 校验失败，阻止更新");
                }

                if (!TrySelectPackage(packageRoot, selectedPackage, out source, out var version, out var findError))
                {
                    return Fail(UpdateOperation, "查找资源", packageRoot, bundle,
                        $"未找到有效 Host 资源，请先执行对应平台的 Host 代码和资源构建。{findError}");
                }

                source = FullPath(source);
                EnsureWithin(source, packageRoot, "Host 资源目录");
                if (!YooAssetBuildUtility.ValidateBuildResult(source, version))
                {
                    return Fail(UpdateOperation, "验证新资源", source, bundle, "新 Host 资源校验失败");
                }

                var suffix = Guid.NewGuid().ToString("N");
                newDirectory = FullPath(Path.Combine(bundle, $".ResourceFiles_{suffix}.new"));
                backupDirectory = FullPath(Path.Combine(bundle, $".ResourceFiles_{suffix}.backup"));
                EnsureWithin(newDirectory, bundle, "新资源临时目录");
                EnsureWithin(backupDirectory, bundle, "旧资源备份目录");
                CopyDirectorySafe(source, newDirectory);
                if (!YooAssetBuildUtility.ValidateBuildResult(newDirectory, version))
                {
                    throw new InvalidOperationException("新资源临时目录校验失败");
                }

                Directory.Move(resourceFiles, backupDirectory);
                try
                {
                    BeforeCommitUpdateForTests?.Invoke(bundle);
                    Directory.Move(newDirectory, resourceFiles);
                    if (!YooAssetBuildUtility.ValidateBuildResult(resourceFiles, version))
                    {
                        throw new InvalidOperationException("正式 ResourceFiles 替换后校验失败");
                    }
                }
                catch (Exception commitException)
                {
                    RestoreBackup(resourceFiles, backupDirectory, previousVersion);
                    CleanupKnownTemp(newDirectory, bundle);
                    throw new InvalidOperationException($"替换失败，已恢复旧资源。原始原因：{commitException.Message}");
                }

                CleanupKnownTemp(backupDirectory, bundle);
                var templateRoot = Path.Combine(GetWorkspaceRoot(), "Server", "ResourceServer");
                try
                {
                    ValidateTemplate(templateRoot);
                    UpgradeServerSupportFiles(bundle, templateRoot);
                }
                catch (Exception supportException)
                {
                    return new ResourceServerPublishResult
                    {
                        Success = false,
                        Operation = UpdateOperation,
                        Version = version,
                        PreviousVersion = previousVersion,
                        SourcePackageDirectory = source,
                        BundleDirectory = bundle,
                        ResourceFilesSucceeded = true,
                        NoticeSyncSucceeded = false,
                        NoticeSyncError = supportException.Message,
                        Error = $"资源更新成功，服务器支持文件同步失败：{supportException.Message}"
                    };
                }

                var noticeSyncSucceeded = true;
                string noticeSyncError = string.Empty;
                try
                {
                    EnsureNoticeFile(templateRoot);
                    ValidateNoticeFile(Path.Combine(templateRoot, "Notices", "resource-update-notices.json"));
                    SyncNoticeFile(templateRoot, bundle);
                }
                catch (Exception noticeException)
                {
                    noticeSyncSucceeded = false;
                    noticeSyncError = noticeException.Message;
                }

                return new ResourceServerPublishResult
                {
                    Success = noticeSyncSucceeded,
                    Operation = UpdateOperation,
                    Version = version,
                    PreviousVersion = previousVersion,
                    SourcePackageDirectory = source,
                    BundleDirectory = bundle,
                    ResourceFilesSucceeded = true,
                    NoticeSyncSucceeded = noticeSyncSucceeded,
                    NoticeSyncError = noticeSyncError,
                    Error = noticeSyncSucceeded
                        ? string.Empty
                        : $"资源更新成功，公告同步失败：{noticeSyncError}"
                };
            }
            catch (Exception exception)
            {
                CleanupKnownTemp(newDirectory, bundle);
                return Fail(UpdateOperation, "更新事务", source, bundle, exception.Message);
            }
            finally
            {
                BeforeCommitUpdateForTests = null;
                BeforeCommitServerSupportForTests = null;
                AfterBackupServerSupportFileForTests = null;
                AfterRestoreServerSupportFileForTests = null;
            }
        }

        private static bool TrySelectPackage(string packageRoot, string selectedPackage,
            out string source, out string version, out string error)
        {
            if (selectedPackage == null)
                return YooAssetBuildUtility.TryFindLatestValidPackage(packageRoot, out source, out version, out error);
            source = selectedPackage;
            version = ReadPackageVersion(source);
            error = null;
            return true;
        }

        internal static bool TryFindLatestBundle(
            string releaseRoot,
            out string bundleDirectory,
            out string error)
        {
            bundleDirectory = null;
            releaseRoot = FullPath(releaseRoot);
            if (!Directory.Exists(releaseRoot))
            {
                error = "未找到 Release/ResourceServer/ResourceServerBundle_yyyyMMdd_HHmmss，请先执行“发布资源服”。";
                return false;
            }

            var candidates = new List<Tuple<DateTime, string>>();
            foreach (var directory in Directory.GetDirectories(releaseRoot))
            {
                var name = Path.GetFileName(directory);
                var match = BundleNameRegex.Match(name);
                if (!match.Success)
                {
                    continue;
                }

                var stamp = $"{match.Groups[1].Value}_{match.Groups[2].Value}";
                if (DateTime.TryParseExact(
                        stamp,
                        "yyyyMMdd_HHmmss",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var timestamp))
                {
                    candidates.Add(Tuple.Create(timestamp, FullPath(directory)));
                }
            }

            if (candidates.Count == 0)
            {
                error = "未找到 Release/ResourceServer/ResourceServerBundle_yyyyMMdd_HHmmss，请先执行“发布资源服”。";
                return false;
            }

            bundleDirectory = candidates
                .OrderByDescending(item => item.Item1)
                .First()
                .Item2;
            error = null;
            return true;
        }

        /// <summary>仅升级已发布目录的服务脚本，不替换 ResourceFiles 或公告内容。</summary>
        internal static void SyncServerSupportFiles(string bundleDirectory)
        {
            string bundle = FullPath(bundleDirectory);
            string templateRoot = Path.Combine(GetWorkspaceRoot(), "Server", "ResourceServer");
            EnsureDirectoryExists(bundle, "资源服务器发布目录");
            ValidateTemplate(templateRoot);
            UpgradeServerSupportFiles(bundle, templateRoot);
        }

        internal static bool IsNoticeCapableBundle(
            string bundleDirectory,
            string templateRoot,
            out string error)
        {
            var releaseRoot = Path.Combine(GetWorkspaceRoot(), "Release", "ResourceServer");
            return IsNoticeCapableBundle(bundleDirectory, templateRoot, releaseRoot, out error);
        }

        internal static bool IsNoticeCapableBundle(
            string bundleDirectory,
            string templateRoot,
            string releaseRoot,
            out string error)
        {
            try
            {
                bundleDirectory = FullPath(bundleDirectory);
                templateRoot = FullPath(templateRoot);
                releaseRoot = FullPath(releaseRoot);
                if (!IsWithin(bundleDirectory, releaseRoot))
                {
                    error = "发布包必须位于 Release/ResourceServer 目录下。";
                    return false;
                }

                if (!Directory.Exists(bundleDirectory) ||
                    !BundleNameRegex.IsMatch(Path.GetFileName(bundleDirectory)))
                {
                    error = "发布包名称不符合 ResourceServerBundle_yyyyMMdd_HHmmss。";
                    return false;
                }

                var resourceFiles = Path.Combine(bundleDirectory, ResourceFilesName);
                var version = ReadPackageVersion(resourceFiles);
                if (!YooAssetBuildUtility.ValidateBuildResult(resourceFiles, version))
                {
                    error = "ResourceFiles 校验失败。";
                    return false;
                }

                ValidateTemplate(templateRoot);
                foreach (var fileName in TemplateFiles)
                {
                    if (!FilesEqual(Path.Combine(templateRoot, fileName), Path.Combine(bundleDirectory, fileName)))
                    {
                        error = $"服务器支持文件不一致：{fileName}";
                        return false;
                    }
                }

                ValidateNoticeFile(Path.Combine(bundleDirectory, "Notices", "resource-update-notices.json"));
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static string GetWorkspaceRoot()
        {
            string clientRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("无法解析 Client 项目根目录。");
            return Directory.GetParent(clientRoot)?.FullName
                ?? throw new InvalidOperationException("无法解析工作区根目录。");
        }

        private static string ReadPackageVersion(string packageDirectory)
        {
            var versionFile = Path.Combine(packageDirectory, $"{PackageName}.version");
            if (!File.Exists(versionFile))
            {
                throw new FileNotFoundException($"版本文件不存在：{versionFile}");
            }

            var version = File.ReadAllText(versionFile).Trim();
            if (string.IsNullOrWhiteSpace(version))
            {
                throw new InvalidOperationException($"版本文件为空：{versionFile}");
            }

            return version;
        }

        private static void ValidateTemplate(string templateRoot)
        {
            foreach (var fileName in TemplateFiles)
            {
                var path = Path.Combine(templateRoot, fileName);
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException($"模板文件缺失：{path}");
                }

                if (new FileInfo(path).Length == 0)
                {
                    throw new InvalidOperationException($"模板文件为空：{path}");
                }
            }

            var notices = Path.Combine(templateRoot, "Notices");
            if (!Directory.Exists(notices))
            {
                throw new DirectoryNotFoundException($"模板 Notices 目录缺失：{notices}");
            }
        }

        private static void CopyTemplate(string templateRoot, string targetRoot)
        {
            foreach (var fileName in TemplateFiles)
            {
                CopyFileSafe(Path.Combine(templateRoot, fileName), Path.Combine(targetRoot, fileName));
            }

            CopyDirectorySafe(Path.Combine(templateRoot, "Notices"), Path.Combine(targetRoot, "Notices"));
        }

        private static void EnsureNoticeFile(string templateRoot)
        {
            var notices = Path.Combine(templateRoot, "Notices");
            Directory.CreateDirectory(notices);
            var noticeFile = Path.Combine(notices, "resource-update-notices.json");
            if (!File.Exists(noticeFile))
            {
                File.WriteAllText(
                    noticeFile,
                    "{\n  \"schemaVersion\": 1,\n  \"defaultLanguage\": \"zh-CN\",\n  \"notices\": []\n}\n",
                    new System.Text.UTF8Encoding(false));
            }
        }

        private static void ValidateNoticeFile(string noticeFile)
        {
            if (!File.Exists(noticeFile))
            {
                throw new FileNotFoundException($"公告文件缺失：{noticeFile}");
            }

            var json = File.ReadAllText(noticeFile, System.Text.Encoding.UTF8);
            var result = ResourceUpdateNoticeDocument.TryParse(json, out _);
            if (!result.Success)
            {
                throw new InvalidOperationException($"公告校验失败：{result.Error}");
            }
        }

        private static void SyncNoticeFile(string templateRoot, string bundle)
        {
            var source = Path.Combine(templateRoot, "Notices", "resource-update-notices.json");
            var targetDirectory = Path.Combine(bundle, "Notices");
            var target = Path.Combine(targetDirectory, "resource-update-notices.json");
            Directory.CreateDirectory(targetDirectory);
            var temp = Path.Combine(targetDirectory, $".resource-update-notices.{Guid.NewGuid():N}.tmp");
            CopyFileSafe(source, temp);
            ValidateNoticeFile(temp);
            if (File.Exists(target))
            {
                File.Replace(temp, target, null);
            }
            else
            {
                File.Move(temp, target);
            }
        }

        private static void UpgradeServerSupportFiles(string bundle, string templateRoot)
        {
            var suffix = Guid.NewGuid().ToString("N");
            var newDirectory = FullPath(Path.Combine(bundle, $".ServerSupport_{suffix}.new"));
            var backupDirectory = FullPath(Path.Combine(bundle, $".ServerSupport_{suffix}.backup"));
            var existedBefore = TemplateFiles.ToDictionary(
                fileName => fileName,
                fileName => File.Exists(Path.Combine(bundle, fileName)),
                StringComparer.Ordinal);
            var transactionSucceeded = false;
            var restoreSucceeded = false;
            try
            {
                EnsureWithin(newDirectory, bundle, "服务器支持文件临时目录");
                EnsureWithin(backupDirectory, bundle, "服务器支持文件备份目录");
                Directory.CreateDirectory(newDirectory);
                Directory.CreateDirectory(backupDirectory);
                foreach (var fileName in TemplateFiles)
                {
                    CopyFileSafe(Path.Combine(templateRoot, fileName), Path.Combine(newDirectory, fileName));
                    if (!FilesEqual(Path.Combine(templateRoot, fileName), Path.Combine(newDirectory, fileName)))
                    {
                        throw new InvalidOperationException($"服务器支持文件临时校验失败：{fileName}");
                    }
                }

                try
                {
                    foreach (var fileName in TemplateFiles)
                    {
                        var current = Path.Combine(bundle, fileName);
                        if (File.Exists(current))
                        {
                            File.Move(current, Path.Combine(backupDirectory, fileName));
                            AfterBackupServerSupportFileForTests?.Invoke(bundle, fileName);
                        }
                    }

                    BeforeCommitServerSupportForTests?.Invoke(bundle);
                    foreach (var fileName in TemplateFiles)
                    {
                        File.Move(Path.Combine(newDirectory, fileName), Path.Combine(bundle, fileName));
                    }

                    foreach (var fileName in TemplateFiles)
                    {
                        if (!FilesEqual(Path.Combine(templateRoot, fileName), Path.Combine(bundle, fileName)))
                        {
                            throw new InvalidOperationException($"服务器支持文件正式校验失败：{fileName}");
                        }
                    }

                    transactionSucceeded = true;
                }
                catch (Exception transactionException)
                {
                    try
                    {
                        RestoreServerSupportFiles(bundle, backupDirectory, existedBefore);
                        restoreSucceeded = true;
                    }
                    catch (Exception restoreException)
                    {
                        throw new InvalidOperationException(
                            $"服务器支持文件同步失败且恢复失败，请人工检查发布包：{restoreException.Message}",
                            transactionException);
                    }

                    throw;
                }
            }
            finally
            {
                CleanupKnownTemp(newDirectory, bundle);
                if (transactionSucceeded ||
                    restoreSucceeded ||
                    !Directory.Exists(backupDirectory) ||
                    Directory.GetFileSystemEntries(backupDirectory).Length == 0)
                {
                    CleanupKnownTemp(backupDirectory, bundle);
                }
            }
        }

        private static void RestoreServerSupportFiles(
            string bundle,
            string backupDirectory,
            IReadOnlyDictionary<string, bool> existedBefore)
        {
            foreach (var fileName in TemplateFiles)
            {
                var current = Path.Combine(bundle, fileName);
                var backup = Path.Combine(backupDirectory, fileName);
                if (File.Exists(backup))
                {
                    if (File.Exists(current))
                    {
                        File.Delete(current);
                    }

                    File.Move(backup, current);
                    AfterRestoreServerSupportFileForTests?.Invoke(bundle, fileName);
                    continue;
                }

                if (!existedBefore.TryGetValue(fileName, out var existed) || existed)
                {
                    continue;
                }

                if (File.Exists(current))
                {
                    File.Delete(current);
                }
            }
        }
        private static void CopyDirectorySafe(string sourceDirectory, string targetDirectory)
        {
            sourceDirectory = FullPath(sourceDirectory);
            targetDirectory = FullPath(targetDirectory);
            EnsureDirectoryExists(sourceDirectory, "复制源目录");
            RejectSameOrNested(sourceDirectory, targetDirectory);
            if (Directory.Exists(targetDirectory) || File.Exists(targetDirectory))
            {
                throw new InvalidOperationException($"复制目标已存在：{targetDirectory}");
            }

            RejectReparsePoint(sourceDirectory);
            Directory.CreateDirectory(targetDirectory);
            foreach (var directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                RejectReparsePoint(directory);
                var relative = GetRelativePath(sourceDirectory, directory);
                Directory.CreateDirectory(Path.Combine(targetDirectory, relative));
            }

            foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                RejectReparsePoint(file);
                var relative = GetRelativePath(sourceDirectory, file);
                CopyFileSafe(file, Path.Combine(targetDirectory, relative));
            }

            CompareDirectoryLengths(sourceDirectory, targetDirectory);
        }

        private static void CopyFileSafe(string sourceFile, string targetFile)
        {
            sourceFile = FullPath(sourceFile);
            targetFile = FullPath(targetFile);
            RejectReparsePoint(sourceFile);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile));
            File.Copy(sourceFile, targetFile, false);
            if (new FileInfo(sourceFile).Length != new FileInfo(targetFile).Length)
            {
                throw new InvalidOperationException($"复制后长度不一致：{sourceFile}");
            }
        }

        private static void CompareDirectoryLengths(string sourceDirectory, string targetDirectory)
        {
            var sourceFiles = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories)
                .ToDictionary(file => GetRelativePath(sourceDirectory, file), file => new FileInfo(file).Length, StringComparer.OrdinalIgnoreCase);
            var targetFiles = Directory.GetFiles(targetDirectory, "*", SearchOption.AllDirectories)
                .ToDictionary(file => GetRelativePath(targetDirectory, file), file => new FileInfo(file).Length, StringComparer.OrdinalIgnoreCase);
            if (sourceFiles.Count != targetFiles.Count)
            {
                throw new InvalidOperationException("复制后文件集合数量不一致");
            }

            foreach (var pair in sourceFiles)
            {
                if (!targetFiles.TryGetValue(pair.Key, out var length) || length != pair.Value)
                {
                    throw new InvalidOperationException($"复制后文件不一致：{pair.Key}");
                }
            }
        }

        private static void RestoreBackup(string resourceFiles, string backupDirectory, string previousVersion)
        {
            if (Directory.Exists(resourceFiles))
            {
                var failedDirectory = FullPath(resourceFiles + ".failed_" + Guid.NewGuid().ToString("N"));
                Directory.Move(resourceFiles, failedDirectory);
                CleanupKnownTemp(failedDirectory, Directory.GetParent(resourceFiles).FullName);
            }

            if (!Directory.Exists(backupDirectory))
            {
                throw new InvalidOperationException("恢复失败，发布包需要人工检查：备份目录不存在");
            }

            Directory.Move(backupDirectory, resourceFiles);
            if (!YooAssetBuildUtility.ValidateBuildResult(resourceFiles, previousVersion))
            {
                throw new InvalidOperationException("恢复失败，发布包需要人工检查：旧 ResourceFiles 校验失败");
            }
        }

        private static void CleanupPublishTemp(string temp, string releaseRoot)
        {
            if (string.IsNullOrWhiteSpace(temp) || string.IsNullOrWhiteSpace(releaseRoot))
            {
                return;
            }

            temp = FullPath(temp);
            releaseRoot = FullPath(releaseRoot);
            if (Directory.Exists(temp) &&
                Path.GetFileName(temp).StartsWith(".", StringComparison.Ordinal) &&
                Path.GetFileName(temp).EndsWith(".tmp", StringComparison.Ordinal) &&
                IsWithin(temp, releaseRoot))
            {
                Directory.Delete(temp, true);
            }
        }

        private static void CleanupKnownTemp(string directory, string ownerRoot)
        {
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(ownerRoot))
            {
                return;
            }

            directory = FullPath(directory);
            ownerRoot = FullPath(ownerRoot);
            if (Directory.Exists(directory) && IsWithin(directory, ownerRoot))
            {
                var name = Path.GetFileName(directory);
                if (name.StartsWith(".ResourceFiles_", StringComparison.Ordinal) ||
                    name.StartsWith(".ServerSupport_", StringComparison.Ordinal) ||
                    name.Contains(".failed_", StringComparison.Ordinal))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        private static void EnsureTempUnderRelease(string temp, string releaseRoot)
        {
            EnsureWithin(temp, releaseRoot, "临时发布目录");
            var name = Path.GetFileName(temp);
            if (!name.StartsWith(".", StringComparison.Ordinal) || !name.EndsWith(".tmp", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"临时目录名称非法：{temp}");
            }
        }

        private static void RejectSameOrNested(string source, string target)
        {
            if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase) ||
                IsWithin(source, target) ||
                IsWithin(target, source))
            {
                throw new InvalidOperationException($"源目录和目标目录相同或互为父子：{source} -> {target}");
            }
        }

        private static void RejectReparsePoint(string path)
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException($"拒绝复制 ReparsePoint：{path}");
            }
        }

        private static void EnsureDirectoryExists(string path, string label)
        {
            if (!Directory.Exists(path))
            {
                throw new DirectoryNotFoundException($"{label}不存在：{path}");
            }
        }

        private static void EnsureWithin(string target, string allowedRoot, string label)
        {
            if (!IsWithin(target, allowedRoot))
            {
                throw new InvalidOperationException($"{label}越界：{target}");
            }
        }

        private static bool IsWithin(string target, string allowedRoot)
        {
            target = FullPath(target);
            allowedRoot = FullPath(allowedRoot);
            return target.StartsWith(allowedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetRelativePath(string root, string path)
        {
            var rootUri = new Uri(AppendSeparator(FullPath(root)));
            var pathUri = new Uri(FullPath(path));
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString()).Replace('/', Path.DirectorySeparatorChar);
        }

        private static string AppendSeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? path
                : path + Path.DirectorySeparatorChar;
        }

        private static string FullPath(string path)
        {
            return Path.GetFullPath(path);
        }

        private static bool FilesEqual(string left, string right)
        {
            if (!File.Exists(left) || !File.Exists(right))
            {
                return false;
            }

            var leftInfo = new FileInfo(left);
            var rightInfo = new FileInfo(right);
            if (leftInfo.Length != rightInfo.Length)
            {
                return false;
            }

            using (var leftStream = File.OpenRead(left))
            using (var rightStream = File.OpenRead(right))
            {
                int value;
                while ((value = leftStream.ReadByte()) >= 0)
                {
                    if (value != rightStream.ReadByte())
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static ResourceServerPublishResult Fail(
            string operation,
            string stage,
            string source,
            string target,
            string reason)
        {
            return new ResourceServerPublishResult
            {
                Success = false,
                Operation = operation,
                SourcePackageDirectory = source,
                BundleDirectory = target,
                Error = $"阶段：{stage}；源：{source ?? string.Empty}；目标：{target ?? string.Empty}；原因：{reason}"
            };
        }
    }
}
#endif
