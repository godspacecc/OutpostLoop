using NUnit.Framework;
using UnityEngine;
namespace OutpostLoop.Tests
{
    /// <summary>地图与战斗的限定验证，包含完整局时模拟和敌波容量边界。</summary>
    public class PrototypeWorldTests
    {
        [TearDown] public void CloseFoundation() { Game.FoundationRuntime.Shutdown(); }

        [SetUp] public void PrepareFoundation() { Game.FoundationRuntime.Initialize(); }

        [Test] public void ExtremeWaveConfigurationStillSpawnsWithinPoolLimit()
        {
            var config = PrototypeServices.LoadConfig();
            // 构造独立极值快照，不修改权威CSV或生成产物。
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(OutpostLoop.Config.PrototypeConfig).GetField("<WaveCountBase>k__BackingField", flags).SetValue(config, int.MaxValue);
            typeof(OutpostLoop.Config.PrototypeConfig).GetField("<WaveCountStep>k__BackingField", flags).SetValue(config, int.MaxValue);
            var world = new WorldState(12, config);
            world.Run.Tick(config.WaveTimes[0], 0);
            world.Step(0.01f);
            Assert.AreEqual(config.MaxEnemies, world.Enemies.Count);
        }
        [Test] public void SeedProducesBoundedRepeatableMap()
        {
            var a = new WorldState(41); var b = new WorldState(41); var c = new WorldState(42);
            Assert.AreEqual(36, a.Nodes.Count); Assert.AreEqual(a.Nodes[5].Position, b.Nodes[5].Position);
            Assert.AreNotEqual(a.Nodes[5].Position, c.Nodes[5].Position);
            foreach (var node in a.Nodes) { Assert.LessOrEqual(node.Position.magnitude, 47); Assert.Greater(node.Position.magnitude, 5); }
            var opening = new WorldState(732);
            foreach (var node in opening.Nodes)
                foreach (var site in opening.ExpansionSites)
                    Assert.Greater(Vector2.Distance(node.Position, site.Position), 3.7f, "资源应留出建筑地块");
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
            Assert.IsFalse(north.MoveTurret(0, 1), "不能覆盖已占用炮位");
            Assert.IsTrue(north.MoveTurret(0, 2));
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

        [Test] public void ForecastMatchesSpawnAndSquadCoversAssignedApproach()
        {
            var w = new WorldState(1);
            int sector = w.NextWaveSector;
            Vector2 approach = w.Turrets[sector].Position.normalized;
            Assert.AreEqual(w.Run.Config.WaveTimes[0], w.NextWaveTime);
            Assert.IsTrue(w.DeploySquad(sector));
            for (int i = 0; i < 50; i++) w.Step(.1f);
            Assert.AreEqual(approach * 12, w.SquadPosition);
            var enemy = new EnemyState { Position = approach * 21, Health = 100 };
            w.Enemies.Add(enemy);
            w.Step(.1f);
            Assert.Less(enemy.Health, 100, "守军应在基础炮塔射程外接敌");
            Assert.AreEqual(0, w.Run.Scrap, "调防不扣费");
            w.Enemies.Clear();
            w.Run.Tick(w.NextWaveTime - w.Run.Elapsed, 0);
            w.Step(.1f);
            Assert.AreEqual(1, w.Wave);
            Assert.Greater(w.Enemies.Count, 0);
            foreach (var spawned in w.Enemies)
                Assert.Greater(Vector2.Dot(approach, spawned.Position.normalized), .42f, "生成来向应落在预告侧");
        }

        [Test] public void FullyPreparedBaseWinsCompleteRun()
        {
            foreach(int seed in new[]{11,22})
            {
                var w = new WorldState(seed);
                for(int n=0;n<50;n++){for(int j=0;j<12;j++)w.Run.Gather(j%3==0); w.Run.Deposit();}
                for(int route=0;route<3;route++)for(int level=0;level<3;level++)Assert.IsTrue(w.Run.Upgrade(route));
                for(int i=0;i<7201;i++)w.Step(0.1f);
                Assert.AreEqual(1,w.Run.Outcome,"seed="+seed); Assert.AreEqual(6,w.Wave); Assert.Greater(w.Kills,0);
            }
        }
        [Test] public void LargeFrameDoesNotSkipEntireWaves()
        {
            var w = new WorldState(1); w.Step(120);
            Assert.LessOrEqual(w.Run.Elapsed,0.101f); Assert.AreEqual(0,w.Wave);
        }
    }
}
