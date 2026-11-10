using System;
using System.Collections.Generic;
using UnityEngine;
using OutpostLoop.Config;

namespace OutpostLoop
{
    /// <summary>有限资源节点的规则状态。</summary>
    public sealed class ResourceNode
    {
        public Vector2 Position;
        public int Remaining;
        // 负数表示尚未被侦察。
        internal int ObservedRemaining = -1;
    }

    /// <summary>记录一路敌军被首次目击时的波次、相对基地的方向与局时。</summary>
    public readonly struct EnemySighting
    {
        public readonly int Wave, Sector;
        public readonly float Time;

        internal EnemySighting(int wave, int sector, float time)
        {
            Wave = wave;
            Sector = sector;
            Time = time;
        }
    }

    /// <summary>统一推进网格占地、单位行动和波次，不依赖视图。</summary>
    public sealed partial class WorldState
    {
        private const int ExpansionLimit = 4, TurretLimit = 4;
        private const float HeadquartersSight = 20;
        private static readonly Vector2[] Posts = { Vector2.left, Vector2.right, Vector2.up, Vector2.down };
        public RunState Run { get; }
        public int Seed { get; }
        private PrototypeConfig Rules => Run.Config;
        private bool Active => !Run.Paused && Run.Outcome == 0;
        public List<ResourceNode> Nodes { get; } = new List<ResourceNode>();
        public List<EnemyState> Enemies { get; } = new List<EnemyState>();
        public IReadOnlyList<ExpansionSite> Buildings { get; }
        // 选址前只有指挥员。基地一旦启用，占地和驻防都以玩家所选位置为准。
        public Vector2 CommanderPosition { get; private set; }
        public RectInt? Headquarters { get; private set; }
        public Vector2 BasePosition => Headquarters.HasValue ? BattleGrid.Center(Headquarters.Value) : CommanderPosition;
        public int Wave => Expedition == null ? waves.Wave : expeditionWave;
        public int WaitingEnemies => waves.Waiting;
        public BattleVisibility Vision { get; }
        public IReadOnlyList<EnemySighting> Sightings { get; }
        public int DiscoveredWave { get; private set; }
        public int Kills { get; private set; }
        public float LastMineDepletedAt { get; private set; } = float.NegativeInfinity;
        public ExpansionKind LastDestroyedKind { get; private set; }
        public Vector2 LastDestroyedPosition { get; private set; }
        public float LastDestroyedAt { get; private set; } = float.NegativeInfinity;

        // 每份距离场只在占地变更或目的地变更时重新计算。
        private readonly BattleNavigation navigation;
        private readonly WaveDirector waves;
        private int[] coreAttackRoute;
        // 选址阶段没有阻挡通行的建筑，指挥员直接走向地面目标。
        private Vector2? commanderTarget;
        private readonly int[][] buildingRoutes = new int[ExpansionLimit + TurretLimit][];
        private Vector2Int? miningPreviewCell;
        private bool miningPreviewValid;
        private readonly List<SightSource> sightSources = new List<SightSource>();
        private readonly HashSet<int> seenGroups = new HashSet<int>();
        private readonly List<EnemySighting> sightings = new List<EnemySighting>();
        private float TurretRange => Rules.ShotRange + Run.Levels[1] * Rules.ShotRangePerLevel;

        /// <summary>保留两侧候选区，矿点按种子生成，玩家不带建筑及生产人员进场。</summary>
        public WorldState(int seed, PrototypeConfig config = null, int startingFunds = PrototypeWallet.InitialFunds)
            : this(seed, config, startingFunds, null) { }

