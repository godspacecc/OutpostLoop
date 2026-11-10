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
        private Vector2 commanderDestination;
        private Vector2Int pursuitCell;

        /// <summary>只从局外档案允许的地图出发，并复制档案中的强化和已带回科技。</summary>
        public static WorldState Depart(int seed, ExpeditionMap map, ExpeditionProfile profile, PrototypeConfig config = null)
        {
            if (profile?.CanEnter(map) != true) return null;
            return new WorldState(seed, config, 0, new ExpeditionState(map, profile.Tech, profile.MaxHealth, profile.CommanderSight));
        }

        private void InitializeExpedition(System.Random random)
        {
            expeditionRandom = random;
            Run.DeployCommander(Expedition.MaxHealth);
            GenerateExpeditionMap(random);
        }

        public ExpansionSpec BuildingSpec(ExpansionKind kind) => Expedition == null ? ExpansionSite.SpecFor(kind) : kind switch
        {
            ExpansionKind.Turret => new ExpansionSpec(30, Expedition.Has(ExpeditionTech.Reinforcement) ? 120 : 90, 6, 1, 1),
            ExpansionKind.Extractor => new ExpansionSpec(25, 80, 5, 2, 2),
            ExpansionKind.Barricade => new ExpansionSpec(10, Expedition.Has(ExpeditionTech.Bracing) ? 120 : 80, 2, 2, 1),
            _ => ExpansionSite.SpecFor(kind)
        };


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
            if (Expedition == null || (uint)tech > (uint)ExpeditionTech.Bracing) return "无效节点";
            if (Expedition.Has(tech)) return "已完成";
            if (Expedition.Researching.HasValue) return "已有研究进行中";
            if (!NearShelter) return "需在完工庇护所 3 米内";
            if (tech == ExpeditionTech.Survey && (!Expedition.Has(ExpeditionTech.Fortifications) ||
                Expedition.Map != ExpeditionMap.Wilderness || !Expedition.FoundClue)) return ExpeditionState.Requirement(tech);
            if (tech == ExpeditionTech.Reinforcement && (!Expedition.Has(ExpeditionTech.Survey) ||
                Expedition.Map != ExpeditionMap.Industrial || !Expedition.FoundClue)) return ExpeditionState.Requirement(tech);
            if (tech == ExpeditionTech.Pulley && (!Expedition.Has(ExpeditionTech.Handcart) ||
                !Expedition.Has(ExpeditionTech.Survey))) return ExpeditionState.Requirement(tech);
            if (tech == ExpeditionTech.Bracing && (!Expedition.Has(ExpeditionTech.Fortifications) ||
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
            if (Expedition != null)
            {
                Expedition.SurveyProgress = 0;
                Expedition.GateProgress = 0;
            }
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
                string mission = MissionInteractionHint();
                if (mission.Length > 0) return mission;
                if (!Expedition.FoundClue && CanReach(Expedition.CluePosition)) return "E 获取研究线索";
                var ore = NearbyOre();
                return ore == null ? "" : $"按住 E 采集 · 剩余 {ore.Remaining}";
            }
        }

        private void StepInteraction(float dt)
        {
            if (interactionPressed)
            {
                interactionPressed = false;
                StepMissionPress();
            }
            if (!interactHeld || StepMissionHold(dt)) return;
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
            Expedition.HasAxle = Expedition.HasRope = Expedition.BeaconLit = false;
            Expedition.BeaconCare = 0;
            Expedition.BeaconWaves = Expedition.BeaconQueued = 0;
            beaconRaids.Clear();
            foreach (var site in Buildings) site.Damage(int.MaxValue);
            Enemies.Clear();
        }

        /// <summary>优先走向点击的原始坐标，受阻时才求共享网格上的绕行路线。</summary>
        private void StepCommander(float dt)
        {
            if (!commanderTarget.HasValue) return;
            var point = CommanderPosition;
            if (commanderRevision != navigation.Revision)
            {
                commanderRevision = navigation.Revision;
                var requested = commanderTarget.Value;
                commanderDestination = navigation.Walkable(BattleGrid.Cell(requested))
                    ? requested : navigation.NearestReachable(point, requested);
                commanderRoute = null;
                commanderWalker.Next = null;
            }
            float travel = Rules.MoveSpeed * dt;
            if (navigation.CanTravelStraight(point, commanderDestination))
            {
                point = Vector2.MoveTowards(point, commanderDestination, travel);
                commanderWalker.Next = null;
                if (point == commanderDestination) commanderTarget = null;
            }
            else
            {
                commanderRoute ??= navigation.Field(new RectInt(BattleGrid.Cell(commanderDestination), Vector2Int.one), 0);
                if (navigation.Distance(commanderRoute, BattleGrid.Cell(point)) < 0)
                {
                    commanderDestination = navigation.NearestReachable(point, commanderDestination);
                    commanderRoute = navigation.Field(new RectInt(BattleGrid.Cell(commanderDestination), Vector2Int.one), 0);
                    commanderWalker.Next = null;
                }
                navigation.FollowSmoothed(commanderRoute, ref point, commanderWalker, travel);
            }
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
            SpawnBeaconRaiders();
            if (time < (expeditionWave + 1) * map.EnemyInterval) return;
            expeditionWave++;
            float angle = (float)expeditionRandom.NextDouble() * 2 * Mathf.PI;
            int threat = Expedition.ThreatLevel(time);
            int fast = map.FastEnemies + (threat >= 1 ? 1 : 0);
            int total = map.StandardEnemies + fast + (threat >= 2 ? 1 : 0);
            // 余位不足时先跳过普通敌人，保留升级后的快速与攻坚压力。
            int first = Mathf.Max(0, total - (map.EnemyCap - Enemies.Count));
            for (int i = first; i < total && Enemies.Count < map.EnemyCap; i++, angle += .09f)
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
                var kind = i < map.StandardEnemies ? EnemyKind.Standard
                    : i < map.StandardEnemies + fast ? EnemyKind.Fast : EnemyKind.Siege;
                AddExpeditionEnemy(kind, BattleGrid.Center(cell), expeditionWave, expeditionWave, false);
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
            StepMission(dt);
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
