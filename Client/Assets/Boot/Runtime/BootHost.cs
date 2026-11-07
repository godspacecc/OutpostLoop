using System.Collections.Generic;
namespace Boot
{
    /// <summary>Game 可使用的宿主启动端口；隐藏 Launcher 和资源状态存储的具体实现。</summary>
    public static class BootHost
    {
        public static bool ReportProgress(string message) => Launcher.TryReportMainProgress(message);
        public static bool ReportReady() => Launcher.TryReportMainReady();
        public static bool ReportFailure(IReadOnlyDictionary<string, string> details) => Launcher.TryReportMainFailure(details);
        public static bool TryGetResourceInfo(out BootResourceStartupInfo info) => BootResourceStartupState.TryGet(out info);
        public static bool SupportsApplicationUpdate => AndroidApkInstaller.Supported;
        public static int InstalledVersionCode => AndroidApkInstaller.CurrentVersionCode;
    }
}
