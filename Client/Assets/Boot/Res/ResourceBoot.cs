using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;
using Boot.UI;

namespace Boot
{
    /// <summary>
    /// 参与 Boot 启动链的 ResourceBoot，在热更代码加载前完成资源、配置或界面衔接。
    /// </summary>
    public class ResourceBoot : MonoBehaviour
    {
        private const string PACKAGE_NAME = "OutpostLoop";
        private const int MAX_DOWNLOAD_COUNT = 10;
        private const int VERSION_TIMEOUT_SECONDS = 8;
        private const int MANIFEST_TIMEOUT_SECONDS = 15;
        private const string LAST_SUCCESSFUL_HOST_VERSION_KEY = "OutpostLoop.LastSuccessfulHostVersion";

        [Header("编辑器资源模式")]
        [SerializeField]
#if UNITY_EDITOR
        [Tooltip("编辑器运行时使用的资源加载模式")]
#endif
        private ResourcePlayMode _editorPlayMode = ResourcePlayMode.EditorSourceMode;

        private ResourcePlayMode _actualPlayMode;
        private bool _isReady;
        private string _lastError;
        private ResourcePackage _package;
        private ResourceDownloaderOperation _downloader;
        private HotUpdateView _updateView;
        private string _resourceServer;
        private bool _isInitializing;
        private long _lastDownloadBytes;
        private float _lastDownloadTime;
        private string _pendingHostVersion;
        private BootResourceSource _pendingResourceSource;
        private string _detectedHostVersion;
        private ApkUpdateFlow _apkUpdate;

        public ResourcePlayMode PlayMode => _actualPlayMode;
        public bool IsReady => _isReady;
        public string LastError => _lastError;

#if UNITY_INCLUDE_TESTS
        public static Func<UniTask<string>> TestRawInitializationOutcome { get; set; }
        public static Func<string, bool> TestNeedsDownload { get; set; }

        public static void ResetTestHooks()
        {
            TestRawInitializationOutcome = null;
            TestNeedsDownload = null;
        }
#endif

        private void Awake()
        {
            _isInitializing = false;
            _isReady = false;
            _lastError = null;
            _pendingHostVersion = null;
            _pendingResourceSource = BootResourceSource.EditorSimulate;
            _detectedHostVersion = string.Empty;
        }

        public async UniTask<BootOperationResult> InitializeAsync()
        {
            if (_updateView == null)
            {
                throw new InvalidOperationException(
                    "ResourceBoot 尚未配置 HotUpdateView，不能启动资源流程。");
            }

            if (_isInitializing)
            {
                Debug.LogWarning("[ResourceBoot] 资源系统正在初始化，请勿重复触发。");
                return _isReady
                    ? BootOperationResult.Success()
                    : BootOperationResult.Failed(BootFailure.Create(
                        BootFailureCode.HostInitializationFailed,
                        BootFailureStage.ResourceInitialization,
                        BootRetryScope.ResourceInitialization,
                        DiagnosticFields(("resourcePackage", PACKAGE_NAME))));
            }

            if (_isReady)
                return BootOperationResult.Success();

            _isInitializing = true;
            _lastError = null;
            _pendingHostVersion = null;
            _pendingResourceSource = BootResourceSource.EditorSimulate;
            _detectedHostVersion = string.Empty;
            BootResourceStartupState.Reset();

            try
            {
#if UNITY_INCLUDE_TESTS
                if (TestRawInitializationOutcome != null)
                {
                    return await InitializeFromTestRawOutcome();
                }
#endif

#if UNITY_EDITOR
                if (_editorPlayMode == ResourcePlayMode.EditorSourceMode)
                {
                    if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>("Assets/Game/Scenes/Main.unity") == null ||
                        UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/tbrule.bytes") == null)
                        return Fail(BootFailureCode.RequiredResourceMissing, BootFailureStage.ResourceValidation, BootRetryScope.ResourceInitialization, ("source", "Main scene or tbrule.bytes"));
                    _actualPlayMode = ResourcePlayMode.EditorSourceMode;
                    BootResourceStartupState.Publish(new BootResourceStartupInfo(BootResourceSource.EditorSource, "", "editor-source", ""));
                    _isReady = true;
                    SetStatus("编辑器源资源就绪（未构建资源包）");
                    return BootOperationResult.Success();
                }
#endif
                YooAssets.Initialize();
                DeterminePlayMode();

                BootOperationResult packageResult = await InitializePackage();
                if (!packageResult.Succeeded)
                    return packageResult;

                BootResourceStartupState.Publish(new BootResourceStartupInfo(
                    _pendingResourceSource,
                    _resourceServer,
                    _package.GetPackageVersion(),
                    _detectedHostVersion));
                _isReady = true;
                _lastError = null;
                RefreshVersion();
                return BootOperationResult.Success();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _lastError = "资源初始化异常";
                return BootOperationResult.Failed(BootFailure.Unexpected(e, BootFailureStage.ResourceInitialization));
            }
            finally
            {
                _isInitializing = false;
            }
        }

