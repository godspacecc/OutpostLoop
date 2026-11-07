#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace Game.Editor
{
    /// <summary>以 Client 目录为粒度的进程间锁；同线程可嵌套，其他线程或进程不能绕过。</summary>
    public static class WorkspaceBuildLock
    {
        private sealed class Owner
        {
            public FileStream Stream;
            public int Thread;
            public int References;
        }
        private sealed class Lease : IDisposable
        {
            private string key;
            public Lease(string value) { key = value; }
            public void Dispose()
            {
                lock (Owners)
                {
                    if (key == null) return;
                    var owner = Owners[key];
                    if (owner.Thread != Thread.CurrentThread.ManagedThreadId)
                        throw new InvalidOperationException("工作目录锁必须由持有线程释放。");
                    if (--owner.References == 0) { owner.Stream.Dispose(); Owners.Remove(key); }
                    key = null;
                }
            }
        }
        private static readonly Dictionary<string, Owner> Owners = new Dictionary<string, Owner>(StringComparer.OrdinalIgnoreCase);

        public static IDisposable Acquire(string clientDirectory)
        {
            string key = Path.GetFullPath(clientDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            BuildDataStore.NoLinks(key);
            if (!Directory.Exists(key)) throw new DirectoryNotFoundException("Unity 工作目录不存在。");
            lock (Owners)
            {
                int thread = Thread.CurrentThread.ManagedThreadId;
                if (Owners.TryGetValue(key, out var existing))
                {
                    if (existing.Thread != thread) throw new IOException("其他线程持有工作目录构建锁。");
                    existing.References++;
                    return new Lease(key);
                }
                string path = Path.Combine(key, ".outpostloop-build.lock");
                BuildDataStore.NoLinks(path);
                var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                try
                {
                    byte[] bytes = Encoding.UTF8.GetBytes("pid=" + Process.GetCurrentProcess().Id + "\nutc=" + DateTime.UtcNow.ToString("O"));
                    stream.SetLength(0);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                    Owners.Add(key, new Owner { Stream = stream, Thread = thread, References = 1 });
                }
                catch { stream.Dispose(); throw; }
                return new Lease(key);
            }
        }

        internal static bool IsHeld(string clientDirectory)
        {
            string key = Path.GetFullPath(clientDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            lock (Owners) return Owners.TryGetValue(key, out var value) && value.Thread == Thread.CurrentThread.ManagedThreadId;
        }
    }
}
#endif
