using System.Linq;
using Boot;
using Cysharp.Threading.Tasks;
using Game;
using QFramework;
using UnityEngine;
using UnityEngine.UI;

namespace OutpostLoop
{
    /// <summary>临时会话窗口，统一走项目的分层 UI 和安全区。只读取快照、转发操作。</summary>
    public sealed class PrototypeModalCtrl : BaseUICtrl
    {
        private PrototypeController owner;
        private PrototypeOverlayView view;
        private bool wasShowingStart;

        protected override string Path() => null;
        public override UILayer Layer() => UILayer.Dialog;
        protected override BaseUI CreateView() => PrototypeOverlayView.Create("PrototypeModal");
        protected override void OnInit() => view = (PrototypeOverlayView)BaseUI;
        protected override void OnShow() { }
        protected override void OnHide() => wasShowingStart = false;
        protected override void OnUnload() { owner = null; view = null; }

        /// <summary>同一个会话窗口在开局、暂停、升级与结算之间复用。</summary>
        internal void Bind(PrototypeController controller, Font font)
        {
            if (view == null || owner == controller) return;
            owner = controller;
            view.BuildModal(font, controller);
            view.GetComponent<SafeAreaFitter>().ApplyNow();
        }

        internal void Refresh(PrototypeController controller)
        {
            if (owner != controller || !IsShow()) return;
            bool showingStart = controller.Overlay == PrototypeOverlay.Start;
            if (showingStart && !wasShowingStart)
            {
                // 进入开始页时静默检查一次；内置回退保留手动重试入口。
                var updates = Game.Game.Interface.GetSystem<ResourceUpdateSystem>();
                if (updates.Snapshot.ResourceSource != BootResourceSource.BuiltinFallback)
                    updates.CheckWithNoticeAsync(false).Forget();
            }
            wasShowingStart = showingStart;
            view.Present(controller);
        }

        internal void Detach(PrototypeController controller)
        {
            if (owner == controller) Unload();
        }
    }

    /// <summary>指挥 HUD 提供升级、暂停和四向调防入口。</summary>
    public sealed class PrototypeHudCtrl : BaseUICtrl
    {
        private PrototypeController owner;
        // 调防按钮反映当前命令，避免重复下达同一驻防指令。
        private Text[] squadLabels;
        private int displayedSector;
        private string displayedLanguage;
        protected override string Path() => null;
        public override UILayer Layer() => UILayer.HUD;
        public override bool BlocksGame => false;
        protected override BaseUI CreateView() => PrototypeOverlayView.Create("PrototypeHud");
        protected override void OnInit() { }
        protected override void OnShow() { }
        protected override void OnHide() { }
        protected override void OnUnload() { owner = null; squadLabels = null; }

        internal void Bind(PrototypeController controller, Font font)
        {
            if (BaseUI == null || owner == controller) return;
            owner = controller;
            squadLabels = ((PrototypeOverlayView)BaseUI).BuildHudEntries(font, controller);
            displayedLanguage = null;
            BaseUI.GetComponent<SafeAreaFitter>().ApplyNow();
        }

        internal void Refresh(int sector)
        {
            string language = Game.Game.Interface.GetModel<LocalizationModel>().CurrentLanguageCode.Value;
            if (displayedSector == sector && displayedLanguage == language) return;
            displayedSector = sector;
            displayedLanguage = language;
            for (int i = 0; i < squadLabels.Length; i++)
            {
                var label = squadLabels[i];
                string command = Loc.T("StrategyDeploy", Loc.T(PrototypeOverlayView.SectorNames[i]));
                if (label.text != command) label.text = command;
                label.GetComponentInParent<Button>().interactable = i != sector;
            }
        }

        internal void Detach(PrototypeController controller)
        {
            if (owner == controller) Unload();
        }
    }

    /// <summary>独立建造侧栏与指挥 HUD 同层，保持局时和地图输入可用。</summary>
    public sealed class PrototypeConstructionCtrl : BaseUICtrl
    {
        private PrototypeController owner;
        private PrototypeOverlayView view;

