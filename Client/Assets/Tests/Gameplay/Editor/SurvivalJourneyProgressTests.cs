using NUnit.Framework;
using UnityEngine;

namespace OutpostLoop.Tests
{
    /// <summary>只验证跨趟结果和情报，不把局内装备状态当作局外库存。</summary>
    public sealed class SurvivalJourneyProgressTests
    {
        private static ScavengeRun Extract(bool ready)
        {
            var run = new ScavengeRun(41, true, ScavengeSurvival.Create(41));
            run.Survival.elapsed = ready ? ScavengeSurvival.Goal : 120;
            typeof(ScavengeRun).GetProperty(nameof(ScavengeRun.Player)).SetValue(run, ScavengeTown.Exit);
            run.Tick(.01f, Vector2.zero);
            Assert.IsTrue(run.SurvivalExit());
            return run;
        }

        [Test]
        public void SuccessfulReturnCreatesAnIndependentCompletionRecord()
        {
            var previous = new SurvivalJourneyProgress();
            var run = Extract(true);
            run.Survival.caches[0].known = true;
            run.Survival.pack.Add(new SurvivalStack(SurvivalItem.Water));
            var result = SurvivalJourneyProgress.Record(previous, run, "密封饮水");
            Assert.IsFalse(previous.hasResult);
            Assert.IsTrue(result.completedFirst);
            Assert.IsTrue(result.visitedSpace);
            Assert.AreEqual(1, result.carriedSlots);
            Assert.Contains(ScavengeTown.Names[0], result.discovered);
            run.Survival.pack.Clear();
            run.Survival.caches[0].known = false;
            Assert.AreEqual("密封饮水", result.carriedSummary);
            Assert.Contains(ScavengeTown.Names[0], result.discovered);
        }

        [Test]
        public void EarlyReturnCannotCompleteTheGoalOrRevokePastCompletion()
        {
            var early = SurvivalJourneyProgress.Record(null, Extract(false), "空手");
            Assert.IsTrue(early.visitedSpace);
            Assert.IsFalse(early.completedFirst);
            var completed = SurvivalJourneyProgress.Record(early, Extract(true), "空手");
            var retried = SurvivalJourneyProgress.Record(completed, Extract(false), "空手");
            Assert.IsTrue(retried.completedFirst);
            Assert.That(retried.survived, Is.InRange(120, 121));
        }

        [Test]
        public void DeathKeepsOldIntelAndDiscardsCurrentCarryAndIntel()
        {
            var success = Extract(true);
            success.Survival.caches[0].known = true;
            var previous = SurvivalJourneyProgress.Record(null, success, "空手");
            var failed = new ScavengeRun(731, true, ScavengeSurvival.Create(731));
            failed.Survival.pack.Add(new SurvivalStack(SurvivalItem.Pistol));
            failed.Survival.caches[1].known = true;
            failed.Survival.health = 0;
            failed.Tick(.1f, Vector2.zero);
            var result = SurvivalJourneyProgress.Record(previous, failed, "手枪");
            Assert.AreEqual(ScavengeOutcome.Died, result.lastOutcome);
            Assert.AreEqual(0, result.carriedSlots);
            Assert.That(result.carriedSummary, Does.Not.Contain("手枪"));
            CollectionAssert.AreEqual(previous.discovered, result.discovered);
            Assert.IsTrue(result.completedFirst);
        }

        [Test]
        public void CombinedSaveKeepsProgressWhileAnActionIsSavedOrClosed()
        {
            var progress = SurvivalJourneyProgress.Record(null, Extract(true), "密封饮水");
            var next = new ScavengeRun(731, true, ScavengeSurvival.Create(731));
            var save = next.CaptureSurvival();
            save.journey = progress;
            var restored = JsonUtility.FromJson<SurvivalSave>(JsonUtility.ToJson(save));
            Assert.IsTrue(restored.available);
            Assert.IsTrue(restored.journey.completedFirst);
            Assert.IsEmpty(ScavengeRun.RestoreSurvival(restored).Survival.pack);
            restored = JsonUtility.FromJson<SurvivalSave>(JsonUtility.ToJson(new SurvivalSave { journey = progress }));
            Assert.IsFalse(restored.available);
            Assert.AreEqual("密封饮水", restored.journey.carriedSummary);
        }
    }
}
