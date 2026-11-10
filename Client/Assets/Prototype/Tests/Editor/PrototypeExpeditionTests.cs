using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using static OutpostLoop.Tests.PrototypeWorldTests;

namespace OutpostLoop.Tests
{
    /// <summary>使用正式地图规则验证探索生命周期，档案隔离于本机玩家数据。</summary>
    public sealed class PrototypeExpeditionTests
    {
        [SetUp] public void Prepare() => Game.FoundationRuntime.Initialize();
        [TearDown] public void Close() => Game.FoundationRuntime.Shutdown();

        internal static string ProfileDirectory() => Path.GetFullPath(Path.Combine(Application.dataPath,
            "../../Backup/Artifacts/ExpeditionTacticsV010/Profile-" + Guid.NewGuid().ToString("N")));

        [Test]
        public void HandcartAndFortificationsBothReturnOnTwoSeeds()
        {
            var report = new System.Collections.Generic.List<string>();
            foreach (int seed in new[] { 41, 731 })
            foreach (bool hauling in new[] { true, false })
            {
                var profile = new ExpeditionProfile(ProfileDirectory());
                var world = WorldState.Depart(seed, ExpeditionMap.Wilderness, profile);
                PrepareShelter(world);
                Mine(world, 1, 2);
                var shelter = world.Buildings.First(site => site.Kind == ExpansionKind.Shelter);
                MoveNear(world, shelter);
                var tech = hauling ? ExpeditionTech.Handcart : ExpeditionTech.Fortifications;
                int firstBranchStock = world.Run.Funds;
                Assert.IsTrue(world.StartResearch(tech));
                Assert.AreEqual(firstBranchStock - 20, world.Run.Funds);
                Advance(world, 10);
                if (hauling)
                {
                    var target = FindPlacement(world, ExpansionKind.Extractor, world.CommanderPosition + Vector2.one * 4);
                    Assert.AreEqual(PlacementIssue.None, world.Build(ExpansionKind.Extractor, target));
                    var site = world.Buildings[world.FindBuilding(target)];
                    int reserve = world.Nodes[site.NodeIndex].Remaining;
                    int stock = world.Run.Funds;
                    Advance(world, 5);
                    world.Run.Paused = true;
                    Advance(world, 2);
                    Assert.AreEqual(stock, world.Run.Funds);
                    world.Run.Paused = false;
                    Advance(world, 2);
                    Assert.AreEqual(stock + 4, world.Run.Funds);
                    Assert.AreEqual(reserve - 4, world.Nodes[site.NodeIndex].Remaining);
                }
                else
                {
                    BuildNear(world, ExpansionKind.Turret, world.CommanderPosition + Vector2.one * 4);
                    var cell = FindPlacement(world, ExpansionKind.Barricade, world.CommanderPosition - Vector2.one * 4);
                    Assert.AreEqual(PlacementIssue.None, world.Build(ExpansionKind.Barricade, cell, true));
                    int index = world.FindBuilding(cell);
                    Assert.AreEqual(new Vector2Int(1, 2), world.Buildings[index].Footprint.size);
                    int stock = world.Run.Funds;
                    Assert.IsTrue(world.DismantleExpeditionBarricade(index));
                    Assert.AreEqual(-1, world.FindBuilding(cell));
                    Assert.AreEqual(stock, world.Run.Funds);
                    BuildNear(world, ExpansionKind.Barricade, BattleGrid.Center(cell));
                }
                while (world.Run.Elapsed < 76 && world.Run.Outcome == 0) world.Step(.1f);
                int firstWaveStock = world.Run.Funds;
                int placed = hauling ? 2 : 3;
                int firstWaveLosses = placed - world.Buildings.Count(site => site.Kind != ExpansionKind.Empty);
                Survive(world);
                Assert.IsTrue(world.ReturnFromExpedition());
                Assert.IsTrue(profile.RecordReturn(world));
                Assert.AreEqual(25, profile.Funds);
                report.Add($"种子 {seed}，路线 {tech}，分支首次投入 20，首波后物资 {firstWaveStock}，" +
                    $"首波建筑损失 {firstWaveLosses}，回归前总损失 {placed - world.Buildings.Count(site => site.Kind != ExpansionKind.Empty)}，" +
                    $"回归生命 {world.Run.Health}，入账 {profile.Funds}");
            }
            var directory = Path.GetDirectoryName(ProfileDirectory());
            File.WriteAllLines(Path.Combine(directory, "路线对照-" + Guid.NewGuid().ToString("N") + ".txt"), report);
        }

