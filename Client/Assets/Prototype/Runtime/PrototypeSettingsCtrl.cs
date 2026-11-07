using System;
using Cysharp.Threading.Tasks;
using Game;
using QFramework;
using UnityEngine;
using UnityEngine.UI;

namespace OutpostLoop
{
    /// <summary>临时设置窗口通过公共 UI 生命周期接入，设置及更新行为由基础系统执行。</summary>
    public sealed class PrototypeSettingsCtrl : BaseUICtrl
    {
        private PrototypeSettingsView view;
        private PrototypeSystem session;
        private bool wasPaused;
        protected override string Path() => null;
        public override UILayer Layer() => UILayer.Top;
        protected override BaseUI CreateView()
        {
            var root = new GameObject("Settings", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)root.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            root.GetComponent<Image>().color = new Color(0, 0, 0, .8f);
            return root.AddComponent<PrototypeSettingsView>();
        }
        protected override void OnInit() { view = (PrototypeSettingsView)BaseUI; view.Close = Hide; }
        protected override void OnShow()
        {
            var architecture = Game.Game.Interface;
            session = architecture.GetSystem<PrototypeSystem>();
            var world = architecture.GetModel<PrototypeModel>()?.World;
            wasPaused = world == null || world.Run.Paused;
            session?.SetPaused(true);
            view.LoadDisplay();
        }
        protected override void OnHide()
        {
            view.Cancel();
            if (FoundationRuntime.IsInitialized) session?.SetPaused(wasPaused);
        }
        protected override void OnUnload() { view.Cancel(); view.ReleaseFont(); }
    }

    /// <summary>原型设置表现层，提供语言、音量、显示预览、按键及更新公告的实际入口。</summary>
    internal sealed class PrototypeSettingsView : BaseUI
    {
        // 候选显示值在用户确认前不写入模型。异步请求使用会话代次过滤关闭后的结果。
        internal Action Close;
        private string resolution;
        private FullScreenMode mode;
        private string message = "";
        private bool checking;
        private int generation;
        private bool canRestart;
        private Vector2 scroll;
        private Font font;
        private IArchitecture Architecture => Game.Game.Interface;

        internal void LoadDisplay()
        {
            if (font == null) font = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 18);
            var model = Architecture.GetModel<DisplaySettingsModel>();
            resolution = model.CurrentResolutionCode.Value;
            mode = model.CurrentWindowMode.Value;
            message = "";
        }

        internal void Cancel()
        {
            generation++;
            checking = false;
            Architecture.GetSystem<DisplayPreviewTransaction>().Cancel();
            Architecture.GetSystem<HotKeySystem>().CancelRebind();
            Architecture.GetSystem<SettingsSystem>().Flush();
        }

