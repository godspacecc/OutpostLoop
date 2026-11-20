using Game;
using UnityEngine;
using UnityEngine.UI;

namespace OutpostLoop
{
    /// <summary>原型日志热键的实际窗口，读取公共日志模型。</summary>
    public sealed class PrototypeLogCtrl : BaseUICtrl
    {
        private PrototypeSystem session;
        private bool wasPaused;
        protected override string Path() => null;
        public override UILayer Layer() => UILayer.Top;
        protected override BaseUI CreateView()
        {
            var root = new GameObject("RuntimeLogs", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)root.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            root.GetComponent<Image>().color = new Color(0, 0, 0, .8f);
            return root.AddComponent<PrototypeLogView>();
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
    }

    internal sealed class PrototypeLogView : BaseUI
    {
        private Vector2 scroll;
        // 指令输入只在当前日志窗口打开期间保留。
        private bool commandInputVisible;
        private string commandInput = "#help";

        private void OnGUI()
        {
            if (!FoundationRuntime.IsInitialized) return;
            // Log 面板与行动 HUD 都使用 IMGUI，需要显式设置绘制层级。
            var previousMatrix = GUI.matrix;
            int previousDepth = GUI.depth;
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            GUI.depth = -10;
            var panel = new Rect((Screen.width / scale - 900) * .5f, 20, 900, 680);
            GUI.DrawTexture(panel, Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0,
                new Color(.08f, .09f, .11f), 0, 0);
            GUILayout.BeginArea(panel, GUI.skin.box);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("CommonBack"))) UIHelper.HideUI<PrototypeLogCtrl>();
            if (GUILayout.Button(Loc.T("RuntimeLogDebugCommands"))) commandInputVisible = true;
            GUILayout.EndHorizontal();
            if (commandInputVisible) DrawCommandInput();

            scroll = GUILayout.BeginScrollView(scroll);
            foreach (var entry in Game.Game.Interface.GetModel<RuntimeLogModel>().Snapshot())
                GUILayout.Label(entry.Timestamp.ToString("HH:mm:ss") + " " + entry.Type + " " + entry.Message);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.depth = previousDepth;
            GUI.matrix = previousMatrix;
        }

        /// <summary>通过同一白名单执行入口提交单条指令，结果随运行日志显示。</summary>
        private void DrawCommandInput()
        {
            GUILayout.BeginHorizontal();
            commandInput = GUILayout.TextField(commandInput);
            if (GUILayout.Button(Loc.T("RuntimeLogExecuteCommand"), GUILayout.ExpandWidth(false)))
            {
                Game.Game.Interface.GetSystem<DebugCommandSystem>().ExecuteAsync(commandInput).GetAwaiter().GetResult();
                scroll.y = float.MaxValue;
            }
            if (GUILayout.Button(Loc.T("CommonCancel"), GUILayout.ExpandWidth(false))) ResetCommandInput();
            GUILayout.EndHorizontal();
        }

        /// <summary>关闭窗口或取消输入时清除上一条指令。</summary>
        private void ResetCommandInput()
        {
            commandInputVisible = false;
            commandInput = "#help";
        }

        private void OnDisable() => ResetCommandInput();
    }
}
