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
            Assert.AreEqual(controller.World.Run.Config.InitialHealth, controller.World.Run.Health);
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
                "../../Backup/Artifacts/ConstructionAffordability-20261110/PlayWallet-" + System.Guid.NewGuid().ToString("N")));
            var wallet = new PrototypeWallet(walletDirectory);
            Game.Game.Interface.RegisterUtility(wallet);
            var run = controller.World.Run;
            Assert.IsTrue(run.Paused);
            Assert.IsTrue(Game.UIHelper.HasUIOpen);
            Assert.IsNull(GameObject.Find("OpenPause"), "开局前应隐藏局内暂停入口");
            var artwork = GameObject.Find("BaseArtwork")?.GetComponent<RawImage>();
            Assert.IsNotNull(artwork?.texture, "从 Boot 进入 Main 后，开始页应显示场景引用的基地概念图");
            Click("PrimaryAction");
            Assert.IsFalse(run.Paused);
            Assert.IsFalse(Game.UIHelper.HasUIOpen);
            Assert.IsFalse(artwork.gameObject.activeInHierarchy);
            Assert.IsNotNull(GameObject.Find("OpenUpgrade"), "指挥视图可直接打开升级");
            var camera = controller.GetComponentInChildren<Camera>();
            var scrapNode = controller.World.Nodes[1];
            var worldPoint = new Vector3(scrapNode.Position.x, .6f, scrapNode.Position.y);
            Assert.IsTrue(controller.OrderResourceAt(camera.WorldToScreenPoint(worldPoint)), "主视图资源投影应可点选");
            Assert.AreEqual(1, controller.World.ResourceOrder);
            Click("DeploySquad2");
            Assert.AreEqual(2, controller.World.SquadSector);
            for (int i = 0; i < 400 && run.Scrap == 0; i++) controller.World.Step(.1f);
            yield return null;
            Assert.GreaterOrEqual(run.Scrap, run.ScrapCost(1), "库存来自实际工人自动返程");
            Assert.Greater(controller.World.SquadPosition.y, 0);
            Click("OpenPause");
            Assert.IsTrue(run.Paused, "离开基地时也应能通过 HUD 暂停");
            Assert.IsTrue(Game.UIHelper.HasUIOpen);
            Assert.IsNull(GameObject.Find("OpenPause"));
            var pausedElapsed = run.Elapsed;
            for (int frame = 0; frame < 5; frame++) yield return null;
            Assert.AreEqual(pausedElapsed, run.Elapsed, "触屏暂停应冻结本局倒计时");
            Click("PrimaryAction");
            Assert.IsFalse(run.Paused);
            Assert.IsNotNull(GameObject.Find("OpenPause"), "继续后应恢复局内暂停入口");

            int cost = run.ScrapCost(1);
            int beforeUpgrade = run.Scrap;
            yield return null;
            Click("OpenUpgrade");
            Assert.IsTrue(run.Paused);
            Assert.IsNull(GameObject.Find("OpenPause"), "升级窗口应隐藏暂停入口");
            Assert.IsNull(GameObject.Find("OpenConstruction"), "建造入口只在局内 HUD 显示");
            Click("Purchase1");
            Assert.AreEqual(1, run.Levels[1]);
            Assert.AreEqual(beforeUpgrade - cost, run.Scrap);
            Click("Purchase1");
            Assert.AreEqual(1, run.Levels[1], "资源不足时按钮不能改变升级等级");
            Click("PrimaryAction");
            Click("OpenConstruction");
            var world = controller.World;
            var original = new Vector2Int(-2, 0);
            Assert.IsTrue(ClickCell(controller, original));
            Click("MoveSelectedTurret");
            Assert.IsFalse(ClickCell(controller, new Vector2Int(2, 0)), "不应覆盖已有炮塔");
            var target = FindPlacement(world, ExpansionKind.Turret, 4);
            Assert.IsTrue(ClickCell(controller, target));
            Assert.AreEqual(-1, world.FindBuilding(original));
            Assert.AreEqual(4, world.FindBuilding(target));
            var turretButton = GameObject.Find("BuildTurret").GetComponent<Button>();
            Assert.IsFalse(turretButton.interactable);
            Assert.AreEqual(turretButton.image.color.r, turretButton.image.color.g, "库存不足时按钮底色应为灰色");
            Click("BuildTurret");
            yield return null;
            Assert.IsNull(GameObject.Find("BuildRange"), "点击禁用按钮不应进入选址");
            Assert.AreEqual(beforeUpgrade - cost, run.Scrap);
            Assert.IsFalse(run.Paused);

            var turretSpec = ExpansionSite.SpecFor(ExpansionKind.Turret);
            int scrap = turretSpec.Scrap - run.Scrap;
            int energy = turretSpec.Energy - run.Energy;
            for (int i = 0; i < scrap + energy - 1; i++)
            {
                if (run.CargoScrap + run.CargoEnergy == run.Capacity) run.Deposit();
                Assert.IsTrue(run.Gather(i >= scrap));
            }
            run.Deposit();
            yield return null;
            Assert.AreEqual(turretSpec.Scrap, run.Scrap);
            Assert.IsFalse(turretButton.interactable, "废料足够但缺一单位能源时仍不可点击");
            Assert.IsTrue(run.Gather(true));
            run.Deposit();
            yield return null;
            Assert.IsTrue(turretButton.interactable, "库存足够后，无需重开侧栏即可建造");
            Assert.AreNotEqual(turretButton.image.color.r, turretButton.image.color.g, "恢复可建时恢复原色");
            Click("BuildTurret");
            Assert.AreEqual(turretSpec.Scrap, run.Scrap, "选类型不会扣费");
            Assert.IsTrue(ClickCell(controller, FindPlacement(world, ExpansionKind.Turret)));
            Assert.IsTrue(world.Buildings[6].IsUnderConstruction);
            var model = GameObject.Find("Building6").transform;
            Assert.IsTrue(model.Find("Worksite").gameObject.activeInHierarchy);
            Assert.IsFalse(model.Find("Turret").gameObject.activeInHierarchy);
            Assert.AreEqual(0, run.Scrap);
            Assert.AreEqual(0, run.Energy);
            foreach (string kind in new[] { "Extractor", "Barracks", "Barricade", "Turret" })
                Assert.IsFalse(GameObject.Find("Build" + kind).GetComponent<Button>().interactable, kind);
            Click("CloseConstruction");
            for (int i = 0; i < 62; i++) world.Step(.1f);
            yield return null;
            Assert.IsTrue(model.Find("Turret").gameObject.activeInHierarchy);
            Assert.IsFalse(model.Find("Worksite").gameObject.activeInHierarchy);

            Assert.IsFalse(run.Paused);
            run.Damage(run.Health);
            yield return null;
            Assert.IsTrue(Game.UIHelper.HasUIOpen);
            Assert.IsNull(GameObject.Find("OpenPause"), "结算时应隐藏暂停入口");
            Assert.AreEqual(0, wallet.Coins, "失败不给补给币");
            var oldWorld = controller.World;
            Click("PrimaryAction");
            Assert.AreNotSame(oldWorld, controller.World);
            Assert.AreEqual(0, controller.World.Run.Levels[1]);
            Assert.IsTrue(controller.World.Run.Paused, "结算后的下一局应先停在开局补给页");
            Assert.IsNotNull(GameObject.Find("SupplyScrap"), "开局前应能选择废料补给");
            Click("PrimaryAction");
            Assert.IsFalse(controller.World.Run.Paused);

            // 直接推进结算条件，整局实战由原有流程测试覆盖。
            var victory = controller.World.Run;
            victory.Tick(victory.Config.Duration, 0);
            for (int frame = 0; frame < 5; frame++) yield return null;
            Assert.AreEqual(PrototypeWallet.VictoryCoins, wallet.Coins, "胜利发币一次");
            Assert.AreEqual(wallet.Coins, new PrototypeWallet(walletDirectory).Coins, "退出后可读回胜利余额");
            Click("PrimaryAction");
            Click("SupplyScrap");
            Click("SupplyEnergy");
            Assert.AreEqual(PrototypeWallet.VictoryCoins, wallet.Coins, "只选择补给不扣币");
            Click("PrimaryAction");
            Assert.AreEqual(0, wallet.Coins);
            Assert.AreEqual(0, new PrototypeWallet(walletDirectory).Coins, "购买后保存剩余余额");
            Assert.AreEqual(PrototypeWallet.ScrapPack, controller.World.Run.Scrap);
            Assert.AreEqual(PrototypeWallet.EnergyPack, controller.World.Run.Energy);
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
                var node = world.Nodes[1];
                var resource = new Vector3(node.Position.x, .6f, node.Position.y);
                Assert.IsTrue(controller.OrderResourceAt(camera.WorldToScreenPoint(resource)));
                Assert.AreEqual(1, world.ResourceOrder, "旋转后应点中同一个资源节点");
                for (int i = 0; i < 400 && world.Run.Scrap == 0; i++) world.Step(.1f);
                Click("OpenConstruction");
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
                Assert.Less(Vector3.Distance(home, camera.transform.position), .001f, "新局恢复默认镜头");
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

        /// <summary>从 Boot 用真实按钮派工、施工和付费出兵，核对所选地块及完工模型。</summary>
        [UnityTest]
        public IEnumerator BootExpansionButtonsBuildExtractorFromWorkerStock()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            yield return WaitForMain();
            var controller = Object.FindObjectOfType<PrototypeController>();
            var world = controller.World;
            Click("PrimaryAction");
            var node = world.Nodes[1];
            var camera = controller.GetComponentInChildren<Camera>();
            Assert.IsTrue(controller.OrderResourceAt(camera.WorldToScreenPoint(new Vector3(node.Position.x, .6f, node.Position.y))));
            for (int i = 0; i < 400 && world.Run.Scrap == 0; i++) world.Step(.1f);
            int before = world.Run.Scrap;
            var spec = ExpansionSite.SpecFor(ExpansionKind.Extractor);
            Assert.GreaterOrEqual(before, spec.Scrap);
            Click("OpenConstruction");
            Assert.IsFalse(world.Run.Paused, "建造侧栏应保持战斗实时推进");
            Assert.IsFalse(Game.UIHelper.HasUIOpen, "建造不应登记为阻断玩法的窗口");
            Click("BuildExtractor");
            Assert.AreEqual(before, world.Run.Scrap);
            Assert.IsFalse(ClickCell(controller, Vector2Int.zero));
            Assert.AreEqual(before, world.Run.Scrap);
            var location = FindPlacement(world, ExpansionKind.Extractor);
            Assert.IsTrue(ClickCell(controller, location));
            int index = world.FindBuilding(location);
            var site = world.Buildings[index];
            Assert.AreEqual(ExpansionKind.Extractor, site.Kind);
            Assert.AreEqual(before - spec.Scrap, world.Run.Scrap);
            var plot = GameObject.Find("Building" + index).transform;
            var model = plot.Find("Extractor").gameObject;
            Assert.IsTrue(plot.Find("Worksite").gameObject.activeInHierarchy);
            Assert.IsFalse(model.activeInHierarchy);
            var source = world.Nodes[site.NodeIndex];
            int beforeRemaining = source.Remaining;
            var elapsed = world.Run.Elapsed;
            for (int i = 0; i < 5; i++) yield return null;
            Assert.Greater(world.Run.Elapsed, elapsed, "真实生命周期应在面板打开时推进");
            Assert.AreEqual(beforeRemaining, source.Remaining);
            Click("OpenUpgrade");
            Assert.IsNull(GameObject.Find("ConstructionPanel"), "打开基地升级应关闭独立建造面板");
            var pausedConstruction = site.ConstructionLeft;
            for (int i = 0; i < 5; i++) yield return null;
            Assert.AreEqual(pausedConstruction, site.ConstructionLeft);
            Click("PrimaryAction");
            for (int i = 0; i < 80; i++) world.Step(.1f);
            yield return null;
            Assert.IsTrue(model.activeInHierarchy);
            Assert.IsFalse(plot.Find("Worksite").gameObject.activeInHierarchy);
            Assert.Less(source.Remaining, beforeRemaining);
            Assert.Greater(world.Run.Scrap + world.Run.Energy, before - spec.Scrap);

            foreach (int resource in new[] { 2, 0 })
            {
                Assert.IsTrue(world.AssignResource(resource));
                for (int i = 0; i < 600 && (world.ResourceOrder >= 0 || world.WorkerReturning); i++) world.Step(.1f);
            }
            Click("OpenConstruction");
            Click("BuildBarracks");
            var barracksLocation = FindPlacement(world, ExpansionKind.Barracks);
            Assert.IsTrue(ClickCell(controller, barracksLocation));
            int barracksIndex = world.FindBuilding(barracksLocation);
            var barracks = world.Buildings[barracksIndex];
            Assert.AreEqual(ExpansionKind.Barracks, barracks.Kind);
            Assert.IsNull(GameObject.Find("TrainGarrison"));
            Click("CloseConstruction");
            for (int i = 0; i < 85; i++) world.Step(.1f);
            Assert.IsTrue(ClickCell(controller, barracksLocation));
            Assert.IsFalse(barracks.HasGarrison);
            int beforeTrainingScrap = world.Run.Scrap;
            int beforeTrainingEnergy = world.Run.Energy;
            Click("TrainGarrison");
            Assert.AreEqual(beforeTrainingScrap - ExpansionSite.TrainingScrap, world.Run.Scrap);
            Assert.AreEqual(beforeTrainingEnergy - ExpansionSite.TrainingEnergy, world.Run.Energy);
            Assert.AreEqual(ExpansionSite.TrainingSeconds, barracks.TrainingLeft);
            Assert.IsNull(GameObject.Find("TrainGarrison"));
            for (int i = 0; i < 125; i++) world.Step(.1f);
            yield return null;
            Assert.IsTrue(barracks.HasGarrison);
            var garrison = GameObject.Find("Building" + barracksIndex).transform.Find("Garrison").gameObject;
            Assert.IsTrue(garrison.activeInHierarchy);
            for (int i = 0; i < 40; i++)
                world.Enemies.Add(new EnemyState { Position = barracks.Position, Health = 10000 });
            for (int i = 0; i < 20; i++) world.Step(.1f);
            yield return null;
            Assert.AreEqual(ExpansionKind.Empty, barracks.Kind);
            Assert.IsFalse(garrison.activeInHierarchy, "兵营被毁时移除已部署守军");
            yield return new ExitPlayMode();
        }

        private static Vector2Int FindPlacement(WorldState world, ExpansionKind kind, int moving = -1)
        {
            for (int x = -5; x <= -2; x++)
                for (int y = 1; y <= 6; y++)
                {
                    var cell = new Vector2Int(x, y);
                    if (world.PreviewPlacement(kind, cell, false, moving, out _, out _) == PlacementIssue.None)
                        return cell;
                }
            Assert.Fail("找不到可见的合法落位");
            return default;
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
