using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 将运行时日志写入诊断文件，负责目录创建和文本落盘，不采集日志。
    /// </summary>
    public static class RuntimeLogExportUtility
    {
        public static string Export(IEnumerable<RuntimeLogEntry> entries, string appState, string directory = null, DateTime? now = null, string mapId = "")
        {
            directory ??= RuntimeLogPaths.Directory;
            Directory.CreateDirectory(directory);
            var timestamp = (now ?? DateTime.Now).ToString("yyyyMMdd-HHmmss");
            var path = Path.Combine(directory, $"RuntimeLog-{timestamp}.txt");
            File.WriteAllText(path, RuntimeLogTextFormatter.Format(entries, appState, mapId));
            return path;
        }
    }
}