        private WorldState(int seed, PrototypeConfig config, int startingFunds, ExpeditionState expedition)
        {
            Seed = seed;
            Run = new RunState(config, startingFunds);
            Expedition = expedition;
            var buildings = new ExpansionSite[ExpansionLimit + TurretLimit];
            for (int i = 0; i < buildings.Length; i++) buildings[i] = new ExpansionSite();
            Buildings = Array.AsReadOnly(buildings);
            navigation = new BattleNavigation(Rules.MapRadius);
            Vision = new BattleVisibility(Rules.MapRadius);
            Sightings = sightings.AsReadOnly();
            var random = new System.Random(seed);
            waves = new WaveDirector(random);
            if (Expedition != null)
            {
                InitializeExpedition(random);
                RefreshVision();
                return;
            }
            var west = new RectInt(-5, -1, 3, 3);
            var east = new RectInt(3, -1, 3, 3);
            for (int i = 0; i < Rules.ResourceNodeCount; i++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2;
                float radius = i < Rules.NearNodeCount ? Rules.NearRadius + i * Rules.NearRadiusStep
                    : Rules.FarRadius + (float)random.NextDouble() * Rules.FarRadiusRange;
                for (int attempt = 0; attempt < 128; attempt++, angle += .618f)
                {
                    var position = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    var cell = BattleGrid.Cell(position);
                    if (!navigation.Walkable(cell) || cell == Vector2Int.zero || west.Contains(cell) || east.Contains(cell) ||
                        Nodes.Exists(node => BattleGrid.Cell(node.Position) == cell)) continue;
                    Nodes.Add(new ResourceNode { Position = position, Remaining = 480 });
                    break;
                }
            }
            RefreshVision();
        }

        /// <summary>仅由实际在场的己方单位与完工建筑提供视野。</summary>
        private void RefreshVision()
        {
            sightSources.Clear();
            if (Expedition != null)
            {
                sightSources.Add(new SightSource(CommanderPosition, 16));
                foreach (var site in Buildings)
                {
                    if (site.IsUnderConstruction) continue;
                    if (site.Kind == ExpansionKind.Shelter) sightSources.Add(new SightSource(site.Position, 12));
                    if (site.Kind == ExpansionKind.Turret) sightSources.Add(new SightSource(site.Position, Mathf.Max(16, TurretRange)));
                }
            }
            else if (!Headquarters.HasValue) sightSources.Add(new SightSource(CommanderPosition, 16));
            else
            {
                sightSources.Add(new SightSource(BasePosition, HeadquartersSight));
                foreach (var site in Buildings)
                {
                    if (site.IsUnderConstruction) continue;
                    if (site.Kind == ExpansionKind.Extractor || site.Kind == ExpansionKind.Barracks)
                        sightSources.Add(new SightSource(site.Position, 10));
                    if (site.Kind == ExpansionKind.Turret)
                        sightSources.Add(new SightSource(site.Position, Mathf.Max(16, TurretRange)));
                    if (site.Personnel != null)
                        sightSources.Add(new SightSource(site.Personnel.Position, site.Kind == ExpansionKind.Extractor ? 12 : 14));
                }
            }
            Vision.Observe(sightSources);
            foreach (var node in Nodes)
            {
                if (!Vision.IsVisible(node.Position)) continue;
                if ((node.ObservedRemaining > 0) != (node.Remaining > 0)) miningPreviewCell = null;
                node.ObservedRemaining = node.Remaining;
            }
        }

        /// <summary>未发现时返回负数，离开视野后只返回最后观测的余量。</summary>
        public int KnownOreRemaining(int index)
        {
            var node = Nodes[index];
            return Vision.IsVisible(node.Position) ? node.Remaining : node.ObservedRemaining;
        }

        /// <summary>以进入视野的实际位置相对于主基地计算来向，每路只发现一次。</summary>
        private void RecordSightings(float time)
        {
            foreach (var enemy in Enemies)
            {
                if (enemy.Health <= 0 || enemy.ArrivalGroup < 0 || !Vision.IsVisible(enemy.Position) ||
                    !seenGroups.Add(enemy.ArrivalGroup)) continue;
                var offset = enemy.Position - BasePosition;
                int sector = Mathf.Abs(offset.x) >= Mathf.Abs(offset.y) ? (offset.x < 0 ? 0 : 1) : (offset.y > 0 ? 2 : 3);
                sightings.Add(new EnemySighting(enemy.Wave, sector, time));
                DiscoveredWave = Mathf.Max(DiscoveredWave, enemy.Wave);
            }
        }

        /// <summary>启用后接收相对真实基地和占地重算的路线。</summary>
        private void RefreshNavigation()
        {
            navigation.Rebuild(Buildings, Headquarters);
            if (Headquarters.HasValue) coreAttackRoute = navigation.Field(Headquarters.Value, Rules.AttackRadius);
            Array.Clear(buildingRoutes, 0, buildingRoutes.Length);
        }

