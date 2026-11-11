using NUnit.Framework;
using UnityEngine;

namespace OutpostLoop.Tests
{
    public sealed class ScavengeRunTests
    {
        [Test]
        public void SafeRoadExtractionShowsOnlyCurrentTripAndResetsInventory()
        {
            var run = new ScavengeRun();
            Assert.AreEqual(ScavengeItem.None, run.Inventory);
            Assert.IsFalse(run.Attack(Vector2.right));
            MoveTo(run, ScavengeLayout.Caches[0]);
            Assert.AreEqual(ScavengeItem.None, run.VisibleContents(0));
            Assert.IsTrue(run.Interact());
            Assert.IsFalse(run.Transfer(ScavengeItem.Materials, true));
            Advance(run, .7f);
            Assert.IsTrue(run.Transfer(ScavengeItem.Materials, true));
            Assert.IsFalse(run.Transfer(ScavengeItem.Materials, true));
            MoveTo(run, ScavengeLayout.Exit);
            Assert.IsTrue(run.Interact());
            Assert.IsFalse(run.Interact());
            Assert.AreEqual(ScavengeItem.Materials, run.CarriedOut);
            Assert.AreEqual(ScavengeItem.None, run.Inventory);

            var next = new ScavengeRun();
            Assert.AreEqual(ScavengeItem.None, next.Inventory);
            MoveTo(next, ScavengeLayout.Exit);
            Assert.IsTrue(next.Interact());
            Assert.AreEqual(ScavengeItem.None, next.CarriedOut);
        }