        protected override string Path() => null;
        public override UILayer Layer() => UILayer.HUD;
        public override bool BlocksGame => false;
        protected override BaseUI CreateView() => PrototypeOverlayView.Create("PrototypeConstruction");
        protected override void OnInit() => view = (PrototypeOverlayView)BaseUI;
        protected override void OnShow() { }
        protected override void OnHide() { }
        protected override void OnUnload() { owner = null; view = null; }

        internal void Bind(PrototypeController controller, Font font)
        {
            if (view == null || owner == controller) return;
            owner = controller;
            view.BuildConstruction(font, controller);
            view.GetComponent<SafeAreaFitter>().ApplyNow();
        }

        internal void Refresh(PrototypeController controller)
        {
            if (owner == controller && IsShow()) view.PresentConstruction(controller);
        }

        internal void Detach(PrototypeController controller)
        {
            if (owner == controller) Unload();
        }
    }

    /// <summary>临时玩法窗口复用同一套视图工具，展示逻辑不修改玩法状态。</summary>
    internal sealed class PrototypeOverlayView : BaseUI
    {
        private static string[] Routes => new[] { Loc.T("PreviewText15"), Loc.T("PreviewText16"), Loc.T("PreviewText17") };
        private readonly Text[] routeLabels = new Text[Routes.Length];
        private Text heading, details, actionLabel, feedback, startBriefing, turretHint;
        private GameObject purchaseRows, turretRows;
        private readonly Text[] turretLabels = new Text[4];
        // 扩建面板先选方位，再显示该地块的产出预览或现有建筑状态。
        private GameObject expansionRows, expansionChoices;
        private Text constructionStock, constructionFeedback, expansionDetail;
        private Button expansionTab, turretTab;
        private readonly Text[] expansionPlots = new Text[4];
        private readonly Text[] expansionOptions = new Text[3];
        private static readonly ExpansionKind[] ExpansionOptions =
            { ExpansionKind.Extractor, ExpansionKind.Barracks, ExpansionKind.Barricade };
        private static readonly string[] ExpansionNames =
            { "ExpansionEmpty", "ExpansionExtractor", "ExpansionBarracks", "ExpansionBarricade" };
        private static readonly string[] ExpansionHints =
            { "ExpansionExtractorHint", "ExpansionBarracksHint", "ExpansionBarricadeHint" };
        internal static readonly string[] SectorNames = { "TurretWest", "TurretEast", "TurretNorth", "TurretSouth" };
        // 开始页拥有独立构图，局内弹窗保留紧凑的原型布局。
        private RectTransform startStage, sessionPanel;
        private GameObject startScreen;
        private Button startUpdateEntry;
        private Text startUpdateVersion, startUpdateStatus;
        private Text supplyBalance, supplyScrap, supplyEnergy;

        internal static PrototypeOverlayView Create(string name)
        {
            var root = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)root.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            return root.AddComponent<PrototypeOverlayView>();
        }