        /// <summary>保留右键指向的地面坐标，并把目标限制在地图圆内。</summary>
        public bool MoveCommander(Vector2 point)
        {
            if (!Active || Headquarters.HasValue || float.IsNaN(point.x) || float.IsNaN(point.y) ||
                float.IsInfinity(point.x) || float.IsInfinity(point.y)) return false;
            commanderTarget = Vector2.ClampMagnitude(point, Rules.MapRadius);
            commanderRevision = -1;
            return true;
        }

        public int FindBuilding(Vector2Int cell)
        {
            for (int i = 0; i < Buildings.Count; i++)
                if (Buildings[i].Kind != ExpansionKind.Empty && Buildings[i].Footprint.Contains(cell)) return i;
            return -1;
        }

        private int FreeSlot(ExpansionKind kind)
        {
            int start = kind == ExpansionKind.Turret ? ExpansionLimit : 0;
            int end = kind == ExpansionKind.Turret ? Buildings.Count : ExpansionLimit;
            for (int i = start; i < end; i++)
                if (Buildings[i].Kind == ExpansionKind.Empty) return i;
            return -1;
        }

        /// <summary>点选已出场的守军时返回其兵营。</summary>
        public int FindSquad(Vector2 point)
        {
            int index = -1;
            float closest = 2.25f;
            for (int i = 0; i < Buildings.Count; i++)
            {
                var site = Buildings[i];
                if (site.Kind != ExpansionKind.Barracks || site.Personnel == null) continue;
                float distance = (site.Personnel.Position - point).sqrMagnitude;
                if (distance > closest) continue;
                closest = distance;
                index = i;
            }
            return index;
        }

        private bool UnitOccupies(RectInt footprint)
        {
            if (!Headquarters.HasValue && footprint.Contains(BattleGrid.Cell(CommanderPosition))) return true;
            foreach (var site in Buildings)
                if (site.Personnel != null && footprint.Contains(BattleGrid.Cell(site.Personnel.Position))) return true;
            foreach (var enemy in Enemies)
                if (enemy.Health > 0 && footprint.Contains(BattleGrid.Cell(enemy.Position))) return true;
            return false;
        }

        private bool MineralOccupies(RectInt footprint) =>
            Nodes.Exists(node => node.Remaining > 0 && footprint.Contains(BattleGrid.Cell(node.Position)));

        private bool InBuildArea(RectInt footprint, Vector2 center)
        {
            foreach (var part in footprint.allPositionsWithin)
                if (!navigation.InBounds(part) || (BattleGrid.Center(part) - center).sqrMagnitude >
                    BattleGrid.BuildRadius * BattleGrid.BuildRadius) return false;
            return true;
        }

        /// <summary>预览与左键提交共用校验。主基地以光标格为中心，外围建筑以光标格为起点。</summary>
        public PlacementIssue PreviewPlacement(ExpansionKind kind, Vector2Int cell, bool rotated, int moving,
            out RectInt footprint, out int node)
        {
            var spec = BuildingSpec(kind);
            var size = rotated && kind == ExpansionKind.Barricade ? new Vector2Int(spec.Size.y, spec.Size.x) : spec.Size;
            footprint = new RectInt(kind == ExpansionKind.Headquarters ? cell - Vector2Int.one : cell, size);
            node = -1;
            if (!Active || spec.Health == 0) return PlacementIssue.Inactive;
            if (Expedition != null) return PreviewExpeditionPlacement(kind, footprint, moving, spec);
            if (kind == ExpansionKind.Shelter) return PlacementIssue.Inactive;
            if (kind == ExpansionKind.Headquarters) return PreviewHeadquarters(cell, footprint);
            if (!Headquarters.HasValue) return PlacementIssue.Inactive;
            if (!Vision.Covers(footprint)) return PlacementIssue.NotVisible;
            if (moving >= 0 && (kind != ExpansionKind.Turret || moving >= Buildings.Count ||
                Buildings[moving].Kind != ExpansionKind.Turret || Buildings[moving].IsUnderConstruction))
                return PlacementIssue.MissingTurret;
            if (!InBuildArea(footprint, BasePosition)) return PlacementIssue.Outside;
            if (Headquarters.Value.Overlaps(footprint) || UnitOccupies(footprint) || MineralOccupies(footprint))
                return PlacementIssue.Occupied;
            foreach (var part in footprint.allPositionsWithin)
            {
                int occupied = FindBuilding(part);
                if (occupied >= 0 && occupied != moving) return PlacementIssue.Occupied;
            }
            if (moving >= 0) return PlacementIssue.None;
            if (FreeSlot(kind) < 0) return PlacementIssue.Limit;
            if (kind == ExpansionKind.Extractor)
            {
                node = FindResourceFor(BattleGrid.Center(footprint));
                if (node < 0) return PlacementIssue.NoResource;
            }
            return Run.CanAfford(spec.Cost) ? PlacementIssue.None : PlacementIssue.InsufficientStock;
        }