        [Test]
        public void OldProfileOpensWorkshopAndWinchUnlocksBeaconOnlyAfterReturn()
        {
            string directory = ProfileDirectory();
            var profile = WriteProfile(directory, false);
            Assert.AreEqual(100, profile.Funds);
            Assert.AreEqual(3, profile.Tech);
            Assert.IsTrue(profile.Trained);
            Assert.IsFalse(profile.CanEnter(ExpeditionMap.Beacon));
            var world = WorldState.Depart(41, ExpeditionMap.Industrial, profile);
            var expedition = world.Expedition;
            MoveTo(world, expedition.ObjectivePosition);
            Assert.IsTrue(world.MoveCommander(expedition.SurveyPosition));
            Advance(world, 6);
            Assert.That(Vector2.Distance(world.CommanderPosition, expedition.SurveyPosition), Is.GreaterThan(2));
            MoveTo(world, expedition.WorkshopRope);
            world.PressInteraction();
            Advance(world, .1f);
            MoveTo(world, expedition.ObjectivePosition);
            Interact(world, 4);
            Assert.IsFalse(expedition.BasicComplete);
            Assert.IsTrue(expedition.HasRope);
            MoveTo(world, expedition.WorkshopAxle);
            world.PressInteraction();
            Advance(world, .1f);
            MoveTo(world, expedition.ObjectivePosition);
            world.HoldInteraction(true);
            Advance(world, 2);
            world.Run.Paused = true;
            Advance(world, 2);
            Assert.That(expedition.GateProgress, Is.EqualTo(2).Within(.01));
            world.Run.Paused = false;
            world.HoldInteraction(false);
            Assert.AreEqual(0, expedition.GateProgress);
            Interact(world, 4);
            Assert.IsTrue(expedition.BasicComplete);
            Assert.IsNull(world.ClosedWorkshopGate);
            Assert.IsFalse(profile.CanEnter(ExpeditionMap.Beacon));
            MoveTo(world, expedition.SurveyPosition);
            Interact(world, 4.9f);
            Assert.AreEqual(ExpeditionEnd.None, expedition.End);
            Interact(world, 5);
            Assert.AreEqual(ExpeditionEnd.Surveyed, expedition.End);
            Assert.IsTrue(profile.RecordReturn(world));
            Assert.IsTrue(profile.TrainScout());
            Assert.IsFalse(profile.TrainScout());
            var restored = new ExpeditionProfile(directory);
            Assert.AreEqual(175, restored.Funds);
            var beacon = WorldState.Depart(731, ExpeditionMap.Beacon, restored);
            Assert.AreEqual(18, beacon.Expedition.CommanderSight);
            Assert.AreEqual(120, beacon.Run.Health);
        }

        [Test]
        public void BeaconDefenseSurvivesThreeRaidsAndReturnsWithFlagReward()
        {
            var profile = WriteProfile(ProfileDirectory(), true);
            var world = WorldState.Depart(731, ExpeditionMap.Beacon, profile);
            CompleteBeacon(world);
            Assert.IsTrue(profile.RecordReturn(world));
            Assert.AreEqual(320, profile.Funds);

            var full = WorldState.Depart(41, ExpeditionMap.Beacon, profile);
            MoveTo(full, full.Expedition.ObjectivePosition);
            for (int index = 0; index < full.Expedition.Rules.EnemyCap; index++)
                full.Enemies.Add(new EnemyState { Health = 100, Position = full.Expedition.SurveyPosition });
            full.PressInteraction();
            Advance(full, .1f);
            Assert.AreEqual(3, full.Expedition.BeaconQueued);
            full.Enemies.RemoveAt(0);
            full.PressInteraction();
            Advance(full, .1f);
            Assert.AreEqual(1, full.Expedition.BeaconWaves);
            Assert.AreEqual(2, full.Expedition.BeaconQueued);
            Assert.AreEqual(1, full.Enemies.Count(enemy => enemy.IsBeaconRaider));
        }

