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
            GUILayout.BeginArea(new Rect(20, 20, Mathf.Min(Screen.width - 40, 900), Mathf.Max(100, Screen.height - 40)), GUI.skin.box);
            if (GUILayout.Button(Loc.T("CommonBack"))) UIHelper.HideUI<PrototypeLogCtrl>();
            scroll = GUILayout.BeginScrollView(scroll);
            foreach (var entry in Game.Game.Interface.GetModel<RuntimeLogModel>().Snapshot())
                GUILayout.Label(entry.Timestamp.ToString("HH:mm:ss") + " " + entry.Type + " " + entry.Message);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
