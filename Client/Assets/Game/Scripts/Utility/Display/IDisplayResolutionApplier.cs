using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 抽象显示设置应用边界，生产实现调用 Unity Screen.SetResolution，测试可替换为记录器。
    /// </summary>
    public interface IDisplayResolutionApplier : IUtility
    {
        void Apply(DisplayResolutionPreset preset, FullScreenMode windowMode);
    }
}
