using Cysharp.Threading.Tasks;
using QFramework;

namespace Game
{
    /// <summary>
    /// 表示纯远端版本探测结果，成功时只携带版本，失败时携带诊断文本。
    /// </summary>
    public readonly struct RemoteResourceVersionProbeResult
    {
        private RemoteResourceVersionProbeResult(
            bool success,
            string version,
            string error)
        {
            Success = success;
            Version = version ?? string.Empty;
            Error = error ?? string.Empty;
        }

        public bool Success { get; }
        public string Version { get; }
        public string Error { get; }

        public static RemoteResourceVersionProbeResult Ok(string version)
        {
            return new RemoteResourceVersionProbeResult(true, version, string.Empty);
        }

        public static RemoteResourceVersionProbeResult Fail(string error)
        {
            return new RemoteResourceVersionProbeResult(false, string.Empty, error);
        }
    }

    /// <summary>
    /// 定义内置资源回退模式下的远端版本文件探测能力。
    /// </summary>
    public interface IRemoteResourceVersionProbe : IUtility
    {
        UniTask<RemoteResourceVersionProbeResult> ProbeAsync(
            string resourceServer,
            int timeoutSeconds);
    }
}
