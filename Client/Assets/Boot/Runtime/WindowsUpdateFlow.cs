using System;
using System.IO;
using System.Text;
using System.Threading;
using Boot.UI;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Boot
{
    /// <summary>启动前可选整包更新事务；拥有请求和临时文件，失败放行，不管理 YooAsset 或游戏存档。</summary>
    public sealed class WindowsUpdateFlow
    {
        private readonly HotUpdateView view;
        /// <summary>本次启动已提示的服务器与版本组合；资源重试不得重复弹出同一提示。</summary>
        private string offered;

        public WindowsUpdateFlow(HotUpdateView view) { this.view = view; }

        public async UniTask<bool> RunAsync(string server, CancellationToken cancellation)
        {
            if (!WindowsInstaller.Supported) return false;
            var installed = WindowsUpdateManifest.ReadInstalled();
            if (installed == null) return false;
            try
            {
                var manifest = await CheckAsync(server, installed.releaseNumber, cancellation);
                if (manifest == null) return false;
                string key = server + "/" + manifest.versionName + "/" + manifest.sha256;
                if (offered == key) return false;
                offered = key;
                string root = WindowsInstaller.UpdateDirectory;
                Directory.CreateDirectory(root);
                string path = Path.Combine(root, manifest.sha256.ToLowerInvariant() + ".exe");
                string message = "新版游戏安装包已发布，是否下载更新？你也可以稍后更新，继续游戏。" +
                    $"\n当前 {installed.versionName} → 新版 {manifest.versionName}\n大小：{manifest.sizeBytes / 1048576d:F1} MB";
                bool ready = await VerifyAsync(manifest, path, cancellation);
                while (true)
                {
                    cancellation.ThrowIfCancellationRequested();
                    bool primary = await ChooseAsync(message, ready ? "安装更新" : "下载更新", cancellation);
                    if (!primary) return false;
                    if (!ready)
                    {
                        bool? downloaded = await DownloadAsync(server, manifest, path, cancellation);
                        ready = downloaded == true;
                        message = ready ? "安装包已下载。点击安装更新将退出游戏并打开安装向导。" :
                            downloaded == null ? "下载已取消，你可以重新下载或稍后更新。" :
                            "下载失败或文件校验未通过，请检查网络和存储空间后重新下载。";
                        continue;
                    }
                    // 复用文件与系统交接前再次验证，损坏文件永远不能进入安装入口。
                    if (!await VerifyAsync(manifest, path, cancellation))
                    {
                        ready = false;
                        message = "安装包校验未通过，请重新下载。";
                        continue;
                    }
                    cancellation.ThrowIfCancellationRequested();
                    if (WindowsInstaller.TryStart(path, out string failure)) return true;
                    message = "安装器未能启动，可以重试或稍后更新。\n" + failure;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                Debug.LogWarning($"[WindowsUpdate] 可选整包更新已跳过：{exception.GetType().Name}");
            }
            finally
            {
                if (view) view.RenderNormal("正在检查资源更新...");
            }
            return false;
        }

        /// <summary>启动默认容错；运行时保留失败语义以免清除已知更新。取消始终传播给所有者。</summary>
        public static async UniTask<WindowsUpdateManifest> CheckAsync(string server, int currentRelease, CancellationToken cancellation) =>
            await CheckAsync(server, currentRelease, cancellation, false);

        /// <summary>reportFailure=true 时传播网络及无效描述错误；有效同版或旧版仍返回 null。</summary>
        public static async UniTask<WindowsUpdateManifest> CheckAsync(string server, int currentRelease, CancellationToken cancellation,
            bool reportFailure)
        {
            using (var handler = new LimitedTextHandler())
            using (var request = new UnityWebRequest(server.TrimEnd('/') + "/app-update-windows.json", "GET", handler, null))
            {
                request.timeout = 5;
                request.redirectLimit = 0;
                request.SetRequestHeader("Cache-Control", "no-cache");
                try
                {
                    await request.SendWebRequest().ToUniTask(cancellationToken: cancellation);
                    if (reportFailure && (request.result != UnityWebRequest.Result.Success ||
                        WindowsUpdateManifest.ParseAvailable(handler.Text, currentRelease, false) == null))
                        throw new InvalidOperationException("PC 安装包版本描述不可用");
                    return request.result == UnityWebRequest.Result.Success
                        ? WindowsUpdateManifest.ParseAvailable(handler.Text, currentRelease) : null;
                }
                catch (UnityWebRequestException) when (!reportFailure) { return null; }
            }
        }

        private async UniTask<bool> ChooseAsync(string message, string primary, CancellationToken cancellation)
        {
            var completion = new UniTaskCompletionSource<bool>();
            void Complete(bool accepted)
            {
                // 点击当帧清除旧标题、正文与已完成回调，不等待下一次资源状态渲染。
                view.RenderNormal(accepted ? "正在处理安装包..." : "正在检查资源更新...");
                completion.TrySetResult(accepted);
            }
            view.RenderApkChoice(message, primary, () => Complete(true), "稍后更新", () => Complete(false));
            using (cancellation.Register(() => completion.TrySetCanceled(cancellation)))
                return await completion.Task;
        }

        private static async UniTask<bool> VerifyAsync(WindowsUpdateManifest manifest, string path, CancellationToken cancellation)
        {
            bool valid = await UniTask.RunOnThreadPool(() => manifest.VerifyFile(path), cancellationToken: cancellation);
            await UniTask.SwitchToMainThread(cancellation);
            return valid;
        }

        /// <summary>true 表示已验证提交，false 表示失败，null 表示玩家取消；仅临时文件由本次下载清理。</summary>
        private async UniTask<bool?> DownloadAsync(string server, WindowsUpdateManifest manifest, string path, CancellationToken owner)
        {
            string temporary = path + ".part-" + Guid.NewGuid().ToString("N");
            using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(owner))
            {
                view.RenderApkDownload(() => cancellation.Cancel());
                view.SetProgress(0);
                view.SetSize(0, (ulong)manifest.sizeBytes);
                view.SetSpeed(0);
                try
                {
                    using (var request = new UnityWebRequest(server.TrimEnd('/') + "/app-updates/windows/" +
                        manifest.installerFileName, "GET"))
                    {
                        request.downloadHandler = new DownloadHandlerFile(temporary) { removeFileOnAbort = true };
                        request.redirectLimit = 0;
                        var operation = request.SendWebRequest();
                        ulong previous = 0;
                        ulong observed = 0;
                        float sampleTime = Time.realtimeSinceStartup;
                        float lastProgress = sampleTime;
                        while (!operation.isDone)
                        {
                            cancellation.Token.ThrowIfCancellationRequested();
                            ulong bytes = request.downloadedBytes;
                            float now = Time.realtimeSinceStartup;
                            if (bytes > (ulong)manifest.sizeBytes || now - lastProgress > 30f)
                            { request.Abort(); return false; }
                            if (bytes != observed) { lastProgress = now; observed = bytes; }
                            view.SetProgress(Mathf.Clamp01(bytes / (float)manifest.sizeBytes));
                            view.SetSize(bytes, (ulong)manifest.sizeBytes);
                            if (now - sampleTime >= 0.5f)
                            {
                                ulong speed = (ulong)((bytes - previous) / (now - sampleTime));
                                view.SetSpeed(speed);
                                previous = bytes;
                                sampleTime = now;
                            }
                            await UniTask.Yield(PlayerLoopTiming.Update, cancellation.Token);
                        }
                        cancellation.Token.ThrowIfCancellationRequested();
                        if (request.result != UnityWebRequest.Result.Success) return false;
                    }
                    view.RenderBusy("正在校验安装包……");
                    if (!await VerifyAsync(manifest, temporary, owner)) return false;
                    owner.ThrowIfCancellationRequested();
                    // 多个游戏实例共享完整缓存，但每次请求只清理自己的临时文件。
                    if (File.Exists(path))
                    {
                        if (await VerifyAsync(manifest, path, owner)) return true;
                        File.Delete(path);
                    }
                    try { File.Move(temporary, path); }
                    catch (IOException) when (File.Exists(path))
                    {
                        if (!await VerifyAsync(manifest, path, owner)) throw;
                    }
                    return true;
                }
                catch (OperationCanceledException) when (!owner.IsCancellationRequested) { return null; }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception)
                { Debug.LogWarning($"[WindowsUpdate] 下载失败：{exception.GetType().Name}"); return false; }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
            }
        }

        /// <summary>只接收最多 64 KiB 的描述，拒绝服务器误发大文件。</summary>
        private sealed class LimitedTextHandler : DownloadHandlerScript
        {
            private readonly MemoryStream bytes = new MemoryStream();
            public string Text => Encoding.UTF8.GetString(bytes.ToArray());
            public LimitedTextHandler() : base(new byte[8192]) { }
            protected override bool ReceiveData(byte[] data, int length)
            {
                if (data == null || length < 0 || bytes.Length + length > 65536) return false;
                bytes.Write(data, 0, length);
                return true;
            }
            public override void Dispose() { bytes.Dispose(); base.Dispose(); }
        }
    }
}
