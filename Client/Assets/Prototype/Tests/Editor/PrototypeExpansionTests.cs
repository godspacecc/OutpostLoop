using NUnit.Framework;
using UnityEngine;

namespace OutpostLoop.Tests
{
    /// <summary>覆盖网格选址、生产和建筑参与战斗的边界。</summary>
    public sealed class PrototypeExpansionTests
    {
        [SetUp] public void Prepare() => Game.FoundationRuntime.Initialize();
        [TearDown] public void Close() => Game.FoundationRuntime.Shutdown();

        [Test]
        public void PlacementRejectsObstaclesAndStockWithoutSpending()
        {
            var world = EmptyMap();
            var free = new Vector2Int(-6, 2);
            Fund(world, 20, 0);
            world.Nodes.Add(new ResourceNode { Position = new Vector2(-8, 0), Remaining = 4 });
            Assert.AreEqual(PlacementIssue.InsufficientStock, world.Build(ExpansionKind.Barracks, free));
            Assert.AreEqual(PlacementIssue.Occupied, world.Build(ExpansionKind.Barricade, Vector2Int.zero));
            Assert.AreEqual(PlacementIssue.Occupied, world.Build(ExpansionKind.Barricade, new Vector2Int(0, -2)));
            Assert.AreEqual(PlacementIssue.Occupied, world.Build(ExpansionKind.Barricade, new Vector2Int(-4, 0)));
            Assert.AreEqual(PlacementIssue.Outside, world.Build(ExpansionKind.Barricade, new Vector2Int(12, 0)));
            world.Run.Paused = true;
            Assert.AreEqual(PlacementIssue.Inactive, world.Build(ExpansionKind.Barricade, free));
            Assert.AreEqual(20, world.Run.Scrap);
            world.Run.Paused = false;
            Assert.AreEqual(PlacementIssue.None, world.Build(ExpansionKind.Barricade, free, true));
            Assert.AreEqual(PlacementIssue.Occupied, world.Build(ExpansionKind.Barricade, free + Vector2Int.up));
            Assert.AreEqual(new Vector2Int(1, 2), world.Buildings[0].Footprint.size);
            Assert.AreEqual(10, world.Run.Scrap);
        }

        [Test]
        public void ExtractorBindsNearestUnclaimedNodeAndProducesOnlyAfterCompletion()
        {
            var world = EmptyMap();
            world.Nodes.Add(new ResourceNode { Position = new Vector2(-12, 8), Remaining = 3 });
            world.Nodes.Add(new ResourceNode { Position = new Vector2(-12, 12), Remaining = 3, Energy = true });
            Fund(world, 24, 0);
            Assert.AreEqual(PlacementIssue.None, world.Build(ExpansionKind.Extractor, new Vector2Int(-6, 2)));
            Assert.AreEqual(0, world.Buildings[0].NodeIndex);
            Assert.AreEqual(PlacementIssue.None, world.Build(ExpansionKind.Extractor, new Vector2Int(-8, 3)));
            Assert.AreEqual(1, world.Buildings[1].NodeIndex);
            Assert.AreEqual(PlacementIssue.NoResource, world.Build(ExpansionKind.Extractor, new Vector2Int(-5, 6)));
            Advance(world, 49);
            Assert.AreEqual(8, world.Run.Scrap);
            Assert.AreEqual(0, world.Run.Energy);
            Advance(world, 100);
            Assert.AreEqual(11, world.Run.Scrap);
            Assert.AreEqual(3, world.Run.Energy);
        }

        [Test]
        public void BarracksWaitsForOrderAndStopsTrainingWhenDestroyed()
        {
            var world = EmptyMap();
            Fund(world, 20, 3);
            var cell = new Vector2Int(0, 8);
            Assert.AreEqual(PlacementIssue.None, world.Build(ExpansionKind.Barracks, cell));
            Assert.IsFalse(world.TrainGarrison(0));
            Advance(world, 205);
            var site = world.Buildings[0];
            Assert.IsTrue(site.CanTrain);
            Assert.IsFalse(site.HasGarrison);
            Assert.IsFalse(world.TrainGarrison(0));
            Assert.AreEqual(6, world.Run.Scrap);
            Fund(world, 0, 2);
            Assert.IsTrue(world.TrainGarrison(0));
            Assert.IsFalse(world.TrainGarrison(0));
            Assert.AreEqual(0, world.Run.Scrap);
            Assert.AreEqual(0, world.Run.Energy);
            for (int i = 0; i < 15; i++)
                world.Enemies.Add(new EnemyState(EnemyKind.Siege) { Position = site.Position + Vector2.up * 3, Health = 1000 });
            Advance(world, 12);
            Assert.AreEqual(ExpansionKind.Empty, site.Kind);
            Assert.AreEqual(0, site.TrainingLeft);
            Assert.IsFalse(site.HasGarrison);
            Assert.AreEqual(-1, world.FindBuilding(cell));
        }

