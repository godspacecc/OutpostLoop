using System;
using Boot;
using UnityEngine;
using UnityEngine.UI;

namespace Boot.UI
{
    /// <summary>
    /// Boot 热更新界面，显示资源初始化、下载进度和重试入口。
    /// </summary>
    public class HotUpdateView : MonoBehaviour
    {
        [Header("状态显示")]
        [SerializeField] private Text _statusText;
        [SerializeField] private Text _versionText;
        [SerializeField] private Text _titleText;
        [SerializeField] private Text _guidanceText;
        [SerializeField] private Text _detailsText;

        [Header("进度显示")]
        [SerializeField] private GameObject _progressArea;
        [SerializeField] private GameObject _downloadStatsArea;
        [SerializeField] private GameObject _buttonArea;
        [SerializeField] private Slider _progressSlider;
        [SerializeField] private Text _progressText;
        [SerializeField] private Text _sizeText;
        [SerializeField] private Text _fileCountText;
        [SerializeField] private Text _speedText;

        [Header("按钮")]
        [SerializeField] private Button _retryButton;
        [SerializeField] private Button _copyDetailsButton;
        [SerializeField] private Button _quitButton;

        [Header("启动阶段提示（兼容旧预制体）")]
        // 主题控件是表现扩展，启动协议和独立测试视图只依赖原有必需引用。
        [SerializeField] private BootBeaconGraphic _beacon;
        [SerializeField] private Text _stageText;
        [SerializeField] private GameObject _busyIndicator;
        [SerializeField] private Text _copyNotice;
        // 仅在启动中断时出现的恢复面板，正常抵达时不遮挡空间。
        [SerializeField] private GameObject _recoveryShade;
        private static readonly Color SignalColor = new Color32(193, 217, 235, 255);
        private static readonly Color WarningColor = new Color32(241, 188, 130, 255);

        public event Action OnRetryClicked;
        public event Action OnCopyDetailsClicked;
        public event Action OnQuitClicked;

        private bool _initialized;
        private bool _apkActions;
        private Action _apkPrimary;
        private Action _apkSecondary;
        private Action _apkCopy;
        private Text _primaryLabel;
        private Text _secondaryLabel;
        private Text _quitLabel;
        private string _originalPrimary;
        private string _originalSecondary;
        private string _originalQuit;

#if UNITY_INCLUDE_TESTS
        public void InitializeForTests()
        {
            InitializeOnce();
        }
#endif

        private void Awake()
        {
            InitializeOnce();
        }

        private void InitializeOnce()
        {
            if (_initialized)
            {
                return;
            }

            ValidateRequiredReferences();
            _retryButton.onClick.AddListener(() => { if (_apkActions) _apkPrimary?.Invoke(); else OnRetryClicked?.Invoke(); });
            _copyDetailsButton.onClick.AddListener(() => { if (_apkActions) _apkSecondary?.Invoke(); else OnCopyDetailsClicked?.Invoke(); });
            _quitButton.onClick.AddListener(() => { if (_apkActions) _apkCopy?.Invoke(); else OnQuitClicked?.Invoke(); });
            RenderBusy("正在准备启动...");
            SetVersion(string.Empty, string.Empty);
            _initialized = true;
        }

        public void SetStatus(string status)
        {
            _statusText.text = status;
        }

        public void SetVersion(string localVersion, string remoteVersion)
        {
            if (string.IsNullOrWhiteSpace(localVersion))
            {
                _versionText.text = "资源版本：检测中";
            }
            else if (string.IsNullOrWhiteSpace(remoteVersion) ||
                string.Equals(localVersion, remoteVersion, StringComparison.Ordinal))
            {
                _versionText.text = $"资源版本：{localVersion}";
            }
            else
            {
                _versionText.text = $"当前 {localVersion}（目标 {remoteVersion} 未应用）";
            }
        }

        public void RenderBusy(string status)
        {
            PresentSignal("正在前往空间", SignalColor, true);
            _titleText.text = "正在启动";
            _titleText.gameObject.SetActive(false);
            _guidanceText.text = string.Empty;
            _guidanceText.gameObject.SetActive(false);
            _detailsText.text = string.Empty;
            _detailsText.gameObject.SetActive(false);
            _statusText.text = status;
            _statusText.gameObject.SetActive(true);
            _progressArea.SetActive(false);
            _downloadStatsArea.SetActive(false);
            HideAllButtons();
        }

        public void RenderNormal(string status)
        {
            PresentSignal("正在抵达空间", SignalColor, true);
            _titleText.text = "启动中";
            _titleText.gameObject.SetActive(false);
            _guidanceText.text = string.Empty;
            _guidanceText.gameObject.SetActive(false);
            _detailsText.text = string.Empty;
            _detailsText.gameObject.SetActive(false);
            _statusText.text = status;
            _statusText.gameObject.SetActive(true);
            _progressArea.SetActive(false);
            _downloadStatsArea.SetActive(false);
            HideAllButtons();
        }