        [Test]
        public void WarehouseCanTradeFullBagAndLeaveThroughSideDoor()
        {
            var run = new ScavengeRun();
            MoveTo(run, ScavengeLayout.Caches[0]);
            SearchAndTake(run, ScavengeItem.Materials);
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
            Assert.IsTrue(run.Transfer(ScavengeItem.Materials, false));
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
        public void RoadConstructionDrawsPatrolAndInstalledDefenseProtectsShelter()
        {
            var run = ArmedWithMaterials();
            var site = new Vector2(-3, -6.25f);
            var defense = new Vector2(-1.68f, -5.22f);
            MoveTo(run, new Vector2(-.8f, -6));
            MoveTo(run, new Vector2(-5.4f, -6));
            Assert.AreEqual(ShelterPlacement.NeedsShelter, run.CheckDefense(defense));
            Assert.IsTrue(run.BuildShelter(site));
            Assert.AreEqual(ShelterPlacement.Obstructed, run.CheckDefense(site));
            Assert.IsTrue(run.BuildDefense(defense));
            Assert.IsFalse(run.BuildDefense(defense));
            Assert.AreEqual(ScavengeItem.None, run.Inventory);
            MoveTo(run, new Vector2(-6.8f, -6));
            bool investigated = false;
            bool fired = false;
            for (int i = 0; i < 650; i++)
            {
                run.Tick(.02f, Vector2.zero);
                investigated |= run.ConstructionInvestigated;
                fired |= run.DefenseShotRemaining > 0;
            }
            Assert.IsTrue(investigated, "近处施工应让路边巡逻者离开原路线查看");
            Assert.IsTrue(fired, "发现据点后应进入装置的有效射界");
            Assert.IsFalse(run.Patrols[1].Alive);
            Assert.IsTrue(run.Patrols[0].Alive);
            Assert.Greater(run.ShelterHealth, 0);
            MoveTo(run, ScavengeLayout.Exit);
            Assert.IsTrue(run.Interact());
            Assert.IsNull(run.Defense);
        }

        [Test]
        public void ShelterCanFallWhileAnUnarmedPlayerCanStillExtract()
        {
            var run = ArmedWithMaterials();
            MoveTo(run, new Vector2(-.8f, -6));
            MoveTo(run, new Vector2(-5.4f, -6));
            Assert.IsTrue(run.BuildShelter(new Vector2(-3, -6.25f)));
            MoveTo(run, new Vector2(-6.8f, -6));
            Advance(run, 15);
            Assert.IsTrue(run.ShelterLost);
            Assert.AreEqual(ScavengeOutcome.Exploring, run.Outcome);
            MoveTo(run, ScavengeLayout.Exit);
            Assert.IsTrue(run.Interact());
            Assert.AreEqual(ScavengeItem.DefenseParts, run.CarriedOut);
        }

        [Test]
        public void CoveredSiteCanFinishWithoutAnAttackAndWallBlocksFire()
        {
            var run = ArmedWithMaterials();
            MoveTo(run, new Vector2(-.8f, -6));
            MoveTo(run, new Vector2(-6.3f, -6));
            MoveTo(run, new Vector2(-6.3f, 9.8f));
            for (int step = 0; step < 500 && run.Enemy.y > -1.5f; step++) run.Tick(.02f, Vector2.zero);
            MoveTo(run, new Vector2(6.4f, 9.8f));
            for (int step = 0; step < 500 && (run.Enemy.y > -1.5f || run.EnemyFacing.y >= 0); step++)
                run.Tick(.02f, Vector2.zero);
            Assert.IsTrue(run.BuildShelter(new Vector2(8.65f, 8.85f)), $"生命 {run.Health}，状态 {run.Outcome}");
            Assert.IsTrue(run.BuildDefense(new Vector2(7.3f, 9.45f)));
            Advance(run, 3.15f);
            Assert.AreEqual(ScavengeRun.ShelterMaxHealth, run.ShelterHealth);
            Assert.IsFalse(run.Alerted);
            Assert.IsFalse(ScavengeLayout.ClearLine(run.Defense.Value, new Vector2(7.8f, 4.9f)));
            Assert.IsNull(run.DefenseShotTarget);
        }

        private static ScavengeRun ArmedWithMaterials()
        {
            var run = new ScavengeRun();
            MoveTo(run, ScavengeLayout.Caches[0]);
            SearchAndTake(run, ScavengeItem.Materials);
            MoveTo(run, ScavengeLayout.Caches[ScavengeLayout.LoadingRack]);
            SearchAndTake(run, ScavengeItem.DefenseParts);
            return run;
        }

        [Test]
        public void WorkshopProcessesWhileAwayAndRepairRestoresDamagedShelter()
        {
            var run = RoadShelter(true);
            MoveTo(run, ScavengeLayout.Caches[ScavengeLayout.Pharmacy]);
            SearchAndTake(run, ScavengeItem.Supplies);
            MoveTo(run, new Vector2(-4, -6.25f));
            Assert.IsTrue(run.TransferStorage(ScavengeItem.Supplies, false));
            Assert.AreEqual(0, run.Count);
            MoveTo(run, ScavengeLayout.Caches[1]);
            SearchAndTake(run, ScavengeItem.Crowbar);
            MoveTo(run, new Vector2(7.8f, -3));
            for (int i = 0; i < 380 && Vector2.Distance(run.Player, run.Enemy) > 1.6f; i++)
                run.Tick(.02f, Vector2.zero);
            Assert.IsTrue(run.Attack(run.Enemy - run.Player));
            Advance(run, .56f);
            Assert.IsTrue(run.Attack(run.Enemy - run.Player));
            Assert.IsFalse(run.EnemyAlive);
            MoveTo(run, ScavengeLayout.Caches[3]);
            SearchAndTake(run, ScavengeItem.RepairMaterials);
            MoveTo(run, new Vector2(-.8f, -3));
            MoveTo(run, new Vector2(-4, -6.25f));
            Assert.IsTrue(run.TransferStorage(ScavengeItem.RepairMaterials, false));
            Assert.IsTrue(run.StartWorkshop());
            Assert.IsFalse(run.StartWorkshop());
            MoveTo(run, ScavengeLayout.Caches[0]);
            Assert.IsFalse(run.CanUseStorage);
            Advance(run, ScavengeRun.WorkshopSeconds);
            Assert.AreEqual(ScavengeItem.Supplies | ScavengeItem.RepairKit, run.Stored);
            Assert.IsFalse(run.RepairShelter());
            MoveTo(run, new Vector2(-4, -6.25f));
            int damaged = run.ShelterHealth;
            Assert.Less(damaged, ScavengeRun.ShelterMaxHealth);
            Assert.IsTrue(run.RepairShelter());
            Assert.AreEqual(Mathf.Min(damaged + 2, ScavengeRun.ShelterMaxHealth), run.ShelterHealth);
            Assert.IsFalse(run.RepairShelter());
            MoveTo(run, ScavengeLayout.Exit);
            Assert.IsTrue(run.Interact());
            Assert.AreEqual(ScavengeItem.Crowbar, run.CarriedOut);
            Assert.AreEqual(ScavengeItem.None, run.Stored);
        }

        [Test]
        public void ReservedOutputPreventsOverfillingStorageAndFullHealthPreservesKit()
        {
            var run = RoadShelter(true);
            MoveTo(run, new Vector2(-4, -6.25f));
            // 直接布置满仓的边界状态，避免重复走已覆盖的拾取路线。
            ArrangeItems(run, ScavengeItem.Supplies | ScavengeItem.Signal | ScavengeItem.DefenseParts |
                ScavengeItem.RepairMaterials, ScavengeItem.Crowbar | ScavengeItem.Materials);
            Assert.IsFalse(run.TransferStorage(ScavengeItem.Crowbar, false));
            Assert.IsFalse(run.TransferStorage(ScavengeItem.Supplies, true));
            Assert.IsTrue(run.StartWorkshop());
            Assert.AreEqual(4, run.StorageUsed);
            Assert.IsFalse(run.TransferStorage(ScavengeItem.Crowbar, false));
            Advance(run, ScavengeRun.WorkshopSeconds);
            Assert.AreEqual(4, run.StorageUsed);
            typeof(ScavengeRun).GetProperty(nameof(ScavengeRun.ShelterHealth)).SetValue(run, ScavengeRun.ShelterMaxHealth);
            Assert.IsFalse(run.RepairShelter());
            Assert.IsTrue(run.HasRepairKit);
            Assert.IsNull(new ScavengeRun().Shelter);
            Assert.AreEqual(ScavengeItem.None, new ScavengeRun().Stored);
        }

        [Test]
        public void DestroyedShelterCancelsWorkshopAndKeepsBackpack()
        {
            var run = RoadShelter(false);
            MoveTo(run, new Vector2(-4.15f, -6.25f));
            ArrangeItems(run, ScavengeItem.RepairMaterials | ScavengeItem.Supplies, ScavengeItem.DefenseParts);
            Assert.IsTrue(run.StartWorkshop());
            MoveTo(run, new Vector2(-6.9f, -6.25f));
            Advance(run, 12f);
            Assert.IsTrue(run.ShelterLost);
            Assert.AreEqual(0, run.WorkshopRemaining);
            Assert.AreEqual(ScavengeItem.None, run.Stored);
            Assert.AreEqual(ScavengeItem.DefenseParts, run.Inventory);
            Assert.IsFalse(run.StartWorkshop());
        }

        private static void ArrangeItems(ScavengeRun run, ScavengeItem stored, ScavengeItem inventory)
        {
            typeof(ScavengeRun).GetProperty(nameof(ScavengeRun.Stored)).SetValue(run, stored);
            typeof(ScavengeRun).GetProperty(nameof(ScavengeRun.Inventory)).SetValue(run, inventory);
        }

        private static ScavengeRun RoadShelter(bool defend)
        {
            var run = ArmedWithMaterials();
            MoveTo(run, new Vector2(-.8f, -6));
            MoveTo(run, new Vector2(-5.4f, -6));
            Assert.IsTrue(run.BuildShelter(new Vector2(-3, -6.25f)));
            if (defend) Assert.IsTrue(run.BuildDefense(new Vector2(-1.68f, -5.22f)));
            MoveTo(run, new Vector2(-6.8f, -6));
            Advance(run, defend ? 7f : 3.15f);
            return run;
        }

        [Test]
        public void DeathClearsOnlyTheCurrentTrip()
        {
            var run = new ScavengeRun();
            MoveTo(run, ScavengeLayout.Caches[0]);
            SearchAndTake(run, ScavengeItem.Materials);
            MoveTo(run, new Vector2(-.7f, -3));
            MoveTo(run, new Vector2(7.8f, -3));
            Advance(run, 12);
            Assert.AreEqual(ScavengeOutcome.Died, run.Outcome);
            Assert.AreEqual(ScavengeItem.None, run.Inventory);
            Assert.AreEqual(ScavengeItem.None, run.CarriedOut);
            Assert.IsFalse(run.Interact());
        }

        [TestCase(0, ScavengeLayout.Pharmacy, ScavengeLayout.LoadingRack)]
        [TestCase(3, ScavengeLayout.AbandonedCar, ScavengeLayout.RepairRoom)]
        public void SeedChangesAidAndPartsWithoutRevealingUnsearchedLoot(int seed, int aid, int defense)
        {
            var expected = new[] { ScavengeItem.Materials, ScavengeItem.Crowbar, ScavengeItem.Signal,
                ScavengeItem.RepairMaterials, ScavengeItem.RepairMaterials, ScavengeItem.None, ScavengeItem.None };
            expected[aid] = ScavengeItem.Supplies;
            expected[defense] = ScavengeItem.DefenseParts;
            for (int i = 0; i < expected.Length; i++)
            {
                var run = new ScavengeRun(seed);
                Assert.AreEqual(seed, run.Seed);
                Assert.IsFalse(run.IsDiscovered(i));
                // 单独检查每个容器，巡逻路线和实地走法另有覆盖。
                foreach (var patrol in run.Patrols)
                    typeof(ScavengePatrol).GetProperty(nameof(ScavengePatrol.Health)).SetValue(patrol, 0);
                typeof(ScavengeRun).GetProperty(nameof(ScavengeRun.Player)).SetValue(run, ScavengeLayout.Caches[i]);
                Assert.IsTrue(run.Interact());
                Assert.AreEqual(i, run.OpenCache);
                Assert.IsTrue(run.IsDiscovered(i));
                Assert.IsFalse(run.IsSearched(i));
                Assert.AreEqual(ScavengeItem.None, run.VisibleContents(i));
                Advance(run, ScavengeRun.SearchSeconds + .05f);
                Assert.IsTrue(run.IsSearched(i), ScavengeLayout.CacheNames[i]);
                Assert.AreEqual(expected[i], run.VisibleContents(i));
            }
        }

        [Test]
        public void FirstAidNeedsDamageAndIsConsumedOnce()
        {
            var run = new ScavengeRun();
            MoveTo(run, ScavengeLayout.Caches[ScavengeLayout.Pharmacy]);
            SearchAndTake(run, ScavengeItem.Supplies);
            Assert.IsFalse(run.UseFirstAid());
            Assert.AreEqual(1, run.Count);
            MoveTo(run, new Vector2(-.8f, -3));
            MoveTo(run, new Vector2(7.8f, -3));
            for (int step = 0; step < 400 && run.Health == ScavengeRun.MaxHealth; step++) run.Tick(.02f, Vector2.zero);
            Assert.AreEqual(2, run.Health);
            Assert.IsTrue(run.UseFirstAid());
            Assert.AreEqual(ScavengeRun.MaxHealth, run.Health);
            Assert.AreEqual(0, run.Count);
            Assert.IsFalse(run.UseFirstAid());
        }

        [Test]
        public void LeavingASearchKeepsDiscoveredMarkerAndHidesContents()
        {
            var run = new ScavengeRun();
            MoveTo(run, ScavengeLayout.Caches[0]);
            Assert.IsTrue(run.IsDiscovered(0));
            Assert.IsTrue(run.Interact());
            run.Tick(.4f, Vector2.down);
            Assert.AreEqual(-1, run.OpenCache);
            Assert.IsTrue(run.IsDiscovered(0));
            Assert.IsFalse(run.IsSearched(0));
            Assert.AreEqual(ScavengeItem.None, run.VisibleContents(0));
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

    public sealed class ScavengeTownTests
    {
        [Test]
        public void TownPreviewGeometryBlocksMovementAndCanBeRemoved()
        {
            const System.Reflection.BindingFlags hiddenStatic = System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.NonPublic;
            var set = typeof(ScavengeRun).GetMethod("SetTownPreviewGeometry", hiddenStatic);
            var run = new ScavengeRun(0, true);
            var walls = new System.Collections.Generic.List<Rect>(Read<Rect[]>(run, "RunWalls"))
                { new Rect(-61.8f, -54f, 1f, 5f) };
            try
            {
                set.Invoke(null, new object[] { walls.ToArray(), System.Array.Empty<Vector2>() });
                for (int i = 0; i < 32; i++) run.Tick(.02f, Vector2.right);
                Assert.That(run.Player.x, Is.EqualTo(-62.2f).Within(.13f));
                set.Invoke(null, new object[] { null, null });
                for (int i = 0; i < 32; i++) run.Tick(.02f, Vector2.right);
                Assert.Greater(run.Player.x, -60.5f);
            }
            finally { set.Invoke(null, new object[] { null, null }); }
        }

        [Test]
        public void SeedsVaryLootAndPatrolsAcrossTheSameTown()
        {
            var first = new ScavengeRun(0, true);
            var second = new ScavengeRun(1, true);
            Assert.AreEqual(first.Player, second.Player);
            Assert.IsFalse(first.IsRain);
            Assert.IsTrue(second.IsRain);
            Assert.AreEqual(12, first.Patrols.Count);
            int fast = 0;
            foreach (var patrol in first.Patrols) if (patrol.Fast) fast++;
            Assert.AreEqual(2, fast);
            Assert.AreNotEqual(first.Patrols[0].Position, second.Patrols[0].Position);
            int differences = 0;
            for (int i = 0; i < 22; i++)
            {
                if (ReadLoot(first, i) != ReadLoot(second, i)) differences++;
            }
            Assert.Greater(differences, 0);
        }

        [Test]
        public void SurvivorAndManifestBothGiveAReachableOptionalLead()
        {
            var run = new ScavengeRun(0, true);
            Place(run, new Vector2(-30, -33));
            Assert.IsTrue(run.Interact());
            Assert.IsTrue(run.SurvivorSpoken);
            Assert.IsTrue(run.MedicineLeadKnown);
            int target = Read<int>(run, "MedicineCache");
            Assert.AreEqual(ScavengeItem.ReserveMedicine, ReadLoot(run, target));
            Assert.IsTrue(run.Transfer(ScavengeItem.ReserveMedicine, true));
            Assert.IsTrue(run.MedicineFound);
            run.CloseSearch();
            Place(run, new Vector2(-70, -40));
            run.Tick(.02f, Vector2.zero);
            Assert.IsTrue(run.Interact());
            Assert.AreEqual(ScavengeOutcome.Extracted, run.Outcome);
            Assert.AreEqual(ScavengeItem.ReserveMedicine, run.CarriedOut);
            var other = new ScavengeRun(1, true);
            Assert.AreEqual(ScavengeItem.Signal, ReadLoot(other, 7));
            Assert.IsTrue(other.MedicineLeadKnown);
            Assert.IsFalse(other.SurvivorSpoken);
            Assert.IsFalse(new ScavengeRun(1, true).MedicineFound);
        }

        [Test]
        public void PistolConsumesAmmunitionAndNearbyPatrolInvestigatesTheShot()
        {
            var run = new ScavengeRun(0, true);
            Place(run, new Vector2(-75, -62));
            typeof(ScavengeRun).GetProperty(nameof(ScavengeRun.Inventory)).SetValue(run,
                ScavengeItem.Pistol | ScavengeItem.Ammo);
            var patrol = run.Patrols[0];
            typeof(ScavengePatrol).GetProperty(nameof(ScavengePatrol.Position)).SetValue(patrol, new Vector2(-65, -62));
            Assert.IsFalse(run.Attack(Vector2.up));
            Assert.IsTrue(run.Reload());
            Assert.AreEqual(6, run.Rounds);
            Assert.AreEqual(ScavengeItem.Pistol, run.Inventory);
            Assert.IsTrue(run.Attack(Vector2.up));
            run.Tick(.02f, Vector2.zero);
            Assert.AreEqual(5, run.Rounds);
            Assert.IsTrue(patrol.Investigating);
            for (int i = 0; i < 18; i++) run.Tick(.02f, Vector2.zero);
            Assert.IsTrue(run.Attack(patrol.Position - run.Player));
            Assert.IsFalse(patrol.Alive);
            Assert.AreEqual(4, run.Rounds);
        }

        private static T Read<T>(ScavengeRun run, string name) => (T)typeof(ScavengeRun).GetProperty(name,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(run);

        private static void Place(ScavengeRun run, Vector2 point) =>
            typeof(ScavengeRun).GetProperty(nameof(ScavengeRun.Player)).SetValue(run, point);

        private static ScavengeItem ReadLoot(ScavengeRun run, int index)
        {
            run.CloseSearch();
            foreach (var patrol in run.Patrols)
                typeof(ScavengePatrol).GetProperty(nameof(ScavengePatrol.Health)).SetValue(patrol, 0);
            Place(run, Read<Vector2[]>(run, "CachePoints")[index]);
            Assert.IsTrue(run.Interact(), $"搜点 {index}");
            Assert.AreEqual(index, run.OpenCache);
            for (int i = 0; i < 33; i++) run.Tick(.02f, Vector2.zero);
            Assert.IsTrue(run.IsSearched(index), $"搜点 {index}");
            return run.VisibleContents(index);
        }
    }
}
