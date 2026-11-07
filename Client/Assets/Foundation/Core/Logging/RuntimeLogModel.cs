using System;
using System.Collections.Generic;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 表示一条运行时日志记录，包含等级、时间、正文和堆栈，供诊断面板与导出工具读取。
    /// </summary>
    public readonly struct RuntimeLogEntry
    {
        public RuntimeLogEntry(DateTime timestamp, LogType type, string message, string stackTrace)
        {
            Timestamp = timestamp;
            Type = type;
            Message = message ?? string.Empty;
            StackTrace = stackTrace ?? string.Empty;
        }

        public DateTime Timestamp { get; }
        public LogType Type { get; }
        public string Message { get; }
        public string StackTrace { get; }
    }

    /// <summary>
    /// 运行时日志集合变化通知，携带新增日志条目，不负责筛选或持久化。
    /// </summary>
    public readonly struct RuntimeLogChangedEvent
    {
        public RuntimeLogChangedEvent(IReadOnlyList<RuntimeLogEntry> entries)
        {
            Entries = entries;
        }

        public IReadOnlyList<RuntimeLogEntry> Entries { get; }
    }

    /// <summary>
    /// 保存有容量上限的运行时日志队列，超过上限时丢弃最旧条目以控制内存占用。
    /// </summary>
    public sealed class RuntimeLogModel : AbstractModel
    {
        private const int Capacity = 500;
        private readonly List<RuntimeLogEntry> entries = new List<RuntimeLogEntry>(Capacity);

        public IReadOnlyList<RuntimeLogEntry> Snapshot()
        {
            return entries.ToArray();
        }

        public void Add(RuntimeLogEntry entry)
        {
            AddRange(new[] { entry });
        }

        public void AddRange(IReadOnlyList<RuntimeLogEntry> newEntries)
        {
            if (newEntries == null || newEntries.Count == 0)
            {
                return;
            }

            var overflow = entries.Count + newEntries.Count - Capacity;
            if (overflow > 0)
            {
                entries.RemoveRange(0, Mathf.Min(overflow, entries.Count));
            }

            var start = Mathf.Max(0, newEntries.Count - Capacity);
            for (var i = start; i < newEntries.Count; i++)
            {
                entries.Add(newEntries[i]);
            }

            this.SendEvent(new RuntimeLogChangedEvent(Snapshot()));
        }

        public void Clear()
        {
            entries.Clear();
            this.SendEvent(new RuntimeLogChangedEvent(Snapshot()));
        }

        protected override void OnInit()
        {
        }
    }
}
