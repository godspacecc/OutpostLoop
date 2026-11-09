using NUnit.Framework;
using UnityEngine;

namespace OutpostLoop.Tests
{
    /// <summary>通过真实领域采集、运输和升级入口验证完整局，不注入库存。</summary>
    public class PrototypeFlowTests
    {
        [TearDown] public void CloseFoundation() { Game.FoundationRuntime.Shutdown(); }

        [SetUp] public void PrepareFoundation() { Game.FoundationRuntime.Initialize(); }

        [Test] public void TwoSeedsCanWinByAssigningWorkersAndSquad()
        {
            foreach(int seed in new[]{732,733})
            {
                var w=new WorldState(seed); int purchases=0;
                int observedWave = 0;
                for(int step=0;step<7201&&w.Run.Outcome==0;step++)
                {
                    var s = w.Run;
                    int route = s.Levels[0] == 0 ? 0 : s.Levels[1] < 3 ? 1 : s.Levels[2] < 3 ? 2 : 0;
                    if (s.Upgrade(route)) purchases++;
                    if (w.ResourceOrder < 0 && !w.WorkerReturning && s.Levels[route] < 3)
                    {
                        bool needEnergy = s.Energy < s.EnergyCost(route);
                        int nearest = -1;
                        float distance = float.PositiveInfinity;
                        for (int i = 0; i < w.Nodes.Count; i++)
                        {
                            var node = w.Nodes[i];
                            if (node.Remaining <= 0 || node.Energy != needEnergy || node.Position.sqrMagnitude >= distance) continue;
                            distance = node.Position.sqrMagnitude;
                            nearest = i;
                        }
                        if (nearest >= 0) Assert.IsTrue(w.AssignResource(nearest));
                    }
                    if (w.Wave != observedWave && w.Enemies.Count > 0)
                    {
                        // 只读取已经进场的敌人，按主要方向反应调防。
                        observedWave = w.Wave;
                        var approach = Vector2.zero;
                        foreach (var enemy in w.Enemies) approach += enemy.Position;
                        int sector = Mathf.Abs(approach.x) >= Mathf.Abs(approach.y)
                            ? (approach.x < 0 ? 0 : 1) : (approach.y > 0 ? 2 : 3);
                        Assert.IsTrue(w.DeploySquad(sector));
                    }
                    w.Step(.1f);
                }
                Debug.Log("BALANCE seed="+seed+" outcome="+w.Run.Outcome+" time="+w.Run.Elapsed+" hp="+w.Run.Health+" purchases="+purchases+" kills="+w.Kills);
                Assert.AreEqual(1,w.Run.Outcome,"seed="+seed);Assert.GreaterOrEqual(purchases,3);
            }
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
