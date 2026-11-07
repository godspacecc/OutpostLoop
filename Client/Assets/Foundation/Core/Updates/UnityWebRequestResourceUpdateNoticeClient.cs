using System;
using Cysharp.Threading.Tasks;
using UnityEngine.Networking;

namespace Game
{
    /// <summary>
    /// 使用 UnityWebRequest 从独立资源服务器固定路由读取并选择资源更新公告。
    /// </summary>
    public sealed class UnityWebRequestResourceUpdateNoticeClient : IResourceUpdateNoticeClient
    {
        public async UniTask<ResourceUpdateNoticeResult> FetchAsync(ResourceUpdateNoticeRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ResourceServer) ||
                string.IsNullOrWhiteSpace(request.Version))
            {
                return ResourceUpdateNoticeResult.NoNotice;
            }

            if (!Uri.TryCreate(request.ResourceServer, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return ResourceUpdateNoticeResult.Fail("公告请求失败：http resource server url is invalid");
            }

            var url = $"{request.ResourceServer.TrimEnd('/')}/resource-update-notices.json?t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            using (var webRequest = UnityWebRequest.Get(url))
            {
                webRequest.timeout = Math.Max(1, request.TimeoutSeconds);
                var operation = webRequest.SendWebRequest();
                while (!operation.isDone)
                {
                    await UniTask.Yield();
                }

                if (webRequest.result != UnityWebRequest.Result.Success)
                {
                    return ResourceUpdateNoticeResult.Fail(
                        $"公告请求失败：http request failed url={url} version={request.Version} error={webRequest.error}");
                }

                var contentLength = webRequest.GetResponseHeader("Content-Length");
                if (long.TryParse(contentLength, out var length) && length > ResourceUpdateNoticeDocument.MaxBytes)
                {
                    return ResourceUpdateNoticeResult.Fail(
                        $"公告请求失败：http response too large url={url} version={request.Version}");
                }

                var bytes = webRequest.downloadHandler?.data;
                if (bytes == null || bytes.Length == 0)
                {
                    return ResourceUpdateNoticeResult.Fail(
                        $"公告请求失败：http response empty url={url} version={request.Version}");
                }

                if (bytes.Length > ResourceUpdateNoticeDocument.MaxBytes)
                {
                    return ResourceUpdateNoticeResult.Fail(
                        $"公告请求失败：http downloaded response too large url={url} version={request.Version}");
                }

                var json = System.Text.Encoding.UTF8.GetString(bytes);
                var parse = ResourceUpdateNoticeDocument.TryParse(json, out var document);
                if (!parse.Success)
                {
                    return ResourceUpdateNoticeResult.Fail(
                        $"公告解析失败：parse url={url} version={request.Version} error={parse.Error}");
                }

                var selected = ResourceUpdateNoticeDocument.Select(
                    document,
                    request.Version,
                    request.LanguageCode);
                return selected.Success
                    ? selected
                    : ResourceUpdateNoticeResult.Fail(
                        $"公告选择失败：select url={url} version={request.Version} error={selected.Error}");
            }
        }
    }
}
