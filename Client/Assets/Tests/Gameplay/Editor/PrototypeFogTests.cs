using NUnit.Framework;
using UnityEngine;
using static OutpostLoop.Tests.PrototypeWorldTests;

namespace OutpostLoop.Tests
{
    /// <summary>校验探索记忆、落位视野和来敌发现时机。</summary>
    public sealed class PrototypeFogTests
    {
        [SetUp] public void Prepare() => Game.FoundationRuntime.Initialize();
        [TearDown] public void Close() => Game.FoundationRuntime.Shutdown();

        [Test]
        public void CommanderExploresAndRemembersOreUntilItIsSeenAgain()
        {
            var world = new WorldState(732);
            world.Nodes.Clear();
            var node = new ResourceNode { Position = Vector2.up * 24, Remaining = 480 };
            world.Nodes.Add(node);
            int span = world.Vision.Span;
            Assert.AreEqual(FogCell.Unexplored, world.Vision.At(span, span + 24));
            Assert.AreEqual(-1, world.KnownOreRemaining(0));

            Assert.IsTrue(world.MoveCommander(node.Position));
            Advance(world, 10);
            Assert.AreEqual(FogCell.Visible, world.Vision.At(span, span + 24));
            Assert.AreEqual(480, world.KnownOreRemaining(0));
            Assert.AreEqual(0, world.Run.Elapsed);

            Assert.IsTrue(world.MoveCommander(Vector2.zero));
            Advance(world, 10);
            node.Remaining = 140;
            Assert.AreEqual(FogCell.Remembered, world.Vision.At(span, span + 24));
            Assert.AreEqual(480, world.KnownOreRemaining(0));

            Assert.IsTrue(world.MoveCommander(node.Position));
            Advance(world, 10);
            Assert.AreEqual(140, world.KnownOreRemaining(0));
            Assert.AreEqual(FogCell.Unexplored, new WorldState(732).Vision.At(span, span + 24));
        }

        [Test]
        public void HeadquartersRequiresDiscoveredOreAndRejectsHiddenFootprint()
        {
            var world = new WorldState(732);
            world.Nodes.Clear();
            world.Nodes.Add(new ResourceNode { Position = Vector2.right * 20, Remaining = 480 });
            var baseCell = new Vector2Int(4, 0);
            Assert.AreEqual(PlacementIssue.NoResource, world.Build(ExpansionKind.Headquarters, baseCell));
            Assert.AreEqual(PlacementIssue.NotVisible, world.Build(ExpansionKind.Headquarters, new Vector2Int(0, 10)));
            Assert.IsTrue(world.MoveCommander(Vector2.right * 6));
            Advance(world, 3);
            Assert.IsTrue(world.MoveCommander(Vector2.zero));
            Advance(world, 3);
            Assert.AreEqual(480, world.KnownOreRemaining(0));
            Assert.AreEqual(PlacementIssue.None, world.Build(ExpansionKind.Headquarters, baseCell));
            Assert.AreEqual(800, world.Run.Funds);
        }

        [Test]
        public void WorksitesDoNotRevealAndContinueAfterTurretMovesAway()
        {
            var world = WithBase(732);
            world.Nodes.Clear();
            var turretCell = new Vector2Int(-4, 8);
            var wallCell = new Vector2Int(-4, 11);
            var outside = new Vector2Int(-4, 13);
            var destination = new Vector2Int(-4, -7);
            Assert.AreEqual(PlacementIssue.None, world.Build(ExpansionKind.Turret, turretCell));
            int turret = world.FindBuilding(turretCell);
            Assert.AreEqual(PlacementIssue.NotVisible, world.Build(ExpansionKind.Barricade, wallCell));
            Assert.AreEqual(500, world.Run.Funds);
            Advance(world, 6.2f);
            Assert.AreEqual(PlacementIssue.Outside, world.Build(ExpansionKind.Turret, outside));
            Assert.AreEqual(PlacementIssue.None, world.Build(ExpansionKind.Barricade, wallCell));
            Assert.AreEqual(PlacementIssue.None, world.MoveTurret(turret, destination));
            Assert.IsFalse(world.Vision.IsVisible(BattleGrid.Center(wallCell)));
            Assert.IsTrue(world.Vision.IsExplored(BattleGrid.Center(wallCell)));
            Assert.AreEqual(PlacementIssue.NotVisible, world.MoveTurret(turret, turretCell + Vector2Int.up * 3));
            Advance(world, 3.2f);
            Assert.IsFalse(world.Buildings[world.FindBuilding(wallCell)].IsUnderConstruction);
            Assert.IsFalse(world.Vision.IsVisible(BattleGrid.Center(wallCell)));
            Assert.AreEqual(400, world.Run.Funds);
        }

        [Test]
        public void WaveAndEachLaneAreRevealedOnlyOnContact()
        {
            var world = WithBase(732);
            var main = new EnemyState(EnemyKind.Standard, 4, 6) { Health = 1000, Position = Vector2.up * 41 };
            var second = new EnemyState(EnemyKind.Fast, 4, 7) { Health = 1000, Position = Vector2.down * 41 };
            world.Enemies.Add(main);
            world.Enemies.Add(second);
            world.Step(.1f);
            Assert.AreEqual(0, world.DiscoveredWave);
            Assert.IsEmpty(world.Sightings);

            main.Position = world.BasePosition + Vector2.up * 14;
            world.Step(.1f);
            Assert.AreEqual(4, world.DiscoveredWave);
            Assert.AreEqual(1, world.Sightings.Count);
            Assert.AreEqual(2, world.Sightings[0].Sector);

            main.Position = Vector2.up * 41;
            second.Position = world.BasePosition + Vector2.down * 14;
            world.Step(.1f);
            Assert.IsFalse(world.Vision.IsVisible(main.Position));
            Assert.IsTrue(world.Vision.IsVisible(second.Position));
            Assert.AreEqual(2, world.Sightings.Count);
            Assert.AreEqual(3, world.Sightings[1].Sector);

            main.Position = world.BasePosition + Vector2.up * 14;
            world.Step(.1f);
            Assert.AreEqual(2, world.Sightings.Count);
        }
    }
}
