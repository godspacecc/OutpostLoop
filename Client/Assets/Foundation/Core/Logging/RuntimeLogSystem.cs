using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 订阅 Unity 日志并批量写入内存模型；错误在回调线程即时落盘，文件失败不递归产生日志。
    /// </summary>
    public sealed class RuntimeLogSystem : AbstractSystem
    {
        private readonly ConcurrentQueue<RuntimeLogEntry> pending = new ConcurrentQueue<RuntimeLogEntry>();
        private const int MaxPending = 2000;
        private bool subscribed;
        private RuntimeErrorLogStore errorLogStore;
        private bool reportedBackpressure;

        protected override void OnInit()
        {
            // 在主线程取得持久化路径；回调线程只访问已经构造的文件适配器。
            try { errorLogStore = new RuntimeErrorLogStore(RuntimeLogPaths.Directory); }
            catch { errorLogStore = null; } // 诊断目录不可写时仍保留内存日志。
            Subscribe();
            LifeCycle.RegisterUpdate(Update);
        }

        protected override void OnDeinit()
        {
            LifeCycle.UnregisterUpdate(Update);
            if (subscribed)
            {
                Application.logMessageReceivedThreaded -= OnLogReceived;
                subscribed = false;
            }

            while (pending.TryDequeue(out _))
            {
            }
        }

        public void EnqueueForTests(RuntimeLogEntry entry)
        {
            pending.Enqueue(entry);
        }

        /// <summary>在主线程导出前吸收队列中的日志；限制单次处理数量。</summary>
        public int DrainPending(int maxCount = 2001) => Drain(maxCount);

        /// <summary>测试入口沿用参考项目契约。</summary>
        public int DrainForTests(int maxCount) => DrainPending(maxCount);

        public void Clear()
        {
            this.GetModel<RuntimeLogModel>().Clear();
        }

        private void Subscribe()
        {
            if (subscribed)
            {
                return;
            }

            Application.logMessageReceivedThreaded += OnLogReceived;
            subscribed = true;
        }

        private void OnLogReceived(string condition, string stackTrace, LogType type)
        {
            var entry = new RuntimeLogEntry(DateTime.Now, type, condition, stackTrace);
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                errorLogStore?.Append(entry);
            pending.Enqueue(entry);
        }

        private void Update()
        {
            Drain(100);
        }

        private int Drain(int maxCount)
        {
            if (pending.IsEmpty)
            {
                return 0;
            }

            ApplyBackpressure();
            var count = 0;
            var batch = new List<RuntimeLogEntry>(Mathf.Max(0, maxCount));
            var model = this.GetModel<RuntimeLogModel>();
            while (count < maxCount && pending.TryDequeue(out var entry))
            {
                batch.Add(entry);
                count++;
            }

            model.AddRange(batch);
            return count;
        }

        private void ApplyBackpressure()
        {
            if (pending.Count <= MaxPending)
            {
                return;
            }

            var drained = new List<RuntimeLogEntry>(pending.Count);
            while (pending.TryDequeue(out var entry))
            {
                drained.Add(entry);
            }

            var dropped = 0;
            for (var i = 0; i < drained.Count && drained.Count - dropped > MaxPending; i++)
            {
                if (drained[i].Type == LogType.Log)
                {
                    drained[i] = default;
                    dropped++;
                }
            }

            if (dropped == 0)
            {
                while (drained.Count - dropped > MaxPending)
                {
                    drained[dropped] = default;
                    dropped++;
                }
            }

            foreach (var entry in drained)
            {
                if (entry.Message != null)
                {
                    pending.Enqueue(entry);
                }
            }

            if (dropped > 0 && !reportedBackpressure)
            {
                pending.Enqueue(new RuntimeLogEntry(DateTime.Now, LogType.Warning, $"RuntimeLog dropped {dropped} low-priority log entries.", string.Empty));
                reportedBackpressure = true;
            }
        }
    }
}
