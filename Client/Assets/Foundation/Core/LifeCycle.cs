using System;
using System.Collections.Generic;
using System.Reflection;

namespace Game
{
    public static class LifeCycle
    {
        private static readonly List<Action> Updates = new List<Action>();
        private static readonly List<Action> FixedUpdates = new List<Action>();
        private static readonly List<Action> LateUpdates = new List<Action>();
        private static readonly List<Action> Quits = new List<Action>();
        private static readonly List<Action<bool>> ApplicationPauses = new List<Action<bool>>();
        private static readonly List<Action<bool>> ApplicationFocuses = new List<Action<bool>>();

        private static Dictionary<(object owner, MethodInfo method), Action> _frameEndActions =
            new Dictionary<(object owner, MethodInfo method), Action>();

        private static Dictionary<(object owner, MethodInfo method), Action> _processing =
            new Dictionary<(object owner, MethodInfo method), Action>();

        public static void RegisterUpdate(Action action)
        {
            if (action != null && !Updates.Contains(action))
            {
                Updates.Add(action);
            }
        }

        public static void UnregisterUpdate(Action action)
        {
            if (action != null)
            {
                Updates.Remove(action);
            }
        }

        public static void RegisterFixedUpdate(Action action)
        {
            if (action != null && !FixedUpdates.Contains(action))
            {
                FixedUpdates.Add(action);
            }
        }

        public static void UnregisterFixedUpdate(Action action)
        {
            if (action != null)
            {
                FixedUpdates.Remove(action);
            }
        }

        public static void RegisterLateUpdate(Action action)
        {
            if (action != null && !LateUpdates.Contains(action))
            {
                LateUpdates.Add(action);
            }
        }

        public static void UnregisterLateUpdate(Action action)
        {
            if (action != null)
            {
                LateUpdates.Remove(action);
            }
        }

        public static void RegisterQuit(Action action)
        {
            if (action != null && !Quits.Contains(action))
            {
                Quits.Add(action);
            }
        }

        public static void UnregisterQuit(Action action)
        {
            if (action != null)
            {
                Quits.Remove(action);
            }
        }

        public static void RegisterApplicationPause(Action<bool> action)
        {
            if (action != null && !ApplicationPauses.Contains(action))
            {
                ApplicationPauses.Add(action);
            }
        }

        public static void UnregisterApplicationPause(Action<bool> action)
        {
            if (action != null)
            {
                ApplicationPauses.Remove(action);
            }
        }

        public static void RegisterApplicationFocus(Action<bool> action)
        {
            if (action != null && !ApplicationFocuses.Contains(action))
            {
                ApplicationFocuses.Add(action);
            }
        }

        public static void UnregisterApplicationFocus(Action<bool> action)
        {
            if (action != null)
            {
                ApplicationFocuses.Remove(action);
            }
        }

        public static void ScheduleFrameEnd(object owner, Action action)
        {
            if (owner == null || action == null)
            {
                return;
            }

            _frameEndActions[(owner, action.Method)] = action;
        }

        public static void Update()
        {
            InvokeCallbacks(Updates, "Update");
        }

        public static void FixedUpdate()
        {
            InvokeCallbacks(FixedUpdates, "FixedUpdate");
        }

        public static void LateUpdate()
        {
            InvokeCallbacks(LateUpdates, "LateUpdate");
            FlushFrameEnd();
        }

        public static void Quit()
        {
            InvokeCallbacks(Quits, "Quit");
            Updates.Clear();
            FixedUpdates.Clear();
            LateUpdates.Clear();
            Quits.Clear();
            ApplicationPauses.Clear();
            ApplicationFocuses.Clear();
            _frameEndActions.Clear();
            _processing.Clear();
        }

        public static void ApplicationPause(bool paused)
        {
            var snapshot = ApplicationPauses.ToArray();
            for (var i = 0; i < snapshot.Length; i++)
            {
                try
                {
                    snapshot[i]?.Invoke(paused);
                }
                catch (Exception e)
                {
                    Log.Error($"[LifeCycle] 生命周期阶段 ApplicationPause 回调异常：{e}");
                }
            }
        }

        public static void ApplicationFocus(bool focused)
        {
            var snapshot = ApplicationFocuses.ToArray();
            for (var i = 0; i < snapshot.Length; i++)
            {
                try
                {
                    snapshot[i]?.Invoke(focused);
                }
                catch (Exception e)
                {
                    Log.Error($"[LifeCycle] 生命周期阶段 ApplicationFocus 回调异常：{e}");
                }
            }
        }

        public static void ClearForTests()
        {
            Updates.Clear();
            FixedUpdates.Clear();
            LateUpdates.Clear();
            Quits.Clear();
            ApplicationPauses.Clear();
            ApplicationFocuses.Clear();
            _frameEndActions.Clear();
            _processing.Clear();
            TimerMgr.Instance.ClearForTests();
        }

        private static void FlushFrameEnd()
        {
            if (_frameEndActions.Count == 0)
            {
                return;
            }

            var ready = _frameEndActions;
            _frameEndActions = _processing;
            _processing = ready;

            foreach (var kv in _processing)
            {
                try
                {
                    kv.Value.Invoke();
                }
                catch (Exception e)
                {
                    Log.Error($"[LifeCycle] 帧末回调异常：{e}");
                }
            }

            _processing.Clear();
        }

        private static void InvokeCallbacks(List<Action> callbacks, string phase)
        {
            var snapshot = callbacks.ToArray();
            for (var i = 0; i < snapshot.Length; i++)
            {
                try
                {
                    snapshot[i]?.Invoke();
                }
                catch (Exception e)
                {
                    Log.Error($"[LifeCycle] 生命周期阶段 {phase} 回调异常：{e}");
                }
            }
        }
    }
}
