using UnityEngine;

namespace Game
{
    /// <summary>
    /// 将显示设置应用到 Unity Screen.SetResolution，保持模型与平台 API 的责任分离。
    /// </summary>
    public sealed class UnityDisplayResolutionApplier : IDisplayResolutionApplier
    {
        public void Apply(DisplayResolutionPreset preset, FullScreenMode windowMode)
        {
            if (preset == null)
            {
                return;
            }

            var sanitizedMode = DisplaySettingsModel.SanitizeWindowMode(windowMode);
            var desktop = Screen.currentResolution;
            var resolved = ResolveSize(preset, sanitizedMode, desktop.width, desktop.height);
            Screen.SetResolution(resolved.x, resolved.y, sanitizedMode);
        }

        public static Vector2Int ResolveSize(
            DisplayResolutionPreset preset,
            FullScreenMode windowMode,
            int desktopWidth,
            int desktopHeight)
        {
            var sanitizedMode = DisplaySettingsModel.SanitizeWindowMode(windowMode);
            if (sanitizedMode == FullScreenMode.FullScreenWindow &&
                desktopWidth > 0 &&
                desktopHeight > 0)
            {
                return new Vector2Int(desktopWidth, desktopHeight);
            }

            return new Vector2Int(preset.Width, preset.Height);
        }
    }
}
