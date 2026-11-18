using NUnit.Framework;
using UnityEngine;
using OutpostLoop.RiverValley;

namespace OutpostLoop.Tests
{
    /// <summary>验证旋转建筑碰撞及河道通行和视线的不同语义。</summary>
    public sealed class RiverValleyWorldGeometryTests
    {
        [Test]
        public void RotatedBuildingsKeepTheirPaddingAtTheCorners()
        {
            var solid = new RiverValleyWorldGeometry.Solid(new Vector2(10,10),new Vector2(4,2),Mathf.PI/2);
            Assert.IsTrue(solid.Contains(new Vector2(10,11.8f),0));
            Assert.IsFalse(solid.Contains(new Vector2(11.8f,10),0));
            Assert.IsTrue(solid.Contains(new Vector2(11.2f,10),.34f));
            var post = new RiverValleyWorldGeometry.Solid(Vector2.zero,Vector2.one*.4f,Mathf.PI/4);
            Assert.IsTrue(post.Contains(new Vector2(.75f,0),.34f),"粗筛不能漏掉旋转后的碰撞边角");
        }

        [Test]
        public void RiverBlocksWalkingButOnlyBuildingsBlockSight()
        {
            var world = new RiverValleyWorldGeometry();
            world.Water.Add(new RiverValleyWorldGeometry.Channel(new Vector2(0,-10),new Vector2(0,10),10));
            Assert.IsFalse(world.Walkable(Vector2.zero,.34f));
            Assert.IsTrue(world.ClearBody(Vector2.zero,0));
            world.Bridges.Add(new RiverValleyWorldGeometry.Channel(new Vector2(-8,0),new Vector2(8,0),4));
            Assert.IsTrue(world.Walkable(Vector2.zero,.34f));
            Assert.IsFalse(world.Walkable(new Vector2(0,3),.34f));
            world.Solids.Add(new RiverValleyWorldGeometry.Solid(Vector2.zero,Vector2.one,0));
            Assert.IsFalse(world.ClearBody(Vector2.zero,0));
            Assert.IsFalse(world.Walkable(Vector2.zero,.34f));
        }
    }
}
