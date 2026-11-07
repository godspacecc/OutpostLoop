using System;
using Boot;
using Cysharp.Threading.Tasks;
using QFramework;

namespace Game
{
    /// <summary>
    /// 描述资源版本检查状态，区分未检查、检查中、无更新、有更新和检查失败。
    /// </summary>
    public enum ResourceUpdateStatus
    {
        NotChecked,
        Checking,
        NoUpdate,
        UpdateAvailable,
        CheckFailed
    }

    /// <summary>
    /// 表达一次资源版本检查的不可变快照，包含版本、下载规模、启动来源和仅在失败时使用的技术错误。
    /// </summary>
    public readonly struct ResourceUpdateSnapshot
    {
        public ResourceUpdateSnapshot(
            ResourceUpdateStatus status,
            string currentVersion,
            string targetVersion,
            int totalDownloadCount,
            long totalDownloadBytes,
            string error,
            BootResourceSource resourceSource = BootResourceSource.HostLatest,
            string resourceServer = "",
            bool requiresRestartToUpdate = false,
            bool hasKnownDownloadSize = true)
        {
            Status = status;
            CurrentVersion = currentVersion ?? string.Empty;
            TargetVersion = targetVersion ?? string.Empty;
            TotalDownloadCount = Math.Max(0, totalDownloadCount);
            TotalDownloadBytes = Math.Max(0L, totalDownloadBytes);
            Error = error ?? string.Empty;
            ResourceSource = resourceSource;
            ResourceServer = resourceServer ?? string.Empty;
            RequiresRestartToUpdate = requiresRestartToUpdate;
            HasKnownDownloadSize = hasKnownDownloadSize;
        }

        public ResourceUpdateStatus Status { get; }
        public string CurrentVersion { get; }
        public string TargetVersion { get; }
        public int TotalDownloadCount { get; }
        public long TotalDownloadBytes { get; }
        public string Error { get; }
        public BootResourceSource ResourceSource { get; }
        public string ResourceServer { get; }
        public bool RequiresRestartToUpdate { get; }
        public bool HasKnownDownloadSize { get; }

        public static ResourceUpdateSnapshot NotChecked =>
            new ResourceUpdateSnapshot(ResourceUpdateStatus.NotChecked, string.Empty, string.Empty, 0, 0, string.Empty);
    }

    /// <summary>
    /// 表示资源版本检查实现的返回结果，成功携带快照，失败保留当前版本和诊断原因。
    /// </summary>
    public readonly struct ResourceUpdateCheckResult
    {
        public ResourceUpdateCheckResult(bool success, ResourceUpdateSnapshot snapshot, string error)
        {
            Success = success;
            Snapshot = snapshot;
            Error = error ?? string.Empty;
        }

        public bool Success { get; }
        public ResourceUpdateSnapshot Snapshot { get; }
        public string Error { get; }

        public static ResourceUpdateCheckResult Ok(ResourceUpdateSnapshot snapshot)
        {
            return new ResourceUpdateCheckResult(true, snapshot, string.Empty);
        }

        public static ResourceUpdateCheckResult Fail(string currentVersion, string error)
        {
            return new ResourceUpdateCheckResult(false,
                new ResourceUpdateSnapshot(ResourceUpdateStatus.CheckFailed, currentVersion, string.Empty, 0, 0, error),
                error);
        }
    }

    /// <summary>
    /// 定义运行时资源版本检查边界，只检查远端版本和下载规模，不执行下载更新。
    /// </summary>
    public interface IResourceUpdateRuntimeChecker : IUtility
    {
        bool CanCheck { get; }
        UniTask<ResourceUpdateCheckResult> CheckAsync(int timeoutSeconds);
    }

    /// <summary>
    /// 资源检查快照变化通知，供启动界面刷新状态展示。
    /// </summary>
    public readonly struct ResourceUpdateChangedEvent
    {
        public ResourceUpdateChangedEvent(ResourceUpdateSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public ResourceUpdateSnapshot Snapshot { get; }
    }
}