        internal void BuildModal(Font font, PrototypeController controller)
        {
            BuildStartScreen(font, controller);
            Button(transform, font, "SettingsEntry", Loc.T("SettingsTitle"), new Vector2(180, 48),
                new Vector2(-115, -40), new Vector2(1, 1), () => UIHelper.ShowUI<PrototypeSettingsCtrl>());
            var updateLabel = Button(transform, font, "StartUpdateEntry", Loc.T("SettingsCheck"), new Vector2(270, 48),
                new Vector2(-356, -40), new Vector2(1, 1), OpenUpdateCheck);
            startUpdateEntry = updateLabel.GetComponentInParent<Button>();
            startUpdateVersion = Label(startUpdateEntry.transform, font, "StartUpdateVersion", new Vector2(270, 28),
                new Vector2(0, -42), 16);
            startUpdateStatus = Label(startUpdateEntry.transform, font, "StartUpdateStatus", new Vector2(270, 44),
                new Vector2(0, -75), 16);
            var mark = Label(startUpdateEntry.transform, font, "StartUpdateRedPoint", new Vector2(28, 28),
                new Vector2(119, 16), 24);
            mark.text = "●";
            mark.color = new Color32(255, 96, 80, 255);
            var architecture = Game.Game.Interface;
            var redPoint = mark.gameObject;
            redPoint.SetActive(architecture.GetSystem<RedPointSystem>().GetState(RedPointIds.StartResourceUpdate).Active);
            // 设置窗口打开时底层界面停止逐帧刷新，红点仍随通用事件更新。
            architecture.RegisterEvent<RedPointChangedEvent>(change =>
            {
                if (change.Id == RedPointIds.StartResourceUpdate && redPoint != null)
                    redPoint.SetActive(change.Active);
            }).UnRegisterWhenGameObjectDestroyed(gameObject);
            var panel = Rect("Panel", transform, new Vector2(.5f,.5f), new Vector2(780,530), Vector2.zero);
            sessionPanel = panel;
            panel.gameObject.AddComponent<Image>().color = new Color(.08f,.13f,.14f,.98f);
            heading = Label(panel, font, "Title", new Vector2(680,64), new Vector2(0,205), 40);
            details = Label(panel, font, "Details", new Vector2(670,190), new Vector2(0,34), 29);
            purchaseRows = Rect("PurchaseRows", panel, new Vector2(.5f,.5f), new Vector2(660,300), new Vector2(0,8)).gameObject;
            for (int index = 0; index < Routes.Length; index++)
            {
                int route = index;
                routeLabels[index] = Button(purchaseRows.transform, font, "Purchase" + index, "", new Vector2(620,60),
                    new Vector2(0,102 - index * 70), new Vector2(.5f,.5f), () => controller.PurchaseUpgrade(route));
            }
            feedback = Label(panel, font, "Feedback", new Vector2(660,36), new Vector2(0,-145), 24);
            actionLabel = Button(panel, font, "PrimaryAction", "", new Vector2(450,68), new Vector2(0,-200),
                new Vector2(.5f,.5f), controller.ActOnModal);
        }

        /// <summary>建造侧栏直接挂在局内 HUD，地图和基地升级保持各自入口。</summary>
        internal void BuildConstruction(Font font, PrototypeController controller)
        {
            var panel = Rect("ConstructionPanel", transform, new Vector2(1, .5f), new Vector2(512, 438), new Vector2(-286, -85));
            panel.gameObject.AddComponent<Image>().color = new Color(.08f, .13f, .14f, .98f);
            var title = Label(panel, font, "ConstructionTitle", new Vector2(270, 44), new Vector2(0, 187), 30);
            var localizedTitle = title.gameObject.AddComponent<LocalizedText>();
            localizedTitle.Key = "ConstructionTitle";
            localizedTitle.Refresh();
            var close = Button(panel, font, "CloseConstruction", Loc.T("ConstructionClose"), new Vector2(84, 34),
                new Vector2(192, 187), new Vector2(.5f, .5f), controller.CloseConstruction);
            close.fontSize = 18;
            expansionTab = Button(panel, font, "ConstructionExpansionTab", Loc.T("ExpansionManage"), new Vector2(225, 39),
                new Vector2(-119, 141), new Vector2(.5f, .5f), () => controller.SelectConstructionTab(ConstructionTab.Expansion))
                .GetComponentInParent<Button>();
            turretTab = Button(panel, font, "ConstructionTurretTab", Loc.T("TurretManage"), new Vector2(225, 39),
                new Vector2(119, 141), new Vector2(.5f, .5f), () => controller.SelectConstructionTab(ConstructionTab.Turrets))
                .GetComponentInParent<Button>();
            expansionTab.GetComponentInChildren<Text>().fontSize = 21;
            turretTab.GetComponentInChildren<Text>().fontSize = 21;
            constructionStock = Label(panel, font, "ConstructionStock", new Vector2(480, 30), new Vector2(0, 106), 20);
            constructionFeedback = Label(panel, font, "ConstructionFeedback", new Vector2(480, 30), new Vector2(0, -185), 19);
            turretRows = Rect("TurretRows", panel, new Vector2(.5f,.5f), new Vector2(480,260), Vector2.zero).gameObject;
            turretHint = Label(turretRows.transform, font, "TurretHint", new Vector2(480,44), new Vector2(0,71), 17);
            // 炮位按方位摆放，和场景中的四个位置对应。
            var positions = new[] { new Vector2(-118,-38), new Vector2(118,-38), new Vector2(0,15), new Vector2(0,-91) };
            for (int i = 0; i < turretLabels.Length; i++)
            {
                int slot = i;
                turretLabels[i] = Button(turretRows.transform, font, "TurretSlot" + i, "", new Vector2(218,48),
                    positions[i], new Vector2(.5f,.5f), () => controller.ActOnTurretSlot(slot));
                turretLabels[i].fontSize = 18;
            }
            BuildExpansionRows(panel, font, controller);
        }

