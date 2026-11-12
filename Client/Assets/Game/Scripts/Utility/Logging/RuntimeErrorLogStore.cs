using System;
using System.IO;
using System.Text;

namespace Game
{
    /// <summary>
    /// 将 Game 运行期的错误即时写入有限大小的本地文件；不采集日志，也不负责 Boot 阶段或原生崩溃。
    /// </summary>
    public sealed class RuntimeErrorLogStore
    {
        public const string CurrentFileName = "runtime-error.log";
        public const string PreviousFileName = "runtime-error.previous.log";
        public const int DefaultMaxBytes = 1024 * 1024;

        private readonly object writeLock = new object();
        private readonly string currentPath;
        private readonly string previousPath;
        /// <summary>单个日志文件的最大字节数；轮转后最多保留当前与上一份。</summary>
        private readonly int maxBytes;

        public RuntimeErrorLogStore(string directory, int maxBytes = DefaultMaxBytes)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("日志目录不能为空", nameof(directory));
            if (maxBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxBytes));

            Directory.CreateDirectory(directory);
            currentPath = Path.Combine(directory, CurrentFileName);
            previousPath = Path.Combine(directory, PreviousFileName);
            this.maxBytes = maxBytes;
        }

        /// <summary>在日志回调线程同步写入错误；写入失败返回 false，不抛异常或再产生日志。</summary>
        public bool Append(RuntimeLogEntry entry)
        {
            try
            {
                var text = $"[{entry.Timestamp:O}] [{entry.Type}] {entry.Message}\n{entry.StackTrace}\n";
                var bytes = Encoding.UTF8.GetBytes(text);
                if (bytes.Length > maxBytes)
                {
                    // 异常堆栈可能极长，预留截断标记空间并避免单条记录突破文件上限。
                    const string marker = "\n[truncated]\n";
                    var markerBytes = Encoding.UTF8.GetBytes(marker);
                    var limited = new byte[maxBytes];
                    var markerLength = Math.Min(markerBytes.Length, maxBytes);
                    int written = 0;
                    if (maxBytes > markerLength) Encoding.UTF8.GetEncoder().Convert(text.ToCharArray(), 0, text.Length,
                        limited, 0, maxBytes - markerLength, true, out _, out written, out _);
                    Buffer.BlockCopy(markerBytes, 0, limited, written, markerLength);
                    Array.Resize(ref limited, written + markerLength);
                    bytes = limited;
                }

                lock (writeLock)
                {
                    if (File.Exists(currentPath) && new FileInfo(currentPath).Length + bytes.Length > maxBytes)
                    {
                        if (File.Exists(previousPath)) File.Delete(previousPath);
                        File.Move(currentPath, previousPath);
                    }

                    using (var stream = new FileStream(currentPath, FileMode.Append, FileAccess.Write, FileShare.Read))
                        stream.Write(bytes, 0, bytes.Length);
                }

                return true;
            }
            catch
            {
                // 诊断落盘不得影响游戏，也不能在日志回调中递归记录自身的异常。
                return false;
            }
        }
    }
}