        private PlacementIssue PreviewHeadquarters(Vector2Int cell, RectInt footprint)
        {
            if (Headquarters.HasValue) return PlacementIssue.Limit;
            if (!Vision.Covers(footprint)) return PlacementIssue.NotVisible;
            var center = BattleGrid.Center(cell);
            if (center.sqrMagnitude > 28 * 28 || !InBuildArea(footprint, center)) return PlacementIssue.Outside;
            if ((center - CommanderPosition).sqrMagnitude > 12 * 12) return PlacementIssue.CommanderTooFar;
            if (UnitOccupies(footprint) || MineralOccupies(footprint)) return PlacementIssue.Occupied;
            if (miningPreviewCell != cell)
            {
                miningPreviewCell = cell;
                miningPreviewValid = CanStartMining(footprint, center);
            }
            return miningPreviewValid ? PlacementIssue.None : PlacementIssue.NoResource;
        }

        /// <summary>主基地预留至少一块能通往矿点的合法采集站用地。</summary>
        private bool CanStartMining(RectInt headquarters, Vector2 center)
        {
            var size = ExpansionSite.SpecFor(ExpansionKind.Extractor).Size;
            var origin = BattleGrid.Cell(center);
            const int radius = BattleGrid.BuildRadius / BattleGrid.CellSize;
            var possible = new RectInt(origin - Vector2Int.one * radius, Vector2Int.one * (2 * radius + 1));
            var futureSight = new SightSource(center, HeadquartersSight);
            foreach (var cell in possible.allPositionsWithin)
            {
                var station = new RectInt(cell, size);
                if (!InBuildArea(station, center) || station.Overlaps(headquarters) || MineralOccupies(station)) continue;
                bool visible = true;
                foreach (var part in station.allPositionsWithin) visible &= futureSight.Sees(BattleGrid.Center(part));
                if (!visible) continue;
                int node = FindResourceFor(BattleGrid.Center(station));
                if (node < 0 || headquarters.Contains(BattleGrid.Cell(Nodes[node].Position))) continue;
                // 选址阶段只有这两个小型占地，矿点和站点外侧都位于连通的地图圆盘内。
                return true;
            }
            return false;
        }

        private int FindResourceFor(Vector2 position)
        {
            float closest = 12 * 12;
            int nearest = -1;
            for (int i = 0; i < Nodes.Count; i++)
            {
                var node = Nodes[i];
                float distance = (node.Position - position).sqrMagnitude;
                if (KnownOreRemaining(i) <= 0 || distance > closest || (nearest >= 0 && distance == closest)) continue;
                bool bound = false;
                foreach (var site in Buildings) bound |= site.NodeIndex == i;
                if (bound) continue;
                nearest = i;
                closest = distance;
            }
            return nearest;
        }

        /// <summary>同一步内校验库存、锁定占地并建立工地。</summary>
        public PlacementIssue Build(ExpansionKind kind, Vector2Int cell, bool rotated = false)
        {
            var issue = PreviewPlacement(kind, cell, rotated, -1, out var footprint, out int node);
            if (issue != PlacementIssue.None) return issue;
            if (kind == ExpansionKind.Headquarters)
            {
                Headquarters = footprint;
                commanderTarget = null;
                Run.EstablishBase();
                RefreshNavigation();
                RefreshVision();
                return PlacementIssue.None;
            }
            var spec = BuildingSpec(kind);
            if (!Run.TrySpend(spec.Cost)) return PlacementIssue.InsufficientStock;
            Buildings[FreeSlot(kind)].Place(kind, footprint, node, spec);
            RefreshNavigation();
            return PlacementIssue.None;
        }

