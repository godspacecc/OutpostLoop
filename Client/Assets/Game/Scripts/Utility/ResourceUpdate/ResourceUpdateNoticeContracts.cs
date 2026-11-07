using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using QFramework;

namespace Game
{
    /// <summary>
    /// 表达一次资源更新公告请求所需的服务器、目标版本、语言和超时参数。
    /// </summary>
    public readonly struct ResourceUpdateNoticeRequest
    {
        public ResourceUpdateNoticeRequest(
            string resourceServer,
            string version,
            string languageCode,
            int timeoutSeconds)
        {
            ResourceServer = resourceServer ?? string.Empty;
            Version = version ?? string.Empty;
            LanguageCode = languageCode ?? string.Empty;
            TimeoutSeconds = Math.Max(1, timeoutSeconds);
        }

        public string ResourceServer { get; }
        public string Version { get; }
        public string LanguageCode { get; }
        public int TimeoutSeconds { get; }
    }

    /// <summary>
    /// 保存已经按语言选择后的可展示公告标题、摘要和普通文本条目。
    /// </summary>
    public readonly struct ResourceUpdateNoticeContent
    {
        public ResourceUpdateNoticeContent(
            string version,
            string title,
            string summary,
            IReadOnlyList<string> items)
        {
            Version = version ?? string.Empty;
            Title = title ?? string.Empty;
            Summary = summary ?? string.Empty;
            Items = items ?? Array.Empty<string>();
        }

        public string Version { get; }
        public string Title { get; }
        public string Summary { get; }
        public IReadOnlyList<string> Items { get; }

        public bool IsDisplayable =>
            !string.IsNullOrWhiteSpace(Title) ||
            !string.IsNullOrWhiteSpace(Summary) ||
            Items.Count > 0;
    }

    /// <summary>
    /// 表达公告附加信息获取结果，失败只影响公告本身，不复用资源更新状态。
    /// </summary>
    public readonly struct ResourceUpdateNoticeResult
    {
        public ResourceUpdateNoticeResult(
            bool success,
            bool hasNotice,
            ResourceUpdateNoticeContent content,
            string error)
        {
            Success = success;
            HasNotice = hasNotice && content.IsDisplayable;
            Content = content;
            Error = error ?? string.Empty;
        }

        public bool Success { get; }
        public bool HasNotice { get; }
        public ResourceUpdateNoticeContent Content { get; }
        public string Error { get; }

        public static ResourceUpdateNoticeResult NoNotice =>
            new ResourceUpdateNoticeResult(true, false, default, string.Empty);

        public static ResourceUpdateNoticeResult Ok(ResourceUpdateNoticeContent content)
        {
            return new ResourceUpdateNoticeResult(true, content.IsDisplayable, content, string.Empty);
        }

        public static ResourceUpdateNoticeResult Fail(string error)
        {
            return new ResourceUpdateNoticeResult(false, false, default, error);
        }
    }

    /// <summary>
    /// 组合主动资源检查快照与同版本公告结果，供主界面一次性展示。
    /// </summary>
    public readonly struct ResourceUpdateCheckWithNoticeResult
    {
        public ResourceUpdateCheckWithNoticeResult(
            ResourceUpdateSnapshot snapshot,
            ResourceUpdateNoticeResult notice,
            ApplicationUpdateCandidate applicationUpdate = null)
        {
            Snapshot = snapshot;
            Notice = notice;
            ApplicationUpdate = applicationUpdate;
        }

        public ResourceUpdateSnapshot Snapshot { get; }
        public ResourceUpdateNoticeResult Notice { get; }
        public ApplicationUpdateCandidate ApplicationUpdate { get; }
    }

    /// <summary>
    /// 定义独立公告客户端边界，由资源更新系统协调调用，UI 不直接发起网络请求。
    /// </summary>
    public interface IResourceUpdateNoticeClient : IUtility
    {
        UniTask<ResourceUpdateNoticeResult> FetchAsync(ResourceUpdateNoticeRequest request);
    }
}
