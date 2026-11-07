using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Boot
{
    /// <summary>
    /// 保存最近一次 Boot 会话的阶段和 Unity 异常；由 Launcher 持有并释放订阅。
    /// 不负责资源状态、超时判断、Game 运行日志或原生崩溃采集，落盘失败不得阻断启动。
    /// </summary>
    public sealed class BootStartupLogStore : IDisposable
    {
        public const string FileName = "boot-startup.log";
        /// <summary>单次启动日志的字节上限；错误最多占用四分之三，剩余容量用于阶段标记。</summary>
        public const int DefaultMaxBytes = 256 * 1024;
        private static readonly Encoding Utf8 = new UTF8Encoding(false);
        private readonly object writeLock = new object();
        private readonly string path;
        private readonly int maxBytes;
        private readonly int errorBudget;
        private int writtenBytes;
        private int errorBytes;
        private bool active;

        /// <summary>创建新会话并覆盖旧文件；应用信息须由 Unity 主线程传入，回调不读取 Unity 状态。</summary>
        public BootStartupLogStore(string persistentRoot, string applicationVersion, string platform,
            int maxBytes = DefaultMaxBytes)
        {
            this.maxBytes = Math.Max(256, maxBytes);
            errorBudget = this.maxBytes * 3 / 4;
            try
            {
                path = Path.Combine(persistentRoot, "Logs", FileName);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, string.Empty, Utf8);
                active = true;
                RecordStage($"Session:Started; ApplicationVersion={applicationVersion}; Platform={platform}");
                Application.logMessageReceivedThreaded += OnLogReceived;
            }
            catch
            {
                active = false;
                // 诊断不可用时安静降级，避免递归报错和改变启动结果。
            }
        }

        /// <summary>在真实状态转换处立即落盘；不依赖下一帧更新，满额或已释放时忽略。</summary>
        public void RecordStage(string stage) => Append("Stage", stage, false);

        private void OnLogReceived(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert || type == LogType.Warning)
                Append(type.ToString(), message + "\n" + stack, true);
        }

        private void Append(string kind, string content, bool isError)
        {
            lock (writeLock)
            {
                if (!active) return;
                int available = maxBytes - writtenBytes;
                if (isError) available = Math.Min(available, errorBudget - errorBytes);
                if (available < 64) return;
                try
                {
                    string text = $"[{DateTimeOffset.Now:O}] [{kind}] {content}\n";
                    byte[] bytes = Utf8.GetBytes(text);
                    if (bytes.Length > available)
                    {
                        byte[] marker = Utf8.GetBytes("\n[truncated]\n");
                        int prefixBytes = available - marker.Length;
                        // 截断点避开 UTF-8 多字节字符的中间，手机读取时不产生乱码。
                        while (prefixBytes > 0 && (bytes[prefixBytes] & 0xC0) == 0x80) prefixBytes--;
                        var truncated = new byte[prefixBytes + marker.Length];
                        Buffer.BlockCopy(bytes, 0, truncated, 0, prefixBytes);
                        Buffer.BlockCopy(marker, 0, truncated, prefixBytes, marker.Length);
                        bytes = truncated;
                    }
                    using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                        stream.Write(bytes, 0, bytes.Length);
                    writtenBytes += bytes.Length;
                    if (isError) errorBytes += bytes.Length;
                }
                catch
                {
                    // 不从 Unity 日志回调中报告写入异常，避免再次触发自身。
                }
            }
        }

        /// <summary>主场景就绪或 Launcher 销毁时结束会话；允许重复释放，迟到回调不再写入。</summary>
        public void Dispose()
        {
            lock (writeLock) active = false;
            Application.logMessageReceivedThreaded -= OnLogReceived;
        }
    }
}
