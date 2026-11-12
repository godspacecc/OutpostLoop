using System;
using Cysharp.Threading.Tasks;
using UnityEngine.Networking;

namespace Game
{
    /// <summary>
    /// 使用 UnityWebRequest 只读取资源服务器 OutpostLoop.version 的版本探测器。
    /// </summary>
    public sealed class UnityWebRequestResourceVersionProbe :
        IRemoteResourceVersionProbe
    {
        public async UniTask<RemoteResourceVersionProbeResult> ProbeAsync(
            string resourceServer,
            int timeoutSeconds)
        {
            if (!Uri.TryCreate(resourceServer, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp &&
                 uri.Scheme != Uri.UriSchemeHttps))
            {
                return RemoteResourceVersionProbeResult.Fail(
                    "资源服务器地址无效");
            }

            string url =
                $"{resourceServer.TrimEnd('/')}/OutpostLoop.version";
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = Math.Max(1, timeoutSeconds);
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    await UniTask.Yield();
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    return RemoteResourceVersionProbeResult.Fail(
                        $"请求资源服务器版本失败：{request.error}");
                }

                string version = request.downloadHandler.text?.Trim();
                return string.IsNullOrWhiteSpace(version)
                    ? RemoteResourceVersionProbeResult.Fail(
                        "资源服务器版本为空")
                    : RemoteResourceVersionProbeResult.Ok(version);
            }
        }
    }
}
