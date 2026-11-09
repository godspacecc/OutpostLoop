using NUnit.Framework;
using UnityEngine;

namespace OutpostLoop.Tests
{
    /// <summary>限定验证扩建扣费、有限资源、训练及外围拦截。</summary>
    public sealed class PrototypeExpansionTests
    {
        [SetUp] public void PrepareFoundation() => Game.FoundationRuntime.Initialize();
        [TearDown] public void CloseFoundation() => Game.FoundationRuntime.Shutdown();

        [Test]
        public void FailedOrdersDoNotSpendAndOccupiedPlotCannotBeOverwritten()
        {
            var world = new WorldState(732);
            AddStock(world.Run, 20, 0);
            Assert.IsFalse(world.BuildExpansion(0, ExpansionKind.Barracks), "缺能源不应扣除废料");
            Assert.IsFalse(world.BuildExpansion(-1, ExpansionKind.Barricade));
            Assert.IsFalse(world.BuildExpansion(0, ExpansionKind.Empty));
            Assert.AreEqual(20, world.Run.Scrap);
            world.Run.Paused = true;
            Assert.IsFalse(world.BuildExpansion(0, ExpansionKind.Barricade), "真正暂停期间不接受建造");
            Assert.AreEqual(20, world.Run.Scrap);
            world.Run.Paused = false;
            Assert.IsTrue(world.BuildExpansion(0, ExpansionKind.Barricade));
            Assert.IsFalse(world.BuildExpansion(0, ExpansionKind.Extractor));
            Assert.AreEqual(20 - ExpansionSite.SpecFor(ExpansionKind.Barricade).Scrap, world.Run.Scrap);
        }

        [Test]
        public void WorkerAndExtractorShareFiniteNodeAndDeliverLastUnitExactlyOnce()
        {
            var world = new WorldState(732);
            var run = world.Run;
            var spec = ExpansionSite.SpecFor(ExpansionKind.Extractor);
            int nodeIndex = world.FindExpansionResource(0);
            Assert.GreaterOrEqual(nodeIndex, 0);
            var node = world.Nodes[nodeIndex];
            int original = node.Remaining;
            AddStock(run, spec.Scrap, 0);
            Assert.IsTrue(world.BuildExpansion(0, ExpansionKind.Extractor));
            Assert.AreEqual(nodeIndex, world.ExpansionSites[0].NodeIndex);
            Assert.IsTrue(world.AssignResource(nodeIndex));
            run.Paused = true;
            world.Step(.1f);
            Assert.AreEqual(original, node.Remaining);
            run.Paused = false;
            for (int i = 0; i < 650; i++) world.Step(.1f);
            Assert.AreEqual(original, run.Scrap + run.Energy);
            Assert.AreEqual(0, run.CargoScrap + run.CargoEnergy);
            Assert.AreEqual(0, node.Remaining);
            Assert.AreEqual(-1, world.ResourceOrder);
            Assert.IsFalse(world.WorkerReturning);
        }

        [Test]
        public void BarracksTrainsBeforeItsGarrisonAttacks()
        {
            var world = new WorldState(732);
            var spec = ExpansionSite.SpecFor(ExpansionKind.Barracks);
            AddStock(world.Run, spec.Scrap, spec.Energy);
            Assert.IsTrue(world.BuildExpansion(2, ExpansionKind.Barracks));
            var site = world.ExpansionSites[2];
            world.Run.Paused = true;
            world.Step(.1f);
            Assert.AreEqual(spec.Cycle, site.TrainingLeft);
            world.Run.Paused = false;
            var enemy = new EnemyState { Position = site.Position + Vector2.up * 4f, Health = 100 };
            world.Enemies.Add(enemy);
            for (int i = 0; i < 20; i++) world.Step(.1f);
            Assert.AreEqual(100, enemy.Health, "尚在训练，且敌人在基础炮塔覆盖之外");
            for (int i = 0; i < 120; i++) world.Step(.1f);
            Assert.AreEqual(0, site.TrainingLeft);
            Assert.Less(enemy.Health, 100);
            Assert.Greater(site.LastShotTime, 0);
        }

        [Test]
        public void BarricadeInterceptsAndDestroyedPlotCanBeRebuilt()
        {
            var world = new WorldState(732);
            var spec = ExpansionSite.SpecFor(ExpansionKind.Barricade);
            AddStock(world.Run, spec.Scrap * 2, 0);
            Assert.IsTrue(world.BuildExpansion(2, ExpansionKind.Barricade));
            var site = world.ExpansionSites[2];
            world.Enemies.Add(new EnemyState { Position = site.Position + Vector2.up, Health = 10000 });
            for (int i = 0; i < 20; i++) world.Step(.1f);
            Assert.Less(site.Health, spec.Health);
            Assert.AreEqual(world.Run.Config.InitialHealth, world.Run.Health);
            for (int i = 0; i < 900 && site.Kind != ExpansionKind.Empty; i++) world.Step(.1f);
            Assert.AreEqual(ExpansionKind.Empty, site.Kind);
            for (int i = 0; i < 200 && world.Run.Health == world.Run.Config.InitialHealth; i++) world.Step(.1f);
            Assert.Less(world.Run.Health, world.Run.Config.InitialHealth, "路障被摧毁后敌人应继续攻击基地");
            Assert.IsTrue(world.BuildExpansion(2, ExpansionKind.Barricade));
            Assert.AreEqual(spec.Health, site.Health);
        }

        private static void AddStock(RunState run, int scrap, int energy)
        {
            for (int i = 0; i < scrap + energy; i++)
            {
                if (run.CargoScrap + run.CargoEnergy == run.Capacity) run.Deposit();
                Assert.IsTrue(run.Gather(i >= scrap));
            }
            run.Deposit();
        }
    }
}
