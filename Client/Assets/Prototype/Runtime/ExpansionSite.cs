using UnityEngine;

namespace OutpostLoop
{
    /// <summary>四向地块可建的临时设施类型。</summary>
    public enum ExpansionKind { Empty, Extractor, Barracks, Barricade, Turret }

    /// <summary>用于预览及真正下单的共同校验结果。</summary>
    public enum PlacementIssue { None, Inactive, Outside, Occupied, Limit, NoResource, InsufficientStock, MissingTurret }

    /// <summary>扩建费用、耐久及生产周期的原型调参数据。</summary>
    public readonly struct ExpansionSpec
    {
        public readonly int Scrap, Energy, Health;
        public readonly float BuildSeconds, Cycle;
        public readonly Vector2Int Size;

        public ExpansionSpec(int scrap, int energy, int health, float buildSeconds, int width, int height, float cycle = 0)
        {
            Scrap = scrap;
            Energy = energy;
            Health = health;
            BuildSeconds = buildSeconds;
            Size = new Vector2Int(width, height);
            Cycle = cycle;
        }
    }

    /// <summary>一个外围地块的本局状态。地块被摧毁后可重新使用。</summary>
    public sealed class ExpansionSite
    {
        // 驻军是独立订单，费用不计入兵营的施工。
        public const int TrainingScrap = 6, TrainingEnergy = 2;
        public const float TrainingSeconds = 12;
        public RectInt Footprint { get; private set; }
        public Vector2 Position => BattleGrid.Center(Footprint);
        public ExpansionKind Kind { get; private set; }
        public int Health { get; private set; }
        public float ConstructionLeft { get; private set; }
        public bool IsUnderConstruction => ConstructionLeft > 0;
        public int HealthLimit => IsUnderConstruction ? SpecFor(Kind).Health / 2 : SpecFor(Kind).Health;
        // 采集站开工时锁定节点，完工后才开采。
        public int NodeIndex { get; private set; } = -1;
        // 兵营只有付费下单后才训练，训练结束后保留一支驻军。
        public float TrainingLeft { get; private set; }
        public bool HasGarrison { get; private set; }
        public bool GarrisonWaiting { get; private set; }
        public Vector2 GarrisonPosition { get; private set; }
        public bool CanTrain => Kind == ExpansionKind.Barracks && !IsUnderConstruction && TrainingLeft <= 0 &&
            !HasGarrison && !GarrisonWaiting;
        internal float WorkClock { get; set; }
        public Vector2 LastShotTarget { get; internal set; }
        public float LastShotTime { get; internal set; } = float.NegativeInfinity;

        /// <summary>建造界面和扣费事务读取同一组原型参数。</summary>
        public static ExpansionSpec SpecFor(ExpansionKind kind) => kind switch
        {
            ExpansionKind.Extractor => new ExpansionSpec(8, 0, 42, 5f, 2, 2, 2.5f),
            ExpansionKind.Barracks => new ExpansionSpec(14, 3, 72, 8f, 2, 2),
            ExpansionKind.Barricade => new ExpansionSpec(10, 0, 150, 3f, 2, 1),
            ExpansionKind.Turret => new ExpansionSpec(16, 3, 90, 6f, 1, 1),
            _ => default
        };

        /// <summary>占地并建立半耐久的工地。</summary>
        internal void Place(ExpansionKind kind, RectInt footprint, int node, bool finished = false)
        {
            var spec = SpecFor(kind);
            Kind = kind;
            Footprint = footprint;
            Health = finished ? spec.Health : spec.Health / 2;
            NodeIndex = node;
            ConstructionLeft = finished ? 0 : spec.BuildSeconds;
        }

        /// <summary>迁移保留耐久与冷却，清除旧位置的瞬时射线。</summary>
        internal void Relocate(RectInt footprint)
        {
            Footprint = footprint;
            LastShotTime = float.NegativeInfinity;
        }

        /// <summary>完工只补足剩余耐久，保留施工期间的损伤。</summary>
        internal void AdvanceConstruction(float seconds)
        {
            ConstructionLeft = Mathf.Max(0, ConstructionLeft - seconds);
            if (IsUnderConstruction) return;
            var spec = SpecFor(Kind);
            Health += spec.Health - spec.Health / 2;
        }

        internal void BeginTraining() => TrainingLeft = TrainingSeconds;

        /// <summary>训练结束后等待一个空闲的出入口。</summary>
        internal void AdvanceTraining(float seconds)
        {
            TrainingLeft = Mathf.Max(0, TrainingLeft - seconds);
            if (TrainingLeft <= 0) GarrisonWaiting = true;
        }

        internal void DeployGarrison(Vector2 position)
        {
            GarrisonPosition = position;
            HasGarrison = true;
            GarrisonWaiting = false;
        }

        /// <summary>建筑耗尽耐久时，清除采集绑定和驻军冷却。</summary>
        internal void Damage(int damage)
        {
            Health = Mathf.Max(0, Health - damage);
            if (Health != 0) return;
            Kind = ExpansionKind.Empty;
            NodeIndex = -1;
            ConstructionLeft = TrainingLeft = WorkClock = 0;
            HasGarrison = GarrisonWaiting = false;
            LastShotTime = float.NegativeInfinity;
        }
    }
}
