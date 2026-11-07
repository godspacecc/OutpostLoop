using Game;
using UnityEngine;
using UnityEngine.UI;

namespace OutpostLoop
{
    /// <summary>临时会话窗口，统一走项目的分层 UI 和安全区。只读取快照、转发操作。</summary>
    public sealed class PrototypeModalCtrl : BaseUICtrl
    {
        private PrototypeController owner;
        private PrototypeOverlayView view;

        protected override string Path() => null;
        public override UILayer Layer() => UILayer.Dialog;
        protected override BaseUI CreateView() => PrototypeOverlayView.Create("PrototypeModal");
        protected override void OnInit() => view = (PrototypeOverlayView)BaseUI;
        protected override void OnShow() { }
        protected override void OnHide() { }
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
            if (owner == controller && IsShow()) view.Present(controller);
        }

        internal void Detach(PrototypeController controller)
        {
            if (owner == controller) Unload();
        }
    }

    /// <summary>基地附近显示的升级入口；它本身不阻断玩法。</summary>
    public sealed class PrototypeUpgradeButtonCtrl : BaseUICtrl
    {
        private PrototypeController owner;
        protected override string Path() => null;
        public override UILayer Layer() => UILayer.HUD;
        public override bool BlocksGame => false;
        protected override BaseUI CreateView() => PrototypeOverlayView.Create("PrototypeUpgradeEntry");
        protected override void OnInit() { }
        protected override void OnShow() { }
        protected override void OnHide() { }
        protected override void OnUnload() => owner = null;

        internal void Bind(PrototypeController controller, Font font)
        {
            if (BaseUI == null || owner == controller) return;
            owner = controller;
            ((PrototypeOverlayView)BaseUI).BuildUpgradeEntry(font, controller.ToggleUpgrade);
            BaseUI.GetComponent<SafeAreaFitter>().ApplyNow();
        }

        internal void Detach(PrototypeController controller)
        {
            if (owner == controller) Unload();
        }
    }

    /// <summary>开始页与局内弹窗复用会话控制器，展示逻辑不修改玩法状态。</summary>
    internal sealed class PrototypeOverlayView : BaseUI
    {
        private static readonly string[] Routes = { "采集 / 运输", "炮塔火力 / 射程", "基地耐久 / 修复" };
        private readonly Text[] routeLabels = new Text[Routes.Length];
        private Text heading, details, actionLabel, feedback;
        private GameObject purchaseRows;
        // 开始页拥有独立构图，局内弹窗保留紧凑的原型布局。
        private RectTransform startStage, sessionPanel;
        private GameObject startScreen;

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
            feedback = Label(purchaseRows.transform, font, "Feedback", new Vector2(640,48), new Vector2(0,-108), 28);
            actionLabel = Button(panel, font, "PrimaryAction", "", new Vector2(450,68), new Vector2(0,-200),
                new Vector2(.5f,.5f), controller.ActOnModal);
        }

        internal void BuildUpgradeEntry(Font font, UnityEngine.Events.UnityAction onClick)
        {
            Button(transform, font, "OpenUpgrade", "打开基地升级 [B]", new Vector2(450,72),
                new Vector2(-255,72), new Vector2(1,0), onClick);
        }

        internal void Present(PrototypeController controller)
        {
            var mode = controller.Overlay;
            bool starting = mode == PrototypeOverlay.Start;
            startScreen.SetActive(starting);
            sessionPanel.gameObject.SetActive(!starting);
            if (starting)
            {
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
            details.gameObject.SetActive(!upgrading);
            switch (mode)
            {
                case PrototypeOverlay.Paused:
                    SetText(heading, "已暂停");
                    SetText(details, "本局计时与战斗已暂停。");
                    SetText(actionLabel, "继续 [ESC]");
                    break;
                case PrototypeOverlay.Upgrade:
                    SetText(heading, "基地升级 · 局时已暂停");
                    SetText(actionLabel, "返回探索 [B]");
                    for (int i = 0; i < Routes.Length; i++)
                    {
                        var cost = run.Levels[i] >= run.Config.MaxLevel ? "已满级" : $"废料 {run.ScrapCost(i)} / 能源 {run.EnergyCost(i)}";
                        SetText(routeLabels[i], $"{Routes[i]}   {run.Levels[i]} / {run.Config.MaxLevel}   {cost}");
                    }
                    SetText(feedback, controller.UpgradeFeedback);
                    break;
                case PrototypeOverlay.Result:
                    SetText(heading, run.Outcome > 0 ? "防守成功" : "本局结束");
                    SetText(details, $"坚持 {Mathf.FloorToInt(run.Elapsed)} 秒，击退 {controller.World.Kills} 个敌人。\n下一局将生成新的资源布局。");
                    SetText(actionLabel, "新地图，再来一局");
                    break;
            }
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
            StartText(font, "Brief", "前哨行动", 1670, 55, 160, 48, 22, mint, alignment: TextAnchor.MiddleRight);
            Fill("HeaderRule", 96, 122, 1728, 2, line);
            StartText(font, "Operations", "F I E L D   O P E R A T I O N S", 96, 230, 630, 40, 19, mint);
            var latinFont = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            StartText(latinFont, "TitleOutpost", "OUTPOST", 88, 268, 705, 150, 123, ink, FontStyle.Bold);
            StartText(latinFont, "TitleLoop", "LOOP", 89, 386, 640, 150, 123, mint, FontStyle.Bold);
            StartText(font, "GameLoop", "探索  ·  采集  ·  升级  ·  防守", 96, 555, 650, 44, 26, muted);

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
            StartText(font, "ArtworkCaption", "前哨基地 · 概念图", 833, 891, 420, 50, 22, ink);

            Fill("BriefingRule", 96, 644, 650, 2, line);
            string briefing = $"回收废料与能源，强化基地防线。\n守住 {controller.World.Run.Config.Duration / 60:0.#} 分钟内的最后一波进攻。";
            StartText(font, "Briefing", briefing, 96, 660, 650, 94, 27, ink);
            var action = Button(startStage, font, "PrimaryAction", "开始这一局  →", new Vector2(390, 77),
                new Vector2(291, -825), new Vector2(0, 1), controller.ActOnModal);
            action.color = new Color32(15, 32, 26, 255);
            action.fontStyle = FontStyle.Bold;
            action.transform.parent.GetComponent<Image>().color = mint;
            Fill("FooterRule", 96, 992, 1728, 2, line);
            StartText(font, "Controls", "WASD 移动    E 采集    B 升级    ESC 暂停", 96, 1003, 800, 50, 20, muted);
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
            return label;
        }

        private static void SetText(Text label, string text)
        {
            if (label.text != text) label.text = text;
        }
    }
}
