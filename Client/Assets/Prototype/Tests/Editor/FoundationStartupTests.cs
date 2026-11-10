using NUnit.Framework;
using UnityEngine;
using Game;
namespace OutpostLoop.Tests
{
    /// <summary>限定基础入口初始化、失败回滚、拥有者清理和重复入口隔离。</summary>
    public sealed class FoundationStartupTests
    {
        // EditMode 不自动调用普通 MonoBehaviour 的 Awake，显式调用相同生命周期方法。
        private static void Open(GameObject root)
        {
            var loop=root.AddComponent<FoundationLoop>();
            typeof(FoundationLoop).GetMethod("Awake",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(loop,null);
        }
        private static void Close(GameObject root)
        {
            if(root==null)return;
            var loop=root.GetComponent<FoundationLoop>();
            if(loop!=null)typeof(FoundationLoop).GetMethod("OnDestroy",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(loop,null);
            Object.DestroyImmediate(root);
        }
        [TearDown] public void Cleanup() { Game.FoundationRuntime.Shutdown(); }
        [SetUp] public void Prepare(){Game.FoundationRuntime.Shutdown();ResourceLoader.Use(null);}
        [Test] public void LoopInitializesResourcesWithoutPrototype()
        {
            var root=new GameObject("foundation only");
            try {Open(root);Assert.IsTrue(ResourceLoader.IsConfigured);}
            finally {Close(root);}
        }
        [Test] public void DestroyingOwnerReleasesResources()
        {
            var root=new GameObject("foundation owner");Open(root);
            ResourceLoader.Use(new OutpostLoop.Resources.EditorSourceResourceLoader());
            Close(root);Assert.IsFalse(ResourceLoader.IsConfigured);
        }
        [Test] public void ShutdownClearsTimersAndCallbacksAndCanRestart()
        {
            Game.FoundationRuntime.Initialize();
            var timer = Game.TimerMgr.Instance;
            int frames = 0, staleTimers = 0, quits = 0, freshTimers = 0;
            try
            {
                Game.LifeCycle.RegisterUpdate(() => frames++);
                Game.LifeCycle.RegisterQuit(() =>
                {
                    quits++;
                    timer.Add(() => { staleTimers++; return true; });
                });
                timer.Add(() => { staleTimers++; return true; });
                Game.FoundationRuntime.Shutdown();
                Game.FoundationRuntime.Shutdown();
                Game.LifeCycle.Update();
                timer.TickForTests(1);
                Assert.AreEqual(0, frames);
                Assert.AreEqual(0, staleTimers, "退出回调中新建的计时器也应清理");
                Assert.AreEqual(1, quits);

                Game.FoundationRuntime.Initialize();
                Assert.IsTrue(ResourceLoader.IsConfigured);
                timer.Add(() => { freshTimers++; return true; });
                Game.LifeCycle.Update();
                Assert.AreEqual(1, freshTimers, "新会话的计时器应重新订阅帧更新");
            }
            finally { timer.Clear(); }
        }
        [Test] public void InitializeIsIdempotent()
        {
            Game.FoundationRuntime.Initialize();var loader=ResourceLoader.Current;var architecture=Game.Game.Interface;
            Game.FoundationRuntime.Initialize();
            Assert.AreSame(loader,ResourceLoader.Current);Assert.AreSame(architecture,Game.Game.Interface);
            Game.FoundationRuntime.Shutdown();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FailedInitializationReleasesSessionAndAllowsFreshStartup(bool failDuringArchitecture)
        {
            var previous = Game.Game.OnRegisterPatch;
            QFramework.IArchitecture failedArchitecture = null;
            var fault = new SystemStartupFault();
            try
            {
                Game.Game.OnRegisterPatch = candidate =>
                {
                    previous?.Invoke(candidate);
                    failedArchitecture = candidate;
                    if (failDuringArchitecture) candidate.RegisterSystem(fault);
                    else candidate.RegisterUtility<IDisplayResolutionApplier>(new DisplayStartupFault());
                };
                Assert.Throws<System.InvalidOperationException>(() => FoundationRuntime.Initialize());
                Assert.IsFalse(FoundationRuntime.IsInitialized);
                Assert.IsFalse(ResourceLoader.IsConfigured);
                Assert.AreEqual(failDuringArchitecture ? 1 : 0, fault.Cleanups);

                Game.Game.OnRegisterPatch = previous;
                FoundationRuntime.Initialize();
                Assert.IsTrue(FoundationRuntime.IsInitialized);
                Assert.AreNotSame(failedArchitecture, Game.Game.Interface);
            }
            finally
            {
                Game.Game.OnRegisterPatch = previous;
                FoundationRuntime.Shutdown();
            }
        }

        // 两个入口分别注入架构组件初始化错误和架构建成后的显示设置错误。
        private sealed class SystemStartupFault : QFramework.AbstractSystem
        {
            public int Cleanups { get; private set; }
            protected override void OnInit() => throw new System.InvalidOperationException("Injected system startup failure");
            protected override void OnDeinit() => Cleanups++;
        }

        private sealed class DisplayStartupFault : IDisplayResolutionApplier
        {
            public void Apply(DisplayResolutionPreset preset, FullScreenMode mode) =>
                throw new System.InvalidOperationException("Injected display startup failure");
        }
        [Test] public void DuplicateDoesNotShutdownOwner()
        {
            var first=new GameObject("first");var second=new GameObject("duplicate");
            try {
                Open(first);Open(second);
                Close(second);Assert.IsTrue(ResourceLoader.IsConfigured);
            } finally {if(second!=null)Close(second);Close(first);}
        }
    }
}
