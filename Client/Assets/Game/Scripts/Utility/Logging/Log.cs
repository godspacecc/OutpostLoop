using System;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 统一项目日志入口，转发到 Unity Debug 并保留项目自写中文诊断上下文。
    /// </summary>
    public static class Log
    {
        private static Action<LogType, string> captureForTests;

        public static void Debug(object message)
        {
            if (TryCaptureForTests(LogType.Log, message))
            {
                return;
            }

            UnityEngine.Debug.Log(message);
        }

        public static void Warning(object message)
        {
            if (TryCaptureForTests(LogType.Warning, message))
            {
                return;
            }

            UnityEngine.Debug.LogWarning(message);
        }

        public static void Error(object message)
        {
            if (TryCaptureForTests(LogType.Error, message))
            {
                return;
            }

            UnityEngine.Debug.LogError(message);
        }

        public static void ErrorFormat(string format, params object[] args)
        {
            if (TryCaptureForTests(LogType.Error, string.Format(format, args)))
            {
                return;
            }

            UnityEngine.Debug.LogErrorFormat(format, args);
        }

        public static IDisposable CaptureForTests(Action<LogType, string> capture)
        {
            var previous = captureForTests;
            captureForTests = capture;
            return new Scope(() => captureForTests = previous);
        }

        private static bool TryCaptureForTests(LogType type, object message)
        {
            if (captureForTests == null)
            {
                return false;
            }

            captureForTests(type, message?.ToString() ?? string.Empty);
            return true;
        }

        private sealed class Scope : IDisposable
        {
            private Action dispose;

            public Scope(Action dispose)
            {
                this.dispose = dispose;
            }

            public void Dispose()
            {
                dispose?.Invoke();
                dispose = null;
            }
        }
    }
}