        internal static void CompleteBeacon(WorldState world)
        {
            var expedition = world.Expedition;
            Mine(world, 0, 1, 2, 3);
            MoveTo(world, expedition.ObjectivePosition);
            float back = Mathf.Sign(expedition.ObjectivePosition.y);
            BuildNear(world, ExpansionKind.Turret, expedition.ObjectivePosition + new Vector2(-4, -4 * back));
            BuildNear(world, ExpansionKind.Turret, expedition.ObjectivePosition + new Vector2(4, -4 * back));
            BuildNear(world, ExpansionKind.Barricade, expedition.ObjectivePosition + new Vector2(-4, -7 * back));
            BuildNear(world, ExpansionKind.Barricade, expedition.ObjectivePosition + new Vector2(4, -7 * back));
            Advance(world, 6);
            world.PressInteraction();
            Advance(world, .1f);
            Assert.AreEqual(1, expedition.BeaconWaves);
            Assert.AreEqual(3, world.Enemies.Count(enemy => enemy.IsBeaconRaider));
            MoveTo(world, expedition.ObjectivePosition + new Vector2(0, 10 * back));
            float care = expedition.BeaconCare;
            Advance(world, 2);
            Assert.AreEqual(care, expedition.BeaconCare);
            MoveTo(world, expedition.ObjectivePosition);
            var loop = new[] { new Vector2(-4, 4 * back), new Vector2(4, 4 * back), new Vector2(4, 0), new Vector2(-4, 0) };
            for (int leg = 0; leg < 100 && !expedition.BasicComplete && world.Run.Outcome == 0; leg++)
            {
                world.MoveCommander(expedition.ObjectivePosition + loop[leg % loop.Length]);
                Advance(world, 1.7f);
            }
            Assert.IsTrue(expedition.BasicComplete, $"照料 {expedition.BeaconCare}，生命 {world.Run.Health}，来敌 {world.Enemies.Count}");
            Assert.AreEqual(3, expedition.BeaconWaves);
            Assert.IsFalse(world.Enemies.Any(enemy => enemy.IsBeaconRaider));
            MoveTo(world, expedition.SurveyPosition);
            Interact(world, 6);
            Assert.AreEqual(ExpeditionEnd.Surveyed, expedition.End);
        }

        [Test]
        public void EnemyPressureIncreasesAfterBasicMission()
        {
            var world = WorldState.Depart(41, ExpeditionMap.Wilderness, new ExpeditionProfile(ProfileDirectory()));
            Survive(world);
            var loop = new[] { new Vector2(24, 24), new Vector2(-24, 24), new Vector2(-24, -24), new Vector2(24, -24) };
            for (int leg = 0; world.Run.Elapsed < 379 && world.Run.Outcome == 0; leg++)
            {
                world.MoveCommander(loop[leg % loop.Length]);
                Advance(world, 9);
            }
            Assert.Greater(world.Run.Health, 0);
            Assert.AreEqual(2, world.Expedition.ThreatLevel(world.Run.Elapsed));
            Assert.IsTrue(world.Enemies.Any(enemy => enemy.Kind == EnemyKind.Fast && enemy.Wave == 4));
            Assert.IsTrue(world.Enemies.Any(enemy => enemy.Kind == EnemyKind.Siege && enemy.Wave == 5));
        }

        private static ExpeditionProfile WriteProfile(string directory, bool workshopReturned)
        {
            Directory.CreateDirectory(directory);
            string flag = workshopReturned ? ",\"workshopReturned\":true" : "";
            File.WriteAllText(Path.Combine(directory, "expedition-profile.json"),
                "{\"format\":1,\"data\":{\"funds\":100,\"tech\":3,\"trained\":true,\"returns\":[\"legacy\"]" + flag + "}}");
            return new ExpeditionProfile(directory);
        }

