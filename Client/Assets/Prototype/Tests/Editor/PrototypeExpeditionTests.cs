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
            "../../Backup/Artifacts/ExpeditionV09-20261110/Profile-" + Guid.NewGuid().ToString("N")));

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
            Assert.AreEqual(480, industrial.Expedition.Rules.SurvivalSeconds);
            var shelter = PrepareShelter(industrial);
            Mine(industrial, 1, 2);
            MoveTo(industrial, industrial.Expedition.CluePosition);
            Interact(industrial, .1f);
            MoveNear(industrial, shelter);
            Assert.IsTrue(industrial.StartResearch(ExpeditionTech.Reinforcement));
            float left = industrial.Expedition.ResearchLeft;
            industrial.Run.Paused = true;
            Advance(industrial, 2);
            Assert.AreEqual(left, industrial.Expedition.ResearchLeft);
            industrial.Run.Paused = false;
            Advance(industrial, 20);
            Assert.IsTrue(industrial.Expedition.Has(ExpeditionTech.Reinforcement));
            Assert.AreEqual(120, industrial.BuildingSpec(ExpansionKind.Turret).Health);
            Advance(industrial, Mathf.Max(0, 60.1f - industrial.Run.Elapsed));
            Assert.IsTrue(industrial.Enemies.Any(enemy => enemy.Kind == EnemyKind.Fast));
            Survive(industrial);
            Assert.IsTrue(industrial.ReturnFromExpedition());
            Assert.IsTrue(profile.RecordReturn(industrial));
            var next = WorldState.Depart(731, ExpeditionMap.Industrial, new ExpeditionProfile(directory));
            Mine(next, 0);
            int turret = BuildNear(next, ExpansionKind.Turret, next.CommanderPosition + Vector2.one * 3);
            Advance(next, 6);
            Assert.AreEqual(120, next.Buildings[turret].Health);
        }

        private static WorldState SurveyResearch(ExpeditionProfile profile)
        {
            var world = WorldState.Depart(731, ExpeditionMap.Wilderness, profile);
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
            return world;
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