        public async UniTask<BootOperationResult> ReinitializeForRetryAsync()
        {
            _isReady = false;
            return await InitializeAsync();
        }

        private void RefreshVersion()
        {
            if (_updateView == null || _package == null)
                return;

            _updateView.SetVersion(_package.GetPackageVersion(), _detectedHostVersion);
        }

        private static IReadOnlyDictionary<string, string> DiagnosticFields(params (string key, string value)[] fields)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in fields)
            {
                result[field.key] = field.value;
            }

            return result;
        }

        private BootOperationResult Fail(
            BootFailureCode code,
            BootFailureStage stage,
            BootRetryScope retryScope,
            params (string key, string value)[] fields)
        {
            _lastError = code.ToString();
            return BootOperationResult.Failed(BootFailure.Create(code, stage, retryScope, DiagnosticFields(fields)));
        }

        private BootOperationResult Success()
        {
            return BootOperationResult.Success();
        }

        private void DeterminePlayMode()
        {
#if UNITY_EDITOR
            _actualPlayMode = _editorPlayMode;
#elif OUTPOSTLOOP_ANDROID_BUILTIN_RESOURCES
            _actualPlayMode = ResourcePlayMode.OfflinePlayMode;
#else
            _actualPlayMode = ResourcePlayMode.HostPlayMode;
#endif
        }

        /// <summary>Host 模式异步加载资源地址，兼容 Android APK 内的 StreamingAssets。</summary>
        private async UniTask<BootOperationResult> LoadBootstrapConfigAsync()
        {
            if (_actualPlayMode != ResourcePlayMode.HostPlayMode)
            {
                _resourceServer = "http://127.0.0.1:8000";
                return null;
            }

            try
            {
                var config = await BootstrapConfig.LoadAsync();
                _resourceServer = config.ResourceServer;
                return null;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return Fail(
                    BootFailureCode.BootstrapConfigInvalid,
                    BootFailureStage.BootstrapConfig,
                    BootRetryScope.None,
                    ("resourcePackage", PACKAGE_NAME));
            }
        }

        private async UniTask<BootOperationResult> InitializePackage()
        {
            await ResetPackageAsync();

            switch (_actualPlayMode)
            {
#if UNITY_EDITOR
                case ResourcePlayMode.EditorSimulateMode:
                    return await InitializeEditorSimulateMode();
#endif
                case ResourcePlayMode.OfflinePlayMode:
                    return await InitializeOfflineMode();
                case ResourcePlayMode.HostPlayMode:
                    return await InitializeHostMode();
                default:
                    return Fail(
                        BootFailureCode.HostInitializationFailed,
                        BootFailureStage.ResourceInitialization,
                        BootRetryScope.ResourceInitialization,
                        ("playMode", _actualPlayMode.ToString()));
            }
        }

        private async UniTask ResetPackageAsync()
        {
            var oldPackage = YooAssets.TryGetPackage(PACKAGE_NAME);
            if (oldPackage != null)
            {
                var destroyOperation = oldPackage.DestroyAsync();
                await destroyOperation.WaitAsync();
                YooAssets.RemovePackage(oldPackage);
            }

            _package = YooAssets.CreatePackage(PACKAGE_NAME);
            YooAssets.SetDefaultPackage(_package);
        }

