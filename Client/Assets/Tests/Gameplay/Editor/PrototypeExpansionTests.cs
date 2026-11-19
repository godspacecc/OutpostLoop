using NUnit.Framework;
using UnityEngine;
using static OutpostLoop.Tests.PrototypeWorldTests;

namespace OutpostLoop.Tests
{
    /// <summary>施工、占地、采运和人员生产的规则边界。</summary>
    public sealed class PrototypeExpansionTests
    {
        [SetUp] public void Prepare() => Game.FoundationRuntime.Initialize();
        [TearDown] public void Close() => Game.FoundationRuntime.Shutdown();

        [Test]
        public void RejectsOverlapsAndShortfallWithoutSpending()
        {
            var world = WithBase(12, funds: 100);
            world.Nodes.Clear();
            var free = new Vector2Int(-6, 3);
            var mine = new Vector2Int(-2, 4);
            world.Nodes.Add(new ResourceNode { Position = BattleGrid.Center(mine), Remaining = 480 });
            Assert.AreEqual(PlacementIssue.InsufficientStock, world.Build(ExpansionKind.Barracks, free));
            Assert.AreEqual(PlacementIssue.Occupied, world.Build(ExpansionKind.Barricade, world.Headquarters.Value.min));
            Assert.AreEqual(PlacementIssue.Occupied, world.Build(ExpansionKind.Barricade, mine));
            Assert.AreEqual(PlacementIssue.NotVisible, world.Build(ExpansionKind.Barricade, new Vector2Int(12, 0)));
            world.Run.Paused = true;
            Assert.AreEqual(PlacementIssue.Inactive, world.Build(ExpansionKind.Barricade, free));
            Assert.AreEqual(100, world.Run.Funds);
            world.Run.Paused = false;
            Assert.AreEqual(PlacementIssue.None, world.Build(ExpansionKind.Barricade, free, true));
            Assert.AreEqual(new Vector2Int(1, 2), world.Buildings[0].Footprint.size);
            Assert.AreEqual(0, world.Run.Funds);
        }

        [Test]
        public void MiningRequiresAWorkerAndCreditsOnlyOnDelivery()
        {
            var world = WithBase(12);
            world.Nodes.Clear();
            world.Nodes.Add(new ResourceNode { Position = new Vector2(-8, 12), Remaining = 40 });
            world.Nodes.Add(new ResourceNode { Position = new Vector2(-12, 14), Remaining = 40 });
            int station = BuildNear(world, ExpansionKind.Extractor, new Vector2(-8, 6));
            var site = world.Buildings[station];
            Assert.AreEqual(0, site.NodeIndex);
            Assert.IsFalse(world.Produce(station));
            Advance(world, 8);
            Assert.AreEqual(450, world.Run.Funds);
            Assert.AreEqual(40, world.Nodes[0].Remaining);
            Assert.IsTrue(world.Produce(station));
            Assert.IsFalse(world.Produce(station));
            for (int step = 0; step < 250 && (site.Personnel?.Cargo ?? 0) == 0; step++) world.Step(.1f);
            Assert.Greater(site.Personnel.Cargo, 0);
            Assert.AreEqual(300, world.Run.Funds);
            Advance(world, 20);
            Assert.AreEqual(380, world.Run.Funds);
            Assert.AreEqual(-1, site.NodeIndex);
            Assert.AreEqual(0, site.Personnel.Cargo);
            Assert.IsFalse(world.Produce(station));
        }

        [Test]
        public void ProductionWaitsForExitAndDeploysWithoutAnotherCharge()
        {
            var world = WithBase(12, funds: 1400);
            world.Nodes.Clear();
            world.Nodes.Add(new ResourceNode { Position = new Vector2(-8, 18), Remaining = 480 });
            Assert.AreEqual(PlacementIssue.None, world.Build(ExpansionKind.Extractor, new Vector2Int(-4, 5)));
            Assert.AreEqual(PlacementIssue.None, world.Build(ExpansionKind.Barricade, new Vector2Int(-4, 4)));
            Assert.AreEqual(PlacementIssue.None, world.Build(ExpansionKind.Barricade, new Vector2Int(-4, 7)));
            Assert.AreEqual(PlacementIssue.None, world.Build(ExpansionKind.Barricade, new Vector2Int(-5, 5), true));
            Assert.AreEqual(PlacementIssue.None, world.Build(ExpansionKind.Turret, new Vector2Int(-2, 5)));
            Assert.AreEqual(PlacementIssue.None, world.Build(ExpansionKind.Turret, new Vector2Int(-2, 6)));
            Advance(world, 6.2f);
            Assert.IsTrue(world.Produce(0));
            Advance(world, 7);
            Assert.IsTrue(world.Buildings[0].PersonnelWaiting);
            Assert.IsNull(world.Buildings[0].Personnel);
            Assert.IsFalse(world.Produce(0));
            Assert.AreEqual(0, world.Run.Funds);
            Assert.AreEqual(PlacementIssue.None, world.MoveTurret(4, new Vector2Int(-1, 5)));
            Advance(world, .1f);
            Assert.IsFalse(world.Buildings[0].PersonnelWaiting);
            Assert.IsNotNull(world.Buildings[0].Personnel);
            Assert.AreEqual(0, world.Run.Funds);
        }

