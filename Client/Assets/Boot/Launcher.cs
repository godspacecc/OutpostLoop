using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Boot.UI;
using YooAsset;

namespace Boot
{
    public enum BootMainStartupStage
    {
        InitializingGame,
        LoadingGameConfig,
        PreparingStartUI
    }

    /// <summary>
    /// Boot 启动链入口，负责把必需 UI、资源初始化和热更新 DLL 加载串接到主场景前。
    /// </summary>
    [RequireComponent(typeof(ResourceBoot))]
    public class Launcher : MonoBehaviour
    {
        private WindowsUpdateFlow windowsUpdate;
        [Header("界面引用")]
        [SerializeField] private HotUpdateView _updateView;
        [SerializeField] private Canvas _updateCanvas;

        private static Launcher current;
        private ResourceBoot _resourceBoot;
        private BootStartupLogStore _startupLog;
        private bool _isStarting;
        private bool _resourceServerEditing;
        /// <summary>只允许已结束的资源失败流程编辑地址，不中断下载、安装或业务初始化。</summary>
        public bool CanEditResourceServer => !_isStarting && _terminalState == StartupTerminalState.Failed &&
            _currentFailure != null && (_currentFailure.Code == BootFailureCode.BootstrapConfigInvalid ||
            _currentFailure.Code == BootFailureCode.HostInitializationFailed ||
            _currentFailure.Code == BootFailureCode.PackageVersionRequestFailed ||
            _currentFailure.Code == BootFailureCode.PackageManifestUpdateFailed ||
            _currentFailure.Code == BootFailureCode.ResourceDownloadFailed);
        public bool ShouldShowResourceServer => CanEditResourceServer;
        public event Action<bool> ResourceServerAvailabilityChanged;

        /// <summary>锁定失败恢复会话，避免重复打开或同时点击重试。</summary>
        public bool TryBeginResourceServerEdit()
        {
            if (!CanEditResourceServer || _resourceServerEditing) return false;
            _resourceServerEditing = true;
            return true;
        }

        /// <summary>取消保持原失败；保存后串行重走包括宿主更新校验在内的启动链。</summary>
        public void FinishResourceServerEdit(bool restart)
        {
            if (!_resourceServerEditing) return;
            _resourceServerEditing = false;
            if (restart && CanEditResourceServer) RunStartupAsync().Forget();
        }
        private bool _viewEventsBound;
        private BootFailure _currentFailure;
        private StartupTerminalState _terminalState = StartupTerminalState.Running;

#if UNITY_INCLUDE_TESTS
        public static Func<ResourcePackage> TestPackageResolver { get; set; }
        public static Func<UniTask<bool>> TestMainSceneLoader { get; set; }
        public static Action<string> TestClipboardWriter { get; set; }
        public static Action TestQuitPort { get; set; }

        public static void ResetTestPorts()
        {
            TestPackageResolver = null;
            TestMainSceneLoader = null;
            TestClipboardWriter = null;
            TestQuitPort = null;
            current = null;
        }

        public void InitializeForTests()
        {
            _updateView.InitializeForTests();
            InitializeOwner();
        }

        public UniTask StartForTestsAsync()
        {
            return RunStartupAsync();
        }
#endif

        private enum StartupTerminalState
        {
            Running,
            InstallerExitPending,
            ReadyPendingRelease,
            Released,
            Failed
        }

        public static bool TryReportMainStage(BootMainStartupStage stage)
        {
            if (!Enum.IsDefined(typeof(BootMainStartupStage), stage))
            {
                return false;
            }

            var owner = current;
            if (!owner || owner._terminalState != StartupTerminalState.Running)
            {
                return false;
            }

            owner.SetMainStage(stage);
            return true;
        }

        public static bool TryReportMainReady()
        {
            var owner = current;
            if (!owner || owner._terminalState != StartupTerminalState.Running)
            {
                return false;
            }

            owner._terminalState = StartupTerminalState.ReadyPendingRelease;
            owner._startupLog?.RecordStage("主界面就绪");
            owner._startupLog?.Dispose();
            owner.ReleaseAfterNextFrameAsync().Forget();
            return true;
        }

