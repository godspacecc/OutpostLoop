using System;
using System.Collections.Generic;
using UnityEngine;

namespace OutpostLoop
{
    public sealed partial class WorldState
    {
        // 工坊由旧闸门隔开制图台，两条厂外路线会合在闸前。
        private static readonly RectInt[] WorkshopTerrain =
        {
            new RectInt(-2, -2, 3, 5), new RectInt(3, -7, 3, 4),
            new RectInt(9, -3, 6, 1), new RectInt(9, 3, 6, 1), new RectInt(14, -2, 1, 5),
            new RectInt(9, -2, 1, 1), new RectInt(9, 2, 1, 1)
        };
        private static readonly Vector2Int[] WorkshopOre =
        {
            new Vector2Int(-12,-6), new Vector2Int(-12,0), new Vector2Int(-10,6), new Vector2Int(-7,-4),
            new Vector2Int(-7,4), new Vector2Int(-2,7), new Vector2Int(1,4), new Vector2Int(2,-9),
            new Vector2Int(7,-5), new Vector2Int(8,7)
        };
        // 石脊横贯战场，左右各留三格山口。两处山口都是循烟敌群的入口。
        private static readonly RectInt[] BeaconTerrain =
        {
            new RectInt(-23, 0, 17, 2), new RectInt(-3, 0, 7, 2), new RectInt(7, 0, 17, 2),
            new RectInt(-10, 6, 4, 3), new RectInt(7, 6, 4, 3)
        };
        private static readonly Vector2Int[] BeaconOre =
        {
            new Vector2Int(-6,-10), new Vector2Int(-4,-5), new Vector2Int(-1,-12), new Vector2Int(1,-4),
            new Vector2Int(4,-9), new Vector2Int(7,-6), new Vector2Int(-11,-5), new Vector2Int(11,-4),
            new Vector2Int(-5,3), new Vector2Int(5,3), new Vector2Int(-4,9), new Vector2Int(5,11)
        };
        private readonly List<RectInt> expeditionTerrain = new List<RectInt>();
        private RectInt? workshopGate;
        private readonly Vector2Int[] beaconEntrances = new Vector2Int[2];
        public IReadOnlyList<RectInt> ExpeditionTerrain => expeditionTerrain;
        public RectInt? ClosedWorkshopGate => Expedition?.BasicComplete == false ? workshopGate : null;

        /// <summary>用种子翻转地形和目标方向，在每张地图的安全区抽取出生格。</summary>
        private void GenerateExpeditionMap(System.Random random)
        {
            var wildernessSpawn = Expedition.Map == ExpeditionMap.Wilderness
                ? new Vector2Int(random.Next(-7, 8), random.Next(-7, 8)) : default;
            int horizontal = random.Next(2) == 0 ? -1 : 1;
            int vertical = random.Next(2) == 0 ? -1 : 1;
            Vector2Int Mirror(int x, int y) => new Vector2Int(x * horizontal, y * vertical);
            Vector2 Point(int x, int y) => BattleGrid.Center(Mirror(x, y));
            RectInt MirrorRect(RectInt rect)
            {
                var start = Mirror(rect.xMin, rect.yMin);
                var end = Mirror(rect.xMax - 1, rect.yMax - 1);
                return new RectInt(Vector2Int.Min(start, end), rect.size);
            }
            void AddTerrain(RectInt[] terrain)
            {
                foreach (var rect in terrain) expeditionTerrain.Add(MirrorRect(rect));
            }
            void AddNodes(Vector2Int[] cells)
            {
                foreach (var cell in cells)
                    Nodes.Add(new ResourceNode { Position = Point(cell.x, cell.y), Remaining = 30 });
            }

            switch (Expedition.Map)
            {
                case ExpeditionMap.Industrial:
                    CommanderPosition = Point(random.Next(-13, -8), random.Next(-4, 5));
                    AddTerrain(WorkshopTerrain);
                    AddNodes(WorkshopOre);
                    workshopGate = MirrorRect(new RectInt(9, -1, 1, 3));
                    Expedition.WorkshopAxle = Point(-5, -9);
                    Expedition.WorkshopRope = Point(4, 8);
                    Expedition.ObjectivePosition = Point(8, 0);
                    Expedition.CluePosition = Point(-5, 8);
                    Expedition.SurveyPosition = Point(12, 0);
                    break;
                case ExpeditionMap.Beacon:
                    CommanderPosition = Point(random.Next(-4, 5), random.Next(-9, -5));
                    AddTerrain(BeaconTerrain);
                    AddNodes(BeaconOre);
                    Expedition.ObjectivePosition = Point(0, 5);
                    Expedition.CluePosition = Point(7, 9);
                    Expedition.SurveyPosition = Point(0, 14);
                    beaconEntrances[0] = Mirror(-5, -4);
                    beaconEntrances[1] = Mirror(5, -4);
                    break;
                default:
                    CommanderPosition = BattleGrid.Center(wildernessSpawn);
                    expeditionTerrain.Add(new RectInt(-20, -2, 2, 4));
                    expeditionTerrain.Add(new RectInt(19, -2, 2, 4));
                    foreach (var offset in OreOffsets)
                        Nodes.Add(new ResourceNode { Position = CommanderPosition + BattleGrid.Center(offset), Remaining = 30 });
                    Expedition.CluePosition = CommanderPosition + Point(5, 6);
                    Expedition.SurveyPosition = CommanderPosition + Point(-8, -7);
                    break;
            }
            RefreshNavigation();
            ValidateExpeditionLayout();
        }

