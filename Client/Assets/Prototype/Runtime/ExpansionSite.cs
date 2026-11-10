using UnityEngine;

namespace OutpostLoop
{
    public enum ExpansionKind { Empty, Extractor, Barracks, Barricade, Turret, Headquarters }

    /// <summary>预览与提交共用同一组拒绝原因。</summary>
    public enum PlacementIssue { None, Inactive, Outside, Occupied, Limit, NoResource, InsufficientStock, MissingTurret, CommanderTooFar, NotVisible }

    /// <summary>建造侧栏与扣费规则共用的设施参数。</summary>
    public readonly struct ExpansionSpec
    {
        public readonly int Cost, Health;
        public readonly float BuildSeconds;
        public readonly Vector2Int Size;

        public ExpansionSpec(int cost, int health, float buildSeconds, int width, int height)
        {
            Cost = cost;
            Health = health;
            BuildSeconds = buildSeconds;
            Size = new Vector2Int(width, height);
        }
    }

    /// <summary>工地、建成设施及其唯一一份人员订单。</summary>
    public sealed class ExpansionSite
    {
        public const int PersonnelCost = 150;
        public const float GathererSeconds = 6, GuardSeconds = 12;
        public RectInt Footprint { get; private set; }
        public Vector2 Position => BattleGrid.Center(Footprint);
        public ExpansionKind Kind { get; private set; }
        public int Health { get; private set; }
        public float ConstructionLeft { get; private set; }
        public bool IsUnderConstruction => ConstructionLeft > 0;
        public int HealthLimit => IsUnderConstruction ? SpecFor(Kind).Health / 2 : SpecFor(Kind).Health;
        public int NodeIndex { get; internal set; } = -1;
        public float ProductionLeft { get; private set; }
        public float ProductionDuration => Kind == ExpansionKind.Extractor ? GathererSeconds : GuardSeconds;
        public bool PersonnelWaiting { get; private set; }
        public SitePersonnel Personnel { get; private set; }
        public bool CanProduce => (Kind == ExpansionKind.Extractor || Kind == ExpansionKind.Barracks) &&
            !IsUnderConstruction && ProductionLeft <= 0 && Personnel == null && !PersonnelWaiting;
        internal float WorkClock { get; set; }
        public Vector2 LastShotTarget { get; internal set; }
        public float LastShotTime { get; internal set; } = float.NegativeInfinity;

        public static ExpansionSpec SpecFor(ExpansionKind kind) => kind switch
        {
            ExpansionKind.Headquarters => new ExpansionSpec(0, 100, 0, 3, 3),
            ExpansionKind.Extractor => new ExpansionSpec(350, 42, 5, 2, 2),
            ExpansionKind.Barracks => new ExpansionSpec(250, 72, 8, 2, 2),
            ExpansionKind.Barricade => new ExpansionSpec(100, 150, 3, 2, 1),
            ExpansionKind.Turret => new ExpansionSpec(300, 90, 6, 1, 1),
            _ => default
        };

        internal void Place(ExpansionKind kind, RectInt footprint, int node)
        {
            var spec = SpecFor(kind);
            Kind = kind;
            Footprint = footprint;
            Health = spec.Health / 2;
            NodeIndex = node;
            ConstructionLeft = spec.BuildSeconds;
        }

        /// <summary>迁移保留炮塔耐久和射击冷却。</summary>
        internal void Relocate(RectInt footprint)
        {
            Footprint = footprint;
            LastShotTime = float.NegativeInfinity;
        }

        internal void AdvanceConstruction(float seconds)
        {
            ConstructionLeft = ConstructionLeft <= seconds + .00001f ? 0 : ConstructionLeft - seconds;
            if (IsUnderConstruction) return;
            var spec = SpecFor(Kind);
            Health += spec.Health - spec.Health / 2;
        }

        internal void BeginProduction() => ProductionLeft = ProductionDuration;

        internal void AdvanceProduction(float seconds)
        {
            ProductionLeft = ProductionLeft <= seconds + .00001f ? 0 : ProductionLeft - seconds;
            if (ProductionLeft <= 0) PersonnelWaiting = true;
        }

        internal void Deploy(Vector2 position)
        {
            Personnel = new SitePersonnel(position);
            PersonnelWaiting = false;
        }

        /// <summary>设施被毁后释放占地、采矿绑定及所属人员。</summary>
        internal void Damage(int damage)
        {
            Health = Mathf.Max(0, Health - damage);
            if (Health != 0) return;
            Kind = ExpansionKind.Empty;
            NodeIndex = -1;
            ConstructionLeft = ProductionLeft = WorkClock = 0;
            PersonnelWaiting = false;
            Personnel = null;
            LastShotTime = float.NegativeInfinity;
        }
    }

    /// <summary>采集员和守军共用行走状态，各自的命令与载货归所属设施。</summary>
    public sealed class SitePersonnel
    {
        public Vector2 Position { get; internal set; }
        public int Cargo { get; internal set; }
        public int Sector { get; internal set; } = -1;
        public bool Blocked { get; internal set; }
        internal bool Returning;
        internal float GatherClock;
        internal readonly RouteWalker Walker = new RouteWalker();
        // 随占地版本与目标变化刷新路线。
        internal int Revision = -1, RouteTarget = int.MinValue;
        internal int[] Route;

        internal SitePersonnel(Vector2 position) => Position = position;
    }
}