        public PlacementIssue MoveTurret(int source, Vector2Int cell)
        {
            if (source < 0) return PlacementIssue.MissingTurret;
            var issue = PreviewPlacement(ExpansionKind.Turret, cell, false, source, out var footprint, out _);
            if (issue != PlacementIssue.None) return issue;
            Buildings[source].Relocate(footprint);
            RefreshNavigation();
            RefreshVision();
            RecordSightings(Run.Elapsed);
            return PlacementIssue.None;
        }

        /// <summary>生产建筑完工后一次付费，完成或等待出入口期间禁止重复订单。</summary>
        public bool Produce(int index)
        {
            if (!Active || index < 0 || index >= Buildings.Count || !Buildings[index].CanProduce ||
                !Run.TrySpend(ExpansionSite.PersonnelCost)) return false;
            Buildings[index].BeginProduction();
            return true;
        }

        public bool DeploySquad(int index, int sector)
        {
            if (!Active || index < 0 || index >= Buildings.Count || sector < 0 || sector >= Posts.Length) return false;
            var site = Buildings[index];
            if (site.Kind != ExpansionKind.Barracks || site.Personnel == null) return false;
            site.Personnel.Sector = sector;
            return true;
        }

        /// <summary>按设施管理采集、返程和载货。矿点耗尽后先送达手里的货。</summary>
        private void StepGatherer(ExpansionSite site, float dt)
        {
            var gatherer = site.Personnel;
            if (gatherer == null) return;
            if (site.NodeIndex < 0 && gatherer.Cargo == 0) { gatherer.Blocked = false; return; }
            if (site.NodeIndex < 0) gatherer.Returning = true;
            int target = gatherer.Returning ? -2 : site.NodeIndex;
            if (gatherer.Revision != navigation.Revision || gatherer.RouteTarget != target)
            {
                gatherer.Revision = navigation.Revision;
                gatherer.RouteTarget = target;
                gatherer.Route = gatherer.Returning ? navigation.Field(site.Footprint, 1.1f) :
                    navigation.Field(new RectInt(BattleGrid.Cell(Nodes[target].Position), Vector2Int.one), 0);
            }
            if (!Follow(gatherer, (Rules.MoveSpeed + Run.Levels[0]) * dt)) return;
            if (gatherer.Returning)
            {
                Run.CreditDelivery(gatherer.Cargo);
                gatherer.Cargo = 0;
                gatherer.Returning = false;
                return;
            }
            var node = Nodes[target];
            gatherer.GatherClock += dt;
            if (gatherer.GatherClock + .00001f < Rules.GatherInterval / (1 + Run.Levels[0] * Rules.GatherLevelFactor)) return;
            gatherer.GatherClock = 0;
            int amount = Mathf.Min(20, Mathf.Min(node.Remaining, Run.Capacity - gatherer.Cargo));
            node.Remaining -= amount;
            gatherer.Cargo += amount;
            if (node.Remaining == 0) LastMineDepletedAt = Run.Elapsed;
            gatherer.Returning = node.Remaining == 0 || gatherer.Cargo == Run.Capacity;
        }

        private bool Follow(SitePersonnel personnel, float travel)
        {
            var point = personnel.Position;
            personnel.Blocked = navigation.Distance(personnel.Route, BattleGrid.Cell(point)) < 0;
            if (personnel.Blocked) return false;
            bool arrived = navigation.Follow(personnel.Route, ref point, personnel.Walker, travel);
            personnel.Position = point;
            return arrived;
        }

        private EnemyState FindEnemy(Vector2 origin, float range)
        {
            EnemyState target = null;
            float closest = range * range;
            foreach (var enemy in Enemies)
            {
                float distance = (enemy.Position - origin).sqrMagnitude;
                if (enemy.Health <= 0 || distance >= closest || !Vision.IsVisible(enemy.Position)) continue;
                closest = distance;
                target = enemy;
            }
            return target;
        }

