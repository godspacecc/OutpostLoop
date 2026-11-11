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
        public void ShelterPlacementRejectsBlockedGroundAndConsumesOnlyOnSuccess()
        {
            var run = new ScavengeRun();
            var roadSite = new Vector2(-3, -6.25f);
            Assert.AreEqual(ShelterPlacement.MissingMaterials, run.CheckShelter(roadSite));
            MoveTo(run, ScavengeLayout.Caches[0]);
            SearchAndTake(run, ScavengeItem.Materials);
            Assert.AreEqual(ShelterPlacement.Obstructed, run.CheckShelter(new Vector2(-6.5f, -6)));
            Assert.AreEqual(ShelterPlacement.TooFar, run.CheckShelter(new Vector2(8.5f, 8.9f)));
            MoveTo(run, new Vector2(-5.4f, -6));
            Assert.AreEqual(ShelterPlacement.Available, run.CheckShelter(roadSite));
            Assert.AreEqual(ShelterPlacement.Available, run.CheckShelter(roadSite + Vector2.right * .5f));
            Assert.IsTrue(run.BuildShelter(roadSite));
            Assert.IsFalse(run.BuildShelter(roadSite + Vector2.right * .5f));
            Assert.AreEqual(ScavengeItem.None, run.Inventory);
            Assert.AreEqual(ScavengeRun.ShelterBuildSeconds, run.ShelterRemaining);
            Assert.IsNull(new ScavengeRun().Shelter);
        }

        [Test]
        public void SideDoorShelterCanRestoreOneHealthThenClearsOnExtraction()
        {
            var run = new ScavengeRun();
            MoveTo(run, ScavengeLayout.Caches[0]);
            SearchAndTake(run, ScavengeItem.Materials);
            MoveTo(run, ScavengeLayout.Caches[1]);
            SearchAndTake(run, ScavengeItem.Crowbar);
            MoveTo(run, new Vector2(7.8f, -3));
            for (int step = 0; step < 400 && run.Health == 3; step++) run.Tick(.02f, Vector2.zero);
            Assert.AreEqual(2, run.Health);
            Assert.IsTrue(run.Attack(run.Enemy - run.Player));
            Advance(run, .56f);
            Assert.IsTrue(run.Attack(run.Enemy - run.Player));
            Assert.IsFalse(run.EnemyAlive);
            MoveTo(run, new Vector2(5.4f, -3));
            MoveTo(run, new Vector2(5.4f, 8.65f));
            var sideSite = new Vector2(8.5f, 8.9f);
            Assert.IsTrue(run.BuildShelter(sideSite));
            MoveTo(run, sideSite);
            Assert.IsFalse(run.Interact(), "施工期间不可休整");
            int wounded = run.Health;
            Advance(run, ScavengeRun.ShelterBuildSeconds);
            Assert.IsTrue(run.Interact(), $"剩余工期 {run.ShelterRemaining}，生命 {run.Health}，终局 {run.Outcome}");
            Assert.AreEqual(wounded + 1, run.Health);
            Assert.IsFalse(run.Interact());
            MoveTo(run, ScavengeLayout.Exit);
            Assert.IsTrue(run.Interact());
            Assert.IsNull(run.Shelter);
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
