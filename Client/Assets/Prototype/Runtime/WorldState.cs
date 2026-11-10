using System;
using System.Collections.Generic;
using UnityEngine;
using OutpostLoop.Config;

namespace OutpostLoop
{
    /// <summary>有限资源节点的规则状态。</summary>
    public sealed class ResourceNode { public Vector2 Position; public bool Energy; public int Remaining; }

    /// <summary>统一推进网格占地、单位行动和波次，不依赖视图。</summary>
    public sealed class WorldState
    {
        private const int ExpansionLimit = 4, TurretLimit = 4;
        private static readonly Vector2[] Posts = { Vector2.left, Vector2.right, Vector2.up, Vector2.down };
        public RunState Run { get; }
        public int Seed { get; }
        private PrototypeConfig Rules => Run.Config;
        private bool Active => !Run.Paused && Run.Outcome == 0;
        public List<ResourceNode> Nodes { get; } = new List<ResourceNode>();
        public List<EnemyState> Enemies { get; } = new List<EnemyState>();
        public IReadOnlyList<ExpansionSite> Buildings { get; }
        public Vector2 WorkerPosition { get; private set; } = new Vector2(0, -4);
        public int ResourceOrder { get; private set; } = -1;
        public bool WorkerReturning { get; private set; }
        public bool WorkerBlocked { get; private set; }
        public Vector2 SquadPosition { get; private set; } = new Vector2(0, -6);
        public int SquadSector { get; private set; } = -1;
        public bool SquadBlocked { get; private set; }
        public Vector2 SquadShotTarget { get; private set; }
        public float SquadShotTime { get; private set; } = float.NegativeInfinity;
        public int Wave => waves.Wave;
        public int WaitingEnemies => waves.Waiting;
        public float LastArrival => waves.LastArrival;
        public int Kills { get; private set; }
        public ExpansionKind LastDestroyedKind { get; private set; }
        public Vector2 LastDestroyedPosition { get; private set; }
        public float LastDestroyedAt { get; private set; } = float.NegativeInfinity;

        // 每份距离场只在占地变更或目的地变更时重新计算。
        private readonly BattleNavigation navigation;
        private readonly WaveDirector waves;
        private readonly RouteWalker workerRoute = new RouteWalker(), squadRoute = new RouteWalker();
        private int[] coreRoute, coreAttackRoute, resourceRoute, postRoute;
        private readonly int[][] buildingRoutes = new int[ExpansionLimit + TurretLimit][];
        private int resourceRouteIndex = -1, postRouteIndex = -1;
        private float gatherClock, squadShotClock;

        /// <summary>东西炮塔占用真实格子。资源保持原来的近远分布，并让开占地。</summary>
        public WorldState(int seed, PrototypeConfig config = null)
        {
            Seed = seed;
            Run = new RunState(config);
            var buildings = new ExpansionSite[ExpansionLimit + TurretLimit];
            for (int i = 0; i < buildings.Length; i++) buildings[i] = new ExpansionSite();
            Buildings = Array.AsReadOnly(buildings);
            Buildings[4].Place(ExpansionKind.Turret, new RectInt(-2, 0, 1, 1), -1, true);
            Buildings[5].Place(ExpansionKind.Turret, new RectInt(2, 0, 1, 1), -1, true);
            navigation = new BattleNavigation(Rules.MapRadius);
            RefreshNavigation();
            var random = new System.Random(seed);
            waves = new WaveDirector(random);
            for (int i = 0; i < Rules.ResourceNodeCount; i++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2;
                float radius = i < Rules.NearNodeCount ? Rules.NearRadius + i * Rules.NearRadiusStep
                    : Rules.FarRadius + (float)random.NextDouble() * Rules.FarRadiusRange;
                Vector2 position = Vector2.zero;
                for (int attempt = 0; attempt < 64; attempt++, angle += .618f)
                {
                    position = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    var cell = BattleGrid.Cell(position);
                    if (navigation.Walkable(cell) && !UnitOccupies(new RectInt(cell, Vector2Int.one)) &&
                        !Nodes.Exists(node => BattleGrid.Cell(node.Position) == cell)) break;
                }
                bool energy = i % Rules.EnergyEvery == 0;
                Nodes.Add(new ResourceNode { Position = position, Energy = energy, Remaining = energy ? Rules.EnergyAmount : Rules.ScrapAmount });
            }
        }

