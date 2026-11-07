#if UNITY_EDITOR
using System;
using System.Globalization;

namespace Game.Editor
{
    /// <summary>构建版本、重载门禁及失败文本规则；不切平台、不触发构建、不改变产物。</summary>
    public static class BuildWorkflowUtility
    {
        public static string CreateResourceVersion(DateTime timestamp) =>
            timestamp.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

        public static bool CanResume(bool reloaded, bool compiling, bool updating) =>
            reloaded && !compiling && !updating;

        public static string FormatFailure(BuildStageTiming timing, string error, string retainedPath = null)
        {
            string retained = string.IsNullOrEmpty(retainedPath) ? string.Empty : $"\n已保留产物: {retainedPath}";
            return $"入口: {timing.WorkflowName}\n失败阶段: {timing.CurrentStage ?? "（无）"}\n原因: {error}" +
                retained + "\n\n" + timing.BuildSummary();
        }
    }
}
#endif
