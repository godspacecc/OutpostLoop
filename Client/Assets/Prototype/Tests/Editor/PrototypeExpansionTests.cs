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
            var site = world.ExpansionSites[0];
            Assert.AreEqual(nodeIndex, site.NodeIndex);
            Assert.AreEqual(spec.BuildSeconds, site.ConstructionLeft);
            for (int i = 0; i < 30; i++) world.Step(.1f);
            Assert.AreEqual(original, node.Remaining, "施工中不得开采");
            Assert.IsTrue(world.AssignResource(nodeIndex));
            var remaining = site.ConstructionLeft;
            run.Paused = true;
            world.Step(.1f);
            Assert.AreEqual(remaining, site.ConstructionLeft);
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
        public void CompletedBarracksRequiresPaidOrderAndTrainingBeforeAttacking()
        {
            var world = new WorldState(732);
            var spec = ExpansionSite.SpecFor(ExpansionKind.Barracks);
            AddStock(world.Run, spec.Scrap + ExpansionSite.TrainingScrap, spec.Energy);
            Assert.IsTrue(world.BuildExpansion(2, ExpansionKind.Barracks));
            var site = world.ExpansionSites[2];
            Assert.IsFalse(world.TrainGarrison(2), "工地不能训练");
            Advance(world, spec.BuildSeconds + .2f);
            Assert.IsTrue(site.CanTrain);
            Advance(world, ExpansionSite.TrainingSeconds + .2f);
            Assert.IsFalse(site.HasGarrison, "完工后不自动出兵");
            Assert.IsFalse(world.TrainGarrison(2), "缺少能源不应开始训练");
            Assert.AreEqual(ExpansionSite.TrainingScrap, world.Run.Scrap);
            AddStock(world.Run, 0, ExpansionSite.TrainingEnergy);
            Assert.IsTrue(world.TrainGarrison(2));
            Assert.AreEqual(0, world.Run.Scrap);
            Assert.AreEqual(0, world.Run.Energy);
            Assert.IsFalse(world.TrainGarrison(2));
            world.Run.Paused = true;
            world.Step(.1f);
            Assert.AreEqual(ExpansionSite.TrainingSeconds, site.TrainingLeft);
            world.Run.Paused = false;
            var enemy = new EnemyState { Position = site.Position + Vector2.up * 4f, Health = 100 };
            world.Enemies.Add(enemy);
            for (int i = 0; i < 20; i++) world.Step(.1f);
            Assert.AreEqual(100, enemy.Health, "尚在训练，且敌人在基础炮塔覆盖之外");
            for (int i = 0; i < 120; i++) world.Step(.1f);
            Assert.AreEqual(0, site.TrainingLeft);
            Assert.IsTrue(site.HasGarrison);
            Assert.IsFalse(world.TrainGarrison(2));
            Assert.Less(enemy.Health, 100);
            Assert.Greater(site.LastShotTime, 0);

            AddStock(world.Run, spec.Scrap + ExpansionSite.TrainingScrap, spec.Energy + ExpansionSite.TrainingEnergy);
            Assert.IsTrue(world.BuildExpansion(3, ExpansionKind.Barracks));
            Advance(world, spec.BuildSeconds + .2f);
            Assert.IsTrue(world.TrainGarrison(3));
            var threatened = world.ExpansionSites[3];
            world.Enemies.Clear();
            for (int i = 0; i < 40; i++)
                world.Enemies.Add(new EnemyState { Position = threatened.Position + Vector2.down, Health = 10000 });
            Advance(world, 2);
            Assert.AreEqual(ExpansionKind.Empty, threatened.Kind);
            Assert.AreEqual(0, threatened.TrainingLeft);
            Assert.IsFalse(threatened.HasGarrison);
            Assert.AreEqual(0, world.Run.Scrap, "摧毁后不退训练费");
            Assert.AreEqual(0, world.Run.Energy);
        }

        [Test]
        public void BarricadeInterceptsAndDestroyedPlotCanBeRebuilt()
        {
            var world = new WorldState(732);
            var spec = ExpansionSite.SpecFor(ExpansionKind.Barricade);
            AddStock(world.Run, spec.Scrap * 2, 0);
            Assert.IsTrue(world.BuildExpansion(2, ExpansionKind.Barricade));
            var site = world.ExpansionSites[2];
            var enemy = new EnemyState { Position = site.Position + Vector2.up, Health = 10000 };
            world.Enemies.Add(enemy);
            for (int i = 0; i < 20; i++) world.Step(.1f);
            Assert.IsTrue(site.IsUnderConstruction);
            Assert.Less(site.Health, spec.Health / 2);
            int damagedHealth = site.Health;
            world.Enemies.Clear();
            Advance(world, spec.BuildSeconds);
            Assert.AreEqual(damagedHealth + spec.Health / 2, site.Health, "完工保留工地损伤");
            world.Enemies.Add(enemy);
            Assert.AreEqual(world.Run.Config.InitialHealth, world.Run.Health);
            for (int i = 0; i < 900 && site.Kind != ExpansionKind.Empty; i++) world.Step(.1f);
            Assert.AreEqual(ExpansionKind.Empty, site.Kind);
            for (int i = 0; i < 200 && world.Run.Health == world.Run.Config.InitialHealth; i++) world.Step(.1f);
            Assert.Less(world.Run.Health, world.Run.Config.InitialHealth, "路障被摧毁后敌人应继续攻击基地");
            Assert.IsTrue(world.BuildExpansion(2, ExpansionKind.Barricade));
            Assert.AreEqual(spec.Health / 2, site.Health);
        }

        [Test]
        public void TurretWorksiteReservesSlotAndCompletesAlongsideExpansion()
        {
            var world = new WorldState(732);
            AddStock(world.Run, world.TurretScrapCost + ExpansionSite.SpecFor(ExpansionKind.Barricade).Scrap, world.TurretEnergyCost);
            Assert.IsTrue(world.BuildTurret(2));
            Assert.IsTrue(world.BuildExpansion(3, ExpansionKind.Barricade));
            Assert.IsFalse(world.BuildTurret(2));
            Assert.IsFalse(world.MoveTurret(2, 3));
            Assert.IsFalse(world.MoveTurret(0, 2));
            Advance(world, 3.2f);
            Assert.IsFalse(world.ExpansionSites[3].IsUnderConstruction);
            Assert.IsFalse(world.Turrets[2].Occupied);
            Assert.IsTrue(world.Turrets[2].IsUnderConstruction);
            Advance(world, 3.2f);
            Assert.IsTrue(world.Turrets[2].Occupied);
            Assert.IsFalse(world.Turrets[2].IsUnderConstruction);
            Assert.AreEqual(0, world.Run.Scrap);
            Assert.AreEqual(0, world.Run.Energy);
        }

        [Test]
        public void DepletedNodeAndDestroyedWorksiteNeverProduceResources()
        {
            var world = new WorldState(732);
            var spec = ExpansionSite.SpecFor(ExpansionKind.Extractor);
            AddStock(world.Run, spec.Scrap * 2, 0);
            Assert.IsTrue(world.BuildExpansion(0, ExpansionKind.Extractor));
            Assert.IsTrue(world.BuildExpansion(2, ExpansionKind.Extractor));
            var emptySource = world.ExpansionSites[0];
            var attacked = world.ExpansionSites[2];
            world.Nodes[emptySource.NodeIndex].Remaining = 0;
            for (int i = 0; i < 20; i++)
                world.Enemies.Add(new EnemyState { Position = attacked.Position + Vector2.up, Health = 10000 });
            Advance(world, 2);
            Assert.AreEqual(ExpansionKind.Empty, attacked.Kind);
            world.Enemies.Clear();
            Advance(world, spec.BuildSeconds + spec.Cycle);
            Assert.AreEqual(ExpansionKind.Extractor, emptySource.Kind);
            Assert.IsFalse(emptySource.IsUnderConstruction);
            Assert.AreEqual(0, world.Run.Scrap);
            Assert.AreEqual(0, world.Run.Energy);
        }

        private static void Advance(WorldState world, float seconds)
        {
            for (int i = 0; i < Mathf.CeilToInt(seconds * 10); i++) world.Step(.1f);
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
