using System;
using Cysharp.Threading.Tasks;
using YooAsset;

namespace Game
{
    /// <summary>
    /// 使用 YooAsset 查询远端包版本、清单和下载规模，失败时返回带上下文的检查结果。
    /// </summary>
    public sealed class YooAssetResourceUpdateRuntimeChecker : IResourceUpdateRuntimeChecker
    {
        private const string PackageName = "OutpostLoop";
        private const int DownloadingMaxNumber = 10;
        private const int FailedTryAgain = 3;

        public bool CanCheck => YooAssets.TryGetPackage(PackageName) != null;

        public UniTask<ResourceUpdateCheckResult> CheckAsync(int timeoutSeconds)
        {
            return CheckInternal(Math.Max(1, timeoutSeconds));
        }

        private static async UniTask<ResourceUpdateCheckResult> CheckInternal(int timeoutSeconds)
        {
            var package = YooAssets.TryGetPackage(PackageName);
            if (package == null)
            {
                return ResourceUpdateCheckResult.Fail(string.Empty, "资源更新检查失败：OutpostLoop 为空");
            }

            var currentVersion = package.GetPackageVersion() ?? string.Empty;
            var versionOperation = package.RequestPackageVersionAsync(true, timeoutSeconds);
            while (!versionOperation.IsDone)
            {
                await UniTask.Yield();
            }

            if (versionOperation.Status != EOperationStatus.Succeed)
            {
                return ResourceUpdateCheckResult.Fail(currentVersion, $"请求资源版本失败：{versionOperation.Error}");
            }

            var targetVersion = versionOperation.PackageVersion ?? string.Empty;
            if (string.Equals(currentVersion, targetVersion, StringComparison.Ordinal))
            {
                return ResourceUpdateCheckResult.Ok(new ResourceUpdateSnapshot(
                    ResourceUpdateStatus.NoUpdate,
                    currentVersion,
                    targetVersion,
                    0,
                    0,
                    string.Empty));
            }

            var preDownloadOperation = package.PreDownloadContentAsync(targetVersion, timeoutSeconds);
            while (!preDownloadOperation.IsDone)
            {
                await UniTask.Yield();
            }

            if (preDownloadOperation.Status != EOperationStatus.Succeed)
            {
                return ResourceUpdateCheckResult.Fail(currentVersion, $"预下载资源清单失败：{preDownloadOperation.Error}");
            }

            var downloader = preDownloadOperation.CreateResourceDownloader(DownloadingMaxNumber, FailedTryAgain);
            var count = downloader?.TotalDownloadCount ?? 0;
            var bytes = downloader?.TotalDownloadBytes ?? 0L;
            // 下载规模只表示本地缺少的文件数；新版清单和代码仍需重启后才会生效。
            return ResourceUpdateCheckResult.Ok(new ResourceUpdateSnapshot(
                ResourceUpdateStatus.UpdateAvailable,
                currentVersion,
                targetVersion,
                count,
                bytes,
                string.Empty,
                requiresRestartToUpdate: true));
        }
    }
}
