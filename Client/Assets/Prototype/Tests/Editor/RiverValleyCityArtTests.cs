using System.Linq;
using NUnit.Framework;
using UnityEngine;
using OutpostLoop.RiverValley;

namespace OutpostLoop.Tests
{
    /// <summary>核对资产真实依赖和替换前后通行几何，避免美术换件改变已确认地图。</summary>
    public sealed class RiverValleyCityArtTests
    {
        [Test]
        public void BakedArtKeepsUnitFootprintsAndWorkingMaterials()
        {
            var catalog=RiverValleyCityArtCatalog.Load();Assert.NotNull(catalog);Assert.AreEqual(41,catalog.entries.Length);
            Assert.AreEqual(catalog.entries.Length,catalog.entries.Select(e=>e.key).Distinct().Count());
            foreach(var entry in catalog.entries)
            {
                Assert.NotNull(entry.mesh,entry.key);Assert.That(entry.mesh.vertexCount,Is.GreaterThan(0),entry.key);
                var bounds=entry.mesh.bounds;
                Assert.That(bounds.min.x,Is.GreaterThanOrEqualTo(-.501f),entry.key);Assert.That(bounds.max.x,Is.LessThanOrEqualTo(.501f),entry.key);
                Assert.That(bounds.min.y,Is.GreaterThanOrEqualTo(-.501f),entry.key);Assert.That(bounds.max.y,Is.LessThanOrEqualTo(.501f),entry.key);
                Assert.That(bounds.min.z,Is.GreaterThanOrEqualTo(-.501f),entry.key);Assert.That(bounds.max.z,Is.LessThanOrEqualTo(.501f),entry.key);
                Assert.AreEqual(entry.mesh.subMeshCount,entry.materials.Length,entry.key);
                foreach(var material in entry.materials) {Assert.NotNull(material,entry.key);Assert.AreEqual("Standard",material.shader.name,entry.key);Assert.IsTrue(material.shader.isSupported,entry.key);if(!material.name.Contains("Glass"))Assert.NotNull(material.mainTexture,entry.key+" 原贴图缺失");}
            }
        }

        [Test]
        public void CityReplacementKeepsEverySolidBridgeAndCacheInPlace()
        {
            var content=UnityEngine.Resources.Load<RiverValleyGameplayContent>("Prototype/RiverValleyGameplayContent");
            var parent=new GameObject("美术几何对照");
            try
            {
                var before=RiverValleyTownPreview.CreateWorld(parent.transform,content.layout,content.surface,false);
                typeof(RiverValleyTownPreview).GetMethod("Awake",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(before,null);
                var solids=before.WorldGeometry.Solids.ToArray();var bridges=before.WorldGeometry.Bridges.ToArray();var caches=(Vector2[])before.WorldGeometry.Caches.Clone();
                Object.DestroyImmediate(before.gameObject);
                var after=RiverValleyTownPreview.CreateWorld(parent.transform,content.layout,content.surface);
                typeof(RiverValleyTownPreview).GetMethod("Awake",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(after,null);
                Assert.That(after.CityDressingFixtureCount,Is.GreaterThan(200));
                Assert.That(after.CityDressingSurfaceCount,Is.GreaterThan(0));
                Assert.That(after.CityDistrictPadCount,Is.GreaterThanOrEqualTo(4));
                foreach(var sample in after.DressingGroundSamples)Assert.IsTrue(after.WorldGeometry.Walkable(sample,.34f),"地表陈设覆盖障碍 "+sample);
                Assert.That(after.CityBuildingCount,Is.GreaterThan(200));Assert.That(after.CityTreeCount,Is.GreaterThan(100));Assert.That(after.CityVehicleCount,Is.GreaterThan(0));Assert.That(after.CityRoadSegmentCount,Is.GreaterThan(100));
                Assert.AreEqual(solids.Length,after.WorldGeometry.Solids.Count,"替换不得增删障碍");
                for(int i=0;i<solids.Length;i++) {var actual=after.WorldGeometry.Solids[i];Assert.AreEqual(solids[i].Center,actual.Center,"位置 "+i);Assert.AreEqual(solids[i].Half,actual.Half,"占地 "+i);Assert.AreEqual(solids[i].Angle,actual.Angle,"旋转 "+i);}
                Assert.AreEqual(bridges.Length,after.WorldGeometry.Bridges.Count);
                for(int i=0;i<bridges.Length;i++) {Assert.AreEqual(bridges[i].A,after.WorldGeometry.Bridges[i].A);Assert.AreEqual(bridges[i].B,after.WorldGeometry.Bridges[i].B);Assert.AreEqual(bridges[i].Width,after.WorldGeometry.Bridges[i].Width);}
                CollectionAssert.AreEqual(caches,after.WorldGeometry.Caches);
            }
            finally {Object.DestroyImmediate(parent);}
        }
        [Test]
        public void FilledShelvesKeepContentsWithinExistingCollisionFootprint()
        {
            var catalog=RiverValleyCityArtCatalog.Load();
            foreach(bool warehouse in new[]{false,true})
            {
                var prop=GameObject.CreatePrimitive(PrimitiveType.Cube);
                try
                {
                    catalog.Replace(prop.transform,warehouse?"shelf":"desk");
                    var before=prop.GetComponent<MeshFilter>().sharedMesh.vertexCount;
                    RiverValleyShelfDressing.Fill(prop.transform,catalog,warehouse);
                    var mesh=prop.GetComponent<MeshFilter>().sharedMesh;Assert.Greater(mesh.vertexCount,before);
                    Assert.That(mesh.bounds.min.x,Is.GreaterThanOrEqualTo(-.501f));Assert.That(mesh.bounds.max.x,Is.LessThanOrEqualTo(.501f));
                    Assert.That(mesh.bounds.min.z,Is.GreaterThanOrEqualTo(-.501f));Assert.That(mesh.bounds.max.z,Is.LessThanOrEqualTo(.501f));
                    Assert.AreEqual(1,prop.GetComponents<MeshRenderer>().Length,"内容与柜台保持同一遮挡对象");
                }
                finally {Object.DestroyImmediate(prop);}
            }
        }
    }
}
