using System.Collections;
using NUnit.Framework;
using QFramework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
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
                "../../Backup/Artifacts/VictorySupplies-20261108/PlayWallet-" + System.Guid.NewGuid().ToString("N")));
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
            Click("Purchase1");
            Assert.AreEqual(1, run.Levels[1]);
            Assert.AreEqual(beforeUpgrade - cost, run.Scrap);
            Click("Purchase1");
            Assert.AreEqual(1, run.Levels[1], "资源不足时按钮不能改变升级等级");
            Click("OpenTurrets");
            Click("TurretSlot0");
            Click("TurretSlot2");
            Assert.IsNull(GameObject.Find("Turret0"), "迁出后原炮位应为空");
            Assert.IsNotNull(GameObject.Find("Turret2"), "迁入后目标炮位应出现炮塔");
            Click("TurretSlot3");
            Assert.IsNull(GameObject.Find("Turret3"), "库存不足时不能建造");
            Assert.AreEqual(beforeUpgrade - cost, run.Scrap);
            Click("PrimaryAction");
            Click("PrimaryAction");
            Assert.IsFalse(run.Paused);

            int scrap = run.Config.ScrapCostBase * 2 - run.Scrap;
            int energy = run.Config.EnergyCostPerLevel - run.Energy;
            for (int i = 0; i < scrap + energy; i++)
            {
                if (run.CargoScrap + run.CargoEnergy == run.Capacity) run.Deposit();
                Assert.IsTrue(run.Gather(i >= scrap));
            }
            run.Deposit();
            yield return null;
            Click("OpenUpgrade");
            Click("OpenTurrets");
            Click("TurretSlot3");
            Assert.IsNotNull(GameObject.Find("Turret3"), "有足够库存时应在空炮位建造");
            Assert.AreEqual(0, run.Scrap);
            Assert.AreEqual(0, run.Energy);
            Click("PrimaryAction");
            Click("PrimaryAction");

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

        /// <summary>真实 Boot 启动后，用工人取得首笔库存，通过按钮建站并验证自动入库。</summary>
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
            int sector = 0;
            while (sector < world.ExpansionSites.Count && world.FindExpansionResource(sector) < 0) sector++;
            Assert.Less(sector, world.ExpansionSites.Count);
            Click("OpenUpgrade");
            Click("OpenExpansion");
            Click("ExpansionPlotButton" + sector);
            Click("BuildExtractor");
            var site = world.ExpansionSites[sector];
            Assert.AreEqual(ExpansionKind.Extractor, site.Kind);
            Assert.AreEqual(before - spec.Scrap, world.Run.Scrap);
            var model = GameObject.Find("ExpansionSite" + sector).transform.Find("Extractor").gameObject;
            Assert.IsTrue(model.activeInHierarchy);
            var source = world.Nodes[site.NodeIndex];
            int beforeRemaining = source.Remaining;
            world.Step(.1f);
            Assert.AreEqual(beforeRemaining, source.Remaining, "扩建界面暂停生产");
            Click("PrimaryAction");
            Click("PrimaryAction");
            for (int i = 0; i < 30; i++) world.Step(.1f);
            Assert.Less(source.Remaining, beforeRemaining);
            Assert.Greater(world.Run.Scrap + world.Run.Energy, before - spec.Scrap);
            yield return new ExitPlayMode();
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
