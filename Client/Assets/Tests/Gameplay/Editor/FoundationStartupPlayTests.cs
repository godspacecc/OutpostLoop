using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;
namespace OutpostLoop.Tests
{
    /// <summary>四项真实 Play 检查；所有编辑器设置在 finally 中恢复。</summary>
    public sealed class FoundationStartupPlayTests
    {
        private static IEnumerator WaitForMain()
        {
            for(int frame=0;frame<600 && Object.FindObjectOfType<PrototypeController>()==null;frame++)yield return null;
            Assert.NotNull(Object.FindObjectOfType<PrototypeController>(), "Boot 未成功进入 Main");
            // Main Start 握手后，Boot 入口与加载界面应释放。
            for(int frame=0;frame<10;frame++)yield return null;
            Assert.AreEqual("Main",UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            Assert.IsEmpty(Object.FindObjectsOfType<Boot.Launcher>(), "Main 就绪后应销毁 Boot 入口");
        }
        [UnityTest] public IEnumerator MainReloadKeepsSingleFoundation()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            Assert.IsTrue(Game.FoundationRuntime.IsInitialized);
            var old=Object.FindObjectOfType<PrototypeController>();Assert.NotNull(old.World);
            var previous=old.World;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Game/Scenes/Main.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var current=Object.FindObjectOfType<PrototypeController>();
            Assert.NotNull(current.World);Assert.AreNotSame(previous,current.World);
            Assert.IsTrue(Game.ResourceLoader.IsConfigured);
            Assert.AreEqual(1,Object.FindObjectsOfType<Game.FoundationLoop>().Length);
            yield return new ExitPlayMode();
            Assert.IsFalse(Game.FoundationRuntime.IsInitialized);
            Assert.IsFalse(Game.ResourceLoader.IsConfigured);
        }
        private static GameObject temporaryPrefab;
        private static GameObject LoadTemporaryPrefab(string address) => temporaryPrefab;
        [UnityTest] public IEnumerator BootResourceSessionReusesAndReleasesSyncHandle()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            const string address = "tbrule.bytes";
            var probe = YooAsset.YooAssets.GetPackage("OutpostLoop").LoadAssetSync<TextAsset>(address);
            try
            {
                Assert.NotNull(probe.AssetObject);
                // 已有 Main 读过规则，用独立句柄观测同一个 YooAsset 提供器的真实引用计数。
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var provider = typeof(YooAsset.AssetHandle).BaseType.GetProperty("Provider", flags).GetValue(probe);
                var count = provider.GetType().GetProperty("RefCount");
                var before = (int)count.GetValue(provider);
                Assert.GreaterOrEqual(before, 2, "应有会话与独立探针各持有一个句柄");
                Assert.AreSame(probe.AssetObject, Game.ResourceLoader.LoadAsset<TextAsset>(address));
                Assert.AreSame(probe.AssetObject, Game.ResourceLoader.LoadAsset<TextAsset>(address));
                Assert.AreEqual(before, count.GetValue(provider));
                Game.FoundationRuntime.Shutdown();
                Game.FoundationRuntime.Shutdown();
                Assert.AreEqual(before - 1, count.GetValue(provider));
                Assert.NotNull(probe.AssetObject, "会话关闭不得释放调用方自己的句柄");
            }
            finally { probe.Release(); }
            yield return new ExitPlayMode();
        }
        [UnityTest] public IEnumerator OpenWindowUnloadsBeforeResourcesAndOnlyOnce()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            temporaryPrefab=new GameObject("temporary shutdown view",typeof(RectTransform),typeof(Game.BaseUI));
            Game.UIHelper.SetPrefabLoader(LoadTemporaryPrefab);
            var ctrl=Game.UIHelper.ShowUI<ShutdownProbe>();
            Assert.IsTrue(ctrl.IsShow());
            Game.FoundationRuntime.Shutdown();
            Assert.IsFalse(ctrl.IsShow(),"退出应立即复位窗口状态，不等待帧末");
            Assert.AreEqual(1,ctrl.UnloadCount);
            Assert.IsTrue(ctrl.ResourcesWereReady,"卸载回调必须先于资源入口关闭");
            Object.Destroy(temporaryPrefab);temporaryPrefab=null;
            yield return null;
            Assert.AreEqual(1,ctrl.UnloadCount,"帧末销毁不得重复触发卸载回调");
            yield return new ExitPlayMode();
        }
        /// <summary>只记录卸载时序的独立测试窗口，不创建正式资源。</summary>
        public sealed class ShutdownProbe : Game.BaseUICtrl
        {
            public int UnloadCount; public bool ResourcesWereReady;
            protected override string Path()=>"Prefabs/UI/Test/Shutdown";
            public override Game.UILayer Layer()=>Game.UILayer.Dialog;
            protected override void OnInit(){} protected override void OnShow(){} protected override void OnHide(){}
            protected override void OnUnload(){UnloadCount++;ResourcesWereReady=Game.ResourceLoader.IsConfigured;}
        }
        [UnityTest] public IEnumerator ReenterWithoutDomainReloadStartsClean()
        {
            var wasEnabled=EditorSettings.enterPlayModeOptionsEnabled;
            var options=EditorSettings.enterPlayModeOptions;
            try {
                EditorSettings.enterPlayModeOptionsEnabled=true;
                EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;
                EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
                for(int run=0;run<2;run++) {
                    yield return new EnterPlayMode(false);
                    yield return WaitForMain();
                    var controller=Object.FindObjectOfType<PrototypeController>();
                    Assert.NotNull(controller.World);Assert.IsTrue(Game.ResourceLoader.IsConfigured);
                    Assert.AreEqual(0,controller.World.Run.Elapsed);
                    yield return new ExitPlayMode();
                    Assert.IsFalse(Game.FoundationRuntime.IsInitialized);
                    Assert.IsFalse(Game.ResourceLoader.IsConfigured);
                }
            } finally {EditorSettings.enterPlayModeOptions=options;EditorSettings.enterPlayModeOptionsEnabled=wasEnabled;}
        }
    }
}