        /// <summary>建造侧栏按方位显示四个地块，再列出可建项目。</summary>
        private void BuildExpansionRows(Transform panel, Font font, PrototypeController controller)
        {
            expansionRows = Rect("ExpansionRows", panel, new Vector2(.5f, .5f), new Vector2(480, 285), Vector2.zero).gameObject;
            for (int i = 0; i < expansionPlots.Length; i++)
            {
                int sector = i;
                expansionPlots[i] = Button(expansionRows.transform, font, "ExpansionPlotButton" + i, "", new Vector2(110, 40),
                    new Vector2(-177 + 118 * i, 68), new Vector2(.5f, .5f), () => controller.SelectExpansionSite(sector));
                expansionPlots[i].fontSize = 16;
            }
            expansionDetail = Label(expansionRows.transform, font, "ExpansionDetail", new Vector2(475, 48), new Vector2(0, 21), 18);
            expansionChoices = Rect("ExpansionChoices", expansionRows.transform, new Vector2(.5f, .5f), new Vector2(475, 170), Vector2.zero).gameObject;
            for (int i = 0; i < expansionOptions.Length; i++)
            {
                var kind = ExpansionOptions[i];
                expansionOptions[i] = Button(expansionChoices.transform, font, "Build" + kind, "", new Vector2(468, 46),
                    new Vector2(0, -28 - i * 51), new Vector2(.5f, .5f), () => controller.PurchaseExpansion(kind));
                expansionOptions[i].fontSize = 17;
            }
        }

        /// <summary>在安全区内摆放指挥按钮，防线按钮按下后立即派队。</summary>
        internal Text[] BuildHudEntries(Font font, PrototypeController controller)
        {
            Button(transform, font, "OpenPause", Loc.T("HudPause"), new Vector2(152,88),
                new Vector2(-106,172), new Vector2(1,0), controller.PauseFromHud);
            Button(transform, font, "OpenUpgrade", Loc.T("PreviewText14"), new Vector2(300,64),
                new Vector2(-180,62), new Vector2(1,0), controller.ToggleUpgrade);
            Button(transform, font, "OpenConstruction", Loc.T("ConstructionOpen"), new Vector2(240,64),
                new Vector2(-460,62), new Vector2(1,0), controller.ToggleConstruction);
            var guideSize = new Vector2(652, 80);
            var guide = Rect("CommandGuide", transform, Vector2.zero, guideSize, new Vector2(348, 62));
            guide.gameObject.AddComponent<Image>().color = new Color(.08f, .13f, .14f, .9f);
            var guideText = Label(guide, font, "Controls", guideSize, Vector2.zero, 24).gameObject.AddComponent<LocalizedText>();
            guideText.Key = "HudControls";
            guideText.Refresh();
            var commands = new Text[SectorNames.Length];
            for (int i = 0; i < commands.Length; i++)
            {
                int sector = i;
                var label = Button(transform, font, "DeploySquad" + i, "",
                    new Vector2(154, 54), new Vector2(99 + 166 * i, 145), Vector2.zero, () => controller.DeploySquad(sector));
                label.fontSize = 22;
                commands[i] = label;
            }
            return commands;
        }

