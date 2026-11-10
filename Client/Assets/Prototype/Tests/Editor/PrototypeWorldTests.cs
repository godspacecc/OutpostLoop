using NUnit.Framework;
using UnityEngine;
namespace OutpostLoop.Tests
{
    /// <summary>地图与战斗的限定验证，包含完整局时模拟和敌波容量边界。</summary>
    public class PrototypeWorldTests
    {
        [TearDown] public void CloseFoundation() { Game.FoundationRuntime.Shutdown(); }

        [SetUp] public void PrepareFoundation() { Game.FoundationRuntime.Initialize(); }

        [Test] public void EnemiesWaitForAvailableCapacity()
        {
            var world = new WorldState(12);
            var config = world.Run.Config;
            for (int i = 0; i < config.MaxEnemies; i++)
                world.Enemies.Add(new EnemyState { Position = Vector2.up * 42, Health = 1000 });
            world.Run.Tick(config.WaveTimes[0], 0);
            world.Step(.01f);
            Assert.AreEqual(10, world.WaitingEnemies);
            world.Enemies.RemoveRange(0, 5);
            world.Step(.01f);
            Assert.AreEqual(config.MaxEnemies, world.Enemies.Count);
            Assert.AreEqual(5, world.WaitingEnemies);
        }
        [Test] public void SeedProducesBoundedRepeatableMap()
        {
            var a = new WorldState(41); var b = new WorldState(41); var c = new WorldState(42);
            Assert.AreEqual(36, a.Nodes.Count); Assert.AreEqual(a.Nodes[5].Position, b.Nodes[5].Position);
            Assert.AreNotEqual(a.Nodes[5].Position, c.Nodes[5].Position);
            foreach (var node in a.Nodes) { Assert.LessOrEqual(node.Position.magnitude, 47); Assert.Greater(node.Position.magnitude, 5); }
            foreach (var node in a.Nodes)
            {
                var cell = BattleGrid.Cell(node.Position);
                Assert.IsFalse(BattleGrid.Base.Contains(cell));
                Assert.AreEqual(-1, a.FindBuilding(cell));
            }
        }
        [Test] public void WorkerOrdersRespectPauseAndRejectInvalidTargets()
        {
            var w = new WorldState(1);
            Assert.IsFalse(w.AssignResource(-1));
            Assert.IsFalse(w.AssignResource(w.Nodes.Count));
            Assert.IsTrue(w.AssignResource(0));
            w.Step(.1f);
            var position = w.WorkerPosition;
            w.Run.Paused = true;
            Assert.IsFalse(w.AssignResource(1));
            Assert.IsFalse(w.DeploySquad(0));
            w.Step(1);
            Assert.AreNotEqual(Vector2.zero, position);
            Assert.AreEqual(position, w.WorkerPosition);
            Assert.AreEqual(0, w.ResourceOrder);
        }
        [Test] public void WorkerRepeatsTripsAndDepositsFinalPartialLoad()
        {
            var w = new WorldState(1);
            var node = w.Nodes[1];
            int stock = node.Remaining;
            Assert.Greater(stock, w.Run.Capacity);
            Assert.IsTrue(w.AssignResource(1));
            for (int i = 0; i < 400 && w.Run.Scrap == 0; i++) w.Step(.1f);
            Assert.AreEqual(w.Run.Capacity, w.Run.Scrap);
            Assert.AreEqual(1, w.ResourceOrder, "满载卸货后应自动继续原任务");
            for (int i = 0; i < 400 && w.Run.Scrap < stock; i++) w.Step(.1f);
            Assert.AreEqual(stock, w.Run.Scrap);
            Assert.AreEqual(0, w.Run.CargoScrap);
            Assert.AreEqual(0, node.Remaining);
            Assert.AreEqual(-1, w.ResourceOrder);
            Assert.IsFalse(w.WorkerReturning);
            Assert.IsFalse(w.AssignResource(1));
        }
        [Test] public void ReassigningLoadedWorkerDepositsBeforeVisitingNewNode()
        {
            var w = new WorldState(1);
            Assert.IsTrue(w.AssignResource(1));
            for (int i = 0; i < 400 && w.Run.CargoScrap == 0; i++) w.Step(.1f);
            int cargo = w.Run.CargoScrap;
            Assert.Greater(cargo, 0);
            Assert.IsTrue(w.AssignResource(0));
            Assert.IsTrue(w.WorkerReturning);
            for (int i = 0; i < 400 && w.WorkerReturning; i++) w.Step(.1f);
            Assert.AreEqual(cargo, w.Run.Scrap);
            Assert.AreEqual(0, w.Run.CargoEnergy);
            Assert.AreEqual(0, w.ResourceOrder);
            for (int i = 0; i < 400 && w.Run.Energy == 0; i++) w.Step(.1f);
            Assert.AreEqual(w.Run.Config.EnergyAmount, w.Run.Energy);
        }
        [Test] public void WaveAndEnemyCountsStayBounded()
        {
            var w = new WorldState(12);
            for(int i=0;i<7200;i++) { w.Step(0.1f); Assert.LessOrEqual(w.Enemies.Count,48); }
            Assert.Greater(w.Wave,0); Assert.AreNotEqual(0,w.Run.Outcome);
        }
        [Test] public void MovingTurretChangesWhichApproachItCanCover()
        {
            var west = new WorldState(1);
            var north = new WorldState(1);
            Assert.AreEqual(PlacementIssue.Occupied, north.MoveTurret(4, new Vector2Int(2, 0)));
            Assert.AreEqual(PlacementIssue.None, north.MoveTurret(4, new Vector2Int(0, 2)));
            Assert.AreEqual(0, north.Run.Scrap, "迁移不扣费");
            float distance = west.Run.Config.ShotRange + 2;
            var westEnemy = new EnemyState { Position = Vector2.left * distance, Health = 100 };
            var northEnemy = new EnemyState { Position = Vector2.left * distance, Health = 100 };
            west.Enemies.Add(westEnemy);
            north.Enemies.Add(northEnemy);
            for (int i = 0; i < 5; i++)
            {
                west.Step(.1f);
                north.Step(.1f);
            }
            Assert.Less(westEnemy.Health, 100, "西炮位可先打到西侧接近的敌人");
            Assert.AreEqual(100, northEnemy.Health, "迁往北侧后不能再覆盖原来的距离");
        }

