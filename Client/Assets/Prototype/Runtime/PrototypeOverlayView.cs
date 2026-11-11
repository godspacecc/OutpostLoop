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
        private bool displayedBase, displayedSquad;
        private Button upgradeButton, researchButton, returnButton;
        private Text returnPreview;
        private GameObject expeditionGuide;
        private LocalizedText commandGuide;
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
            upgradeButton = BaseUI.transform.Find("OpenUpgrade").GetComponent<Button>();
            researchButton = BaseUI.transform.Find("OpenResearch").GetComponent<Button>();
            returnButton = BaseUI.transform.Find("OpenReturn").GetComponent<Button>();
            returnPreview = BaseUI.transform.Find("ReturnPreview").GetComponent<Text>();
            expeditionGuide = BaseUI.transform.Find("ExpeditionGuide").gameObject;
            commandGuide = BaseUI.transform.Find("CommandGuide/Controls").GetComponent<LocalizedText>();
            displayedLanguage = null;
            BaseUI.GetComponent<SafeAreaFitter>().ApplyNow();
        }

        internal void Refresh(bool hasBase, SitePersonnel squad)
        {
            bool exploring = owner.World.Expedition != null;
            upgradeButton.gameObject.SetActive(!exploring);
            researchButton.gameObject.SetActive(exploring);
            returnButton.gameObject.SetActive(exploring);
            researchButton.interactable = exploring && owner.World.NearShelter;
            returnButton.interactable = owner.World.Expedition?.BasicComplete == true;
            returnPreview.gameObject.SetActive(returnButton.interactable);
            if (returnButton.interactable)
            {
                var expedition = owner.World.Expedition;
                string preview = $"{expedition.Rules.BasicFunds} 资金 · {ExpeditionState.DescribeTech(expedition.NewTech)}";
                if (returnPreview.text != preview) returnPreview.text = preview;
            }
            BaseUI.transform.Find("CommandGuide").gameObject.SetActive(!exploring);
            expeditionGuide.SetActive(exploring && !owner.ExpeditionIntroDismissed);
            string guide = owner.TouchScoutingAvailable && UnityEngine.InputSystem.Touchscreen.current != null
                ? "TouchScoutGuide" : "HudControls";
            if (commandGuide.Key != guide)
            {
                commandGuide.Key = guide;
                commandGuide.Refresh();
            }
            string language = Game.Game.Interface.GetModel<LocalizationModel>().CurrentLanguageCode.Value;
            int sector = squad?.Sector ?? -1;
            bool hasSquad = squad != null;
            if (displayedSector == sector && displayedLanguage == language && displayedBase == hasBase && displayedSquad == hasSquad) return;
            displayedSector = sector;
            displayedLanguage = language;
            displayedBase = hasBase;
            displayedSquad = hasSquad;
            upgradeButton.interactable = hasBase;
            for (int i = 0; i < squadLabels.Length; i++)
            {
                var label = squadLabels[i];
                var button = label.transform.parent.GetComponent<Button>();
                button.gameObject.SetActive(hasSquad);
                string command = Loc.T("StrategyDeploy", Loc.T(PrototypeOverlayView.SectorNames[i]));
                if (label.text != command) label.text = command;
                button.interactable = i != sector;
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
    internal sealed partial class PrototypeOverlayView : BaseUI
    {
        private static readonly Color ButtonColor = new Color(.23f, .38f, .38f);
        private static readonly Color LabelColor = new Color(.92f, .97f, .97f);
        private static string[] Routes => new[] { Loc.T("PreviewText15"), Loc.T("PreviewText16"), Loc.T("PreviewText17") };
        private readonly Text[] routeLabels = new Text[Routes.Length];
        private Text heading, details, actionLabel, feedback, restartAction;
        private GameObject purchaseRows;
        private Text constructionStock, constructionFeedback, constructionDetail, producePersonnel, moveTurret;
        // 选址时和生产、迁移互斥，共用侧栏操作位置。
        private Text cancelPlacementButton;
        private readonly Text[] constructionOptions = new Text[5];
        private static readonly ExpansionKind[] ExpansionOptions =
            { ExpansionKind.Headquarters, ExpansionKind.Extractor, ExpansionKind.Barracks, ExpansionKind.Barricade, ExpansionKind.Turret };
        private static readonly string[] ExpansionNames =
            { "ExpansionEmpty", "ExpansionExtractor", "ExpansionBarracks", "ExpansionBarricade", "BuildingTurret", "BuildingHeadquarters" };

        internal static string BuildingName(ExpansionKind kind) => kind == ExpansionKind.Shelter ? "庇护所" : Loc.T(ExpansionNames[(int)kind]);
        internal static readonly string[] SectorNames = { "TurretWest", "TurretEast", "TurretNorth", "TurretSouth" };
        // 开始页拥有独立构图，局内弹窗保留紧凑的原型布局。
        private RectTransform startStage, sessionPanel;
        private GameObject startScreen;
        private Button startUpdateEntry;
        private Text startUpdateVersion, startUpdateStatus;

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
            restartAction = Button(panel, font, "RestartRun", "", new Vector2(390, 48),
                new Vector2(0, -117), new Vector2(.5f, .5f), controller.RequestRestart);
            BuildExpeditionModal(panel, font, controller);
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
            constructionStock = Label(panel, font, "ConstructionStock", new Vector2(480, 28), new Vector2(0, 148), 20);
            for (int i = 0; i < constructionOptions.Length; i++)
            {
                var kind = ExpansionOptions[i];
                constructionOptions[i] = Button(panel, font, "Build" + kind, "", new Vector2(468, 39),
                    new Vector2(0, 110 - Mathf.Max(0, i - 1) * 44), new Vector2(.5f, .5f), () => controller.SelectPlacement(kind));
                constructionOptions[i].fontSize = 18;
            }
            shelterOption = Button(panel, font, "BuildShelter", "", new Vector2(468, 39),
                new Vector2(0, 110), new Vector2(.5f, .5f), () => controller.SelectPlacement(ExpansionKind.Shelter));
            shelterOption.fontSize = 18;
            constructionDetail = Label(panel, font, "ConstructionDetail", new Vector2(478, 66), new Vector2(0, -86), 18);
            producePersonnel = Button(panel, font, "ProducePersonnel", "", new Vector2(468, 38),
                new Vector2(0, -143), new Vector2(.5f, .5f), controller.OrderPersonnel);
            moveTurret = Button(panel, font, "MoveSelectedTurret", "", new Vector2(468, 38),
                new Vector2(0, -143), new Vector2(.5f, .5f), controller.BeginTurretMove);
            cancelPlacementButton = Button(panel, font, "CancelPlacement", "", new Vector2(468, 38),
                new Vector2(0, -143), new Vector2(.5f, .5f), controller.CancelPlacementFromHud);
            dismantleBarricade = Button(panel, font, "DismantleBarricade", "", new Vector2(468, 38),
                new Vector2(0, -143), new Vector2(.5f, .5f), controller.DismantleBarricade);
            producePersonnel.fontSize = moveTurret.fontSize = cancelPlacementButton.fontSize = 18;
            dismantleBarricade.fontSize = 18;
            constructionFeedback = Label(panel, font, "ConstructionFeedback", new Vector2(478, 32), new Vector2(0, -186), 19);
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
            Button(transform, font, "OpenResearch", "研究  T", new Vector2(140,64),
                new Vector2(-247,62), new Vector2(1,0), controller.ToggleResearch);
            Button(transform, font, "OpenReturn", "回归", new Vector2(128,64),
                new Vector2(-101,62), new Vector2(1,0), controller.ConfirmReturn);
            var returnNote = Rect("ReturnPreview", transform, new Vector2(1, 0), new Vector2(460, 34), new Vector2(-264, 110));
            var returnText = returnNote.gameObject.AddComponent<Text>();
            returnText.font = font;
            returnText.fontSize = 18;
            returnText.resizeTextForBestFit = true;
            returnText.resizeTextMinSize = 13;
            returnText.resizeTextMaxSize = 18;
            returnText.alignment = TextAnchor.MiddleRight;
            returnText.raycastTarget = false;
            var guideSize = new Vector2(652, 94);
            var guide = Rect("CommandGuide", transform, Vector2.zero, guideSize, new Vector2(348, 60));
            guide.gameObject.AddComponent<Image>().color = new Color(.08f, .13f, .14f, .9f);
            var guideText = Label(guide, font, "Controls", guideSize, Vector2.zero, 24).gameObject.AddComponent<LocalizedText>();
            guideText.Key = "HudControls";
            guideText.Refresh();
            var expeditionGuide = Rect("ExpeditionGuide", transform, Vector2.zero, new Vector2(652, 116), new Vector2(348, 71));
            expeditionGuide.gameObject.AddComponent<Image>().color = new Color(.08f, .13f, .14f, .9f);
            Label(expeditionGuide, font, "Controls", new Vector2(546, 112), new Vector2(-34, 0), 18).text =
                "右键移动 · E 采集或交互 · V 建造 · T 研究\n方向键平移镜头 · 中键旋转 · 滚轮缩放 · H 回焦点\n两指拖动平移 · 张开拉近 · 捏合拉远\n基础任务完成后可回归";
            Button(expeditionGuide, font, "DismissExpeditionIntro", "关闭", new Vector2(78, 42), new Vector2(272, 0),
                new Vector2(.5f, .5f), controller.DismissExpeditionIntro).fontSize = 18;
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
                PresentDeparture(controller);
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
            researchRows.SetActive(mode == PrototypeOverlay.Research);
            cancelReturn.transform.parent.gameObject.SetActive(mode == PrototypeOverlay.ReturnConfirm);
            feedback.gameObject.SetActive(upgrading);
            details.gameObject.SetActive(!upgrading);
            restartAction.transform.parent.gameObject.SetActive(mode == PrototypeOverlay.Paused);
            if (controller.World.Expedition != null)
            {
                PresentExpeditionModal(controller);
                return;
            }
            switch (mode)
            {
                case PrototypeOverlay.Paused:
                    SetText(heading, Loc.T("PreviewText0"));
                    SetText(details, Loc.T(controller.ConfirmingRestart ? "ConfirmRestartDetail" : "PreviewText1"));
                    SetText(restartAction, Loc.T(controller.ConfirmingRestart ? "ConfirmRestartAction" : "RestartRun"));
                    SetText(actionLabel, Loc.T("PreviewText2"));
                    break;
                case PrototypeOverlay.Upgrade:
                    SetText(feedback, controller.ActionFeedback);
                    SetText(heading, Loc.T("PreviewText3"));
                    SetText(actionLabel, Loc.T("PreviewText4"));
                    for (int i = 0; i < Routes.Length; i++)
                    {
                        var cost = run.Levels[i] >= run.Config.MaxLevel ? Loc.T("PreviewText5") : Loc.T("FundsCost", run.UpgradeCost(i));
                        SetText(routeLabels[i], $"{Routes[i]}   {run.Levels[i]} / {run.Config.MaxLevel}   {cost}");
                    }
                    break;
                case PrototypeOverlay.Result:
                    SetText(heading, run.Outcome > 0 ? Loc.T("PreviewText6") : Loc.T("PreviewText7"));
                    string result = Loc.T("BattleResultKills", controller.World.Kills);
                    bool pendingReward = run.Outcome > 0 && !controller.VictoryRewardRecorded;
                    if (run.Outcome > 0)
                        result += "\n" + (pendingReward ? Loc.T("WinSavePending") :
                            Loc.T("VictoryFinalWave") + "\n" + Loc.T("StartingFundsTier", controller.StartingFunds) + " · " + TierProgress(controller));
                    if (run.Outcome < 0) result += "\n" + Loc.T("DefeatBaseLost");
                    SetText(details, result);
                    SetText(actionLabel, Loc.T(pendingReward ? "SupplyRetryReward" : "SupplyNextRun"));
                    break;
            }
        }

        private static string TierProgress(PrototypeController controller) => controller.Wins >= 3
            ? Loc.T("FundsTierMax")
            : Loc.T("FundsTierNext", controller.Wins, controller.Wins == 0 ? 1 : 3, controller.Wins == 0 ? 900 : 1000);

        /// <summary>侧栏显示实时库存、鼠标落位原因及选中建筑的生产状态。</summary>
        internal void PresentConstruction(PrototypeController controller)
        {
            var world = controller.World;
            var run = world.Run;
            bool exploring = world.Expedition != null;
            SetText(constructionStock, exploring ? $"物资 {run.Funds}" : Loc.T("FundsStock", run.Funds));
            SetText(constructionFeedback, controller.ActionFeedback);
            shelterOption.transform.parent.gameObject.SetActive(exploring);
            if (exploring) PresentExpeditionBuilding(controller, shelterOption, ExpansionKind.Shelter);
            for (int i = 0; i < constructionOptions.Length; i++)
            {
                var kind = ExpansionOptions[i];
                var option = constructionOptions[i];
                bool isBase = kind == ExpansionKind.Headquarters;
                bool expeditionOption = kind == ExpansionKind.Turret || kind == ExpansionKind.Extractor || kind == ExpansionKind.Barricade;
                option.transform.parent.gameObject.SetActive(exploring ? expeditionOption : isBase != world.Headquarters.HasValue);
                if (exploring)
                {
                    if (expeditionOption) PresentExpeditionBuilding(controller, option, kind);
                    continue;
                }
                ((RectTransform)option.transform.parent).anchoredPosition = new Vector2(0, 110 - Mathf.Max(0, i - 1) * 44);
                var spec = ExpansionSite.SpecFor(kind);
                string label = isBase ? Loc.T("BuildHeadquartersOption") :
                    Loc.T("BuildingOption", BuildingName(kind), Loc.T("FundsCost", spec.Cost), spec.BuildSeconds);
                bool selected = controller.PlacementKind == kind && controller.MovingTurret < 0;
                SetText(option, selected ? Loc.T("ExpansionSelected", label) : label);
                SetAffordable(option, run.CanAfford(spec.Cost));
            }

            var site = controller.SelectedBuilding < 0 ? null : world.Buildings[controller.SelectedBuilding];
            bool selectedSite = !controller.HasPlacement && site != null && site.Kind != ExpansionKind.Empty;
            bool canProduce = !exploring && selectedSite && site.CanProduce;
            bool canMove = !exploring && selectedSite && site.Kind == ExpansionKind.Turret && !site.IsUnderConstruction;
            producePersonnel.transform.parent.gameObject.SetActive(canProduce);
            moveTurret.transform.parent.gameObject.SetActive(canMove);
            bool canDismantle = exploring && selectedSite && site.Kind == ExpansionKind.Barricade;
            dismantleBarricade.transform.parent.gameObject.SetActive(canDismantle);
            if (canDismantle) SetText(dismantleBarricade, controller.ConfirmingDismantle ? "确认拆除 · 无返还" : "拆除木桩拒马");
            if (canProduce)
            {
                SetText(producePersonnel, Loc.T(site.Kind == ExpansionKind.Extractor ? "ProduceGatherer" : "ProduceGuard",
                    ExpansionSite.PersonnelCost, site.ProductionDuration));
                SetAffordable(producePersonnel, run.CanAfford(ExpansionSite.PersonnelCost));
            }
            if (canMove) SetText(moveTurret, Loc.T("MoveSelectedTurret"));
            bool touchScouting = controller.TouchScoutingAvailable && UnityEngine.InputSystem.Touchscreen.current != null;
            bool canCancelOnTouch = touchScouting && controller.HasPlacement;
            cancelPlacementButton.transform.parent.gameObject.SetActive(canCancelOnTouch);
            if (canCancelOnTouch) SetText(cancelPlacementButton, Loc.T("TouchCancelPlacement"));

            if (controller.HasPlacement)
            {
                string instruction = Loc.T(touchScouting ? "TouchPlacementControls" :
                    controller.PlacementKind == ExpansionKind.Barricade ? "PlacementRotate" : "PlacementControls");
                if (controller.HoverIssue.HasValue) instruction += "\n" + controller.PlacementMessage(controller.HoverIssue.Value);
                if (controller.HoverResource >= 0) instruction += "\n" + controller.ResourceStatus(controller.HoverResource);
                SetText(constructionDetail, instruction);
                return;
            }
            if (!selectedSite)
            {
                SetText(constructionDetail, exploring ? "选建筑后左键落位，右键或 Esc 取消" :
                    Loc.T(world.Headquarters.HasValue ? "PlacementGuide" : "HeadquartersGuide"));
                return;
            }
            string status = site.IsUnderConstruction ? Loc.T("ConstructionRemaining", Mathf.CeilToInt(site.ConstructionLeft))
                : site.ProductionLeft > 0 ? Loc.T("ProductionRemaining", Mathf.CeilToInt(site.ProductionLeft))
                : site.PersonnelWaiting ? Loc.T("PersonnelExitBlocked")
                : site.Kind switch
                {
                    ExpansionKind.Extractor => (site.NodeIndex < 0 ? Loc.T("MineUnavailable") : controller.ResourceStatus(site.NodeIndex)) +
                        (site.Personnel != null ? "\n" + Loc.T("GathererCargo", site.Personnel.Cargo, run.Capacity) : ""),
                    ExpansionKind.Barracks => Loc.T(site.Personnel != null ? "ExpansionGarrisonReady" : "ExpansionGarrisonIdle"),
                    ExpansionKind.Barricade => Loc.T("ExpansionBarricadeHint"),
                    ExpansionKind.Shelter => "在 3 米内按 T 打开研究",
                    _ => Loc.T("TurretOperational")
                };
            SetText(constructionDetail, Loc.T("ExpansionStatus",
                exploring ? ExpeditionBuildingName(site.Kind) : BuildingName(site.Kind), site.Health, site.HealthLimit, status));
        }


        /// <summary>资金不足时禁用实际按钮，价格仍然可读。</summary>
        private static void SetAffordable(Text label, bool affordable)
        {
            var button = label.transform.parent.GetComponent<Button>();
            if (button.interactable == affordable) return;
            button.interactable = affordable;
            button.image.color = affordable ? ButtonColor : new Color(.36f, .36f, .36f);
            label.color = affordable ? LabelColor : new Color(.65f, .65f, .65f);
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
            BuildDeparture(font, controller, ink, mint, muted, line);

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
            var harbor = Button(startStage, font, "StartOldHarbor", "旧港河堤 · 守城试玩", new Vector2(326, 52),
                new Vector2(1623, -918), new Vector2(0, 1), controller.StartOldHarbor);
            harbor.fontSize = 24;

            Fill("FooterRule", 96, 992, 1728, 2, line);
            StartText(font, "Controls", Loc.T("CommanderControls"), 96, 1003, 800, 50, 20, muted);
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
            label.color = LabelColor;
            label.raycastTarget = false;
            return label;
        }

        private static Text Button(Transform parent, Font font, string name, string text, Vector2 size, Vector2 offset,
            Vector2 anchor, UnityEngine.Events.UnityAction onClick)
        {
            var rect = Rect(name, parent, anchor, size, offset);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = ButtonColor;
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