        internal void Present(PrototypeController controller)
        {
            var mode = controller.Overlay;
            bool starting = mode == PrototypeOverlay.Start;
            startScreen.SetActive(starting);
            startUpdateEntry.gameObject.SetActive(starting);
            sessionPanel.gameObject.SetActive(!starting);
            if (starting)
            {
                var snapshot = Game.Game.Interface.GetSystem<ResourceUpdateSystem>().Snapshot;
                startUpdateEntry.interactable = snapshot.Status != ResourceUpdateStatus.Checking;
                SetText(startUpdateVersion, PrototypeUpdateText.CurrentVersion(snapshot));
                SetText(startUpdateStatus, PrototypeUpdateText.Status(snapshot));
                if (startBriefing != null) startBriefing.text = Loc.T("PreviewBrief", controller.World.Run.Config.Duration / 60);
                int selected = (controller.WantScrapPack ? 1 : 0) + (controller.WantEnergyPack ? 1 : 0);
                string wallet = Loc.T("SupplyBalance", controller.SupplyCoins, selected * PrototypeWallet.SupplyPrice);
                if (controller.SupplyError != null) wallet += "\n" + Loc.T(controller.SupplyError);
                SetText(supplyBalance, wallet);
                SetText(supplyScrap, SupplyLabel("SupplyScrap", PrototypeWallet.ScrapPack, controller.WantScrapPack));
                SetText(supplyEnergy, SupplyLabel("SupplyEnergy", PrototypeWallet.EnergyPack, controller.WantEnergyPack));
                // 以较窄边为准缩放整幅构图，保住宽屏和较窄窗口中的标题及按钮。
                var viewport = ((RectTransform)transform).rect.size;
                var safe = Screen.safeArea.size;
                if (safe.x > 0 && safe.y > 0 && Screen.width > 0 && Screen.height > 0)
                    viewport = Vector2.Scale(viewport, new Vector2(safe.x / Screen.width, safe.y / Screen.height));
                float scale = Mathf.Min(viewport.x / 1920, viewport.y / 1080);
                if (scale > 0) startStage.localScale = Vector3.one * scale;
                return;
            }

            var run = controller.World.Run;
            bool upgrading = mode == PrototypeOverlay.Upgrade;
            purchaseRows.SetActive(upgrading);
            feedback.gameObject.SetActive(upgrading);
            details.gameObject.SetActive(!upgrading);
            switch (mode)
            {
                case PrototypeOverlay.Paused:
                    SetText(heading, Loc.T("PreviewText0"));
                    SetText(details, Loc.T("PreviewText1"));
                    SetText(actionLabel, Loc.T("PreviewText2"));
                    break;
                case PrototypeOverlay.Upgrade:
                    SetText(feedback, controller.ActionFeedback);
                    SetText(heading, Loc.T("PreviewText3"));
                    SetText(actionLabel, Loc.T("PreviewText4"));
                    for (int i = 0; i < Routes.Length; i++)
                    {
                        var cost = run.Levels[i] >= run.Config.MaxLevel ? Loc.T("PreviewText5") : Loc.T("PreviewCost", run.ScrapCost(i), run.EnergyCost(i));
                        SetText(routeLabels[i], $"{Routes[i]}   {run.Levels[i]} / {run.Config.MaxLevel}   {cost}");
                    }
                    break;
                case PrototypeOverlay.Result:
                    SetText(heading, run.Outcome > 0 ? Loc.T("PreviewText6") : Loc.T("PreviewText7"));
                    string result = Loc.T("PreviewResult", Mathf.FloorToInt(run.Elapsed), controller.World.Kills);
                    bool pendingReward = run.Outcome > 0 && !controller.VictoryRewardRecorded;
                    if (run.Outcome > 0)
                        result += "\n" + (pendingReward ? Loc.T("SupplyRewardPending")
                            : Loc.T("SupplyReward", PrototypeWallet.VictoryCoins, controller.SupplyCoins));
                    SetText(details, result);
                    SetText(actionLabel, Loc.T(pendingReward ? "SupplyRetryReward" : "SupplyNextRun"));
                    break;
            }
        }

