using System;
using System.Reflection;
using Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
namespace OutpostLoop.Tests
{
    /// <summary>限定九项，验证与 3DBase 一致的 UI 行为与公共接口。</summary>
    public sealed class ReferenceUiAlignmentTests
    {
        private GameObject root, prefab;
        [SetUp] public void Prepare()
        {
            root=new GameObject("UI reference probe",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            root.SetActive(false);
            foreach(var name in new[]{"HUD","Dialog","Popup","Tips","Top"})new GameObject(name,typeof(RectTransform)).transform.SetParent(root.transform,false);
            new GameObject("EventSystem",typeof(UnityEngine.EventSystems.EventSystem),typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule)).transform.SetParent(root.transform,false);
            root.AddComponent<UIRoot>();root.SetActive(true);
            prefab=new GameObject("probe",typeof(RectTransform),typeof(BaseUI));
            UIHelper.SetPrefabLoader(_=>prefab);
        }
        [TearDown] public void Cleanup(){UIHelper.Shutdown();UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(prefab);UIRoot.ClearForTests();}
        private static RectTransform Full(string name,Transform parent)
        {
            var r=(RectTransform)new GameObject(name,typeof(RectTransform)).transform;r.SetParent(parent,false);
            r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.sizeDelta=Vector2.zero;return r;
        }
        private static void Apply(GameObject owner)
        {
            var fitter=owner.AddComponent<SafeAreaFitter>();
            typeof(SafeAreaFitter).GetMethod("ApplyAnchors",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(fitter,new object[]{new Vector2(.1f,.2f),new Vector2(.9f,.8f)});
        }
        [Test] public void NestedContentInsideBackgroundReceivesSafeArea()
        {
            var area=Full("area",root.transform);var background=Full("background",area);background.gameObject.AddComponent<Image>();
            var content=Full("content",background);Apply(area.gameObject);
            Assert.AreEqual(Vector2.zero,background.anchorMin);Assert.AreEqual(new Vector2(.1f,.2f),content.anchorMin);
        }
        [Test] public void FullscreenSelectableIsContentInsteadOfBackground()
        {
            var area=Full("area",root.transform);var button=Full("button",area);button.gameObject.AddComponent<Image>();button.gameObject.AddComponent<Button>();Apply(area.gameObject);
            Assert.AreEqual(new Vector2(.1f,.2f),button.anchorMin);
        }
        [Test] public void TopWindowForcesLockedTransientPopupToHide()
        {
            var popup=UIHelper.ShowUI<LockedPopup>();popup.Hide();Assert.IsTrue(popup.IsShow());
            UIHelper.ShowUI<Top>();Assert.IsFalse(popup.IsShow());
        }
        [Test] public void MovingHiddenTopDoesNotCloseVisiblePopup()
        {
            var top=UIHelper.ShowUI<Top>();top.Hide();var popup=UIHelper.ShowUI<Popup>();
            top.MoveToTop();Assert.IsTrue(popup.IsShow());Assert.IsFalse(top.IsShow());
        }
        [Test] public void ControllerOffersVirtualCreateView()
        {
            UIHelper.SetPrefabLoader(_=>throw new InvalidOperationException("动态视图不应访问 Prefab 加载器"));
            var ctrl=UIHelper.ShowUI<Dynamic>();Assert.IsTrue(ctrl.IsShow());ctrl.Hide();Assert.IsFalse(ctrl.IsShow());
        }
        [Test] public void GridOffersResourcePathLoadingContract()
        {
            var loader=new Loader{Asset=prefab};ResourceLoader.Use(loader);
            try {var grid=Full("grid",root.transform).gameObject.AddComponent<CustomGridLayoutGroup>();
                int refreshed=0;grid.AddItemRefreshListener((item,index)=>refreshed++);
                grid.LoadAndFillGrid("Prefabs/UI/Test/Item",2);Assert.AreEqual(2,refreshed);Assert.AreEqual("Prefabs/UI/Test/Item",loader.Address);Assert.AreEqual(2,grid.GetDraggableItems().Count);
            } finally {ResourceLoader.Use(null);}
        }
        [Test] public void SimpleListCountRemainsSerializedField()
        {
            Assert.NotNull(typeof(CustomListView).GetField("totalCount"));
        }
        [Test] public void LoopGridBuildsMissingViewportAndContent()
        {
            var owner=Full("grid",root.transform).gameObject;owner.SetActive(false);
            var grid=owner.AddComponent<LoopGridLayoutGroup>();
            using(Log.CaptureForTests((_,__)=>{}))
            {
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages=true;
                try{grid.EnsureGridContent();}finally{UnityEngine.TestTools.LogAssert.ignoreFailingMessages=false;}
            }
            Assert.NotNull(grid.viewport);Assert.NotNull(grid.content);Assert.AreSame(grid.viewport,grid.content.parent);
        }
        [Test] public void LoopGridOffersSerializedGridPrefab()
        {
            var owner=Full("grid",root.transform).gameObject;owner.SetActive(false);var grid=owner.AddComponent<LoopGridLayoutGroup>();
            grid.gridPrefab=prefab;grid.totalCount=3;grid.RefreshCells();Assert.AreEqual(3,grid.content.childCount);
            grid.ClearCells();Assert.AreEqual(0,grid.content.childCount);Assert.AreEqual(0,grid.totalCount);
        }
        private sealed class Loader:IResourceLoader
        {
            public GameObject Asset;public string Address;
            public T LoadAsset<T>(string address) where T:UnityEngine.Object {Address=address;return Asset as T;}
            public ResourceLoadHandle LoadAssetAsync<T>(string address,Action<T> callback) where T:UnityEngine.Object {callback?.Invoke(LoadAsset<T>(address));return ResourceLoadHandle.Empty;}
            public void Release(ResourceLoadHandle handle){}
        }
        public sealed class Dynamic:Popup
        {
            protected override string Path()=>null;
            protected override BaseUI CreateView()=>new GameObject("dynamic view",typeof(RectTransform),typeof(BaseUI)).GetComponent<BaseUI>();
        }
        public class Popup : BaseUICtrl
        {
            protected override string Path()=>"Prefabs/UI/Test/Popup";public override UILayer Layer()=>UILayer.Popup;
            protected override void OnInit(){}protected override void OnShow(){}protected override void OnHide(){}
        }
        public sealed class LockedPopup:Popup { protected override bool CanHide()=>false; }
        public sealed class Top:Popup { public override UILayer Layer()=>UILayer.Top; }
    }
}