        private void StepSquad(ExpansionSite site, float dt)
        {
            var squad = site.Personnel;
            if (squad == null) return;
            if (squad.Sector >= 0)
            {
                if (squad.Revision != navigation.Revision || squad.RouteTarget != squad.Sector)
                {
                    squad.Revision = navigation.Revision;
                    squad.RouteTarget = squad.Sector;
                    var cell = BattleGrid.Cell(BasePosition + Posts[squad.Sector] * 12);
                    squad.Route = navigation.Field(new RectInt(cell, Vector2Int.one), navigation.Walkable(cell) ? 0 : 1.1f);
                }
                Follow(squad, Rules.MoveSpeed * dt);
            }
        }

        /// <summary>生产完毕后，从设施的空闲相邻格出场。</summary>
        private void TryDeploy(ExpansionSite site)
        {
            var ring = new RectInt(site.Footprint.min - Vector2Int.one, site.Footprint.size + Vector2Int.one * 2);
            foreach (var cell in ring.allPositionsWithin)
            {
                var point = BattleGrid.Center(cell);
                if (!navigation.Walkable(cell) || BattleGrid.DistanceTo(site.Footprint, point) > 1.1f ||
                    UnitOccupies(new RectInt(cell, Vector2Int.one))) continue;
                site.Deploy(point);
                return;
            }
        }

        private void StepBuildings(float dt)
        {
            foreach (var site in Buildings)
            {
                if (site.IsUnderConstruction)
                {
                    if (Expedition != null && site.Kind == ExpansionKind.Turret && site.HealthLimit < 60 &&
                        site.ConstructionLeft <= dt + .00001f && Expedition.Has(ExpeditionTech.Reinforcement))
                        site.ReinforceTurret();
                    site.AdvanceConstruction(dt);
                    continue;
                }
                if (site.ProductionLeft > 0) site.AdvanceProduction(dt);
                if (site.PersonnelWaiting) TryDeploy(site);
                switch (site.Kind)
                {
                    case ExpansionKind.Extractor:
                        if (site.NodeIndex < 0 || KnownOreRemaining(site.NodeIndex) <= 0)
                            site.NodeIndex = FindResourceFor(site.Position);
                        StepGatherer(site, dt);
                        break;
                    case ExpansionKind.Barracks:
                        StepSquad(site, dt);
                        break;
                }
            }
        }

        /// <summary>友军移动并刷新视野后，再按当前可见目标交战。</summary>
        private void StepDefenders(float dt)
        {
            foreach (var site in Buildings)
            {
                if (site.IsUnderConstruction) continue;
                if (site.Kind == ExpansionKind.Turret)
                    Fire(site, site.Position, TurretRange, (Rules.ShotDamage + Run.Levels[1] * Rules.ShotDamagePerLevel) / 2f, dt);
                if (site.Kind == ExpansionKind.Barracks && site.Personnel != null)
                    Fire(site, site.Personnel.Position, Rules.ShotRange * .75f, Rules.ShotDamage / 2f, dt);
            }
        }

        private void Fire(ExpansionSite site, Vector2 origin, float range, float damage, float dt)
        {
            site.WorkClock += dt;
            if (site.WorkClock < Rules.ShotInterval) return;
            var target = FindEnemy(origin, range);
            if (target == null) return;
            target.Health -= damage;
            site.LastShotTarget = target.Position;
            site.LastShotTime = Run.Elapsed;
            site.WorkClock = 0;
        }

