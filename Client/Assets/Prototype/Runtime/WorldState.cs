using System.Collections.Generic;
using UnityEngine;
namespace OutpostLoop
{
    /// <summary>资源点的领域数据，不持有场景对象。</summary>
    public sealed class ResourceNode { public Vector2 Position; public bool Energy; public int Remaining; }
    /// <summary>敌人的领域数据，实例数受世界规则限制。</summary>
    public sealed class EnemyState { public Vector2 Position; public float Health; public float AttackClock; }
    /// <summary>有界地图和防守规则；预览视图只提交输入并读取状态。</summary>
    public sealed class WorldState
    {
        public RunState Run { get; } = new RunState();
        public List<ResourceNode> Nodes { get; } = new List<ResourceNode>();
        public List<EnemyState> Enemies { get; } = new List<EnemyState>();
        public Vector2 Player { get; private set; }
        public int Wave { get; private set; }
        public int Kills { get; private set; }
        // 最近一次真实命中供临时表现绘制短射线，不驱动伤害。
        public Vector2 LastShotTarget { get; private set; }
        public float LastShotTime { get; private set; } = -10;
        // 本局独立随机源和采集/射击计时，不读取全局或旧存档。
        private readonly System.Random random;
        private float gatherClock, shotClock;
        private static readonly float[] WaveTimes = {120,240,360,480,600,660};
        /// <summary>按种子建立有限资源布局；近区一定存在两类可采资源。</summary>
        public WorldState(int seed)
        {
            random = new System.Random(seed);
            Player=new Vector2(0,-5);
            for(int i=0;i<36;i++)
            {
                float angle = (float)random.NextDouble()*Mathf.PI*2;
                float radius = i < 6 ? 7+i*1.5f : 18+(float)random.NextDouble()*26;
                Nodes.Add(new ResourceNode { Position=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius, Energy=i%3==0, Remaining=i%3==0?8:18 });
            }
        }
        /// <summary>单步最多推进 0.1 秒，卡帧不瞬间跳过整段准备时间。</summary>
        public void Step(float seconds, Vector2 movement, bool gathering)
        {
            if(Run.Paused || Run.Outcome!=0 || float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds<=0)return;
            float dt=Mathf.Min(seconds,0.1f);
            if(float.IsNaN(movement.x)||float.IsNaN(movement.y)||float.IsInfinity(movement.x)||float.IsInfinity(movement.y))movement=Vector2.zero;
            Player=Vector2.ClampMagnitude(Player+Vector2.ClampMagnitude(movement,1)*(5+Run.Levels[0])*dt,46);
            if(Player.magnitude<3.5f)Run.Deposit();
            gatherClock+=dt;
            if(gathering && gatherClock>=0.6f/(1+Run.Levels[0]*0.4f))
            {
                foreach(var node in Nodes)
                    if(node.Remaining>0 && Vector2.Distance(Player,node.Position)<2 && Run.Gather(node.Energy))
                    { node.Remaining--; gatherClock=0; break; }
            }
            if(Wave<WaveTimes.Length && Run.Elapsed+dt>=WaveTimes[Wave])
            {
                Wave++;
                int count=Mathf.Min(48-Enemies.Count,6+Wave*4);
                float side=(float)random.NextDouble()*Mathf.PI*2;
                for(int i=0;i<count;i++)
                {
                    float angle=side+(float)random.NextDouble()*0.65f-0.325f;
                    Enemies.Add(new EnemyState {Position=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*(36+(float)random.NextDouble()*8), Health=16+Wave*3});
                }
            }
            shotClock+=dt;
            if(shotClock>=0.4f)
            {
                EnemyState target=null;
                foreach(var enemy in Enemies)
                    if(enemy.Position.magnitude<14+Run.Levels[1]*3 && (target==null||enemy.Position.sqrMagnitude<target.Position.sqrMagnitude))target=enemy;
                if(target!=null){LastShotTarget=target.Position;LastShotTime=Run.Elapsed;target.Health-=6+Run.Levels[1]*8;shotClock=0;}
            }
            for(int i=Enemies.Count-1;i>=0;i--)
            {
                var enemy=Enemies[i];
                if(enemy.Health<=0){Enemies.RemoveAt(i);Kills++;continue;}
                enemy.Position=Vector2.MoveTowards(enemy.Position,Vector2.zero,dt*(1.5f+Wave*0.1f));
                if(enemy.Position.magnitude<2.5f)
                {
                    enemy.AttackClock+=dt;
                    if(enemy.AttackClock>=1){enemy.AttackClock-=1;Run.Damage(2);}
                }
            }
            Run.Tick(dt,Enemies.Count);
        }
    }
}
