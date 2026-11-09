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
        // 工人持续执行同一资源任务，装满或采尽后自动返程。
        public Vector2 WorkerPosition { get; private set; }
        public int ResourceOrder { get; private set; } = -1;
        public bool WorkerReturning { get; private set; }
        // 小队共用一个驻防命令与开火状态，表现可以绘制多名守军。
        public Vector2 SquadPosition { get; private set; } = new Vector2(0, -5);
        public int SquadSector { get; private set; } = -1;
        public Vector2 SquadShotTarget { get; private set; }
        public float SquadShotTime { get; private set; } = float.NegativeInfinity;
        // 方向与炮位顺序一致。最后一波出发后不再预告。
        public int NextWaveSector => Wave < WaveTimes.Count ? SectorFor(nextWaveDirection) : -1;
        public float NextWaveTime => Wave < WaveTimes.Count ? WaveTimes[Wave] : Run.Elapsed;
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
        public int TurretScrapCost => Rules.ScrapCostBase * 2;
        public int TurretEnergyCost => Rules.EnergyCostPerLevel;

        // 本局独立随机源。提前抽取下一波角度，预告和生成共用同一个值。
        private readonly System.Random random;
        private Vector2 nextWaveDirection;
        private float gatherClock;
        private float squadShotClock;
        private IReadOnlyList<float> WaveTimes => Rules.WaveTimes;
        /// <summary>按种子建立有限资源布局；近区一定存在两类可采资源。</summary>
        public WorldState(int seed, OutpostLoop.Config.PrototypeConfig config = null)
        {
            Run = new RunState(config);
            random = new System.Random(seed);
            for(int i=0;i<Rules.ResourceNodeCount;i++)
            {
                float angle = (float)random.NextDouble()*Mathf.PI*2;
                float radius = i < Rules.NearNodeCount ? Rules.NearRadius+i*Rules.NearRadiusStep : Rules.FarRadius+(float)random.NextDouble()*Rules.FarRadiusRange;
                Nodes.Add(new ResourceNode { Position=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius, Energy=i%Rules.EnergyEvery==0, Remaining=i%Rules.EnergyEvery==0?Rules.EnergyAmount:Rules.ScrapAmount });
            }
            PlanNextWave();
        }

        /// <summary>改派带货工人时先卸货，再执行新的资源任务。</summary>
        public bool AssignResource(int index)
        {
            if (Run.Paused || Run.Outcome != 0 || index < 0 || index >= Nodes.Count || Nodes[index].Remaining <= 0)
                return false;
            if (ResourceOrder == index) return true;
            ResourceOrder = index;
            WorkerReturning = Run.CargoScrap + Run.CargoEnergy > 0;
            gatherClock = 0;
            return true;
        }

        /// <summary>一条命令调动整支小队，移动和开火由本局推进。</summary>
        public bool DeploySquad(int sector)
        {
            if (Run.Paused || Run.Outcome != 0 || !IsValidSlot(sector)) return false;
            SquadSector = sector;
            return true;
        }

        /// <summary>从指挥视图建造，先校验炮位与库存，再一次性扣费。</summary>
        public bool BuildTurret(int index)
        {
            if (!IsValidSlot(index) || Turrets[index].Occupied ||
                !Run.TrySpend(TurretScrapCost, TurretEnergyCost)) return false;
            Turrets[index].Occupied = true;
            return true;
        }

        /// <summary>免费迁入空位，保留开火冷却，清掉旧炮位的射线。</summary>
        public bool MoveTurret(int source, int destination)
        {
            if (Run.Outcome != 0 || !IsValidSlot(source) || !IsValidSlot(destination)) return false;
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
                var target = FindEnemy(turret.Position, range);
                if (target == null) continue;
                target.Health -= damage;
                turret.LastShotTarget = target.Position;
                turret.LastShotTime = Run.Elapsed;
                turret.ShotClock = 0;
            }
        }

        /// <summary>炮塔和小队都从自身位置锁定射程内最近的存活敌人。</summary>
        private EnemyState FindEnemy(Vector2 origin, float range)
        {
            EnemyState target = null;
            float closest = range * range;
            foreach (var enemy in Enemies)
            {
                if (enemy.Health <= 0) continue;
                float distance = (enemy.Position - origin).sqrMagnitude;
                if (distance >= closest) continue;
                closest = distance;
                target = enemy;
            }
            return target;
        }

        /// <summary>沿直线往返基地与资源点，采集速度和装载上限复用原有升级。</summary>
        private void StepWorker(float dt)
        {
            float travel = (Rules.MoveSpeed + Run.Levels[0]) * dt;
            if (WorkerReturning)
            {
                WorkerPosition = Vector2.MoveTowards(WorkerPosition, Vector2.zero, travel);
                if (WorkerPosition.sqrMagnitude >= Rules.DepositRadius * Rules.DepositRadius) return;
                Run.Deposit();
                WorkerReturning = false;
                return;
            }
            if (ResourceOrder < 0) return;
            var node = Nodes[ResourceOrder];
            WorkerPosition = Vector2.MoveTowards(WorkerPosition, node.Position, travel);
            if ((WorkerPosition - node.Position).sqrMagnitude >= Rules.GatherRadius * Rules.GatherRadius) return;
            gatherClock += dt;
            if (gatherClock < Rules.GatherInterval / (1 + Run.Levels[0] * Rules.GatherLevelFactor)) return;
            gatherClock = 0;
            if (Run.Gather(node.Energy)) node.Remaining--;
            if (node.Remaining <= 0) ResourceOrder = -1;
            WorkerReturning = ResourceOrder < 0 || Run.CargoScrap + Run.CargoEnergy >= Run.Capacity;
        }

        /// <summary>小队在指定防线驻守，途中和到位后均可自动开火。</summary>
        private void StepSquad(float dt)
        {
            if (SquadSector < 0) return;
            var post = Turrets[SquadSector].Position * 3f;
            SquadPosition = Vector2.MoveTowards(SquadPosition, post, Rules.MoveSpeed * dt);
            squadShotClock += dt;
            if (squadShotClock < Rules.ShotInterval) return;
            var target = FindEnemy(SquadPosition, Rules.ShotRange * .75f);
            if (target == null) return;
            target.Health -= Rules.ShotDamage / 2f;
            SquadShotTarget = target.Position;
            SquadShotTime = Run.Elapsed;
            squadShotClock = 0;
        }

        /// <summary>按主要坐标轴映射到西、东、北、南四条防线。</summary>
        private static int SectorFor(Vector2 direction) => Mathf.Abs(direction.x) >= Mathf.Abs(direction.y)
            ? (direction.x < 0 ? 0 : 1)
            : (direction.y > 0 ? 2 : 3);

        private void PlanNextWave()
        {
            if (Wave >= WaveTimes.Count) return;
            float angle = (float)random.NextDouble() * Mathf.PI * 2;
            nextWaveDirection = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        /// <summary>单步最多推进 0.1 秒，卡帧不瞬间跳过整段准备时间。</summary>
        public void Step(float seconds)
        {
            if(Run.Paused || Run.Outcome!=0 || float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds<=0)return;
            float dt=Mathf.Min(seconds,Rules.MaxStep);
            StepWorker(dt);
            if(Wave<WaveTimes.Count && Run.Elapsed+dt>=WaveTimes[Wave])
            {
                Wave++;
                // 先用长整型计算配置数量，再截断到池容量，避免大数溢出后整波消失。
                int count=(int)System.Math.Min(Rules.MaxEnemies-Enemies.Count,(long)Rules.WaveCountBase+(long)Wave*Rules.WaveCountStep);
                float side=Mathf.Atan2(nextWaveDirection.y, nextWaveDirection.x);
                for(int i=0;i<count;i++)
                {
                    float angle=side+(float)random.NextDouble()*Rules.SpawnSpread-Rules.SpawnSpread/2;
                    Enemies.Add(new EnemyState {Position=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*(Rules.SpawnRadius+(float)random.NextDouble()*Rules.SpawnRadiusRange), Health=Rules.EnemyHealthBase+Wave*Rules.EnemyHealthStep});
                }
                PlanNextWave();
            }
            FireTurrets(dt);
            StepSquad(dt);
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
