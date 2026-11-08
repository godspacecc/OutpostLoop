using System;
using Boot;
using Cysharp.Threading.Tasks;
using Game;
using QFramework;
using UnityEngine;
using UnityEngine.InputSystem;
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
            view.Open();
        }
        internal void CheckUpdateFromStart() => view.CheckFromStart();
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
        // 保存结构化结果，切换语言后按当前语言重新显示。
        private ResourceUpdateCheckWithNoticeResult? lastUpdate;
        private bool showUpdateFirst;
        private Vector2 scroll;
        private Font font;
        private IArchitecture Architecture => Game.Game.Interface;

        internal void Open()
        {
            showUpdateFirst = false;
            lastUpdate = null;
            message = "";
            scroll = Vector2.zero;
            LoadDisplay();
        }

        internal void CheckFromStart()
        {
            showUpdateFirst = true;
            Check().Forget();
        }

        private void LoadDisplay()
        {
            if (font == null) font = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 18);
            var model = Architecture.GetModel<DisplaySettingsModel>();
            resolution = model.CurrentResolutionCode.Value;
            mode = model.CurrentWindowMode.Value;
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
            if (showUpdateFirst) DrawUpdate();
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
                if (GUILayout.Button(HotKeyLabel(binding))) keys.BeginRebind(binding.ActionId);
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
            if (!showUpdateFirst) DrawUpdate();
            if (GUILayout.Button(Loc.T("CommonBack"))) Close?.Invoke();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.matrix = oldMatrix;
            GUI.skin.font = previousFont;
        }

        private void DrawUpdate()
        {
            var wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && !checking;
            if (GUILayout.Button(Loc.T("SettingsCheck"))) Check().Forget();
            GUI.enabled = wasEnabled;
            var snapshot = lastUpdate?.Snapshot ?? Architecture.GetSystem<ResourceUpdateSystem>().Snapshot;
            GUILayout.Label(PrototypeUpdateText.CurrentVersion(snapshot));
            GUILayout.Label(checking ? Loc.T("UpdateChecking") : PrototypeUpdateText.Status(snapshot));
            if (lastUpdate is { } result)
            {
                if (snapshot.Status == ResourceUpdateStatus.UpdateAvailable && snapshot.HasKnownDownloadSize)
                    GUILayout.Label(Loc.T("UpdateResourceSize", snapshot.TotalDownloadCount, snapshot.TotalDownloadBytes / 1048576d));
                if (result.ApplicationUpdate is { } app)
                    GUILayout.Label(Loc.T("UpdateApplication", Application.version, app.versionName, app.sizeBytes / 1048576d));
                if (result.Notice.HasNotice)
                    GUILayout.Label(ResourceUpdateNoticeDocument.FormatForNotify(result.Notice.Content, true));
            }
            if (!string.IsNullOrEmpty(message)) GUILayout.Label(message);
            bool canRestart = lastUpdate is { } update &&
                              (update.Snapshot.Status == ResourceUpdateStatus.UpdateAvailable || update.ApplicationUpdate != null);
            if (!checking && canRestart && GUILayout.Button(Loc.T("SettingsRestart")))
            {
                Architecture.GetSystem<SettingsSystem>().Flush();
                if (!Architecture.GetUtility<IApplicationRestartUtility>().TryRestart(out var error)) message = error;
            }
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

        /// <summary>设置只展示动作名称和键帽文字，重绑与存储仍使用原始标识。</summary>
        private static string HotKeyLabel(HotKeyBinding binding)
        {
            var action = Loc.T(binding.ActionId switch
            {
                HotKeyActionIds.TogglePause => "SettingsKeyPause",
                HotKeyActionIds.ToggleRuntimeLog => "SettingsKeyLog",
                _ => "SettingsKeyOther"
            });
            var key = binding.BindingPath == HotKeyDefaults.Escape ? "Esc" :
                InputControlPath.ToHumanReadableString(binding.BindingPath, InputControlPath.HumanReadableStringOptions.OmitDevice);
            return Loc.T("SettingsKeyLabel", action, string.IsNullOrWhiteSpace(key) ? Loc.T("SettingsKeyUnbound") : key);
        }

        /// <summary>更新结果和公告只写回仍打开的窗口，重启让 Boot 执行下载与安装。</summary>
        private async UniTaskVoid Check()
        {
            int request = ++generation;
            checking = true;
            lastUpdate = null;
            message = "";
            try
            {
                var result = await Architecture.GetSystem<ResourceUpdateSystem>().CheckWithNoticeAsync(true);
                if (request != generation || this == null) return;
                lastUpdate = result;
            }
            catch (Exception error) { if (request == generation && this != null) message = error.Message; }
            finally { if (request == generation && this != null) checking = false; }
        }
    }

    /// <summary>开始页与设置窗口共用资源版本和检查状态的本地化文案。</summary>
    internal static class PrototypeUpdateText
    {
        internal static string CurrentVersion(ResourceUpdateSnapshot snapshot)
        {
            if (snapshot.ResourceSource == BootResourceSource.EditorSimulate &&
                string.Equals(snapshot.CurrentVersion, "Simulate", StringComparison.OrdinalIgnoreCase))
                return Loc.T("UpdateEditorSimulation");
            return string.IsNullOrWhiteSpace(snapshot.CurrentVersion)
                ? Loc.T("UpdateVersionUnknown")
                : Loc.T("UpdateCurrentVersion", snapshot.CurrentVersion);
        }

        internal static string Status(ResourceUpdateSnapshot snapshot)
        {
            bool builtin = snapshot.ResourceSource == BootResourceSource.BuiltinFallback;
            return snapshot.Status switch
            {
                ResourceUpdateStatus.Checking => Loc.T("UpdateChecking"),
                ResourceUpdateStatus.NoUpdate => Loc.T(builtin ? "UpdateBuiltinCurrent" : "SettingsUpToDate"),
                ResourceUpdateStatus.UpdateAvailable => Loc.T("UpdateTargetVersion", snapshot.TargetVersion),
                ResourceUpdateStatus.CheckFailed => Loc.T(builtin ? "UpdateServerUnavailable" : "UpdateCheckFailed"),
                _ => Loc.T(builtin ? "UpdateBuiltinActive" : "UpdateNotChecked")
            };
        }
    }
}
