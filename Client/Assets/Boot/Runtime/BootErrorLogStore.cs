using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Boot
{
    /// <summary>
    /// 在热更新代码加载前保存最近一次 Boot 失败信息；只负责本地文件持久化，不负责日志采集或上传。
    /// </summary>
    public static class BootErrorLogStore
    {
        public const string FileName = "boot-error.log";
        private const string DirectoryName = "Logs";

        /// <summary>
        /// 将最近一次启动失败覆盖写入应用持久化目录；失败只通过返回值报告，不能遮蔽原启动错误。
        /// </summary>
        public static bool TryWrite(BootFailure failure, out string logPath, out string diagnostic)
        {
            return TryWrite(BootLogPaths.Root, failure, out logPath, out diagnostic);
        }

        /// <summary>
        /// 写入指定持久化根目录，供不同平台和自动化验证复用；同一路径始终只保留最近一次失败。
        /// </summary>
        public static bool TryWrite(
            string persistentRoot,
            BootFailure failure,
            out string logPath,
            out string diagnostic)
        {
            logPath = GetLogPath(persistentRoot);
            diagnostic = null;
            if (failure == null)
            {
                diagnostic = "failure-null";
                return false;
            }

            if (string.IsNullOrWhiteSpace(logPath))
            {
                diagnostic = "persistence-unavailable";
                return false;
            }

            string directory = Path.GetDirectoryName(logPath);
            string tempPath = logPath + ".tmp";
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(tempPath, BuildContent(failure), new UTF8Encoding(false));
                if (File.Exists(logPath))
                {
                    File.Replace(tempPath, logPath, null, true);
                }
                else
                {
                    File.Move(tempPath, logPath);
                }

                return true;
            }
            catch (Exception exception)
            {
                diagnostic = exception.GetType().Name;
                TryDeleteTemporaryFile(tempPath);
                return false;
            }
        }

        private static string GetLogPath(string persistentRoot)
        {
            return string.IsNullOrWhiteSpace(persistentRoot)
                ? null
                : Path.Combine(persistentRoot, DirectoryName, FileName);
        }

        private static string BuildContent(BootFailure failure)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"Timestamp: {DateTimeOffset.Now:O}");
            builder.AppendLine($"ApplicationVersion: {Application.version}");
            builder.AppendLine($"UnityVersion: {Application.unityVersion}");
            builder.AppendLine($"Platform: {Application.platform}");
            builder.AppendLine();
            builder.Append(failure.DiagnosticDetails);
            return builder.ToString();
        }

        private static void TryDeleteTemporaryFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // 清理失败不再写日志，避免在原启动异常之外制造递归错误。
            }
        }
    }
}
