using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 把运行时日志条目格式化为可读文本，保留时间、级别、正文和堆栈。
    /// </summary>
    public static class RuntimeLogTextFormatter
    {
        public static string Format(IEnumerable<RuntimeLogEntry> entries, string appState, string mapId = "")
        {
            var builder = new StringBuilder();
            builder.AppendLine($"Application.version: {Application.version}");
            builder.AppendLine($"Application.unityVersion: {Application.unityVersion}");
            builder.AppendLine($"AppState: {appState}");
            builder.AppendLine($"MapId: {mapId ?? string.Empty}");
            builder.AppendLine();

            foreach (var entry in entries)
            {
                builder.AppendLine($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] {entry.Type}: {entry.Message}");
                if (!string.IsNullOrWhiteSpace(entry.StackTrace))
                {
                    builder.AppendLine(entry.StackTrace);
                }
            }

            return builder.ToString();
        }
    }
}
