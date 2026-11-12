using System;
using System.Threading;
using System.Threading.Tasks;
using Boot;
using Cysharp.Threading.Tasks;
using QFramework;

namespace Game
{
    /// <summary>
    /// 协调运行时资源版本检查并发布不可变快照；只负责检查，不下载或替换资源。
    /// </summary>
    public sealed class ResourceUpdateSystem : AbstractSystem
    {
        private const int DefaultTimeoutSeconds = 5;
        private Task<ResourceUpdateSnapshot> currentCheck;
        private Task<ResourceUpdateCheckWithNoticeResult> currentManualCheck;
        private Task<ResourceUpdateCheckWithNoticeResult> currentVersionCheck;
        private CancellationTokenSource cancellation;
        private int generation;

        public ResourceUpdateSnapshot Snapshot => this.GetModel<ResourceUpdateModel>().Snapshot.Value;

        protected override void OnInit()
        {
            // 由功能拥有者注册节点，完整架构启动后即可驱动更新提示。
            var redPoints = this.GetSystem<RedPointSystem>();
            redPoints.Register(RedPointIds.Start);
            redPoints.Register(RedPointIds.StartResourceUpdate, RedPointIds.Start);
            cancellation = new CancellationTokenSource();
        }

        protected override void OnDeinit()
        {
            cancellation?.Cancel();
            cancellation?.Dispose();
            cancellation = null;
        }

        public void InitializeStartupInfo(BootResourceStartupInfo info)
        {
            generation++;
            currentCheck = null;
            currentManualCheck = null;
            currentVersionCheck = null;
            var model = this.GetModel<ResourceUpdateModel>();
            model.ResourceUpdateAvailable = false;
            model.ApplicationUpdateAvailable = false;
            model.HasUpdate = false;
            Publish(new ResourceUpdateSnapshot(
                ResourceUpdateStatus.NotChecked,
                info.ActiveVersion,
                info.HostVersion,
                0,
                0,
                string.Empty,
                info.Source,
                info.ResourceServer,
                false,
                false));
        }

        /// <summary>
        /// 检查远端资源更新并发布快照；并发或过期请求只保留最新一次结果。
        /// </summary>
        public UniTask<ResourceUpdateSnapshot> CheckAsync(bool userInitiated)
        {
            if (currentCheck != null && !currentCheck.IsCompleted)
            {
                return currentCheck.AsUniTask();
            }

            currentCheck = CheckInternal(userInitiated, ++generation);
            return currentCheck.AsUniTask();
        }

        /// <summary>资源和应用版本复用同一在途检查；主动入口完成后仍允许再次检查并提示同一候选。</summary>
        public UniTask<ResourceUpdateCheckWithNoticeResult> CheckWithNoticeAsync(bool userInitiated)
        {
            if (!userInitiated) return GetVersionCheck(false).AsUniTask();
            if (currentManualCheck == null || currentManualCheck.IsCompleted)
                currentManualCheck = CheckWithNoticeInternal();
            return currentManualCheck.AsUniTask();
        }

        private async Task<ResourceUpdateCheckWithNoticeResult> CheckWithNoticeInternal()
        {
            CancellationToken token = cancellation.Token;
            Task<ResourceUpdateCheckWithNoticeResult> check = GetVersionCheck(true);
            int requestGeneration = generation;
            ResourceUpdateCheckWithNoticeResult result = await check;
            if (token.IsCancellationRequested || requestGeneration != generation)
                return new ResourceUpdateCheckWithNoticeResult(result.Snapshot, ResourceUpdateNoticeResult.NoNotice);
            ResourceUpdateNoticeResult notice = await FetchNoticeAfterSuccessfulManualCheck(true, result.Snapshot);
            if (token.IsCancellationRequested || requestGeneration != generation)
                return new ResourceUpdateCheckWithNoticeResult(result.Snapshot, ResourceUpdateNoticeResult.NoNotice);
            return new ResourceUpdateCheckWithNoticeResult(result.Snapshot, notice, result.ApplicationUpdate);
        }

        private Task<ResourceUpdateCheckWithNoticeResult> GetVersionCheck(bool userInitiated)
        {
            if (currentVersionCheck == null || currentVersionCheck.IsCompleted)
                currentVersionCheck = CheckVersionsInternal(userInitiated);
            return currentVersionCheck;
        }