        /// <summary>优先攻击已接触的建筑。基地不可达时，用最短路线寻找可攻击的建筑。</summary>
        private void StepEnemy(EnemyState enemy, float dt)
        {
            int target = -2;
            float closest = Rules.AttackRadius;
            for (int i = 0; i < Buildings.Count; i++)
            {
                var site = Buildings[i];
                if (site.Kind == ExpansionKind.Empty) continue;
                float distance = BattleGrid.DistanceTo(site.Footprint, enemy.Position);
                if (distance > closest || (target >= 0 && distance == closest)) continue;
                closest = distance;
                target = i;
            }
            if (target < 0 && (Expedition == null
                ? BattleGrid.DistanceTo(Headquarters.Value, enemy.Position) <= Rules.AttackRadius
                : (CommanderPosition - enemy.Position).sqrMagnitude <= 1.5f * 1.5f &&
                    navigation.CanTravelStraight(enemy.Position, CommanderPosition))) target = -1;
            if (enemy.AttackSite != target)
            {
                enemy.AttackSite = target;
                enemy.AttackClock = 0;
            }
            if (target >= -1)
            {
                enemy.AttackClock += dt;
                if (enemy.AttackClock + .00001f < Rules.AttackInterval) return;
                enemy.AttackClock = Mathf.Max(0, enemy.AttackClock - Rules.AttackInterval);
                enemy.LastAttackTime = Run.Elapsed;
                int damage = enemy.Kind switch { EnemyKind.Fast => 1, EnemyKind.Siege => target < 0 ? 3 : 5, _ => Rules.AttackDamage };
                if (target < 0) Run.Damage(damage);
                else
                {
                    var site = Buildings[target];
                    var kind = site.Kind;
                    site.Damage(damage);
                    if (site.Kind != ExpansionKind.Empty) return;
                    LastDestroyedKind = kind;
                    LastDestroyedPosition = site.Position;
                    LastDestroyedAt = Run.Elapsed;
                    if (Expedition != null && kind == ExpansionKind.Shelter) CancelResearch();
                    RefreshNavigation();
                }
                return;
            }

            float speed = enemy.Kind switch
            {
                EnemyKind.Fast => 2.4f + .1f * enemy.Wave,
                EnemyKind.Siege => 1f + .05f * enemy.Wave,
                _ => Rules.EnemySpeed + enemy.Wave * Rules.EnemySpeedPerWave
            };
            if (Expedition != null && navigation.CanTravelStraight(enemy.Position, CommanderPosition))
            {
                enemy.Walker.Next = null;
                enemy.Position = Vector2.MoveTowards(enemy.Position, CommanderPosition, speed * dt);
                return;
            }

            var field = Expedition == null ? coreAttackRoute : CommanderAttackField();
            var cell = BattleGrid.Cell(enemy.Position);
            if (navigation.Distance(field, cell) < 0)
            {
                int shortest = int.MaxValue;
                for (int i = 0; i < Buildings.Count; i++)
                {
                    if (Buildings[i].Kind == ExpansionKind.Empty) continue;
                    var route = buildingRoutes[i] ??= navigation.Field(Buildings[i].Footprint, Rules.AttackRadius);
                    int distance = navigation.Distance(route, cell);
                    if (distance < 0 || distance >= shortest) continue;
                    shortest = distance;
                    field = route;
                }
            }
            if (Expedition != null) navigation.FollowSmoothed(field, ref enemy.Position, enemy.Walker, speed * dt);
            else navigation.Follow(field, ref enemy.Position, enemy.Walker, speed * dt);
        }

        /// <summary>单步有上限。终局同时检查活敌和待进场敌群。</summary>
        public void Step(float seconds)
        {
            if (!Active || float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0) return;
            float dt = Mathf.Min(seconds, Rules.MaxStep);
            if (Expedition != null) { StepExpedition(dt); return; }
            if (!Headquarters.HasValue)
            {
                if (!commanderTarget.HasValue) return;
                var target = commanderTarget.Value;
                CommanderPosition = Vector2.MoveTowards(CommanderPosition, target, Rules.MoveSpeed * dt);
                if (CommanderPosition.Equals(target)) commanderTarget = null;
                RefreshVision();
                return;
            }
            waves.Step(Run.Elapsed + dt, Rules.WaveTimes, Enemies, Rules);
            StepBuildings(dt);
            RefreshVision();
            RecordSightings(Run.Elapsed + dt);
            StepDefenders(dt);
            for (int i = Enemies.Count - 1; i >= 0; i--)
            {
                var enemy = Enemies[i];
                if (enemy.Health <= 0) { Enemies.RemoveAt(i); Kills++; continue; }
                StepEnemy(enemy, dt);
                if (Run.Outcome != 0) break;
            }
            RefreshVision();
            RecordSightings(Run.Elapsed + dt);
            if (Run.Outcome != 0) return;
            Run.Tick(dt, Wave == Rules.WaveTimes.Count && Enemies.Count == 0 && waves.Waiting == 0);
        }
    }
}
