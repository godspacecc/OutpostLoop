using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;
namespace OutpostLoop.Tests
{
    /// <summary>三项真实 Play 检查；所有编辑器设置在 finally 中恢复。</summary>
    public sealed class FoundationStartupPlayTests
    {
        [UnityTest] public IEnumerator SceneReloadStartsFreshFoundation()
        {
            EditorSceneManager.OpenScene("Assets/Prototype/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            Assert.IsTrue(Game.FoundationRuntime.IsInitialized);
            var old=Object.FindObjectOfType<PrototypeController>();Assert.NotNull(old.World);
            var previous=old.World;
            yield return SceneManager.LoadSceneAsync("Assets/Prototype/Scenes/Boot.unity");
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
        [UnityTest] public IEnumerator OpenWindowUnloadsBeforeResourcesAndOnlyOnce()
        {
            EditorSceneManager.OpenScene("Assets/Prototype/Scenes/Boot.unity");
            yield return new EnterPlayMode();
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
            protected override string Path()=>"Test/Shutdown";
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
                EditorSceneManager.OpenScene("Assets/Prototype/Scenes/Boot.unity");
                for(int run=0;run<2;run++) {
                    yield return new EnterPlayMode(false);
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
