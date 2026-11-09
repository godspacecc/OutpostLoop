using UnityEngine;

namespace OutpostLoop
{
    /// <summary>四向地块可建的临时设施类型。</summary>
    public enum ExpansionKind { Empty, Extractor, Barracks, Barricade }

    /// <summary>扩建费用、耐久及生产周期的原型调参数据。</summary>
    public readonly struct ExpansionSpec
    {
        public readonly int Scrap, Energy, Health;
        public readonly float BuildSeconds, Cycle;

        public ExpansionSpec(int scrap, int energy, int health, float buildSeconds, float cycle = 0)
        {
            Scrap = scrap;
            Energy = energy;
            Health = health;
            BuildSeconds = buildSeconds;
            Cycle = cycle;
        }
    }

    /// <summary>一个外围地块的本局状态。地块被摧毁后可重新使用。</summary>
    public sealed class ExpansionSite
    {
        // 驻军是独立订单，费用不计入兵营的施工。
        public const int TrainingScrap = 6, TrainingEnergy = 2;
        public const float TrainingSeconds = 12;
        public Vector2 Position { get; }
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
        public bool CanTrain => Kind == ExpansionKind.Barracks && !IsUnderConstruction && TrainingLeft <= 0 && !HasGarrison;
        internal float WorkClock { get; set; }
        public Vector2 LastShotTarget { get; internal set; }
        public float LastShotTime { get; internal set; } = float.NegativeInfinity;

        internal ExpansionSite(Vector2 position) => Position = position;

        /// <summary>建造界面和扣费事务读取同一组原型参数。</summary>
        public static ExpansionSpec SpecFor(ExpansionKind kind) => kind switch
        {
            ExpansionKind.Extractor => new ExpansionSpec(8, 0, 42, 5f, 2.5f),
            ExpansionKind.Barracks => new ExpansionSpec(14, 3, 72, 8f),
            ExpansionKind.Barricade => new ExpansionSpec(10, 0, 150, 3f),
            _ => default
        };

        /// <summary>占地并建立半耐久的工地。</summary>
        internal void BeginConstruction(ExpansionKind kind, ExpansionSpec spec, int node)
        {
            Kind = kind;
            Health = spec.Health / 2;
            NodeIndex = node;
            ConstructionLeft = spec.BuildSeconds;
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

        /// <summary>订单到期时才建立驻军。</summary>
        internal void AdvanceTraining(float seconds)
        {
            TrainingLeft = Mathf.Max(0, TrainingLeft - seconds);
            if (TrainingLeft <= 0) HasGarrison = true;
        }

        /// <summary>建筑耗尽耐久时，清除采集绑定和驻军冷却。</summary>
        internal void Damage(int damage)
        {
            Health = Mathf.Max(0, Health - damage);
            if (Health != 0) return;
            Kind = ExpansionKind.Empty;
            NodeIndex = -1;
            ConstructionLeft = TrainingLeft = WorkClock = 0;
            HasGarrison = false;
            LastShotTime = float.NegativeInfinity;
        }
    }
}
