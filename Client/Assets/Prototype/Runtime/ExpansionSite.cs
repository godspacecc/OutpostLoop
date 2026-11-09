using UnityEngine;

namespace OutpostLoop
{
    /// <summary>四向地块可建的临时设施类型。</summary>
    public enum ExpansionKind { Empty, Extractor, Barracks, Barricade }

    /// <summary>扩建费用、耐久及生产周期的原型调参数据。</summary>
    public readonly struct ExpansionSpec
    {
        public readonly int Scrap, Energy, Health;
        public readonly float Cycle;

        public ExpansionSpec(int scrap, int energy, int health, float cycle = 0)
        {
            Scrap = scrap;
            Energy = energy;
            Health = health;
            Cycle = cycle;
        }
    }

    /// <summary>一个外围地块的本局状态。地块被摧毁后可重新使用。</summary>
    public sealed class ExpansionSite
    {
        public Vector2 Position { get; }
        public ExpansionKind Kind { get; private set; }
        public int Health { get; private set; }
        // 采集站只开采建成时绑定的有限资源点。
        public int NodeIndex { get; private set; } = -1;
        // 兵营先完成一次训练，再由驻军自动攻击附近敌人。
        public float TrainingLeft { get; internal set; }
        internal float WorkClock { get; set; }
        public Vector2 LastShotTarget { get; internal set; }
        public float LastShotTime { get; internal set; } = float.NegativeInfinity;

        internal ExpansionSite(Vector2 position) => Position = position;

        /// <summary>建造界面和扣费事务读取同一组原型参数。</summary>
        public static ExpansionSpec SpecFor(ExpansionKind kind) => kind switch
        {
            ExpansionKind.Extractor => new ExpansionSpec(8, 0, 42, 2.5f),
            ExpansionKind.Barracks => new ExpansionSpec(14, 3, 72, 12f),
            ExpansionKind.Barricade => new ExpansionSpec(10, 0, 150),
            _ => default
        };

        internal void Build(ExpansionKind kind, ExpansionSpec spec, int node)
        {
            Kind = kind;
            Health = spec.Health;
            NodeIndex = node;
            TrainingLeft = kind == ExpansionKind.Barracks ? spec.Cycle : 0;
        }

        /// <summary>建筑耗尽耐久时，清除采集绑定和驻军冷却。</summary>
        internal void Damage(int damage)
        {
            Health = Mathf.Max(0, Health - damage);
            if (Health != 0) return;
            Kind = ExpansionKind.Empty;
            NodeIndex = -1;
            TrainingLeft = WorkClock = 0;
            LastShotTime = float.NegativeInfinity;
        }
    }
}