        private static string SupplyLabel(string key, int amount, bool selected)
        {
            string label = Loc.T(key, amount, PrototypeWallet.SupplyPrice);
            return selected ? Loc.T("SupplySelected", label) : label;
        }

        /// <summary>持续显示实时库存和建筑状态，不冻结战斗或采运。</summary>
        internal void PresentConstruction(PrototypeController controller)
        {
            var run = controller.World.Run;
            bool turrets = controller.BuildTab == ConstructionTab.Turrets;
            expansionTab.interactable = turrets;
            turretTab.interactable = !turrets;
            turretRows.SetActive(turrets);
            expansionRows.SetActive(!turrets);
            SetText(constructionStock, Loc.T("HudStock", run.Scrap, run.Energy));
            SetText(constructionFeedback, controller.ActionFeedback);
            if (turrets) PresentTurrets(controller);
            else PresentExpansion(controller);
        }

        /// <summary>按炮位占用和选择状态刷新建造或迁移文案。</summary>
        private void PresentTurrets(PrototypeController controller)
        {
            var world = controller.World;
            bool moving = controller.SelectedTurretSlot >= 0;
            SetText(turretHint, Loc.T(moving ? "TurretChooseEmpty" : "TurretInstructions"));
            for (int i = 0; i < turretLabels.Length; i++)
            {
                var key = world.Turrets[i].Occupied
                    ? (controller.SelectedTurretSlot == i ? "TurretSelected" : "TurretOccupied")
                    : (moving ? "TurretMoveHere" : "TurretBuildHere");
                SetText(turretLabels[i], Loc.T(key, Loc.T(SectorNames[i]), world.TurretScrapCost, world.TurretEnergyCost));
            }
        }

        /// <summary>预览最近可用资源、建造价格以及已建建筑的运行状态。</summary>
        private void PresentExpansion(PrototypeController controller)
        {
            var world = controller.World;
            int selected = controller.SelectedExpansionSite;
            var site = world.ExpansionSites[selected];
            bool vacant = site.Kind == ExpansionKind.Empty;
            for (int i = 0; i < expansionPlots.Length; i++)
            {
                string name = Loc.T(ExpansionNames[(int)world.ExpansionSites[i].Kind]);
                string plot = Loc.T("ExpansionPlot", Loc.T(SectorNames[i]), name);
                SetText(expansionPlots[i], i == selected ? Loc.T("ExpansionSelected", plot) : plot);
            }
            expansionChoices.SetActive(vacant);
            int node = vacant ? world.FindExpansionResource(selected) : site.NodeIndex;
            string resource = node >= 0
                ? Loc.T("ResourceMarkerRemaining", Loc.T(world.Nodes[node].Energy ? "ResourceMarkerEnergy" : "ResourceMarkerScrap"),
                    world.Nodes[node].Remaining)
                : Loc.T("ExpansionNoResource");
            if (!vacant)
            {
                string status = site.Kind switch
                {
                    ExpansionKind.Extractor => resource,
                    ExpansionKind.Barracks => site.TrainingLeft > 0
                        ? Loc.T("ExpansionTraining", Mathf.CeilToInt(site.TrainingLeft))
                        : Loc.T("ExpansionGarrisonReady"),
                    _ => Loc.T("ExpansionBarricadeHint")
                };
                SetText(expansionDetail, Loc.T("ExpansionStatus", Loc.T(ExpansionNames[(int)site.Kind]), site.Health,
                    ExpansionSite.SpecFor(site.Kind).Health, status));
                return;
            }
            SetText(expansionDetail, Loc.T("ExpansionResourcePreview", resource));
            for (int i = 0; i < expansionOptions.Length; i++)
            {
                var kind = ExpansionOptions[i];
                var spec = ExpansionSite.SpecFor(kind);
                SetText(expansionOptions[i], Loc.T("ExpansionBuildOption", Loc.T(ExpansionNames[(int)kind]),
                    Loc.T("PreviewCost", spec.Scrap, spec.Energy), Loc.T(ExpansionHints[i], spec.Cycle)));
                expansionOptions[i].GetComponentInParent<Button>().interactable = kind != ExpansionKind.Extractor || node >= 0;
            }
        }