        private async Task<ResourceUpdateCheckWithNoticeResult> CheckVersionsInternal(bool userInitiated)
        {
            CancellationToken token = cancellation.Token;
            UniTask<ResourceUpdateSnapshot> resourceCheck = CheckAsync(userInitiated);
            int requestGeneration = generation;
            ResourceUpdateSnapshot snapshot = await resourceCheck;
            if (token.IsCancellationRequested || requestGeneration != generation)
                return new ResourceUpdateCheckWithNoticeResult(snapshot, ResourceUpdateNoticeResult.NoNotice);

            ApplicationUpdateCandidate applicationUpdate = null;
            bool applicationChecked = false;
            var checker = this.GetUtility<IApplicationUpdateChecker>();
            if (checker != null && !string.IsNullOrWhiteSpace(snapshot.ResourceServer))
            {
                try
                {
                    applicationUpdate = await checker.CheckAsync(snapshot.ResourceServer, token);
                    if (!token.IsCancellationRequested && requestGeneration == generation)
                    {
                        var model = this.GetModel<ResourceUpdateModel>();
                        model.ApplicationUpdateAvailable = applicationUpdate != null;
                        applicationChecked = true;
                        RefreshCombinedUpdateState(model);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception exception)
                {
                    Log.Warning($"[ResourceUpdateSystem] 应用安装包检查不可用：{exception.GetType().Name}");
                }
                if (token.IsCancellationRequested || requestGeneration != generation ||
                    !string.Equals(snapshot.ResourceServer, Snapshot.ResourceServer, StringComparison.Ordinal))
                    applicationUpdate = null;
            }

            if (token.IsCancellationRequested || requestGeneration != generation)
                return new ResourceUpdateCheckWithNoticeResult(snapshot, ResourceUpdateNoticeResult.NoNotice);

            // 成功的无候选结论可以熄灭应用更新红点；失败保留上一次有效结论。
            if (applicationChecked)
                RefreshCombinedUpdateState(this.GetModel<ResourceUpdateModel>());
            return new ResourceUpdateCheckWithNoticeResult(
                snapshot, ResourceUpdateNoticeResult.NoNotice, applicationUpdate);
        }

        private async Task<ResourceUpdateSnapshot> CheckInternal(bool userInitiated, int requestGeneration)
        {
            var token = cancellation.Token;
            var initial = Snapshot;
            if (Snapshot.ResourceSource == BootResourceSource.BuiltinFallback)
            {
                return await CheckBuiltinFallback(userInitiated, requestGeneration);
            }

            var checker = this.GetUtility<IResourceUpdateRuntimeChecker>();
            if (checker == null || !checker.CanCheck)
            {
                var failed = new ResourceUpdateSnapshot(ResourceUpdateStatus.CheckFailed,
                    Snapshot.CurrentVersion,
                    string.Empty,
                    0,
                    0,
                    "资源更新检查器不可用",
                    Snapshot.ResourceSource,
                    Snapshot.ResourceServer);
                Publish(failed);
                return failed;
            }

            Publish(new ResourceUpdateSnapshot(ResourceUpdateStatus.Checking,
                Snapshot.CurrentVersion,
                Snapshot.TargetVersion,
                Snapshot.TotalDownloadCount,
                Snapshot.TotalDownloadBytes,
                string.Empty,
                Snapshot.ResourceSource,
                Snapshot.ResourceServer));

            try
            {
                var result = await checker.CheckAsync(DefaultTimeoutSeconds);
                if (token.IsCancellationRequested || requestGeneration != generation) return initial;

                var snapshot = result.Success
                    ? PreserveRuntimeContext(result.Snapshot, string.Empty)
                    : PreserveRuntimeContext(
                        new ResourceUpdateSnapshot(ResourceUpdateStatus.CheckFailed,
                            result.Snapshot.CurrentVersion,
                            result.Snapshot.TargetVersion,
                            result.Snapshot.TotalDownloadCount,
                            result.Snapshot.TotalDownloadBytes,
                            string.IsNullOrWhiteSpace(result.Error)
                                ? $"资源更新检查失败：{result.Snapshot.Error}"
                                : $"资源更新检查失败：{result.Error}"),
                        string.Empty);

                Publish(snapshot);
                return snapshot;
            }
            catch (Exception e)
            {
                if (token.IsCancellationRequested || requestGeneration != generation) return initial;
                var failed = new ResourceUpdateSnapshot(ResourceUpdateStatus.CheckFailed,
                    Snapshot.CurrentVersion,
                    string.Empty,
                    0,
                    0,
                    $"资源更新检查异常：{e.Message}",
                    Snapshot.ResourceSource,
                    Snapshot.ResourceServer);
                Publish(failed);
                return failed;
            }
        }

        private async Task<ResourceUpdateSnapshot> CheckBuiltinFallback(bool userInitiated, int requestGeneration)
        {
            var token = cancellation.Token;
            var initial = Snapshot;
            if (!userInitiated)
            {
                return Snapshot;
            }

            Publish(new ResourceUpdateSnapshot(
                ResourceUpdateStatus.Checking,
                Snapshot.CurrentVersion,
                Snapshot.TargetVersion,
                0,
                0,
                string.Empty,
                Snapshot.ResourceSource,
                Snapshot.ResourceServer,
                false,
                false));

            var probe = this.GetUtility<IRemoteResourceVersionProbe>();
            if (probe == null)
            {
                var failedProbe = BuiltinSnapshot(ResourceUpdateStatus.CheckFailed, string.Empty, "资源服务器版本探测器不可用", false);
                Publish(failedProbe);
                return failedProbe;
            }

            try
            {
                var result = await probe.ProbeAsync(Snapshot.ResourceServer, DefaultTimeoutSeconds);
                if (token.IsCancellationRequested || requestGeneration != generation)
                {
                    return initial;
                }

                ResourceUpdateSnapshot snapshot;
                if (!result.Success)
                {
                    snapshot = BuiltinSnapshot(ResourceUpdateStatus.CheckFailed, string.Empty, result.Error, false);
                }
                else if (string.Equals(result.Version, Snapshot.CurrentVersion, StringComparison.Ordinal))
                {
                    snapshot = BuiltinSnapshot(ResourceUpdateStatus.NoUpdate, result.Version, string.Empty, false);
                }
                else
                {
                    snapshot = BuiltinSnapshot(ResourceUpdateStatus.UpdateAvailable, result.Version, string.Empty, true);
                }

                Publish(snapshot);
                return snapshot;
            }
            catch (Exception e)
            {
                if (token.IsCancellationRequested || requestGeneration != generation) return initial;
                var failed = BuiltinSnapshot(ResourceUpdateStatus.CheckFailed, string.Empty, $"资源服务器版本探测异常：{e.Message}", false);
                Publish(failed);
                return failed;
            }
        }

        private ResourceUpdateSnapshot BuiltinSnapshot(
            ResourceUpdateStatus status,
            string targetVersion,
            string error,
            bool requiresRestartToUpdate)
        {
            return new ResourceUpdateSnapshot(
                status,
                Snapshot.CurrentVersion,
                targetVersion,
                0,
                0,
                error,
                Snapshot.ResourceSource,
                Snapshot.ResourceServer,
                requiresRestartToUpdate,
                false);
        }

        /// <summary>
        /// 保留启动来源与实际运行版本；成功检查按版本差异判定更新，缓存齐全仍需重启激活清单。
        /// </summary>
        private ResourceUpdateSnapshot PreserveRuntimeContext(
            ResourceUpdateSnapshot snapshot,
            string error)
        {
            var successful = snapshot.Status == ResourceUpdateStatus.NoUpdate ||
                             snapshot.Status == ResourceUpdateStatus.UpdateAvailable;
            var differentVersion = successful && !string.IsNullOrWhiteSpace(snapshot.TargetVersion) &&
                                   !string.Equals(snapshot.CurrentVersion, snapshot.TargetVersion, StringComparison.Ordinal);
            return new ResourceUpdateSnapshot(
                differentVersion ? ResourceUpdateStatus.UpdateAvailable : snapshot.Status,
                snapshot.CurrentVersion,
                snapshot.TargetVersion,
                snapshot.TotalDownloadCount,
                snapshot.TotalDownloadBytes,
                string.IsNullOrWhiteSpace(error) ? snapshot.Error : error,
                Snapshot.ResourceSource,
                Snapshot.ResourceServer,
                differentVersion,
                true);
        }

        private async UniTask<ResourceUpdateNoticeResult> FetchNoticeAfterSuccessfulManualCheck(
            bool userInitiated,
            ResourceUpdateSnapshot snapshot)
        {
            if (!userInitiated ||
                (snapshot.Status != ResourceUpdateStatus.NoUpdate &&
                 snapshot.Status != ResourceUpdateStatus.UpdateAvailable) ||
                string.IsNullOrWhiteSpace(snapshot.ResourceServer) ||
                string.IsNullOrWhiteSpace(snapshot.TargetVersion))
            {
                return ResourceUpdateNoticeResult.NoNotice;
            }

            var client = this.GetUtility<IResourceUpdateNoticeClient>();
            if (client == null)
            {
                return ResourceUpdateNoticeResult.NoNotice;
            }

            var languageCode = this.GetModel<LocalizationModel>().CurrentLanguageCode.Value;
            var result = await client.FetchAsync(new ResourceUpdateNoticeRequest(
                snapshot.ResourceServer,
                snapshot.TargetVersion,
                languageCode,
                DefaultTimeoutSeconds));
            if (!result.Success)
            {
                Log.Warning($"[ResourceUpdateSystem] 资源更新公告不可用：{result.Error}");
                return ResourceUpdateNoticeResult.NoNotice;
            }

            return result;
        }

        private void Publish(ResourceUpdateSnapshot snapshot)
        {
            var model = this.GetModel<ResourceUpdateModel>();
            if (snapshot.Status == ResourceUpdateStatus.UpdateAvailable)
                model.ResourceUpdateAvailable = true;
            else if (snapshot.Status == ResourceUpdateStatus.NoUpdate)
                model.ResourceUpdateAvailable = false;
            model.Snapshot.Value = snapshot;
            this.SendEvent(new ResourceUpdateChangedEvent(snapshot));

            RefreshCombinedUpdateState(model);
        }

        private void RefreshCombinedUpdateState(ResourceUpdateModel model)
        {
            model.HasUpdate = model.ResourceUpdateAvailable || model.ApplicationUpdateAvailable;
            var redPoints = this.GetSystem<RedPointSystem>();
            redPoints?.SetLeaf(RedPointIds.StartResourceUpdate, model.HasUpdate, 1);
        }
    }
}
