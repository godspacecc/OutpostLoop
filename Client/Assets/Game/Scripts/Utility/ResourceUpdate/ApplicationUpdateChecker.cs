using System;
using System.Threading;
using Boot;
using Cysharp.Threading.Tasks;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>应用安装包版本检查端口；只返回兼容候选，不下载或安装。</summary>
    public interface IApplicationUpdateChecker : IUtility
    {
        UniTask<ApplicationUpdateCandidate> CheckAsync(string resourceServer, CancellationToken cancellation);
    }

    /// <summary>在 Game 中检查可选安装包更新；宿主只提供平台能力与已安装版本。</summary>
    public sealed class ApplicationUpdateChecker : IApplicationUpdateChecker
    {
        public async UniTask<ApplicationUpdateCandidate> CheckAsync(string resourceServer, CancellationToken cancellation)
        {
            if (!BootHost.SupportsApplicationUpdate) return null;
            if (string.IsNullOrWhiteSpace(resourceServer))
                throw new InvalidOperationException("资源服务器地址为空");
            return await ApplicationUpdateRequest.CheckAsync(resourceServer, Application.identifier, Application.version,
                BootHost.InstalledVersionCode, cancellation);
        }
    }
}