        /// <summary>复用设置窗口的公告与重启交互，打开时直接定位更新检查。</summary>
        private static void OpenUpdateCheck()
        {
            if (UIHelper.GetUICtrl<PrototypeSettingsCtrl>()?.IsShow() == true ||
                UIHelper.GetUICtrl<PrototypeLogCtrl>()?.IsShow() == true) return;
            UIHelper.ShowUI<PrototypeSettingsCtrl>().CheckUpdateFromStart();
        }

        /// <summary>用已确认的构图搭建开始页，基地画面跟随 Main 场景引用。</summary>
        private void BuildStartScreen(Font font, PrototypeController controller)
        {
            var ink = new Color32(244, 238, 224, 255);
            var mint = new Color32(147, 214, 189, 255);
            var muted = new Color32(170, 190, 177, 255);
            var line = new Color32(60, 83, 73, 255);
            startScreen = new GameObject("StartScreenBackground", typeof(RectTransform));
            startScreen.transform.SetParent(transform, false);
            var backgroundRect = (RectTransform)startScreen.transform;
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = backgroundRect.offsetMax = Vector2.zero;
            startScreen.AddComponent<Image>().color = new Color32(16, 26, 24, 255);
            startStage = Rect("StartLayout", backgroundRect, new Vector2(.5f,.5f), new Vector2(1920,1080), Vector2.zero);
            StartText(font, "Brand", "OUTPOST LOOP", 96, 55, 420, 48, 23, mint, FontStyle.Bold);
            StartText(font, "Brief", Loc.T("PreviewText9"), 1670, 55, 160, 48, 22, mint, alignment: TextAnchor.MiddleRight);
            Fill("HeaderRule", 96, 122, 1728, 2, line);
            StartText(font, "Operations", "F I E L D   O P E R A T I O N S", 96, 230, 630, 40, 19, mint);
            var latinFont = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            StartText(latinFont, "TitleOutpost", "OUTPOST", 88, 268, 705, 150, 123, ink, FontStyle.Bold);
            StartText(latinFont, "TitleLoop", "LOOP", 89, 386, 640, 150, 123, mint, FontStyle.Bold);
            StartText(font, "GameLoop", Loc.T("PreviewText10"), 96, 555, 650, 44, 26, muted);

            Fill("ArtFrame", 796, 160, 1028, 798, line);
            var hero = StartRect("BaseArtwork", 798, 162, 1024, 794).gameObject.AddComponent<RawImage>();
            hero.texture = controller.StartArtwork;
            hero.color = hero.texture != null ? new Color(.84f, .89f, .85f) : (Color)new Color32(35, 57, 48, 255);
            hero.raycastTarget = false;
            // 按面板比例裁取中央构图，并避开原图底边的说明。
            if (hero.texture != null)
            {
                float cropWidth = 1024f / 794f * .90f * hero.texture.height / hero.texture.width;
                hero.uvRect = new Rect((1 - cropWidth) / 2, .06f, cropWidth, .90f);
            }
            Fill("SectorPlate", 833, 198, 166, 47, new Color32(21, 39, 33, 230));
            StartText(font, "Sector", "SECTOR 01", 850, 198, 140, 47, 19, mint, FontStyle.Bold);
            Fill("ArtworkCaptionPlate", 798, 883, 1024, 73, new Color32(17, 30, 25, 218));
            StartText(font, "ArtworkCaption", Loc.T("PreviewText11"), 833, 891, 420, 50, 22, ink);

            Fill("BriefingRule", 96, 644, 650, 2, line);
            string briefing = Loc.T("PreviewBrief", controller.World.Run.Config.Duration / 60);
            StartText(font, "Briefing", briefing, 96, 660, 650, 94, 27, ink);
            startBriefing = startStage.Find("Briefing").GetComponent<Text>();
            var action = Button(startStage, font, "PrimaryAction", Loc.T("PreviewText12"), new Vector2(390, 77),
                new Vector2(291, -825), new Vector2(0, 1), controller.ActOnModal);
            action.color = new Color32(15, 32, 26, 255);
            action.fontStyle = FontStyle.Bold;
            action.transform.parent.GetComponent<Image>().color = mint;
            supplyBalance = Label(StartRect("SupplyBalance", 96, 867, 650, 49), font, "Label",
                new Vector2(650, 49), Vector2.zero, 21);
            supplyBalance.alignment = TextAnchor.MiddleLeft;
            supplyScrap = Button(startStage, font, "SupplyScrap", "", new Vector2(310, 52),
                new Vector2(251, -950), new Vector2(0, 1), () => controller.ToggleSupply(false));
            supplyEnergy = Button(startStage, font, "SupplyEnergy", "", new Vector2(310, 52),
                new Vector2(576, -950), new Vector2(0, 1), () => controller.ToggleSupply(true));
            supplyScrap.fontSize = supplyEnergy.fontSize = 20;
            Fill("FooterRule", 96, 992, 1728, 2, line);
            StartText(font, "Controls", Loc.T("PreviewText13"), 96, 1003, 800, 50, 20, muted);
        }

