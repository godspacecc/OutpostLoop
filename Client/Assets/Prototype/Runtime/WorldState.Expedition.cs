using System;
using UnityEngine;
using OutpostLoop.Config;

namespace OutpostLoop
{
    public sealed partial class WorldState
    {
        // 近处保留连续空地，采集点、线索和终点均位于通行圆盘内。
        private static readonly Vector2Int[] OreOffsets =
        {
            new Vector2Int(3, 0), new Vector2Int(0, 3), new Vector2Int(-3, 0), new Vector2Int(0, -3),
            new Vector2Int(6, 3), new Vector2Int(-6, 3), new Vector2Int(-6, -3), new Vector2Int(6, -3)
        };

        public ExpeditionState Expedition { get; }
        private System.Random expeditionRandom;
        private int expeditionWave;
        private bool interactHeld;
        private ResourceNode gatheringNode;
        private float miningWork;
        // 指挥员和追兵各自共享目标格距离场，占地或目标格变化才重算。
        private readonly RouteWalker commanderWalker = new RouteWalker();
        private int[] commanderRoute, pursuitRoute;
        private int commanderRevision = -1, pursuitRevision = -1;
        private Vector2Int commanderRouteCell, pursuitCell;

        /// <summary>只从局外档案允许的地图出发，并复制档案中的强化和已带回科技。</summary>
        public static WorldState Depart(int seed, ExpeditionMap map, ExpeditionProfile profile, PrototypeConfig config = null)
        {
            if (profile?.CanEnter(map) != true) return null;
            return new WorldState(seed, config, 0, new ExpeditionState(map, profile.Tech, profile.MaxHealth));
        }

        private void InitializeExpedition(System.Random random)
        {
            expeditionRandom = random;
            int radius = Mathf.Max(1, Mathf.Min(7, Mathf.FloorToInt(Rules.MapRadius / 5)));
            var spawn = new Vector2Int(random.Next(-radius, radius + 1), random.Next(-radius, radius + 1));
            CommanderPosition = BattleGrid.Center(spawn);
            Run.DeployCommander(Expedition.MaxHealth);
            int horizontal = random.Next(2) == 0 ? -1 : 1;
            int vertical = random.Next(2) == 0 ? -1 : 1;
            foreach (var offset in OreOffsets)
                Nodes.Add(new ResourceNode { Position = BattleGrid.Center(spawn + offset), Remaining = 30 });
            Expedition.CluePosition = BattleGrid.Center(spawn + new Vector2Int(5 * horizontal, 6 * vertical));
            Expedition.SurveyPosition = BattleGrid.Center(spawn + new Vector2Int(-8 * horizontal, -7 * vertical));
        }

        public ExpansionSpec BuildingSpec(ExpansionKind kind) => Expedition != null && kind == ExpansionKind.Turret
            ? new ExpansionSpec(30, Expedition.Has(ExpeditionTech.Reinforcement) ? 120 : 90, 6, 1, 1)
            : ExpansionSite.SpecFor(kind);

        private PlacementIssue PreviewExpeditionPlacement(ExpansionKind kind, RectInt footprint, int moving, ExpansionSpec spec)
        {
            if (moving >= 0 || (kind != ExpansionKind.Shelter && kind != ExpansionKind.Turret)) return PlacementIssue.Inactive;
            if (kind == ExpansionKind.Turret && !Expedition.Has(ExpeditionTech.Fortifications)) return PlacementIssue.MissingResearch;
            int count = 0;
            foreach (var site in Buildings)
            {
                if (site.Kind == ExpansionKind.Empty) continue;
                if (site.Kind == kind) count++;
                if (site.Footprint.Overlaps(footprint)) return PlacementIssue.Occupied;
            }
            if (count >= (kind == ExpansionKind.Shelter ? 1 : 2)) return PlacementIssue.Limit;
            if (!InBuildArea(footprint, CommanderPosition)) return PlacementIssue.Outside;
            if (!Vision.Covers(footprint)) return PlacementIssue.NotVisible;
            if (UnitOccupies(footprint) || MineralOccupies(footprint) ||
                footprint.Contains(BattleGrid.Cell(Expedition.CluePosition)) ||
                footprint.Contains(BattleGrid.Cell(Expedition.SurveyPosition))) return PlacementIssue.Occupied;
            return Run.CanAfford(spec.Cost) ? PlacementIssue.None : PlacementIssue.InsufficientStock;
        }

