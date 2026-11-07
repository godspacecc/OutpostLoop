#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Game.Editor
{
    /// <summary>
    /// 支撑编辑器构建流程中的 BuildStageTiming，记录或执行构建、资源和本地服务相关职责。
    /// </summary>
    public sealed class BuildStageTiming
    {
        private readonly Stopwatch _total = Stopwatch.StartNew();
        private readonly List<BuildStageRecord> _completedStages = new List<BuildStageRecord>();

        public BuildStageTiming(string workflowName)
        {
            WorkflowName = workflowName;
        }

        public string WorkflowName { get; }
        public string CurrentStage { get; private set; }
        public TimeSpan TotalElapsed => _total.Elapsed;
        public IReadOnlyList<BuildStageRecord> CompletedStages => _completedStages;

        public void Run(string stageName, Action action)
        {
            Run<object>(stageName, () =>
            {
                action();
                return null;
            });
        }

        public T Run<T>(string stageName, Func<T> action)
        {
            CurrentStage = stageName;
            CiBuildProgress.Stage(stageName);
            var sw = Stopwatch.StartNew();
            try
            {
                return action();
            }
            finally
            {
                sw.Stop();
                var record = new BuildStageRecord
                {
                    Name = stageName,
                    Seconds = sw.Elapsed.TotalSeconds
                };
                _completedStages.Add(record);
                Debug.Log($"[BuildTiming] 构建阶段耗时：{stageName}，{record.Seconds:F2}s");
            }
        }

        public string BuildSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"工作流：{WorkflowName}");
            sb.AppendLine($"当前阶段：{CurrentStage ?? "（无）"}");
            sb.AppendLine($"总耗时：{TotalElapsed.TotalSeconds:F2}s");
            sb.AppendLine("已完成阶段：");
            foreach (var stage in _completedStages)
                sb.AppendLine($"- {stage.Name}: {stage.Seconds:F2}s");
            return sb.ToString();
        }
    }

    /// <summary>
    /// 支撑编辑器构建流程中的 BuildStageRecord，记录或执行构建、资源和本地服务相关职责。
    /// </summary>
    [Serializable]
    public sealed class BuildStageRecord
    {
        public string Name;
        public double Seconds;
    }
}
#endif
