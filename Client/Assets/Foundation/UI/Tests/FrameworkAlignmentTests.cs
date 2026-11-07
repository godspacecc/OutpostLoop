using System;
using System.Linq;
using System.Reflection;
using Game;
using NUnit.Framework;
using UnityEngine;
namespace OutpostLoop.Tests
{
    /// <summary>限定五项：公共资源入口、UI 相对地址、安全区内容映射。</summary>
    public sealed class FrameworkAlignmentTests
    {
        private static Type Find(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null);
        [Test] public void CommonResourceEntryExists()
        {
            Assert.NotNull(Find("Game.IResourceLoader"));
            Assert.NotNull(Find("Game.ResourceLoader"));
            Assert.NotNull(Find("Game.YooAssetResourceLoader"));
        }
        [Test] public void ControllerResolvesRelativeUiAddress()
        {
            string observed = null;
            UIHelper.SetPrefabLoader(path => { observed = path; return null; });
            try { using (Log.CaptureForTests((type, message) => {})) new Probe().Show();
                Assert.AreEqual("Prefabs/UI/Probe/Window", observed);
            } finally { UIHelper.SetPrefabLoader(null); }
        }
        [Test] public void SafeAreaMapsContentAndKeepsFullscreenBackground()
        {
            var type = Find("Game.SafeAreaFitter"); Assert.NotNull(type);
            var root = new GameObject("safe area test", typeof(RectTransform));
            try {
                var rect = (RectTransform)root.transform; rect.anchorMin=Vector2.zero; rect.anchorMax=Vector2.one; rect.sizeDelta=Vector2.zero;
                var child = new GameObject("content", typeof(RectTransform)); child.transform.SetParent(root.transform,false);
                var content=(RectTransform)child.transform; content.anchorMin=Vector2.zero; content.anchorMax=Vector2.one;
                var fitter=root.AddComponent(type);
                var apply=type.GetMethod("ApplyAnchors",BindingFlags.Instance|BindingFlags.NonPublic);
                apply.Invoke(fitter,new object[]{new Vector2(.1f,.2f),new Vector2(.9f,.8f)});
                Assert.AreEqual(Vector2.zero,rect.anchorMin); Assert.AreEqual(Vector2.one,rect.anchorMax);
                Assert.AreEqual(new Vector2(.1f,.2f),content.anchorMin); Assert.AreEqual(new Vector2(.9f,.8f),content.anchorMax);
                apply.Invoke(fitter,new object[]{Vector2.zero,Vector2.one});
                Assert.AreEqual(Vector2.zero,content.anchorMin); Assert.AreEqual(Vector2.one,content.anchorMax);
            } finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        [Test] public void DefaultUiLoaderUsesSharedResourceEntry()
        {
            var prefab=new GameObject("shared loader probe");
            var loader=new ProbeLoader{Asset=prefab};
            try {
                ResourceLoader.Use(loader); UIHelper.SetPrefabLoader(null);
                Assert.AreSame(prefab,UIHelper.LoadPrefab("Prefabs/UI/Probe/Window"));
                Assert.AreEqual("Prefabs/UI/Probe/Window",loader.Address);
            } finally { ResourceLoader.Use(null); UnityEngine.Object.DestroyImmediate(prefab); }
        }
        [Test] public void OpenedWindowAutomaticallyReceivesSafeAreaFitter()
        {
            var root=new GameObject("temporary UI root",typeof(RectTransform),typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler),typeof(UnityEngine.UI.GraphicRaycaster));
            root.SetActive(false);
            foreach(var name in new[]{"HUD","Dialog","Popup","Tips","Top"})new GameObject(name,typeof(RectTransform)).transform.SetParent(root.transform,false);
            new GameObject("EventSystem",typeof(UnityEngine.EventSystems.EventSystem),typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule)).transform.SetParent(root.transform,false);
            root.AddComponent<UIRoot>(); root.SetActive(true);
            var prefab=new GameObject("temporary window",typeof(RectTransform),typeof(BaseUI));
            var loader=new ProbeLoader{Asset=prefab};
            var ctrl=new Probe();
            try {
                ResourceLoader.Use(loader); UIHelper.SetPrefabLoader(null);ctrl=UIHelper.ShowUI<Probe>();
                Assert.IsTrue(ctrl.IsShow());
                Assert.NotNull(root.GetComponentInChildren<SafeAreaFitter>(true));
                UIHelper.Shutdown();
                Assert.IsNull(UIHelper.GetUICtrl<Probe>());
                Assert.IsNull(root.GetComponentInChildren<SafeAreaFitter>(true));
                Assert.IsFalse(ctrl.IsShow());
                Assert.AreEqual("Prefabs/UI/Probe/Window",loader.Address);
            } finally {
                ctrl.Hide();UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(prefab);
                UIRoot.ClearForTests();ResourceLoader.Use(null);

            }
        }
        private sealed class ProbeLoader : IResourceLoader
        {
            public GameObject Asset; public string Address;
            public T LoadAsset<T>(string path) where T:UnityEngine.Object { Address=path;return Asset as T; }
            public ResourceLoadHandle LoadAssetAsync<T>(string path,Action<T> callback) where T:UnityEngine.Object { callback?.Invoke(LoadAsset<T>(path));return ResourceLoadHandle.Empty; }
            public void Release(ResourceLoadHandle handle) {}
        }
        private sealed class Probe : BaseUICtrl { public Probe() {} protected override string Path() => "Probe/Window"; public override UILayer Layer() => UILayer.Dialog; protected override void OnInit() {} protected override void OnShow() {} protected override void OnHide() {} }
    }
}
