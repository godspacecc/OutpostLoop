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
    /// <summary>启动资源更新前执行的可选整包更新；失败或玩家跳过均不阻断原启动链。</summary>
    public sealed class ApkUpdateFlow
    {
        private readonly HotUpdateView view;
        private string offered;
        private string installationDetails;
#if UNITY_INCLUDE_TESTS
        public Func<string, ApkUpdateManifest, CancellationToken, UniTask<ApkInstallResult>> TestInstallRequest;
        public Action<string> TestClipboardWriter;
#endif
        public ApkUpdateFlow(HotUpdateView view) { this.view = view; }

        public async UniTask RunAsync(string server, CancellationToken cancellation)
        {
            if (!AndroidApkInstaller.Supported) return;
            try
            {
                var manifest = await CheckAsync(server, Application.identifier, Application.version,
                    AndroidApkInstaller.CurrentVersionCode, cancellation);
                if (manifest == null) return;
                string key = server + "/" + manifest.versionName + "/" + manifest.sha256;
                if (offered == key) return;
                offered = key;

                string root = AndroidApkInstaller.UpdateDirectory;
                Directory.CreateDirectory(root);
                string path = Path.Combine(root, manifest.sha256.ToLowerInvariant() + ".apk");
                string message = "发现新的完整安装包，是否下载更新？也可以稍后更新并继续启动。" +
                    $"\n当前 {Application.version} → 新版 {manifest.versionName}\n大小：{manifest.sizeBytes / 1048576d:F1} MB";
                bool ready = await VerifyAsync(manifest, path, cancellation);

                while (true)
                {
                    cancellation.ThrowIfCancellationRequested();
                    bool primary = await ChooseAsync(message, ready ? "安装更新" : "下载更新", cancellation);
                    if (!primary) return;
                    if (!ready)
                    {
                        bool? downloaded = await DownloadAsync(server, manifest, path, cancellation);
                        ready = downloaded == true;
                        message = ready ? "安装包已下载并通过校验，点击后由系统引导完成安装。" :
                            downloaded == null ? "下载已取消，可以重新下载或稍后更新。" :
                            "下载失败或校验未通过，请检查网络和存储空间后重试。";
                        continue;
                    }
                    if (!await VerifyAsync(manifest, path, cancellation))
                    {
                        installationDetails = null;
                        ready = false;
                        message = "安装包校验未通过，请重新下载。";
                        continue;
                    }

                    var result = await InstallAsync(path, manifest, cancellation);
                    if (result == null) return;
                    installationDetails = result.result < 0 ? result.ToCopyDetails(manifest) : null;
                    message = result.result == 0 ? "已打开系统安装界面。若取消安装，可以稍后更新并继续启动。" :
                        result.result == 1 ? "请允许此来源安装应用，返回后再次点击安装更新。" : result.PlayerMessage;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ApkUpdate] 可选整包更新已跳过：{exception.GetType().Name}");
            }
            finally
            {
                if (view) view.RenderNormal("正在检查资源更新...");
            }
        }

        public static async UniTask<ApkUpdateManifest> CheckAsync(string server, string package, string version,
            int code, CancellationToken cancellation) => await CheckAsync(server, package, version, code, cancellation, false);

        public static async UniTask<ApkUpdateManifest> CheckAsync(string server, string package, string version,
            int code, CancellationToken cancellation, bool reportFailure)
        {
            using (var handler = new LimitedTextHandler())
            using (var request = new UnityWebRequest(server.TrimEnd('/') + "/app-update.json", "GET", handler, null))
            {
                request.timeout = 5;
                request.redirectLimit = 0;
                request.SetRequestHeader("Cache-Control", "no-cache");
                try
                {
                    await request.SendWebRequest().ToUniTask(cancellationToken: cancellation);
                    if (reportFailure && (request.result != UnityWebRequest.Result.Success ||
                        ApkUpdateManifest.ParseAvailable(handler.Text, package, version, code, false) == null))
                        throw new InvalidOperationException("安装包版本描述不可用");
                    return request.result == UnityWebRequest.Result.Success
                        ? ApkUpdateManifest.ParseAvailable(handler.Text, package, version, code) : null;
                }
                catch (UnityWebRequestException) when (!reportFailure) { return null; }
            }
        }

        private async UniTask<bool> ChooseAsync(string message, string primary, CancellationToken cancellation)
        {
            var completion = new UniTaskCompletionSource<bool>();
            void Complete(bool accepted)
            {
                view.RenderNormal(accepted ? "正在处理安装包..." : "正在检查资源更新...");
                completion.TrySetResult(accepted);
            }
            if (string.IsNullOrEmpty(installationDetails))
                view.RenderApkChoice(message, primary, () => Complete(true), "稍后更新", () => Complete(false));
            else
                view.RenderApkInstallFailure(message, installationDetails, () => Complete(true), () => Complete(false), () =>
                {
#if UNITY_INCLUDE_TESTS
                    if (TestClipboardWriter != null) TestClipboardWriter(installationDetails);
                    else
#endif
                    GUIUtility.systemCopyBuffer = installationDetails;
                    view.ShowApkDetailsCopied();
                });
            using (cancellation.Register(() => completion.TrySetCanceled(cancellation)))
                return await completion.Task;
        }

        private async UniTask<ApkInstallResult> InstallAsync(string path, ApkUpdateManifest manifest, CancellationToken owner)
        {
            using (var request = CancellationTokenSource.CreateLinkedTokenSource(owner))
            {
                bool deferred = false;
                view.RenderApkInstalling(() => { deferred = true; request.Cancel(); });
                try
                {
#if UNITY_INCLUDE_TESTS
                    var operation = TestInstallRequest != null ? TestInstallRequest(path, manifest, request.Token) :
                        AndroidApkInstaller.RequestInstallAsync(path, manifest, request.Token);
#else
                    var operation = AndroidApkInstaller.RequestInstallAsync(path, manifest, request.Token);
#endif
                    return await operation.AttachExternalCancellation(request.Token);
                }
                catch (OperationCanceledException) when (deferred && !owner.IsCancellationRequested) { return null; }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception) { return ApkInstallResult.Failure("BRIDGE_EXCEPTION", "bridge", exception.ToString()); }
            }
        }

        private static async UniTask<bool> VerifyAsync(ApkUpdateManifest manifest, string path, CancellationToken cancellation)
        {
            bool valid = await UniTask.RunOnThreadPool(() => manifest.VerifyFile(path), cancellationToken: cancellation);
            await UniTask.SwitchToMainThread(cancellation);
            return valid;
        }

        private async UniTask<bool?> DownloadAsync(string server, ApkUpdateManifest manifest, string path, CancellationToken owner)
        {
            string temporary = path + ".part";
            using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(owner))
            {
                view.RenderApkDownload(() => cancellation.Cancel());
                view.SetProgress(0);
                view.SetSize(0, (ulong)manifest.sizeBytes);
                view.SetSpeed(0);
                try
                {
                    using (var request = new UnityWebRequest(server.TrimEnd('/') + "/app-updates/android/" + manifest.apkFileName, "GET"))
                    {
                        request.downloadHandler = new DownloadHandlerFile(temporary) { removeFileOnAbort = true };
                        request.redirectLimit = 0;
                        var operation = request.SendWebRequest();
                        ulong previous = 0;
                        float sampleTime = Time.realtimeSinceStartup;
                        float lastProgress = sampleTime;
                        while (!operation.isDone)
                        {
                            cancellation.Token.ThrowIfCancellationRequested();
                            ulong bytes = request.downloadedBytes;
                            float now = Time.realtimeSinceStartup;
                            if (bytes > (ulong)manifest.sizeBytes || now - lastProgress > 30f) { request.Abort(); return false; }
                            if (bytes != previous) lastProgress = now;
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
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(temporary, path);
                    return true;
                }
                catch (OperationCanceledException) when (!owner.IsCancellationRequested) { return null; }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[ApkUpdate] 下载失败：{exception.GetType().Name}");
                    return false;
                }
                finally
                {
                    try { if (File.Exists(temporary)) File.Delete(temporary); } catch (Exception) { }
                }
            }
        }

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