        public void RenderDownloading(string status)
        {
            PresentSignal("资源更新", SignalColor, false);
            _titleText.gameObject.SetActive(false);
            _guidanceText.gameObject.SetActive(false);
            _detailsText.gameObject.SetActive(false);
            _statusText.text = status;
            _statusText.gameObject.SetActive(true);
            _progressArea.SetActive(true);
            _downloadStatsArea.SetActive(true);
            _fileCountText.gameObject.SetActive(false);
            HideAllButtons();
        }

        public void RenderFailure(BootFailure failure)
        {
            if (failure == null)
            {
                throw new ArgumentNullException(nameof(failure));
            }

            PresentSignal("启动中断", WarningColor, false, true);
            _titleText.text = failure.PlayerTitle;
            _titleText.gameObject.SetActive(true);
            _guidanceText.text = failure.PlayerGuidance;
            _guidanceText.gameObject.SetActive(!string.IsNullOrWhiteSpace(failure.PlayerGuidance));
            _detailsText.text = failure.DiagnosticDetails;
            _detailsText.gameObject.SetActive(!string.IsNullOrWhiteSpace(failure.DiagnosticDetails));
            _statusText.text = "启动失败";
            _statusText.gameObject.SetActive(false);
            _progressArea.SetActive(false);
            _downloadStatsArea.SetActive(false);
            ShowButtons(failure.Actions);
        }

        public void ShowCopiedNotice()
        {
            _statusText.text = "已复制错误信息";
            if (_copyNotice)
            {
                _copyNotice.text = _statusText.text;
                _copyNotice.gameObject.SetActive(true);
            }
        }

        /// <summary>只切换阶段文案和辅助提示（旧信标引用仅兼容测试预制体），真实进度仍由下载事件写入。</summary>
        private void PresentSignal(string stage, Color tint, bool checking, bool failure = false)
        {
            if (_recoveryShade)
            {
                _recoveryShade.SetActive(failure);
                if (_stageText)
                {
                    var position = _stageText.rectTransform.anchoredPosition;
                    _stageText.rectTransform.anchoredPosition = new Vector2(position.x, failure ? -372 : -533);
                    _stageText.fontSize = failure ? 15 : 26;
                }
            }
            if (_beacon) _beacon.color = tint;
            if (_stageText)
            {
                _stageText.text = stage;
                _stageText.color = tint;
            }
            if (_busyIndicator) _busyIndicator.SetActive(checking);
            if (_copyNotice) _copyNotice.gameObject.SetActive(false);
        }

        public void SetProgress(float progress)
        {
            _progressSlider.value = progress;
            _progressText.text = $"{(int)(progress * 100)}%";
        }

        public void SetSize(ulong downloadedBytes, ulong totalBytes)
        {
            string downloaded = FormatFileSize(downloadedBytes);
            string total = FormatFileSize(totalBytes);
            _sizeText.text = $"大小: {downloaded} / {total}";
        }

        public void SetFileCount(int downloadedCount, int totalCount)
        {
            _fileCountText.text = $"文件: {downloadedCount} / {totalCount}";
        }

        public void SetSpeed(ulong bytesPerSecond)
        {
            _speedText.text = $"速度: {FormatFileSize(bytesPerSecond)}/s";
        }

        public void ShowRetryButton(bool show)
        {
            _retryButton.gameObject.SetActive(show);
            _retryButton.interactable = show;
        }

        public void HideAllButtons()
        {
            _apkActions = false;
            _apkPrimary = null;
            _apkSecondary = null;
            _apkCopy = null;
            if (_primaryLabel) _primaryLabel.text = _originalPrimary;
            if (_secondaryLabel) _secondaryLabel.text = _originalSecondary;
            if (_quitLabel) _quitLabel.text = _originalQuit;
            _buttonArea.SetActive(false);
            ShowRetryButton(false);
            ShowCopyDetailsButton(false);
            ShowQuitButton(false);
        }

        private void ShowButtons(BootFailureAction actions)
        {
            HideAllButtons();
            _buttonArea.SetActive(actions != BootFailureAction.None);
            ShowRetryButton((actions & BootFailureAction.Retry) != 0);
            ShowCopyDetailsButton((actions & BootFailureAction.CopyDetails) != 0);
            ShowQuitButton((actions & BootFailureAction.Quit) != 0);
        }