        private void OnGUI()
        {
            if (!FoundationRuntime.IsInitialized) return;
            var previousFont = GUI.skin.font;
            GUI.skin.font = font;
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            var oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            GUILayout.BeginArea(new Rect(290, 30, 700, 660), GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label(Loc.T("SettingsTitle"));
            var language = Architecture.GetModel<LocalizationModel>();
            GUILayout.BeginHorizontal();
            foreach (var item in LocalizationLanguages.All)
            {
                if (GUILayout.Button(item.Label))
                {
                    language.SetLanguageCode(item.Code);
                    Architecture.GetSystem<SettingsSystem>().MarkDirty();
                }
            }
            GUILayout.EndHorizontal();
            var audio = Architecture.GetModel<AudioSettingsModel>();
            var master = Volume("SettingsMaster", audio.MasterVolume.Value);
            var music = Volume("SettingsMusic", audio.MusicVolume.Value);
            var sound = Volume("SettingsSound", audio.SoundVolume.Value);
            bool mute = GUILayout.Toggle(audio.Mute.Value, Loc.T("SettingsMute"));
            if (master != audio.MasterVolume.Value || music != audio.MusicVolume.Value || sound != audio.SoundVolume.Value || mute != audio.Mute.Value)
            {
                audio.MasterVolume.Value = master;
                audio.MusicVolume.Value = music;
                audio.SoundVolume.Value = sound;
                audio.Mute.Value = mute;
                Architecture.GetSystem<AudioSystem>().ApplyVolumes();
                Architecture.GetSystem<SettingsSystem>().MarkDirty();
            }

            GUILayout.Label(Loc.T("SettingsDisplay"));
            GUILayout.BeginHorizontal();
            foreach (var preset in DisplayResolutionPresets.All)
                if (GUILayout.Toggle(resolution == preset.Code, preset.Label, GUI.skin.button)) resolution = preset.Code;
            GUILayout.EndHorizontal();
            mode = GUILayout.Toggle(mode == FullScreenMode.FullScreenWindow, Loc.T("SettingsFullscreen")) ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            var preview = Architecture.GetSystem<DisplayPreviewTransaction>();
            if (preview.IsActive)
            {
                GUILayout.Label(Loc.T("SettingsCountdown", Mathf.CeilToInt(preview.RemainingSeconds)));
                if (GUILayout.Button(Loc.T("CommonConfirm"))) preview.Confirm();
                if (GUILayout.Button(Loc.T("CommonCancel"))) { preview.Cancel(); LoadDisplay(); }
            }
            else if (GUILayout.Button(Loc.T("SettingsPreview"))) preview.BeginPreview(resolution, mode);

            GUILayout.Label(Loc.T("SettingsKeys"));
            var keys = Architecture.GetSystem<HotKeySystem>();
            foreach (var binding in Architecture.GetModel<HotKeyModel>().Bindings)
                if (GUILayout.Button(binding.ActionId + "  " + binding.BindingPath)) keys.BeginRebind(binding.ActionId);
            if (keys.IsCapturingRebind)
            {
                GUILayout.Label(Loc.T("SettingsCapture"));
                if (GUILayout.Button(Loc.T("SettingsReplace"))) message = keys.ConfirmPendingBinding(true).Error;
                if (GUILayout.Button(Loc.T("CommonCancel"))) keys.CancelRebind();
            }
            if (GUILayout.Button(Loc.T("SettingsReset")))
            {
                preview.Cancel();
                Architecture.GetSystem<SettingsSystem>().ResetAllToDefault();
                Architecture.GetSystem<AudioSystem>().ApplyVolumes();
                var display = Architecture.GetModel<DisplaySettingsModel>();
                Architecture.GetUtility<IDisplayResolutionApplier>().Apply(display.GetCurrentResolution(), display.CurrentWindowMode.Value);
                LoadDisplay();
            }
            GUI.enabled = !checking;
            if (GUILayout.Button(Loc.T("SettingsCheck"))) Check().Forget();
            GUI.enabled = true;
            GUILayout.Label(message);
            if (canRestart && GUILayout.Button(Loc.T("SettingsRestart")))
            {
                Architecture.GetSystem<SettingsSystem>().Flush();
                if (!Architecture.GetUtility<IApplicationRestartUtility>().TryRestart(out var error)) message = error;
            }
            if (GUILayout.Button(Loc.T("CommonBack"))) Close?.Invoke();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.matrix = oldMatrix;
            GUI.skin.font = previousFont;
        }

        internal void ReleaseFont()
        {
            if (font != null) Destroy(font);
        }

        private static float Volume(string key, float value)
        {
            GUILayout.Label(Loc.T(key) + "  " + Mathf.RoundToInt(value * 100) + "%");
            return GUILayout.HorizontalSlider(value, 0, 1);
        }

        /// <summary>更新结果和公告只写回仍打开的窗口，重启让 Boot 执行下载与安装。</summary>
        private async UniTaskVoid Check()
        {
            int request = ++generation;
            checking = true;
            try
            {
                var result = await Architecture.GetSystem<ResourceUpdateSystem>().CheckWithNoticeAsync(true);
                if (request != generation || this == null) return;
                canRestart = result.Snapshot.Status == ResourceUpdateStatus.UpdateAvailable || result.ApplicationUpdate != null;
                message = Loc.T(canRestart ? "SettingsUpdateAvailable" : "SettingsUpToDate");
                if (result.Snapshot.Status == ResourceUpdateStatus.CheckFailed) message = result.Snapshot.Error;
                if (result.Notice.HasNotice)
                    message += "\n" + result.Notice.Content.Title + "\n" + result.Notice.Content.Summary + "\n" + string.Join("\n", result.Notice.Content.Items);
            }
            catch (Exception error) { if (request == generation && this != null) message = error.Message; }
            finally { if (request == generation && this != null) checking = false; }
        }
    }
}
