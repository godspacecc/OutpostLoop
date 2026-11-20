using System.Linq;
using Game;
using QFramework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace OutpostLoop
{
    /// <summary>在游戏内打开测试工具，按钮本身不暂停行动。</summary>
    public sealed class PrototypeTestCommandButtonCtrl : BaseUICtrl
    {
        protected override string Path() => null;
        public override UILayer Layer() => UILayer.Top;
        public override bool BlocksGame => false;
        protected override BaseUI CreateView()
        {
            var root = new GameObject("TestCommandLauncher", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            return root.AddComponent<PrototypeTestCommandButtonView>();
        }

        protected override void OnInit() { }
        protected override void OnShow() { }
        protected override void OnHide() { }

        internal void Configure(PrototypeController owner, Font font) =>
            ((PrototypeTestCommandButtonView)BaseUI).Configure(owner, font);

        internal bool Contains(Vector2 cursor) =>
            IsShow() && ((PrototypeTestCommandButtonView)BaseUI).Contains(cursor);
    }

    /// <summary>将拖动位置换算为归一化设置，分辨率变化后仍夹在屏幕范围内。</summary>
    internal sealed class PrototypeTestCommandButtonView : BaseUI
    {
        private PrototypeController owner;
        private CanvasGroup group;
        private Vector2 dragPosition;
        private Vector2 pressPosition;
        private Pointer pressedPointer;
        private bool pressing;
        private bool dragging;
        private RectTransform Rect => (RectTransform)transform;
        private RectTransform Parent => (RectTransform)transform.parent;
        private DebugCommandSystem Commands => Game.Game.Interface.GetSystem<DebugCommandSystem>();

        internal void Configure(PrototypeController controller, Font font)
        {
            owner = controller;
            group = GetComponent<CanvasGroup>();
            Rect.anchorMin = Rect.anchorMax = Rect.pivot = new Vector2(.5f, .5f);
            Rect.sizeDelta = new Vector2(156, 48);
            GetComponent<Image>().color = new Color(.125f, .18f, .235f, .96f);
            var text = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(transform, false);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            text.font = font;
            text.fontSize = 22;
            text.alignment = TextAnchor.MiddleCenter;
            text.text = "测试指令";
            text.raycastTarget = false;
            LateUpdate();
        }

        internal bool Contains(Vector2 cursor) => group != null && group.blocksRaycasts &&
            (dragging || RectTransformUtility.RectangleContainsScreenPoint(Rect, cursor));

        private void LateUpdate()
        {
            if (group == null) return;
            bool available = FoundationRuntime.IsInitialized && owner != null && owner.CanOpenTestCommands;
            group.alpha = available ? 1 : 0;
            group.blocksRaycasts = available;
            if (!available) { pressedPointer = null; pressing = false; dragging = false; return; }
            PollPointer();
            var range = Vector2.Max(Parent.rect.size - Rect.sizeDelta, Vector2.zero);
            var position = dragging ? dragPosition : Commands.LauncherPosition;
            Rect.anchoredPosition = Vector2.Scale(position - Vector2.one * .5f, range);
        }

        /// <summary>跟踪同一指针直至释放，区分点击、拖动和触点移出。</summary>
        private void PollPointer()
        {
            var pointer = pressedPointer ?? Pointer.current;
            if (pointer == null) return;
            var press = pointer is Mouse mouse ? mouse.leftButton : pointer.press;
            TrackPointer(pointer.position.ReadValue(), press.wasPressedThisFrame, press.isPressed);
            pressedPointer = pressing ? pointer : null;
        }

        /// <summary>用 Game 视图坐标处理点击与拖动；批处理可独立验证坐标转换。</summary>
        internal void TrackPointer(Vector2 position, bool pressedThisFrame, bool held)
        {
            if (!pressing)
            {
                if (!pressedThisFrame || !Contains(position)) return;
                pressing = true;
                pressPosition = position;
                dragPosition = Commands.LauncherPosition;
            }

            dragging |= (position - pressPosition).sqrMagnitude > 25;
            if (dragging) MoveLauncher(position);
            if (held) return;

            if (dragging) Commands.SetLauncherPosition(dragPosition);
            else if (Contains(position))
                UIHelper.ShowUI<PrototypeTestCommandCtrl>().SetFont(GetComponentInChildren<Text>().font);
            pressing = false;
            dragging = false;
        }

        private void MoveLauncher(Vector2 position)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Parent, position, null, out var local)) return;
            var range = Vector2.Max(Parent.rect.size - Rect.sizeDelta, Vector2.one);
            var center = Parent.rect.center;
            dragPosition = new Vector2(Mathf.Clamp01((local.x - center.x) / range.x + .5f),
                Mathf.Clamp01((local.y - center.y) / range.y + .5f));
        }

    }

    /// <summary>独立测试窗口打开时冻结行动，关闭后恢复原暂停状态。</summary>
    public sealed class PrototypeTestCommandCtrl : BaseUICtrl
    {
        private PrototypeSystem session;
        private bool wasPaused;
        protected override string Path() => null;
        public override UILayer Layer() => UILayer.Top;
        protected override BaseUI CreateView()
        {
            var root = new GameObject("TestCommands", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)root.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            root.GetComponent<Image>().color = new Color(0, 0, 0, .78f);
            return root.AddComponent<PrototypeTestCommandView>();
        }

        protected override void OnInit() { }
        protected override void OnShow()
        {
            var architecture = Game.Game.Interface;
            session = architecture.GetSystem<PrototypeSystem>();
            wasPaused = architecture.GetModel<PrototypeModel>()?.World?.Run.Paused ?? true;
            session?.SetPaused(true);
        }

        protected override void OnHide()
        {
            if (FoundationRuntime.IsInitialized) session?.SetPaused(wasPaused);
        }

        internal void SetFont(Font font) => ((PrototypeTestCommandView)BaseUI).Font = font;
    }

    /// <summary>浏览目录和历史，提交指令并显示结果，业务由调试指令系统处理。</summary>
    internal sealed class PrototypeTestCommandView : BaseUI
    {
        private static readonly string[] Categories = DebugCommandCatalog.Entries.Select(entry => entry.Category).Distinct().ToArray();
        private const string Recent = "最近执行";
        private const string InputName = "TestCommandInput";
        private string category = "背包物品";
        private string input = "";
        private string result = "";
        private bool success;
        private DebugCommandDefinition selected;
        private Vector2 commandScroll;
        private Vector2 resultScroll;
        internal Font Font { get; set; }
        private DebugCommandSystem Commands => Game.Game.Interface.GetSystem<DebugCommandSystem>();

        private void OnGUI()
        {
            if (!FoundationRuntime.IsInitialized) return;
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                Event.current.Use();
                UIHelper.HideUI<PrototypeTestCommandCtrl>();
                return;
            }
            var previousMatrix = GUI.matrix;
            var previousFont = GUI.skin.font;
            int previousDepth = GUI.depth;
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            GUI.skin.font = Font != null ? Font : previousFont;
            GUI.depth = -10;
            var panel = new Rect((Screen.width / scale - 990) * .5f, 28, 990, 664);
            GUI.DrawTexture(panel, Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0,
                new Color(.075f, .10f, .13f), 0, 0);
            GUILayout.BeginArea(new Rect(panel.x + 20, panel.y + 16, panel.width - 40, panel.height - 32));
            GUILayout.BeginHorizontal();
            GUILayout.Label("测试指令");
            if (GUILayout.Button(Loc.T("CommonBack"), GUILayout.Width(96))) UIHelper.HideUI<PrototypeTestCommandCtrl>();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            DrawCategory(Recent);
            foreach (var item in Categories) DrawCategory(item);
            GUILayout.EndHorizontal();

            commandScroll = GUILayout.BeginScrollView(commandScroll, GUI.skin.box, GUILayout.Height(196));
            if (category == Recent)
            {
                if (Commands.RecentCommands.Count == 0) GUILayout.Label("执行过的指令会显示在这里。");
                foreach (var command in Commands.RecentCommands)
                    if (GUILayout.Button(command)) SelectCommand(command);
            }
            else
            {
                foreach (var entry in DebugCommandCatalog.Entries)
                    if (entry.Category == category && GUILayout.Button(entry.Label))
                        SelectCommand(entry.Example);
            }
            GUILayout.EndScrollView();

            if (selected != null)
            {
                GUILayout.Label(selected.Description);
                GUILayout.Label("用法  " + selected.Usage);
                if (selected.Example != selected.Usage) GUILayout.Label("示例  " + selected.Example);
            }
            GUILayout.BeginHorizontal();
            GUI.SetNextControlName(InputName);
            input = GUILayout.TextField(input);
            var evt = Event.current;
            bool enter = evt.type == EventType.KeyDown && (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) &&
                GUI.GetNameOfFocusedControl() == InputName;
            if (GUILayout.Button("执行", GUILayout.Width(96)) || enter)
            {
                (success, result) = Commands.ExecuteCommand(input);
                resultScroll = Vector2.zero;
                if (enter) evt.Use();
            }
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(result))
            {
                GUILayout.Label(success ? "执行结果" : "执行失败");
                resultScroll = GUILayout.BeginScrollView(resultScroll, GUI.skin.box);
                GUILayout.Label(result);
                GUILayout.EndScrollView();
            }
            GUILayout.EndArea();
            GUI.depth = previousDepth;
            GUI.skin.font = previousFont;
            GUI.matrix = previousMatrix;
        }

        private void DrawCategory(string name)
        {
            if (GUILayout.Toggle(category == name, name, GUI.skin.button) && category != name)
            {
                category = name;
                commandScroll = Vector2.zero;
            }
        }

        private void SelectCommand(string command)
        {
            input = command;
            selected = DebugCommandCatalog.TryParse(command, out var request, out _) ? request.Definition : null;
            GUI.FocusControl(null);
        }
    }
}
