using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace OutpostLoop.Tests
{
    /// <summary>指挥员开局、动态主基地和无总时限的波次规则。</summary>
    public sealed class PrototypeWorldTests
    {
        [SetUp] public void Prepare() => Game.FoundationRuntime.Initialize();
        [TearDown] public void Close() => Game.FoundationRuntime.Shutdown();

        [Test]
        public void CommanderAloneCanChooseEitherBaseLocation()
        {
            var left = new WorldState(732);
            var right = new WorldState(732);
            Assert.AreEqual(36, left.Nodes.Count);
            CollectionAssert.AreEqual(left.Nodes.Select(node => node.Position), right.Nodes.Select(node => node.Position));
            Assert.AreNotEqual(left.Nodes[0].Position, new WorldState(733).Nodes[0].Position);
            Assert.IsNull(left.Headquarters);
            Assert.IsTrue(left.Buildings.All(site => site.Kind == ExpansionKind.Empty));
            Assert.AreEqual(800, left.Run.Funds);
            Assert.AreEqual(0, left.Run.Health);
            Advance(left, 150);
            Assert.AreEqual(0, left.Run.Elapsed);
            Assert.AreEqual(0, left.Wave);
            Assert.AreEqual(PlacementIssue.Inactive, left.Build(ExpansionKind.Turret, new Vector2Int(1, 0)));
            Assert.AreEqual(PlacementIssue.Occupied, left.Build(ExpansionKind.Headquarters, Vector2Int.zero));
            Assert.AreEqual(PlacementIssue.CommanderTooFar, left.Build(ExpansionKind.Headquarters, new Vector2Int(-6, -2)));
            BuildBase(left, -4);
            BuildBase(right, 4);
            Assert.AreEqual(new Vector2(-8, 0), left.BasePosition);
            Assert.AreEqual(new Vector2(8, 0), right.BasePosition);
            Assert.AreEqual(100, left.Run.Health);
            Assert.AreEqual(800, left.Run.Funds);
            Assert.AreEqual(PlacementIssue.Limit, left.Build(ExpansionKind.Headquarters, new Vector2Int(4, 0)));
            Assert.AreEqual(PlacementIssue.NotVisible, left.PreviewPlacement(ExpansionKind.Turret, new Vector2Int(9, 0), false, -1, out _, out _));
            Assert.AreNotEqual(PlacementIssue.NotVisible, right.PreviewPlacement(ExpansionKind.Turret, new Vector2Int(9, 0), false, -1, out _, out _));
        }

        [Test]
        public void CommanderMovesBeforeBaseAndPauses()
        {
            var world = new WorldState(732);
            var firstTarget = new Vector2(3.4f, -4.6f);
            Assert.IsTrue(world.MoveCommander(firstTarget));
            Advance(world, .5f);
            var expected = firstTarget.normalized * (world.Run.Config.MoveSpeed * .5f);
            Assert.Less(Vector2.Distance(expected, world.CommanderPosition), .0001f, "斜向移动应沿直线");
            world.Run.Paused = true;
            var location = world.CommanderPosition;
            Assert.IsFalse(world.MoveCommander(Vector2.zero));
            Advance(world, 1);
            Assert.AreEqual(location, world.CommanderPosition);
            world.Run.Paused = false;
            var secondTarget = new Vector2(.35f, -7.65f);
            Assert.IsTrue(world.MoveCommander(secondTarget));
            world.Step(.1f);
            expected = Vector2.MoveTowards(location, secondTarget, world.Run.Config.MoveSpeed * .1f);
            Assert.AreEqual(expected, world.CommanderPosition, "改派从当前位置直接转向");
            Advance(world, 2);
            Assert.AreEqual(secondTarget, world.CommanderPosition, "终点不吸附格心");
            BuildBase(world, 4);
            Assert.IsFalse(world.MoveCommander(Vector2.zero));

            var boundary = new WorldState(732);
            Assert.IsTrue(boundary.MoveCommander(Vector2.right * 100));
            Advance(boundary, 12);
            Assert.AreEqual(Vector2.right * boundary.Run.Config.MapRadius, boundary.CommanderPosition);
        }

        [Test]
        public void EnemiesWaitForAvailableCapacity()
        {
            var world = WithBase(12);
            for (int i = 0; i < world.Run.Config.MaxEnemies; i++)
                world.Enemies.Add(new EnemyState { Position = Vector2.up * 42, Health = 1000 });
            // 逐步推进之前保持敌人远离主基地，只验证排队与补位。
            for (int i = 0; i < 1201; i++)
            {
                foreach (var enemy in world.Enemies) enemy.Position = Vector2.up * 42;
                world.Step(.1f);
            }
            Assert.AreEqual(10, world.WaitingEnemies);
            world.Enemies.RemoveRange(0, 5);
            world.Step(.1f);
            Assert.AreEqual(48, world.Enemies.Count);
            Assert.AreEqual(5, world.WaitingEnemies);
        }

        [Test]
        public void FourthWaveSendsOppositeLaneFiveSecondsLater()
        {
            var world = WithBase(11);
            while (world.Wave < 4)
            {
                world.Enemies.Clear();
                world.Step(.1f);
            }
            Assert.AreEqual(14, world.Enemies.Count);
            Assert.AreEqual(8, world.WaitingEnemies);
            var main = world.Enemies.Aggregate(Vector2.zero, (sum, enemy) => sum + enemy.Position);
            world.Enemies.Clear();
            Advance(world, 4.8f);
            Assert.IsEmpty(world.Enemies);
            Advance(world, .3f);
            Assert.AreEqual(8, world.Enemies.Count);
            var secondary = world.Enemies.Aggregate(Vector2.zero, (sum, enemy) => sum + enemy.Position);
            Assert.Less(Vector2.Dot(main.normalized, secondary.normalized), -.8f);
        }

        [Test]
        public void FinalWaveEndsOnlyAfterBothLanesAndAllEnemiesAreCleared()
        {
            var world = WithBase(12);
            while (world.Wave < 6)
            {
                world.Enemies.Clear();
                world.Step(.1f);
            }
            world.Enemies.Clear();
            world.Step(.1f);
            Assert.AreEqual(0, world.Run.Outcome, "次路仍在等待，不能提前获胜");
            Advance(world, 6);
            Assert.AreEqual(0, world.WaitingEnemies);
            Assert.IsNotEmpty(world.Enemies);
            while (world.Run.Elapsed <= 721)
            {
                foreach (var enemy in world.Enemies) enemy.Position = Vector2.up * 42;
                world.Step(.1f);
            }
            Assert.AreEqual(0, world.Run.Outcome, "超过旧版时限仍继续");
            world.Enemies.Clear();
            world.Step(.1f);
            Assert.AreEqual(1, world.Run.Outcome);
        }

        [Test]
        public void EnemiesAimAtThePlacedBaseAndLossIsImmediate()
        {
            var world = WithBase(12, 4);
            var enemy = new EnemyState { Position = new Vector2(14, 0), Health = 1000 };
            world.Enemies.Add(enemy);
            Advance(world, 5);
            Assert.Less(enemy.Position.x, 14);
            Assert.Less(world.Run.Health, 100);
            world.Run.Damage(world.Run.Health);
            Assert.AreEqual(-1, world.Run.Outcome);
        }

        [Test]
        public void LargeFrameCannotJumpOverOpening()
        {
            var world = WithBase(12);
            world.Step(120);
            Assert.LessOrEqual(world.Run.Elapsed, .101f);
            Assert.AreEqual(0, world.Wave);
        }

        internal static WorldState WithBase(int seed, int cellX = -4, int funds = 800)
        {
            var world = new WorldState(seed, startingFunds: funds);
            BuildBase(world, cellX);
            return world;
        }

        internal static void BuildBase(WorldState world, int cellX)
        {
            Assert.AreEqual(PlacementIssue.None, world.Build(ExpansionKind.Headquarters, new Vector2Int(cellX, 0)));
        }

        internal static void Advance(WorldState world, float seconds)
        {
            for (int i = 0; i < Mathf.RoundToInt(seconds * 10); i++) world.Step(.1f);
        }

        internal static Vector2Int FindPlacement(WorldState world, ExpansionKind kind, Vector2 preferred, int moving = -1)
        {
            float best = float.PositiveInfinity;
            var target = Vector2Int.zero;
            var center = BattleGrid.Cell(world.BasePosition);
            for (int x = center.x - 12; x <= center.x + 12; x++)
            for (int y = center.y - 12; y <= center.y + 12; y++)
            {
                var cell = new Vector2Int(x, y);
                float distance = (BattleGrid.Center(cell) - preferred).sqrMagnitude;
                if (distance >= best || world.PreviewPlacement(kind, cell, false, moving, out _, out _) != PlacementIssue.None) continue;
                target = cell;
                best = distance;
            }
            Assert.IsFalse(float.IsPositiveInfinity(best), kind + " 无合法落位");
            return target;
        }

        internal static int BuildNear(WorldState world, ExpansionKind kind, Vector2 preferred)
        {
            var target = FindPlacement(world, kind, preferred);
            Assert.AreEqual(PlacementIssue.None, world.Build(kind, target));
            return world.FindBuilding(target);
        }
    }
}
