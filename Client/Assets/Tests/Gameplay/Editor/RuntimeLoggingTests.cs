using System;
using System.IO;
using System.Linq;
using Game;
using QFramework;
using NUnit.Framework;
using UnityEngine;
namespace OutpostLoop.Tests
{
    /// <summary>限定五项：注册、真实回调、容量、文件轮转和导出。</summary>
    public sealed class RuntimeLoggingTests
    {
        private string folder;
        [SetUp] public void Setup(){folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Backup/Artifacts/RuntimeLogging-20261107/"+Guid.NewGuid().ToString("N")));FoundationRuntime.Initialize();}
        [TearDown] public void Cleanup(){FoundationRuntime.Shutdown();}
        [Test] public void RegistersAndUnsubscribesOnShutdown()
        {
            var system=Game.Game.Interface.GetSystem<RuntimeLogSystem>();Assert.NotNull(system);
            FoundationRuntime.Shutdown();Debug.Log("RuntimeLog after shutdown probe");Assert.AreEqual(0,system.DrainForTests(100));
        }
        [Test] public void RealUnityLogReachesModel()
        {
            var marker="RuntimeLog probe "+Guid.NewGuid();Debug.Log(marker);
            Game.Game.Interface.GetSystem<RuntimeLogSystem>().DrainForTests(100);
            Assert.IsTrue(Game.Game.Interface.GetModel<RuntimeLogModel>().Snapshot().Any(e=>e.Message==marker));
        }
        [Test] public void ModelKeepsLatestFiveHundredEntries()
        {
            var model=Game.Game.Interface.GetModel<RuntimeLogModel>();model.Clear();
            model.AddRange(Enumerable.Range(0,601).Select(i=>new RuntimeLogEntry(DateTime.Now,LogType.Log,i.ToString(),"")).ToArray());
            Assert.AreEqual(500,model.Snapshot().Count);Assert.AreEqual("101",model.Snapshot()[0].Message);
        }
        [Test] public void ErrorStoreRotatesAndBoundsUtf8Records()
        {
            var store=new RuntimeErrorLogStore(folder,256);
            var entry=new RuntimeLogEntry(DateTime.Now,LogType.Error,new string('错',300),"stack");
            Assert.IsTrue(store.Append(entry));Assert.IsTrue(store.Append(entry));
            Assert.LessOrEqual(new FileInfo(Path.Combine(folder,RuntimeErrorLogStore.CurrentFileName)).Length,256);
            Assert.LessOrEqual(new FileInfo(Path.Combine(folder,RuntimeErrorLogStore.PreviousFileName)).Length,256);
            StringAssert.Contains("[truncated]",File.ReadAllText(Path.Combine(folder,RuntimeErrorLogStore.CurrentFileName)));
        }
        [Test] public void ExportPreservesMessageStackAndMetadata()
        {
            var path=RuntimeLogExportUtility.Export(new[]{new RuntimeLogEntry(DateTime.Now,LogType.Error,"导出测试","probe stack")},"Main",folder);
            var text=File.ReadAllText(path);StringAssert.Contains("导出测试",text);StringAssert.Contains("probe stack",text);StringAssert.Contains("AppState: Main",text);
        }
    }
}
