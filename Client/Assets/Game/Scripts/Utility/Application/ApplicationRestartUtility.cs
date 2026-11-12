using System;
using System.IO;
using QFramework;
using UnityEngine;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace Game
{
    /// <summary>
    /// 抽象新进程启动能力，使重启工具可在测试中替换真实 Process.Start。
    /// </summary>
    public interface IProcessLauncher
    {
        bool TryLaunch(string fileName, string workingDirectory, out string error);
    }

    /// <summary>
    /// 抽象当前应用退出动作，区分启动新进程和关闭当前 Unity Player 的责任。
    /// </summary>
    public interface IApplicationQuitter
    {
        void Quit();
    }

    /// <summary>
    /// 定义应用重启入口，调用方只关心是否已成功发起重启及失败原因。
    /// </summary>
    public interface IApplicationRestartUtility : IUtility
    {
        bool TryRestart(out string error);
    }

    /// <summary>把 Android 冷启动交给独立原生进程；成功仅表示系统已接受中转 Activity。</summary>
    public interface IAndroidRestartLauncher
    {
        bool TryRestart(out string error);
    }

    /// <summary>通过 JNI 启动 APK 内的重启中转 Activity，不使用桌面进程 API。</summary>
    public sealed class AndroidRestartLauncher : IAndroidRestartLauncher
    {
        public bool TryRestart(out string error)
        {
            error = string.Empty;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                if (activity == null) throw new InvalidOperationException("Android Activity 不可用");
                using var manager = activity.Call<AndroidJavaObject>("getPackageManager");
                using var launch = manager.Call<AndroidJavaObject>("getLaunchIntentForPackage", activity.Call<string>("getPackageName"));
                if (launch == null) throw new InvalidOperationException("Android 启动入口不可用");

                // 先解析中转类，旧 APK 缺少插件时绝不提前关闭当前游戏。
                using var restartClass = new AndroidJavaClass("com.godspace.outpostloop.restart.ResourceRestartActivity");
                using var intent = new AndroidJavaObject("android.content.Intent", activity, restartClass);
                using var process = new AndroidJavaClass("android.os.Process");
                using var pidExtra = intent.Call<AndroidJavaObject>("putExtra", "main_pid", process.CallStatic<int>("myPid"));
                using var textExtra = intent.Call<AndroidJavaObject>("putExtra", "failure_text",
                    Loc.T("ui.start.update.restart_manual"));
                using var flags = intent.Call<AndroidJavaObject>("addFlags", 0x10000000); // FLAG_ACTIVITY_NEW_TASK
                activity.Call("startActivity", intent);
                return true;
            }
            catch (Exception exception)
            {
                error = $"Android 重启入口失败：{exception.Message}";
                return false;
            }
#else
            error = "当前平台没有 Android 重启入口";
            return false;
#endif
        }
    }

    /// <summary>
    /// 使用系统进程 API 启动目标可执行文件，不负责校验路径是否属于当前 Player。
    /// </summary>
    public sealed class ProcessLauncher : IProcessLauncher
    {
        public bool TryLaunch(string fileName, string workingDirectory, out string error)
        {
            error = string.Empty;
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = true
                };
                return Process.Start(startInfo) != null;
            }
            catch (Exception e)
            {
                error = $"启动进程异常：{e.Message}";
                return false;
            }
        }
    }

    /// <summary>
    /// 封装 Unity 应用退出调用，让重启流程在启动新进程后再关闭当前 Player。
    /// </summary>
    public sealed class UnityApplicationQuitter : IApplicationQuitter
    {
        public void Quit()
        {
            Application.Quit();
        }
    }

    /// <summary>
    /// 按平台请求冷启动；Android 由独立原生 Activity 交接，桌面先启动再退出。
    /// </summary>
    public sealed class ApplicationRestartUtility : IApplicationRestartUtility
    {
        private readonly IProcessLauncher launcher;
        private readonly IApplicationQuitter quitter;
        private readonly Func<bool> isEditor;
        private readonly Func<string> executablePathProvider;
        private readonly Func<bool> isAndroid;
        private readonly IAndroidRestartLauncher androidLauncher;
        private bool requested;

        public ApplicationRestartUtility()
            : this(new ProcessLauncher(), new UnityApplicationQuitter(), () => Application.isEditor, GetCurrentExecutablePath)
        {
        }

        public ApplicationRestartUtility(IProcessLauncher launcher, IApplicationQuitter quitter, Func<bool> isEditor,
            Func<string> executablePathProvider, Func<bool> isAndroid = null, IAndroidRestartLauncher androidLauncher = null)
        {
            this.launcher = launcher ?? new ProcessLauncher();
            this.quitter = quitter ?? new UnityApplicationQuitter();
            this.isEditor = isEditor ?? (() => Application.isEditor);
            this.executablePathProvider = executablePathProvider ?? GetCurrentExecutablePath;
            this.isAndroid = isAndroid ?? (() => Application.platform == RuntimePlatform.Android);
            this.androidLauncher = androidLauncher ?? new AndroidRestartLauncher();
        }

        public bool TryRestart(out string error)
        {
            error = string.Empty;
            if (requested) return true;
            if (isEditor())
            {
                error = "编辑器内不支持重启应用";
                return false;
            }

            try
            {
                if (isAndroid())
                {
                    requested = androidLauncher.TryRestart(out error);
                    return requested;
                }

                var exe = executablePathProvider() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(exe))
                {
                    error = "可执行文件路径为空";
                    return false;
                }

                var workingDirectory = Path.GetDirectoryName(exe) ?? string.Empty;
                if (!launcher.TryLaunch(exe, workingDirectory, out error))
                {
                    error = string.IsNullOrWhiteSpace(error) ? "启动重启进程失败" : error;
                    return false;
                }

                requested = true;
                quitter.Quit();
                return true;
            }
            catch (Exception exception)
            {
                requested = false;
                error = $"请求重启失败：{exception.Message}";
                return false;
            }
        }

        private static string GetCurrentExecutablePath()
        {
            return Process.GetCurrentProcess().MainModule?.FileName;
        }
    }
}