        [Test]
        public void CommanderAndPursuerMoveDiagonallyAcrossOpenGround()
        {
            var world = WorldState.Depart(41, ExpeditionMap.Wilderness, new ExpeditionProfile(ProfileDirectory()));
            var start = world.CommanderPosition;
            var destination = start + new Vector2(11.4f, 7.3f);
            var enemy = new EnemyState { Position = start - new Vector2(8, 5), Health = 16 };
            var enemyStart = enemy.Position;
            world.Enemies.Add(enemy);
            Assert.IsTrue(world.MoveCommander(destination));
            Advance(world, .5f);
            Assert.That(Vector2.Distance(world.CommanderPosition, Vector2.MoveTowards(start, destination, world.Run.Config.MoveSpeed * .5f)),
                Is.LessThan(.001f));
            Assert.Greater(enemy.Position.x, enemyStart.x);
            Assert.Greater(enemy.Position.y, enemyStart.y);
            MoveTo(world, destination);
            Assert.That(Vector2.Distance(world.CommanderPosition, destination), Is.LessThan(.001f));
        }

        [Test]
        public void CommanderReroutesAroundNewShelterAndApproachesBlockedClicks()
        {
            var world = WorldState.Depart(731, ExpeditionMap.Wilderness, new ExpeditionProfile(ProfileDirectory()));
            var start = world.CommanderPosition;
            Mine(world, 0);
            MoveTo(world, start);
            var destination = start + new Vector2(11, 3);
            Assert.IsTrue(world.MoveCommander(destination));
            Advance(world, .2f);
            var siteCell = BattleGrid.Cell(start) + new Vector2Int(2, 1);
            Assert.AreEqual(PlacementIssue.None, world.Build(ExpansionKind.Shelter, siteCell));
            var shelter = world.Buildings[world.FindBuilding(siteCell)];
            for (int i = 0; i < 60; i++)
            {
                world.Step(.1f);
                Assert.IsFalse(shelter.Footprint.Contains(BattleGrid.Cell(world.CommanderPosition)), "施工后不能穿过新占地");
            }
            Assert.That(Vector2.Distance(world.CommanderPosition, destination), Is.LessThan(.001f));

            Assert.IsTrue(world.MoveCommander(shelter.Position));
            Advance(world, 4);
            Assert.IsFalse(shelter.Footprint.Contains(BattleGrid.Cell(world.CommanderPosition)));
            Assert.LessOrEqual(BattleGrid.DistanceTo(shelter.Footprint, world.CommanderPosition), 1.01f);

            Assert.IsTrue(world.MoveCommander(new Vector2(100, 20)));
            Advance(world, 20);
            Assert.That(world.CommanderPosition.magnitude, Is.InRange(43f, world.Run.Config.MapRadius));
        }

        [Test]
        public void NewSeedsProvideReachableOreShelterAndClue()
        {
            var profile = new ExpeditionProfile(ProfileDirectory());
            var world = WorldState.Depart(41, ExpeditionMap.Wilderness, profile);
            var other = WorldState.Depart(731, ExpeditionMap.Wilderness, profile);
            Assert.AreNotEqual(world.CommanderPosition, other.CommanderPosition);
            Assert.IsNull(WorldState.Depart(42, ExpeditionMap.Industrial, profile));
            Assert.IsFalse(profile.Train());
            foreach (var run in new[] { world, other })
            {
                Assert.AreEqual(0, run.Run.Funds);
                Assert.AreEqual(100, run.Run.Health);
                Assert.IsFalse(run.ReturnFromExpedition());
                Assert.GreaterOrEqual(run.Nodes.Count, 4);
                Assert.AreEqual(PlacementIssue.InsufficientStock,
                    run.Build(ExpansionKind.Shelter, BattleGrid.Cell(run.CommanderPosition) + Vector2Int.one));
                Assert.AreEqual(PlacementIssue.MissingResearch,
                    run.Build(ExpansionKind.Turret, BattleGrid.Cell(run.CommanderPosition) + Vector2Int.one));
                var shelter = PrepareShelter(run);
                Assert.AreEqual(100, shelter.Health);
                MoveTo(run, run.Expedition.CluePosition);
                Interact(run, .1f);
                Assert.IsTrue(run.Expedition.FoundClue);
                Mine(run, 1);
                MoveNear(run, shelter);
                Assert.IsTrue(run.StartResearch(ExpeditionTech.Fortifications));
                int funds = run.Run.Funds;
                for (int i = 0; i < 10; i++)
                    run.Enemies.Add(new EnemyState { Position = shelter.Position, Health = 100 });
                Advance(run, 5);
                Assert.AreEqual(ExpansionKind.Empty, shelter.Kind);
                Assert.IsNull(run.Expedition.Researching);
                Assert.AreEqual(funds, run.Run.Funds);
                Assert.IsFalse(run.Expedition.Has(ExpeditionTech.Fortifications));
            }
            var pursuit = WorldState.Depart(41, ExpeditionMap.Wilderness, profile);
            var behind = pursuit.CommanderPosition - Vector2.right * 12;
            var enemy = new EnemyState { Position = behind, Health = 16 };
            pursuit.Enemies.Add(enemy);
            Assert.IsTrue(pursuit.MoveCommander(pursuit.CommanderPosition + Vector2.right * 25));
            Advance(pursuit, 4);
            Assert.Greater(enemy.Position.x - behind.x, 4, "指挥员不断换格时，敌人也应持续前进");
        }

