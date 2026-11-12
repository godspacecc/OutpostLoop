using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 保存当前显示设置，包括分辨率预设和窗口模式；实际应用由显示工具和设置系统完成。
    /// </summary>
    public sealed class DisplaySettingsModel : AbstractModel
    {
        [System.Obsolete("Runtime settings are stored in settings.json.")]
        public const string ResolutionPlayerPrefsKey = "";

        [System.Obsolete("Runtime settings are stored in settings.json.")]
        public const string WindowModePlayerPrefsKey = "";

        public BindableProperty<string> CurrentResolutionCode { get; } =
            new BindableProperty<string>(DisplayResolutionPresets.Default.Code);

        public BindableProperty<FullScreenMode> CurrentWindowMode { get; } =
            new BindableProperty<FullScreenMode>(FullScreenMode.Windowed);

        protected override void OnInit()
        {
        }

        public DisplayResolutionPreset GetCurrentResolution()
        {
            return DisplayResolutionPresets.FindOrDefault(CurrentResolutionCode.Value);
        }

        public void SetResolutionCode(string resolutionCode)
        {
            CurrentResolutionCode.Value = DisplayResolutionPresets.FindOrDefault(resolutionCode).Code;
        }

        public void SetWindowMode(FullScreenMode mode)
        {
            CurrentWindowMode.Value = SanitizeWindowMode(mode);
        }

        public void SetWindowMode(string mode)
        {
            CurrentWindowMode.Value = TryParseWindowMode(mode, out var parsed)
                ? parsed
                : FullScreenMode.Windowed;
        }

        public void ResetToDefault()
        {
            CurrentResolutionCode.Value = DisplayResolutionPresets.Default.Code;
            CurrentWindowMode.Value = FullScreenMode.Windowed;
        }

        public void Apply(DisplaySettingsData data)
        {
            data = (data ?? DisplaySettingsData.CreateDefault()).Sanitize();
            SetResolutionCode(data.ResolutionCode);
            SetWindowMode(data.WindowMode);
        }

        public DisplaySettingsData ToData()
        {
            return new DisplaySettingsData
            {
                ResolutionCode = CurrentResolutionCode.Value,
                WindowMode = CurrentWindowMode.Value
            }.Sanitize();
        }

        public static FullScreenMode SanitizeWindowMode(FullScreenMode mode)
        {
            return mode == FullScreenMode.FullScreenWindow ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        }

        private static bool TryParseWindowMode(string mode, out FullScreenMode parsed)
        {
            if (System.Enum.TryParse(mode, out parsed))
            {
                parsed = SanitizeWindowMode(parsed);
                return true;
            }

            parsed = FullScreenMode.Windowed;
            return false;
        }
    }
}