        public bool NearShelter
        {
            get
            {
                foreach (var site in Buildings)
                    if (site.Kind == ExpansionKind.Shelter && !site.IsUnderConstruction &&
                        BattleGrid.DistanceTo(site.Footprint, CommanderPosition) <= 3) return true;
                return false;
            }
        }

        /// <summary>空字符串表示节点可下单。暂停界面由调用者控制，战场位置仍须满足条件。</summary>
        public string ResearchBlock(ExpeditionTech tech)
        {
            if (Expedition == null || (uint)tech > (uint)ExpeditionTech.Reinforcement) return "无效节点";
            if (Expedition.Has(tech)) return "已完成";
            if (Expedition.Researching.HasValue) return "已有研究进行中";
            if (!NearShelter) return "需在完工庇护所 3 米内";
            if (tech == ExpeditionTech.Survey && (!Expedition.Has(ExpeditionTech.Fortifications) ||
                Expedition.Map != ExpeditionMap.Wilderness || !Expedition.FoundClue)) return ExpeditionState.Requirement(tech);
            if (tech == ExpeditionTech.Reinforcement && (!Expedition.Has(ExpeditionTech.Survey) ||
                Expedition.Map != ExpeditionMap.Industrial || !Expedition.FoundClue)) return ExpeditionState.Requirement(tech);
            return Run.CanAfford(ExpeditionState.TechCost(tech)) ? "" : "物资不足";
        }

        public bool StartResearch(ExpeditionTech tech)
        {
            if (!Active || ResearchBlock(tech).Length != 0 || !Run.TrySpend(ExpeditionState.TechCost(tech))) return false;
            Expedition.Researching = tech;
            Expedition.ResearchLeft = ExpeditionState.TechSeconds(tech);
            return true;
        }

        private void CancelResearch()
        {
            Expedition.Researching = null;
            Expedition.ResearchLeft = 0;
        }

        public void HoldInteraction(bool held)
        {
            interactHeld = held && Run.Outcome == 0;
            if (!interactHeld) ResetInteraction();
        }

        private void ResetInteraction()
        {
            gatheringNode = null;
            miningWork = 0;
            if (Expedition != null) Expedition.SurveyProgress = 0;
        }

        private bool CanReach(Vector2 position) => (CommanderPosition - position).sqrMagnitude <= 4 && Vision.IsVisible(position);
        private ResourceNode NearbyOre()
        {
            ResourceNode nearest = null;
            float distance = float.PositiveInfinity;
            foreach (var node in Nodes)
            {
                if (node.Remaining <= 0 || !CanReach(node.Position)) continue;
                float next = (node.Position - CommanderPosition).sqrMagnitude;
                if (next >= distance) continue;
                nearest = node;
                distance = next;
            }
            return nearest;
        }

        public string InteractionHint
        {
            get
            {
                if (Expedition == null) return "";
                if (!Expedition.FoundClue && CanReach(Expedition.CluePosition)) return "E 获取研究线索";
                if (CanReach(Expedition.SurveyPosition)) return Expedition.BasicComplete
                    ? $"按住 E 勘察 {Expedition.SurveyProgress:0.0}/3 秒" : "勘察需先完成基础任务";
                var ore = NearbyOre();
                return ore == null ? "" : $"按住 E 采集 · 剩余 {ore.Remaining}";
            }
        }

        private void StepInteraction(float dt)
        {
            if (!interactHeld) return;
            if (Expedition.BasicComplete && CanReach(Expedition.SurveyPosition))
            {
                gatheringNode = null;
                miningWork = 0;
                Expedition.SurveyProgress += dt;
                if (Expedition.SurveyProgress + .00001f >= 3) FinishExpedition(ExpeditionEnd.Surveyed);
                return;
            }
            Expedition.SurveyProgress = 0;
            if (!Expedition.FoundClue && CanReach(Expedition.CluePosition)) Expedition.FoundClue = true;
            var ore = NearbyOre();
            if (ore != gatheringNode) { gatheringNode = ore; miningWork = 0; }
            if (ore == null) return;
            miningWork += 5 * dt;
            int amount = Mathf.Min(ore.Remaining, Mathf.FloorToInt(miningWork + .00001f));
            ore.Remaining -= amount;
            miningWork -= amount;
            Run.CreditDelivery(amount);
        }

        public bool ReturnFromExpedition()
        {
            if (Expedition?.BasicComplete != true || Run.Outcome != 0) return false;
            FinishExpedition(ExpeditionEnd.Returned);
            return true;
        }

