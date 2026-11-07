using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace Boot
{
    /// <summary>仅启动已验证的当前用户安装器；成功后由 Launcher 退出，不杀进程、不宣称安装完成。</summary>
    public static class WindowsInstaller
    {
        public static bool Supported => Application.platform == RuntimePlatform.WindowsPlayer && IntPtr.Size == 8;
        public static string UpdateDirectory => Path.Combine(Application.persistentDataPath, "Updates", "Windows");

        public static bool TryStart(string path, out string failure)
        {
            failure = null;
            if (!Supported) { failure = "当前平台不支持安装。"; return false; }
            return TryStartNative(path, out failure);
        }

        /// <summary>使用 IL2CPP 支持的 Win32 接口交接；失败保留游戏，成功只关闭本进程持有的句柄。</summary>
        private static bool TryStartNative(string path, out string failure)
        {
            failure = null;
            if (Application.platform != RuntimePlatform.WindowsPlayer && Application.platform != RuntimePlatform.WindowsEditor)
            { failure = "当前平台不支持安装。"; return false; }
            try
            {
                string executable = Path.GetFullPath(path);
                if (!File.Exists(executable)) { failure = "安装文件不存在，请重新下载。"; return false; }
                var command = new StringBuilder("\"" + executable + "\" " + GetHandoffArguments());
                var startup = new StartupInfo { size = (uint)Marshal.SizeOf(typeof(StartupInfo)) };
                ProcessInformation process;
                // 显式指定可执行文件，避免带空格路径歧义；不继承句柄、不经过命令行解释器。
                if (!CreateProcess(executable, command, IntPtr.Zero, IntPtr.Zero, false, 0x08000000,
                    IntPtr.Zero, Path.GetDirectoryName(executable), ref startup, out process))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                try { return true; }
                finally
                {
                    // 关闭句柄不会结束安装器；安装器自行等待游戏退出后继续安装。
                    if (process.thread != IntPtr.Zero) CloseHandle(process.thread);
                    if (process.process != IntPtr.Zero) CloseHandle(process.process);
                }
            }
            catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is IOException ||
                e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException ||
                e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                failure = "请检查安装文件和系统权限。错误：" + e.GetType().Name;
                if (e is Win32Exception native) failure += " (" + native.NativeErrorCode + ")";
                Debug.LogWarning("[WindowsUpdate] 安装器启动失败：" + e);
                return false;
            }
        }

        /// <summary>PID 和原始 FILETIME 共同标识当前游戏进程，防止 PID 重用造成等待错误。</summary>
        private static string GetHandoffArguments()
        {
            FileTime created, exited, kernel, user;
            // 当前进程伪句柄不属于调用者，不能 CloseHandle。
            if (!GetProcessTimes(GetCurrentProcess(), out created, out exited, out kernel, out user))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "/WAITPID={0} /WAITLOW={1} /WAITHIGH={2}", GetCurrentProcessId(), created.low, created.high);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileTime { public uint low, high; }

        // 与 Win32 STARTUPINFOW 对齐；指针使用 IntPtr，保持 x64 原生布局。
        [StructLayout(LayoutKind.Sequential)]
        private struct StartupInfo
        {
            public uint size;
            public IntPtr reserved, desktop, title;
            public uint x, y, width, height, columns, rows, fill, flags;
            public ushort show, reservedSize;
            public IntPtr reservedBytes, input, output, error;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation
        {
            public IntPtr process, thread;
            public uint processId, threadId;
        }

        [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateProcess(string application, StringBuilder command, IntPtr processAttributes,
            IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, IntPtr environment,
            string directory, ref StartupInfo startup, out ProcessInformation process);

        [DllImport("kernel32.dll", ExactSpelling = true)] private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", ExactSpelling = true)] private static extern uint GetCurrentProcessId();
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessTimes(IntPtr process, out FileTime created, out FileTime exited,
            out FileTime kernel, out FileTime user);
        [DllImport("kernel32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);

        /// <summary>宿主版本变化时，先使旧资源缓存及成功版本标记失效；不删除 LocalLow 存档。</summary>
        public static void InvalidatePreviousHostCache()
        {
            if (!Supported) return;
            var installed = WindowsUpdateManifest.ReadInstalled();
            if (installed == null) return;
            const string key = "OutpostLoop.WindowsHostRelease";
            if (PlayerPrefs.GetInt(key, -1) == installed.releaseNumber) return;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "yoo"));
            // 路径来自当前 Player 的固定缓存目录，清理失败则抛出并阻止旧缓存启动。
            if (Directory.Exists(root)) Directory.Delete(root, true);
            PlayerPrefs.DeleteKey("OutpostLoop.LastSuccessfulHostVersion");
            PlayerPrefs.SetInt(key, installed.releaseNumber);
            PlayerPrefs.Save();
        }
    }
}
