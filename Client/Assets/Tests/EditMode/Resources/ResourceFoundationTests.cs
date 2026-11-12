using NUnit.Framework;
using OutpostLoop.Resources.Editor;
namespace OutpostLoop.Resources.Tests
{
    /// <summary>六项编辑器检查：真实源收集、地址与加载器所有权；不验证 Player 资源包。</summary>
    public sealed class ResourceFoundationTests
    {
        [Test] public void UiSourceAndCollectorUseSameLogicalAddress()
        {
            ResourceSourceAudit.EnsureSettings();
            const string path="Assets/Game/Prefabs/UI/AlignmentProbe.prefab";
            var root=new UnityEngine.GameObject("AlignmentProbe");
            try {
                UnityEditor.PrefabUtility.SaveAsPrefabAsset(root,path);
                var setting=UnityEditor.AssetDatabase.LoadAssetAtPath<YooAsset.Editor.AssetBundleCollectorSetting>(ResourceSourceAudit.SettingPath);
                var result=setting.BeginCollect(ResourcePackageRuntime.PackageName,true,false);
                var entry=System.Linq.Enumerable.Single(result.CollectAssets,x=>x.Address=="Prefabs/UI/AlignmentProbe");
                Assert.AreEqual(path,entry.AssetInfo.AssetPath);CollectionAssert.Contains(entry.AssetTags,"startup");
                Assert.NotNull(new EditorSourceResourceLoader().LoadAsset<UnityEngine.GameObject>(entry.Address));
            } finally { UnityEngine.Object.DestroyImmediate(root);UnityEditor.AssetDatabase.DeleteAsset(path); }
        }
        [Test] public void ActualCollectorContainsStartupRule() { ResourceSourceAudit.Audit(); }
        [Test] public void MainIsStartupResourceAndBootIsOnlyPlayerScene()
        {
            var scenes=UnityEditor.EditorBuildSettings.scenes;
            Assert.AreEqual(1,scenes.Length);
            Assert.IsTrue(scenes[0].enabled);
            Assert.AreEqual("Assets/Game/Scenes/Boot.unity",scenes[0].path);
            var setting=UnityEditor.AssetDatabase.LoadAssetAtPath<YooAsset.Editor.AssetBundleCollectorSetting>(ResourceSourceAudit.SettingPath);
            var result=setting.BeginCollect(ResourcePackageRuntime.PackageName,true,false);
            var main=System.Linq.Enumerable.Single(result.CollectAssets,x=>x.Address=="Scenes/Main");
            Assert.AreEqual("Assets/Game/Scenes/Main.unity",main.AssetInfo.AssetPath);
            CollectionAssert.Contains(main.AssetTags,"startup");
            Assert.IsFalse(System.Linq.Enumerable.Any(result.CollectAssets,x=>x.AssetInfo.AssetPath==scenes[0].path));
        }
        [Test] public void EditorSourceReadsRealRule() { Assert.IsNotEmpty(new EditorSourceResourceLoader().LoadAsset<UnityEngine.TextAsset>("tbrule.bytes").bytes); }
        [Test] public void EditorSourceRejectsTraversal() { Assert.Throws<System.ArgumentException>(() => new EditorSourceResourceLoader().LoadAsset<UnityEngine.TextAsset>("../tbrule.bytes")); }

        [Test] public void SwitchingLoadersKeepsSessionOwnersAndRoutesAsyncRelease()
        {
            var first = new OwnedLoader();
            var second = new OwnedLoader();
            try
            {
                Game.ResourceLoader.Use(first);
                var handle = Game.ResourceLoader.LoadAssetAsync<UnityEngine.TextAsset>("probe", _ => { });
                Game.ResourceLoader.Use(second);
                Game.ResourceLoader.Release(handle);
                Assert.AreEqual(1, first.Releases);
                Assert.AreEqual(0, second.Releases);
                Game.ResourceLoader.Use(first);
                Assert.AreEqual(0, first.Disposals + second.Disposals);
                Game.ResourceLoader.Shutdown();
                Game.ResourceLoader.Shutdown();
                Assert.AreEqual(1, first.Disposals);
                Assert.AreEqual(1, second.Disposals);
                Assert.IsFalse(Game.ResourceLoader.IsConfigured);
            }
            finally { Game.ResourceLoader.Shutdown(); }
        }

        // 跟踪两个后端各自收到的异步归还与会话关闭。
        private sealed class OwnedLoader : Game.IResourceLoader, System.IDisposable
        {
            public int Releases, Disposals;
            public T LoadAsset<T>(string path) where T : UnityEngine.Object => null;
            public Game.ResourceLoadHandle LoadAssetAsync<T>(string path, System.Action<T> onLoaded)
                where T : UnityEngine.Object => new Game.ResourceLoadHandle(new object());
            public void Release(Game.ResourceLoadHandle handle) => Releases++;
            public void Dispose() => Disposals++;
        }
    }
}
