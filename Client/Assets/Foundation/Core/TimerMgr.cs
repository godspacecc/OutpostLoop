using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 帧驱动计时器管理器，支持回调移除和完成队列；单个回调异常不会阻塞其它计时器。
    /// </summary>
    public sealed class TimerMgr
    {
        private static readonly TimerMgr SharedInstance = new TimerMgr();
        private readonly List<int> _completedTimers = new List<int>();
        private readonly Dictionary<int, TimerEntry> _timers = new Dictionary<int, TimerEntry>();
        private int _nextId;
        private bool _registered;

        public static TimerMgr Instance => SharedInstance;

        private TimerMgr()
        {
        }

        /// <summary>添加帧驱动计时器；默认随游戏暂停，UI 可显式选择非缩放时间。</summary>
        /// <param name="useUnscaledTime">为 true 时使用真实帧间隔，暂停和时间缩放不影响计时。</param>
        public int Add(Func<bool> callback, float interval = 0f, float delay = 0f, bool useUnscaledTime = false)
        {
            if (callback == null)
            {
                Log.Error("[TimerMgr] 添加计时器失败：回调为空。");
                return 0;
            }

            EnsureRegistered();

            var id = NextId();
            _timers[id] = new TimerEntry
            {
                Callback = callback,
                Interval = Mathf.Max(0f, interval),
                Remaining = Mathf.Max(0f, delay),
                UseUnscaledTime = useUnscaledTime
            };

            return id;
        }

        public void Delete(int id)
        {
            _timers.Remove(id);
            UnregisterWhenIdle();
        }

        public void TickForTests(float deltaTime)
        {
            Tick(deltaTime, deltaTime);
        }

        /// <summary>独立推进游戏时间和真实时间，用于验证暂停或时间缩放下的计时行为。</summary>
        public void TickForTests(float deltaTime, float unscaledDeltaTime)
        {
            Tick(deltaTime, unscaledDeltaTime);
        }

        public void ClearForTests()
        {
            _timers.Clear();
            _completedTimers.Clear();
            UnregisterWhenIdle();
        }

        private void EnsureRegistered()
        {
            if (_registered)
            {
                return;
            }

            LifeCycle.RegisterUpdate(OnUpdate);
            _registered = true;
        }

        private void OnUpdate()
        {
            Tick(Time.deltaTime, Time.unscaledDeltaTime);
        }

        private void Tick(float deltaTime, float unscaledDeltaTime)
        {
            if (_timers.Count == 0)
            {
                return;
            }

            _completedTimers.Clear();
            var timerIds = new List<int>(_timers.Keys);
            foreach (var id in timerIds)
            {
                if (!_timers.TryGetValue(id, out var timer))
                {
                    continue;
                }

                timer.Remaining -= timer.UseUnscaledTime ? unscaledDeltaTime : deltaTime;
                if (timer.Remaining > 0f)
                {
                    _timers[id] = timer;
                    continue;
                }

                bool repeat;
                try
                {
                    repeat = timer.Callback();
                }
                catch (Exception e)
                {
                    Log.Error($"[TimerMgr] 计时器回调异常：{e}");
                    _completedTimers.Add(id);
                    continue;
                }

                if (repeat && timer.Interval > 0f && _timers.ContainsKey(id))
                {
                    timer.Remaining += timer.Interval;
                    _timers[id] = timer;
                }
                else
                {
                    _completedTimers.Add(id);
                }
            }

            foreach (var id in _completedTimers)
            {
                _timers.Remove(id);
            }

            UnregisterWhenIdle();
        }

        private void UnregisterWhenIdle()
        {
            if (!_registered || _timers.Count != 0)
            {
                return;
            }

            LifeCycle.UnregisterUpdate(OnUpdate);
            _registered = false;
        }

        private int NextId()
        {
            do
            {
                _nextId++;
            }
            while (_timers.ContainsKey(_nextId));

            return _nextId;
        }

        private struct TimerEntry
        {
            public Func<bool> Callback;
            public float Interval;
            public float Remaining;
            /// <summary>该计时器创建时确定的时间源，生命周期内保持不变。</summary>
            public bool UseUnscaledTime;
        }
    }
}