        /// <summary>检查固定地形经种子变换后仍满足近处采集、庇护所与任务入口约束。</summary>
        private void ValidateExpeditionLayout()
        {
            var spawn = BattleGrid.Cell(CommanderPosition);
            var field = navigation.Field(new RectInt(spawn, Vector2Int.one), 0);
            int nearby = Nodes.FindAll(node => (node.Position - CommanderPosition).sqrMagnitude <= 144 &&
                navigation.Distance(field, BattleGrid.Cell(node.Position)) >= 0).Count;
            if (nearby < 2 || !CanReachExpeditionPosition(field, Expedition.CluePosition) ||
                !CanReachMission(field, includeTerminal: Expedition.Map != ExpeditionMap.Industrial))
                throw new InvalidOperationException("探索地图缺少可达资源或任务入口");
            if (Expedition.Map == ExpeditionMap.Industrial)
            {
                if (CanReachExpeditionPosition(field, Expedition.SurveyPosition))
                    throw new InvalidOperationException("旧工坊制图台可绕过闸门抵达");
                navigation.Rebuild(Buildings, Headquarters, expeditionTerrain);
                var opened = navigation.Field(new RectInt(spawn, Vector2Int.one), 0);
                RefreshNavigation();
                if (!CanReachExpeditionPosition(opened, Expedition.SurveyPosition))
                    throw new InvalidOperationException("旧工坊开闸后制图台仍不可达");
            }
            var candidates = new RectInt(spawn - Vector2Int.one * 4, Vector2Int.one * 8);
            foreach (var cell in candidates.allPositionsWithin)
            {
                var shelter = new RectInt(cell, new Vector2Int(2, 2));
                if (shelter.Contains(spawn) || MineralOccupies(shelter) || !TerrainAllows(shelter)) continue;
                var preview = navigation.Field(new RectInt(spawn, Vector2Int.one), 0, shelter);
                if (CanReachProtectedPositions(preview)) return;
            }
            throw new InvalidOperationException("探索地图缺少合法庇护所用地");
        }

        private bool TerrainAllows(RectInt footprint)
        {
            foreach (var cell in footprint.allPositionsWithin)
                if (!navigation.Walkable(cell) || workshopGate?.Contains(cell) == true) return false;
            if (Expedition.Map == ExpeditionMap.Beacon &&
                (footprint.Contains(beaconEntrances[0]) || footprint.Contains(beaconEntrances[1]))) return false;
            return !footprint.Contains(BattleGrid.Cell(Expedition.CluePosition)) &&
                !footprint.Contains(BattleGrid.Cell(Expedition.SurveyPosition)) &&
                (Expedition.Map == ExpeditionMap.Wilderness ||
                    !footprint.Contains(BattleGrid.Cell(Expedition.ObjectivePosition))) &&
                (Expedition.Map != ExpeditionMap.Industrial || (!footprint.Contains(BattleGrid.Cell(Expedition.WorkshopAxle)) &&
                    !footprint.Contains(BattleGrid.Cell(Expedition.WorkshopRope))));
        }

        private bool CanReachExpeditionPosition(int[] field, Vector2 point)
        {
            var cell = BattleGrid.Cell(point);
            var ring = new RectInt(cell - Vector2Int.one, Vector2Int.one * 3);
            foreach (var neighbor in ring.allPositionsWithin)
                if ((BattleGrid.Center(neighbor) - point).sqrMagnitude <= 4 && navigation.Distance(field, neighbor) >= 0)
                    return true;
            return false;
        }

        private bool CanReachMission(int[] field, bool includeTerminal)
        {
            if (includeTerminal && !CanReachExpeditionPosition(field, Expedition.SurveyPosition)) return false;
            if (Expedition.Map != ExpeditionMap.Wilderness && !CanReachExpeditionPosition(field, Expedition.ObjectivePosition))
                return false;
            return Expedition.Map != ExpeditionMap.Industrial ||
                (CanReachExpeditionPosition(field, Expedition.WorkshopAxle) &&
                    CanReachExpeditionPosition(field, Expedition.WorkshopRope));
        }

        private bool CanReachProtectedPositions(int[] field)
        {
            foreach (var node in Nodes)
                if (node.Remaining > 0 && !CanReachExpeditionPosition(field, node.Position)) return false;
            return CanReachExpeditionPosition(field, Expedition.CluePosition) &&
                CanReachMission(field, Expedition.Map != ExpeditionMap.Industrial || Expedition.BasicComplete);
        }
    }
}