        // 开始页按 1920×1080 的左上角坐标布置，整页随安全区缩放。
        private RectTransform StartRect(string name, float x, float y, float width, float height) =>
            Rect(name, startStage, new Vector2(0, 1), new Vector2(width, height), new Vector2(x + width / 2, -y - height / 2));

        private void Fill(string name, float x, float y, float width, float height, Color color)
        {
            var image = StartRect(name, x, y, width, height).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private void StartText(Font font, string name, string text, float x, float y, float width, float height, int size,
            Color color, FontStyle style = FontStyle.Normal, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var label = StartRect(name, x, y, width, height).gameObject.AddComponent<Text>();
            label.font = font;
            label.text = text;
            var key = LocalizationLanguages.All.Count > 0
                ? Game.Game.Interface.GetUtility<LocalizationUtility>().GetKeys(Game.Game.Interface.GetModel<LocalizationModel>().CurrentLanguageCode.Value)
                    .FirstOrDefault(candidate => Loc.T(candidate) == text)
                : null;
            if (key != null) label.gameObject.AddComponent<LocalizedText>().Key = key;
            label.fontSize = size;
            label.color = color;
            label.fontStyle = style;
            label.alignment = alignment;
            label.raycastTarget = false;
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 size, Vector2 offset)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.sizeDelta = size;
            rect.anchoredPosition = offset;
            return rect;
        }

        private static Text Label(Transform parent, Font font, string name, Vector2 size, Vector2 offset, int fontSize)
        {
            var label = Rect(name, parent, new Vector2(.5f,.5f), size, offset).gameObject.AddComponent<Text>();
            label.font = font;
            label.fontSize = fontSize;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(.92f,.97f,.97f);
            label.raycastTarget = false;
            return label;
        }

        private static Text Button(Transform parent, Font font, string name, string text, Vector2 size, Vector2 offset,
            Vector2 anchor, UnityEngine.Events.UnityAction onClick)
        {
            var rect = Rect(name, parent, anchor, size, offset);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(.23f,.38f,.38f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);
            var label = Label(rect, font, "Label", size, Vector2.zero, 30);
            label.text = text;
            var key = LocalizationLanguages.All.Count > 0
                ? Game.Game.Interface.GetUtility<LocalizationUtility>().GetKeys(Game.Game.Interface.GetModel<LocalizationModel>().CurrentLanguageCode.Value)
                    .FirstOrDefault(candidate => Loc.T(candidate) == text)
                : null;
            if (key != null) label.gameObject.AddComponent<LocalizedText>().Key = key;
            return label;
        }

        private static void SetText(Text label, string text)
        {
            if (label.text != text) label.text = text;
        }
    }
}
