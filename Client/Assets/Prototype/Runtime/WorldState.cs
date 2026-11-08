using System.Collections.Generic;
using UnityEngine;
namespace OutpostLoop
{
    /// <summary>资源点的领域数据，不持有场景对象。</summary>
    public sealed class ResourceNode { public Vector2 Position; public bool Energy; public int Remaining; }
    /// <summary>敌人的领域数据，实例数受世界规则限制。</summary>
    public sealed class EnemyState { public Vector2 Position; public float Health; public float AttackClock; }
    /// <summary>一个固定炮位及其本局开火状态，位置不依赖场景对象。</summary>
    public sealed class TurretSlot
    {
        public Vector2 Position { get; }
        public bool Occupied { get; internal set; }
        public Vector2 LastShotTarget { get; internal set; }
        public float LastShotTime { get; internal set; } = float.NegativeInfinity;
        internal float ShotClock { get; set; }

        internal TurretSlot(Vector2 position, bool occupied = false)
        {
            Position = position;
            Occupied = occupied;
        }
    }
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
        // 炮位顺序对应基地面板的西、东、北、南。开局西、东各一座。
        public IReadOnlyList<TurretSlot> Turrets { get; } = System.Array.AsReadOnly(new[]
        {
            new TurretSlot(new Vector2(-4, 0), true),
            new TurretSlot(new Vector2(4, 0), true),
            new TurretSlot(new Vector2(0, 4)),
            new TurretSlot(new Vector2(0, -4))
        });
        public bool IsAtBase => Player.sqrMagnitude < 25f;
        public int TurretScrapCost => Rules.ScrapCostBase * 2;
        public int TurretEnergyCost => Rules.EnergyCostPerLevel;

        // 本局独立随机源和采集计时，不读取全局或旧存档。
        private readonly System.Random random;
        private float gatherClock;
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

        /// <summary>在基地范围内建造，先校验炮位与库存，再一次性扣费。</summary>
        public bool BuildTurret(int index)
        {
            if (!IsAtBase || !IsValidSlot(index) || Turrets[index].Occupied ||
                !Run.TrySpend(TurretScrapCost, TurretEnergyCost)) return false;
            Turrets[index].Occupied = true;
            return true;
        }

        /// <summary>免费迁入空位，保留开火冷却，清掉旧炮位的射线。</summary>
        public bool MoveTurret(int source, int destination)
        {
            if (!IsAtBase || Run.Outcome != 0 || !IsValidSlot(source) || !IsValidSlot(destination)) return false;
            var from = Turrets[source];
            var to = Turrets[destination];
            if (!from.Occupied || to.Occupied) return false;
            to.Occupied = true;
            to.ShotClock = from.ShotClock;
            from.Occupied = false;
            from.ShotClock = 0;
            from.LastShotTime = float.NegativeInfinity;
            return true;
        }

        private bool IsValidSlot(int index) => index >= 0 && index < Turrets.Count;

        /// <summary>各炮塔以自身位置独立索敌，开局两座炮塔平分原基础火力。</summary>
        private void FireTurrets(float dt)
        {
            float range = Rules.ShotRange + Run.Levels[1] * Rules.ShotRangePerLevel;
            float damage = (Rules.ShotDamage + Run.Levels[1] * Rules.ShotDamagePerLevel) / 2f;
            foreach (var turret in Turrets)
            {
                if (!turret.Occupied) continue;
                turret.ShotClock += dt;
                if (turret.ShotClock < Rules.ShotInterval) continue;
                EnemyState target = null;
                float closest = range * range;
                foreach (var enemy in Enemies)
                {
                    if (enemy.Health <= 0) continue;
                    float distance = (enemy.Position - turret.Position).sqrMagnitude;
                    if (distance >= closest) continue;
                    closest = distance;
                    target = enemy;
                }
                if (target == null) continue;
                target.Health -= damage;
                turret.LastShotTarget = target.Position;
                turret.LastShotTime = Run.Elapsed;
                turret.ShotClock = 0;
            }
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
            FireTurrets(dt);
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