        [Test]
        public void WorksiteKeepsDamageAndMovingTurretPreservesHealth()
        {
            var world = EmptyMap();
            Fund(world, 16, 3);
            var initial = new Vector2Int(0, 7);
            Assert.AreEqual(PlacementIssue.None, world.Build(ExpansionKind.Turret, initial));
            var site = world.Buildings[6];
            Assert.AreEqual(45, site.Health);
            Assert.AreEqual(PlacementIssue.MissingTurret, world.MoveTurret(6, new Vector2Int(1, 8)));
            var enemy = new EnemyState(EnemyKind.Siege) { Position = site.Position + Vector2.up * 2, Health = 1000 };
            world.Enemies.Add(enemy);
            Advance(world, 11);
            world.Enemies.Clear();
            Assert.AreEqual(40, site.Health);
            Advance(world, 52);
            Assert.AreEqual(85, site.Health);
            var destination = new Vector2Int(1, 8);
            Assert.AreEqual(PlacementIssue.None, world.MoveTurret(6, destination));
            Assert.AreEqual(85, site.Health);
            Assert.AreEqual(-1, world.FindBuilding(initial));
            Assert.AreEqual(6, world.FindBuilding(destination));
            Assert.AreEqual(0, world.Run.Scrap);
        }

        [Test]
        public void ExpansionAndTurretLimitsAreIndependent()
        {
            var world = EmptyMap();
            Fund(world, 82, 6);
            for (int i = 0; i < 4; i++)
                Assert.AreEqual(PlacementIssue.None, world.Build(ExpansionKind.Barricade, new Vector2Int(i * 2 - 4, 6)));
            Assert.AreEqual(PlacementIssue.Limit, world.Build(ExpansionKind.Barricade, new Vector2Int(0, 7)));
            Assert.AreEqual(PlacementIssue.None, world.Build(ExpansionKind.Turret, new Vector2Int(0, 7)));
            Assert.AreEqual(PlacementIssue.None, world.Build(ExpansionKind.Turret, new Vector2Int(1, 7)));
            Assert.AreEqual(PlacementIssue.Limit, world.Build(ExpansionKind.Turret, new Vector2Int(2, 7)));
            Assert.AreEqual(10, world.Run.Scrap);
        }

        [Test]
        public void WorkerRoutesAroundConstructionAndDeposits()
        {
            var world = EmptyMap();
            Fund(world, 10, 0);
            var cell = new Vector2Int(-1, -4);
            Assert.AreEqual(PlacementIssue.None, world.Build(ExpansionKind.Barricade, cell));
            world.Nodes.Add(new ResourceNode { Position = new Vector2(0, -16), Remaining = 4 });
            Assert.IsTrue(world.AssignResource(0));
            for (int i = 0; i < 400 && world.Run.Scrap < 4; i++)
            {
                world.Step(.1f);
                Assert.IsFalse(world.Buildings[0].Footprint.Contains(BattleGrid.Cell(world.WorkerPosition)));
            }
            Assert.AreEqual(4, world.Run.Scrap);
        }

        [Test]
        public void SealedEnemyAttacksRoadblockAndWalksThroughTheOpenedRoute()
        {
            var world = EmptyMap();
            Fund(world, 40, 0);
            Assert.AreEqual(PlacementIssue.None, world.Build(ExpansionKind.Barricade, new Vector2Int(-1, 8), true));
            Assert.AreEqual(PlacementIssue.None, world.Build(ExpansionKind.Barricade, new Vector2Int(0, 9)));
            Assert.AreEqual(PlacementIssue.None, world.Build(ExpansionKind.Barricade, new Vector2Int(1, 7), true));
            Assert.AreEqual(PlacementIssue.None, world.Build(ExpansionKind.Barricade, new Vector2Int(-1, 7)));
            Advance(world, 33);
            var enemy = new EnemyState(EnemyKind.Siege) { Position = Vector2.up * 16, Health = 1000 };
            world.Enemies.Add(enemy);
            Advance(world, 12);
            Assert.AreEqual(145, world.Buildings[0].Health);
            Assert.AreEqual(Vector2.up * 16, enemy.Position);
            Advance(world, 300);
            Assert.AreEqual(ExpansionKind.Empty, world.Buildings[0].Kind);
            Assert.AreEqual(ExpansionKind.Barricade, world.LastDestroyedKind);
            // 缺口仍可能让敌人先接触第二块路障，最终会穿过恢复的路线。
            Advance(world, 950);
            Assert.Less(enemy.Position.y, 12);
        }

        private static WorldState EmptyMap()
        {
            var world = new WorldState(12);
            world.Nodes.Clear();
            return world;
        }

        private static void Advance(WorldState world, int steps)
        {
            for (int i = 0; i < steps; i++) world.Step(.1f);
        }

        private static void Fund(WorldState world, int scrap, int energy)
        {
            for (int i = 0; i < scrap + energy; i++)
            {
                if (world.Run.CargoScrap + world.Run.CargoEnergy == world.Run.Capacity) world.Run.Deposit();
                Assert.IsTrue(world.Run.Gather(i >= scrap));
            }
            world.Run.Deposit();
        }
    }
}