#if UNITY_EDITOR
        private async UniTask<BootOperationResult> InitializeEditorSimulateMode()
        {
            SetStatus("编辑器模拟模式...");

            var simulateResult = EditorSimulateModeHelper.SimulateBuild(PACKAGE_NAME);

            var parameters = new EditorSimulateModeParameters();
            parameters.EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(simulateResult.PackageRootDirectory);

            var operation = _package.InitializeAsync(parameters);
            await operation.WaitAsync();

            if (operation.Status != EOperationStatus.Succeed)
            {
                return Fail(
                    BootFailureCode.HostInitializationFailed,
                    BootFailureStage.ResourceInitialization,
                    BootRetryScope.ResourceInitialization,
                    ("operationStatus", operation.Status.ToString()),
                    ("resourcePackage", PACKAGE_NAME));
            }

            _pendingResourceSource = BootResourceSource.EditorSimulate;
            var manifestResult = await EnsurePackageManifestAsync("编辑器模拟模式");
            return manifestResult.Succeeded ? await ValidateReadyResourcesAsync() : manifestResult;
        }
#endif

        private async UniTask<BootOperationResult> InitializeOfflineMode()
        {
            if (string.IsNullOrWhiteSpace(_resourceServer))
                _resourceServer = "http://127.0.0.1:8000";
            SetStatus("离线模式...");

            var parameters = new OfflinePlayModeParameters();
            parameters.BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters();

            var operation = _package.InitializeAsync(parameters);
            await operation.WaitAsync();

            if (operation.Status != EOperationStatus.Succeed)
            {
                return Fail(
                    BootFailureCode.BuiltinResourceInvalid,
                    BootFailureStage.ResourceInitialization,
                    BootRetryScope.None,
                    ("operationStatus", operation.Status.ToString()),
                    ("resourcePackage", PACKAGE_NAME));
            }

            _pendingResourceSource = BootResourceSource.BuiltinFallback;
            var manifestResult = await EnsurePackageManifestAsync("离线模式");
            return manifestResult.Succeeded ? await ValidateReadyResourcesAsync() : manifestResult;
        }

        private async UniTask<BootOperationResult> EnsurePackageManifestAsync(string stage)
        {
            var versionOp = _package.RequestPackageVersionAsync(true, VERSION_TIMEOUT_SECONDS);
            await versionOp.WaitAsync();

            if (versionOp.Status != EOperationStatus.Succeed)
            {
                return Fail(
                    BootFailureCode.PackageVersionRequestFailed,
                    BootFailureStage.ResourceVersion,
                    BootRetryScope.ResourceInitialization,
                    ("operationStatus", versionOp.Status.ToString()),
                    ("resourcePackage", PACKAGE_NAME),
                    ("resourceStage", stage));
            }

            if (!string.IsNullOrEmpty(versionOp.PackageVersion))
            {
                _detectedHostVersion = versionOp.PackageVersion;
            }

            var manifestOp = _package.UpdatePackageManifestAsync(versionOp.PackageVersion, MANIFEST_TIMEOUT_SECONDS);
            await manifestOp.WaitAsync();

            if (manifestOp.Status != EOperationStatus.Succeed)
            {
                return Fail(
                    BootFailureCode.PackageManifestUpdateFailed,
                    BootFailureStage.ResourceManifest,
                    BootRetryScope.ResourceInitialization,
                    ("operationStatus", manifestOp.Status.ToString()),
                    ("resourceVersion", versionOp.PackageVersion),
                    ("resourceStage", stage));
            }

            RefreshVersion();
            return Success();
        }

        private async UniTask<BootOperationResult> InitializeHostMode()
        {
            BootOperationResult configResult = await LoadBootstrapConfigAsync();
            if (configResult != null)
                return configResult;

            // 整包更新是可选前置步骤；网络、下载或安装交接失败均由流程内部降级放行。
            if (AndroidApkInstaller.Supported)
            {
                using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy()))
                    await _apkUpdate.RunAsync(_resourceServer, cancellation.Token);
            }

            string lastSuccessVersion = PlayerPrefs.GetString(LAST_SUCCESSFUL_HOST_VERSION_KEY, "");
            bool hasCachedVersion = !string.IsNullOrEmpty(lastSuccessVersion);

            SetStatus("正在连接资源服务器...");

            var remoteServices = new RemoteServices(_resourceServer);
            var parameters = new HostPlayModeParameters();
            parameters.BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters();
            parameters.CacheFileSystemParameters = FileSystemParameters.CreateDefaultCacheFileSystemParameters(remoteServices);

            var operation = _package.InitializeAsync(parameters);
            await operation.WaitAsync();

            BootOperationResult hostFailure = Fail(BootFailureCode.HostInitializationFailed,
                BootFailureStage.ResourceInitialization, BootRetryScope.ResourceInitialization,
                ("operationStatus", operation.Status.ToString()));
            if (operation.Status == EOperationStatus.Succeed)
            {
                SetStatus("正在查询资源版本...");
                var versionOp = _package.RequestPackageVersionAsync(true, VERSION_TIMEOUT_SECONDS);
                await versionOp.WaitAsync();
                hostFailure = Fail(BootFailureCode.PackageVersionRequestFailed,
                    BootFailureStage.ResourceVersion, BootRetryScope.ResourceInitialization,
                    ("operationStatus", versionOp.Status.ToString()));

                if (versionOp.Status == EOperationStatus.Succeed)
                {
                    string remoteVersion = versionOp.PackageVersion;
                    _detectedHostVersion = remoteVersion ?? string.Empty;
                    SetStatus("正在更新资源清单...");
                    var manifestOp = _package.UpdatePackageManifestAsync(remoteVersion, MANIFEST_TIMEOUT_SECONDS);
                    await manifestOp.WaitAsync();
                    hostFailure = Fail(BootFailureCode.PackageManifestUpdateFailed,
                        BootFailureStage.ResourceManifest, BootRetryScope.ResourceInitialization,
                        ("operationStatus", manifestOp.Status.ToString()));

                    if (manifestOp.Status == EOperationStatus.Succeed)
                    {
                        RefreshVersion();
                        BootOperationResult downloadResult = await DownloadResources(remoteVersion);
                        if (downloadResult.Succeeded)
                            return downloadResult;
                        hostFailure = downloadResult;
                    }
                }
            }

            if (hasCachedVersion)
            {
                SetStatus($"回退到缓存版本 {lastSuccessVersion}...");
                BootOperationResult cachedResult = await FallbackToCachedVersion(lastSuccessVersion);
                if (cachedResult.Succeeded)
                    return cachedResult;
            }

            SetStatus("回退到内置资源...");
            BootOperationResult builtinResult = await FallbackToBuiltin();
            BootOperationResult resolved = ResolveHostFallback(hostFailure, builtinResult);
            _lastError = resolved.Succeeded ? null : resolved.Failure.Code.ToString();
            return resolved;
        }

        // 宿主不兼容是不可重试终态；普通本地资源失败仍保留联网重试能力。
        public static BootOperationResult ResolveHostFallback(BootOperationResult hostFailure,
            BootOperationResult fallbackResult)
        {
            if (fallbackResult.Succeeded) return fallbackResult;
            Launcher.RecordStartupStage("本地资源回退失败：" + fallbackResult.Failure.DiagnosticDetails);
            if (fallbackResult.Failure.Code == BootFailureCode.HostIncompatible) return fallbackResult;
            return hostFailure;
        }