        [Test]
        public void ActiveReturnSavesSurveyAndUnlocksIndustrialMap()
        {
            string directory = ProfileDirectory();
            var profile = new ExpeditionProfile(directory);
            var world = SurveyResearch(profile);
            Assert.IsFalse(profile.CanEnter(ExpeditionMap.Industrial));
            Survive(world);
            Assert.IsTrue(world.ReturnFromExpedition());
            Assert.IsFalse(world.ReturnFromExpedition());
            Assert.AreEqual(0, world.Run.Funds);
            Assert.IsTrue(profile.RecordReturn(world));
            var loaded = new ExpeditionProfile(directory);
            Assert.AreEqual(25, loaded.Funds);
            Assert.IsTrue(loaded.CanEnter(ExpeditionMap.Industrial));
            Assert.AreEqual(3, loaded.Tech);
        }

        [Test]
        public void TerminalSurveyPausesCancelsAndRetriesWithoutDoubleCredit()
        {
            string directory = ProfileDirectory();
            var profile = new ExpeditionProfile(directory);
            var world = WorldState.Depart(731, ExpeditionMap.Wilderness, profile);
            Survive(world);
            MoveTo(world, world.Expedition.SurveyPosition);
            world.HoldInteraction(true);
            Advance(world, 1);
            world.Run.Paused = true;
            Advance(world, 2);
            Assert.That(world.Expedition.SurveyProgress, Is.EqualTo(1).Within(.001));
            world.Run.Paused = false;
            world.HoldInteraction(false);
            Assert.AreEqual(0, world.Expedition.SurveyProgress);
            Interact(world, 2.9f);
            Assert.AreEqual(0, world.Run.Outcome);
            Interact(world, 3);
            Assert.AreEqual(ExpeditionEnd.Surveyed, world.Expedition.End);
            Assert.AreEqual(75, world.Expedition.Reward);

            // 先让同名文件挡住档案目录，修正后以原局号重试。
            Directory.CreateDirectory(Path.GetDirectoryName(directory));
            File.WriteAllText(directory, "");
            Assert.IsFalse(profile.RecordReturn(world));
            Assert.AreEqual(0, profile.Funds);
            File.Delete(directory);
            Assert.IsTrue(profile.RecordReturn(world));
            var loaded = new ExpeditionProfile(directory);
            Assert.IsTrue(loaded.RecordReturn(world));
            Assert.AreEqual(75, loaded.Funds);
        }