        public bool AbandonExpedition()
        {
            if (Expedition == null || Expedition.BasicComplete || Run.Outcome != 0) return false;
            FinishExpedition(ExpeditionEnd.Abandoned);
            return true;
        }

        /// <summary>失败清空本局成果，回归仅留下可结算的任务奖励和完工科技。</summary>
        private void FinishExpedition(ExpeditionEnd end)
        {
            Expedition.End = end;
            CancelResearch();
            HoldInteraction(false);
            bool returned = Expedition.Reward > 0;
            Run.FinishExpedition(returned);
            if (returned) return;
            Expedition.CompletedTech = Expedition.StartingTech;
            Expedition.BasicComplete = false;
            Expedition.FoundClue = false;
            foreach (var site in Buildings) site.Damage(int.MaxValue);
            Enemies.Clear();
        }

        private void StepCommander(float dt)
        {
            if (!commanderTarget.HasValue) return;
            var target = commanderTarget.Value;
            var cell = BattleGrid.Cell(target);
            if (!navigation.Walkable(cell)) { commanderTarget = null; return; }
            if (commanderRevision != navigation.Revision || commanderRouteCell != cell)
            {
                commanderRevision = navigation.Revision;
                commanderRouteCell = cell;
                commanderRoute = navigation.Field(new RectInt(cell, Vector2Int.one), 0);
            }
            var point = CommanderPosition;
            if (BattleGrid.Cell(point) == cell)
            {
                point = Vector2.MoveTowards(point, target, Rules.MoveSpeed * dt);
                if (point == target) commanderTarget = null;
            }
            else navigation.Follow(commanderRoute, ref point, commanderWalker, Rules.MoveSpeed * dt);
            CommanderPosition = point;
        }

        private int[] CommanderAttackField()
        {
            var cell = BattleGrid.Cell(CommanderPosition);
            if (pursuitRevision != navigation.Revision || pursuitCell != cell)
            {
                pursuitRevision = navigation.Revision;
                pursuitCell = cell;
                pursuitRoute = navigation.Field(new RectInt(cell, Vector2Int.one), 0);
            }
            return pursuitRoute;
        }

        private void StepExpeditionEnemies(float time)
        {
            var map = Expedition.Rules;
            if (time < (expeditionWave + 1) * map.EnemyInterval) return;
            expeditionWave++;
            float angle = (float)expeditionRandom.NextDouble() * 2 * Mathf.PI;
            for (int i = 0; i < (map.HasFastEnemy ? 3 : 2) && Enemies.Count < map.EnemyCap; i++, angle += .09f)
            {
                var point = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (Rules.MapRadius - 4);
                var cell = BattleGrid.Cell(point);
                // 外围有建筑时向圆周寻找下一处可通行的格子。
                for (int attempt = 0; !navigation.Walkable(cell) && attempt < 64; attempt++)
                {
                    angle += .37f;
                    point = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (Rules.MapRadius - 4);
                    cell = BattleGrid.Cell(point);
                }
                if (!navigation.Walkable(cell)) continue;
                bool fast = i == 2;
                Enemies.Add(new EnemyState(fast ? EnemyKind.Fast : EnemyKind.Standard, expeditionWave, expeditionWave)
                {
                    Position = BattleGrid.Center(cell),
                    Health = fast ? 12 : Rules.EnemyHealthBase
                });
            }
        }

        private void StepExpedition(float dt)
        {
            StepCommander(dt);
            StepExpeditionEnemies(Run.Elapsed + dt);
            StepBuildings(dt);
            RefreshVision();
            RecordSightings(Run.Elapsed + dt);
            StepDefenders(dt);
            for (int i = Enemies.Count - 1; i >= 0; i--)
            {
                var enemy = Enemies[i];
                if (enemy.Health <= 0) { Enemies.RemoveAt(i); Kills++; continue; }
                StepEnemy(enemy, dt);
                if (Run.Outcome < 0) { FinishExpedition(ExpeditionEnd.Died); return; }
            }
            Run.Tick(dt, false);
            Expedition.BasicComplete = Run.Elapsed >= Expedition.Rules.SurvivalSeconds;
            if (Expedition.Researching.HasValue)
            {
                Expedition.ResearchLeft -= dt;
                if (Expedition.ResearchLeft <= .00001f)
                {
                    Expedition.CompletedTech |= ExpeditionState.Bit(Expedition.Researching.Value);
                    CancelResearch();
                }
            }
            StepInteraction(dt);
        }
    }
}
