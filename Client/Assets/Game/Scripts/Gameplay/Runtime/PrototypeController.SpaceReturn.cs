using Game;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        // 结果缓存与存储实例绑定，编辑器测试切换隔离目录时不会混入玩家记录。
        private JsonFileStore<SurvivalSave> journeyStoreOwner;
        private SurvivalJourneyProgress survivalJourney;
        private ScavengeRun journeyRecordedRun;

        /// <summary>只读行动结果供空间菜单展示；没有任何可取出的物资。</summary>
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

        internal string JourneyResultTitle => JourneyProgress.lastOutcome switch
        {
            ScavengeOutcome.Died => "行动结束 · 阵亡",
            ScavengeOutcome.WindowClosed => "行动结束 · 撤离窗口关闭",
            _ => JourneyProgress.survived >= ScavengeSurvival.Goal ? "首次行动 · 达标回归" : "已回归 · 生存目标未达成"
        };

        internal string JourneyResultBody
        {
            get
            {
                var progress = JourneyProgress;
                string places = progress.discovered.Length == 0 ? "尚无带回的地点记录" : string.Join("、", progress.discovered);
                return $"存活 {SurvivalTime(progress.survived)}\n\n随身物品快照：{progress.carriedSummary}\n\n已查明地点：{places}\n\n空间不存放物资、装备或收藏。下一趟从空手开始。";
            }
        }

        internal string JourneyNextStep => JourneyProgress.completedFirst
            ? "三天生存已完成。后续行动：修通桥头备用通道。\n任务内容正在接入；当前可再次体验三天生存。"
            : "当前目标：活过三天并成功撤离。\n可以再次出发；历史结果会保留到下一次结算。";
    }
}