        [Test]
        public void DeathWinsSameStepAsSurveyAndAbandonClearsThisRun()
        {
            string directory = ProfileDirectory();
            var profile = new ExpeditionProfile(directory);
            var prior = WorldState.Depart(41, ExpeditionMap.Wilderness, profile);
            var shelter = PrepareShelter(prior);
            Mine(prior, 1);
            MoveNear(prior, shelter);
            Assert.IsTrue(prior.StartResearch(ExpeditionTech.Fortifications));
            Advance(prior, 10);
            Survive(prior);
            MoveTo(prior, prior.Expedition.SurveyPosition);
            Interact(prior, 3);
            Assert.IsTrue(profile.RecordReturn(prior));
            Assert.IsTrue(profile.Train());

            var world = SurveyResearch(profile);
            Survive(world);
            MoveTo(world, world.Expedition.SurveyPosition);
            world.HoldInteraction(true);
            Advance(world, 3 - world.Run.Config.AttackInterval);
            world.Enemies.Add(new EnemyState { Position = world.CommanderPosition, Health = 100 });
            Advance(world, world.Run.Config.AttackInterval - .1f);
            world.Run.Damage(world.Run.Health - 1);
            Advance(world, .1f);
            Assert.AreEqual(ExpeditionEnd.Died, world.Expedition.End);
            Assert.IsFalse(world.Expedition.BasicComplete);
            Assert.AreEqual(0, world.Expedition.NewTech);
            Assert.AreEqual(0, world.Run.Funds);
            Assert.IsTrue(world.Buildings.All(site => site.Kind == ExpansionKind.Empty));
            Assert.IsFalse(profile.RecordReturn(world));

            var abandon = WorldState.Depart(731, ExpeditionMap.Wilderness, profile);
            PrepareShelter(abandon);
            Assert.IsTrue(abandon.AbandonExpedition());
            Assert.AreEqual(ExpeditionEnd.Abandoned, abandon.Expedition.End);
            Assert.AreEqual(0, abandon.Run.Funds);
            Assert.IsFalse(profile.RecordReturn(abandon));
            var loaded = new ExpeditionProfile(directory);
            Assert.AreEqual(25, loaded.Funds);
            Assert.AreEqual(1, loaded.Tech);
            Assert.IsTrue(loaded.Trained);
        }

        [Test]
        public void IndustrialResearchAndTrainingApplyOnLaterDepartures()
        {
            string directory = ProfileDirectory();
            var profile = new ExpeditionProfile(directory);
            var wilderness = SurveyResearch(profile);
            Survive(wilderness);
            MoveTo(wilderness, wilderness.Expedition.SurveyPosition);
            Interact(wilderness, 3);
            Assert.IsTrue(profile.RecordReturn(wilderness));
            Assert.IsTrue(profile.Train());
            Assert.IsFalse(profile.Train());
            Assert.AreEqual(25, profile.Funds);
            var industrial = WorldState.Depart(41, ExpeditionMap.Industrial, profile);
            Assert.AreEqual(120, industrial.Run.Health);
            Assert.IsFalse(industrial.Expedition.BasicComplete);
            var shelter = PrepareShelter(industrial);
            Mine(industrial, 1, 2);
            MoveTo(industrial, industrial.Expedition.CluePosition);
            Interact(industrial, .1f);
            MoveNear(industrial, shelter);
            BuildNear(industrial, ExpansionKind.Turret, shelter.Position + Vector2.right * 4);
            Assert.IsTrue(industrial.StartResearch(ExpeditionTech.Reinforcement));
            float left = industrial.Expedition.ResearchLeft;
            industrial.Run.Paused = true;
            Advance(industrial, 2);
            Assert.AreEqual(left, industrial.Expedition.ResearchLeft);
            industrial.Run.Paused = false;
            Advance(industrial, 20);
            Assert.IsTrue(industrial.Expedition.Has(ExpeditionTech.Reinforcement));
            Assert.AreEqual(120, industrial.BuildingSpec(ExpansionKind.Turret).Health);
            Mine(industrial, 3, 4, 5, 6);
            MoveNear(industrial, shelter);
            BuildNear(industrial, ExpansionKind.Turret, shelter.Position - Vector2.right * 4);
            Assert.IsTrue(industrial.StartResearch(ExpeditionTech.Handcart));
            Advance(industrial, 10);
            Assert.IsTrue(industrial.StartResearch(ExpeditionTech.Pulley));
            Advance(industrial, 15);
            Assert.IsTrue(industrial.StartResearch(ExpeditionTech.Bracing));
            Advance(industrial, 15);
            Assert.IsTrue(industrial.Expedition.Has(ExpeditionTech.Pulley));
            Assert.AreEqual(120, industrial.BuildingSpec(ExpansionKind.Barricade).Health);
            Assert.GreaterOrEqual(industrial.Wave, 1);
            OpenWorkshop(industrial);
            Assert.IsTrue(industrial.ReturnFromExpedition());
            Assert.IsTrue(profile.RecordReturn(industrial));
            var next = WorldState.Depart(731, ExpeditionMap.Industrial, new ExpeditionProfile(directory));
            Mine(next, 0);
            int turret = BuildNear(next, ExpansionKind.Turret, next.CommanderPosition + Vector2.one * 3);
            Advance(next, 6);
            Assert.AreEqual(120, next.Buildings[turret].Health);
            Assert.IsTrue(new ExpeditionProfile(directory).CanEnter(ExpeditionMap.Beacon));
        }

