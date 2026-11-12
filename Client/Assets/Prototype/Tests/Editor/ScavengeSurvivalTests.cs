using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace OutpostLoop.Tests
{
    /// <summary>覆盖长局独有的状态边界和存档往返。</summary>
    public sealed class ScavengeSurvivalTests
    {
        [Test]
        public void RealTimeClockClosesWindowAndDeathTakesPriority()
        {
            var state = ScavengeSurvival.Create(42);
            var run = new ScavengeRun(42, true, state);
            state.elapsed = ScavengeSurvival.Goal - .1;
            run.Tick(.2f, Vector2.zero);
            Assert.IsTrue(state.Ready);
            Assert.AreEqual(ScavengeOutcome.Exploring, run.Outcome);
            state.elapsed = ScavengeSurvival.Deadline - .1;
            run.Tick(.2f, Vector2.zero);
            Assert.AreEqual(ScavengeOutcome.WindowClosed, run.Outcome);

            state = ScavengeSurvival.Create(42);
            run = new ScavengeRun(42, true, state);
            state.elapsed = ScavengeSurvival.Deadline - .1;
            state.health = 5;
            state.bleeding = true;
            state.bleedClock = 29.9f;
            run.Tick(.2f, Vector2.zero);
            Assert.AreEqual(ScavengeOutcome.Died, run.Outcome);
        }

        [Test]
        public void InterruptedItemUseKeepsTheItemAndSuccessfulUseCommitsOnce()
        {
            var state = ScavengeSurvival.Create(42);
            var run = new ScavengeRun(42, true, state);
            state.pack.Add(new SurvivalStack(SurvivalItem.Water, 2));
            state.water = 40;
            Assert.IsTrue(run.BeginSurvivalAction(SurvivalAction.Drink));
            run.Tick(.1f, Vector2.up);
            Assert.AreEqual(2, state.pack[0].count);
            Assert.AreEqual(SurvivalAction.None, state.action);
            Assert.IsTrue(run.BeginSurvivalAction(SurvivalAction.Drink));
            for (int i = 0; i < 22; i++) run.Tick(.1f, Vector2.zero);
            Assert.That(state.water, Is.InRange(74f, 75f));
            Assert.AreEqual(1, state.pack[0].count);
        }

        [Test]
        public void FilledPackRejectsTransferAndDroppedEquipmentKeepsChargeAndRounds()
        {
            var state = ScavengeSurvival.Create(42);
            for (int i = 0; i < 3; i++) state.pack.Add(new SurvivalStack(SurvivalItem.Boards));
            var ground = new List<SurvivalStack> { new SurvivalStack(SurvivalItem.Pistol) { rounds = 4 } };
            Assert.IsFalse(ScavengeSurvival.Transfer(ground, 0, state.pack, ScavengeSurvival.Capacity));
            Assert.AreEqual(4, ground[0].rounds);
            Assert.IsTrue(ScavengeSurvival.Transfer(state.pack, 0, ground, int.MaxValue));
            Assert.IsTrue(ScavengeSurvival.Transfer(ground, 0, state.pack, ScavengeSurvival.Capacity));
            Assert.IsTrue(state.Equip(2));
            Assert.AreEqual(4, state.weapon.rounds);
            Assert.IsTrue(state.Stow(false));
            Assert.AreEqual(4, state.pack[2].rounds);
        }

        [Test]
        public void RestoredActionKeepsTimedStateAndPhysicalDoor()
        {
            var state = ScavengeSurvival.Create(42);
            var run = new ScavengeRun(42, true, state);
            state.elapsed = 1600.125;
            state.water = 39;
            state.weapon = new SurvivalStack(SurvivalItem.Pistol) { rounds = 3 };
            state.torch = new SurvivalStack(SurvivalItem.Torch) { charge = 40 };
            state.torchOn = true;
            state.baseRoom = 0;
            state.rooms[0].durability = 25;
            state.rooms[1].open = true;
            state.caches[0].searched = true;
            state.sleeping = true;
            state.sleepRoom = 0;
            state.energy = 99.99f;
            var save = JsonUtility.FromJson<SurvivalSave>(JsonUtility.ToJson(run.CaptureSurvival()));
            var resumed = ScavengeRun.RestoreSurvival(save);
            Assert.AreEqual(1600.125, resumed.Survival.elapsed);
            Assert.AreEqual(39, resumed.Survival.water);
            Assert.AreEqual(3, resumed.Survival.weapon.rounds);
            Assert.AreEqual(40, resumed.Survival.torch.charge);
            Assert.AreEqual(25, resumed.Survival.rooms[0].durability);
            Assert.IsTrue(resumed.Survival.rooms[1].open);
            Assert.IsTrue(resumed.IsSearched(0));
            Assert.AreEqual(run.Patrols[0].Position, resumed.Patrols[0].Position);
            var closed = SurvivalRooms.Door(0).center;
            var open = SurvivalRooms.Door(1).center;
            Assert.IsFalse(ScavengeLayout.ClearLine(closed - Vector2.right, closed + Vector2.right, .1f, resumed.RunWalls));
            Assert.IsTrue(ScavengeLayout.ClearLine(open - Vector2.right, open + Vector2.right, .1f, resumed.RunWalls));
            Assert.IsTrue(resumed.Survival.sleeping);
            resumed.Tick(.1f, Vector2.zero);
            Assert.IsFalse(resumed.Survival.sleeping);
        }
    }
}
