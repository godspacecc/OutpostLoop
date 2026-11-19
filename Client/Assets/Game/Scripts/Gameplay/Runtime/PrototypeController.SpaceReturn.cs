using Game;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        // 结果缓存与存储实例绑定，编辑器测试切换隔离目录时不会混入玩家记录。
        private JsonFileStore<SurvivalSave> journeyStoreOwner;
        private SurvivalJourneyProgress survivalJourney;
        private ScavengeRun journeyRecordedRun;

        /// <summary>读取持久化行动进度和结果，供出发流程及后续任务界面使用。</summary>
        internal SurvivalJourneyProgress JourneyProgress
        {
            get
            {
                if (survivalJourney == null || !ReferenceEquals(journeyStoreOwner, SurvivalStore))
                {
                    var read = SurvivalStore.Read(SurvivalSaveName);
                    survivalJourney = read.Status == SaveFileStatus.Valid && read.Data.journey != null
                        ? read.Data.journey : new SurvivalJourneyProgress();
                    journeyStoreOwner = SurvivalStore;
                }
                return survivalJourney;
            }
        }

        /// <summary>先保存结果和关闭续玩，再返回已有空间菜单；保存失败留在结算画面。</summary>
        private void ReturnFromSurvival()
        {
            if (scavengeRun?.Survival == null || scavengeRun.Outcome == ScavengeOutcome.Exploring) return;
            if (CloseSurvivalSave()) LeaveScavenge();
        }

        /// <summary>结果快照为文字，不携带物品对象、弹量或可恢复的装备状态。</summary>
        private string CaptureSurvivalCarriedSummary()
        {
            var state = scavengeRun.Survival;
            var names = new System.Collections.Generic.List<string>();
            foreach (var item in state.pack) names.Add(DescribeSurvivalItem(item));
            if (state.weapon != null) names.Add(DescribeSurvivalItem(state.weapon));
            if (state.torch != null) names.Add(DescribeSurvivalItem(state.torch));
            return names.Count == 0 ? "空手" : string.Join("、", names);
        }
    }
}