        internal static void OpenWorkshop(WorldState world)
        {
            MoveTo(world, world.Expedition.WorkshopAxle);
            world.PressInteraction();
            Advance(world, .1f);
            MoveTo(world, world.Expedition.WorkshopRope);
            world.PressInteraction();
            Advance(world, .1f);
            MoveTo(world, world.Expedition.ObjectivePosition);
            Interact(world, 4);
            Assert.IsTrue(world.Expedition.BasicComplete);
        }

        private static WorldState SurveyResearch(ExpeditionProfile profile)
        {
            var world = WorldState.Depart(731, ExpeditionMap.Wilderness, profile);
            ResearchSurvey(world);
            return world;
        }

        internal static void ResearchSurvey(WorldState world)
        {
            var shelter = PrepareShelter(world);
            Mine(world, 1, 2);
            MoveTo(world, world.Expedition.CluePosition);
            Interact(world, .1f);
            MoveNear(world, shelter);
            if (!world.Expedition.Has(ExpeditionTech.Fortifications))
            {
                Assert.IsTrue(world.StartResearch(ExpeditionTech.Fortifications));
                Assert.IsFalse(world.StartResearch(ExpeditionTech.Survey));
                Advance(world, 10);
            }
            Assert.IsTrue(world.StartResearch(ExpeditionTech.Survey));
            Advance(world, 15);
            Assert.IsTrue(world.Expedition.Has(ExpeditionTech.Survey));
        }

        private static ExpansionSite PrepareShelter(WorldState world)
        {
            Mine(world, 0);
            Assert.AreEqual(30, world.Run.Funds);
            int index = BuildNear(world, ExpansionKind.Shelter, world.CommanderPosition + Vector2.one * 3);
            var shelter = world.Buildings[index];
            world.Run.Paused = true;
            Advance(world, 1);
            Assert.AreEqual(4, shelter.ConstructionLeft);
            world.Run.Paused = false;
            Advance(world, 4);
            MoveNear(world, shelter);
            return shelter;
        }

        private static void MoveNear(WorldState world, ExpansionSite shelter)
        {
            var adjacent = shelter.Footprint.min - Vector2Int.right;
            MoveTo(world, BattleGrid.Center(adjacent));
            Assert.IsTrue(world.NearShelter);
        }

        internal static void MoveTo(WorldState world, Vector2 point)
        {
            Assert.IsTrue(world.MoveCommander(point));
            for (int step = 0; step < 360 && (world.CommanderPosition - point).sqrMagnitude > .001f; step++) world.Step(.1f);
            Assert.That(Vector2.Distance(world.CommanderPosition, point), Is.LessThan(.04f));
        }

        private static void Mine(WorldState world, params int[] nodes)
        {
            foreach (int node in nodes)
            {
                MoveTo(world, world.Nodes[node].Position);
                Interact(world, 6);
                Assert.AreEqual(0, world.Nodes[node].Remaining);
            }
        }

        private static void Interact(WorldState world, float seconds)
        {
            world.HoldInteraction(true);
            Advance(world, seconds);
            world.HoldInteraction(false);
        }

        /// <summary>按外围矩形持续移动以绕开真实追兵，直到任务达标。</summary>
        internal static void Survive(WorldState world)
        {
            var loop = new[] { new Vector2(24, 24), new Vector2(-24, 24), new Vector2(-24, -24), new Vector2(24, -24) };
            for (int leg = 0; leg < 80 && !world.Expedition.BasicComplete && world.Run.Outcome == 0; leg++)
            {
                Assert.IsTrue(world.MoveCommander(loop[leg % loop.Length]));
                Advance(world, leg == 0 ? 5 : 9);
            }
            Assert.IsTrue(world.Expedition.BasicComplete, $"任务达标前已停止，局时 {world.Run.Elapsed}，生命 {world.Run.Health}");
        }
    }
}
