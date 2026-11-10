using NUnit.Framework;
using UnityEngine;

namespace OutpostLoop.Tests
{
    /// <summary>通过真实领域采集、运输和升级入口验证完整局，不注入库存。</summary>
    public class PrototypeFlowTests
    {
        [TearDown] public void CloseFoundation() { Game.FoundationRuntime.Shutdown(); }

        [SetUp] public void PrepareFoundation() { Game.FoundationRuntime.Initialize(); }

        [Test] public void EconomyAndDefenseOpeningsCanWinOnTheSameTwoSeeds()
        {
            foreach (int seed in new[] { 732, 733 })
            foreach (bool economy in new[] { true, false })
            {
                var world = new WorldState(seed);
                var run = world.Run;
                bool firstPurchase = false, supportBuilt = false;
                int extraTurrets = 0, loggedClear = 0;
                float observedArrival = float.NegativeInfinity;
                string strategy = economy ? "economy" : "defense";
                for (int step = 0; step < 7300 && run.Outcome == 0; step++)
                {
                    if (!firstPurchase)
                    {
                        firstPurchase = economy ? TryBuild(world, ExpansionKind.Extractor, Vector2.zero) : run.Upgrade(1);
                        if (firstPurchase)
                            Debug.Log($"V05 seed={seed} strategy={strategy} first={(economy ? "extractor" : "firepower")} time={run.Elapsed:F1}");
                    }
                    if (firstPurchase)
                    {
                        if (run.Levels[1] < 3) run.Upgrade(1);
                        if (run.Levels[1] >= 2 && !supportBuilt)
                            supportBuilt = TryBuild(world, economy ? ExpansionKind.Barracks : ExpansionKind.Barricade,
                                economy ? new Vector2(0, -7) : new Vector2(0, 8));
                        if (supportBuilt && economy)
                            for (int i = 0; i < world.Buildings.Count; i++)
                                if (world.Buildings[i].CanTrain) world.TrainGarrison(i);
                        if (run.Levels[1] >= 2 && extraTurrets < 2 &&
                            TryBuild(world, ExpansionKind.Turret, new Vector2(0, extraTurrets == 0 ? 5 : -5))) extraTurrets++;
                        if (run.Levels[1] == 3 && (run.Health < 75 || extraTurrets >= 2)) run.Upgrade(2);
                    }
                    if (world.ResourceOrder < 0 && !world.WorkerReturning)
                    {
                        bool needEnergy = firstPurchase && run.Energy < (run.Levels[1] < 3 ? run.EnergyCost(1) : 5);
                        int nearest = FindNode(world, needEnergy);
                        if (nearest < 0) nearest = FindNode(world, !needEnergy);
                        if (nearest >= 0) Assert.IsTrue(world.AssignResource(nearest));
                    }
                    if (world.LastArrival > observedArrival && world.Enemies.Count > 0)
                    {
                        observedArrival = world.LastArrival;
                        var approach = world.Enemies[world.Enemies.Count - 1].Position;
                        int sector = Mathf.Abs(approach.x) >= Mathf.Abs(approach.y)
                            ? (approach.x < 0 ? 0 : 1) : (approach.y > 0 ? 2 : 3);
                        Assert.IsTrue(world.DeploySquad(sector));
                    }
                    if (loggedClear == 0 && world.Kills >= 10)
                    {
                        LogState(world, strategy, "after1");
                        loggedClear = 1;
                    }
                    if (loggedClear == 1 && world.Kills >= 42)
                    {
                        LogState(world, strategy, "after3");
                        loggedClear = 3;
                    }
                    world.Step(.1f);
                }
                LogState(world, strategy, "result");
                Assert.IsTrue(firstPurchase && supportBuilt, $"seed={seed}, {strategy}");
                Assert.AreEqual(1, run.Outcome, $"seed={seed}, {strategy}");
                Assert.AreEqual(120, world.Kills);
            }
        }

        private static void LogState(WorldState world, string strategy, string stage)
        {
            var layout = string.Join(",", System.Linq.Enumerable.Select(
                System.Linq.Enumerable.Where(world.Buildings, b => b.Kind != ExpansionKind.Empty), b => $"{b.Kind}@{b.Footprint.min}"));
            Debug.Log($"V05 seed={world.Seed} strategy={strategy} {stage} t={world.Run.Elapsed:F1} " +
                $"stock={world.Run.Scrap}/{world.Run.Energy} hp={world.Run.Health} kills={world.Kills} " +
                $"outcome={world.Run.Outcome} lastLoss={world.LastDestroyedKind} layout={layout}");
        }

        private static int FindNode(WorldState world, bool energy)
        {
            int nearest = -1;
            float distance = float.PositiveInfinity;
            for (int i = 0; i < world.Nodes.Count; i++)
            {
                var node = world.Nodes[i];
                if (node.Remaining <= 0 || node.Energy != energy || node.Position.sqrMagnitude >= distance) continue;
                distance = node.Position.sqrMagnitude;
                nearest = i;
            }
            return nearest;
        }

        private static bool TryBuild(WorldState world, ExpansionKind kind, Vector2 preference)
        {
            var spec = ExpansionSite.SpecFor(kind);
            if (world.Run.Scrap < spec.Scrap || world.Run.Energy < spec.Energy) return false;
            float best = float.PositiveInfinity;
            var target = Vector2Int.zero;
            for (int x = -8; x <= 8; x++)
            for (int y = -8; y <= 8; y++)
            {
                var cell = new Vector2Int(x, y);
                float distance = (BattleGrid.Center(cell) - preference).sqrMagnitude;
                if (distance >= best || world.PreviewPlacement(kind, cell, false, -1, out _, out int node) != PlacementIssue.None) continue;
                if (kind == ExpansionKind.Extractor && world.Nodes[node].Energy) continue;
                target = cell;
                best = distance;
            }
            return !float.IsPositiveInfinity(best) && world.Build(kind, target) == PlacementIssue.None;
        }
        [Test] public void ControllerRestartReplacesWorldAndKeepsBoundedPool()
        {
            var root=new GameObject("FlowTestRoot");
            try
            {
                var c=root.AddComponent<PrototypeController>();c.Initialize();c.Restart(true);
                var prior=c.World;prior.AssignResource(0);prior.DeploySquad(2);prior.Run.Damage(100);
                c.Restart(true);
                Assert.AreNotSame(prior,c.World);Assert.AreEqual(0,c.World.Run.Outcome);Assert.IsFalse(c.World.Run.Paused);
                Assert.AreEqual(0,c.World.Run.Scrap);Assert.AreEqual(48,root.GetComponentsInChildren<Transform>(true).Length>48?CountEnemies(root):0);
                Assert.AreEqual(-1,c.World.ResourceOrder);Assert.AreEqual(-1,c.World.SquadSector);
                Assert.AreEqual(1,root.GetComponentsInChildren<Camera>(true).Length);
            }
            finally{Object.DestroyImmediate(root);}
        }
        private static int CountEnemies(GameObject root)
        {int count=0;foreach(var t in root.GetComponentsInChildren<Transform>(true))if(t.name=="PooledEnemy")count++;return count;}
    }
}
