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
            world.Step(0.01f, Vector2.zero, false);
            Assert.AreEqual(config.MaxEnemies, world.Enemies.Count);
        }
        [Test] public void SeedProducesBoundedRepeatableMap()
        {
            var a = new WorldState(41); var b = new WorldState(41); var c = new WorldState(42);
            Assert.AreEqual(36, a.Nodes.Count); Assert.AreEqual(a.Nodes[5].Position, b.Nodes[5].Position);
            Assert.AreNotEqual(a.Nodes[5].Position, c.Nodes[5].Position);
            foreach (var node in a.Nodes) { Assert.LessOrEqual(node.Position.magnitude, 47); Assert.Greater(node.Position.magnitude, 5); }
        }
        [Test] public void MovementRespectsMapAndPause()
        {
            var w = new WorldState(1);
            for(int i=0;i<200;i++) w.Step(0.1f, Vector2.right, false);
            Assert.Greater(w.Player.x, 20); Assert.LessOrEqual(w.Player.x, 46);
            var before = w.Player; w.Run.Paused = true; w.Step(1, Vector2.left, false); Assert.AreEqual(before, w.Player);
        }
        [Test] public void NearbyGatheringConsumesNodeAndDepositsAtHome()
        {
            var w = new WorldState(1); var node = w.Nodes[0];
            for(int i=0;i<500 && Vector2.Distance(w.Player,node.Position)>0.2f;i++) w.Step(0.02f,node.Position-w.Player,false);
            Assert.AreSame(node, w.FindGatherTarget(), "提示应定位按 E 实际采集的资源点");
            int before = node.Remaining;
            for(int i=0;i<50;i++) w.Step(0.02f,Vector2.zero,true);
            Assert.Less(node.Remaining,before); Assert.Greater(w.Run.CargoScrap+w.Run.CargoEnergy,0);
            for(int i=0;i<500 && w.Player.magnitude>0.2f;i++) w.Step(0.02f,-w.Player,false);
            Assert.AreEqual(0,w.Run.CargoScrap+w.Run.CargoEnergy); Assert.Greater(w.Run.Scrap+w.Run.Energy,0);
            Assert.IsNull(w.FindGatherTarget(), "返回基地离开采集范围后不应继续提示");
        }
        [Test] public void WaveAndEnemyCountsStayBounded()
        {
            var w = new WorldState(12);
            for(int i=0;i<7200;i++) { w.Step(0.1f,Vector2.zero,false); Assert.LessOrEqual(w.Enemies.Count,48); }
            Assert.Greater(w.Wave,0); Assert.AreNotEqual(0,w.Run.Outcome);
        }
        [Test] public void MovingTurretChangesWhichApproachItCanCover()
        {
            var west = new WorldState(1);
            var north = new WorldState(1);
            Assert.IsFalse(north.MoveTurret(0, 2), "远离基地时不能调整炮位");
            north.Step(.1f, Vector2.up, false);
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
                west.Step(.1f, Vector2.zero, false);
                north.Step(.1f, Vector2.zero, false);
            }
            Assert.Less(westEnemy.Health, 100, "西炮位可先打到西侧接近的敌人");
            Assert.AreEqual(100, northEnemy.Health, "迁往北侧后不能再覆盖原来的距离");
        }

        [Test] public void FullyPreparedBaseWinsCompleteRun()
        {
            foreach(int seed in new[]{11,22})
            {
                var w = new WorldState(seed);
                for(int n=0;n<50;n++){for(int j=0;j<12;j++)w.Run.Gather(j%3==0); w.Run.Deposit();}
                for(int route=0;route<3;route++)for(int level=0;level<3;level++)Assert.IsTrue(w.Run.Upgrade(route));
                for(int i=0;i<7201;i++)w.Step(0.1f,Vector2.zero,false);
                Assert.AreEqual(1,w.Run.Outcome,"seed="+seed); Assert.AreEqual(6,w.Wave); Assert.Greater(w.Kills,0);
            }
        }
        [Test] public void LargeFrameDoesNotSkipEntireWaves()
        {
            var w = new WorldState(1); w.Step(120,Vector2.zero,false);
            Assert.LessOrEqual(w.Run.Elapsed,0.101f); Assert.AreEqual(0,w.Wave);
        }
    }
}
