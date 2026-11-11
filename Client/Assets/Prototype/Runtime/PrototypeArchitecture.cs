using QFramework;
using UnityEngine;
using OutpostLoop.Config;

namespace OutpostLoop
{
    /// <summary>玩法配置读取入口，只消费基础启动已准备的资源后端。</summary>
    public static class PrototypeServices
    {
        public static PrototypeConfig LoadConfig()
        {
            return PrototypeConfig.Load(address => Game.ResourceLoader.LoadAsset<TextAsset>(address)?.bytes);
        }
    }
    /// <summary>本局状态归模型持有，表现组件不再拥有权威世界。</summary>
    public sealed class PrototypeModel : AbstractModel
    {
        public WorldState World { get; internal set; }
        protected override void OnInit() { }
    }
    /// <summary>每次新局开始后发布，不携带Unity对象。</summary>
    public struct PrototypeRunStarted { public int Seed; }
    /// <summary>统一生命周期推进规则；界面只提交调度和购买意图。</summary>
    public sealed class PrototypeSystem : AbstractSystem
    {
        // 退出架构时解除帧订阅，调度命令直接交给本局校验。
        private PrototypeModel model;
        private bool awaitingStart;
        private bool rewardAttempted;
        public bool VictoryRewardRecorded { get; private set; }
        /// <summary>开局后尚未结算，或胜利奖励还未落盘时，重启应用会丢失进度。</summary>
        internal bool HasUnsavedRun => !awaitingStart && model?.World is { } world &&
            (world.Run.Outcome == 0 || world.Run.Outcome > 0 && !VictoryRewardRecorded);
        public int Wins => this.GetUtility<PrototypeWallet>()?.Wins ?? 0;
        public int StartingFunds => this.GetUtility<PrototypeWallet>()?.StartingFunds ?? PrototypeWallet.InitialFunds;
        internal ExpeditionProfile Profile => this.GetUtility<ExpeditionProfile>();
        protected override void OnInit(){model=this.GetModel<PrototypeModel>();Game.LifeCycle.RegisterUpdate(OnTick);}
        protected override void OnDeinit(){Game.LifeCycle.UnregisterUpdate(OnTick);}
        public WorldState Begin(int seed,bool running)
            => SetWorld(new WorldState(seed, startingFunds: StartingFunds), running);

        /// <summary>旧港地图沿用普通守城会话、资金和胜场结算。</summary>
        internal WorldState BeginOldHarbor(int seed) =>
            SetWorld(WorldState.CreateOldHarbor(seed, StartingFunds), true);

        internal WorldState BeginExpedition(int seed, ExpeditionMap map)
        {
            var next = WorldState.Depart(seed, map, Profile);
            return next == null ? null : SetWorld(next, true);
        }

        private WorldState SetWorld(WorldState next, bool running)
        {
            next.Run.Paused = !running;
            model.World=next;
            awaitingStart = !running;
            rewardAttempted = false;
            VictoryRewardRecorded = false;
            this.GetSystem<Game.AudioSystem>()?.SetPaused(!running);
            this.SendEvent(new PrototypeRunStarted { Seed = next.Seed });
            return next;
        }
        public bool MoveCommander(Vector2 point) => !awaitingStart && model.World?.MoveCommander(point) == true;
        public bool DeploySquad(int building, int sector) => !awaitingStart && model.World?.DeploySquad(building, sector) == true;

        private void OnTick()
        {
            model.World?.Step(Time.deltaTime);
            if (model.World?.Run.Outcome > 0 && !rewardAttempted) TryRecordVictory();
        }

        /// <summary>从准备页进入指挥员选址阶段，资金已按开局档位记录。</summary>
        internal bool StartRun()
        {
            if (!awaitingStart || model.World == null) return false;
            awaitingStart = false;
            SetPaused(false);
            return true;
        }

        /// <summary>胜利只结算一次；写盘失败可以从结算界面重试。</summary>
        internal bool TryRecordVictory()
        {
            var run = model.World?.Run;
            if (run == null || run.Outcome <= 0) return false;
            rewardAttempted = true;
            if (!VictoryRewardRecorded)
                VictoryRewardRecorded = model.World.Expedition != null
                    ? Profile?.RecordReturn(model.World) == true
                    : this.GetUtility<PrototypeWallet>()?.RecordVictory(run.RewardId) == true;
            return VictoryRewardRecorded;
        }
        /// <summary>同步本局暂停与基础音频状态；UI声音不受会话暂停影响。</summary>
        public void SetPaused(bool value)
        {
            if(model.World==null)return;
            model.World.Run.Paused=value;
            this.GetSystem<Game.AudioSystem>()?.SetPaused(value);
        }
        public bool Purchase(int route)
        {
            if(model.World==null || model.World.Expedition != null)return false;
            var run=model.World.Run;bool paused=run.Paused;
            try{run.Paused=false;return run.Upgrade(route);}finally{run.Paused=paused;}
        }

        /// <summary>转发地图落位和迁移命令，界面无法直接扣费或改占地。</summary>
        public PlacementIssue Build(ExpansionKind kind, Vector2Int cell, bool rotated) =>
            awaitingStart ? PlacementIssue.Inactive : model.World?.Build(kind, cell, rotated) ?? PlacementIssue.Inactive;

        public PlacementIssue MoveTurret(int source, Vector2Int cell) =>
            awaitingStart ? PlacementIssue.Inactive : model.World?.MoveTurret(source, cell) ?? PlacementIssue.Inactive;

        /// <summary>转发生产建筑的付费人员订单。</summary>
        public bool Produce(int building) => !awaitingStart && model.World?.Produce(building) == true;
    }
}