        public void RenderApkChoice(string message, string primary, Action accept, string secondary, Action defer)
        {
            EnsureApkLabels();
            RenderNormal(string.Empty);
            PresentSignal("应用更新", SignalColor, false);
            _titleText.text = "有新版本可下载";
            _titleText.gameObject.SetActive(true);
            _guidanceText.text = message;
            _guidanceText.gameObject.SetActive(true);
            _statusText.gameObject.SetActive(false);
            _apkActions = true;
            _apkPrimary = accept;
            _apkSecondary = defer;
            _primaryLabel.text = primary;
            _secondaryLabel.text = secondary;
            _buttonArea.SetActive(true);
            ShowRetryButton(true);
            ShowCopyDetailsButton(true);
        }

        public void RenderApkDownload(Action cancel)
        {
            EnsureApkLabels();
            RenderDownloading("正在下载安装包……");
            _apkActions = true;
            _apkPrimary = cancel;
            _primaryLabel.text = "取消下载";
            _buttonArea.SetActive(true);
            ShowRetryButton(true);
        }

        public void RenderApkInstalling(Action defer)
        {
            RenderApkChoice("正在校验安装包并打开系统安装界面...", "安装处理中", () => { }, "稍后更新", defer);
            _titleText.text = "正在安装更新";
            ShowRetryButton(false);
        }

        public void RenderApkInstallFailure(string message, string details, Action retry, Action defer, Action copy)
        {
            RenderApkChoice(message, "重试安装", retry, "稍后更新", defer);
            PresentSignal("应用更新中断", WarningColor, false);
            _titleText.text = "安装更新失败";
            int summaryEnd = details.IndexOf("\n包名：", StringComparison.Ordinal);
            _detailsText.text = summaryEnd >= 0 ? details.Substring(0, Math.Min(summaryEnd, 512)) : "错误详情已记录，可复制给开发者排查。";
            _detailsText.gameObject.SetActive(true);
            _apkCopy = copy;
            _quitLabel.text = "复制错误详情";
            ShowQuitButton(true);
        }

        public void ShowApkDetailsCopied() { _quitLabel.text = "错误详情已复制"; }

        private void EnsureApkLabels()
        {
            if (_primaryLabel) return;
            _primaryLabel = _retryButton.GetComponentInChildren<Text>(true);
            _secondaryLabel = _copyDetailsButton.GetComponentInChildren<Text>(true);
            _quitLabel = _quitButton.GetComponentInChildren<Text>(true);
            RequireAssigned(_primaryLabel, nameof(_primaryLabel));
            RequireAssigned(_secondaryLabel, nameof(_secondaryLabel));
            RequireAssigned(_quitLabel, nameof(_quitLabel));
            _originalPrimary = _primaryLabel.text;
            _originalSecondary = _secondaryLabel.text;
            _originalQuit = _quitLabel.text;
        }

        private void ShowCopyDetailsButton(bool show)
        {
            _copyDetailsButton.gameObject.SetActive(show);
            _copyDetailsButton.interactable = show;
        }

        private void ShowQuitButton(bool show)
        {
            _quitButton.gameObject.SetActive(show);
            _quitButton.interactable = show;
        }

        private void ValidateRequiredReferences()
        {
            RequireAssigned(_statusText, nameof(_statusText));
            RequireAssigned(_versionText, nameof(_versionText));
            RequireAssigned(_titleText, nameof(_titleText));
            RequireAssigned(_guidanceText, nameof(_guidanceText));
            RequireAssigned(_detailsText, nameof(_detailsText));
            RequireAssigned(_progressArea, nameof(_progressArea));
            RequireAssigned(_downloadStatsArea, nameof(_downloadStatsArea));
            RequireAssigned(_buttonArea, nameof(_buttonArea));
            RequireAssigned(_progressSlider, nameof(_progressSlider));
            RequireAssigned(_progressText, nameof(_progressText));
            RequireAssigned(_sizeText, nameof(_sizeText));
            RequireAssigned(_fileCountText, nameof(_fileCountText));
            RequireAssigned(_speedText, nameof(_speedText));
            RequireAssigned(_retryButton, nameof(_retryButton));
            RequireAssigned(_copyDetailsButton, nameof(_copyDetailsButton));
            RequireAssigned(_quitButton, nameof(_quitButton));
        }

        private void RequireAssigned(UnityEngine.Object value, string fieldName)
        {
            if (value)
            {
                return;
            }

            throw new InvalidOperationException(
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

        private string FormatFileSize(ulong bytes)
        {
            const ulong KB = 1024;
            const ulong MB = KB * 1024;
            const ulong GB = MB * 1024;

            if (bytes >= GB)
                return $"{(bytes / (float)GB):F2} GB";
            if (bytes >= MB)
                return $"{(bytes / (float)MB):F2} MB";
            if (bytes >= KB)
                return $"{(bytes / (float)KB):F2} KB";
            return $"{bytes} B";
        }
    }
}
