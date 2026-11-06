using NUnit.Framework;
using UnityEngine;

namespace OutpostLoop.Tests
{
    /// <summary>通过真实领域采集、运输和升级入口验证完整局，不注入库存。</summary>
    public class PrototypeFlowTests
    {
        [Test] public void TwoSeedsCanWinByGatheringAndReturningHome()
        {
            foreach(int seed in new[]{732,733})
            {
                var w=new WorldState(seed); int trips=0;
                for(int step=0;step<7201&&w.Run.Outcome==0;step++)
                {
                    var s=w.Run;int route=s.Levels[1]<3?1:s.Levels[2]<3?2:0;
                    bool ready=s.Scrap+s.CargoScrap>=s.ScrapCost(route)&&s.Energy+s.CargoEnergy>=s.EnergyCost(route);
                    bool full=s.CargoScrap+s.CargoEnergy>=s.Capacity;
                    bool done=s.Levels[0]==3&&s.Levels[1]==3&&s.Levels[2]==3;
                    Vector2 goal=Vector2.zero;
                    if(!full&&!ready&&!done)
                    {
                        bool needEnergy=s.Energy+s.CargoEnergy<s.EnergyCost(route);
                        ResourceNode best=null;
                        foreach(var n in w.Nodes)if(n.Remaining>0&&n.Energy==needEnergy&&(best==null||Vector2.Distance(w.Player,n.Position)<Vector2.Distance(w.Player,best.Position)))best=n;
                        if(best!=null)goal=best.Position;
                    }
                    if(w.Player.magnitude<3.5f&&s.Upgrade(route))trips++;
                    var direction=goal-w.Player;
                    w.Step(0.1f,direction.magnitude>0.2f?direction:Vector2.zero,true);
                }
                Debug.Log("BALANCE seed="+seed+" outcome="+w.Run.Outcome+" time="+w.Run.Elapsed+" hp="+w.Run.Health+" purchases="+trips+" kills="+w.Kills);
                Assert.AreEqual(1,w.Run.Outcome,"seed="+seed);Assert.GreaterOrEqual(trips,3);
            }
        }
        [Test] public void ControllerRestartReplacesWorldAndKeepsBoundedPool()
        {
            var root=new GameObject("FlowTestRoot");
            try
            {
                var c=root.AddComponent<PrototypeController>();c.Initialize();c.Restart(true);
                var prior=c.World;prior.Run.Gather(false);prior.Run.Deposit();prior.Run.Damage(100);
                c.Restart(true);
                Assert.AreNotSame(prior,c.World);Assert.AreEqual(0,c.World.Run.Outcome);Assert.IsFalse(c.World.Run.Paused);
                Assert.AreEqual(0,c.World.Run.Scrap);Assert.AreEqual(48,root.GetComponentsInChildren<Transform>(true).Length>48?CountEnemies(root):0);
                Assert.AreEqual(1,root.GetComponentsInChildren<Camera>(true).Length);
            }
            finally{Object.DestroyImmediate(root);}
        }
        private static int CountEnemies(GameObject root)
        {int count=0;foreach(var t in root.GetComponentsInChildren<Transform>(true))if(t.name=="PooledEnemy")count++;return count;}
    }
}
