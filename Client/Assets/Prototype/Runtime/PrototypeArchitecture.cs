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
        public int SupplyCoins => this.GetUtility<PrototypeWallet>()?.Coins ?? 0;
        protected override void OnInit(){model=this.GetModel<PrototypeModel>();Game.LifeCycle.RegisterUpdate(OnTick);}
        protected override void OnDeinit(){Game.LifeCycle.UnregisterUpdate(OnTick);}
        public WorldState Begin(int seed,bool running)
        {
            var next=new WorldState(seed);next.Run.Paused=!running;
            model.World=next;
            awaitingStart = !running;
            rewardAttempted = false;
            VictoryRewardRecorded = false;
            this.GetSystem<Game.AudioSystem>()?.SetPaused(!running);
            this.SendEvent(new PrototypeRunStarted{Seed=seed});return next;
        }
        public bool AssignResource(int node) => model.World?.AssignResource(node) == true;
        public bool DeploySquad(int sector) => model.World?.DeploySquad(sector) == true;

        private void OnTick()
        {
            model.World?.Step(Time.deltaTime);
            if (model.World?.Run.Outcome > 0 && !rewardAttempted) TryRecordVictory();
        }

        /// <summary>只接受开局准备阶段的付款，成功后将物资加入库存并开始计时。</summary>
        internal bool StartWithSupplies(bool scrap, bool energy)
        {
            var run = model.World?.Run;
            if (!awaitingStart || run == null || run.Outcome != 0) return false;
            int price = (scrap ? PrototypeWallet.SupplyPrice : 0) + (energy ? PrototypeWallet.SupplyPrice : 0);
            if (price > 0 && this.GetUtility<PrototypeWallet>()?.Pay(price) != true) return false;
            awaitingStart = false;
            run.AddStarterSupplies(scrap, energy);
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
                VictoryRewardRecorded = this.GetUtility<PrototypeWallet>()?.RewardVictory(run.RewardId) == true;
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
            if(model.World==null)return false;
            var run=model.World.Run;bool paused=run.Paused;
            try{run.Paused=false;return run.Upgrade(route);}finally{run.Paused=paused;}
        }

        /// <summary>将基地炮位操作转发给本局规则。</summary>
        public bool BuildTurret(int slot) => model.World?.BuildTurret(slot) == true;
        public bool MoveTurret(int source, int destination) => model.World?.MoveTurret(source, destination) == true;

        /// <summary>将外围建设订单转发给运行中的本局规则。</summary>
        public bool BuildExpansion(int sector, ExpansionKind kind) =>
            !awaitingStart && model.World?.BuildExpansion(sector, kind) == true;

        /// <summary>转发兵营独立的付费训练订单。</summary>
        public bool TrainGarrison(int sector) => !awaitingStart && model.World?.TrainGarrison(sector) == true;
    }
}
