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
    /// <summary>统一生命周期推进规则；界面只提交输入和购买意图。</summary>
    public sealed class PrototypeSystem : AbstractSystem
    {
        // 输入快照与领域模型，退出架构时解除帧订阅。
        private Vector2 movement;
        private bool gathering;
        private PrototypeModel model;
        protected override void OnInit(){model=this.GetModel<PrototypeModel>();Game.LifeCycle.RegisterUpdate(OnTick);}
        protected override void OnDeinit(){Game.LifeCycle.UnregisterUpdate(OnTick);}
        public WorldState Begin(int seed,bool running)
        {
            var next=new WorldState(seed);next.Run.Paused=!running;
            model.World=next;movement=Vector2.zero;gathering=false;
            this.GetSystem<Game.AudioSystem>()?.SetPaused(!running);
            this.SendEvent(new PrototypeRunStarted{Seed=seed});return next;
        }
        public void SubmitInput(Vector2 direction,bool collect){movement=direction;gathering=collect;}
        private void OnTick(){model.World?.Step(Time.deltaTime,movement,gathering);}
        /// <summary>同步本局暂停与基础音频状态；UI声音不受会话暂停影响。</summary>
        public void SetPaused(bool value)
        {
            if(model.World==null)return;
            model.World.Run.Paused=value;
            this.GetSystem<Game.AudioSystem>()?.SetPaused(value);
        }
        public bool Purchase(int route)
        {
            if(model.World==null||!model.World.IsAtBase)return false;
            var run=model.World.Run;bool paused=run.Paused;
            try{run.Paused=false;return run.Upgrade(route);}finally{run.Paused=paused;}
        }

        /// <summary>将基地炮位操作转发给本局规则。</summary>
        public bool BuildTurret(int slot) => model.World?.BuildTurret(slot) == true;
        public bool MoveTurret(int source, int destination) => model.World?.MoveTurret(source, destination) == true;
    }
}
