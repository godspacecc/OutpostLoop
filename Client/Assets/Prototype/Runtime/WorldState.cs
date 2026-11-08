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
        public RunState Run { get; }
        private OutpostLoop.Config.PrototypeConfig Rules => Run.Config;
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
        private System.Collections.Generic.IReadOnlyList<float> WaveTimes => Rules.WaveTimes;
        /// <summary>按种子建立有限资源布局；近区一定存在两类可采资源。</summary>
        public WorldState(int seed, OutpostLoop.Config.PrototypeConfig config = null)
        {
            Run = new RunState(config);
            random = new System.Random(seed);
            Player=new Vector2(0,-5);
            for(int i=0;i<Rules.ResourceNodeCount;i++)
            {
                float angle = (float)random.NextDouble()*Mathf.PI*2;
                float radius = i < Rules.NearNodeCount ? Rules.NearRadius+i*Rules.NearRadiusStep : Rules.FarRadius+(float)random.NextDouble()*Rules.FarRadiusRange;
                Nodes.Add(new ResourceNode { Position=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius, Energy=i%Rules.EnergyEvery==0, Remaining=i%Rules.EnergyEvery==0?Rules.EnergyAmount:Rules.ScrapAmount });
            }
        }
        /// <summary>按实际采集顺序选出范围内首个未耗尽资源点，供交互和提示共用。</summary>
        public ResourceNode FindGatherTarget()
        {
            float radiusSquared = Rules.GatherRadius * Rules.GatherRadius;
            foreach (var node in Nodes)
                if (node.Remaining > 0 && (Player - node.Position).sqrMagnitude < radiusSquared)
                    return node;
            return null;
        }

        /// <summary>单步最多推进 0.1 秒，卡帧不瞬间跳过整段准备时间。</summary>
        public void Step(float seconds, Vector2 movement, bool gathering)
        {
            if(Run.Paused || Run.Outcome!=0 || float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds<=0)return;
            float dt=Mathf.Min(seconds,Rules.MaxStep);
            if(float.IsNaN(movement.x)||float.IsNaN(movement.y)||float.IsInfinity(movement.x)||float.IsInfinity(movement.y))movement=Vector2.zero;
            Player=Vector2.ClampMagnitude(Player+Vector2.ClampMagnitude(movement,1)*(Rules.MoveSpeed+Run.Levels[0])*dt,Rules.MapRadius);
            if(Player.magnitude<Rules.DepositRadius)Run.Deposit();
            gatherClock+=dt;
            if(gathering && gatherClock>=Rules.GatherInterval/(1+Run.Levels[0]*Rules.GatherLevelFactor))
            {
                var node = FindGatherTarget();
                if (node != null && Run.Gather(node.Energy))
                {
                    node.Remaining--;
                    gatherClock = 0;
                }
            }
            if(Wave<WaveTimes.Count && Run.Elapsed+dt>=WaveTimes[Wave])
            {
                Wave++;
                // 先用长整型计算配置数量，再截断到池容量，避免大数溢出后整波消失。
                int count=(int)System.Math.Min(Rules.MaxEnemies-Enemies.Count,(long)Rules.WaveCountBase+(long)Wave*Rules.WaveCountStep);
                float side=(float)random.NextDouble()*Mathf.PI*2;
                for(int i=0;i<count;i++)
                {
                    float angle=side+(float)random.NextDouble()*Rules.SpawnSpread-Rules.SpawnSpread/2;
                    Enemies.Add(new EnemyState {Position=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*(Rules.SpawnRadius+(float)random.NextDouble()*Rules.SpawnRadiusRange), Health=Rules.EnemyHealthBase+Wave*Rules.EnemyHealthStep});
                }
            }
            shotClock+=dt;
            if(shotClock>=Rules.ShotInterval)
            {
                EnemyState target=null;
                foreach(var enemy in Enemies)
                    if(enemy.Position.magnitude<Rules.ShotRange+Run.Levels[1]*Rules.ShotRangePerLevel && (target==null||enemy.Position.sqrMagnitude<target.Position.sqrMagnitude))target=enemy;
                if(target!=null){LastShotTarget=target.Position;LastShotTime=Run.Elapsed;target.Health-=Rules.ShotDamage+Run.Levels[1]*Rules.ShotDamagePerLevel;shotClock=0;}
            }
            for(int i=Enemies.Count-1;i>=0;i--)
            {
                var enemy=Enemies[i];
                if(enemy.Health<=0){Enemies.RemoveAt(i);Kills++;continue;}
                enemy.Position=Vector2.MoveTowards(enemy.Position,Vector2.zero,dt*(Rules.EnemySpeed+Wave*Rules.EnemySpeedPerWave));
                if(enemy.Position.magnitude<Rules.AttackRadius)
                {
                    enemy.AttackClock+=dt;
                    if(enemy.AttackClock>=Rules.AttackInterval){enemy.AttackClock-=Rules.AttackInterval;Run.Damage(Rules.AttackDamage);}
                }
            }
            Run.Tick(dt,Enemies.Count);
        }
    }
}
