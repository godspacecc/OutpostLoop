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
            if (GUILayout.Button(Loc.T("CommonBack"))) UIHelper.HideUI<PrototypeLogCtrl>();

            scroll = GUILayout.BeginScrollView(scroll);
            foreach (var entry in Game.Game.Interface.GetModel<RuntimeLogModel>().Snapshot())
                GUILayout.Label(entry.Timestamp.ToString("HH:mm:ss") + " " + entry.Type + " " + entry.Message);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.depth = previousDepth;
            GUI.matrix = previousMatrix;
        }

    }
}
