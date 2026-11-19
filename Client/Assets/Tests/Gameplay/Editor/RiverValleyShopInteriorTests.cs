using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using OutpostLoop.RiverValley;

namespace OutpostLoop.Tests
{
    /// <summary>验证陈设通路、实体碰撞与可见占地；完整实际地图另由 Boot 用例覆盖。</summary>
    public sealed class RiverValleyShopInteriorTests
    {
        private static RiverValleyWorldGeometry Geometry()
        {
            var geometry=new RiverValleyWorldGeometry();
            for(int i=0;i<6;i++)geometry.Interiors[i]=new Rect(i*50,0,32,25);
            geometry.Finish();
            return geometry;
        }

        [Test]
        public void AllSalesAislesAndExistingFacilitiesRemainConnected()
        {
            var geometry=Geometry();geometry.EnableShopInterior();var plan=geometry.Shop;
            Assert.AreEqual(16,plan.Fixtures.Length);
            // 半米网格与角色 .34 米半径独立验证连通，不依赖游戏寻路实现。
            var visited=new HashSet<Vector2Int>();var queue=new Queue<Vector2Int>();
            Vector2 Point(Vector2Int p)=>new Vector2(p.x*.5f,p.y*.5f);
            bool Free(Vector2 p)=>p.x>.65f && p.x<31.35f && p.y>.65f && p.y<24.35f && geometry.Walkable(p);
            var first=new Vector2Int(2,25);visited.Add(first);queue.Enqueue(first);
            while(queue.Count>0)
            {
                var current=queue.Dequeue();
                foreach(var delta in new[]{Vector2Int.left,Vector2Int.right,Vector2Int.up,Vector2Int.down})
                {
                    var next=current+delta;
                    if(visited.Contains(next) || !Free(Point(next)) || !Free((Point(current)+Point(next))/2))continue;
                    visited.Add(next);queue.Enqueue(next);
                }
            }
            var targets=new List<Vector2>{plan.CheckoutTarget,plan.StockTarget,plan.BedApproach,plan.CabinetApproach};
            foreach(var fixture in plan.Fixtures)
                if(fixture.Type==RiverValleyShopInterior.Kind.Shelf || fixture.Type==RiverValleyShopInterior.Kind.Stock)
                {
                    targets.Add(fixture.Area.center+Vector2.left*(fixture.Area.width/2+.7f));
                    targets.Add(fixture.Area.center+Vector2.right*(fixture.Area.width/2+.7f));
                }
            foreach(var target in targets)
            {
                Assert.IsTrue(Free(target),"交互点或货架侧道被占用 "+target);
                Assert.IsTrue(visited.Contains(new Vector2Int(Mathf.RoundToInt(target.x*2),Mathf.RoundToInt(target.y*2))),"通路不连通 "+target);
            }
        }

        [Test]
        public void VersionedCollisionStopsLargeStepsAndRestoresWithoutDuplicateSolids()
        {
            var legacy=Geometry();var old=ScavengeSurvival.Create(12,true);old.layoutVersion=1;
            var oldRun=new ScavengeRun(12,true,old,legacy);
            var futureShelf=new Vector2(3.9f,5.75f);
            Assert.IsTrue(legacy.Walkable(futureShelf));Assert.IsNull(legacy.Shop);
            var geometry=Geometry();var state=ScavengeSurvival.Create(12,true);state.layoutVersion=2;
            var run=new ScavengeRun(12,true,state,geometry);
            Assert.IsFalse(geometry.ClearSegment(new Vector2(23.9f,5),new Vector2(24.3f,5),0),"薄隔墙不能漏过视线检测");
            Assert.IsTrue(geometry.ClearSegment(new Vector2(23,12.5f),new Vector2(25,12.5f),0),"后仓开口不能遮挡视线");
            foreach(var patrol in run.Patrols)patrol.Health=0;
            var player=typeof(ScavengeRun).GetProperty(nameof(ScavengeRun.Player));
            player.SetValue(run,new Vector2(22,5));run.Tick(1,Vector2.right);
            Assert.Less(run.Player.x,23.7f,"薄隔墙不能被大步长跨过");
            player.SetValue(run,new Vector2(2,5));run.Tick(1,Vector2.right);
            Assert.Less(run.Player.x,2.7f,"货架不能穿行");
            player.SetValue(run,geometry.Shop.CheckoutTarget);run.Interact();
            Assert.AreEqual(1,run.OpenCache);run.Tick(1,Vector2.zero);
            Assert.IsTrue(state.caches[1].searched);
            int count=geometry.Solids.Count;
            var save=run.CaptureSurvival();var restored=ScavengeRun.RestoreSurvival(save,geometry);
            Assert.AreEqual(count,geometry.Solids.Count);
            Assert.AreEqual(run.Player,restored.Player);Assert.IsTrue(restored.Survival.caches[1].searched);
            Assert.AreEqual(geometry.Shop.CheckoutTarget,restored.Survival.caches[1].point);
            Assert.Throws<System.InvalidOperationException>(()=>new ScavengeRun(12,true,old,geometry));
        }

        [Test]
        public void CityFixturesFitTheirSharedCollisionAndRegisterAsWholeOccluders()
        {
            var geometry=Geometry();geometry.EnableShopInterior();var catalog=UnityEditor.AssetDatabase.LoadAssetAtPath<RiverValleyCityArtCatalog>("Assets/Game/Art/CityTown/RiverValleyCityArt.asset");
            var parent=new GameObject("室内占地测试");var palette=new Material[5];
            for(int i=0;i<palette.Length;i++)palette[i]=new Material(Shader.Find("Standard"));
            var groups=new List<Renderer[]>();
            try
            {
                var root=RiverValleyShopVisuals.Create(parent.transform,geometry.Shop,catalog,p=>0,groups.Add,palette);
                Assert.AreEqual(14,groups.Count);
                int index=0;
                foreach(var fixture in geometry.Shop.Fixtures)
                {
                    if(fixture.Type==RiverValleyShopInterior.Kind.ExistingBed || fixture.Type==RiverValleyShopInterior.Kind.ExistingCabinet)continue;
                    var bounds=new Bounds();bool first=true;
                    foreach(var renderer in groups[index++]){if(first){bounds=renderer.bounds;first=false;}else bounds.Encapsulate(renderer.bounds);}
                    Assert.IsFalse(first);
                    Assert.GreaterOrEqual(bounds.min.x,fixture.Area.xMin-.02f);Assert.LessOrEqual(bounds.max.x,fixture.Area.xMax+.02f);
                    Assert.GreaterOrEqual(bounds.min.z,fixture.Area.yMin-.02f);Assert.LessOrEqual(bounds.max.z,fixture.Area.yMax+.02f);
                }
                Assert.AreEqual(0,root.GetComponentsInChildren<Collider>().Length,"表现不能另建一套物理碰撞");
                var fallbackGroups=new List<Renderer[]>();
                var fallback=RiverValleyShopVisuals.Create(parent.transform,geometry.Shop,null,p=>0,fallbackGroups.Add,palette);
                Assert.AreEqual(14,fallbackGroups.Count,"无资源检出必须显示全部家具占位");
                Assert.AreEqual(0,fallback.GetComponentsInChildren<Collider>().Length);
            }
            finally{Object.DestroyImmediate(parent);foreach(var material in palette)Object.DestroyImmediate(material);}
        }
    }
}