        /// <summary>接受热更层的展示进度，不将业务阶段固化为宿主枚举；终态后拒绝迟到报告。</summary>
        public static bool TryReportMainProgress(string message)
        {
            var owner = current;
            if (!owner || owner._terminalState != StartupTerminalState.Running || string.IsNullOrWhiteSpace(message))
                return false;
            // 仅允许短展示文本，避免异常数据淹没启动界面与日志。
            string text = message.Length <= 160 ? message : message.Substring(0, 160);
            owner._updateView.RenderNormal(text);
            owner._startupLog?.RecordStage(text);
            return true;
        }

        public static bool TryReportMainFailure()
        {
            return TryReportMainFailure(null);
        }

        public static bool TryReportMainFailure(IReadOnlyDictionary<string, string> diagnosticFields)
        {
            var owner = current;
            if (!owner || owner._terminalState != StartupTerminalState.Running)
            {
                return false;
            }

            owner._terminalState = StartupTerminalState.Failed;
            owner._updateView.gameObject.SetActive(true);
            owner.ShowFailure(BootFailure.Create(
                BootFailureCode.MainInitializationFailed,
                BootFailureStage.MainInitialization,
                BootRetryScope.None,
                diagnosticFields));
            return true;
        }

        private void Awake()
        {
            if (!current)
                _startupLog = new BootStartupLogStore(BootLogPaths.Root, Application.version, Application.platform.ToString());
            InitializeOwner();
        }

        /// <summary>记录真实启动阶段，不推算或伪造加载进度。</summary>
        public static void RecordStartupStage(string stage) => current?._startupLog?.RecordStage(stage);

        private void InitializeOwner()
        {
            ValidateRequiredReferences();
            if (current && current != this)
            {
                Debug.LogWarning(
                    $"[Launcher] 检测到重复的启动组件。继续使用：{GetHierarchyPath(current.transform)}；" +
                    $"停用重复组件：{GetHierarchyPath(transform)}，并移除其启动界面。");
                if (_updateCanvas)
                {
                    _updateCanvas.gameObject.SetActive(false);
                    DestroyOwnedObject(_updateCanvas.gameObject);
                }

                enabled = false;
                return;
            }

            current = this;
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);
                DontDestroyOnLoad(_updateCanvas.gameObject);
            }

