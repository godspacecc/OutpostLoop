#if UNITY_EDITOR
using System;
using System.IO;
using Boot;
using UnityEditor;

namespace Game.Editor
{
    /// <summary>本次构建的明确产物；只构建入口返回它，发布步骤不再重新查找源资源。</summary>
    public sealed class BuildArtifacts
    {
        public BuildTarget Target;
        public string ResourceVersion;
        public string PackageDirectory;
        public string PlayerPath;
        public string InstallerDirectory;
        public string UpdateManifestPath;
        public string AppVersion;
        public int AppVersionCode;
        public string HostBaselineId;

        /// <summary>独立发布本次产物，不构建或启动服务；目标可显式指定。</summary>
        public ResourceServerPublishResult Publish(string bundleDirectory = null)
        {
            using var lease = WorkspaceBuildLock.Acquire(Directory.GetParent(UnityEngine.Application.dataPath).FullName);
            Validate();
            var result = ResourceServerPublishUtility.PublishBuiltPackage(
                PackageDirectory, ResourceVersion, Target, bundleDirectory, HostBaselineId, !string.IsNullOrEmpty(PlayerPath));
            if (!result.Success)
                throw new InvalidOperationException("资源发布失败：" + result.Error);
            CiBuildProgress.Artifact("PublishedBundle", result.BundleDirectory);
            if (!string.IsNullOrEmpty(PlayerPath))
            {
                if (Target == BuildTarget.StandaloneWindows64)
                    WindowsInstallerPublishUtility.PublishGenerated(InstallerDirectory, result.BundleDirectory, ResourceVersion);
                else
                    AndroidApkPublishUtility.PublishPrepared(PlayerPath, UpdateManifestPath, result.BundleDirectory);
            }
            return result;
        }

        /// <summary>独立校验本次产物，不发布、不启动服务。</summary>
        public void Validate()
        {
            if (Target != BuildTarget.StandaloneWindows64 && Target != BuildTarget.Android)
                throw new InvalidOperationException("产物平台无效。");
            // 安装包先校验，避免已知损坏的产物推进资源发布。
            if (!string.IsNullOrEmpty(PlayerPath))
            {
                if (!File.Exists(PlayerPath) || new FileInfo(PlayerPath).Length <= 0)
                    throw new InvalidOperationException("构建安装包或 Player 文件缺失或为空。");
                if (Target == BuildTarget.StandaloneWindows64)
                {
                    var identity = WindowsUpdateManifest.ParseAvailable(
                        File.ReadAllText(UpdateManifestPath), 0);
                    if (identity == null || identity.versionName != AppVersion || identity.releaseNumber != AppVersionCode)
                        throw new InvalidOperationException("安装器清单与本次构建身份不一致。");
                    WindowsInstallerPublishUtility.ValidateInstaller(InstallerDirectory, identity.releaseNumber);
                }
                else if (Target == BuildTarget.Android)
                {
                    var manifest = ApkUpdateManifest.ParseAvailable(File.ReadAllText(UpdateManifestPath),
                        "com.godspace.outpostloop", AppVersion, AppVersionCode, false);
                    if (manifest == null || manifest.versionName != AppVersion || manifest.versionCode != AppVersionCode ||
                        !manifest.VerifyFile(PlayerPath))
                        throw new InvalidOperationException("APK 清单或产物与本次构建不一致。");
                }
            }
            if (!BuildDataStore.IsWithin(PackageDirectory, YooAssetBuildUtility.GetPackageRoot(Target)) ||
                string.IsNullOrWhiteSpace(ResourceVersion) || !YooAssetBuildUtility.ValidateBuildResult(PackageDirectory, ResourceVersion))
                throw new InvalidOperationException("本次资源目录、版本或产物校验失败。");
        }

        /// <summary>只构建入口同步返回结果，禁止通过切平台或忙碌返回跳过执行。</summary>
        internal static void RequireReady(BuildTarget target)
        {
            if (BuildUtility.HasActiveWorkflow || AndroidBuildUtility.HasActiveWorkflow)
                throw new InvalidOperationException("已有构建工作流，未执行只构建请求。");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("编辑器尚未就绪，未执行只构建请求。");
            if (EditorUserBuildSettings.activeBuildTarget != target)
                throw new InvalidOperationException($"只构建入口要求预先选择平台 {target}；批处理请使用 -buildTarget 指定平台。");
        }
    }
}
#endif