        private void RefreshNavigation()
        {
            navigation.Rebuild(Buildings);
            coreRoute = navigation.Field(BattleGrid.Base, 1.1f);
            coreAttackRoute = navigation.Field(BattleGrid.Base, Rules.AttackRadius);
            Array.Clear(buildingRoutes, 0, buildingRoutes.Length);
            resourceRoute = postRoute = null;
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

        private bool UnitOccupies(RectInt footprint)
        {
            if (footprint.Contains(BattleGrid.Cell(WorkerPosition)) || footprint.Contains(BattleGrid.Cell(SquadPosition))) return true;
            foreach (var site in Buildings)
                if (site.HasGarrison && footprint.Contains(BattleGrid.Cell(site.GarrisonPosition))) return true;
            foreach (var enemy in Enemies)
                if (enemy.Health > 0 && footprint.Contains(BattleGrid.Cell(enemy.Position))) return true;
            return false;
        }

        /// <summary>侧栏预览与左键提交调用同一校验，移动炮塔只忽略自己的原占地。</summary>
        public PlacementIssue PreviewPlacement(ExpansionKind kind, Vector2Int cell, bool rotated, int moving,
            out RectInt footprint, out int node)
        {
            var spec = ExpansionSite.SpecFor(kind);
            var size = rotated && kind == ExpansionKind.Barricade ? new Vector2Int(spec.Size.y, spec.Size.x) : spec.Size;
            footprint = new RectInt(cell, size);
            node = -1;
            if (!Active || spec.Health == 0) return PlacementIssue.Inactive;
            if (moving >= 0 && (kind != ExpansionKind.Turret || moving >= Buildings.Count ||
                Buildings[moving].Kind != ExpansionKind.Turret || Buildings[moving].IsUnderConstruction))
                return PlacementIssue.MissingTurret;
            foreach (var part in footprint.allPositionsWithin)
            {
                if (BattleGrid.Center(part).sqrMagnitude > BattleGrid.BuildRadius * BattleGrid.BuildRadius) return PlacementIssue.Outside;
                int occupied = FindBuilding(part);
                if (BattleGrid.Base.Contains(part) || (occupied >= 0 && occupied != moving)) return PlacementIssue.Occupied;
            }
            if (UnitOccupies(footprint)) return PlacementIssue.Occupied;
            foreach (var resource in Nodes)
                if (resource.Remaining > 0 && footprint.Contains(BattleGrid.Cell(resource.Position))) return PlacementIssue.Occupied;
            if (moving >= 0) return PlacementIssue.None;
            if (FreeSlot(kind) < 0) return PlacementIssue.Limit;
            if (kind == ExpansionKind.Extractor)
            {
                node = FindResourceFor(BattleGrid.Center(footprint));
                if (node < 0) return PlacementIssue.NoResource;
            }
            return Run.Scrap < spec.Scrap || Run.Energy < spec.Energy ? PlacementIssue.InsufficientStock : PlacementIssue.None;
        }

        private int FindResourceFor(Vector2 position)
        {
            float closest = 12 * 12;
            int nearest = -1;
            for (int i = 0; i < Nodes.Count; i++)
            {
                var node = Nodes[i];
                float distance = (node.Position - position).sqrMagnitude;
                if (node.Remaining <= 0 || distance > closest || (nearest >= 0 && distance == closest)) continue;
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
            var spec = ExpansionSite.SpecFor(kind);
            if (!Run.TrySpend(spec.Scrap, spec.Energy)) return PlacementIssue.InsufficientStock;
            Buildings[FreeSlot(kind)].Place(kind, footprint, node);
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
            return PlacementIssue.None;
        }

        /// <summary>兵营完工后由玩家付款训练，施工与训练使用不同计时。</summary>
        public bool TrainGarrison(int index)
        {
            if (!Active || index < 0 || index >= Buildings.Count || !Buildings[index].CanTrain ||
                !Run.TrySpend(ExpansionSite.TrainingScrap, ExpansionSite.TrainingEnergy)) return false;
            Buildings[index].BeginTraining();
            return true;
        }

        public bool AssignResource(int index)
        {
            if (!Active || index < 0 || index >= Nodes.Count || Nodes[index].Remaining <= 0) return false;
            if (ResourceOrder == index) return true;
            ResourceOrder = index;
            WorkerReturning = Run.CargoScrap + Run.CargoEnergy > 0;
            gatherClock = 0;
            return true;
        }

        public bool DeploySquad(int sector)
        {
            if (!Active || sector < 0 || sector >= Posts.Length) return false;
            SquadSector = sector;
            return true;
        }

        /// <summary>采运走可通行格。受阻时保留原命令，地图改变后自动恢复。</summary>
        private void StepWorker(float dt)
        {
            WorkerBlocked = false;
            if (!WorkerReturning && ResourceOrder < 0) return;
            if (!WorkerReturning && Nodes[ResourceOrder].Remaining <= 0)
            {
                ResourceOrder = -1;
                WorkerReturning = true;
            }
            if (!WorkerReturning && (resourceRoute == null || resourceRouteIndex != ResourceOrder))
            {
                resourceRouteIndex = ResourceOrder;
                resourceRoute = navigation.Field(new RectInt(BattleGrid.Cell(Nodes[ResourceOrder].Position), Vector2Int.one), 0);
            }
            var field = WorkerReturning ? coreRoute : resourceRoute;
            var position = WorkerPosition;
            WorkerBlocked = navigation.Distance(field, BattleGrid.Cell(position)) < 0;
            if (WorkerBlocked) return;
            bool arrived = navigation.Follow(field, ref position, workerRoute, (Rules.MoveSpeed + Run.Levels[0]) * dt);
            WorkerPosition = position;
            if (!arrived) return;
            if (WorkerReturning)
            {
                Run.Deposit();
                WorkerReturning = false;
                return;
            }
            var node = Nodes[ResourceOrder];
            gatherClock += dt;
            if (gatherClock < Rules.GatherInterval / (1 + Run.Levels[0] * Rules.GatherLevelFactor)) return;
            gatherClock = 0;
            if (Run.Gather(node.Energy)) node.Remaining--;
            if (node.Remaining <= 0) ResourceOrder = -1;
            WorkerReturning = ResourceOrder < 0 || Run.CargoScrap + Run.CargoEnergy >= Run.Capacity;
        }

        private EnemyState FindEnemy(Vector2 origin, float range)
        {
            EnemyState target = null;
            float closest = range * range;
            foreach (var enemy in Enemies)
            {
                float distance = (enemy.Position - origin).sqrMagnitude;
                if (enemy.Health <= 0 || distance >= closest) continue;
                closest = distance;
                target = enemy;
            }
            return target;
        }

        private void StepSquad(float dt)
        {
            SquadBlocked = false;
            if (SquadSector >= 0)
            {
                if (postRoute == null || postRouteIndex != SquadSector)
                {
                    postRouteIndex = SquadSector;
                    var cell = BattleGrid.Cell(Posts[SquadSector] * 12);
                    postRoute = navigation.Field(new RectInt(cell, Vector2Int.one), navigation.Walkable(cell) ? 0 : 1.1f);
                }
                var position = SquadPosition;
                SquadBlocked = navigation.Distance(postRoute, BattleGrid.Cell(position)) < 0;
                if (!SquadBlocked) navigation.Follow(postRoute, ref position, squadRoute, Rules.MoveSpeed * dt);
                SquadPosition = position;
            }
            squadShotClock += dt;
            if (squadShotClock < Rules.ShotInterval) return;
            var target = FindEnemy(SquadPosition, Rules.ShotRange * .75f);
            if (target == null) return;
            target.Health -= Rules.ShotDamage / 2f;
            SquadShotTarget = target.Position;
            SquadShotTime = Run.Elapsed;
            squadShotClock = 0;
        }

        /// <summary>部署只使用兵营紧邻的空闲通行格，出入口堵住时等待。</summary>
        private void TryDeployGarrison(ExpansionSite site)
        {
            var ring = new RectInt(site.Footprint.min - Vector2Int.one, site.Footprint.size + Vector2Int.one * 2);
            foreach (var cell in ring.allPositionsWithin)
            {
                if (!navigation.Walkable(cell) || UnitOccupies(new RectInt(cell, Vector2Int.one))) continue;
                site.DeployGarrison(BattleGrid.Center(cell));
                return;
            }
        }

        private void StepBuildings(float dt)
        {
            foreach (var site in Buildings)
            {
                if (site.IsUnderConstruction) { site.AdvanceConstruction(dt); continue; }
                switch (site.Kind)
                {
                    case ExpansionKind.Extractor:
                        var node = Nodes[site.NodeIndex];
                        if (node.Remaining <= 0) break;
                        site.WorkClock += dt;
                        float cycle = ExpansionSite.SpecFor(site.Kind).Cycle;
                        if (site.WorkClock < cycle) break;
                        site.WorkClock -= cycle;
                        node.Remaining--;
                        Run.ReceiveExtractedResource(node.Energy);
                        break;
                    case ExpansionKind.Barracks:
                        if (site.TrainingLeft > 0) site.AdvanceTraining(dt);
                        if (site.GarrisonWaiting) TryDeployGarrison(site);
                        if (site.HasGarrison) Fire(site, site.GarrisonPosition, Rules.ShotRange * .75f, Rules.ShotDamage / 2f, dt);
                        break;
                    case ExpansionKind.Turret:
                        int level = Run.Levels[1];
                        Fire(site, site.Position, Rules.ShotRange + level * Rules.ShotRangePerLevel,
                            (Rules.ShotDamage + level * Rules.ShotDamagePerLevel) / 2f, dt);
                        break;
                }
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
            if (target < 0 && BattleGrid.DistanceTo(BattleGrid.Base, enemy.Position) <= Rules.AttackRadius) target = -1;
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
                    RefreshNavigation();
                }
                return;
            }

            var field = coreAttackRoute;
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
            float speed = enemy.Kind switch
            {
                EnemyKind.Fast => 2.4f + .1f * enemy.Wave,
                EnemyKind.Siege => 1f + .05f * enemy.Wave,
                _ => Rules.EnemySpeed + enemy.Wave * Rules.EnemySpeedPerWave
            };
            navigation.Follow(field, ref enemy.Position, enemy.Walker, speed * dt);
        }

        /// <summary>单步有上限。终局同时检查活敌和待进场敌群。</summary>
        public void Step(float seconds)
        {
            if (!Active || float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0) return;
            float dt = Mathf.Min(seconds, Rules.MaxStep);
            StepWorker(dt);
            waves.Step(Run.Elapsed + dt, Rules.WaveTimes, Enemies, Rules);
            StepBuildings(dt);
            StepSquad(dt);
            for (int i = Enemies.Count - 1; i >= 0; i--)
            {
                var enemy = Enemies[i];
                if (enemy.Health <= 0) { Enemies.RemoveAt(i); Kills++; continue; }
                StepEnemy(enemy, dt);
                if (Run.Outcome != 0) return;
            }
            Run.Tick(dt, Enemies.Count + waves.Waiting);
        }
    }
}
