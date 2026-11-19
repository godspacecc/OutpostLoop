using System;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using static OutpostLoop.Tests.PrototypeWorldTests;

namespace OutpostLoop.Tests
{
    public class PrototypeRuleTests
    {
        [SetUp] public void Prepare() => Game.FoundationRuntime.Initialize();
        [TearDown] public void Close() => Game.FoundationRuntime.Shutdown();

        [Test]
        public void UpgradeConsumesFundsOnlyWhenAvailable()
        {
            var world = WithBase(12, funds: 600);
            var run = world.Run;
            Assert.IsTrue(run.Upgrade(1));
            Assert.AreEqual(400, run.Funds);
            Assert.IsTrue(run.Upgrade(1));
            Assert.AreEqual(0, run.Funds);
            Assert.IsFalse(run.Upgrade(1));
            Assert.AreEqual(2, run.Levels[1]);
            Assert.IsFalse(run.Upgrade(-1));
        }

        [Test]
        public void PauseAndDefeatGateOrdersAndElapsed()
        {
            var world = WithBase(12);
            world.Run.Paused = true;
            world.Run.Damage(20);
            world.Step(1);
            Assert.AreEqual(100, world.Run.Health);
            Assert.AreEqual(0, world.Run.Elapsed);
            world.Run.Paused = false;
            world.Run.Damage(100);
            Assert.AreEqual(-1, world.Run.Outcome);
            Assert.IsFalse(world.Run.Upgrade(1));
            Assert.IsFalse(world.Produce(0));
            world.Step(1);
            Assert.AreEqual(0, world.Run.Elapsed);
        }

        [Test]
        public void LegacyWalletKeepsCoinsAndUnlocksStartingFundsOncePerVictory()
        {
            var directory = Path.GetFullPath(Path.Combine(Application.dataPath,
                "../../Backup/Artifacts/FirstPlayableV07-20261110/Wallet-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, "prototype-wallet.json");
            File.WriteAllText(file, "{\"format\":1,\"data\":{\"coins\":27,\"lastVictory\":\"v06\"}}");
            var wallet = new PrototypeWallet(directory);
            Assert.AreEqual(800, wallet.StartingFunds);
            Assert.IsTrue(wallet.RecordVictory("first"));
            Assert.IsTrue(wallet.RecordVictory("first"));
            wallet = new PrototypeWallet(directory);
            Assert.AreEqual(1, wallet.Wins);
            Assert.AreEqual(900, wallet.StartingFunds);
            Assert.IsTrue(wallet.RecordVictory("second"));
            Assert.AreEqual(900, wallet.StartingFunds);
            Assert.IsTrue(wallet.RecordVictory("third"));
            Assert.IsTrue(wallet.RecordVictory("fourth"));
            Assert.AreEqual(1000, new PrototypeWallet(directory).StartingFunds);
            Assert.AreEqual(27, (int)JObject.Parse(File.ReadAllText(file))["data"]["coins"]);
        }
    }
}
