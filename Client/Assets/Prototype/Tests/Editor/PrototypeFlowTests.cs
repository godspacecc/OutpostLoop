using System.Linq;
using NUnit.Framework;
using UnityEngine;
using static OutpostLoop.Tests.PrototypeWorldTests;

namespace OutpostLoop.Tests
{
    /// <summary>最低起始档下，用真实生产和支出路径完成四次守城对照。</summary>
    public class PrototypeFlowTests
    {
        [SetUp] public void Prepare() => Game.FoundationRuntime.Initialize();
        [TearDown] public void Close() => Game.FoundationRuntime.Shutdown();

        [Test]
        public void EconomyAndDefenseOpeningsCanWinOnTwoSeeds() => VerifyOpenings(false);

        [Test]
        public void OldHarborEconomyAndDefenseOpeningsCanWinOnTwoSeeds() => VerifyOpenings(true);

        private static void VerifyOpenings(bool oldHarbor)
        {
            foreach (int seed in new[] { 732, 733 })
            foreach (bool economy in new[] { true, false })
            {
                var world = oldHarbor ? WorldState.CreateOldHarbor(seed, 800) : new WorldState(seed);
                BuildBase(world, economy ? -4 : 4);
                var run = world.Run;
                var center = world.BasePosition;
                string strategy = economy ? "economy" : "defense";
                int turrets = 0;
                if (!economy)
                {
                    BuildNear(world, ExpansionKind.Turret, center + Vector2.right * 5);
                    turrets = 1;
                }
                var miningCell = FindMiningPlacement(world, center + (economy ? Vector2.up : Vector2.left) * 5);
                Assert.AreEqual(PlacementIssue.None, world.Build(ExpansionKind.Extractor, miningCell));
                int station = world.FindBuilding(miningCell);
                Debug.Log($"V08 {seed} {strategy} base={center} first={(economy ? "mine" : "turret")} station={world.Buildings[station].Position}");
                int support = -1;
                int sightingsRead = 0;
                bool firstDelivery = false, firstClear = false, thirdClear = false;
                for (int step = 0; step < 10000 && run.Outcome == 0; step++)
                {
                    if (world.Buildings[station].CanProduce && run.Funds >= ExpansionSite.PersonnelCost)
                        Assert.IsTrue(world.Produce(station));
                    if (world.Buildings[station].Personnel != null)
                    {
                        if (turrets == 0 && run.Funds >= 300)
                        {
                            BuildNear(world, ExpansionKind.Turret, center + Vector2.down * 5);
                            turrets++;
                        }
                        if (turrets > 0 && run.Levels[1] == 0) run.Upgrade(1);
                        if (turrets > 0 && turrets < 4 && run.Funds >= 300)
                        {
                            var offset = turrets == 1 ? Vector2.up : turrets == 2 ? Vector2.left : Vector2.right;
                            BuildNear(world, ExpansionKind.Turret, center + offset * 5);
                            turrets++;
                        }
                        if (turrets >= 2 && run.Health < 82) run.Upgrade(2);
                        if (turrets == 4)
                        {
                            run.Upgrade(1);
                            if (run.Levels[1] == 3 && support < 0 && run.Funds >= (economy ? 500 : 400))
                                support = BuildNear(world, economy ? ExpansionKind.Extractor : ExpansionKind.Barracks,
                                    center + (economy ? Vector2.left : Vector2.down) * 9);
                        }
                        if (support >= 0 && world.Buildings[support].CanProduce) world.Produce(support);
                    }
                    while (sightingsRead < world.Sightings.Count)
                    {
                        var sighting = world.Sightings[sightingsRead++];
                        if (!economy && support >= 0) world.DeploySquad(support, sighting.Sector);
                    }
                    if (!firstClear && world.Kills >= 10)
                    {
                        firstClear = true;
                        LogState(world, strategy, "wave 1 cleared");
                    }
                    if (!thirdClear && world.Kills >= 42)
                    {
                        thirdClear = true;
                        LogState(world, strategy, "wave 3 cleared");
                    }
                    int beforeStep = run.Funds;
                    world.Step(.1f);
                    if (!firstDelivery && run.Funds > beforeStep)
                    {
                        firstDelivery = true;
                        LogState(world, strategy, "first delivery");
                    }
                }
                LogState(world, strategy, "result");
                Assert.IsTrue(firstDelivery);
                Assert.AreEqual(1, run.Outcome, $"{seed} {strategy}");
                Assert.AreEqual(120, world.Kills);
            }
        }

        // 用玩家在开局能看到的矿点选择采集站。优先给采集员留下可接续的矿点。
        private static Vector2Int FindMiningPlacement(WorldState world, Vector2 preferred)
        {
            float best = float.NegativeInfinity;
            var selected = Vector2Int.zero;
            var center = BattleGrid.Cell(world.BasePosition);
            for (int x = center.x - 11; x <= center.x + 11; x++)
            for (int y = center.y - 11; y <= center.y + 11; y++)
            {
                var cell = new Vector2Int(x, y);
                if (world.PreviewPlacement(ExpansionKind.Extractor, cell, false, -1, out var footprint, out int node) != PlacementIssue.None) continue;
                var position = BattleGrid.Center(footprint);
                int mineCount = world.Nodes.Where((mine, index) => world.KnownOreRemaining(index) > 0 &&
                    (mine.Position - position).sqrMagnitude <= 12 * 12).Count();
                float score = Mathf.Min(5, mineCount) * 6f - Vector2.Distance(position, preferred) -
                    .4f * Vector2.Distance(position, world.Nodes[node].Position);
                if (score <= best) continue;
                selected = cell;
                best = score;
            }
            Assert.IsFalse(float.IsNegativeInfinity(best));
            return selected;
        }

        private static void LogState(WorldState world, string strategy, string stage)
        {
            var layout = string.Join(",", world.Buildings.Where(site => site.Kind != ExpansionKind.Empty)
                .Select(site => $"{site.Kind}@{site.Footprint.min}"));
            Debug.Log($"V08 {world.Seed} {strategy} {stage}: t={world.Run.Elapsed:F1} funds={world.Run.Funds} " +
                $"hp={world.Run.Health} kills={world.Kills} outcome={world.Run.Outcome} loss={world.LastDestroyedKind} layout={layout}");
        }

        [Test]
        public void ControllerRestartResetsCommanderAndKeepsBoundedPool()
        {
            var root = new GameObject("FlowTestRoot");
            try
            {
                var c = root.AddComponent<PrototypeController>();
                c.Initialize();
                c.Restart(true);
                var prior = c.World;
                BuildBase(prior, -4);
                prior.Run.Damage(100);
                c.Restart(true);
                Assert.AreNotSame(prior, c.World);
                Assert.AreNotEqual(prior.Seed, c.World.Seed);
                Assert.IsNull(c.World.Headquarters);
                Assert.AreEqual(0, c.World.Run.Outcome);
                Assert.IsFalse(c.World.Run.Paused);
                Assert.AreEqual(48, root.GetComponentsInChildren<Transform>(true).Count(t => t.name == "PooledEnemy"));
                Assert.AreEqual(1, root.GetComponentsInChildren<Camera>(true).Length);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
