using NUnit.Framework;
using QFramework;
using UnityEngine;
namespace OutpostLoop.Tests
{
    /// <summary>四项限定集成检查，证明技术底座被原型实际消费。</summary>
    public sealed class FoundationIntegrationTests
    {
        [SetUp] public void PrepareFoundation() { Game.FoundationRuntime.Initialize(); }

        [TearDown] public void Cleanup(){Game.FoundationRuntime.Shutdown();}
        [Test] public void ArchitectureOwnsRunAndPublishesStart()
        {
            var a=Game.Game.Interface;a.RegisterModel(new PrototypeModel());a.RegisterSystem(new PrototypeSystem());
            int count=0;var sub=a.RegisterEvent<PrototypeRunStarted>(_=>count++);
            var world=a.GetSystem<PrototypeSystem>().Begin(91,true);
            Assert.AreSame(world,a.GetModel<PrototypeModel>().World);Assert.AreEqual(1,count);sub.UnRegister();
        }
        [Test] public void PrototypeReadsGeneratedConfig()
        {
            var config=PrototypeServices.LoadConfig();var state=new RunState(config);
            Assert.AreSame(config,state.Config);Assert.AreEqual(config.InitialHealth,state.Health);
            state.Tick(config.Duration,0);Assert.AreEqual(1,state.Outcome);
        }
        [Test] public void LifecycleDeduplicatesAndUnregisters()
        {
            int calls=0;System.Action action=()=>calls++;
            Game.LifeCycle.RegisterUpdate(action);Game.LifeCycle.RegisterUpdate(action);Game.LifeCycle.Update();
            Game.LifeCycle.UnregisterUpdate(action);Game.LifeCycle.Update();Assert.AreEqual(1,calls);
        }
        [Test] public void TimerRespectsScaledAndUnscaledTime()
        {
            int scaled=0,unscaled=0;Game.TimerMgr.Instance.Add(()=>{scaled++;return false;},delay:1);
            Game.TimerMgr.Instance.Add(()=>{unscaled++;return false;},delay:1,useUnscaledTime:true);
            Game.TimerMgr.Instance.TickForTests(0,1);Assert.AreEqual(0,scaled);Assert.AreEqual(1,unscaled);
            Game.TimerMgr.Instance.TickForTests(1,0);Assert.AreEqual(1,scaled);
        }
    }
}
