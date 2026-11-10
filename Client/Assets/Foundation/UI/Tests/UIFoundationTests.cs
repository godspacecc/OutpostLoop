using System.Collections.Generic;
using Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace OutpostLoop.Tests
{
    /// <summary>仅使用临时对象验证通用 UI，不创建正式视觉资源。</summary>
    public sealed class UIFoundationTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private GameObject Create(string name)
        {
            var value = new GameObject(name);
            objects.Add(value);
            return value;
        }
        [TearDown]
        public void Cleanup()
        {
            UIHelper.SetPrefabLoader(null);
            GameObjectPoolHelper.ClearForTests();
            foreach (var value in objects) if (value != null) Object.DestroyImmediate(value);
            objects.Clear();
        }
        [Test]
        public void CollectorRoundTripPreservesReferenceAndMissingKey()
        {
            var owner = Create("collector");
            var target = Create("target");
            var collector = owner.AddComponent<ReferenceCollector>();
            collector.data.Add(new ReferenceCollectorData { key = "target", gameObject = target, TypeName = typeof(GameObject).FullName });
            collector.OnAfterDeserialize();
            Assert.AreSame(target, collector.Get<GameObject>("target"));
            Assert.IsNull(collector.GetObject("absent"));
        }
        [Test]
        public void ListShrinkAndGrowReusesExistingCells()
        {
            var list = Create("list").AddComponent<CustomListView>();
            list.itemPrefab = Create("item");
            list.totalCount = 2;
            list.RefreshCells();
            var original = list.GetDraggableItems();
            list.totalCount = 1;
            list.RefreshCells();
            Assert.AreEqual(1, list.GetDraggableItems().Count);
            list.totalCount = 2;
            list.RefreshCells();
            CollectionAssert.AreEqual(original, list.GetDraggableItems());
        }
        [Test]
        public void PoolReturnReusesObjectUnderIndependentRoot()
        {
            var prefab = Create("pool item");
            GameObjectPoolHelper.InitPool(prefab, "test", 1);
            var first = GameObjectPoolHelper.GetObjectFromPool(prefab, "test");
            Assert.IsNotNull(first);
            Assert.AreEqual("OutpostLoopUIPool", GameObjectPoolHelper.GetOrCreatePoolRoot().name);
            GameObjectPoolHelper.ReturnObjectToPool(first);
            Assert.AreSame(first, GameObjectPoolHelper.GetObjectFromPool(prefab, "test"));
            GameObjectPoolHelper.ReturnObjectToPool(first);
        }
        [Test]
        public void RecreatedPoolRejectsObjectBorrowedFromRemovedPool()
        {
            const string poolName = "same name";
            var prefab = Create("pool item");
            var borrower = Create("temporary borrower");
            GameObjectPoolHelper.InitPool(prefab, poolName, 1);
            var oldItem = GameObjectPoolHelper.GetObjectFromPool(prefab, poolName);
            oldItem.transform.SetParent(borrower.transform, false);
            GameObjectPoolHelper.RemovePool(poolName);
            GameObjectPoolHelper.InitPool(prefab, poolName, 1);

            GameObjectPoolHelper.ReturnObjectToPool(oldItem);
            var newItem = GameObjectPoolHelper.GetObjectFromPool(prefab, poolName);
            Assert.AreNotSame(oldItem, newItem);
            Assert.AreSame(borrower.transform, oldItem.transform.parent);
            Assert.IsTrue(oldItem.activeSelf);

            GameObjectPoolHelper.ReturnObjectToPool(newItem);
            Assert.AreSame(newItem, GameObjectPoolHelper.GetObjectFromPool(prefab, poolName));
        }
        [Test]
        public void BaseUIAwakePreservesAuthoredColor()
        {
            var owner = Create("Message");
            var panel = Create("Panel");
            panel.transform.SetParent(owner.transform);
            var image = panel.AddComponent<Image>();
            var expected = new Color(0.9f, 0.2f, 0.7f, 0.8f);
            image.color = expected;
            var view = owner.AddComponent<BaseUI>();
            // 直接调用托管生命周期，避免 EditMode SendMessage 触发引擎运行状态断言。
            typeof(BaseUI).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(view, null);
            Assert.AreEqual(expected, image.color);
        }
        [Test]
        public void InjectedLoaderReceivesExactGeneratedRelativePath()
        {
            var prefab = Create("temporary view");
            string observed = null;
            UIHelper.SetPrefabLoader(path => { observed = path; return prefab; });
            Assert.AreSame(prefab, UIHelper.LoadPrefab("Prefabs/UI/Test/Window"));
            Assert.AreEqual("Prefabs/UI/Test/Window", observed);
        }
    }
}
