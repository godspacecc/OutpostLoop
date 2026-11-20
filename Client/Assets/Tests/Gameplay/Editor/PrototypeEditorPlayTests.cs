using System.Collections.Generic;
using System.Collections;
using NUnit.Framework;
using QFramework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace OutpostLoop.Tests
{
    /// <summary>限定编辑器实景检查：验证真实Play帧驱动、暂停和新局，不执行任何构建。</summary>
    public sealed class PrototypeEditorPlayTests
    {
        private static IEnumerator WaitForMain()
        {
            for(int frame=0;frame<600 && Object.FindObjectOfType<PrototypeController>()==null;frame++)yield return null;
            Assert.NotNull(Object.FindObjectOfType<PrototypeController>(), "Boot 未成功进入 Main");
            // Main Start 握手后，Boot 入口与加载界面应释放。
            for(int frame=0;frame<10;frame++)yield return null;
            Assert.AreEqual("Main",UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            Assert.IsTrue(Boot.BootHost.TryGetResourceInfo(out var info));
            Assert.AreEqual(Boot.BootResourceSource.EditorSimulate,info.Source);
            var package=YooAsset.YooAssets.GetPackage("OutpostLoop");
            Assert.NotNull(package,"必须通过实际 YooAsset 包进入 Main");
            Assert.AreEqual(YooAsset.EOperationStatus.Succeed,package.InitializeStatus);
            Assert.IsTrue(package.CheckLocationValid("Scenes/Main"));
            Assert.IsTrue(package.CheckLocationValid("Game.dll.bytes"));
            Assert.IsEmpty(Object.FindObjectsOfType<Boot.Launcher>(), "Main 就绪后应销毁 Boot 入口");
        }

        [UnityTest]
        public IEnumerator BootRiverValleyDrivingUsesKeyboardAndResumesAtRest()
        {
            const string batch = "RiverValleyDriving";
            const string saveName = "town-survival-v1.json";
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            var controller = Object.FindObjectOfType<PrototypeController>();
            var type = typeof(PrototypeController);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var runField = type.GetField("scavengeRun", flags);
            var poll = type.GetMethod("PollSurvival", flags);
            var player = typeof(ScavengeRun).GetProperty(nameof(ScavengeRun.Player));
            var directory = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,
                "../../Backup/Artifacts/" + batch + "/Save-" + System.Guid.NewGuid().ToString("N")));
            var store = new Game.JsonFileStore<SurvivalSave>(directory);
            type.GetField("survivalStore", flags).SetValue(controller, store);
            type.GetField("survivalSaveChecked", flags).SetValue(controller, false);
            type.GetMethod("RefreshOverlay", flags).Invoke(controller, null);
            Click("StartSurvival");
            yield return WaitForCrossing();
            var run = (ScavengeRun)runField.GetValue(controller);
            Assert.IsTrue(run.HasVehicle, "镇口需要一辆可驾驶车辆");
            Assert.NotNull(GameObject.Find("河谷镇可驾驶民用车"));
            Vector2 parked = run.VehiclePosition;
            Vector3 left = Quaternion.Euler(0, run.VehicleHeading, 0) * Vector3.left;
            player.SetValue(run, parked + new Vector2(left.x, left.z) * 1.55f);
            Assert.IsTrue(run.CanEnterVehicle);
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var settings = InputSystem.settings;
            var inputBehavior = settings.editorInputBehaviorInPlayMode;
            var background = settings.backgroundBehavior;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            try
            {
                DriveScavengeKey(controller, poll, keyboard, Key.Tab);
                DriveScavengeKey(controller, poll, keyboard, Key.E);
                Assert.IsFalse(run.Driving, "面板打开时一次 E 只关闭面板");
                DriveScavengeKey(controller, poll, keyboard, Key.E);
                Assert.IsTrue(run.Driving);
                double elapsed = run.Survival.elapsed;
                DriveScavengeKey(controller, poll, keyboard, Key.W);
                float deadline = Time.realtimeSinceStartup + 3f;
                while (run.VehicleSpeed < 1.5f && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.Greater(run.VehicleSpeed, 1f, $"默认车位前方无法开出 {parked} / {run.VehicleHeading}");
                Assert.Greater(Vector2.Distance(parked, run.VehiclePosition), .1f);
                Assert.Greater(run.Survival.elapsed, elapsed);
                DriveScavengeKey(controller, poll, keyboard, Key.E);
                Assert.IsFalse(run.Driving, "行驶中有安全落点时可下车");
                Assert.Zero(run.VehicleSpeed);
                parked = run.VehiclePosition;
                Assert.Greater(Vector2.Distance(run.Player, parked), 1.2f);
                DriveScavengeKey(controller, poll, keyboard, Key.E);
                Assert.IsTrue(run.Driving);
                DriveScavengeKey(controller, poll, keyboard, Key.Escape);
                elapsed = run.Survival.elapsed;
                DriveScavengeKey(controller, poll, keyboard, Key.W);
                yield return null;
                Assert.AreEqual(parked, run.VehiclePosition);
                Assert.AreEqual(elapsed, run.Survival.elapsed);
                InputState.Change(keyboard, new KeyboardState());
                type.GetMethod("SaveAndLeaveSurvival", flags).Invoke(controller, null);
                yield return null;
                var saved = store.Read(saveName).Data;
                Assert.IsTrue(saved.vehicle.occupied);
                Click("ContinueSurvival");
                yield return WaitForCrossing();
                run = (ScavengeRun)runField.GetValue(controller);
                Assert.IsTrue(run.Driving);
                Assert.Zero(run.VehicleSpeed);
                Assert.AreEqual(parked, run.VehiclePosition);
                Assert.AreEqual(parked, run.Player);
                Assert.AreEqual(saved.vehicle.heading, run.VehicleHeading);
                Assert.That(run.Survival.elapsed, Is.EqualTo(saved.run.elapsed).Within(1));
                yield return CaptureSurvivalEvidence("resumed-in-car", 1280, 720, batch);
                DriveScavengeKey(controller, poll, keyboard, Key.E);
                Assert.IsFalse(run.Driving);
                yield return CaptureSurvivalEvidence("parked-on-foot", 1280, 720, batch);
                VerifyDrivableBridges(run, directory);
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                settings.editorInputBehaviorInPlayMode = inputBehavior;
                settings.backgroundBehavior = background;
            }
            yield return new ExitPlayMode();
        }

        /// <summary>在三处真实桥头受控落位，再逐帧驾驶越过连续桥段。</summary>
        private static void VerifyDrivableBridges(ScavengeRun source, string directory)
        {
            var world = source.FullTown;
            var covered = new List<Vector2>();
            var evidence = new List<string>();
            foreach (var section in world.Bridges)
            {
                var center = (section.A + section.B) * .5f;
                if (world.WaterMargin(center) > -3 || covered.Exists(point => Vector2.Distance(point, center) < 140))
                    continue;
                covered.Add(center);
                var path = new List<Vector2> { section.A, section.B };
                for (int i = world.Bridges.IndexOf(section) - 1; i >= 0; i--)
                {
                    var previous = world.Bridges[i];
                    if (previous.B != path[0]) break;
                    path.Insert(0, previous.A);
                }
                for (int i = world.Bridges.IndexOf(section) + 1; i < world.Bridges.Count; i++)
                {
                    var next = world.Bridges[i];
                    if (next.A != path[path.Count - 1]) break;
                    path.Add(next.B);
                }
                var entry = (path[1] - path[0]).normalized;
                var leave = (path[path.Count - 1] - path[path.Count - 2]).normalized;
                var save = JsonUtility.FromJson<SurvivalSave>(JsonUtility.ToJson(source.CaptureSurvival()));
                save.vehicle.position = path[0] - entry * 4;
                save.vehicle.heading = Mathf.Atan2(entry.x, entry.y) * Mathf.Rad2Deg;
                save.vehicle.occupied = true;
                var run = ScavengeRun.RestoreSurvival(save, world);
                Assert.IsTrue(run.Driving, $"桥头 {path[0]} 无法容纳整车");
                foreach (var patrol in run.Patrols) patrol.Health = 0;
                path.Add(path[path.Count - 1] + leave * 4);
                foreach (var target in path)
                {
                    for (int step = 0; step < 500 && Vector2.Distance(run.VehiclePosition, target) > .65f; step++)
                    {
                        var delta = target - run.VehiclePosition;
                        float desired = Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg;
                        float steering = Mathf.Clamp(Mathf.DeltaAngle(run.VehicleHeading, desired) / 18, -1, 1);
                        float throttle = run.VehicleSpeed > 5 ? 0 : 1;
                        run.Tick(.05f, new Vector2(steering, throttle));
                    }
                    Assert.That(Vector2.Distance(run.VehiclePosition, target), Is.LessThanOrEqualTo(.65f),
                        $"桥 {covered.Count} 目标 {target}，车辆 {run.VehiclePosition}");
                }
                evidence.Add($"桥 {covered.Count}：{save.vehicle.position} → {run.VehiclePosition}，连续 {path.Count - 2} 段桥面");
            }
            Assert.AreEqual(3, covered.Count);
            System.IO.File.WriteAllLines(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(directory), "bridge-traversal.txt"), evidence);
        }

        [UnityTest]
        public IEnumerator BootRiverValleyPortalPlaysAfterSuccessfulExtraction()
        {
            const string batch = "RiverValleyPortalWithoutPole-20261119";
            const string path = "RiverValleyPortal/PortalBlue";
            foreach (var phase in new[] { "Idle", "Open", "Close" })
            {
                var prefab = UnityEngine.Resources.Load<GameObject>(path + phase);
                if (!prefab) Assert.Ignore("先运行 Tools/Import-RiverValleyPortal.ps1。");
                foreach (var renderer in prefab.GetComponentsInChildren<ParticleSystemRenderer>())
                {
                    Assert.NotNull(renderer.sharedMaterial);
                    Assert.IsTrue(renderer.sharedMaterial.shader.isSupported, renderer.name);
                }
            }

            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            var controller = Object.FindObjectOfType<PrototypeController>();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(PrototypeController);
            var directory = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,
                "../../Backup/Artifacts/" + batch + "/Save-" + System.Guid.NewGuid().ToString("N")));
            type.GetField("survivalStore", flags).SetValue(controller, new Game.JsonFileStore<SurvivalSave>(directory));
            type.GetField("survivalSaveChecked", flags).SetValue(controller, false);
            type.GetMethod("RefreshOverlay", flags).Invoke(controller, null);
            Click("StartSurvival");
            yield return WaitForCrossing();
            var run = (ScavengeRun)type.GetField("scavengeRun", flags).GetValue(controller);
            typeof(ScavengeRun).GetProperty(nameof(ScavengeRun.Player)).SetValue(run, run.ExitPoint + Vector2.left * 1.4f);
            run.Tick(.01f, Vector2.zero);
            type.GetMethod("SyncScavenge", flags).Invoke(controller, new object[] { true });
            Assert.AreEqual(ScavengeOutcome.Exploring, run.Outcome);
            var idle = GameObject.Find("撤离点微光");
            Assert.NotNull(idle);
            var exitMarker = (Transform)type.GetField("scavengeExit", flags).GetValue(controller);
            Assert.IsNull(exitMarker.GetComponent<Renderer>());
            Assert.IsNull(exitMarker.parent.Find("路灯"));
            Assert.IsNull(exitMarker.parent.Find("灯箱"));
            Assert.IsNull(exitMarker.parent.Find("裂口底座"));
            Assert.That(idle.transform.position.x, Is.EqualTo(run.ExitPoint.x).Within(.01f));
            Assert.That(idle.transform.position.z, Is.EqualTo(run.ExitPoint.y).Within(.01f));
            Vector3 portalCenter = idle.transform.position;
            Assert.That(portalCenter.y - exitMarker.position.y, Is.GreaterThan(2f));
            Assert.IsNull(GameObject.Find("撤离传送门开启"));
            yield return new WaitForSecondsRealtime(.4f);
            yield return CaptureSurvivalEvidence("idle", 1280, 720, batch);

            run.Survival.elapsed = ScavengeSurvival.Goal;
            type.GetMethod("InteractSurvival", flags).Invoke(controller, null);
            Assert.AreEqual(ScavengeOutcome.Extracted, run.Outcome);
            yield return null;
            yield return null;
            var opening = GameObject.Find("撤离传送门开启");
            Assert.NotNull(opening);
            Assert.That(Vector3.Distance(opening.transform.position, portalCenter), Is.LessThan(.01f));
            Assert.IsNull(GameObject.Find("撤离点微光"));
            Assert.IsTrue(controller.JourneyProgress.completedFirst);
            yield return new WaitForSecondsRealtime(.45f);
            yield return CaptureSurvivalEvidence("opening", 1280, 720, batch);
            float deadline = Time.realtimeSinceStartup + 3f;
            GameObject closing;
            while (!(closing = GameObject.Find("撤离传送门关闭")) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.NotNull(closing);
            Assert.That(Vector3.Distance(closing.transform.position, portalCenter), Is.LessThan(.01f));
            yield return CaptureSurvivalEvidence("closing", 1280, 720, batch);
            while (GameObject.Find("撤离传送门关闭") && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsNull(GameObject.Find("撤离传送门关闭"));
            yield return CaptureSurvivalEvidence("settlement", 1280, 720, batch);
            type.GetMethod("ReturnFromSurvival", flags).Invoke(controller, null);
            Assert.IsNull(type.GetField("scavengeRun", flags).GetValue(controller));
            Assert.IsTrue(controller.JourneyProgress.completedFirst);
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator BootSpaceReturnPersistsResultsAndRejectsFailedSettlementWrites()
        {
            const string batch = "TaskInfoMenu-20261119";
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(PrototypeController);
            var directory = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,
                "../../Backup/Artifacts/" + batch + "/Save-" + System.Guid.NewGuid().ToString("N")));
            var store = new Game.JsonFileStore<SurvivalSave>(directory);
            var controller = Object.FindObjectOfType<PrototypeController>();
            type.GetField("survivalStore", flags).SetValue(controller, store);
            type.GetField("survivalSaveChecked", flags).SetValue(controller, false);
            type.GetMethod("RefreshOverlay", flags).Invoke(controller, null);
            Click("StartSurvival");
            Assert.IsTrue(controller.IsCrossing);
            yield return WaitForCrossing();
            var run = (ScavengeRun)type.GetField("scavengeRun", flags).GetValue(controller);
            Assert.NotNull(run);
            run.Survival.elapsed = ScavengeSurvival.Goal;
            run.Survival.pack.Add(new SurvivalStack(SurvivalItem.Water));
            run.Survival.caches[0].known = true;
            typeof(ScavengeRun).GetProperty(nameof(ScavengeRun.Player)).SetValue(run, run.ExitPoint);
            run.Tick(.01f, Vector2.zero);
            // 以文件阻塞目录，真实制造写入失败；结算必须留在原会话并允许恢复后重试。
            var blocked = directory + "-blocked";
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(blocked));
            System.IO.File.WriteAllText(blocked, "test");
            type.GetField("survivalStore", flags).SetValue(controller, new Game.JsonFileStore<SurvivalSave>(blocked));
            type.GetMethod("InteractSurvival", flags).Invoke(controller, null);
            Assert.AreEqual(ScavengeOutcome.Extracted, run.Outcome);
            type.GetMethod("ReturnFromSurvival", flags).Invoke(controller, null);
            Assert.AreSame(run, type.GetField("scavengeRun", flags).GetValue(controller));
            Assert.IsNotEmpty((string)type.GetField("survivalSaveError", flags).GetValue(controller));
            Assert.AreEqual(Game.SaveFileStatus.Missing, store.Read("town-survival-v1.json").Status);
            type.GetField("survivalStore", flags).SetValue(controller, store);
            type.GetMethod("ReturnFromSurvival", flags).Invoke(controller, null);
            yield return null;
            Assert.IsNull(type.GetField("scavengeRun", flags).GetValue(controller));
            Assert.IsTrue(controller.JourneyProgress.completedFirst);
            Assert.IsFalse(controller.HasSurvivalSave);
            Assert.IsNull(GameObject.Find("JourneyRecordPanel"));
            Assert.IsNull(GameObject.Find("JourneyCaption"));
            Assert.That(controller.JourneyProgress.carriedSummary, Does.Contain("密封饮水"));
            yield return CaptureSurvivalEvidence("space-return", 1920, 1080, batch);
            var saved = store.Read("town-survival-v1.json").Data;
            Assert.IsFalse(saved.available);
            Assert.IsTrue(saved.journey.completedFirst);
            UnityEditor.SessionState.SetString("OutpostLoop.SpaceReturnTestSave", directory);
            yield return new ExitPlayMode();

            // 新建真实 Boot 会话，读取同一隔离存档，不能靠原控制器内存维持结果。
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            directory = UnityEditor.SessionState.GetString("OutpostLoop.SpaceReturnTestSave", "");
            Assert.IsNotEmpty(directory);
            UnityEditor.SessionState.EraseString("OutpostLoop.SpaceReturnTestSave");
            type = typeof(PrototypeController);
            flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            controller = Object.FindObjectOfType<PrototypeController>();
            store = new Game.JsonFileStore<SurvivalSave>(directory);
            type.GetField("survivalStore", flags).SetValue(controller, store);
            type.GetField("survivalSaveChecked", flags).SetValue(controller, false);
            type.GetMethod("RefreshOverlay", flags).Invoke(controller, null);
            type.GetMethod("RefreshOverlay", flags).Invoke(controller, null);
            Assert.IsTrue(controller.JourneyProgress.completedFirst);
            Assert.IsNull(GameObject.Find("JourneyRecordPanel"));
            Assert.IsNull(GameObject.Find("JourneyCaption"));
            Assert.AreEqual(ScavengeOutcome.Extracted, controller.JourneyProgress.lastOutcome);
            Assert.That(controller.JourneyProgress.carriedSummary, Does.Contain("密封饮水"));
            Click("StartSurvival");
            yield return WaitForCrossing();
            run = (ScavengeRun)type.GetField("scavengeRun", flags).GetValue(controller);
            Assert.IsEmpty(run.Survival.pack);
            Assert.IsNull(run.Survival.weapon);
            Assert.IsNull(run.Survival.torch);
            type.GetMethod("SaveAndLeaveSurvival", flags).Invoke(controller, null);
            yield return null;
            saved = store.Read("town-survival-v1.json").Data;
            Assert.IsTrue(saved.available);
            Assert.IsTrue(saved.journey.completedFirst);
            Click("ContinueSurvival");
            yield return WaitForCrossing();
            Assert.IsTrue(controller.JourneyProgress.completedFirst);
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator BootSurvivalHelpAndEarlyExitPreserveTheLiveRun()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            var controller = Object.FindObjectOfType<PrototypeController>();
            var type = typeof(PrototypeController);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var directory = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,
                "../../Backup/Artifacts/FirstJourneyUsability-20261113/TestSave-" + System.Guid.NewGuid().ToString("N")));
            type.GetField("survivalStore", flags).SetValue(controller, new Game.JsonFileStore<SurvivalSave>(directory));
            type.GetField("survivalSaveChecked", flags).SetValue(controller, false);
            Click("StartSurvival");
            yield return WaitForCrossing();
            var run = (ScavengeRun)type.GetField("scavengeRun", flags).GetValue(controller);
            var poll = type.GetMethod("PollScavenge", flags);
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var settings = InputSystem.settings;
            var editorInput = settings.editorInputBehaviorInPlayMode;
            var background = settings.backgroundBehavior;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            try
            {
                Assert.That((string)type.GetMethod("SurvivalObjective", flags).Invoke(controller, null), Does.Contain("补给"));
                // 帮助冻结时钟及移动；关闭帮助的 Esc 不会再次打开暂停。
                type.GetField("survivalHelpOpen", flags).SetValue(controller, true);
                yield return CaptureSurvivalEvidence("help", 1280, 720);
                yield return CaptureSurvivalEvidence("help", 1920, 1080);
                double elapsed = run.Survival.elapsed;
                var position = run.Player;
                DriveScavengeKey(controller, poll, keyboard, Key.W);
                yield return null;
                Assert.AreEqual(elapsed, run.Survival.elapsed);
                Assert.AreEqual(position, run.Player);
                DriveScavengeKey(controller, poll, keyboard, Key.Escape);
                Assert.IsFalse((bool)type.GetField("survivalHelpOpen", flags).GetValue(controller));
                Assert.IsFalse((bool)type.GetField("scavengePaused", flags).GetValue(controller));
                type.GetField("scavengePaused", flags).SetValue(controller, true);
                type.GetField("survivalHelpOpen", flags).SetValue(controller, true);
                DriveScavengeKey(controller, poll, keyboard, Key.Escape);
                Assert.IsFalse((bool)type.GetField("survivalHelpOpen", flags).GetValue(controller));
                Assert.IsTrue((bool)type.GetField("scavengePaused", flags).GetValue(controller));
                type.GetField("scavengePaused", flags).SetValue(controller, false);
                yield return CaptureSurvivalEvidence("hud", 1920, 1080);

                // 容器切到随身使用后必须关闭原搜索，物品只在动作完成时扣除。
                typeof(ScavengeRun).GetProperty(nameof(ScavengeRun.Player)).SetValue(run, run.CachePoints[0]);
                DriveScavengeKey(controller, poll, keyboard, Key.E);
                Assert.AreEqual(0, run.OpenCache);
                DriveScavengeKey(controller, poll, keyboard, Key.Tab);
                Assert.AreEqual(-1, run.OpenCache);
                Assert.IsTrue((bool)type.GetField("survivalPackOpen", flags).GetValue(controller));
                run.Survival.pack.Add(new SurvivalStack(SurvivalItem.Water));
                run.Survival.water = 40;
                yield return CaptureSurvivalEvidence("pack", 1280, 720);
                type.GetMethod("StartSurvivalAction", flags).Invoke(controller, new object[] { SurvivalAction.Drink, 0 });
                Assert.IsFalse((bool)type.GetField("survivalPackOpen", flags).GetValue(controller));
                Assert.AreEqual(1, run.Survival.pack.Count);
                for (int i = 0; i < 22; i++) run.Tick(.1f, Vector2.zero);
                Assert.IsEmpty(run.Survival.pack);
                Assert.That(run.Survival.water, Is.GreaterThan(74));

                // 未达标按 E 先确认，取消后仍是同一趟；达标后原撤离规则仍有效。
                typeof(ScavengeRun).GetProperty(nameof(ScavengeRun.Player)).SetValue(run, run.ExitPoint);
                DriveScavengeKey(controller, poll, keyboard, Key.E);
                Assert.IsTrue((bool)type.GetField("survivalConfirmExit", flags).GetValue(controller));
                yield return CaptureSurvivalEvidence("early-exit", 1280, 720);
                elapsed = run.Survival.elapsed;
                yield return null;
                Assert.AreEqual(elapsed, run.Survival.elapsed);
                Assert.AreEqual(ScavengeOutcome.Exploring, run.Outcome);
                DriveScavengeKey(controller, poll, keyboard, Key.Escape);
                Assert.IsFalse((bool)type.GetField("survivalConfirmExit", flags).GetValue(controller));
                Assert.AreEqual(ScavengeOutcome.Exploring, run.Outcome);
                run.Survival.elapsed = ScavengeSurvival.Goal;
                DriveScavengeKey(controller, poll, keyboard, Key.E);
                Assert.AreEqual(ScavengeOutcome.Extracted, run.Outcome);
                yield return CaptureSurvivalEvidence("result", 1280, 720);
                Assert.That((string)type.GetMethod("SurvivalResultDetails", flags).Invoke(controller, null), Does.Contain("空间不存放"));
                type.GetMethod("RestartScavenge", flags).Invoke(controller, null);
                var retry = (ScavengeRun)type.GetField("scavengeRun", flags).GetValue(controller);
                Assert.AreNotSame(run, retry);
                Assert.IsEmpty(retry.Survival.pack);
                Assert.That(retry.Survival.elapsed, Is.LessThan(2));
                Assert.IsFalse((bool)type.GetField("survivalHelpOpen", flags).GetValue(controller));
                Assert.IsFalse((bool)type.GetField("survivalConfirmExit", flags).GetValue(controller));
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                settings.editorInputBehaviorInPlayMode = editorInput;
                settings.backgroundBehavior = background;
            }
            yield return new ExitPlayMode();
        }

        /// <summary>从真实 Game 视图保留含 IMGUI 的画面；仅切换已有分辨率并恢复原选项。</summary>
        private static IEnumerator CaptureSurvivalEvidence(string name, int width, int height, string batch = "FirstJourneyUsability-20261113")
        {
            var assembly = typeof(UnityEditor.Editor).Assembly;
            var type = assembly.GetType("UnityEditor.GameView");
            var window = UnityEditor.EditorWindow.GetWindow(type);
            var selected = type.GetProperty("selectedSizeIndex");
            int previous = (int)selected.GetValue(window);
            var sizesType = assembly.GetType("UnityEditor.GameViewSizes");
            var sizes = typeof(UnityEditor.ScriptableSingleton<>).MakeGenericType(sizesType)
                .GetProperty("instance").GetValue(null);
            var group = sizesType.GetMethod("GetGroup").Invoke(sizes,
                new[] { sizesType.GetProperty("currentGroupType").GetValue(sizes) });
            var names = (string[])group.GetType().GetMethod("GetDisplayTexts").Invoke(group, null);
            int index = System.Array.FindIndex(names, n => n.Contains($"{width}x{height}") && !n.Contains("Portrait"));
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "当前 Game 视图缺少所需横屏尺寸");
            try
            {
                selected.SetValue(window, index);
                for (int i = 0; i < 4; i++) yield return null;
                Assert.AreEqual(width, Screen.width);
                Assert.AreEqual(height, Screen.height);
                ScreenCapture.CaptureScreenshot(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,
                    $"../../Backup/Artifacts/{batch}/{name}-{width}.png")));
                for (int i = 0; i < 4; i++) yield return null;
            }
            finally { selected.SetValue(window, previous); }
        }

        [UnityTest]
        public IEnumerator BootRiverValleyInfectedUseMovementDrivenChaseAndSourcePatrol()
        {
            if (UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Art/RiverValleyInfected/Content/Zombie2.prefab") == null)
                Assert.Ignore("先从本地 My Assets 缓存运行 Tools/Import-RiverValleyInfected.ps1。");
            const string batch = "RiverValleyInfected-20261119";
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            var controller = Object.FindObjectOfType<PrototypeController>();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(PrototypeController);
            var directory = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,
                "../../Backup/Artifacts/" + batch + "/Save-" + System.Guid.NewGuid().ToString("N")));
            type.GetField("survivalStore", flags).SetValue(controller, new Game.JsonFileStore<SurvivalSave>(directory));
            type.GetField("survivalSaveChecked", flags).SetValue(controller, false);
            type.GetMethod("RefreshOverlay", flags).Invoke(controller, null);
            Click("StartSurvival");
            yield return WaitForCrossing();

            var run = (ScavengeRun)type.GetField("scavengeRun", flags).GetValue(controller);
            var visuals = (RiverValleyInfectedVisual[])type.GetField("infectedVisuals", flags).GetValue(controller);
            Assert.AreEqual(12, visuals.Length);
            int fastCount = 0;
            var canSeePatrol = type.GetMethod("CanSeeSurvivalInteriorAt", flags);
            for (int i = 0; i < visuals.Length; i++)
            {
                var patrol = run.Patrols[i];
                Assert.IsTrue(visuals[i].GetComponentInChildren<Animator>(true).isHuman);
                if (patrol.Fast) fastCount++;
                StringAssert.StartsWith(patrol.Fast ? "快速感染者" : "普通感染者", visuals[i].name);
                // 尚未进入的室内沿用遮挡规则，隐藏的表现对象不接收位置刷新。
                bool visible = patrol.Alive && (bool)canSeePatrol.Invoke(controller, new object[] { patrol.Position });
                Assert.AreEqual(visible, visuals[i].gameObject.activeSelf);
                if (!visible) continue;
                Assert.AreEqual(patrol.Position.x, visuals[i].transform.position.x, .002f);
                Assert.AreEqual(patrol.Position.y, visuals[i].transform.position.z, .002f);
            }
            Assert.AreEqual(2, fastCount);

            // 暂停领域循环，在 Boot 创建的表现对象上送入可控样本。
            type.GetField("scavengePaused", flags).SetValue(controller, true);
            var present = typeof(RiverValleyInfectedVisual).GetMethod("Present", flags);
            var phase = typeof(RiverValleyInfectedVisual).GetField("chasePhase", flags);
            var blend = typeof(RiverValleyInfectedVisual).GetField("chaseWeight", flags);
            var patrolTime = typeof(RiverValleyInfectedVisual).GetField("walkTime", flags);
            Vector3 normalStart = visuals[0].transform.position;
            Vector3 fastStart = visuals[3].transform.position;
            void Frame(int index, Vector3 point, Vector2 heading, bool chase, bool reset) =>
                present.Invoke(visuals[index], new object[] { point, heading, chase, reset, reset ? 0f : .02f });

            Frame(0, normalStart, Vector2.up, false, true);
            for (int i = 1; i <= 10; i++)
                Frame(0, normalStart + Vector3.forward * (.041f * i), Vector2.up, false, false);
            Assert.AreEqual(.2f, (float)patrolTime.GetValue(visuals[0]), .001f);
            Assert.AreEqual(0f, (float)blend.GetValue(visuals[0]));

            foreach (int index in new[] { 0, 3 })
            {
                var start = index == 0 ? normalStart : fastStart;
                float speed = index == 0 ? 4.15f : 5.35f;
                float cycle = index == 0 ? .62f : .49f;
                Frame(index, start, Vector2.up, true, true);
                for (int i = 1; i <= 12; i++)
                    Frame(index, start + Vector3.forward * (.02f * speed * i), Vector2.up, true, false);
                Assert.AreEqual(.24f / cycle, (float)phase.GetValue(visuals[index]), .001f);
                Assert.AreEqual(1f, (float)blend.GetValue(visuals[index]), .001f);
                var corner = start + new Vector3(.035f, .028f, .24f * speed);
                Frame(index, corner, Vector2.right, true, false);
                Assert.AreEqual(corner, visuals[index].transform.position);
                Assert.Greater(Vector3.Dot(Vector3.right, visuals[index].transform.forward), .99f);
                var foot = visuals[index].GetComponentInChildren<Animator>().GetBoneTransform(HumanBodyBones.LeftFoot);
                Assert.IsFalse(float.IsNaN(foot.position.sqrMagnitude));
                for (int i = 1; i <= 8; i++)
                    Frame(index, corner + Vector3.right * (.041f * i), Vector2.right, false, false);
                Assert.AreEqual(0f, (float)blend.GetValue(visuals[index]), .001f);
                Frame(index, start, Vector2.up, false, true);
                Assert.AreEqual(0f, (float)phase.GetValue(visuals[index]));
            }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator BootRiverValleyShowsExitAndInteriorsOnlyInContext()
        {
            const string batch = "RiverValleyVisibility-20261120";
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            var controller = Object.FindObjectOfType<PrototypeController>();
            var type = typeof(PrototypeController);
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var evidence = System.Environment.GetEnvironmentVariable("OUTPOSTLOOP_VISIBILITY_EVIDENCE") ??
                System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../Backup/Artifacts/" + batch));
            var directory = System.IO.Path.Combine(evidence, "Save-" + System.Guid.NewGuid().ToString("N"));
            type.GetField("survivalStore", flags).SetValue(controller, new Game.JsonFileStore<SurvivalSave>(directory));
            type.GetField("survivalSaveChecked", flags).SetValue(controller, false);
            type.GetMethod("RefreshOverlay", flags).Invoke(controller, null);
            Click("StartSurvival");
            yield return WaitForCrossing();

            var run = (ScavengeRun)type.GetField("scavengeRun", flags).GetValue(controller);
            var root = (Transform)type.GetField("scavengeRoot", flags).GetValue(controller);
            var camera = (Camera)type.GetField("gameCamera", flags).GetValue(controller);
            var player = typeof(ScavengeRun).GetProperty(nameof(ScavengeRun.Player));
            var sync = type.GetMethod("SyncScavenge", flags);
            var occlusion = type.GetMethod("UpdateScavengeOccluders", flags);
            var label = type.GetMethod("ShouldDrawScavengeExitLabel", flags);
            var map = type.GetProperty("ShouldDrawScavengeMapExit", flags);
            var screen = new Vector2(1280, 720);
            bool ShowsLabel(Vector3 point) => (bool)label.Invoke(controller, new object[] { point, screen });
            void Place(Vector2 point)
            {
                player.SetValue(run, point);
                sync.Invoke(controller, new object[] { true });
            }

            Assert.IsFalse(ShowsLabel(new Vector3(640, 360, 15)));
            Place(run.ExitPoint + Vector2.left * 3.5f);
            run.Tick(.01f, Vector2.zero);
            Assert.IsTrue(run.ExitKnown);
            Assert.IsFalse(run.AtExit, "现场标签可比实际交互范围更早出现");
            Assert.IsTrue(ShowsLabel(new Vector3(640, 360, 15)));
            Assert.IsFalse(ShowsLabel(new Vector3(1280, 360, 15)));
            Assert.IsFalse(ShowsLabel(new Vector3(640, 360, -15)));
            Place(run.ExitPoint + Vector2.left * 4.01f);
            Assert.IsFalse(ShowsLabel(new Vector3(640, 360, 15)));
            Assert.IsTrue((bool)map.GetValue(controller));
            run.Survival.elapsed = ScavengeSurvival.Goal;
            Assert.IsFalse(ShowsLabel(new Vector3(640, 360, 15)), "撤离窗口不能把现场标签钉在远处");
            Place(run.ExitPoint + Vector2.left * 1.4f);
            Assert.IsTrue(run.AtExit);
            Assert.IsTrue(ShowsLabel(new Vector3(640, 360, 15)));
            run.Survival.elapsed = 0;

            foreach (var patrol in run.Patrols) patrol.Health = 0;
            var rooms = new SurvivalRoomSet(run.FullTown.Interiors);
            var crates = (Transform[])type.GetField("scavengeCrates", flags).GetValue(controller);
            var clinic = root.Find("房间内部 2").Find("诊所幸存者").gameObject;
            for (int i = 0; i < run.FullTown.Interiors.Length; i++)
            {
                var roomRoot = root.Find("房间内部 " + i);
                var door = rooms.Door(i).center;
                Place(door - Vector2.right);
                Assert.IsFalse(roomRoot.gameObject.activeSelf, $"门外看见室内 {i}");
                if (i == 2) Assert.IsFalse(clinic.activeInHierarchy);
                if (i < 2 && !run.Survival.rooms[i].open) Assert.IsTrue(run.ToggleSurvivalDoor());
                Place(door + Vector2.right * .1f);
                Assert.IsFalse(roomRoot.gameObject.activeSelf, $"门槛上提前显示 {i}");
                for (int step = 0; step < 8; step++) run.Tick(.05f, Vector2.right);
                sync.Invoke(controller, new object[] { true });
                Assert.IsTrue(roomRoot.gameObject.activeSelf, $"进门后室内仍隐藏 {i}");
                if (i == 2)
                {
                    Assert.IsTrue(clinic.activeInHierarchy);
                    CaptureVisibilityCamera(camera, evidence, "clinic-inside", 1280, 720);
                }
                for (int k = 0; k < crates.Length; k++)
                    if (run.Survival.caches[k].active && rooms.Footprint(i).Contains(run.CachePoints[k]))
                        Assert.IsTrue(crates[k].gameObject.activeSelf, $"入内未显示搜点 {i}/{k}");
                for (int step = 0; step < 14; step++) run.Tick(.05f, Vector2.left);
                sync.Invoke(controller, new object[] { true });
                Assert.IsFalse(roomRoot.gameObject.activeSelf, $"出门后室内仍可见 {i}");
                for (int k = 0; k < crates.Length; k++)
                    if (rooms.Footprint(i).Contains(run.CachePoints[k]))
                        Assert.IsFalse(crates[k].gameObject.activeSelf, $"室外看到室内搜点 {i}/{k}");
            }

            Rect shop = rooms.Footprint(0);
            Place(new Vector2(shop.xMax + .8f, shop.center.y));
            Renderer shopWall = null;
            foreach (Transform part in root)
                if (part.name == "现有外墙" && Mathf.Abs(part.position.x - (shop.xMax - .14f)) < .05f)
                    shopWall = part.GetComponent<Renderer>();
            Assert.NotNull(shopWall);
            camera.transform.position = shopWall.bounds.center + Vector3.right * 5;
            occlusion.Invoke(controller, null);
            var original = shopWall.sharedMaterial;
            camera.transform.position = shopWall.bounds.center + Vector3.left * 4 + Vector3.up * 3;
            camera.transform.LookAt(new Vector3(run.Player.x, shopWall.bounds.center.y, run.Player.y));
            occlusion.Invoke(controller, null);
            Assert.AreNotSame(original, shopWall.sharedMaterial);
            Assert.IsFalse(shopWall.forceRenderingOff, "室外使用半透明墙体");
            Assert.AreEqual(3000, shopWall.sharedMaterial.renderQueue);
            Assert.IsFalse(root.Find("房间内部 0").gameObject.activeSelf);
            CaptureVisibilityCamera(camera, evidence, "outside-wall-faded", 1920, 1080);
            camera.transform.position = shopWall.bounds.center + Vector3.right * 5;
            occlusion.Invoke(controller, null);
            Assert.AreSame(original, shopWall.sharedMaterial, "离开遮挡后还原原材质");

            var town = root.Find("河谷镇整图");
            var closedRoof = town.Find("封闭建筑屋顶").GetComponentInChildren<Renderer>();
            var closedBounds = closedRoof.bounds;
            var nearRoof = new Vector2(closedBounds.max.x + .4f, closedBounds.center.z);
            Place(nearRoof);
            var torso = (Vector3)type.GetMethod("SurvivalPosition", flags)
                .Invoke(controller, new object[] { nearRoof, .95f });
            var roofPoint = new Vector3(closedBounds.max.x - .3f, closedBounds.center.y, closedBounds.center.z);
            camera.transform.position = torso + Vector3.right * 5 + Vector3.up;
            occlusion.Invoke(controller, null);
            var solidRoof = closedRoof.sharedMaterial;
            camera.transform.position = torso + (roofPoint - torso) * 2;
            occlusion.Invoke(controller, null);
            Assert.AreNotSame(solidRoof, closedRoof.sharedMaterial, "封闭建筑屋顶遮挡角色时也要淡化");
            camera.transform.position = torso + Vector3.right * 5 + Vector3.up;
            occlusion.Invoke(controller, null);
            Assert.AreSame(solidRoof, closedRoof.sharedMaterial);
            yield return new ExitPlayMode();
        }

        /// <summary>从本局摄像机保留受控位置的渲染画面，IMGUI 由对应可见性断言检查。</summary>
        private static void CaptureVisibilityCamera(Camera camera, string directory, string name, int width, int height)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            System.IO.Directory.CreateDirectory(directory);
            var target = RenderTexture.GetTemporary(width, height, 24);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, name + ".png"), image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
                Object.Destroy(image);
            }
        }

        [UnityTest]
        public IEnumerator BootRiverValleyFullTownHasSixInteriorsAndKeepsLegacySaves()
        {
            const string batch = "ShopInterior-20261119";
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            var controller = Object.FindObjectOfType<PrototypeController>();
            var type = typeof(PrototypeController);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var directory = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,
                "../../Backup/Artifacts/" + batch + "/Save-" + System.Guid.NewGuid().ToString("N")));
            var store = new Game.JsonFileStore<SurvivalSave>(directory);
            type.GetField("survivalStore", flags).SetValue(controller, store);
            type.GetField("survivalSaveChecked", flags).SetValue(controller, false);
            type.GetMethod("RefreshOverlay", flags).Invoke(controller, null);
            Click("StartSurvival");
            var loading = GameObject.Find("CrossingProgress").GetComponent<Text>();
            var loadingStatus = GameObject.Find("CrossingStatus").GetComponent<Text>();
            Assert.AreEqual("0%", loading.text);
            var progress = new HashSet<int>();
            var labels = new HashSet<string>();
            int previousProgress = 0;
            float deadline = Time.realtimeSinceStartup + 15;
            while (controller.IsCrossing && Time.realtimeSinceStartup < deadline)
            {
                int current = int.Parse(loading.text.TrimEnd('%'));
                Assert.GreaterOrEqual(current, previousProgress);
                previousProgress = current;
                progress.Add(current);
                labels.Add(loadingStatus.text);
                yield return null;
            }
            Assert.IsFalse(controller.IsCrossing);
            Assert.IsTrue(progress.Contains(100), "只有地图和会话就绪后才能显示满格");
            Assert.IsTrue(labels.Contains("正在生成地形"));
            Assert.IsTrue(labels.Contains("正在生成建筑外景"));
            Assert.IsTrue(labels.Contains("正在布置角色和搜点"));
            Assert.GreaterOrEqual(progress.Count, 8, "实际地图构建应显示多次可见进度");
            var field = type.GetField("scavengeRun", flags);
            var run = (ScavengeRun)field.GetValue(controller);
            Assert.NotNull(run.FullTown);
            Assert.AreEqual(2, run.Survival.layoutVersion);
            Assert.AreEqual(2000, run.MapBounds.width);
            Assert.AreEqual(2000, run.MapBounds.height);
            Assert.AreEqual(6, run.FullTown.Interiors.Length);
            Assert.IsNull(GameObject.Find("东侧街区边界"));
            Assert.NotNull(GameObject.Find("河谷镇整图"));
            var city=Object.FindObjectOfType<OutpostLoop.RiverValley.RiverValleyTownPreview>();
            Assert.Greater(city.CityBuildingCount,200);
            // 封闭外景只用半透明遮挡，不能进入旧探索的整组切除列表。
            var closed = city.transform.Find("封闭建筑").GetComponentsInChildren<Renderer>();
            Assert.IsNotEmpty(closed);
            var cutaway = (List<Renderer[]>)type.GetField("scavengeOccluders", flags).GetValue(controller);
            foreach (var group in cutaway)
                foreach (var renderer in group)
                    CollectionAssert.DoesNotContain(closed, renderer);
            var meshFilters=GameObject.Find("河谷镇整图").GetComponentsInChildren<MeshFilter>();
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Directory.GetParent(directory).FullName,"scene-counts.txt"),
                "City fixture dressing="+city.CityDressingFixtureCount+"\nCity surfaces="+city.CityDressingSurfaceCount+"\nCity low dressing="+city.CityDressingGroundCount+"\nDistrict pads="+city.CityDistrictPadCount+"\nCity buildings="+city.CityBuildingCount+"\nCity trees="+city.CityTreeCount+"\nCity vehicles="+city.CityVehicleCount+"\nCity road segments="+city.CityRoadSegmentCount+"\nMesh filters="+meshFilters.Length);
            yield return CaptureSurvivalEvidence("street-start",1920,1080,batch);
            int alive = 0, fast = 0;
            foreach(var patrol in run.Patrols) { if(patrol.Alive)alive++; if(patrol.Fast)fast++; }
            Assert.AreEqual(12, alive); Assert.AreEqual(2, fast);
            int waterCount=0,foodCount=0,nearWater=0,nearFood=0;
            foreach(int index in ScavengeSurvival.ActiveCaches)
                foreach(var item in run.Survival.caches[index].items)
                {
                    if(item.item==SurvivalItem.Water){waterCount+=item.count;if(index<=5)nearWater+=item.count;}
                    if(item.item==SurvivalItem.Food){foodCount+=item.count;if(index<=5)nearFood+=item.count;}
                }
            Assert.AreEqual(12,waterCount);Assert.AreEqual(8,foodCount);
            Assert.GreaterOrEqual(nearWater,7);Assert.GreaterOrEqual(nearFood,5);
            var threat=run.Patrols[0];
            threat.Position=run.Player+Vector2.right*.2f;
            for(int step=0;step<30;step++)run.Tick(.05f,Vector2.zero);
            Assert.Less(run.Survival.health,100,"整图感染者应能发现并攻击玩家");
            run.Survival.health=100;run.Survival.bleeding=false;
            // 停用敌人仅隔离几何验收；不把它当作真人整局生存证据。
            foreach(var patrol in run.Patrols)patrol.Health=0;
            var player = typeof(ScavengeRun).GetProperty(nameof(ScavengeRun.Player));
            for(int step=0;step<30 && Vector2.Distance(run.Player,run.CachePoints[0])>.3f;step++)
                run.Tick(.05f,(run.CachePoints[0]-run.Player).normalized);
            run.Interact();
            Assert.AreEqual(0,run.OpenCache);
            for(int step=0;step<30 && run.SearchRemaining>0;step++)run.Tick(.05f,Vector2.zero);
            Assert.IsTrue(ScavengeSurvival.Transfer(run.Survival.caches[0].items,0,run.Survival.pack,6));
            Assert.IsTrue(run.Survival.pack.Exists(item=>item.item==SurvivalItem.Water));
            run.CloseSearch();
            foreach(int cache in ScavengeSurvival.ActiveCaches)
            {
                string blocked="";
                foreach(var solid in run.FullTown.Solids)
                    if(solid.Contains(run.CachePoints[cache],.1f))blocked += $" {solid.Center}/{solid.Half}/{solid.Angle}";
                Assert.IsTrue(run.FullTown.Walkable(run.CachePoints[cache], .1f),run.CacheNames[cache]+"不可达 "+run.CachePoints[cache]+blocked);
            }
            // 受控定位只用于展示已替换主街，不冒充从落地完整步行到达。
            var streetPoint=new Vector2(-134.38f,-20.94f);
            Assert.IsTrue(run.FullTown.Walkable(streetPoint,.34f),"展示起点应在可通行主街上");
            player.SetValue(run,streetPoint);
            type.GetMethod("SyncScavenge",flags).Invoke(controller,new object[]{true});
            yield return CaptureSurvivalEvidence("main-street",1920,1080,batch);
            for(int i=0;i<6;i++)
            {
                var rooms=new SurvivalRoomSet(run.FullTown.Interiors);
                var door=rooms.Door(i).center;
                player.SetValue(run,door-Vector2.right*1.2f);
                if(i<2)Assert.IsTrue(run.ToggleSurvivalDoor());
                for(int step=0;step<15;step++)run.Tick(.05f,Vector2.right);
                Assert.Greater(run.Player.x,door.x+.5f,"重点建筑入口不能通行 "+i);
                if(i<2)
                {
                    Assert.AreEqual(i,run.InsideSurvivalRoom);
                    if(i==0)
                    {
                        var shop=run.FullTown.Shop;
                        type.GetMethod("SyncScavenge",flags).Invoke(controller,new object[]{true});
                        Assert.NotNull(GameObject.Find("主街杂货店完整室内"));
                        yield return WalkShopPath(controller,run,batch,"shop-checkout",shop.Room.position+new Vector2(5.5f,12.5f),shop.CheckoutTarget);
                        run.Interact();
                        Assert.AreEqual(1,run.OpenCache,"收银区应可搜索原杂货店容器");
                        for(int step=0;step<20 && run.SearchRemaining>0;step++)run.Tick(.05f,Vector2.zero);
                        Assert.IsTrue(run.Survival.caches[1].searched);
                        run.CloseSearch();
                        yield return WalkShopPath(controller,run,batch,"shop-rear",shop.Room.position+new Vector2(5.5f,12.5f),shop.StockTarget);
                        yield return WalkShopPath(controller,run,batch,"shop-bed",shop.Room.position+new Vector2(28.2f,12.5f),shop.Room.position+new Vector2(28.2f,22),shop.Room.position+new Vector2(29.35f,22),shop.BedApproach);
                    }
                    else player.SetValue(run,run.RoomSet.Bed(i));
                    run.Survival.energy=40;
                    Assert.IsTrue(run.SleepSurvival());
                    run.Tick(.1f,Vector2.zero);
                    Assert.Greater(run.Survival.energy,40);
                    run.WakeSurvival("验收起身");
                    if(i==0)
                    {
                        var shop=run.FullTown.Shop;
                        yield return WalkShopPath(controller,run,batch,"shop-storage",shop.Room.position+new Vector2(29.35f,22),shop.Room.position+new Vector2(28.2f,22),shop.Room.position+new Vector2(28.2f,1.2f),shop.CabinetApproach);
                    }
                    else player.SetValue(run,run.RoomSet.Cabinet(i));
                    type.GetMethod("InteractSurvival",flags).Invoke(controller,null);
                    Assert.AreSame(run.Survival.rooms[i].storage,type.GetField("survivalContainer",flags).GetValue(controller));
                    Assert.IsTrue(ScavengeSurvival.Transfer(run.Survival.pack,0,run.Survival.rooms[i].storage.items,12));
                    Assert.IsTrue(ScavengeSurvival.Transfer(run.Survival.rooms[i].storage.items,0,run.Survival.pack,6));
                    type.GetMethod("CloseSurvivalPanels",flags).Invoke(controller,null);
                    if(i==0)
                    {
                        var shop=run.FullTown.Shop;
                        yield return WalkShopPath(controller,run,batch,"shop-return",shop.Room.position+new Vector2(28.2f,1.2f),shop.Room.position+new Vector2(28.2f,12.5f),shop.Entry);
                        for(int step=0;step<12;step++)run.Tick(.05f,Vector2.left);
                        Assert.Less(run.Player.x,door.x-.5f,"必须可以实际从西门离开商店");
                    }
                    else player.SetValue(run,door+Vector2.right*1.2f);
                    Assert.IsTrue(run.ToggleSurvivalDoor());
                    Assert.IsFalse(ScavengeLayout.ClearLine(door-Vector2.right,door+Vector2.right,.1f,run.RunWalls));
                    Assert.IsTrue(run.ToggleSurvivalDoor());
                }
                type.GetMethod("SyncScavenge",flags).Invoke(controller,new object[]{true});
                yield return CaptureSurvivalEvidence("interior-"+i,1280,720,batch);
            }
            // 受控定位只验证区域陈设可见性，六入口步行另由上方断言覆盖。
            foreach(int area in new[]{3,4})
            {
                var point=city.DressingDistrictCenters[area];
                Assert.AreNotEqual(Vector2.zero,point,"重点区域应有独立前场 "+area);
                Assert.IsTrue(run.FullTown.Walkable(point,.34f));
                player.SetValue(run,point);
                type.GetMethod("SyncScavenge",flags).Invoke(controller,new object[]{true});
                yield return CaptureSurvivalEvidence(area==3?"warehouse-yard":"hospital-parking",1920,1080,batch);
            }
            var warehouse=run.FullTown.Interiors[3];
            var stockView=new Vector2(warehouse.xMin+6,warehouse.center.y-8.2f);
            Assert.IsTrue(run.FullTown.Walkable(stockView,.34f));
            player.SetValue(run,stockView);
            type.GetMethod("SyncScavenge",flags).Invoke(controller,new object[]{true});
            yield return CaptureSurvivalEvidence("warehouse-stock",1920,1080,batch);
            // 三桥的河中段可走，离开桥面落入河道则禁止通行。
            var bridges=new List<Vector2>();
            foreach(var bridge in run.FullTown.Bridges)
            {
                var center=(bridge.A+bridge.B)/2;
                if(run.FullTown.WaterMargin(center)>-3)continue;
                if(bridges.Exists(other=>Vector2.Distance(other,center)<140))continue;
                Assert.IsTrue(run.FullTown.Walkable(center,.34f),"桥面不可走");
                bridges.Add(center);
                var segmentStart=bridge.A;
                player.SetValue(run,segmentStart);
                foreach(var section in run.FullTown.Bridges)
                {
                    if(Vector2.Distance((section.A+section.B)/2,center)>140)continue;
                    player.SetValue(run,section.A);
                    for(int step=0;step<250 && Vector2.Distance(run.Player,section.B)>.25f;step++)
                        run.Tick(.05f,(section.B-run.Player).normalized);
                    Assert.That(Vector2.Distance(run.Player,section.B),Is.LessThan(.6f),"桥梁路段不能实际步行通过");
                }
                var normal=(bridge.B-bridge.A).normalized;
                normal=new Vector2(-normal.y,normal.x);
                bool checkedWater=false;
                for(int offset=4;offset<=60&&!checkedWater;offset+=4)
                    for(int side=-1;side<=1&&!checkedWater;side+=2)
                    {
                        var water=center+normal*(bridge.Width/2+offset)*side;
                        if(run.FullTown.WaterMargin(water)>-2)continue;
                        bool onBridge=false;
                        foreach(var other in run.FullTown.Bridges)
                            if(other.Distance(water)<=other.Width/2) { onBridge=true; break; }
                        if(onBridge)continue;
                        Assert.IsFalse(run.FullTown.Walkable(water,.34f),"离开桥面仍能穿河");
                        checkedWater=true;
                    }
                Assert.IsTrue(checkedWater,"未找到桥边河道验证点");
                player.SetValue(run,center);
                type.GetMethod("SyncScavenge",flags).Invoke(controller,new object[]{true});
                yield return CaptureSurvivalEvidence("bridge-"+bridges.Count,1920,1080,batch);
            }
            Assert.AreEqual(3,bridges.Count,"未覆盖三座实际桥梁");
            type.GetField("survivalRegionMapOpen",flags).SetValue(controller,true);
            double elapsed=run.Survival.elapsed;
            yield return CaptureSurvivalEvidence("full-region",1280,720,batch);
            Assert.AreEqual(elapsed,run.Survival.elapsed);
            type.GetField("survivalRegionMapOpen",flags).SetValue(controller,false);
            var savedPosition=run.Player;
            type.GetMethod("SaveAndLeaveSurvival",flags).Invoke(controller,null);
            yield return null;
            Assert.AreEqual(2,store.Read("town-survival-v1.json").Data.run.layoutVersion);
            Click("ContinueSurvival");
            yield return WaitForCrossing();
            run=(ScavengeRun)field.GetValue(controller);
            Assert.NotNull(run.FullTown);
            Assert.That(Vector2.Distance(savedPosition,run.Player),Is.LessThan(.01f));
            type.GetMethod("LeaveScavenge",flags).Invoke(controller,null);
            yield return null;
            // 版本 1 的空房间允许保存在未来货架占地内；继续时必须保持原坐标。
            var oldFull=store.Read("town-survival-v1.json").Data;
            oldFull.run.layoutVersion=1;
            var oldPoint=new Vector2(-32.1f,38.25f);
            oldFull.player=oldPoint;
            store.Save("town-survival-v1.json",oldFull);
            type.GetField("survivalSaveChecked",flags).SetValue(controller,false);
            type.GetMethod("RefreshOverlay",flags).Invoke(controller,null);
            Click("ContinueSurvival");
            yield return WaitForCrossing();
            run=(ScavengeRun)field.GetValue(controller);
            Assert.AreEqual(1,run.Survival.layoutVersion);
            Assert.IsNull(run.FullTown.Shop);
            Assert.AreEqual(oldPoint,run.Player);
            Assert.IsTrue(run.FullTown.Walkable(oldPoint));
            type.GetMethod("LeaveScavenge",flags).Invoke(controller,null);
            yield return null;
            var legacy=new ScavengeRun(41,true,ScavengeSurvival.Create(41));
            store.Save("town-survival-v1.json",legacy.CaptureSurvival());
            type.GetField("survivalSaveChecked",flags).SetValue(controller,false);
            type.GetMethod("RefreshOverlay",flags).Invoke(controller,null);
            Click("ContinueSurvival");
            yield return WaitForCrossing();
            run=(ScavengeRun)field.GetValue(controller);
            Assert.IsNull(run.FullTown);
            Assert.AreEqual(0,run.Survival.layoutVersion);
            Assert.AreEqual(ScavengeTown.Start,run.Player);
            Assert.NotNull(GameObject.Find("河谷镇周边外景"));
            run.Survival.elapsed=ScavengeSurvival.Goal;
            player.SetValue(run,run.ExitPoint);
            run.Tick(.01f,Vector2.zero);
            Assert.IsTrue(run.SurvivalExit());
            type.GetMethod("StartFreshSurvivalFromResult",flags).Invoke(controller,null);
            yield return WaitForCrossing();
            run=(ScavengeRun)field.GetValue(controller);
            Assert.NotNull(run.FullTown);
            Assert.AreEqual(2,run.Survival.layoutVersion);
            Assert.IsEmpty(run.Survival.pack);
            yield return new ExitPlayMode();
        }

        // 领域输入逐帧步行，避免用传送替代家具间通路验收。
        private IEnumerator WalkShopPath(PrototypeController controller,ScavengeRun run,string batch,string name,params Vector2[] targets)
        {
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            foreach(var target in targets)
            {
                Assert.IsTrue(run.FullTown.Walkable(target),"目标占地不可通行 "+target);
                for(int step=0;step<350 && Vector2.Distance(run.Player,target)>.16f;step++)
                {
                    run.Tick(.05f,(target-run.Player).normalized);
                    if(step%8==0)yield return null;
                }
                Assert.That(Vector2.Distance(run.Player,target),Is.LessThan(.25f),"店内通路被挡 "+target+" 实际 "+run.Player);
            }
            typeof(PrototypeController).GetMethod("SyncScavenge",flags).Invoke(controller,new object[]{true});
            if(name=="shop-checkout")
            {
                // 受控镜头穿过货架验证整组隐藏与恢复，随后还原正常跟随镜头。
                var shelf=GameObject.Find("商店Shelf 0").GetComponentsInChildren<Renderer>();
                var camera=(Camera)typeof(PrototypeController).GetField("gameCamera",flags).GetValue(controller);
                var torso=(Vector3)typeof(PrototypeController).GetMethod("SurvivalPosition",flags).Invoke(controller,new object[]{run.Player,.95f});
                var bounds=shelf[0].bounds;foreach(var part in shelf)bounds.Encapsulate(part.bounds);
                camera.transform.position=torso+(bounds.center-torso)*2;
                typeof(PrototypeController).GetMethod("UpdateScavengeOccluders",flags).Invoke(controller,null);
                foreach(var part in shelf)Assert.IsTrue(part.forceRenderingOff,"挡住玩家的货架必须整组隐藏");
                camera.transform.position=torso+Vector3.up*15;
                typeof(PrototypeController).GetMethod("UpdateScavengeOccluders",flags).Invoke(controller,null);
                foreach(var part in shelf)Assert.IsFalse(part.forceRenderingOff,"离开视线的货架必须恢复");
                typeof(PrototypeController).GetMethod("SyncScavenge",flags).Invoke(controller,new object[]{true});
            }
            yield return CaptureSurvivalEvidence(name,1920,1080,batch);
        }

        [UnityTest]
        public IEnumerator RiverValleyStandalonePreviewRetainsItsWalkerAndCamera()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/RiverValleyTown.unity");
            yield return new EnterPlayMode();
            yield return null;
            var preview = Object.FindObjectOfType<OutpostLoop.RiverValley.RiverValleyTownPreview>();
            Assert.NotNull(preview);
            Assert.NotNull(GameObject.Find("外景步行角色"));
            Assert.NotNull(GameObject.Find("河谷镇斜俯视镜头"));
            Assert.That(preview.GetComponentsInChildren<Collider>().Length, Is.GreaterThan(200));
            var before = preview.WalkerPosition;
            for (int step = 0; step < 20; step++) preview.Step(Vector2.up, 1.8f, .05f);
            Assert.That(Vector3.Distance(before, preview.WalkerPosition), Is.GreaterThan(.5f));
            yield return new ExitPlayMode();
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
        }

        [UnityTest]
        public IEnumerator BootSurvivalOpensTownWithIndependentSurvivalClock()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            var controller = Object.FindObjectOfType<PrototypeController>();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var root = new System.IO.DirectoryInfo(Application.dataPath);
            while (root != null && !System.IO.File.Exists(System.IO.Path.Combine(root.FullName, "AGENTS.md")))
                root = root.Parent;
            Assert.NotNull(root);
            var saveDirectory = System.IO.Path.Combine(root.FullName, "Backup", "Artifacts", "FirstSurvival-20261112",
                "BootSave-" + System.Guid.NewGuid().ToString("N"));
            var type = typeof(PrototypeController);
            type.GetField("survivalStore", flags).SetValue(controller, new Game.JsonFileStore<SurvivalSave>(saveDirectory));
            type.GetField("survivalSaveChecked", flags).SetValue(controller, false);
            type.GetMethod("RefreshOverlay", flags).Invoke(controller, null);
            Assert.IsNull(GameObject.Find("CosmicSpace"), "首次回归前保持普通标题页");
            Assert.IsNull(GameObject.Find("TrialDestinations"), "其他试玩不应遮住默认空间菜单");
            Click("StartSurvival");
            Assert.IsTrue(controller.IsCrossing);
            Assert.NotNull(GameObject.Find("WorldCrossing"));
            Click("StartSurvival");
            yield return WaitForCrossing();
            Assert.IsNull(GameObject.Find("WorldCrossing"));
            yield return null;
            var field = type.GetField("scavengeRun", flags);
            var poll = type.GetMethod("PollScavenge", flags);
            var run = (ScavengeRun)field.GetValue(controller);
            Assert.NotNull(run.Survival);
            Assert.That(run.Survival.elapsed, Is.LessThan(2));
            Assert.That(run.Survival.pack, Is.Empty);
            Assert.NotNull(GameObject.Find("现有房门"));
            Assert.NotNull(GameObject.Find("随身手电光束"));
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var settings = InputSystem.settings;
            var editorInput = settings.editorInputBehaviorInPlayMode;
            var background = settings.backgroundBehavior;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            try
            {
                var player = typeof(ScavengeRun).GetProperty(nameof(ScavengeRun.Player));
                player.SetValue(run, run.CachePoints[0]);
                DriveScavengeKey(controller, poll, keyboard, Key.E);
                Assert.AreEqual(0, run.OpenCache);
                run.CloseSearch();

                var door = run.RoomSet.Door(0).center;
                player.SetValue(run, door - Vector2.right);
                DriveScavengeKey(controller, poll, keyboard, Key.E);
                Assert.IsTrue(run.Survival.rooms[0].open);
                Assert.That(run.Survival.pack, Is.Empty);
                for (int room = 0; room < run.Survival.rooms.Length; room++)
                {
                    player.SetValue(run, run.RoomSet.Door(room).center - Vector2.right);
                    DriveScavengeKey(controller, poll, keyboard, Key.V);
                    Assert.IsTrue((bool)type.GetField("survivalBuildOpen", flags).GetValue(controller));
                    run.Survival.pack.Add(new SurvivalStack(SurvivalItem.Boards));
                    type.GetMethod("StartSurvivalAction", flags).Invoke(controller, new object[] { SurvivalAction.Fortify, -1 });
                    Assert.AreEqual(SurvivalAction.Fortify, run.Survival.action);
                    run.Survival.actionTime = .01f;
                    run.Tick(.05f, Vector2.zero);
                    Assert.IsTrue(run.Survival.rooms[room].fortified);
                    Assert.That(run.Survival.pack, Is.Empty);
                }
                run.Survival.elapsed = 1600;
                run.Survival.water = 43;
                DriveScavengeKey(controller, poll, keyboard, Key.Escape);
                double paused = run.Survival.elapsed;
                yield return null;
                Assert.AreEqual(paused, run.Survival.elapsed);
                type.GetMethod("SaveAndLeaveSurvival", flags).Invoke(controller, null);
                yield return null;
                Assert.IsNull(field.GetValue(controller));
                Click("ContinueSurvival");
            yield return WaitForCrossing();
                yield return null;
                var resumed = (ScavengeRun)field.GetValue(controller);
                Assert.NotNull(resumed);
                Assert.AreNotSame(run, resumed);
                Assert.That(resumed.Survival.elapsed, Is.InRange(paused, paused + 2));
                Assert.That(resumed.Survival.water, Is.InRange(42f, 43f));
                Assert.IsTrue(resumed.Survival.rooms[0].open);
                Assert.IsTrue(resumed.Survival.rooms[0].fortified);
                Assert.IsTrue(resumed.Survival.rooms[1].fortified);
                Assert.IsNull(resumed.Survival.weapon);
                Assert.IsNull(resumed.Survival.torch);
                resumed.Survival.elapsed = ScavengeSurvival.Goal;
                player.SetValue(resumed, resumed.ExitPoint);
                DriveScavengeKey(controller, poll, keyboard, Key.E);
                Assert.AreEqual(ScavengeOutcome.Extracted, resumed.Outcome);
                Assert.IsTrue(resumed.Survival.Ready);
                Assert.IsFalse(controller.HasSurvivalSave);
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                settings.editorInputBehaviorInPlayMode = editorInput;
                settings.backgroundBehavior = background;
            }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator BootWeaponCommandGrantsOnlyToAnActiveSurvivalPackWithSpace()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();

            var controller = Object.FindObjectOfType<PrototypeController>();
            var architecture = Game.Game.Interface;
            var commands = architecture.GetSystem<Game.DebugCommandSystem>();
            var logs = architecture.GetModel<Game.RuntimeLogModel>();
            var directory = System.IO.Path.Combine(Application.dataPath, "../../Backup/Artifacts/TestCommandWindow-20261120",
                System.Guid.NewGuid().ToString("N"));
            architecture.RegisterUtility<Game.ISettingsFileStore>(new Game.SettingsFileStore(directory));
            commands.ApplyPreferences(new Game.TestCommandPreferences());
            Game.RuntimeLogEntry Execute(string input)
            {
                commands.ExecuteAsync(input).GetAwaiter().GetResult();
                var entries = logs.Snapshot();
                return entries[entries.Count - 1];
            }

            Assert.That(Execute("#weapon.give crowbar").Type, Is.EqualTo(LogType.Warning));
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var controllerType = typeof(PrototypeController);
            controllerType.GetField("survivalStore", flags).SetValue(controller, new Game.JsonFileStore<SurvivalSave>(directory));
            controllerType.GetField("survivalSaveChecked", flags).SetValue(controller, false);
            controllerType.GetMethod("RefreshOverlay", flags).Invoke(controller, null);
            Click("StartSurvival");
            yield return WaitForCrossing();

            var run = (ScavengeRun)controllerType.GetField("scavengeRun", flags).GetValue(controller);
            var state = run.Survival;
            var launcher = GameObject.Find("TestCommandLauncher").GetComponent<PrototypeTestCommandButtonView>();
            yield return null;
            var position = commands.LauncherPosition;
            var rect = (RectTransform)launcher.transform;
            var start = RectTransformUtility.WorldToScreenPoint(null, rect.position);
            var end = new Vector2(Screen.width / 2f, Screen.height / 2f);
            launcher.TrackPointer(start, pressedThisFrame: true, held: true);
            launcher.TrackPointer(end, pressedThisFrame: false, held: true);
            launcher.TrackPointer(end, pressedThisFrame: false, held: false);
            Assert.That(commands.LauncherPosition, Is.Not.EqualTo(position));
            position = commands.LauncherPosition;
            yield return null;
            var moved = RectTransformUtility.WorldToScreenPoint(null, rect.position);
            launcher.TrackPointer(moved, pressedThisFrame: true, held: true);
            launcher.TrackPointer(moved, pressedThisFrame: false, held: false);
            var testWindow = Game.UIHelper.GetUICtrl<PrototypeTestCommandCtrl>();
            Assert.That(testWindow?.IsShow(), Is.True);
            double time = state.elapsed;
            yield return null;
            Assert.That(state.elapsed, Is.EqualTo(time), "测试窗口应暂停行动");
            Assert.That(commands.ExecuteCommand("#item.list").message, Does.Contain("pistol · 手枪"));
            Assert.That(commands.ExecuteCommand("#clone 急救包 0").success, Is.False);
            Assert.That(commands.ExecuteCommand("#clone 急救包 2").success, Is.True);
            Assert.That(state.pack.FindAll(item => item.item == SurvivalItem.Aid).Count, Is.EqualTo(2));
            Assert.That(commands.RecentCommands[0], Is.EqualTo("#clone 急救包 2"));
            var serialized = Newtonsoft.Json.JsonConvert.SerializeObject(new Game.SettingsData
            {
                TestCommands = commands.CopyPreferences()
            });
            commands.ApplyPreferences(Newtonsoft.Json.JsonConvert.DeserializeObject<Game.SettingsData>(serialized).TestCommands);
            Assert.That(commands.RecentCommands[0], Is.EqualTo("#clone 急救包 2"));
            Assert.That(commands.LauncherPosition, Is.EqualTo(position));
            testWindow.Hide();
            Assert.That(testWindow.IsShow(), Is.False);
            state.pack.Clear();
            Assert.That(Execute("#help").Message, Does.Contain("#weapon.give"));
            Assert.That(Execute("#weapon.give unknown").Type, Is.EqualTo(LogType.Warning));
            Assert.That(state.pack, Is.Empty);
            Assert.That(Execute("#weapon.give crowbar").Type, Is.EqualTo(LogType.Log));
            Assert.That(Execute("#weapon.give PISTOL").Type, Is.EqualTo(LogType.Log));
            Assert.That(state.pack[0].item, Is.EqualTo(SurvivalItem.Crowbar));
            Assert.That(state.pack[1].item, Is.EqualTo(SurvivalItem.Pistol));
            Assert.That(state.pack[1].rounds, Is.EqualTo(ScavengeSurvival.PistolMagazineCapacity));
            Assert.That(state.weapon, Is.Null);

            state.pack.Add(new SurvivalStack(SurvivalItem.Boards));
            state.pack.Add(new SurvivalStack(SurvivalItem.Boards));
            var fullPack = Execute("#weapon.give pistol");
            Assert.That(fullPack.Type, Is.EqualTo(LogType.Warning));
            Assert.That(state.pack.Count, Is.EqualTo(4));
            Assert.That(state.Used, Is.EqualTo(ScavengeSurvival.Capacity));
            Assert.That(state.Equip(1), Is.True);
            Assert.That(state.weapon.item, Is.EqualTo(SurvivalItem.Pistol));
            var partial = commands.ExecuteCommand("#clone ammo 8");
            Assert.That(partial.success, Is.True);
            Assert.That(partial.message, Does.Contain("剩余 2 件"));
            Assert.That(state.pack[state.Find(SurvivalItem.Ammo)].count, Is.EqualTo(6));
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator BootRainStaysOutsideRoomsWhenRoofsAreHidden()
        {
            const string batch="IndoorRain-20261119";
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();yield return WaitForMain();
            var controller=Object.FindObjectOfType<PrototypeController>();
            var type=typeof(PrototypeController);var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var directory=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../Backup/Artifacts/"+batch+"/Save-"+System.Guid.NewGuid().ToString("N")));
            type.GetField("survivalStore",flags).SetValue(controller,new Game.JsonFileStore<SurvivalSave>(directory));
            type.GetField("survivalSaveChecked",flags).SetValue(controller,false);
            type.GetMethod("RefreshOverlay",flags).Invoke(controller,null);
            Click("StartSurvival");yield return WaitForCrossing();
            // 固定雨天种子；不更改地图、敌人或存档规则。
            type.GetMethod("BeginScavenge",flags).Invoke(controller,new object[]{1});
            var run=(ScavengeRun)type.GetField("scavengeRun",flags).GetValue(controller);
            foreach(var patrol in run.Patrols)patrol.Health=0;
            var player=typeof(ScavengeRun).GetProperty(nameof(ScavengeRun.Player));
            var sync=type.GetMethod("SyncScavenge",flags);
            var covered=type.GetMethod("TownRainCovered",flags);
            var rain=(MeshRenderer)type.GetField("townRain",flags).GetValue(controller);
            var mesh=(Mesh)type.GetField("townRainMesh",flags).GetValue(controller);
            Assert.IsTrue(rain.enabled);
            for(int room=0;room<6;room++)
            {
                var area=run.FullTown.Interiors[room];
                var targets=new[]{new Vector2(area.xMin-1,area.center.y),new Vector2(area.xMin+1,area.center.y),area.center,new Vector2(area.xMin-1,area.center.y)};
                for(int phase=0;phase<targets.Length;phase++)
                {
                    player.SetValue(run,targets[phase]);sync.Invoke(controller,new object[]{true});
                    Assert.That(rain.transform.position.y,Is.EqualTo(run.FullTown.Height(run.Player)).Within(.001f));
                    var vertices=mesh.vertices;int hidden=0,visible=0,outside=0;
                    for(int i=0;i<vertices.Length;i+=2)
                    {
                        var point=run.Player+new Vector2(vertices[i].x,vertices[i].z);
                        bool under=(bool)covered.Invoke(controller,new object[]{point});
                        bool shown=(vertices[i+1]-vertices[i]).sqrMagnitude>.001f;
                        if(area.Contains(point))Assert.IsFalse(shown,"实际房间矩形内不应出现雨线");
                        if(under){hidden++;Assert.IsFalse(shown,"雨线穿入房间 "+room+" / "+point);}
                        else{outside++;if(shown)visible++;}
                    }
                    Assert.Greater(hidden,0,"测试必须实际覆盖屋顶投影");
                    if(phase==0 || phase==3)Assert.Greater(outside,0,"门外取景必须覆盖露天雨线");
                    if(outside>0)Assert.AreEqual(outside,visible,"门外的雨不能一起关闭");
                    Assert.IsTrue((bool)covered.Invoke(controller,new object[]{new Vector2(area.xMin-.05f,area.center.y)}),"屋檐不能漏雨");
                    if(room==0 && (phase==0 || phase==1))yield return CaptureSurvivalEvidence(phase==0?"rain-outside":"rain-inside",1920,1080,batch);
                }
            }
            // 原室内屋顶即使已被遮挡系统切掉，挡雨判定仍应保留。
            var shop=run.FullTown.Interiors[0];player.SetValue(run,shop.center);sync.Invoke(controller,new object[]{true});
            var root=(Transform)type.GetField("scavengeRoot",flags).GetValue(controller);
            int roofs=0;
            foreach(var renderer in root.GetComponentsInChildren<Renderer>())
                if(renderer.name=="屋顶"){renderer.forceRenderingOff=true;roofs++;}
            Assert.Greater(roofs,0,"应实际验证屋顶切除状态");
            type.GetMethod("UpdateTownWeather",flags).Invoke(controller,null);
            var cutVertices=mesh.vertices;
            for(int i=0;i<cutVertices.Length;i+=2)
            {
                var point=run.Player+new Vector2(cutVertices[i].x,cutVertices[i].z);
                if(shop.Contains(point))Assert.AreEqual(cutVertices[i],cutVertices[i+1],"隐藏屋顶仍应挡雨");
            }
            type.GetMethod("BeginScavenge",flags).Invoke(controller,new object[]{0});
            Assert.IsFalse(rain.enabled,"晴天不能出现雨线");
            // 旧小街区恢复仍采用其自身建筑占地。
            var legacy=new ScavengeRun(1,true,ScavengeSurvival.Create(1));
            type.GetField("scavengeRun",flags).SetValue(controller,legacy);
            Assert.IsTrue((bool)covered.Invoke(controller,new object[]{SurvivalRooms.Footprint(0).center}));
            Assert.IsFalse((bool)covered.Invoke(controller,new object[]{ScavengeTown.Start}));
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator BootRiverValleyLetsPlayersDiscoverLocalCluesFreely()
        {
            const string batch = "RiverValleyFreeExploration-20261119";
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            var controller = Object.FindObjectOfType<PrototypeController>();
            var type = typeof(PrototypeController);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var directory = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,
                "../../Backup/Artifacts/" + batch + "/Save-" + System.Guid.NewGuid().ToString("N")));
            type.GetField("survivalStore", flags).SetValue(controller, new Game.JsonFileStore<SurvivalSave>(directory));
            type.GetField("survivalSaveChecked", flags).SetValue(controller, false);
            type.GetMethod("RefreshOverlay", flags).Invoke(controller, null);

            Assert.IsNull(GameObject.Find("TrialsEntry"));
            Click("StartSurvival");
            yield return WaitForCrossing();
            var run = (ScavengeRun)type.GetField("scavengeRun", flags).GetValue(controller);
            var player = typeof(ScavengeRun).GetProperty(nameof(ScavengeRun.Player));
            var interact = type.GetMethod("InteractSurvival", flags);
            var context = type.GetMethod("SurvivalInteraction", flags);
            var sync = type.GetMethod("SyncScavenge", flags);
            foreach (var patrol in run.Patrols) patrol.Health = 0;
            Assert.IsFalse(run.CanTalkToClinicSurvivor);
            Assert.IsFalse(run.CanReadWarehouseTransferNote);

            // 定位至搜点来隔离发现逻辑，仍沿用正常搜索、读取容器和取物规则。
            player.SetValue(run, run.CachePoints[12]);
            interact.Invoke(controller, null);
            Assert.AreEqual(12, run.OpenCache);
            while (run.SearchRemaining > 0) run.Tick(.05f, Vector2.zero);
            var hospital = run.Survival.caches[12].items;
            int aid = hospital.FindIndex(item => item.item == SurvivalItem.Aid);
            Assert.GreaterOrEqual(aid, 0);
            Assert.IsTrue(ScavengeSurvival.Transfer(hospital, aid, run.Survival.pack, 6));
            Assert.IsFalse(run.Survival.Ready);
            Assert.IsFalse(run.MedicineLeadKnown);
            interact.Invoke(controller, null);

            int note = OutpostLoop.RiverValley.RiverValleyWorldGeometry.TransferNoteCache;
            player.SetValue(run, run.CachePoints[note] + Vector2.left);
            interact.Invoke(controller, null);
            Assert.AreEqual(note, run.OpenCache);
            Assert.IsFalse(run.CanReadWarehouseTransferNote);
            while (run.SearchRemaining > 0) run.Tick(.05f, Vector2.zero);
            Assert.IsTrue(run.CanReadWarehouseTransferNote);
            sync.Invoke(controller, new object[] { true });
            yield return CaptureSurvivalEvidence("warehouse-transfer-note", 1280, 720, batch);
            interact.Invoke(controller, null);
            Assert.IsFalse(run.CanReadWarehouseTransferNote);
            Assert.IsFalse(run.MedicineLeadKnown, "搜点记录不启用旧试玩任务");

            var survivor = GameObject.Find("诊所幸存者");
            Assert.NotNull(survivor);
            var meeting = run.FullTown.ClinicSurvivor + Vector2.left;
            Assert.IsTrue(run.FullTown.Interiors[2].Contains(meeting));
            Assert.IsTrue(run.FullTown.Walkable(meeting));
            Assert.IsTrue(run.FullTown.Walkable(run.FullTown.ClinicSurvivor));
            player.SetValue(run, meeting);
            Assert.AreEqual("和幸存者交谈", context.Invoke(controller, null));
            int feedback = run.SurvivalFeedbackVersion;
            interact.Invoke(controller, null);
            Assert.AreEqual(feedback + 1, run.SurvivalFeedbackVersion);
            StringAssert.Contains("医院", run.SurvivalFeedback);
            Assert.IsTrue(run.CanTalkToClinicSurvivor, "对话可重复查看");
            Assert.IsFalse(run.SurvivorSpoken, "现场对话不启用旧试玩任务");
            sync.Invoke(controller, new object[] { true });
            yield return CaptureSurvivalEvidence("clinic-conversation", 1280, 720, batch);

            Assert.IsTrue(run.Survival.pack.Exists(item => item.item == SurvivalItem.Aid));
            Assert.IsFalse(run.Survival.Ready);
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator BootRiverValleyContextFollowsActionProgressAndModals()
        {
            const string batch = "RiverValleyInteraction-20261120";
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            var controller = Object.FindObjectOfType<PrototypeController>();
            var type = typeof(PrototypeController);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../Backup/Artifacts/" + batch));
            var save = System.IO.Path.Combine(folder, "Save-" + System.Guid.NewGuid().ToString("N"));
            type.GetField("survivalStore", flags).SetValue(controller, new Game.JsonFileStore<SurvivalSave>(save));
            type.GetField("survivalSaveChecked", flags).SetValue(controller, false);
            type.GetMethod("RefreshOverlay", flags).Invoke(controller, null);
            Click("StartSurvival");
            yield return WaitForCrossing();

            var run = (ScavengeRun)type.GetField("scavengeRun", flags).GetValue(controller);
            foreach (var patrol in run.Patrols) patrol.Health = 0;
            var player = typeof(ScavengeRun).GetProperty(nameof(ScavengeRun.Player));
            var poll = type.GetMethod("PollSurvival", flags);
            var late = type.GetMethod("LateUpdate", flags);
            var sync = type.GetMethod("SyncScavenge", flags);
            var hudContains = type.GetMethod("SurvivalHudContains", flags);
            var language = Game.Game.Interface.GetModel<Game.LocalizationModel>();
            var originalLanguage = language.CurrentLanguageCode.Value;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                language.SetLanguageCode("zh-CN");
                player.SetValue(run, run.CachePoints[12] + Vector2.left);
                sync.Invoke(controller, new object[] { true });
                late.Invoke(controller, null);
                var canvas = GameObject.Find("RiverValleyContextCanvas");
                Assert.NotNull(canvas);
                var context = canvas.transform.Find("Interaction");
                var label = context.Find("Label").GetComponent<TMPro.TMP_Text>();
                var button = context.GetComponent<Button>();
                var progress = context.Find("Progress");
                var fill = (RectTransform)progress.Find("Fill");
                Canvas.ForceUpdateCanvases();
                var rect = (RectTransform)context;
                var cursor = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
                Assert.AreEqual("E  搜索", label.text);
                Assert.IsTrue((bool)hudContains.Invoke(controller, new object[] { cursor }));
                // 小地图图面和键位提示都拦截攻击，旧按钮区域交回场景输入。
                float hudScale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
                Assert.IsTrue((bool)hudContains.Invoke(controller, new object[]
                    { new Vector2(Screen.width - 95 * hudScale, Screen.height - 75 * hudScale) }));
                Assert.IsTrue((bool)hudContains.Invoke(controller, new object[]
                    { new Vector2(Screen.width - 40 * hudScale, Screen.height - 156 * hudScale) }));
                Assert.IsFalse((bool)hudContains.Invoke(controller, new object[]
                    { new Vector2(215 * hudScale, 40 * hudScale) }));
                yield return CaptureSurvivalEvidence("search-ready-zh", 1280, 720, batch);

                PressInventoryButton(button);
                Assert.AreEqual(12, run.OpenCache);
                run.Tick(.2f, Vector2.zero);
                late.Invoke(controller, null);
                Assert.IsTrue(progress.gameObject.activeSelf);
                Assert.IsFalse(button.interactable);
                Assert.That(fill.anchorMax.x, Is.EqualTo(1f / 3f).Within(.01f));
                StringAssert.StartsWith("搜索中", label.text);
                var inventory = GameObject.Find("RiverValleyInventoryCanvas");
                Assert.NotNull(inventory);
                DriveScavengeKey(controller, poll, keyboard, Key.M);
                late.Invoke(controller, null);
                Assert.IsFalse(context.gameObject.activeSelf);
                Assert.IsFalse(inventory.activeSelf);
                float remaining = run.SearchRemaining;
                yield return null;
                Assert.AreEqual(remaining, run.SearchRemaining);
                DriveScavengeKey(controller, poll, keyboard, Key.Escape);
                late.Invoke(controller, null);
                Assert.AreEqual(12, run.OpenCache);
                Assert.IsTrue(inventory.activeSelf);
                Assert.IsTrue(progress.gameObject.activeInHierarchy);
                yield return CaptureSurvivalEvidence("search-progress-zh", 1280, 720, batch);
                run.Tick(.65f, Vector2.zero);
                late.Invoke(controller, null);
                Assert.IsFalse(context.gameObject.activeSelf);
                Assert.IsFalse((bool)hudContains.Invoke(controller, new object[] { cursor }));
                DriveScavengeKey(controller, poll, keyboard, Key.E);
                Assert.AreEqual(-1, run.OpenCache);

                run.Survival.pack.Add(new SurvivalStack(SurvivalItem.Water));
                run.Survival.water = 50;
                type.GetMethod("StartSurvivalAction", flags).Invoke(controller, new object[] { SurvivalAction.Drink, 0 });
                run.Tick(.5f, Vector2.zero);
                language.SetLanguageCode("en");
                late.Invoke(controller, null);
                StringAssert.StartsWith("Drinking", label.text);
                Assert.That(fill.anchorMax.x, Is.EqualTo(.25f).Within(.01f));
                yield return CaptureSurvivalEvidence("action-progress-en", 1920, 1080, batch);
                DriveScavengeKey(controller, poll, keyboard, Key.Escape);
                Assert.AreEqual(SurvivalAction.None, run.Survival.action);

                player.SetValue(run, run.RoomSet.Door(0).center);
                Assert.AreEqual(0, run.NearbySurvivalDoor);
                late.Invoke(controller, null);
                Assert.AreEqual("E  Open door", label.text);
                DriveScavengeKey(controller, poll, keyboard, Key.V);
                late.Invoke(controller, null);
                Assert.AreEqual("E  Close", label.text);
                Assert.IsFalse(inventory.activeSelf);
                PressInventoryButton(button);
                Assert.IsFalse((bool)type.GetField("survivalBuildOpen", flags).GetValue(controller));

                Vector3 left = Quaternion.Euler(0, run.VehicleHeading, 0) * Vector3.left;
                player.SetValue(run, run.VehiclePosition + new Vector2(left.x, left.z) * 1.55f);
                late.Invoke(controller, null);
                Assert.AreEqual("E  Enter vehicle", label.text);
                DriveScavengeKey(controller, poll, keyboard, Key.E);
                Assert.IsTrue(run.Driving);
                late.Invoke(controller, null);
                Assert.AreEqual("E  Exit vehicle", label.text);
                DriveScavengeKey(controller, poll, keyboard, Key.M);
                late.Invoke(controller, null);
                Assert.IsFalse(context.gameObject.activeSelf);
                DriveScavengeKey(controller, poll, keyboard, Key.M);
                Assert.IsTrue(run.Driving);
                late.Invoke(controller, null);
                Assert.AreEqual("E  Exit vehicle", label.text);
                yield return CaptureSurvivalEvidence("driving-en", 1920, 1080, batch);
                DriveScavengeKey(controller, poll, keyboard, Key.E);
                Assert.IsFalse(run.Driving);

                player.SetValue(run, run.ExitPoint + Vector2.left * 2);
                run.Tick(.05f, Vector2.zero);
                Assert.IsTrue(run.ExitKnown);
                Assert.IsFalse(run.AtExit);
                late.Invoke(controller, null);
                Assert.IsFalse(context.gameObject.activeSelf, "发现撤离点后仍须走到交互范围内");
                player.SetValue(run, run.ExitPoint);
                sync.Invoke(controller, new object[] { true });
                late.Invoke(controller, null);
                Assert.AreEqual("E  Extract early", label.text);
                DriveScavengeKey(controller, poll, keyboard, Key.Escape);
                late.Invoke(controller, null);
                Assert.IsFalse(context.gameObject.activeSelf);
                DriveScavengeKey(controller, poll, keyboard, Key.Escape);
                late.Invoke(controller, null);
                PressInventoryButton(button);
                late.Invoke(controller, null);
                Assert.IsFalse(context.gameObject.activeSelf);
                Assert.IsTrue((bool)type.GetField("survivalConfirmExit", flags).GetValue(controller));
                yield return CaptureSurvivalEvidence("early-extraction-confirm-en", 1280, 720, batch);
                DriveScavengeKey(controller, poll, keyboard, Key.Escape);
                Assert.AreEqual(ScavengeOutcome.Exploring, run.Outcome);
                Assert.IsFalse((bool)type.GetField("scavengePaused", flags).GetValue(controller));

                run.Survival.elapsed = ScavengeSurvival.Goal;
                late.Invoke(controller, null);
                Assert.AreEqual("E  Extract", label.text);
                yield return CaptureSurvivalEvidence("extraction-ready-en", 1920, 1080, batch);
            }
            finally
            {
                language.SetLanguageCode(originalLanguage);
                InputSystem.RemoveDevice(keyboard);
            }
            yield return new ExitPlayMode();
        }


        [UnityTest]
        public IEnumerator BootRiverValleyStatusShowsNeedsBesideInventoryAndYieldsToMap()
        {
            const string batch = "RiverValleyPackManagement-20261120";
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            var controller = Object.FindObjectOfType<PrototypeController>();
            var type = typeof(PrototypeController);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../Backup/Artifacts/" + batch));
            var save = System.IO.Path.Combine(folder, "Save-" + System.Guid.NewGuid().ToString("N"));
            type.GetField("survivalStore", flags).SetValue(controller, new Game.JsonFileStore<SurvivalSave>(save));
            type.GetField("survivalSaveChecked", flags).SetValue(controller, false);
            type.GetMethod("RefreshOverlay", flags).Invoke(controller, null);
            Click("StartSurvival");
            yield return WaitForCrossing();
            var run = (ScavengeRun)type.GetField("scavengeRun", flags).GetValue(controller);
            var language = Game.Game.Interface.GetModel<Game.LocalizationModel>();
            var originalLanguage = language.CurrentLanguageCode.Value;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                language.SetLanguageCode("zh-CN");
                foreach (var patrol in run.Patrols) patrol.Health = 0;
                yield return null;
                yield return null;
                var view = GameObject.Find("RiverValleyStatusCanvas").GetComponent<SurvivalStatusView>();
                Assert.AreEqual(Image.FillMethod.Radial360, view.WaterFill.fillMethod);
                Assert.That(view.HealthFill.rectTransform.anchorMax.x, Is.EqualTo(1f).Within(.01f));
                Assert.That(view.WaterFill.fillAmount, Is.EqualTo(.8f).Within(.01f));
                Assert.AreEqual("", view.WaterDetail.text);
                Assert.AreEqual("", view.FoodDetail.text);
                Assert.AreEqual("", view.EnergyDetail.text);
                Assert.AreEqual("", view.HealthWarning.text);
                yield return CaptureSurvivalEvidence("exploring-zh", 1280, 720, batch);

                run.Survival.health = 68f;
                run.Survival.water = 17f;
                run.Survival.food = 51f;
                run.Survival.energy = 22f;
                run.Survival.bleeding = true;
                yield return null;
                yield return null;
                Assert.That(view.HealthFill.rectTransform.anchorMax.x, Is.EqualTo(.68f).Within(.01f));
                Assert.That(view.EnergyFill.fillAmount, Is.EqualTo(.22f).Within(.01f));
                Assert.AreEqual("68", view.HealthValue.text);
                view.HealthValue.ForceMeshUpdate();
                Assert.IsFalse(view.HealthValue.isTextTruncated);
                Assert.That(view.HealthValue.textInfo.characterCount, Is.EqualTo(2));
                Assert.IsTrue(view.HealthValue.textInfo.characterInfo[0].isVisible);
                Assert.AreEqual("出血", view.HealthWarning.text);
                Assert.AreEqual("口渴", view.WaterDetail.text);
                Assert.AreEqual("", view.FoodDetail.text);
                Assert.AreEqual("疲劳", view.EnergyDetail.text);
                yield return CaptureSurvivalEvidence("low-needs-zh", 1920, 1080, batch);

                PressInventoryButton(view.Pack);
                yield return null;
                yield return null;
                var inventory = GameObject.Find("RiverValleyInventoryCanvas").GetComponent<SurvivalInventoryView>();
                Assert.IsTrue(inventory.gameObject.activeSelf);
                Assert.IsFalse(view.gameObject.activeSelf);
                var sheet = inventory.transform.Find("Management/Sheet");
                PressInventoryButton(sheet.Find("TabStatus").GetComponent<Button>());
                yield return null;
                Assert.IsNotNull(sheet.Find("PageStatus"));
                StringAssert.Contains("生命 68", string.Join("|", System.Array.ConvertAll(
                    sheet.Find("PageStatus").GetComponentsInChildren<TMPro.TMP_Text>(), label => label.text)));
                yield return CaptureSurvivalEvidence("status-zh", 1280, 720, batch);

                language.SetLanguageCode("en");
                yield return null;
                yield return null;
                var labels = sheet.Find("PageStatus").GetComponentsInChildren<TMPro.TMP_Text>();
                StringAssert.Contains("Water 17", string.Join("|", System.Array.ConvertAll(labels, label => label.text)));
                foreach (var label in labels)
                {
                    label.ForceMeshUpdate();
                    Assert.IsFalse(label.isTextOverflowing, label.text);
                    Assert.IsFalse(label.isTextTruncated, label.text);
                }
                yield return CaptureSurvivalEvidence("status-en", 1920, 1080, batch);

                var poll = type.GetMethod("PollSurvival", flags);
                DriveScavengeKey(controller, poll, keyboard, Key.M);
                yield return null;
                yield return null;
                Assert.IsFalse(view.gameObject.activeSelf);
                Assert.IsFalse(inventory.gameObject.activeSelf);
                DriveScavengeKey(controller, poll, keyboard, Key.Escape);
                yield return null;
                yield return null;
                Assert.IsTrue(inventory.gameObject.activeSelf);
                Assert.IsTrue(sheet.Find("PageStatus").gameObject.activeSelf);
                DriveScavengeKey(controller, poll, keyboard, Key.Escape);
                yield return null;
                yield return null;
                Assert.IsTrue(view.gameObject.activeSelf);
                Assert.AreSame(view, GameObject.Find("RiverValleyStatusCanvas").GetComponent<SurvivalStatusView>());
            }
            finally
            {
                language.SetLanguageCode(originalLanguage);
                InputSystem.RemoveDevice(keyboard);
            }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator BootRiverValleyInventoryUsesLocalizedTmpAndTransfersItems()
        {
            const string batch = "RiverValleyPackManagement-20261120";
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            var controller = Object.FindObjectOfType<PrototypeController>();
            var type = typeof(PrototypeController);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../Backup/Artifacts/" + batch));
            System.IO.Directory.CreateDirectory(folder);
            var save = System.IO.Path.Combine(folder, "Save-" + System.Guid.NewGuid().ToString("N"));
            type.GetField("survivalStore", flags).SetValue(controller, new Game.JsonFileStore<SurvivalSave>(save));
            type.GetField("survivalSaveChecked", flags).SetValue(controller, false);
            type.GetMethod("RefreshOverlay", flags).Invoke(controller, null);
            Click("StartSurvival");
            yield return WaitForCrossing();
            var run = (ScavengeRun)type.GetField("scavengeRun", flags).GetValue(controller);
            var language = Game.Game.Interface.GetModel<Game.LocalizationModel>();
            var originalLanguage = language.CurrentLanguageCode.Value;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                language.SetLanguageCode("zh-CN");
                foreach (var patrol in run.Patrols) patrol.Health = 0;
                int hospital = 12;
                var player = typeof(ScavengeRun).GetProperty(nameof(ScavengeRun.Player));
                player.SetValue(run, run.CachePoints[hospital] + Vector2.left);
                var poll = type.GetMethod("PollSurvival", flags);
                DriveScavengeKey(controller, poll, keyboard, Key.E);
                Assert.AreEqual(hospital, run.OpenCache);
                Assert.Greater(run.SearchRemaining, 0);
                DriveScavengeKey(controller, poll, keyboard, Key.Tab);
                Assert.AreEqual(-1, run.OpenCache);
                Assert.AreEqual(0, run.SearchRemaining);
                DriveScavengeKey(controller, poll, keyboard, Key.Escape);
                DriveScavengeKey(controller, poll, keyboard, Key.E);
                Assert.AreEqual(hospital, run.OpenCache);
                while (run.SearchRemaining > 0) run.Tick(.05f, Vector2.zero);
                type.GetMethod("SyncScavenge", flags).Invoke(controller, new object[] { true });
                var cache = run.Survival.caches[hospital].items;
                Assert.IsTrue(cache.Exists(item => item.item == SurvivalItem.Aid));
                run.Survival.pack.Clear();
                run.Survival.pack.Add(new SurvivalStack(SurvivalItem.Crowbar));
                run.Survival.pack.Add(new SurvivalStack(SurvivalItem.Water));
                yield return null;
                yield return null;

                var canvas = GameObject.Find("RiverValleyInventoryCanvas");
                Assert.NotNull(canvas.GetComponent<GraphicRaycaster>());
                var view = canvas.GetComponent<SurvivalInventoryView>();
                Assert.NotNull(view);
                Assert.NotNull(view.SourceRow);
                Assert.NotNull(view.PackRow);
                var packHeading = view.Panel.Find("PackHeading").GetComponent<TMPro.TextMeshProUGUI>();
                Assert.NotNull(packHeading.GetComponent<Game.LocalizedTmpText>());
                Assert.AreEqual(Game.Loc.T("InventoryPack"), packHeading.text);
                var title = view.Title;
                Assert.AreEqual(run.CacheNames[hospital], title.text);
                Assert.IsTrue(title.font.HasCharacters(title.text, out uint[] missing, true, true),
                    missing == null ? "" : string.Join(",", missing));
                var take = canvas.transform.Find("InventoryPanel/InventorySource/Rows/Aid/Take").GetComponent<Button>();
                PressInventoryButton(take);
                Assert.IsTrue(run.Survival.pack.Exists(item => item.item == SurvivalItem.Aid));
                yield return null;
                yield return null;
                yield return CaptureSurvivalEvidence("inventory-zh", 1920, 1080, batch);

                language.SetLanguageCode("en");
                yield return null;
                yield return null;
                Assert.AreSame(title, view.Title);
                Assert.AreSame(packHeading, view.Panel.Find("PackHeading").GetComponent<TMPro.TextMeshProUGUI>());
                Assert.AreEqual(Game.Loc.T("InventoryPack"), packHeading.text);
                Assert.AreEqual("Hospital entrance supply cabinet", title.text);
                title.ForceMeshUpdate();
                Assert.IsFalse(title.isTextTruncated);
                Assert.IsFalse(title.isTextOverflowing);
                yield return CaptureSurvivalEvidence("inventory-en", 1920, 1080, batch);

                PressInventoryButton(canvas.transform.Find("InventoryPanel/UseEquipment").GetComponent<Button>());
                yield return null;
                yield return null;
                Assert.AreEqual(-1, run.OpenCache);
                Assert.AreEqual(0, run.SearchRemaining);
                Assert.IsFalse(view.Panel.gameObject.activeSelf);
                var sheet = canvas.transform.Find("Management/Sheet");
                var portrait = sheet.Find("PagePack/Surface/CharacterPreview").GetComponent<RawImage>();
                Assert.IsNotNull(portrait.texture);
                var statusCanvas = (SurvivalStatusView)type.GetField("survivalStatusView", flags).GetValue(controller);
                Assert.IsFalse(statusCanvas.gameObject.activeSelf);

                Button Menu(string name)
                {
                    foreach (var button in sheet.GetComponentsInChildren<Button>())
                        if (button.name == name) return button;
                    Assert.Fail("管理页缺少按钮 " + name);
                    return null;
                }

                PressInventoryButton(Menu("Slot0"));
                yield return null;
                yield return CaptureSurvivalEvidence("backpack-en", 1920, 1080, batch);
                PressInventoryButton(Menu("UseSelected"));
                Assert.AreEqual(SurvivalItem.Crowbar, run.Survival.weapon.item);
                run.Survival.pack.Add(new SurvivalStack(SurvivalItem.Pistol) { rounds = 2 });
                PressInventoryButton(Menu("TabGear"));
                yield return null;
                PressInventoryButton(Menu("Slot0"));
                yield return null;
                PressInventoryButton(Menu("UseSelected"));
                Assert.AreEqual(SurvivalItem.Pistol, run.Survival.weapon.item);
                Assert.IsTrue(run.Survival.pack.Exists(item => item.item == SurvivalItem.Crowbar));
                yield return null;
                yield return CaptureSurvivalEvidence("equipment-en", 1920, 1080, batch);
                PressInventoryButton(Menu("TabMap"));
                yield return null;
                Assert.IsFalse(canvas.activeSelf);
                Assert.IsTrue((bool)type.GetField("survivalRegionMapOpen", flags).GetValue(controller));
                yield return CaptureSurvivalEvidence("map-en", 1280, 720, batch);
                DriveScavengeKey(controller, poll, keyboard, Key.Escape);
                yield return null;
                Assert.IsTrue(sheet.Find("PageGear").gameObject.activeSelf);
                PressInventoryButton(Menu("Stow"));
                Assert.IsNull(run.Survival.weapon);
                language.SetLanguageCode("zh-CN");
                PressInventoryButton(Menu("TabTasks"));
                yield return null;
                yield return CaptureSurvivalEvidence("tasks-zh", 1280, 720, batch);
                PressInventoryButton(Menu("TabJournal"));
                yield return null;
                var journal = sheet.Find("PageJournal").GetComponentsInChildren<TMPro.TMP_Text>();
                Assert.IsTrue(System.Array.Exists(journal, label => label.text == "暂无行动记录"));
                Assert.IsFalse(System.Array.Exists(journal, label => label.text.Contains("空间")));
                yield return CaptureSurvivalEvidence("journal-zh", 1280, 720, batch);

                PressInventoryButton(Menu("TabPack"));
                yield return null;
                int crowbarSlot = run.Survival.pack.FindIndex(item => item.item == SurvivalItem.Crowbar);
                PressInventoryButton(Menu("Slot" + crowbarSlot));
                yield return null;
                yield return CaptureSurvivalEvidence("backpack-zh", 1280, 720, batch);
                PressInventoryButton(Menu("DropSelected"));
                yield return null;
                Assert.AreEqual(1, run.Survival.drops.Count);
                var ground = run.Survival.drops[0];
                Assert.AreEqual(SurvivalItem.Crowbar, ground.items[0].item);
                DriveScavengeKey(controller, poll, keyboard, Key.Escape);
                yield return null;
                Assert.IsFalse(canvas.activeSelf);
                DriveScavengeKey(controller, poll, keyboard, Key.E);
                yield return null;
                Assert.IsTrue(view.Panel.gameObject.activeSelf);
                Assert.IsTrue(statusCanvas.gameObject.activeSelf);
                Assert.AreEqual(Game.Loc.T("InventoryGround"), view.Title.text);
                yield return CaptureSurvivalEvidence("ground-zh", 1280, 720, batch);
                PressInventoryButton(view.Source.content.Find("Crowbar/Take").GetComponent<Button>());
                Assert.IsEmpty(ground.items);
                DriveScavengeKey(controller, poll, keyboard, Key.Tab);
                yield return null;
                Assert.IsTrue(sheet.Find("PagePack").gameObject.activeSelf);
                run.Survival.water = 50;
                int waterSlot = run.Survival.pack.FindIndex(item => item.item == SurvivalItem.Water);
                PressInventoryButton(Menu("Slot" + waterSlot));
                yield return null;
                PressInventoryButton(Menu("UseSelected"));
                Assert.AreEqual(SurvivalAction.Drink, run.Survival.action);
                run.Tick(2.1f, Vector2.zero);
                Assert.That(run.Survival.water, Is.GreaterThan(80f));
                language.SetLanguageCode("en");
                yield return null;
                Assert.IsFalse(canvas.activeSelf);

                player.SetValue(run, run.RoomSet.Cabinet(1));
                var storage = run.Survival.rooms[1].storage;
                storage.items.Add(new SurvivalStack(SurvivalItem.Water));
                storage.items.Add(new SurvivalStack(SurvivalItem.Torch));
                while (storage.items.Count < 12) storage.items.Add(new SurvivalStack(SurvivalItem.Battery));
                run.Survival.pack.Add(new SurvivalStack(SurvivalItem.Water));
                while (run.Survival.Used < ScavengeSurvival.Capacity)
                    run.Survival.pack.Add(new SurvivalStack(SurvivalItem.Food));
                DriveScavengeKey(controller, poll, keyboard, Key.E);
                yield return null;
                yield return null;
                Assert.IsTrue(canvas.activeSelf);
                var panel = canvas.transform.Find("InventoryPanel");
                var sourceScroll = panel.Find("InventorySource").GetComponent<ScrollRect>();
                var packScroll = panel.Find("InventoryPack").GetComponent<ScrollRect>();
                Assert.IsTrue(sourceScroll.verticalScrollbar.gameObject.activeSelf);
                Assert.IsFalse(packScroll.verticalScrollbar.gameObject.activeSelf);
                Assert.IsFalse(sourceScroll.content.Find("Torch/Take").GetComponent<Button>().interactable);
                Assert.IsTrue(sourceScroll.content.Find("Water/Take").GetComponent<Button>().interactable);
                PressInventoryButton(sourceScroll.content.Find("Water/Take").GetComponent<Button>());
                Assert.AreEqual(2, run.Survival.pack.Find(item => item.item == SurvivalItem.Water).count);
                Assert.AreEqual(ScavengeSurvival.Capacity, run.Survival.Used);
                yield return null;
                PressInventoryButton(packScroll.content.Find("Water/Store").GetComponent<Button>());
                Assert.AreEqual(1, run.Survival.pack.Find(item => item.item == SurvivalItem.Water).count);
                Assert.AreEqual(1, storage.items.Find(item => item.item == SurvivalItem.Water).count);
                yield return null;
                var packOrigin = packScroll.content.anchoredPosition;
                sourceScroll.OnScroll(new PointerEventData(EventSystem.current) { scrollDelta = Vector2.down * 4 });
                yield return null;
                Assert.Greater(sourceScroll.content.anchoredPosition.y, 0);
                Assert.AreEqual(packOrigin, packScroll.content.anchoredPosition);
                var scrollbar = sourceScroll.verticalScrollbar;
                var track = (RectTransform)scrollbar.transform;
                var thumb = RectTransformUtility.CalculateRelativeRectTransformBounds(track, scrollbar.handleRect);
                Assert.That(thumb.min.y, Is.GreaterThanOrEqualTo(track.rect.yMin - .05f));
                Assert.That(thumb.max.y, Is.LessThanOrEqualTo(track.rect.yMax + .05f));
                yield return CaptureSurvivalEvidence("storage-en", 1920, 1080, batch);
                DriveScavengeKey(controller, poll, keyboard, Key.M);
                yield return null;
                yield return null;
                Assert.IsFalse(canvas.activeSelf);
            }
            finally
            {
                language.SetLanguageCode(originalLanguage);
                InputSystem.RemoveDevice(keyboard);
            }
            yield return new ExitPlayMode();
        }

        /// <summary>用 Canvas Raycaster 为指定按钮派发指针点击，验证被滚动遮罩后的真实命中。</summary>
        private static void PressInventoryButton(Button button)
        {
            Assert.IsTrue(button.interactable);
            Canvas.ForceUpdateCanvases();
            var rect = (RectTransform)button.transform;
            var point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            var pointer = new PointerEventData(EventSystem.current) { position = point, button = PointerEventData.InputButton.Left };
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, results);
            Assert.IsNotEmpty(results);
            Assert.AreSame(button.gameObject, ExecuteEvents.GetEventHandler<IPointerClickHandler>(results[0].gameObject));
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }

        [UnityTest]
        public IEnumerator LegacyScavengeTownStartsWithWeatherAndLetsThePlayerSpeak()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            var controller = Object.FindObjectOfType<PrototypeController>();
            var type = typeof(PrototypeController);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            type.GetMethod("StartScavengeTown", flags).Invoke(controller, null);
            var field = type.GetField("scavengeRun", flags);
            var poll = type.GetMethod("PollScavenge", flags);
            type.GetMethod("BeginScavenge", flags).Invoke(controller, new object[] { 0 });
            var rain = GameObject.Find("小雨").GetComponent<MeshRenderer>();
            Assert.IsFalse(rain.enabled);
            type.GetMethod("BeginScavenge", flags).Invoke(controller, new object[] { 1 });
            var run = (ScavengeRun)field.GetValue(controller);
            Assert.IsTrue(run.IsTown);
            Assert.IsTrue(run.IsRain);
            Assert.IsTrue(rain.enabled);
            Assert.AreEqual(0, run.Count);
            Assert.NotNull(GameObject.Find("本地幸存者"));
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var settings = InputSystem.settings;
            var editorInput = settings.editorInputBehaviorInPlayMode;
            var background = settings.backgroundBehavior;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            try
            {
                typeof(ScavengeRun).GetProperty(nameof(ScavengeRun.Player)).SetValue(run, new Vector2(-30, -33));
                DriveScavengeKey(controller, poll, keyboard, Key.E);
                Assert.IsTrue(run.SurvivorSpoken);
                Assert.IsTrue(run.MedicineLeadKnown);
                typeof(ScavengeRun).GetProperty(nameof(ScavengeRun.Player)).SetValue(run, new Vector2(-70, -40));
                yield return null;
                Assert.IsTrue(run.Interact());
                Assert.AreEqual(ScavengeOutcome.Extracted, run.Outcome);
                DriveScavengeKey(controller, poll, keyboard, Key.Enter);
                var next = (ScavengeRun)field.GetValue(controller);
                Assert.AreNotSame(run, next);
                Assert.IsTrue(next.IsTown);
                Assert.IsFalse(next.MedicineLeadKnown);
                Assert.AreEqual(0, next.Count);
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                settings.editorInputBehaviorInPlayMode = editorInput;
                settings.backgroundBehavior = background;
            }
            yield return new ExitPlayMode();
        }

        /// <summary>批处理编辑器没有 Game 视图焦点，输入注入与局内轮询在同一步执行。</summary>
        private static void DriveScavengeKey(PrototypeController controller, System.Reflection.MethodInfo poll,
            Keyboard keyboard, Key key)
        {
            InputSystem.Update();
            InputState.Change(keyboard, new KeyboardState(), InputUpdateType.Dynamic);
            InputSystem.Update();
            InputState.Change(keyboard, new KeyboardState(key), InputUpdateType.Dynamic);
            keyboard.MakeCurrent();
            Assert.IsTrue(keyboard[key].wasPressedThisFrame, $"注入键 {key} 未进入当前输入步");
            poll.Invoke(controller, null);
        }

        [UnityTest]
        public IEnumerator BootScavengeCameraFollowsInputAndClearsVisualOcclusion()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            var controller = Object.FindObjectOfType<PrototypeController>();
            var camera = GameObject.Find("PreviewCamera").GetComponent<Camera>();
            float originalFov = camera.fieldOfView;
            var originalColor = camera.backgroundColor;
            var originalRotation = camera.transform.rotation;
            StartLegacyScavenge();
            yield return WaitForCrossing();
            yield return null;
            var type = typeof(PrototypeController);
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var run = (ScavengeRun)type.GetField("scavengeRun", flags).GetValue(controller);
            var sync = type.GetMethod("SyncScavenge", flags);
            var playerProperty = typeof(ScavengeRun).GetProperty(nameof(ScavengeRun.Player));
            Assert.IsFalse(camera.orthographic);
            Assert.That(camera.transform.eulerAngles.x, Is.EqualTo(43).Within(.1f));

            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                var previous = run.Player;
                var cameraStart = camera.transform.position;
                float screenX = camera.WorldToScreenPoint(new Vector3(previous.x, 0, previous.y)).x;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
                for (int frame = 0; frame < 120 && Vector2.Distance(run.Player, previous) < .6f; frame++) yield return null;
                Assert.That(Vector2.Distance(run.Player, previous), Is.GreaterThan(.55f));
                Assert.That(Vector3.Distance(camera.transform.position, cameraStart), Is.GreaterThan(.02f));
                Assert.That(camera.WorldToScreenPoint(new Vector3(run.Player.x, 0, run.Player.y)).x, Is.EqualTo(screenX).Within(1f));
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null;

                var point = run.Player + Vector2.right * 1.7f;
                var screen = (Vector2)camera.WorldToScreenPoint(new Vector3(point.x, 0, point.y));
                var args = new object[] { screen, Vector2.zero };
                Assert.IsTrue((bool)type.GetMethod("TryProjectBattleGround", flags).Invoke(controller, args));
                Assert.That(Vector2.Distance(point, (Vector2)args[1]), Is.LessThan(.01f));

                var wall = System.Array.Find(GameObject.Find("ScavengeTrial").GetComponentsInChildren<Renderer>(),
                    renderer => renderer.name == "矮墙" && Mathf.Abs(renderer.bounds.center.z + 4.95f) < .05f);
                Assert.NotNull(wall);
                // 角色离墙较远，维修间仍在交互距离内，镜头应切开挡住搜点的墙。
                playerProperty.SetValue(run, new Vector2(8.62f, -2.75f));
                sync.Invoke(controller, new object[] { true });
                Assert.AreEqual(ScavengeLayout.RepairRoom, run.NearbyCache);
                Assert.IsTrue(wall.forceRenderingOff);
                Assert.IsFalse(ScavengeLayout.ClearLine(new Vector2(8.62f, -5.8f), new Vector2(8.62f, -3.7f)));

                playerProperty.SetValue(run, ScavengeLayout.Start);
                sync.Invoke(controller, new object[] { true });
                Assert.IsFalse(wall.forceRenderingOff);

                var site = new Vector2(-3f, -6.25f);
                typeof(ScavengeRun).GetProperty(nameof(ScavengeRun.Shelter)).SetValue(run, site);
                playerProperty.SetValue(run, site);
                sync.Invoke(controller, new object[] { true });
                Assert.IsTrue(GameObject.Find("篷顶").GetComponent<Renderer>().forceRenderingOff);

                type.GetMethod("LeaveScavenge", flags).Invoke(controller, null);
                Assert.That(camera.fieldOfView, Is.EqualTo(originalFov).Within(.01f));
                Assert.AreEqual(originalColor, camera.backgroundColor);
                Assert.That(Quaternion.Angle(originalRotation, camera.transform.rotation), Is.LessThan(.1f));
            }
            finally { InputSystem.RemoveDevice(keyboard); }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator BootScavengeSpaceResetsAfterExtractionAndDeath()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            var controller = Object.FindObjectOfType<PrototypeController>();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var type = typeof(PrototypeController);
            var runField = type.GetField("scavengeRun", flags);
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                StartLegacyScavenge();
            yield return WaitForCrossing();
                yield return null;
                var run = (ScavengeRun)runField.GetValue(controller);
                void Walk(Vector2 point)
                {
                    for (int step = 0; step < 350 && Vector2.Distance(run.Player, point) > .1f; step++)
                        run.Tick(.02f, Vector2.ClampMagnitude((point - run.Player) * 10, 1));
                    Assert.That(Vector2.Distance(run.Player, point), Is.LessThan(.11f), $"目标 {point}，实际 {run.Player}，{run.Outcome}");
                }
                void Extract()
                {
                    Walk(new Vector2(run.Player.x, -6.5f));
                    Walk(new Vector2(-9, -6.5f));
                    Walk(new Vector2(-9, 8));
                    Walk(ScavengeLayout.Exit);
                    type.GetMethod("InteractScavenge", flags).Invoke(controller, null);
                    Assert.AreEqual(ScavengeOutcome.Extracted, run.Outcome);
                }

                Assert.AreEqual(0, run.Count);
                Walk(ScavengeLayout.Caches[1]);
                Assert.IsTrue(run.Interact());
                for (int step = 0; step < 35; step++) run.Tick(.02f, Vector2.zero);
                Assert.IsTrue(run.Transfer(ScavengeItem.Crowbar, true));
                Extract();
                Assert.AreEqual(ScavengeItem.Crowbar, run.CarriedOut);
                Assert.AreEqual(0, run.Count);

                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Enter));
                yield return null;
                var empty = (ScavengeRun)runField.GetValue(controller);
                Assert.AreNotSame(run, empty);
                Assert.AreEqual(0, empty.Count);
                Assert.AreEqual(ScavengeItem.None, empty.CarriedOut);

                run = empty;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null;
                Walk(ScavengeLayout.Caches[1]);
                Walk(new Vector2(7.8f, -3));
                for (int step = 0; step < 1200 && run.Outcome == ScavengeOutcome.Exploring; step++)
                    run.Tick(.02f, Vector2.zero);
                Assert.AreEqual(ScavengeOutcome.Died, run.Outcome);
                Assert.AreEqual(ScavengeItem.None, run.CarriedOut);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Enter));
                yield return null;
                var afterDeath = (ScavengeRun)runField.GetValue(controller);
                Assert.AreNotSame(run, afterDeath);
                Assert.AreEqual(ScavengeOutcome.Exploring, afterDeath.Outcome);
                Assert.AreEqual(0, afterDeath.Count);

                type.GetMethod("LeaveScavenge", flags).Invoke(controller, null);
                Assert.IsNull(runField.GetValue(controller));
                StartLegacyScavenge();
            yield return WaitForCrossing();
                Assert.AreEqual(0, ((ScavengeRun)runField.GetValue(controller)).Count);
            }
            finally { InputSystem.RemoveDevice(keyboard); }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator BootScavengeOpensStorageAndPausesWorkshop()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            StartLegacyScavenge();
            yield return WaitForCrossing();
            yield return null;
            var controller = Object.FindObjectOfType<PrototypeController>();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var run = (ScavengeRun)typeof(PrototypeController).GetField("scavengeRun", flags).GetValue(controller);
            var panel = typeof(PrototypeController).GetField("storageOpen", flags);
            void Walk(Vector2 point)
            {
                for (int step = 0; step < 160 && Vector2.Distance(run.Player, point) > .1f; step++)
                    run.Tick(.02f, Vector2.ClampMagnitude((point - run.Player) * 10, 1));
                Assert.That(Vector2.Distance(run.Player, point), Is.LessThan(.11f));
            }
            Walk(ScavengeLayout.Caches[0]);
            Assert.IsTrue(run.Interact());
            for (int step = 0; step < 35; step++) run.Tick(.02f, Vector2.zero);
            Assert.IsTrue(run.Transfer(ScavengeItem.Materials, true));
            run.CloseSearch();
            Walk(new Vector2(-5.4f, -6));
            Assert.IsTrue(run.BuildShelter(new Vector2(-3, -6.25f)));
            for (int step = 0; step < 153; step++) run.Tick(.02f, Vector2.zero);
            Walk(new Vector2(-4.15f, -6.25f));
            Assert.IsTrue(run.CanUseStorage);
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.B));
                yield return null;
                Assert.AreEqual(true, panel.GetValue(controller));
                // 本用例聚焦 Boot 的面板与暂停，材料搜取由规则测试覆盖。
                typeof(ScavengeRun).GetProperty(nameof(ScavengeRun.Inventory)).SetValue(run, ScavengeItem.RepairMaterials);
                Assert.IsTrue(run.TransferStorage(ScavengeItem.RepairMaterials, false));
                Assert.IsTrue(run.StartWorkshop());
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
                yield return null;
                Assert.AreEqual(false, panel.GetValue(controller));
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
                yield return null;
                float remaining = run.WorkshopRemaining;
                for (int frame = 0; frame < 10; frame++) yield return null;
                Assert.Greater(remaining, 0);
                Assert.AreEqual(remaining, run.WorkshopRemaining);
            }
            finally { InputSystem.RemoveDevice(keyboard); }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator BootScavengeInstallsAutomaticDefenseFromBuildMenu()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            StartLegacyScavenge();
            yield return WaitForCrossing();
            yield return null;
            var controller = Object.FindObjectOfType<PrototypeController>();
            typeof(PrototypeController).GetMethod("BeginScavenge", System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance).Invoke(controller, new object[] { 0 });
            var field = typeof(PrototypeController).GetField("scavengeRun",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var run = (ScavengeRun)field.GetValue(controller);
            void Walk(Vector2 target)
            {
                for (int step = 0; step < 220 && Vector2.Distance(run.Player, target) > .1f; step++)
                    run.Tick(.02f, Vector2.ClampMagnitude((target - run.Player) * 10, 1));
                Assert.That(Vector2.Distance(run.Player, target), Is.LessThan(.11f));
            }
            void Take(int index, ScavengeItem item)
            {
                Walk(ScavengeLayout.Caches[index]);
                Assert.IsTrue(run.Interact());
                for (int step = 0; step < 34; step++) run.Tick(.02f, Vector2.zero);
                Assert.IsTrue(run.Transfer(item, true));
                run.CloseSearch();
            }
            Take(0, ScavengeItem.Materials);
            Take(ScavengeLayout.LoadingRack, ScavengeItem.DefenseParts);
            Walk(new Vector2(-.8f, -6));
            Walk(new Vector2(-5.4f, -6));
            Assert.IsTrue(run.BuildShelter(new Vector2(-3, -6.25f)));
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                var target = new Vector2(-1.68f, -5.22f);
                var camera = GameObject.Find("PreviewCamera").GetComponent<Camera>();
                var screen = (Vector2)camera.WorldToScreenPoint(new Vector3(target.x, 0, target.y));
                InputSystem.QueueStateEvent(mouse, new MouseState { position = screen });
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.V));
                yield return null;
                Assert.IsTrue(GameObject.Find("防御射程预览").GetComponent<LineRenderer>().enabled);
                screen = camera.WorldToScreenPoint(new Vector3(target.x, 0, target.y));
                InputSystem.QueueStateEvent(mouse, new MouseState { position = screen }.WithButton(MouseButton.Left));
                for (int frame = 0; frame < 12 && !run.Defense.HasValue; frame++) yield return null;
                Assert.IsTrue(run.Defense.HasValue);
                Assert.That(Vector2.Distance(run.Defense.Value, target), Is.LessThan(.1f));
                Assert.NotNull(GameObject.Find("防御装置工地"));
                Assert.AreEqual(ScavengeItem.None, run.Inventory);
                Walk(new Vector2(-6.8f, -6));
                for (int step = 0; step < 350; step++) run.Tick(.02f, Vector2.zero);
                yield return null;
                Assert.IsFalse(run.Patrols[1].Alive);
                Assert.NotNull(GameObject.Find("自动防御装置"));
                Assert.Greater(run.ShelterHealth, 0);
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
                InputSystem.RemoveDevice(keyboard);
            }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator BootScavengeSelectsShelterGroundAndPausesWork()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            StartLegacyScavenge();
            yield return WaitForCrossing();
            yield return null;
            var controller = Object.FindObjectOfType<PrototypeController>();
            var field = typeof(PrototypeController).GetField("scavengeRun",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var run = (ScavengeRun)field.GetValue(controller);
            var target = ScavengeLayout.Caches[0];
            for (int step = 0; step < 60 && Vector2.Distance(run.Player, target) > .1f; step++)
                run.Tick(.02f, Vector2.ClampMagnitude((target - run.Player) * 10, 1));
            Assert.IsTrue(run.Interact());
            run.Tick(.7f, Vector2.zero);
            Assert.IsTrue(run.Transfer(ScavengeItem.Materials, true));
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                var site = new Vector2(-3, -6.25f);
                var camera = GameObject.Find("PreviewCamera").GetComponent<Camera>();
                var screen = (Vector2)camera.WorldToScreenPoint(new Vector3(site.x, 0, site.y));
                InputSystem.QueueStateEvent(mouse, new MouseState { position = screen });
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.V));
                yield return null;
                Assert.NotNull(GameObject.Find("避难所落位预览"));
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
                yield return null;
                Assert.IsNull(GameObject.Find("避难所落位预览"));
                Assert.AreEqual(ScavengeItem.Materials, run.Inventory);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.V));
                yield return null;
                screen = camera.WorldToScreenPoint(new Vector3(site.x, 0, site.y));
                InputSystem.QueueStateEvent(mouse, new MouseState { position = screen }.WithButton(MouseButton.Left));
                for (int frame = 0; frame < 12 && !run.Shelter.HasValue; frame++) yield return null;
                Assert.That(Vector2.Distance(site, run.Shelter.Value), Is.LessThan(.1f));
                Assert.NotNull(GameObject.Find("避难所工地"));
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
                yield return null;
                float remaining = run.ShelterRemaining;
                for (int frame = 0; frame < 8; frame++) yield return null;
                Assert.AreEqual(remaining, run.ShelterRemaining);
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
                InputSystem.RemoveDevice(keyboard);
            }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator BootScavengeEntryMovesAnUnarmedCharacter()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            StartLegacyScavenge();
            yield return WaitForCrossing();
            yield return null;
            var character = Object.FindObjectOfType<PrototypePlayerVisual>();
            Assert.NotNull(character);
            var controller = Object.FindObjectOfType<PrototypeController>();
            var field = typeof(PrototypeController).GetField("scavengeRun",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var run = (ScavengeRun)field.GetValue(controller);
            Assert.AreEqual(ScavengeItem.None, run.Inventory);
            var start = character.transform.position;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
                for (int frame = 0; frame < 120 && character.transform.position.z < start.z + .25f; frame++)
                    yield return null;
                Assert.Greater(character.transform.position.z, start.z + .2f);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
                yield return null;
                var paused = character.transform.position;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
                for (int frame = 0; frame < 8; frame++) yield return null;
                Assert.AreEqual(paused, character.transform.position);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
                yield return null;
                var target = ScavengeLayout.Caches[0];
                for (int step = 0; step < 60 && Vector2.Distance(run.Player, target) > .1f; step++)
                    run.Tick(.02f, Vector2.ClampMagnitude((target - run.Player) * 10, 1));
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.E));
                for (int frame = 0; frame < 12 && run.OpenCache < 0; frame++) yield return null;
                Assert.AreEqual(0, run.OpenCache);
            }
            finally { InputSystem.RemoveDevice(keyboard); }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator BootScavengeSearchesDifferentStreetLocationsForTwoSeeds()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            StartLegacyScavenge();
            yield return WaitForCrossing();
            yield return null;
            var controller = Object.FindObjectOfType<PrototypeController>();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var begin = typeof(PrototypeController).GetMethod("BeginScavenge", flags);
            var field = typeof(PrototypeController).GetField("scavengeRun", flags);
            foreach (int seed in new[] { 0, 3 })
            {
                begin.Invoke(controller, new object[] { seed });
                var run = (ScavengeRun)field.GetValue(controller);
                Assert.AreEqual(seed, run.Seed);
                Assert.AreEqual(0, run.Count);
                int location = seed == 0 ? ScavengeLayout.Pharmacy : ScavengeLayout.AbandonedCar;
                Assert.IsFalse(run.IsDiscovered(location));
                void Walk(Vector2 target)
                {
                    for (int step = 0; step < 220 && Vector2.Distance(run.Player, target) > .1f; step++)
                        run.Tick(.02f, Vector2.ClampMagnitude((target - run.Player) * 10, 1));
                    Assert.That(Vector2.Distance(run.Player, target), Is.LessThan(.11f), $"种子 {seed}，{target}，{run.Player}");
                }
                if (seed == 3) Walk(new Vector2(-6.9f, -4));
                Walk(ScavengeLayout.Caches[location]);
                Assert.IsTrue(run.IsDiscovered(location));
                Assert.IsFalse(run.IsSearched(location));
                Assert.IsTrue(run.Interact());
                for (int step = 0; step < 34; step++) run.Tick(.02f, Vector2.zero);
                Assert.IsTrue(run.Transfer(ScavengeItem.Supplies, true));
                Assert.AreEqual(1, run.Count);
            }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator LegacyExpeditionsConfirmReturnAndRefreshProfile()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            var controller = Object.FindObjectOfType<PrototypeController>();
            var profile = new ExpeditionProfile(PrototypeExpeditionTests.ProfileDirectory());
            Game.Game.Interface.RegisterUtility(profile);
            yield return null;
            Assert.IsFalse(profile.CanEnter(ExpeditionMap.Industrial));
            Assert.IsFalse(profile.CanTrain);
            StartLegacyExpedition(controller, ExpeditionMap.Industrial);
            yield return WaitForCrossing();
            Assert.IsNull(controller.World.Expedition);
            StartLegacyExpedition(controller, ExpeditionMap.Wilderness);
            yield return WaitForCrossing();
            var world = controller.World;
            Assert.AreEqual(ExpeditionMap.Wilderness, world.Expedition.Map);
            Assert.IsFalse(GameObject.Find("OpenReturn").GetComponent<Button>().interactable);
            Click("OpenPause");
            var elapsed = world.Run.Elapsed;
            yield return null;
            Assert.AreEqual(elapsed, world.Run.Elapsed);
            Click("PrimaryAction");
            yield return WaitForCrossing();
            PrototypeExpeditionTests.ResearchSurvey(world);
            PrototypeExpeditionTests.Survive(world);
            yield return null;
            Click("OpenReturn");
            Assert.IsTrue(world.Run.Paused);
            Click("CancelReturn");
            Assert.IsFalse(world.Run.Paused);
            Assert.AreEqual(0, profile.Funds);
            Click("OpenReturn");
            Click("PrimaryAction");
            yield return WaitForCrossing();
            Assert.AreEqual(ExpeditionEnd.Returned, world.Expedition.End);
            Assert.AreEqual(25, profile.Funds);
            Click("PrimaryAction");
            yield return WaitForCrossing();
            Assert.IsTrue(profile.CanEnter(ExpeditionMap.Wilderness));
            Assert.AreEqual(25, profile.Funds);
            Assert.IsTrue(profile.CanEnter(ExpeditionMap.Industrial));
            Assert.IsFalse(profile.CanEnter(ExpeditionMap.Beacon));
            StartLegacyExpedition(controller, ExpeditionMap.Industrial);
            yield return WaitForCrossing();
            var workshop = controller.World;
            Assert.AreEqual(ExpeditionMap.Industrial, workshop.Expedition.Map);
            PrototypeExpeditionTests.OpenWorkshop(workshop);
            yield return null;
            Click("OpenReturn");
            Click("PrimaryAction");
            yield return WaitForCrossing();
            Assert.AreEqual(75, profile.Funds);
            Click("PrimaryAction");
            yield return WaitForCrossing();
            Assert.IsTrue(profile.CanEnter(ExpeditionMap.Beacon));
            StartLegacyExpedition(controller, ExpeditionMap.Beacon);
            yield return WaitForCrossing();
            var beacon = controller.World;
            Assert.AreEqual(ExpeditionMap.Beacon, beacon.Expedition.Map);
            PrototypeExpeditionTests.CompleteBeacon(beacon);
            yield return null;
            Assert.AreEqual(295, profile.Funds);
            Click("PrimaryAction");
            yield return WaitForCrossing();
            Assert.IsTrue(profile.TrainScout());
            Assert.IsTrue(profile.ScoutTrained);
            Assert.AreEqual(220, profile.Funds);
            yield return new ExitPlayMode();
        }
        [UnityTest]
        public IEnumerator BootAdvancesPausesAndRestartsThroughFoundation()
        {
            var main=EditorSceneManager.OpenScene("Assets/Game/Scenes/Main.unity");
            var preview=Object.FindObjectOfType<PrototypeController>();
            Assert.NotNull(new UnityEditor.SerializedObject(preview).FindProperty("previewShader").objectReferenceValue,
                "Main 必须显式持有 Shader，防止 Player 构建裁剪运行时查找的依赖");
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            var controller = Object.FindObjectOfType<PrototypeController>();
            Assert.NotNull(controller);
            Assert.NotNull(Object.FindObjectOfType<Game.UIRoot>());
            Assert.AreEqual(1, Object.FindObjectsOfType<Game.FoundationLoop>().Length);
            controller.Restart(true);
            var first = controller.World;
            Assert.AreSame(first, Game.Game.Interface.GetModel<PrototypeModel>().World);
            yield return null;
            ClickBase(controller, -4);
            for (int frame = 0; frame < 30; frame++) yield return null;
            Assert.Greater(first.Run.Elapsed, 0, "真实Play中的生命周期应推进局时");
            controller.enabled = false;
            var disabledElapsed = first.Run.Elapsed;
            for (int frame = 0; frame < 5; frame++) yield return null;
            Assert.AreEqual(disabledElapsed, first.Run.Elapsed, "视图失活应暂停并解除输入订阅");
            controller.enabled = true;
            var system = Game.Game.Interface.GetSystem<PrototypeSystem>();
            system.SetPaused(true);
            var elapsed = first.Run.Elapsed;
            for (int frame = 0; frame < 5; frame++) yield return null;
            Assert.AreEqual(elapsed, first.Run.Elapsed, "暂停应冻结局时");
            controller.Restart(false);
            Assert.AreNotSame(first, controller.World);
            Assert.AreEqual(0, controller.World.Run.Elapsed);
            Assert.IsTrue(controller.World.Run.Paused);
            Assert.IsNull(controller.World.Headquarters);
            Assert.AreEqual(0, controller.World.Run.Health);
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator BootPreviewButtonsDriveSessionFromStartThroughRestart()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            var controller = Object.FindObjectOfType<PrototypeController>();
            var walletDirectory = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,
                "../../Backup/Artifacts/FirstPlayableV07-20261110/PlayWallet-" + System.Guid.NewGuid().ToString("N")));
            var wallet = new PrototypeWallet(walletDirectory);
            Game.Game.Interface.RegisterUtility(wallet);
            controller.Restart(false);
            var world = controller.World;
            Assert.IsNotNull(GameObject.Find("StartScreenBackground"));
            Assert.IsNull(GameObject.Find("OpenPause"));
            StartLegacyDefenseTrial(controller);
            yield return WaitForCrossing();
            Assert.IsFalse(world.Run.Paused);
            Assert.AreEqual(800, world.Run.Funds);
            Assert.IsNotNull(GameObject.Find("Commander"));
            Assert.IsNull(GameObject.Find("Headquarters"));
            Assert.IsNull(GameObject.Find("DeploySquad0"));
            Assert.IsFalse(GameObject.Find("OpenUpgrade").GetComponent<Button>().interactable);
            var camera = controller.GetComponentInChildren<Camera>();
            var groundTarget = new Vector3(.6f, 0, -3.4f);
            Assert.IsTrue(controller.MoveCommanderAt(camera.WorldToScreenPoint(groundTarget)));
            PrototypeWorldTests.Advance(world, 1);
            Assert.Less(Vector2.Distance(new Vector2(groundTarget.x, groundTarget.z), world.CommanderPosition), .001f,
                "应到达实际点中的地面位置");
            Assert.AreEqual(0, world.Run.Elapsed);

            Click("OpenConstruction");
            Assert.IsNull(GameObject.Find("BuildTurret"));
            Click("BuildHeadquarters");
            Assert.IsFalse(ClickCell(controller, BattleGrid.Cell(world.CommanderPosition)));
            Assert.AreEqual(800, world.Run.Funds);
            Assert.IsTrue(ClickCell(controller, new Vector2Int(-4, 0)));
            Assert.IsNotNull(GameObject.Find("Headquarters"));
            Assert.IsNull(GameObject.Find("Commander"));
            Assert.IsTrue(GameObject.Find("OpenUpgrade").GetComponent<Button>().interactable);

            Click("OpenUpgrade");
            Assert.IsTrue(world.Run.Paused);
            Click("Purchase1");
            Assert.AreEqual(600, world.Run.Funds);
            Assert.AreEqual(1, world.Run.Levels[1]);
            Click("PrimaryAction");
            yield return WaitForCrossing();
            Click("OpenConstruction");
            Click("BuildTurret");
            Assert.IsTrue(ClickCell(controller, FindPlacement(world, ExpansionKind.Turret)));
            Assert.AreEqual(300, world.Run.Funds);
            var extractor = GameObject.Find("BuildExtractor").GetComponent<Button>();
            Assert.IsFalse(extractor.interactable);
            Assert.AreEqual(extractor.image.color.r, extractor.image.color.g);
            Click("BuildExtractor");
            yield return null;
            Assert.IsNull(GameObject.Find("BuildRange"));
            Click("OpenPause");
            Click("RestartRun");
            Assert.AreSame(world, controller.World, "重开需二次确认");
            Click("PrimaryAction");
            yield return WaitForCrossing();
            Assert.AreSame(world, controller.World);
            Assert.IsFalse(world.Run.Paused);

            world.Run.Damage(world.Run.Health);
            yield return null;
            Assert.IsNotNull(GameObject.Find("PrimaryAction"));
            Assert.AreEqual(0, wallet.Wins);
            Click("PrimaryAction");
            yield return WaitForCrossing();
            Assert.AreNotSame(world, controller.World);
            Assert.AreNotEqual(world.Seed, controller.World.Seed);
            Assert.IsTrue(controller.World.Run.Paused);
            Assert.AreEqual(800, controller.World.Run.Funds);
            Assert.IsNull(controller.World.Headquarters);

            StartLegacyDefenseTrial(controller);
            yield return WaitForCrossing();
            ClickBase(controller, 4);
            var victory = controller.World;
            while (victory.Wave < 6 || victory.WaitingEnemies > 0)
            {
                victory.Enemies.Clear();
                victory.Step(.1f);
            }
            victory.Enemies.Clear();
            victory.Step(.1f);
            yield return null;
            Assert.AreEqual(1, victory.Run.Outcome);
            Assert.AreEqual(1, wallet.Wins);
            Assert.AreEqual(900, new PrototypeWallet(walletDirectory).StartingFunds);
            Click("PrimaryAction");
            yield return WaitForCrossing();
            Assert.AreEqual(900, controller.World.Run.Funds);
            Assert.IsNull(controller.World.Headquarters);
            yield return new ExitPlayMode();
        }

        /// <summary>从 Boot 通过输入事件移动透视镜头，验证取景、落位投影和暂停门禁。</summary>
        [UnityTest]
        public IEnumerator BootBattleCameraOrbitsFramesMapAndPreservesPicking()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            var controller = Object.FindObjectOfType<PrototypeController>();
            StartLegacyDefenseTrial(controller);
            yield return WaitForCrossing();
            var camera = controller.GetComponentInChildren<Camera>();
            Assert.IsFalse(camera.orthographic);
            var openingCamera = camera.transform.position;
            ClickBase(controller, -4);
            var home = camera.transform.position;
            var facing = camera.transform.rotation;
            var inputSettings = InputSystem.settings;
            var previousEditorBehavior = inputSettings.editorInputBehaviorInPlayMode;
            var previousBackgroundBehavior = inputSettings.backgroundBehavior;
            inputSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            inputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                keyboard.MakeCurrent();
                mouse.MakeCurrent();
                var center = camera.pixelRect.center;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.RightArrow));
                for (int i = 0; i < 5; i++) yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                Assert.Greater(camera.transform.position.x, home.x, "方向键应平移镜头");
                InputSystem.QueueStateEvent(mouse, new MouseState { position = center }.WithButton(MouseButton.Middle));
                yield return null;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = center, delta = new Vector2(75, 0) }.WithButton(MouseButton.Middle));
                for (int i = 0; i < 3; i++) yield return null;
                Assert.Greater(Quaternion.Angle(camera.transform.rotation, facing), 1f, "中键横拖应旋转镜头");
                float height = camera.transform.position.y;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = center, scroll = new Vector2(0, 120) });
                for (int i = 0; i < 3; i++) yield return null;
                Assert.Less(camera.transform.position.y, height, "滚轮向前应拉近镜头");
                float firstZoomHeight = camera.transform.position.y;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = center, scroll = new Vector2(0, 120) });
                for (int i = 0; i < 3; i++) yield return null;
                Assert.Less(camera.transform.position.y, firstZoomHeight, "近景应支持继续拉近");
                float closeHeight = camera.transform.position.y;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = center, scroll = new Vector2(0, -120) });
                for (int i = 0; i < 3; i++) yield return null;
                Assert.Greater(camera.transform.position.y, closeHeight, "滚轮向后应拉远镜头");

                var world = controller.World;
                if (!GameObject.Find("ConstructionPanel")) Click("OpenConstruction");
                Click("BuildExtractor");
                var cell = FindPlacement(world, ExpansionKind.Extractor);
                Assert.IsTrue(ClickCell(controller, cell), "移动、旋转和缩放后应落在正确地格");
                Assert.AreEqual(ExpansionKind.Extractor, world.Buildings[world.FindBuilding(cell)].Kind);

                InputSystem.QueueStateEvent(mouse, new MouseState { position = center, scroll = new Vector2(0, -1200) });
                for (int i = 0; i < 3; i++) yield return null;
                float edge = world.Run.Config.MapRadius + 2f;
                for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    var corner = camera.WorldToViewportPoint(new Vector3(x * edge, 0, z * edge));
                    Assert.That(corner.x, Is.InRange(0f, 1f));
                    Assert.That(corner.y, Is.InRange(0f, 1f));
                    Assert.Greater(corner.z, 0f);
                }
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.H));
                for (int i = 0; i < 3; i++) yield return null;
                Assert.Less(Vector3.Distance(home, camera.transform.position), .001f);
                Assert.Less(Quaternion.Angle(facing, camera.transform.rotation), .01f);

                Click("OpenPause");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftArrow));
                InputSystem.QueueStateEvent(mouse, new MouseState { position = center, scroll = new Vector2(0, -120) });
                for (int i = 0; i < 5; i++) yield return null;
                Assert.Less(Vector3.Distance(home, camera.transform.position), .001f, "暂停时不能移动或缩放镜头");
                Click("PrimaryAction");
            yield return WaitForCrossing();
                for (int i = 0; i < 5; i++) yield return null;
                Assert.Less(camera.transform.position.x, home.x);
                controller.Restart(false);
                Assert.Less(Vector3.Distance(openingCamera, camera.transform.position), .001f, "新局恢复指挥员镜头");
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                InputSystem.RemoveDevice(mouse);
                inputSettings.editorInputBehaviorInPlayMode = previousEditorBehavior;
                inputSettings.backgroundBehavior = previousBackgroundBehavior;
            }
            yield return new ExitPlayMode();
        }

        /// <summary>从探索入口验证方向键、全图边界和回焦点能在同一局切换。</summary>
        [UnityTest]
        public IEnumerator BootWildernessCameraPansAndRecoversFromFullMap()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            var controller = Object.FindObjectOfType<PrototypeController>();
            StartLegacyExpedition(controller, ExpeditionMap.Wilderness);
            yield return WaitForCrossing();
            StringAssert.Contains("方向键平移镜头", GameObject.Find("ExpeditionGuide").transform.Find("Controls").GetComponent<Text>().text);
            var camera = controller.GetComponentInChildren<Camera>();
            var home = camera.transform.position;
            var inputSettings = InputSystem.settings;
            var previousEditorBehavior = inputSettings.editorInputBehaviorInPlayMode;
            var previousBackgroundBehavior = inputSettings.backgroundBehavior;
            inputSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            inputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                keyboard.MakeCurrent();
                mouse.MakeCurrent();
                var center = camera.pixelRect.center;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.RightArrow));
                for (int i = 0; i < 5; i++) yield return null;
                Assert.Greater(camera.transform.position.x, home.x, "荒野默认视角应响应方向键");

                InputSystem.QueueStateEvent(mouse, new MouseState { position = center, scroll = new Vector2(0, -1200) });
                for (int i = 0; i < 3; i++) yield return null;
                var overview = camera.transform.position;
                Assert.Greater(overview.y, home.y, "滚轮拉到全图应抬高镜头");
                for (int i = 0; i < 5; i++) yield return null;
                Assert.That(Vector3.Distance(camera.transform.position, overview), Is.LessThan(.001f), "全图应保持居中");

                InputSystem.QueueStateEvent(mouse, new MouseState { position = center, scroll = new Vector2(0, 240) });
                for (int i = 0; i < 5; i++) yield return null;
                Assert.Less(camera.transform.position.y, overview.y);
                Assert.Greater(camera.transform.position.x, overview.x, "拉近后应恢复平移");

                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.H));
                for (int i = 0; i < 3; i++) yield return null;
                Assert.That(Vector3.Distance(camera.transform.position, home), Is.LessThan(.001f), "H 应恢复指挥员焦点和默认缩放");
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                InputSystem.RemoveDevice(mouse);
                inputSettings.editorInputBehaviorInPlayMode = previousEditorBehavior;
                inputSettings.backgroundBehavior = previousBackgroundBehavior;
            }
            yield return new ExitPlayMode();
        }

        /// <summary>从荒野入口验证双指平移、缩放及暂停和界面门禁。</summary>
        [UnityTest]
        public IEnumerator BootWildernessTouchCameraPansAndZooms()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            var controller = Object.FindObjectOfType<PrototypeController>();
            var camera = controller.GetComponentInChildren<Camera>();
            var settings = InputSystem.settings;
            var editorBehavior = settings.editorInputBehaviorInPlayMode;
            var backgroundBehavior = settings.backgroundBehavior;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var screen = InputSystem.AddDevice<Touchscreen>();
            try
            {
                StartLegacyExpedition(controller, ExpeditionMap.Wilderness);
                yield return WaitForCrossing();
                var controls = GameObject.Find("ExpeditionGuide").transform.Find("Controls").GetComponent<Text>();
                StringAssert.Contains("两指拖动平移", controls.text);
                Assert.LessOrEqual(controls.preferredHeight, controls.rectTransform.rect.height, "完整镜头提示应在面板内");
                var commanderPosition = controller.World.CommanderPosition;
                var center = camera.pixelRect.center;
                var gap = Vector2.right * (camera.pixelWidth * .10f);
                var drag = gap * .4f;
                var home = camera.transform.position;
                yield return QueueTouchPair(screen, center - gap, center + gap, UnityEngine.InputSystem.TouchPhase.Began);
                center += drag;
                yield return QueueTouchPair(screen, center - gap, center + gap, UnityEngine.InputSystem.TouchPhase.Moved);
                var panned = camera.transform.position;
                Assert.Less(panned.x, home.x - .01f, "右拖应使地面向右跟手");
                Assert.That(panned.y, Is.EqualTo(home.y).Within(.001f), "等距拖动不缩放");

                center += drag;
                var wide = gap * 1.5f;
                yield return QueueTouchPair(screen, center - wide, center + wide, UnityEngine.InputSystem.TouchPhase.Moved);
                Assert.Less(camera.transform.position.x, panned.x - .01f, "张开时仍应响应中点拖动");
                Assert.Less(camera.transform.position.y, panned.y, "张开应拉近");

                var narrow = gap * .1f;
                yield return QueueTouchPair(screen, center - narrow, center + narrow, UnityEngine.InputSystem.TouchPhase.Moved);
                var overview = camera.transform.position;
                Assert.Greater(overview.y, home.y, "捏到最远应显示全图");
                center -= drag;
                yield return QueueTouchPair(screen, center - narrow, center + narrow, UnityEngine.InputSystem.TouchPhase.Moved);
                Assert.That(Vector3.Distance(camera.transform.position, overview), Is.LessThan(.001f), "全图保持居中");
                yield return QueueTouchPair(screen, center - gap, center + gap, UnityEngine.InputSystem.TouchPhase.Moved);
                var zoomed = camera.transform.position;
                center -= drag;
                yield return QueueTouchPair(screen, center - gap, center + gap, UnityEngine.InputSystem.TouchPhase.Moved);
                Assert.Greater(camera.transform.position.x, zoomed.x + .01f, "拉近后恢复平移");
                Assert.AreEqual(commanderPosition, controller.World.CommanderPosition, "镜头手势不派发移动指令");

                Click("OpenPause");
                var paused = camera.transform.position;
                yield return QueueTouchPair(screen, center - wide, center + wide, UnityEngine.InputSystem.TouchPhase.Moved);
                Assert.AreEqual(paused, camera.transform.position, "暂停时屏蔽镜头手势");
                Click("PrimaryAction");
            yield return WaitForCrossing();
                yield return QueueTouchPair(screen, center - gap, center + gap, UnityEngine.InputSystem.TouchPhase.Moved);
                Assert.AreEqual(paused, camera.transform.position, "暂停前的触点必须松开再起手");
                yield return QueueTouchPair(screen, center - gap, center + gap, UnityEngine.InputSystem.TouchPhase.Ended);

                var pauseButton = GameObject.Find("OpenPause").GetComponent<RectTransform>();
                var buttonPoint = RectTransformUtility.WorldToScreenPoint(null, pauseButton.TransformPoint(pauseButton.rect.center));
                float hudScale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
                var minimapPoint = new Vector2(1155f * hudScale, Screen.height - 140f * hudScale);
                foreach (var blocked in new[] { buttonPoint, minimapPoint })
                {
                    yield return QueueTouchPair(screen, blocked, center, UnityEngine.InputSystem.TouchPhase.Began);
                    yield return QueueTouchPair(screen, blocked + drag, center + drag, UnityEngine.InputSystem.TouchPhase.Moved);
                    Assert.AreEqual(paused, camera.transform.position, "按钮和小地图不能发起镜头手势");
                    yield return QueueTouchPair(screen, blocked + drag, center + drag, UnityEngine.InputSystem.TouchPhase.Ended);
                }

                Game.UIHelper.ShowUI<PrototypeSettingsCtrl>();
                yield return QueueTouchPair(screen, center - gap, center + gap, UnityEngine.InputSystem.TouchPhase.Began);
                yield return QueueTouchPair(screen, center - gap, center + wide, UnityEngine.InputSystem.TouchPhase.Moved);
                Assert.AreEqual(paused, camera.transform.position, "设置界面屏蔽镜头手势");
            }
            finally
            {
                InputSystem.RemoveDevice(screen);
                settings.editorInputBehaviorInPlayMode = editorBehavior;
                settings.backgroundBehavior = backgroundBehavior;
            }
            yield return new ExitPlayMode();
        }

        /// <summary>以触屏设备事件验证平移缩放、选址防误触以及单指抬手落位。</summary>
        [UnityTest]
        public IEnumerator BootTouchPinchZoomsWithoutPlacingHeadquarters()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            var controller = Object.FindObjectOfType<PrototypeController>();
            var camera = controller.GetComponentInChildren<Camera>();
            var settings = InputSystem.settings;
            var editorBehavior = settings.editorInputBehaviorInPlayMode;
            var backgroundBehavior = settings.backgroundBehavior;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var screen = InputSystem.AddDevice<Touchscreen>();
            try
            {
                StartLegacyDefenseTrial(controller);
                yield return WaitForCrossing();
                Click("OpenConstruction");
                Click("BuildHeadquarters");
                var cell = new Vector2Int(-4, 0);
                var point = BattleGrid.Center(cell);
                var worldPoint = new Vector3(point.x, 0, point.y);
                var first = (Vector2)camera.WorldToScreenPoint(worldPoint);
                var second = first + Vector2.right * (camera.pixelWidth * .15f);
                var spread = second + Vector2.right * (camera.pixelWidth * .18f);
                float initialHeight = camera.transform.position.y;
                int initialFunds = controller.World.Run.Funds;
                yield return QueueTouch(screen, 1, first, UnityEngine.InputSystem.TouchPhase.Began);
                Assert.IsFalse(controller.World.Headquarters.HasValue, "第一指按下时应等待后续手势");
                yield return QueueTouch(screen, 2, second, UnityEngine.InputSystem.TouchPhase.Began);
                yield return QueueTouch(screen, 2, spread, UnityEngine.InputSystem.TouchPhase.Moved);
                float nearHeight = camera.transform.position.y;
                Assert.Less(nearHeight, initialHeight, "双指张开应拉近");
                yield return QueueTouch(screen, 2, second, UnityEngine.InputSystem.TouchPhase.Moved);
                Assert.Greater(camera.transform.position.y, nearHeight, "双指捏合应拉远");
                var drag = Vector2.right * (camera.pixelWidth * .05f);
                float positionBeforeDrag = camera.transform.position.x;
                yield return QueueTouchPair(screen, first + drag, second + drag, UnityEngine.InputSystem.TouchPhase.Moved);
                Assert.Less(camera.transform.position.x, positionBeforeDrag, "选址期间可拖动镜头");
                yield return QueueTouch(screen, 2, second + drag, UnityEngine.InputSystem.TouchPhase.Ended);
                yield return QueueTouch(screen, 1, first + drag, UnityEngine.InputSystem.TouchPhase.Ended);
                Assert.IsFalse(controller.World.Headquarters.HasValue, "两指先后抬起不应落位");
                Assert.AreEqual(initialFunds, controller.World.Run.Funds, "镜头手势不扣建造费用");
                var tap = (Vector2)camera.WorldToScreenPoint(worldPoint);
                yield return QueueTouch(screen, 3, tap, UnityEngine.InputSystem.TouchPhase.Began);
                Assert.IsFalse(controller.World.Headquarters.HasValue, "单指应在抬手时确认");
                yield return QueueTouch(screen, 3, tap, UnityEngine.InputSystem.TouchPhase.Ended);
                Assert.IsTrue(controller.World.Headquarters.HasValue, "双指结束后应能用单指落位");
                Assert.AreEqual(point, controller.World.BasePosition);

                Click("OpenPause");
                float pausedHeight = camera.transform.position.y;
                yield return QueueTouch(screen, 4, first, UnityEngine.InputSystem.TouchPhase.Began);
                yield return QueueTouch(screen, 5, second, UnityEngine.InputSystem.TouchPhase.Began);
                yield return QueueTouch(screen, 5, spread, UnityEngine.InputSystem.TouchPhase.Moved);
                Assert.AreEqual(pausedHeight, camera.transform.position.y, "暂停时应屏蔽双指缩放");
            }
            finally
            {
                InputSystem.RemoveDevice(screen);
                settings.editorInputBehaviorInPlayMode = editorBehavior;
                settings.backgroundBehavior = backgroundBehavior;
            }
            yield return new ExitPlayMode();
        }

        /// <summary>两次开局均使用触点侦察与建造，验证手势切换不误操作。</summary>
        [UnityTest]
        public IEnumerator BootSingleTouchScoutsAndBuildsAcrossTwoSeeds()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            var controller = Object.FindObjectOfType<PrototypeController>();
            var camera = controller.GetComponentInChildren<Camera>();
            var settings = InputSystem.settings;
            var editorBehavior = settings.editorInputBehaviorInPlayMode;
            var backgroundBehavior = settings.backgroundBehavior;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var screen = InputSystem.AddDevice<Touchscreen>();
            int finger = 10;
            int? previousSeed = null;
            try
            {
                for (int run = 0; run < 2; run++)
                {
                    if (run > 0)
                    {
                        controller.Restart(false);
                        yield return null;
                    }
                    StartLegacyDefenseTrial(controller);
                    yield return WaitForCrossing();
                    // 固定全图取景，使远处黑区与合法占地在测试窗口中可触及。
                    typeof(PrototypeController).GetField("mapView", System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic).SetValue(controller, 1f);
                    yield return null;
                    var world = controller.World;
                    Assert.AreNotEqual(previousSeed, world.Seed);
                    previousSeed = world.Seed;
                    var initial = world.CommanderPosition;
                    var target = ScoutCell(camera, p => p.magnitude > 17f && p.magnitude < 23f && !world.Vision.IsVisible(p));
                    var point = ProjectCell(camera, target);

                    yield return QueueTouch(screen, finger, point, UnityEngine.InputSystem.TouchPhase.Began);
                    Assert.AreEqual(initial, world.CommanderPosition, "单指按下不提交侦察");
                    yield return QueueTouch(screen, finger + 1, point + Vector2.right * 8, UnityEngine.InputSystem.TouchPhase.Began);
                    yield return QueueTouch(screen, finger + 1, point + Vector2.right * 8, UnityEngine.InputSystem.TouchPhase.Ended);
                    yield return QueueTouch(screen, finger++, point, UnityEngine.InputSystem.TouchPhase.Ended);
                    finger++;
                    Assert.AreEqual(initial, world.CommanderPosition, "两指先后抬起不提交侦察");

                    yield return QueueTouch(screen, finger, point, UnityEngine.InputSystem.TouchPhase.Began);
                    yield return QueueTouch(screen, finger++, point, UnityEngine.InputSystem.TouchPhase.Ended);
                    PrototypeWorldTests.Advance(world, 8);
                    Assert.That(Vector2.Distance(world.CommanderPosition, BattleGrid.Center(target)), Is.LessThan(.2f));
                    Assert.IsTrue(world.Vision.IsVisible(BattleGrid.Center(target)));
                    Assert.IsFalse(world.Headquarters.HasValue);

                    Click("OpenConstruction");
                    Click("BuildHeadquarters");
                    yield return null;
                    int funds = world.Run.Funds;
                    var hidden = ScoutCell(camera, p => !world.Vision.IsVisible(p));
                    var hiddenPoint = ProjectCell(camera, hidden);
                    yield return QueueTouch(screen, finger, hiddenPoint, UnityEngine.InputSystem.TouchPhase.Began);
                    yield return QueueTouch(screen, finger++, hiddenPoint, UnityEngine.InputSystem.TouchPhase.Ended);
                    Assert.IsFalse(world.Headquarters.HasValue);
                    Assert.AreEqual(funds, world.Run.Funds);
                    Assert.NotNull(GameObject.Find("CancelPlacement"), "非法落位应保留选址");

                    yield return QueueTouch(screen, finger, hiddenPoint, UnityEngine.InputSystem.TouchPhase.Began);
                    Click("CancelPlacement");
                    yield return QueueTouch(screen, finger++, hiddenPoint, UnityEngine.InputSystem.TouchPhase.Ended);
                    Assert.IsNull(GameObject.Find("CancelPlacement"), "取消后退出选址");
                    PrototypeWorldTests.Advance(world, 8);
                    Assert.That(Vector2.Distance(world.CommanderPosition, BattleGrid.Center(target)), Is.LessThan(.2f));
                    Assert.AreEqual(funds, world.Run.Funds);

                    Click("BuildHeadquarters");
                    yield return null;
                    var baseCell = ScoutCell(camera, p => world.PreviewPlacement(
                        ExpansionKind.Headquarters, BattleGrid.Cell(p), false, -1, out _, out _) == PlacementIssue.None);
                    var basePoint = ProjectCell(camera, baseCell);
                    yield return QueueTouch(screen, finger, basePoint, UnityEngine.InputSystem.TouchPhase.Began);
                    yield return QueueTouch(screen, finger++, basePoint, UnityEngine.InputSystem.TouchPhase.Ended);
                    Assert.IsTrue(world.Headquarters.HasValue);
                    Assert.AreEqual(BattleGrid.Center(baseCell), world.BasePosition);

                    // 主基地落位会复位镜头。仍按真实触点提交首座采集站。
                    typeof(PrototypeController).GetField("mapView", System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic).SetValue(controller, 1f);
                    Click("BuildExtractor");
                    yield return null;
                    var mineCell = ScoutCell(camera, p => world.PreviewPlacement(
                        ExpansionKind.Extractor, BattleGrid.Cell(p), false, -1, out _, out _) == PlacementIssue.None);
                    var minePoint = ProjectCell(camera, mineCell);
                    yield return QueueTouch(screen, finger, minePoint, UnityEngine.InputSystem.TouchPhase.Began);
                    yield return QueueTouch(screen, finger++, minePoint, UnityEngine.InputSystem.TouchPhase.Ended);
                    int building = world.FindBuilding(mineCell);
                    Assert.GreaterOrEqual(building, 0);
                    Assert.AreEqual(ExpansionKind.Extractor, world.Buildings[building].Kind);
                    PrototypeWorldTests.Advance(world, 5.2f);
                    Assert.IsFalse(world.Buildings[building].IsUnderConstruction);
                    TestContext.WriteLine($"Touch scouting run {run + 1} (seed {world.Seed}): established headquarters and completed extractor.");
                }
            }
            finally
            {
                InputSystem.RemoveDevice(screen);
                settings.editorInputBehaviorInPlayMode = editorBehavior;
                settings.backgroundBehavior = backgroundBehavior;
            }
            yield return new ExitPlayMode();
        }

        private static Vector2 ProjectCell(Camera camera, Vector2Int cell)
        {
            var p = BattleGrid.Center(cell);
            return camera.WorldToScreenPoint(new Vector3(p.x, 0, p.y));
        }

        /// <summary>从实际视口选出不被 UI 遮挡的候选地块。</summary>
        private static Vector2Int ScoutCell(Camera camera, System.Predicate<Vector2> suitable)
        {
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            var ui = new List<RaycastResult>();
            for (int z = -22; z <= 22; z++)
            for (int x = -22; x <= 22; x++)
            {
                var cell = new Vector2Int(x, z);
                if (!suitable(BattleGrid.Center(cell))) continue;
                var screenPoint = ProjectCell(camera, cell);
                if (!camera.pixelRect.Contains(screenPoint)) continue;
                var hud = new Vector2(screenPoint.x, Screen.height - screenPoint.y) / scale;
                if (new Rect(16, 16, 330, 165).Contains(hud) || new Rect(1050, 18, 210, 235).Contains(hud)) continue;
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = screenPoint }, ui);
                if (!ui.Exists(hit => hit.module is UnityEngine.UI.GraphicRaycaster)) return cell;
            }
            throw new AssertionException("当前视口找不到无遮挡的测试地块");
        }

        /// <summary>提交真实触屏设备状态，并等待战场循环读取。</summary>
        private static IEnumerator QueueTouch(Touchscreen screen, int id, Vector2 position, UnityEngine.InputSystem.TouchPhase phase)
        {
            InputSystem.QueueStateEvent(screen, new TouchState { touchId = id, position = position, phase = phase });
            for (int i = 0; i < 3; i++) yield return null;
        }

        /// <summary>在同一输入帧提交两根手指，复现不改变间距的平移。</summary>
        private static IEnumerator QueueTouchPair(Touchscreen screen, Vector2 first, Vector2 second, UnityEngine.InputSystem.TouchPhase phase)
        {
            InputSystem.QueueStateEvent(screen, new TouchState { touchId = 1, position = first, phase = phase });
            yield return QueueTouch(screen, 2, second, phase);
        }

        /// <summary>从 Boot 用真实按钮建造和生产，核对采集入账、暂停及守军调防。</summary>
        [UnityTest]
        public IEnumerator BootConstructionProducesMinersAndMobileGuards()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            var controller = Object.FindObjectOfType<PrototypeController>();
            var world = controller.World;
            StartLegacyDefenseTrial(controller);
            yield return WaitForCrossing();
            ClickBase(controller, -4);
            Click("BuildExtractor");
            var cell = FindPlacement(world, ExpansionKind.Extractor);
            Assert.IsTrue(ClickCell(controller, cell));
            int index = world.FindBuilding(cell);
            var station = world.Buildings[index];
            int startingFunds = world.Run.Funds;
            Assert.IsTrue(station.IsUnderConstruction);
            Assert.IsNull(GameObject.Find("ProducePersonnel"));
            var elapsed = world.Run.Elapsed;
            yield return null;
            Assert.Greater(world.Run.Elapsed, elapsed);
            PrototypeWorldTests.Advance(world, 5.2f);
            yield return null;
            Assert.IsNull(station.Personnel);
            Click("ProducePersonnel");
            Assert.AreEqual(startingFunds - 150, world.Run.Funds);
            Assert.Greater(station.ProductionLeft, 0);
            Click("OpenPause");
            float remaining = station.ProductionLeft;
            yield return null;
            Assert.AreEqual(remaining, station.ProductionLeft);
            Click("PrimaryAction");
            yield return WaitForCrossing();
            PrototypeWorldTests.Advance(world, 26);
            yield return null;
            Assert.NotNull(station.Personnel);
            Assert.Greater(world.Run.Funds, startingFunds - 150);
            Assert.IsTrue(GameObject.Find("Building" + index).transform.Find("Gatherer").gameObject.activeInHierarchy);

            Click("OpenConstruction");
            Click("BuildBarracks");
            var barracksCell = FindPlacement(world, ExpansionKind.Barracks);
            Assert.IsTrue(ClickCell(controller, barracksCell));
            int barracksIndex = world.FindBuilding(barracksCell);
            var barracks = world.Buildings[barracksIndex];
            PrototypeWorldTests.Advance(world, 8.1f);
            yield return null;
            Assert.IsNull(GameObject.Find("DeploySquad2"));
            Click("ProducePersonnel");
            PrototypeWorldTests.Advance(world, 12.1f);
            yield return null;
            Assert.IsNotNull(barracks.Personnel);
            Click("DeploySquad2");
            Assert.AreEqual(2, barracks.Personnel.Sector);
            PrototypeWorldTests.Advance(world, 10);
            var post = world.BasePosition + Vector2.up * 12;
            if (world.FindBuilding(BattleGrid.Cell(post)) >= 0)
                Assert.AreEqual(BattleGrid.CellSize, Vector2.Distance(post, barracks.Personnel.Position), "驻防目标被占时使用相邻格");
            else Assert.AreEqual(post, barracks.Personnel.Position);
            yield return new ExitPlayMode();
        }

        private static void ClickBase(PrototypeController controller, int cellX)
        {
            Click("OpenConstruction");
            Click("BuildHeadquarters");
            Assert.IsTrue(ClickCell(controller, new Vector2Int(cellX, 0)));
        }

        private static Vector2Int FindPlacement(WorldState world, ExpansionKind kind)
        {
            return PrototypeWorldTests.FindPlacement(world, kind, world.BasePosition + Vector2.up * 7);
        }

        private static bool ClickCell(PrototypeController controller, Vector2Int cell)
        {
            var point = BattleGrid.Center(cell);
            var camera = controller.GetComponentInChildren<Camera>();
            return controller.ActOnBattlefield(camera.WorldToScreenPoint(new Vector3(point.x, 0, point.y)));
        }

        /// <summary>等待真实穿越流程交还控制，不跳过动画、不使用测试专用启动口。</summary>
        private static IEnumerator WaitForCrossing()
        {
            var owner = Object.FindObjectOfType<PrototypeController>();
            float deadline = Time.realtimeSinceStartup + 15;
            while (owner != null && owner.IsCrossing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsFalse(owner != null && owner.IsCrossing, "穿越未在限定时间内结束");
        }

        /// <summary>通过活动 EventSystem 分发 UGUI 左键事件，不绕过按钮监听器调用玩法。</summary>
        private static void Click(string name)
        {
            var target = GameObject.Find(name);
            Assert.NotNull(target, name + " 应在当前窗口显示");
            Assert.NotNull(target.GetComponent<Button>());
            Assert.NotNull(EventSystem.current);
            Assert.IsTrue(ExecuteEvents.Execute(target,
                new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left },
                ExecuteEvents.pointerClickHandler));
        }

        /// <summary>直接启动历史守城会话，保留对共享战场与局内按钮的回归覆盖。</summary>
        private static void StartLegacyDefenseTrial(PrototypeController controller)
        {
            Assert.AreEqual(PrototypeOverlay.Start, controller.Overlay);
            controller.CrossToWorld(controller.ActOnModal);
        }

        /// <summary>使用内部入口回归历史撤离试玩中的共享探索操作。</summary>
        private static void StartLegacyScavenge()
        {
            var controller = Object.FindObjectOfType<PrototypeController>();
            Assert.NotNull(controller);
            Assert.AreEqual(PrototypeOverlay.Start, controller.Overlay);
            controller.CrossToWorld(controller.StartScavenge);
        }

        /// <summary>直接启动历史远征，供局内流程和共享镜头回归使用。</summary>
        private static void StartLegacyExpedition(PrototypeController controller, ExpeditionMap map)
        {
            Assert.AreEqual(PrototypeOverlay.Start, controller.Overlay);
            controller.CrossToWorld(() => controller.Depart(map));
        }
    }
}
