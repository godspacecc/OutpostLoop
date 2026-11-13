using System;
using System.Collections.Generic;

namespace OutpostLoop
{
    /// <summary>只保存行动结果与已查明情报，不能作为跨趟物品库存。</summary>
    [Serializable]
    internal sealed class SurvivalJourneyProgress
    {
        // 完成标记只在活满三天并成功撤离时写入，失败重试不撤销历史完成。
        public bool completedFirst, visitedSpace, hasResult;
        public ScavengeOutcome lastOutcome;
        public double survived;
        public int seed, carriedSlots;
        public string carriedSummary;
        // 信息快照只有地点名称，不保存容器、基地或装备状态。
        public string[] discovered = Array.Empty<string>();

        /// <summary>生成独立的结算快照，写盘成功前不修改旧进度。</summary>
        internal static SurvivalJourneyProgress Record(SurvivalJourneyProgress previous, ScavengeRun run, string carried)
        {
            if (run?.Survival == null || run.Outcome == ScavengeOutcome.Exploring)
                throw new ArgumentException("只能记录已结束的生存行动。", nameof(run));
            var state = run.Survival;
            bool extracted = run.Outcome == ScavengeOutcome.Extracted;
            var known = new List<string>(previous?.discovered ?? Array.Empty<string>());
            // 阵亡不带回本趟情报；已回归的历史情报继续保留。
            if (extracted)
                for (int i = 0; i < state.caches.Length; i++)
                    if (state.caches[i].known && !known.Contains(ScavengeTown.Names[i])) known.Add(ScavengeTown.Names[i]);
            return new SurvivalJourneyProgress
            {
                completedFirst = previous?.completedFirst == true || (extracted && state.Ready),
                visitedSpace = previous?.visitedSpace == true || extracted,
                hasResult = true, lastOutcome = run.Outcome, survived = state.elapsed, seed = run.Seed,
                carriedSlots = extracted ? state.Used : 0, carriedSummary = extracted ? carried : "本趟物品全部失去",
                discovered = known.ToArray()
            };
        }
    }
}