            _resourceBoot = GetComponent<ResourceBoot>();
            if (!_resourceBoot)
            {
                throw MissingReference(nameof(_resourceBoot));
            }
        }

        private void Start()
        {
            RunStartupAsync().Forget();
        }

        private void OnDestroy()
        {
            _startupLog?.RecordStage("启动对象释放");
            _startupLog?.Dispose();
            if (current != this)
            {
                return;
            }

            current = null;
            if (_viewEventsBound)
            {
                _updateView.OnRetryClicked -= OnRetryClicked;
                _updateView.OnCopyDetailsClicked -= OnCopyDetailsClicked;
                _updateView.OnQuitClicked -= OnQuitClicked;
                _viewEventsBound = false;
            }
        }

        private async UniTask RunStartupAsync()
        {
            if (_isStarting)
            {
                Debug.LogWarning("[Launcher] 启动流程正在执行，请勿重复触发。");
                return;
            }

            _isStarting = true;
            ResourceServerAvailabilityChanged?.Invoke(false);

            try
            {
                _terminalState = StartupTerminalState.Running;
                RecordStartupStage("开始准备资源");
                _currentFailure = null;
                _updateView.gameObject.SetActive(true);
                _updateView.RenderBusy("正在初始化资源...");

                _resourceBoot.Configure(_updateView);
                BindViewEvents();

                // 宿主整包更新先于资源和热更程序集；成功交接后必须终止本轮启动。
                if (WindowsInstaller.Supported)
                {
                    WindowsInstaller.InvalidatePreviousHostCache();
                    string server = null;
                    try { server = (await BootstrapConfig.LoadAsync()).ResourceServer; }
                    catch (Exception) { } // 由后续资源初始化统一显示启动配置错误。
                    windowsUpdate ??= new WindowsUpdateFlow(_updateView);
                    if (server != null && await windowsUpdate.RunAsync(server, this.GetCancellationTokenOnDestroy()))
                    {
                        _terminalState = StartupTerminalState.InstallerExitPending;
                        RecordStartupStage("已交接安装器，等待退出");
                        Application.Quit();
                        return;
                    }
                }

                BootOperationResult resourceResult = await _resourceBoot.InitializeAsync();
                if (!RequireResult(resourceResult, BootFailureStage.ResourceInitialization, out resourceResult))
                {
                    return;
                }

                if (!resourceResult.Succeeded)
                {
                    ShowFailure(resourceResult.Failure);
                    return;
                }

                RecordStartupStage("资源准备完成");
                _updateView.RenderBusy("正在加载游戏代码……");
                RecordStartupStage("开始加载游戏代码");
                var package = ResolveOutpostLoop();
                BootOperationResult codeResult = await CodeLoader.LoadAsync(package);
                if (!RequireResult(codeResult, BootFailureStage.GameAssembly, out codeResult))
                {
                    return;
                }

                if (!codeResult.Succeeded)
                {
                    ShowFailure(codeResult.Failure);
                    return;
                }

                RecordStartupStage("游戏代码加载完成");
                _resourceBoot.CommitSuccessfulHostVersion();
                _updateView.RenderNormal("正在进入游戏……");

                RecordStartupStage("开始加载主场景");
                bool sceneLoaded = await GetMainSceneLoader()();
                RecordStartupStage("主场景加载结果：" + sceneLoaded);
                if (!sceneLoaded)
                {
                    ShowFailure(BootFailure.Create(
                        BootFailureCode.MainSceneLoadFailed,
                        BootFailureStage.MainScene,
                        BootRetryScope.MainScene,
                        DiagnosticFields(("sceneAddress", "Scenes/Main"))));
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                ShowFailure(BootFailure.Unexpected(e, BootFailureStage.Unexpected));
            }
            finally
            {
                _isStarting = false;
                ResourceServerAvailabilityChanged?.Invoke(CanEditResourceServer);
            }
        }

        private async UniTask RetryMainSceneAsync()
        {
            if (_isStarting)
            {
                return;
            }

            _isStarting = true;
            ResourceServerAvailabilityChanged?.Invoke(false);
            _terminalState = StartupTerminalState.Running;
            RecordStartupStage("重试主场景");
            _currentFailure = null;
            _updateView.RenderBusy("正在重新进入游戏……");

            try
            {
                RecordStartupStage("开始加载主场景");
                bool sceneLoaded = await GetMainSceneLoader()();
                RecordStartupStage("主场景加载结果：" + sceneLoaded);
                if (!sceneLoaded)
                {
                    ShowFailure(BootFailure.Create(
                        BootFailureCode.MainSceneLoadFailed,
                        BootFailureStage.MainScene,
                        BootRetryScope.MainScene,
                        DiagnosticFields(("sceneAddress", "Scenes/Main"))));
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                ShowFailure(BootFailure.Unexpected(e, BootFailureStage.MainScene));
            }
            finally
            {
                _isStarting = false;
                ResourceServerAvailabilityChanged?.Invoke(CanEditResourceServer);
            }
        }

        private void ShowFailure(BootFailure failure)
        {
            _currentFailure = failure ?? BootFailure.Create(
                BootFailureCode.UnexpectedException,
                BootFailureStage.Unexpected);
            _terminalState = StartupTerminalState.Failed;
            RecordStartupStage("启动失败：" + _currentFailure.DiagnosticDetails);
            BootErrorLogStore.TryWrite(_currentFailure, out _, out _);
            Debug.LogWarning($"[Launcher] 启动失败：{_currentFailure.Code}");
            _updateView.gameObject.SetActive(true);
            _updateView.RenderFailure(_currentFailure);
            ResourceServerAvailabilityChanged?.Invoke(CanEditResourceServer);
        }

        public void OnRetryClicked()
        {
            if (_isStarting || _resourceServerEditing || _currentFailure == null)
            {
                return;
            }

            switch (_currentFailure.RetryScope)
            {
                case BootRetryScope.ResourceInitialization:
                    RunStartupAsync().Forget();
                    break;
                case BootRetryScope.MainScene:
                    RetryMainSceneAsync().Forget();
                    break;
            }
        }

        private void OnCopyDetailsClicked()
        {
            if (_currentFailure == null)
            {
                return;
            }

#if UNITY_INCLUDE_TESTS
            if (TestClipboardWriter != null)
            {
                TestClipboardWriter(_currentFailure.DiagnosticDetails);
            }
            else
#endif
            {
                GUIUtility.systemCopyBuffer = _currentFailure.DiagnosticDetails;
            }

            _updateView.ShowCopiedNotice();
        }

        private void OnQuitClicked()
        {
#if UNITY_INCLUDE_TESTS
            if (TestQuitPort != null)
            {
                TestQuitPort();
                return;
            }
#endif

            Application.Quit();
        }

        private void BindViewEvents()
        {
            if (_viewEventsBound)
            {
                return;
            }

            _updateView.OnRetryClicked += OnRetryClicked;
            _updateView.OnCopyDetailsClicked += OnCopyDetailsClicked;
            _updateView.OnQuitClicked += OnQuitClicked;
            _viewEventsBound = true;
        }

        private bool RequireResult(
            BootOperationResult result,
            BootFailureStage nullStage,
            out BootOperationResult safeResult)
        {
            if (result != null)
            {
                safeResult = result;
                return true;
            }

            safeResult = BootOperationResult.Failed(BootFailure.Create(
                BootFailureCode.UnexpectedException,
                nullStage));
            ShowFailure(safeResult.Failure);
            return false;
        }

        private static Func<UniTask<bool>> GetMainSceneLoader()
        {
#if UNITY_INCLUDE_TESTS
            return TestMainSceneLoader ?? (() => ResourceSceneLoader.LoadMainAsync());
#else
            return () => ResourceSceneLoader.LoadMainAsync();
#endif
        }

        private static ResourcePackage ResolveOutpostLoop()
        {
#if UNITY_INCLUDE_TESTS
            if (TestPackageResolver != null)
            {
                return TestPackageResolver();
            }
#endif

            return YooAssets.TryGetPackage("OutpostLoop");
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

        private void ValidateRequiredReferences()
        {
            RequireAssigned(_updateView, nameof(_updateView));
            RequireAssigned(_updateCanvas, nameof(_updateCanvas));
            if (_updateCanvas.transform.parent)
            {
                throw MissingReference(nameof(_updateCanvas));
            }

            if (!_updateView.transform.IsChildOf(_updateCanvas.transform))
            {
                throw MissingReference(nameof(_updateView));
            }
        }

        private void RequireAssigned(UnityEngine.Object value, string fieldName)
        {
            if (value)
            {
                return;
            }

            throw MissingReference(fieldName);
        }

        private InvalidOperationException MissingReference(string fieldName)
        {
            return new InvalidOperationException(
                $"Boot 必需引用缺失：{GetHierarchyPath(transform)}，组件字段：{GetType().Name}.{fieldName}");
        }

        private static string GetHierarchyPath(Transform target)
        {
            string path = target.name;
            while (target.parent)
            {
                target = target.parent;
                path = $"{target.name}/{path}";
            }

            return path;
        }

        private void SetMainStage(BootMainStartupStage stage)
        {
            RecordStartupStage("业务初始化阶段：" + stage);
            switch (stage)
            {
                case BootMainStartupStage.InitializingGame:
                    _updateView.RenderNormal("正在初始化游戏……");
                    break;
                case BootMainStartupStage.LoadingGameConfig:
                    _updateView.RenderNormal("正在加载游戏配置……");
                    break;
                case BootMainStartupStage.PreparingStartUI:
                    _updateView.RenderNormal("正在准备开始界面……");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(stage), stage, null);
            }
        }

        private async UniTaskVoid ReleaseAfterNextFrameAsync()
        {
            var owner = this;
            // 启动拥有者提前销毁时取消等待，避免退出或编辑器测试遗留跨帧任务。
            if (await UniTask.NextFrame(cancellationToken: this.GetCancellationTokenOnDestroy(), cancelImmediately: true).SuppressCancellationThrow())
                return;
            if (current != owner || _terminalState != StartupTerminalState.ReadyPendingRelease)
            {
                return;
            }

            _terminalState = StartupTerminalState.Released;
            if (_updateCanvas)
            {
                DestroyOwnedObject(_updateCanvas.gameObject);
            }

            if (this)
            {
                DestroyOwnedObject(gameObject);
            }
        }

        private static void DestroyOwnedObject(GameObject target)
        {
            if (!target)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
                return;
            }

            DestroyImmediate(target);
        }
    }
}
