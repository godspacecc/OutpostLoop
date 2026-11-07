using System.Collections.Generic;
using System.Linq;

namespace Game
{
    /// <summary>
    /// 描述一个可选择的显示分辨率预设，Code 用于持久化，宽高用于实际调用 Unity 显示接口。
    /// </summary>
    public sealed class DisplayResolutionPreset
    {
        public DisplayResolutionPreset(string code, int width, int height)
        {
            Code = code;
            Width = width;
            Height = height;
        }

        public string Code { get; }
        public int Width { get; }
        public int Height { get; }
        public string Label => Code;
    }

    /// <summary>
    /// 集中提供项目允许的显示分辨率候选，并在读取旧设置或异常值时返回稳定默认项。
    /// </summary>
    public static class DisplayResolutionPresets
    {
        private static readonly List<DisplayResolutionPreset> Presets = new List<DisplayResolutionPreset>
        {
            new DisplayResolutionPreset("1280x720", 1280, 720),
            new DisplayResolutionPreset("1600x900", 1600, 900),
            new DisplayResolutionPreset("1920x1080", 1920, 1080)
        };

        public static IReadOnlyList<DisplayResolutionPreset> All => Presets;

        public static DisplayResolutionPreset Default => Presets[1];

        public static DisplayResolutionPreset FindOrDefault(string code)
        {
            return Presets.FirstOrDefault(preset => preset.Code == code) ?? Default;
        }
    }
}
