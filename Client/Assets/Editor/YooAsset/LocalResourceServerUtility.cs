#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Game.Editor
{
    /// <summary>
    /// 支撑编辑器构建流程中的 LocalResourceServerUtility，记录或执行构建、资源和本地服务相关职责。
    /// </summary>
    public static class LocalResourceServerUtility
    {
        public const int Port = 8000;
        public static string BaseUrl => "http://127.0.0.1:8000";

        public static string ProcessStatePath
        {
            get
            {
                string projectPath = Directory.GetParent(Application.dataPath).FullName;
                return Path.Combine(projectPath, "Library", "OutpostLoopBuild", "LocalResourceServer.json");
            }
        }

        private static string ScriptPath
        {
            get
            {
                string projectPath = Directory.GetParent(Application.dataPath).FullName;
                return Path.GetFullPath(Path.Combine(projectPath, "Assets", "Editor", "YooAsset", "LocalServer", "start_yooasset_local_server.ps1"));
            }
        }

        private static string BatchPath
        {
            get
            {
                string projectPath = Directory.GetParent(Application.dataPath).FullName;
                return Path.GetFullPath(Path.Combine(projectPath, "Assets", "Editor", "YooAsset", "LocalServer", "start_yooasset_local_server.bat"));
            }
        }

        /// <summary>
        /// 启动本项目拥有的本地资源服并探测版本文件；启动失败会尝试停止本次创建的进程。
        /// </summary>
        public static LocalServerStartResult StartAndProbe(string root, string expectedVersion)
        {
            var result = new LocalServerStartResult();
            string normalizedRoot = Path.GetFullPath(root);
            string versionFile = Path.Combine(normalizedRoot, "OutpostLoop.version");

            if (!File.Exists(versionFile))
                return result.Fail($"资源版本文件不存在：{versionFile}");

            string version = File.ReadAllText(versionFile).Trim();
            if (!string.Equals(version, expectedVersion, StringComparison.Ordinal))
                return result.Fail($"资源版本不匹配：期望 {expectedVersion}，实际 {version}");

            StopOwnedServer();

            if (!CanStartOnPort(IsPortInUse(), out string portError))
                return result.Fail(portError);

            if (!File.Exists(BatchPath))
                return result.Fail($"资源服批处理文件不存在：{BatchPath}");
            if (!File.Exists(ScriptPath))
                return result.Fail($"资源服脚本不存在：{ScriptPath}");

            Process process = null;
            try
            {
                process = new Process { StartInfo = CreateServerStartInfo(normalizedRoot) };
                if (!process.Start())
                    return result.Fail("CMD 资源服进程启动失败。");

                var state = CaptureProcessState(process, normalizedRoot);
                if (state == null)
                {
                    TryTerminateStartedProcess(process);
                    return result.Fail("资源服已启动但无法记录进程所有权，已停止本次进程。");
                }
                SaveState(state);

                if (!ProbeVersion(expectedVersion, 10000))
                {
                    StopOwnedServer();
                    return result.Fail($"资源服探测超时或返回版本不匹配：{BaseUrl}/OutpostLoop.version");
                }

                return result.Ok(BaseUrl, normalizedRoot, expectedVersion);
            }
            catch (Exception e)
            {
                if (process != null)
                    TryTerminateStartedProcess(process);
                return result.Fail($"资源服启动异常：{e.Message}");
            }
        }

        public static void StopOwnedServer()
        {
            if (!File.Exists(ProcessStatePath))
                return;

            LocalServerProcessState state = null;
            try
            {
                state = JsonUtility.FromJson<LocalServerProcessState>(File.ReadAllText(ProcessStatePath, Encoding.UTF8));
            }
            catch
            {
                DeleteState();
                return;
            }

            try
            {
                var process = Process.GetProcessById(state.ProcessId);
                var snapshot = ProcessSnapshot.FromProcess(process);
                if (IsOwnedProcess(state, snapshot, BatchPath))
                {
                    using (var stop = Process.Start(CreateOwnedTreeStopStartInfo(state.ProcessId)))
                    {
                        stop.WaitForExit(5000);
                        if (stop.ExitCode != 0)
                            Debug.LogWarning($"[LocalResourceServer] taskkill 退出码异常：code={stop.ExitCode}，PID={state.ProcessId}");
                    }
                    Debug.Log($"[LocalResourceServer] 已停止本项目资源服进程：PID={state.ProcessId}");
                }
                else
                {
                    Debug.LogWarning("[LocalResourceServer] 已记录进程过期或不属于本项目，未停止任何进程。");
                }
            }
            catch (ArgumentException)
            {
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LocalResourceServer] 停止资源服失败：{e.Message}");
            }
            finally
            {
                DeleteState();
            }
        }

        internal static bool IsOwnedProcess(LocalServerProcessState state, ProcessSnapshot actual, string expectedScriptPath)
        {
            if (state == null || actual == null)
                return false;

            bool isCmd = string.Equals(actual.ProcessName, "cmd", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(actual.ProcessName, "cmd.exe", StringComparison.OrdinalIgnoreCase);
            if (!isCmd)
                return false;

            try
            {
                return state.ProcessId == actual.ProcessId &&
                       state.StartTimeUtcTicks == actual.StartTimeUtcTicks &&
                       string.Equals(state.ProcessName, actual.ProcessName, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(Path.GetFullPath(state.ScriptPath ?? string.Empty), Path.GetFullPath(expectedScriptPath), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        internal static bool CanStartOnPort(bool portInUse, out string error)
        {
            if (!portInUse)
            {
                error = null;
                return true;
            }

            error = $"端口 {Port} 已被未知进程占用；未结束该进程。";
            return false;
        }

        internal static ProcessStartInfo CreateServerStartInfo(string normalizedRoot)
        {
            return new ProcessStartInfo
            {
                FileName = BatchPath,
                Arguments = $"{Port} \"{normalizedRoot}\"",
                WorkingDirectory = Path.GetDirectoryName(BatchPath) ?? string.Empty,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal
            };
        }

        internal static ProcessStartInfo CreateOwnedTreeStopStartInfo(int processId)
        {
            return new ProcessStartInfo
            {
                FileName = "taskkill.exe",
                Arguments = $"/PID {processId} /T /F",
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        private static LocalServerProcessState CaptureProcessState(Process process, string root)
        {
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    process.Refresh();
                    if (process.HasExited)
                        return null;

                    return new LocalServerProcessState
                    {
                        ProcessId = process.Id,
                        StartTimeUtcTicks = process.StartTime.ToUniversalTime().Ticks,
                        ProcessName = process.ProcessName,
                        ScriptPath = BatchPath,
                        Root = root
                    };
                }
                catch (InvalidOperationException)
                {
                    Thread.Sleep(100);
                }
            }

            return null;
        }

        private static bool ProbeVersion(string expectedVersion, int timeoutMs)
        {
            string url = $"{BaseUrl}/OutpostLoop.version";
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                try
                {
                    using (var client = new WebClient())
                    {
                        string value = client.DownloadString(url).Trim();
                        if (string.Equals(value, expectedVersion, StringComparison.Ordinal))
                            return true;
                    }
                }
                catch
                {
                    Thread.Sleep(250);
                }
            }

            return false;
        }

        private static bool IsPortInUse()
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var ar = client.BeginConnect(IPAddress.Loopback, Port, null, null);
                    bool connected = ar.AsyncWaitHandle.WaitOne(250);
                    if (!connected)
                        return false;
                    client.EndConnect(ar);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void TryTerminateStartedProcess(Process process)
        {
            try
            {
                if (process != null && !process.HasExited)
                {
                    using (var stop = Process.Start(CreateOwnedTreeStopStartInfo(process.Id)))
                        stop.WaitForExit(5000);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LocalResourceServer] 无法停止本次启动失败的进程: {e.Message}");
            }
        }

        private static void SaveState(LocalServerProcessState state)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ProcessStatePath));
            File.WriteAllText(ProcessStatePath, JsonUtility.ToJson(state, true), new UTF8Encoding(false));
        }

        private static void DeleteState()
        {
            try
            {
                if (File.Exists(ProcessStatePath))
                    File.Delete(ProcessStatePath);
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// 支撑编辑器构建流程中的 LocalServerProcessState，记录或执行构建、资源和本地服务相关职责。
    /// </summary>
    [Serializable]
    public sealed class LocalServerProcessState
    {
        public int ProcessId;
        public long StartTimeUtcTicks;
        public string ProcessName;
        public string ScriptPath;
        public string Root;
    }

    /// <summary>
    /// 支撑编辑器构建流程中的 ProcessSnapshot，记录或执行构建、资源和本地服务相关职责。
    /// </summary>
    public sealed class ProcessSnapshot
    {
        public int ProcessId;
        public long StartTimeUtcTicks;
        public string ProcessName;

        public static ProcessSnapshot FromProcess(Process process)
        {
            return new ProcessSnapshot
            {
                ProcessId = process.Id,
                StartTimeUtcTicks = process.StartTime.ToUniversalTime().Ticks,
                ProcessName = process.ProcessName
            };
        }
    }

    /// <summary>
    /// 支撑编辑器构建流程中的 LocalServerStartResult，记录或执行构建、资源和本地服务相关职责。
    /// </summary>
    public sealed class LocalServerStartResult
    {
        public bool Success;
        public string BaseUrl;
        public string Root;
        public string Version;
        public string Error;

        public LocalServerStartResult Ok(string baseUrl, string root, string version)
        {
            Success = true;
            BaseUrl = baseUrl;
            Root = root;
            Version = version;
            Error = null;
            return this;
        }

        public LocalServerStartResult Fail(string error)
        {
            Success = false;
            Error = error;
            return this;
        }
    }
}
#endif