        [Test]
        public void BarracksProducesOnlyAfterPaidOrderAndCanDeploy()
        {
            var world = WithBase(12);
            var index = BuildNear(world, ExpansionKind.Barracks, world.BasePosition + Vector2.up * 7);
            var site = world.Buildings[index];
            Advance(world, 9);
            Assert.IsNull(site.Personnel);
            Assert.IsFalse(world.DeploySquad(index, 2));
            Assert.IsTrue(world.Produce(index));
            Assert.AreEqual(400, world.Run.Funds);
            world.Run.Paused = true;
            Advance(world, 2);
            Assert.AreEqual(12, site.ProductionLeft);
            world.Run.Paused = false;
            Advance(world, 12.2f);
            Assert.IsNotNull(site.Personnel);
            Assert.IsFalse(world.Produce(index));
            Assert.IsTrue(world.DeploySquad(index, 2));
            Advance(world, 10);
            Assert.AreEqual(world.BasePosition + Vector2.up * 12, site.Personnel.Position);
        }

        [Test]
        public void BarracksDestructionCancelsOrderAndFreesFootprint()
        {
            var world = WithBase(12);
            var index = BuildNear(world, ExpansionKind.Barracks, world.BasePosition + Vector2.up * 13);
            var site = world.Buildings[index];
            Advance(world, 8.1f);
            Assert.IsTrue(world.Produce(index));
            var location = site.Footprint.min;
            for (int i = 0; i < 15; i++)
                world.Enemies.Add(new EnemyState(EnemyKind.Siege) { Position = site.Position + Vector2.up * 3, Health = 1000 });
            Advance(world, 1.2f);
            Assert.AreEqual(-1, world.FindBuilding(location));
            Assert.AreEqual(0, site.ProductionLeft);
            Assert.AreEqual(400, world.Run.Funds);
            Assert.IsNull(site.Personnel);
        }

        [Test]
        public void TurretMigrationPreservesDamageAndIsFree()
        {
            var world = WithBase(12);
            var index = BuildNear(world, ExpansionKind.Turret, world.BasePosition + Vector2.up * 15);
            var site = world.Buildings[index];
            Assert.AreEqual(45, site.Health);
            Assert.AreEqual(PlacementIssue.MissingTurret, world.MoveTurret(index, new Vector2Int(0, 6)));
            world.Enemies.Add(new EnemyState(EnemyKind.Siege) { Position = site.Position + Vector2.up * 2, Health = 1000 });
            Advance(world, 1.1f);
            world.Enemies.Clear();
            Advance(world, 5.2f);
            Assert.AreEqual(85, site.Health);
            var destination = FindPlacement(world, ExpansionKind.Turret, world.BasePosition + Vector2.right * 12, index);
            Assert.AreEqual(PlacementIssue.None, world.MoveTurret(index, destination));
            Assert.AreEqual(85, site.Health);
            Assert.AreEqual(index, world.FindBuilding(destination));
            Assert.AreEqual(500, world.Run.Funds);
        }

        [Test]
        public void FacilitiesAndTurretsHaveSeparateLimits()
        {
            var world = WithBase(12, funds: 2000);
            for (int i = 0; i < 4; i++) BuildNear(world, ExpansionKind.Barricade, world.BasePosition + Vector2.up * 10);
            for (int i = 0; i < 4; i++) BuildNear(world, ExpansionKind.Turret, world.BasePosition + Vector2.down * 10);
            Assert.AreEqual(400, world.Run.Funds);
            var cell = new Vector2Int(-4, -7);
            world.Nodes.Clear();
            Assert.AreEqual(PlacementIssue.Limit, world.Build(ExpansionKind.Barricade, cell));
            Assert.AreEqual(PlacementIssue.Limit, world.Build(ExpansionKind.Turret, cell));
        }

        [Test]
        public void SealedEnemyAttacksRoadblockAndTakesTheOpenedRoute()
        {
            var world = WithBase(12);
            world.Nodes.Clear();
            Assert.AreEqual(PlacementIssue.None, world.Build(ExpansionKind.Barricade, new Vector2Int(-1, 7), true));
            Assert.AreEqual(PlacementIssue.None, world.Build(ExpansionKind.Barricade, new Vector2Int(0, 8)));
            Assert.AreEqual(PlacementIssue.None, world.Build(ExpansionKind.Barricade, new Vector2Int(1, 6), true));
            Assert.AreEqual(PlacementIssue.None, world.Build(ExpansionKind.Barricade, new Vector2Int(-1, 6)));
            Advance(world, 3.3f);
            var enemy = new EnemyState(EnemyKind.Siege) { Position = Vector2.up * 14, Health = 1000 };
            world.Enemies.Add(enemy);
            Advance(world, 1.2f);
            Assert.AreEqual(145, world.Buildings[0].Health);
            Assert.AreEqual(Vector2.up * 14, enemy.Position);
            Advance(world, 30);
            Assert.AreEqual(ExpansionKind.Empty, world.Buildings[0].Kind);
            Assert.AreEqual(ExpansionKind.Barricade, world.LastDestroyedKind);
            Advance(world, 95);
            Assert.Less(enemy.Position.y, 10);
        }
    }
}