#if UNITY_INCLUDE_TESTS
        private async UniTask<BootOperationResult> InitializeFromTestRawOutcome()
        {
            string outcome = await TestRawInitializationOutcome();
            switch (outcome)
            {
                case "success":
                    _isReady = true;
                    return Success();
                case "resource-download-failed":
                    return Fail(
                        BootFailureCode.ResourceDownloadFailed,
                        BootFailureStage.ResourceDownload,
                        BootRetryScope.ResourceInitialization,
                        ("resourcePackage", PACKAGE_NAME));
                case "host-final-builtin-invalid":
                    return Fail(
                        BootFailureCode.BuiltinResourceInvalid,
                        BootFailureStage.ResourceInitialization,
                        BootRetryScope.None,
                        ("resourcePackage", PACKAGE_NAME),
                        ("operationStatus", "Failed"));
                default:
                    return Fail(
                        BootFailureCode.HostInitializationFailed,
                        BootFailureStage.ResourceInitialization,
                        BootRetryScope.ResourceInitialization,
                        ("resourcePackage", PACKAGE_NAME),
                        ("operationStatus", outcome ?? "null"));
            }
        }
#endif

        private async UniTask<BootOperationResult> DownloadResources(string version)
        {
            SetStatus("正在检查资源...");

            _downloader = _package.CreateResourceDownloader(MAX_DOWNLOAD_COUNT, 3);
            if (_downloader.TotalDownloadCount == 0)
            {
                var validation = await ValidateReadyResourcesAsync();
                if (!validation.Succeeded) return validation;
                _pendingHostVersion = version;
                _pendingResourceSource = BootResourceSource.HostLatest;
                SetStatus("资源已是最新版本");
                RefreshVersion();
                return Success();
            }

            SetStatus("正在下载资源...");
            _updateView.RenderDownloading("正在下载资源...");
            _updateView.SetProgress(0);
            _updateView.SetSize(0, (ulong)_downloader.TotalDownloadBytes);
            _updateView.SetSpeed(0);
            _lastDownloadTime = Time.realtimeSinceStartup;
            _lastDownloadBytes = 0;

            _downloader.DownloadUpdateCallback = OnDownloadUpdate;
            _downloader.DownloadErrorCallback = OnDownloadError;
            _downloader.BeginDownload();

            await _downloader.WaitAsync();

            if (_downloader.Status != EOperationStatus.Succeed)
            {
                return Fail(
                    BootFailureCode.ResourceDownloadFailed,
                    BootFailureStage.ResourceDownload,
                    BootRetryScope.ResourceInitialization,
                    ("operationStatus", _downloader.Status.ToString()),
                    ("resourceVersion", version));
            }

            var downloadedValidation = await ValidateReadyResourcesAsync();
            if (!downloadedValidation.Succeeded) return downloadedValidation;
            _pendingHostVersion = version;
            _pendingResourceSource = BootResourceSource.HostLatest;
            SetStatus("资源下载完成");
            RefreshVersion();
            return Success();
        }

        private async UniTask<BootOperationResult> FallbackToCachedVersion(string version)
        {
            await ResetPackageAsync();

            var remoteServices = new RemoteServices(_resourceServer);
            var parameters = new HostPlayModeParameters();
            parameters.BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters();
            parameters.CacheFileSystemParameters = FileSystemParameters.CreateDefaultCacheFileSystemParameters(remoteServices);

            var operation = _package.InitializeAsync(parameters);
            await operation.WaitAsync();
            if (operation.Status != EOperationStatus.Succeed)
                return await FallbackToBuiltin();

            var manifestOp = _package.UpdatePackageManifestAsync(version, MANIFEST_TIMEOUT_SECONDS);
            await manifestOp.WaitAsync();
            if (manifestOp.Status != EOperationStatus.Succeed)
                return await FallbackToBuiltin();

            _downloader = _package.CreateResourceDownloader(MAX_DOWNLOAD_COUNT, 3);
            if (_downloader.TotalDownloadCount > 0)
            {
                SetStatus($"缓存版本不完整，缺少 {_downloader.TotalDownloadCount} 个文件");
                return await FallbackToBuiltin();
            }

            var validation = await ValidateReadyResourcesAsync();
            if (!validation.Succeeded) return await FallbackToBuiltin();
            _pendingHostVersion = version;
            _pendingResourceSource = BootResourceSource.HostCached;
            _detectedHostVersion = _detectedHostVersion ?? string.Empty;
            SetStatus($"已使用缓存版本 {version}");
            RefreshVersion();
            return Success();
        }

        private async UniTask<BootOperationResult> FallbackToBuiltin()
        {
            _actualPlayMode = ResourcePlayMode.OfflinePlayMode;
            await ResetPackageAsync();

            var parameters = new OfflinePlayModeParameters();
            parameters.BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters();

            var operation = _package.InitializeAsync(parameters);
            await operation.WaitAsync();

            if (operation.Status != EOperationStatus.Succeed)
            {
                return Fail(
                    BootFailureCode.BuiltinResourceInvalid,
                    BootFailureStage.ResourceInitialization,
                    BootRetryScope.None,
                    ("operationStatus", operation.Status.ToString()),
                    ("resourcePackage", PACKAGE_NAME));
            }

            BootOperationResult manifestResult = await EnsurePackageManifestAsync("内置资源");
            if (!manifestResult.Succeeded)
                return manifestResult;

            var validation = await ValidateReadyResourcesAsync();
            if (!validation.Succeeded) return validation;
            _pendingResourceSource = BootResourceSource.BuiltinFallback;
            SetStatus("已使用内置资源");
            RefreshVersion();
            return Success();
        }

        private async UniTask<BootOperationResult> ValidateReadyResourcesAsync()
        {
            var locations = ValidateRequiredLocations();
            return locations.Succeeded ? await HostCompatibility.ValidateAsync(_package) : locations;
        }

        public void CommitSuccessfulHostVersion()
        {
            if (string.IsNullOrEmpty(_pendingHostVersion))
                return;

            PlayerPrefs.SetString(LAST_SUCCESSFUL_HOST_VERSION_KEY, _pendingHostVersion);
            PlayerPrefs.Save();
            _pendingHostVersion = null;
        }

        private BootOperationResult ValidateRequiredLocations()
        {
            foreach (var address in GetRequiredAddresses())
            {
                if (!_package.CheckLocationValid(address))
                {
                    return Fail(
                        address.Equals("Game.dll.bytes", StringComparison.OrdinalIgnoreCase)
                            ? BootFailureCode.GameDllMissing
                            : BootFailureCode.RequiredResourceMissing,
                        BootFailureStage.ResourceValidation,
                        BootRetryScope.None,
                        ("resourceAddress", address));
                }
                var available = ValidateAvailableAddress(address);
                if (!available.Succeeded) return available;
            }

            if (_actualPlayMode != ResourcePlayMode.EditorSimulateMode)
                return ValidateAotDlls();

            return Success();
        }

        private BootOperationResult FailAotValidation(BootFailureCode code, string dllName)
        {
            return Fail(code, BootFailureStage.AotList, BootRetryScope.None, ("dllName", dllName));
        }

        /// <summary>检查文件系统归属及依赖是否仍需远端下载，不等同于对所有文件重新计算哈希。</summary>
        private BootOperationResult ValidateAvailableAddress(string address)
        {
            if (_actualPlayMode == ResourcePlayMode.EditorSimulateMode) return Success();
            string exceptionType = string.Empty;
            try
            {
#if UNITY_INCLUDE_TESTS
                bool needsDownload = TestNeedsDownload != null
                    ? TestNeedsDownload(address) : _package.IsNeedDownloadFromRemote(address);
#else
                bool needsDownload = _package.IsNeedDownloadFromRemote(address);
#endif
                if (!needsDownload) return Success();
            }
            catch (Exception exception)
            {
                // 离线资源没有文件系统归属时 YooAsset 会抛异常，转换为可定位的启动失败。
                exceptionType = exception.GetType().FullName;
            }
            bool offline = _actualPlayMode == ResourcePlayMode.OfflinePlayMode;
            return Fail(offline ? BootFailureCode.BuiltinResourceInvalid : BootFailureCode.ResourceDownloadFailed,
                BootFailureStage.ResourceValidation, offline ? BootRetryScope.None : BootRetryScope.ResourceInitialization,
                ("resourceAddress", address), ("exceptionType", exceptionType));
        }

        private BootOperationResult ValidateAotDlls()
        {
            const string aotListAddress = "HybridCLRAssemblyList.bytes";
            if (!_package.CheckLocationValid(aotListAddress))
                return FailAotValidation(BootFailureCode.AotListInvalid, aotListAddress);

            var handle = _package.LoadAssetSync<TextAsset>(aotListAddress);
            try
            {
                if (handle.Status != EOperationStatus.Succeed)
                    return FailAotValidation(BootFailureCode.AotListInvalid, aotListAddress);

                var textAsset = handle.AssetObject as TextAsset;
                if (textAsset == null)
                    return FailAotValidation(BootFailureCode.AotListInvalid, aotListAddress);

                var lines = textAsset.text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int validCount = 0;

                foreach (var line in lines)
                {
                    string dllName = line.Trim();
                    if (string.IsNullOrEmpty(dllName) || dllName.StartsWith("//"))
                        continue;

                    if (dllName.Equals("Game.dll", StringComparison.OrdinalIgnoreCase) ||
                        dllName.Equals("Game", StringComparison.OrdinalIgnoreCase))
                        return FailAotValidation(BootFailureCode.AotListInvalid, "Game.dll");

                    if (!dllName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        dllName += ".dll";

                    if (!seen.Add(dllName))
                        return FailAotValidation(BootFailureCode.AotListInvalid, dllName);

                    string dllAddress = ToDllBytesAddress(dllName);
                    if (!_package.CheckLocationValid(dllAddress))
                        return Fail(BootFailureCode.AotDllMissing, BootFailureStage.AotList, BootRetryScope.None, ("dllName", dllName));

                    var available = ValidateAvailableAddress(dllAddress);
                    if (!available.Succeeded) return available;

                    validCount++;
                }

                if (validCount == 0)
                    return FailAotValidation(BootFailureCode.AotListInvalid, aotListAddress);
                return Success();
            }
            finally
            {
                handle.Release();
            }
        }

        private IEnumerable<string> GetRequiredAddresses()
        {
            yield return "Scenes/Main";
            yield return "tbrule.bytes";
            yield return "Game.dll.bytes";
            yield return "HybridCLRAssemblyList.bytes";
        }

        private static string ToDllBytesAddress(string dllName)
        {
            if (dllName.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase))
                return dllName;
            return $"{dllName}.bytes";
        }

        private void OnDownloadUpdate(DownloadUpdateData data)
        {
            float progress = data.Progress;
            _updateView.SetProgress(progress);
            _updateView.SetFileCount(data.CurrentDownloadCount, data.TotalDownloadCount);
            _updateView.SetSize((ulong)data.CurrentDownloadBytes, (ulong)data.TotalDownloadBytes);

            float currentTime = Time.realtimeSinceStartup;
            float deltaTime = currentTime - _lastDownloadTime;
            if (deltaTime >= 0.5f)
            {
                long bytesDiff = data.CurrentDownloadBytes - _lastDownloadBytes;
                long bytesPerSecond = deltaTime > 0 ? (long)(bytesDiff / deltaTime) : 0;
                _updateView.SetSpeed((ulong)bytesPerSecond);

                _lastDownloadBytes = data.CurrentDownloadBytes;
                _lastDownloadTime = currentTime;
            }
        }

        private void OnDownloadError(DownloadErrorData data)
        {
            Debug.LogError($"[ResourceBoot] 下载错误：{data.FileName}，底层错误：{data.ErrorInfo}");
        }

        private void SetStatus(string status)
        {
            Launcher.RecordStartupStage(status);
            // 离开下载阶段（包括失败回退）时清除旧进度；下载回调只更新数据。
            _updateView.RenderBusy(status);
        }

        public void Configure(HotUpdateView updateView)
        {
            _updateView = updateView != null
                ? updateView
                : throw new ArgumentNullException(nameof(updateView),
                    "ResourceBoot 必须配置 HotUpdateView 后才能启动。");
            if (_apkUpdate == null) _apkUpdate = new ApkUpdateFlow(_updateView);
        }

        private class RemoteServices : IRemoteServices
        {
            private readonly string _remoteURL;

            public RemoteServices(string remoteURL)
            {
                _remoteURL = remoteURL;
            }

            public string GetRemoteMainURL(string fileName)
            {
                return $"{_remoteURL}/{fileName}";
            }

            public string GetRemoteFallbackURL(string fileName)
            {
                return $"{_remoteURL}/{fileName}";
            }
        }
    }
}
