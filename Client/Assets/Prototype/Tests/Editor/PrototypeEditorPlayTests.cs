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
                typeof(ScavengeRun).GetProperty(nameof(ScavengeRun.Player)).SetValue(run, ScavengeTown.Caches[0]);
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
                typeof(ScavengeRun).GetProperty(nameof(ScavengeRun.Player)).SetValue(run, ScavengeTown.Exit);
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
        public IEnumerator BootRiverValleySurvivalLoadsSceneryAndKeepsTheDistrictPlayable()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            var controller = Object.FindObjectOfType<PrototypeController>();
            var type = typeof(PrototypeController);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var directory = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,
                "../../Backup/Artifacts/RiverValleySurvival-20261113/TestSave-" + System.Guid.NewGuid().ToString("N")));
            type.GetField("survivalStore", flags).SetValue(controller, new Game.JsonFileStore<SurvivalSave>(directory));
            type.GetField("survivalSaveChecked", flags).SetValue(controller, false);
            Click("StartSurvival");
            yield return WaitForCrossing();
            var scenery = GameObject.Find("河谷镇周边外景");
            Assert.NotNull(scenery);
            Assert.IsEmpty(scenery.GetComponentsInChildren<Camera>());
            Assert.IsEmpty(scenery.GetComponentsInChildren<Collider>());
            Assert.IsNull(GameObject.Find("外景步行角色"));
            Assert.That(scenery.GetComponentsInChildren<MeshFilter>().Length, Is.GreaterThan(200));
            Assert.NotNull(GameObject.Find("东侧街区边界"));
            Assert.NotNull(GameObject.Find("河谷镇坡屋面"));
            // 外景不得盖住平地、搜点或房间；每个网格顶点都应位于保留区以外。
            bool intrudes = false;
            var district = new Rect(-109, -96, 218, 192);
            foreach (var filter in scenery.GetComponentsInChildren<MeshFilter>())
            {
                foreach (var vertex in filter.sharedMesh.vertices)
                {
                    var world = filter.transform.TransformPoint(vertex);
                    if (district.Contains(new Vector2(world.x, world.z))) { intrudes = true; break; }
                }
                if (intrudes) break;
            }
            Assert.IsFalse(intrudes, "外景侵入可玩街区");
            var run = (ScavengeRun)type.GetField("scavengeRun", flags).GetValue(controller);
            Assert.That(Vector2.Distance(run.Player, ScavengeTown.Start), Is.LessThan(.01f));
            // 实际领域步行抵达镇口搜点，搜索完成后可取得固定饮水。
            for (int step = 0; step < 80 && Vector2.Distance(run.Player, ScavengeTown.Caches[0]) > .4f; step++)
                run.Tick(.05f, (ScavengeTown.Caches[0] - run.Player).normalized);
            Assert.That(Vector2.Distance(run.Player, ScavengeTown.Caches[0]), Is.LessThan(.5f));
            run.Interact();
            for (int step = 0; step < 100 && run.SearchRemaining > 0; step++) run.Tick(.05f, Vector2.zero);
            Assert.AreEqual(0, run.OpenCache);
            Assert.That(run.Survival.caches[0].items.Exists(item => item.item == SurvivalItem.Water), Is.True);
            Assert.IsTrue(ScavengeSurvival.Transfer(run.Survival.caches[0].items, 0, run.Survival.pack, 6));
            run.CloseSearch();
            type.GetMethod("SyncScavenge", flags).Invoke(controller, new object[] { true });
            yield return CaptureSurvivalEvidence("town-street", 1920, 1080, "RiverValleySurvival-20261113");
            type.GetField("survivalRegionMapOpen", flags).SetValue(controller, true);
            double beforeMap = run.Survival.elapsed;
            yield return CaptureSurvivalEvidence("region-map", 1280, 720, "RiverValleySurvival-20261113");
            Assert.AreEqual(beforeMap, run.Survival.elapsed, "区域图应暂停局时");
            type.GetField("survivalRegionMapOpen", flags).SetValue(controller, false);
            // 边界以实体围栏表达，领域移动仍拒绝越界。
            typeof(ScavengeRun).GetProperty(nameof(ScavengeRun.Player)).SetValue(run, new Vector2(83, 0));
            for (int step = 0; step < 20; step++) run.Tick(.05f, Vector2.right);
            Assert.That(run.Player.x, Is.LessThanOrEqualTo(ScavengeTown.Bounds.xMax));
            type.GetMethod("SyncScavenge", flags).Invoke(controller, new object[] { true });
            yield return CaptureSurvivalEvidence("district-edge", 1280, 720, "RiverValleySurvival-20261113");
            type.GetMethod("LeaveScavenge", flags).Invoke(controller, null);
            yield return null;
            Assert.IsNull(GameObject.Find("河谷镇周边外景"));
            Assert.IsNull(GameObject.Find("东侧街区边界"));
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator RiverValleyStandalonePreviewRetainsItsWalkerAndCamera()
        {
            EditorSceneManager.OpenScene("Assets/Prototype/RiverValleyTown/RiverValleyTown.unity");
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
            Assert.NotNull(GameObject.Find("CosmicSpace"));
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
                player.SetValue(run, ScavengeTown.Caches[0]);
                DriveScavengeKey(controller, poll, keyboard, Key.E);
                Assert.AreEqual(0, run.OpenCache);
                run.CloseSearch();

                var door = SurvivalRooms.Door(0).center;
                player.SetValue(run, door - Vector2.right);
                DriveScavengeKey(controller, poll, keyboard, Key.E);
                Assert.IsTrue(run.Survival.rooms[0].open);
                player.SetValue(run, SurvivalRooms.Footprint(0).center);
                Assert.IsTrue(run.ClaimSurvivalRoom());
                Assert.That(run.Survival.pack, Is.Empty);
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
                Assert.AreEqual(0, resumed.Survival.baseRoom);
                Assert.IsTrue(resumed.Survival.rooms[0].open);
                Assert.IsNull(resumed.Survival.weapon);
                Assert.IsNull(resumed.Survival.torch);
                resumed.Survival.elapsed = ScavengeSurvival.Goal;
                player.SetValue(resumed, ScavengeTown.Exit);
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
        public IEnumerator BootScavengeTownStartsWithWeatherAndLetsThePlayerSpeak()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            Click("StartScavengeTown");
            yield return WaitForCrossing();
            yield return null;
            var controller = Object.FindObjectOfType<PrototypeController>();
            var type = typeof(PrototypeController);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
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
            Click("StartScavenge");
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
                Click("StartScavenge");
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
                Click("StartScavenge");
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
            Click("StartScavenge");
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
            Click("StartScavenge");
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
            Click("StartScavenge");
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
            Click("StartScavenge");
            yield return WaitForCrossing();
            yield return null;
            var character = GameObject.Find("探索角色");
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
            Click("StartScavenge");
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
        public IEnumerator BootExpeditionButtonsConfirmReturnAndRefreshProfile()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            var controller = Object.FindObjectOfType<PrototypeController>();
            var profile = new ExpeditionProfile(PrototypeExpeditionTests.ProfileDirectory());
            Game.Game.Interface.RegisterUtility(profile);
            yield return null;
            Click("TrialsEntry");
            Assert.IsFalse(GameObject.Find("DepartIndustrial").GetComponent<Button>().interactable);
            Assert.IsFalse(GameObject.Find("TrainCommander").GetComponent<Button>().interactable);
            Click("DepartIndustrial");
            yield return WaitForCrossing();
            Assert.IsNull(controller.World.Expedition);
            Click("DepartWilderness");
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
            Assert.IsTrue(GameObject.Find("DepartWilderness").GetComponent<Button>().interactable);
            Assert.AreEqual(25, profile.Funds);
            Assert.IsTrue(GameObject.Find("DepartIndustrial").GetComponent<Button>().interactable);
            Assert.IsFalse(GameObject.Find("DepartBeacon").GetComponent<Button>().interactable);
            Click("DepartIndustrial");
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
            Assert.IsTrue(GameObject.Find("DepartBeacon").GetComponent<Button>().interactable);
            Click("DepartBeacon");
            yield return WaitForCrossing();
            var beacon = controller.World;
            Assert.AreEqual(ExpeditionMap.Beacon, beacon.Expedition.Map);
            PrototypeExpeditionTests.CompleteBeacon(beacon);
            yield return null;
            Assert.AreEqual(295, profile.Funds);
            Click("PrimaryAction");
            yield return WaitForCrossing();
            Click("TrainScout");
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
            Assert.IsNotNull(GameObject.Find("BaseArtwork")?.GetComponent<RawImage>()?.texture);
            Assert.IsNull(GameObject.Find("OpenPause"));
            Click("PrimaryAction");
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
            if (GameObject.Find("PrimaryAction") == null) Click("TrialsEntry");
            Assert.IsNotNull(GameObject.Find("PrimaryAction"));
            Assert.AreEqual(0, wallet.Wins);
            Click("PrimaryAction");
            yield return WaitForCrossing();
            Assert.AreNotSame(world, controller.World);
            Assert.AreNotEqual(world.Seed, controller.World.Seed);
            Assert.IsTrue(controller.World.Run.Paused);
            Assert.AreEqual(800, controller.World.Run.Funds);
            Assert.IsNull(controller.World.Headquarters);

            Click("PrimaryAction");
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
            Click("PrimaryAction");
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
            Click("DepartWilderness");
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
                Click("DepartWilderness");
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
                Click("PrimaryAction");
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
                    Click("PrimaryAction");
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
            Click("PrimaryAction");
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
            if (target == null && GameObject.Find("TrialsEntry") != null)
            {
                Click("TrialsEntry");
                target = GameObject.Find(name);
            }
            Assert.NotNull(target, name + " 应在当前窗口显示");
            Assert.NotNull(target.GetComponent<Button>());
            Assert.NotNull(EventSystem.current);
            Assert.IsTrue(ExecuteEvents.Execute(target,
                new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left },
                ExecuteEvents.pointerClickHandler));
        }
    }
}
