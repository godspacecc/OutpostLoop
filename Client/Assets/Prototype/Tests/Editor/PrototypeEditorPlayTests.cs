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
        public IEnumerator BootScavengeInstallsAutomaticDefenseFromBuildMenu()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            Click("StartScavenge");
            yield return null;
            var controller = Object.FindObjectOfType<PrototypeController>();
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
            Take(1, ScavengeItem.DefenseParts);
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
        public IEnumerator BootExpeditionButtonsConfirmReturnAndRefreshProfile()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            var controller = Object.FindObjectOfType<PrototypeController>();
            var profile = new ExpeditionProfile(PrototypeExpeditionTests.ProfileDirectory());
            Game.Game.Interface.RegisterUtility(profile);
            yield return null;
            Assert.IsFalse(GameObject.Find("DepartIndustrial").GetComponent<Button>().interactable);
            Assert.IsFalse(GameObject.Find("TrainCommander").GetComponent<Button>().interactable);
            Click("DepartIndustrial");
            Assert.IsNull(controller.World.Expedition);
            Click("DepartWilderness");
            var world = controller.World;
            Assert.AreEqual(ExpeditionMap.Wilderness, world.Expedition.Map);
            Assert.IsFalse(GameObject.Find("OpenReturn").GetComponent<Button>().interactable);
            Click("OpenPause");
            var elapsed = world.Run.Elapsed;
            yield return null;
            Assert.AreEqual(elapsed, world.Run.Elapsed);
            Click("PrimaryAction");
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
            Assert.AreEqual(ExpeditionEnd.Returned, world.Expedition.End);
            Assert.AreEqual(25, profile.Funds);
            Click("PrimaryAction");
            Assert.IsTrue(GameObject.Find("DepartWilderness").GetComponent<Button>().interactable);
            Assert.AreEqual(25, profile.Funds);
            Assert.IsTrue(GameObject.Find("DepartIndustrial").GetComponent<Button>().interactable);
            Assert.IsFalse(GameObject.Find("DepartBeacon").GetComponent<Button>().interactable);
            Click("DepartIndustrial");
            var workshop = controller.World;
            Assert.AreEqual(ExpeditionMap.Industrial, workshop.Expedition.Map);
            PrototypeExpeditionTests.OpenWorkshop(workshop);
            yield return null;
            Click("OpenReturn");
            Click("PrimaryAction");
            Assert.AreEqual(75, profile.Funds);
            Click("PrimaryAction");
            Assert.IsTrue(GameObject.Find("DepartBeacon").GetComponent<Button>().interactable);
            Click("DepartBeacon");
            var beacon = controller.World;
            Assert.AreEqual(ExpeditionMap.Beacon, beacon.Expedition.Map);
            PrototypeExpeditionTests.CompleteBeacon(beacon);
            yield return null;
            Assert.AreEqual(295, profile.Funds);
            Click("PrimaryAction");
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
            Assert.AreSame(world, controller.World);
            Assert.IsFalse(world.Run.Paused);

            world.Run.Damage(world.Run.Health);
            yield return null;
            Assert.IsNotNull(GameObject.Find("PrimaryAction"));
            Assert.AreEqual(0, wallet.Wins);
            Click("PrimaryAction");
            Assert.AreNotSame(world, controller.World);
            Assert.AreNotEqual(world.Seed, controller.World.Seed);
            Assert.IsTrue(controller.World.Run.Paused);
            Assert.AreEqual(800, controller.World.Run.Funds);
            Assert.IsNull(controller.World.Headquarters);

            Click("PrimaryAction");
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

        /// <summary>用 Boot 的实际地图入口落站、出兵、观察来袭并回到开始页。</summary>
        [UnityTest]
        public IEnumerator BootOldHarborEntersBattleAndReturnsToStart()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            var controller = Object.FindObjectOfType<PrototypeController>();
            Click("StartOldHarbor");
            var harbor = controller.World;
            Assert.IsTrue(harbor.IsOldHarbor);
            Assert.IsNotNull(GameObject.Find("HarborBasin"));
            Assert.IsNotNull(GameObject.Find("HarborLevee"));
            ClickBase(controller, 4);
            Assert.IsNotNull(GameObject.Find("RoofGenerator"));
            Click("BuildExtractor");
            var extractor = FindPlacement(harbor, ExpansionKind.Extractor);
            Assert.IsTrue(ClickCell(controller, extractor));
            PrototypeWorldTests.Advance(harbor, 5.2f);
            yield return null;
            Click("ProducePersonnel");
            PrototypeWorldTests.Advance(harbor, 26);
            Assert.IsNotNull(harbor.Buildings[harbor.FindBuilding(extractor)].Personnel);
            PrototypeWorldTests.Advance(harbor, 91);
            yield return null;
            Assert.GreaterOrEqual(harbor.Wave, 1);
            harbor.Run.Damage(harbor.Run.Health);
            yield return null;
            Click("PrimaryAction");
            Assert.IsFalse(controller.World.IsOldHarbor);
            Assert.IsNull(GameObject.Find("HarborBasin"));
            Click("StartOldHarbor");
            Assert.IsTrue(controller.World.IsOldHarbor);
            Assert.AreNotEqual(harbor.Seed, controller.World.Seed);
            Assert.IsNull(controller.World.Headquarters);
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
    }
}
