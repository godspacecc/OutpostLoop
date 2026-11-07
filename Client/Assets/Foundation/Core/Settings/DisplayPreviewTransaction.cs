using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 暂存显示设置预览，确认时提交，取消或超时时回滚到进入预览前的稳定设置。
    /// </summary>
    public sealed class DisplayPreviewTransaction : AbstractSystem
    {
        /// <summary>显示预览的确认超时（秒）。</summary>
        private const float TimeoutSeconds = 10f;
        /// <summary>预览超时使用的可替换时间源。</summary>
        private ISettingsClock clock = new UnityRealtimeSettingsClock();
        /// <summary>预览前已提交的稳定显示快照。</summary>
        private DisplaySettingsData previousDisplay;
        /// <summary>待确认的分辨率标识。</summary>
        private string previewResolutionCode;
        /// <summary>待确认的窗口模式。</summary>
        private FullScreenMode previewWindowMode;
        /// <summary>自动回滚的实时时间点。</summary>
        private float timeoutAt;
        /// <summary>是否持有未确认的显示预览。</summary>
        private bool active;

        public bool IsActive => active;
        public float RemainingSeconds => active ? Mathf.Max(0f, timeoutAt - clock.Now) : 0f;

        protected override void OnInit()
        {
            LifeCycle.RegisterUpdate(Update);
        }

        protected override void OnDeinit()
        {
            Cancel();
            LifeCycle.UnregisterUpdate(Update);
        }

        public void SetClockForTests(ISettingsClock clock)
        {
            this.clock = clock ?? new UnityRealtimeSettingsClock();
        }

        /// <summary>临时应用候选显示设置，保持已提交模型不变。</summary>
        public void BeginPreview(string resolutionCode, FullScreenMode windowMode)
        {
            if (active)
            {
                Cancel();
            }

            previousDisplay = this.GetModel<DisplaySettingsModel>().ToData();
            var preset = DisplayResolutionPresets.FindOrDefault(resolutionCode);
            previewResolutionCode = preset.Code;
            previewWindowMode = DisplaySettingsModel.SanitizeWindowMode(windowMode);
            this.GetUtility<IDisplayResolutionApplier>().Apply(preset, previewWindowMode);
            timeoutAt = clock.Now + TimeoutSeconds;
            active = true;
        }

        /// <summary>确认预览，更新模型并登记延迟保存。</summary>
        public void Confirm()
        {
            if (!active)
            {
                return;
            }

            global::Game.Game.Interface.SendCommand(new ApplyDisplaySettingsCommand
            {
                ResolutionCode = previewResolutionCode,
                WindowMode = previewWindowMode,
                Commit = true
            });
            Clear();
        }

        /// <summary>取消预览并恢复之前的显示设置，不写入候选值。</summary>
        public void Cancel()
        {
            if (!active)
            {
                return;
            }

            var preset = DisplayResolutionPresets.FindOrDefault(previousDisplay?.ResolutionCode);
            var windowMode = DisplaySettingsModel.SanitizeWindowMode(previousDisplay?.WindowMode ?? FullScreenMode.Windowed);
            this.GetUtility<IDisplayResolutionApplier>().Apply(preset, windowMode);
            Clear();
        }

        private void Update()
        {
            if (active && clock.Now >= timeoutAt)
            {
                Cancel();
            }
        }

        /// <summary>清空预览会话，避免重复提交或回滚。</summary>
        private void Clear()
        {
            active = false;
            previousDisplay = null;
            previewResolutionCode = null;
            previewWindowMode = FullScreenMode.Windowed;
            timeoutAt = 0f;
        }
    }
}