        [Test] public void SquadCoversAssignedApproach()
        {
            var w = new WorldState(1);
            const int sector = 2;
            Vector2 approach = Vector2.up;
            Assert.IsTrue(w.DeploySquad(sector));
            for (int i = 0; i < 80; i++) w.Step(.1f);
            Assert.AreEqual(approach * 12, w.SquadPosition);
            var enemy = new EnemyState { Position = approach * 21, Health = 100 };
            w.Enemies.Add(enemy);
            w.Step(.1f);
            Assert.Less(enemy.Health, 100, "守军应在基础炮塔射程外接敌");
            Assert.AreEqual(0, w.Run.Scrap, "调防不扣费");
        }

        [Test] public void FourthWaveReleasesSecondLaneAfterFiveSeconds()
        {
            var world = new WorldState(11);
            float start = world.Run.Config.WaveTimes[3];
            world.Run.Tick(start, 0);
            world.Step(.01f);
            Assert.AreEqual(4, world.Wave);
            Assert.AreEqual(10 + 14 + 18 + 14 - 8, world.Enemies.Count);
            Assert.AreEqual(16, world.WaitingEnemies);
            world.Enemies.Clear();
            world.Run.Tick(4.8f, 0);
            world.Step(.01f);
            Assert.AreEqual(8, world.Enemies.Count);
            world.Enemies.Clear();
            world.Run.Tick(.2f, 0);
            world.Step(.01f);
            Assert.AreEqual(8, world.Enemies.Count);
            Assert.AreEqual(1, world.Enemies.FindAll(enemy => enemy.Kind == EnemyKind.Siege).Count);
            Assert.AreEqual(0, world.WaitingEnemies);
        }
        [Test] public void LargeFrameDoesNotSkipEntireWaves()
        {
            var w = new WorldState(1); w.Step(120);
            Assert.LessOrEqual(w.Run.Elapsed,0.101f); Assert.AreEqual(0,w.Wave);
        }
    }
}
