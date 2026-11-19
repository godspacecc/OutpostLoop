using NUnit.Framework;
using OutpostLoop.RiverValley;
using UnityEngine;

namespace OutpostLoop.Tests
{
    public sealed class RiverValleyDrivingTests
    {
        [Test]
        public void VehicleFootprintStopsAtWallsAndFallsBackToTheOtherDoor()
        {
            var run = CreateRun();
            var parked = run.VehiclePosition;
            var geometry = run.FullTown;
            geometry.Solids.Add(new RiverValleyWorldGeometry.Solid(parked + new Vector2(.99f, 5),
                new Vector2(.25f, .2f), .22f));
            run.Tick(1.5f, Vector2.up);
            Assert.Zero(run.VehicleSpeed, "车头右角碰上薄墙时应停住");
            Assert.That(run.VehiclePosition.y - parked.y, Is.InRange(1f, 3f));
            run.Tick(.35f, Vector2.down);
            Assert.Less(run.VehicleSpeed, 0, "可以倒车脱离");
            var position = run.VehiclePosition;
            geometry.Solids.Add(new RiverValleyWorldGeometry.Solid(position + Vector2.left * 1.55f,
                Vector2.one * .25f, 0));
            geometry.Solids.Add(new RiverValleyWorldGeometry.Solid(position + Vector2.right * 1.55f,
                Vector2.one * .25f, 0));
            Assert.IsFalse(run.InteractVehicle());
            Assert.IsTrue(run.Driving);
            Assert.AreEqual("两侧无法下车，请挪车", run.SurvivalFeedback);
            geometry.Solids.RemoveAt(geometry.Solids.Count - 1);
            Assert.IsTrue(run.InteractVehicle());
            Assert.AreEqual(position, run.VehiclePosition);
            Assert.Greater(run.Player.x, position.x + 1.2f, "应使用另一侧安全落点");
            run.Tick(.8f, Vector2.left);
            Assert.Greater(run.Player.x, position.x + 1.2f, "停车后不能步行穿过车身");
        }

        [Test]
        public void SavesPreserveParkingAndOlderLayoutsAndDrivingUsesExistingEndings()
        {
            var run = CreateRun();
            var initial = run.VehiclePosition;
            run.Tick(.8f, new Vector2(1, 1));
            Assert.Greater(Mathf.DeltaAngle(0, run.VehicleHeading), 5);
            Assert.Greater(run.VehicleSpeed, 2);
            Assert.IsFalse(run.Interact(), "车内不能搜索或撤离");
            Assert.IsFalse(run.BeginSurvivalAction(SurvivalAction.Reload));
            Assert.IsTrue(run.InteractVehicle());
            var parked = RoundTrip(run.CaptureSurvival());
            var restored = ScavengeRun.RestoreSurvival(parked, run.FullTown);
            Assert.IsFalse(restored.Driving);
            Assert.AreEqual(run.Player, restored.Player);
            Assert.AreEqual(run.VehiclePosition, restored.VehiclePosition);
            Assert.AreEqual(run.VehicleHeading, restored.VehicleHeading);
            Assert.Zero(restored.VehicleSpeed);
            string oldJson = JsonUtility.ToJson(parked.run);
            var old = RoundTrip(parked);
            old.vehicle = default;
            old.run = JsonUtility.FromJson<ScavengeSurvival>(oldJson);
            var oldFull = ScavengeRun.RestoreSurvival(old, run.FullTown);
            Assert.AreEqual(initial, oldFull.VehiclePosition);
            Assert.AreEqual(parked.player, oldFull.Player);
            var legacy = new ScavengeRun(41, true, ScavengeSurvival.Create(41));
            Assert.IsFalse(ScavengeRun.RestoreSurvival(RoundTrip(legacy.CaptureSurvival())).HasVehicle);

            var exiting = CreateRun();
            exiting.FullTown.Exit = exiting.Player;
            exiting.Survival.elapsed = ScavengeSurvival.Goal;
            exiting.Tick(.05f, Vector2.zero);
            Assert.IsTrue(exiting.ExitKnown);
            Assert.IsFalse(exiting.SurvivalExit());
            Assert.IsTrue(exiting.InteractVehicle());
            Assert.IsTrue(exiting.SurvivalExit());
            Assert.AreEqual(ScavengeOutcome.Extracted, exiting.Outcome);
            Assert.AreEqual(initial, CreateRun().VehiclePosition);

            var timedOut = CreateRun();
            timedOut.Survival.elapsed = ScavengeSurvival.Deadline - .001;
            timedOut.Tick(.05f, Vector2.up);
            Assert.AreEqual(ScavengeOutcome.WindowClosed, timedOut.Outcome);
            Assert.Zero(timedOut.VehicleSpeed);

            var attacked = CreateRun();
            attacked.Survival.health = 1;
            var patrol = attacked.Patrols[0];
            patrol.Health = 2;
            patrol.Position = attacked.Player + Vector2.up * .45f;
            for (int i = 0; i < 40 && attacked.Outcome == ScavengeOutcome.Exploring; i++)
                attacked.Tick(.05f, Vector2.zero);
            Assert.AreEqual(ScavengeOutcome.Died, attacked.Outcome);
            Assert.AreEqual(2, patrol.Health);
        }

        private static SurvivalSave RoundTrip(SurvivalSave save) =>
            JsonUtility.FromJson<SurvivalSave>(JsonUtility.ToJson(save));

        /// <summary>用一直路固定车辆朝向，隔离场景资源和敌人随机路线。</summary>
        private static ScavengeRun CreateRun()
        {
            var geometry = new RiverValleyWorldGeometry
            {
                Start = new Vector2(0, -30), Exit = new Vector2(-12, -30),
                Corners = System.Array.Empty<Vector2>()
            };
            geometry.Roads.Add(new RiverValleyWorldGeometry.Channel(Vector2.down * 40, Vector2.up * 40, 8));
            var run = new ScavengeRun(41, true, ScavengeSurvival.Create(41), geometry);
            var save = run.CaptureSurvival();
            Assert.IsTrue(run.HasVehicle);
            save.player = run.VehiclePosition + Vector2.left * 1.52f;
            run = ScavengeRun.RestoreSurvival(save, geometry);
            foreach (var patrol in run.Patrols) patrol.Health = 0;
            Assert.IsTrue(run.InteractVehicle());
            return run;
        }
    }
}
