namespace Boot
{
    /// <summary>
    /// 标记本次 Boot 资源启动最终使用的来源，用于 Game 侧选择更新检查策略。
    /// </summary>
    public enum BootResourceSource
    {
        EditorSimulate,
        HostLatest,
        HostCached,
        BuiltinFallback
    }

    /// <summary>
    /// 保存一次进程内启动资源快照，包含来源、服务器、当前活动版本和已探测 Host 版本。
    /// </summary>
    public readonly struct BootResourceStartupInfo
    {
        public BootResourceStartupInfo(
            BootResourceSource source,
            string resourceServer,
            string activeVersion,
            string hostVersion)
        {
            Source = source;
            ResourceServer = resourceServer ?? string.Empty;
            ActiveVersion = activeVersion ?? string.Empty;
            HostVersion = hostVersion ?? string.Empty;
        }

        public BootResourceSource Source { get; }
        public string ResourceServer { get; }
        public string ActiveVersion { get; }
        public string HostVersion { get; }
    }

    /// <summary>
    /// 发布并读取本进程内最近一次成功的 Boot 资源启动快照，不做持久化。
    /// </summary>
    public static class BootResourceStartupState
    {
        private static BootResourceStartupInfo current;
        private static bool hasCurrent;

        public static bool TryGet(out BootResourceStartupInfo info)
        {
            info = current;
            return hasCurrent;
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void Reset()
        {
            current = default;
            hasCurrent = false;
        }

        internal static void Publish(BootResourceStartupInfo info)
        {
            current = info;
            hasCurrent = true;
        }
    }
}
