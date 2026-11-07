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

    /// <summary>临时 UGUI 控件树，无正式视觉资源；开局到结算期间重复使用同一组控件。</summary>
    internal sealed class PrototypeOverlayView : BaseUI
    {
        private static readonly string[] Routes = { "采集 / 运输", "炮塔火力 / 射程", "基地耐久 / 修复" };
        private readonly Text[] routeLabels = new Text[Routes.Length];
        private Text heading, details, actionLabel, feedback;
        private GameObject purchaseRows;

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
            var panel = Rect("Panel", transform, new Vector2(.5f,.5f), new Vector2(780,530), Vector2.zero);
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
            var run = controller.World.Run;
            bool upgrading = mode == PrototypeOverlay.Upgrade;
            purchaseRows.SetActive(upgrading);
            details.gameObject.SetActive(!upgrading);
            switch (mode)
            {
                case PrototypeOverlay.Start:
                    SetText(heading, "探索 / 采集 / 升级 / 防守");
                    SetText(details, "搜寻废料与能源，满载后回基地卸货。\n升级采集能力和防线，炮塔会自动作战。\n每局 12 分钟，最后一波在第 11 分钟。");
                    SetText(actionLabel, "开始这一局");
                    break;
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
