using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace OutpostLoop.Tests
{
    public sealed class ScavengeRunTests
    {
        [Test]
        public void SafeRoadExtractsOnceAndPersistsCollection()
        {
            var directory = Path.Combine(Path.GetTempPath(), "outpostloop-scavenge-" + Guid.NewGuid());
            try
            {
                var run = new ScavengeRun();
                Assert.AreEqual(ScavengeItem.None, run.Inventory);
                Assert.IsFalse(run.Attack(Vector2.right));
                MoveTo(run, ScavengeLayout.Caches[0]);
                Assert.AreEqual(ScavengeItem.None, run.VisibleContents(0));
                Assert.IsTrue(run.Interact());
                Assert.IsFalse(run.Transfer(ScavengeItem.Supplies, true));
                Advance(run, .7f);
                Assert.IsTrue(run.Transfer(ScavengeItem.Supplies, true));
                Assert.IsFalse(run.Transfer(ScavengeItem.Supplies, true));
                MoveTo(run, ScavengeLayout.Exit);
                Assert.IsTrue(run.Interact());
                Assert.IsFalse(run.Interact());
                Assert.AreEqual(ScavengeItem.Supplies, run.CarriedOut);
                var collection = new ScavengeCollection(directory);
                Assert.IsTrue(collection.SaveReturn(run));
                Assert.IsTrue(collection.SaveReturn(run));
                Assert.AreEqual(ScavengeItem.Supplies, new ScavengeCollection(directory).Items);
                Assert.AreEqual(ScavengeItem.None, new ScavengeRun().Inventory);
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Test]
        public void WarehouseCanTradeFullBagAndLeaveThroughSideDoor()
        {
            var run = new ScavengeRun();
            MoveTo(run, ScavengeLayout.Caches[0]);
            SearchAndTake(run, ScavengeItem.Supplies);
            MoveTo(run, ScavengeLayout.Caches[1]);
            SearchAndTake(run, ScavengeItem.Crowbar);
            MoveTo(run, new Vector2(4.3f, -3));
            MoveTo(run, new Vector2(4.3f, 2));
            MoveTo(run, ScavengeLayout.Caches[2]);
            for (int i = 0; i < 4 && run.EnemyAlive; i++)
            {
                run.Attack(run.Enemy - run.Player);
                Advance(run, .55f);
            }
            Assert.IsTrue(run.Interact());
            Advance(run, .7f);
            Assert.IsFalse(run.Transfer(ScavengeItem.Signal, true));
            Assert.IsTrue(run.Transfer(ScavengeItem.Supplies, false));
            Assert.IsTrue(run.Transfer(ScavengeItem.Signal, true));
            MoveTo(run, new Vector2(5.4f, 5.5f));
            MoveTo(run, new Vector2(5.4f, 8.5f));
            MoveTo(run, ScavengeLayout.Exit);
            Assert.IsTrue(run.Interact());
            Assert.AreEqual(ScavengeItem.Crowbar | ScavengeItem.Signal, run.CarriedOut);
        }

        [Test]
        public void DeathClearsOnlyTheCurrentTrip()
        {
            var run = new ScavengeRun();
            MoveTo(run, ScavengeLayout.Caches[0]);
            SearchAndTake(run, ScavengeItem.Supplies);
            MoveTo(run, new Vector2(-.7f, -3));
            MoveTo(run, new Vector2(7.8f, -3));
            Advance(run, 12);
            Assert.AreEqual(ScavengeOutcome.Died, run.Outcome);
            Assert.AreEqual(ScavengeItem.None, run.Inventory);
            Assert.AreEqual(ScavengeItem.None, run.CarriedOut);
            Assert.IsFalse(run.Interact());
        }

        private static void SearchAndTake(ScavengeRun run, ScavengeItem item)
        {
            Assert.IsTrue(run.Interact());
            Advance(run, .7f);
            Assert.IsTrue(run.Transfer(item, true));
            run.CloseSearch();
        }

        private static void MoveTo(ScavengeRun run, Vector2 point)
        {
            for (int i = 0; i < 350 && Vector2.Distance(run.Player, point) > .09f; i++)
                run.Tick(.02f, Vector2.ClampMagnitude((point - run.Player) * 10, 1));
            Assert.Less(Vector2.Distance(run.Player, point), .1f, $"无法到达 {point}，当前位置 {run.Player}");
        }

        private static void Advance(ScavengeRun run, float seconds)
        {
            for (float elapsed = 0; elapsed < seconds; elapsed += .02f)
                run.Tick(.02f, Vector2.zero);
        }
    }
}
