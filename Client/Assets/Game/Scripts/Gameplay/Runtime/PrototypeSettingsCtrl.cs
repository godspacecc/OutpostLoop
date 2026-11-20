using System;
using Boot;
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
            root.GetComponent<Image>().color = PrototypeMenuWidgets.Shade;
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
        internal void EnterFromSurvival() => view.FromActiveSurvival = true;
        internal void CheckUpdateFromStart() => view.CheckFromStart();
        protected override void OnHide()
        {
            view.Cancel();
            if (FoundationRuntime.IsInitialized) session?.SetPaused(wasPaused);
        }
        protected override void OnUnload() { view.Cancel(); }
    }

    /// <summary>原型设置表现层，提供语言、音量、显示预览、按键及更新公告的实际入口。</summary>
    internal sealed partial class PrototypeSettingsView : BaseUI
    {
        // 候选显示值在用户确认前不写入模型。异步请求使用会话代次过滤关闭后的结果。
        internal Action Close;
        private string resolution;
        private FullScreenMode mode;
        private string message = "";
        private bool checking;
        // 确认只在本次设置窗口及本次更新结果内有效。
        private bool restartPending;
        private int generation;
        // 保存结构化结果，切换语言后按当前语言重新显示。
        private ResourceUpdateCheckWithNoticeResult? lastUpdate;
        internal bool FromActiveSurvival;
        private IArchitecture Architecture => Game.Game.Interface;

        internal void Open()
        {
            FromActiveSurvival = false;
            restartPending = false;
            lastUpdate = null;
            message = "";
            LoadDisplay();
            OpenMenu();
        }

        internal void CheckFromStart()
        {
            SelectSection(Section.Updates);
            Check().Forget();
        }

        private void LoadDisplay()
        {
            var model = Architecture.GetModel<DisplaySettingsModel>();
            resolution = model.CurrentResolutionCode.Value;
            mode = model.CurrentWindowMode.Value;
        }

        internal void Cancel()
        {
            generation++;
            CloseMenu();
            checking = false;
            restartPending = false;
            Architecture.GetSystem<DisplayPreviewTransaction>().Cancel();
            Architecture.GetSystem<HotKeySystem>().CancelRebind();
            Architecture.GetSystem<SettingsSystem>().Flush();
        }

        /// <summary>有未保存战局时先显示确认，开始页仍可直接更新。</summary>
        private void RequestUpdateRestart()
        {
            if (FromActiveSurvival || Architecture.GetSystem<PrototypeSystem>()?.HasUnsavedRun == true)
                restartPending = true;
            else
                RestartForUpdate();
            pageDirty = true;
        }

        private void ConfirmUpdateRestart()
        {
            if (!restartPending) return;
            restartPending = false;
            RestartForUpdate();
            pageDirty = true;
        }

        /// <summary>设置写盘成功后才交给平台重启，失败时保留当前窗口与战局。</summary>
        private void RestartForUpdate()
        {
            if (!Architecture.GetSystem<SettingsSystem>().Flush())
            {
                message = Loc.T("SettingsRestartSaveFailed");
                return;
            }
            message = Architecture.GetUtility<IApplicationRestartUtility>().TryRestart(out var error) ? "" : error;
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
            var key = HotKeyDefaults.DisplayKey(binding.BindingPath);
            return Loc.T("SettingsKeyLabel", action, string.IsNullOrWhiteSpace(key) ? Loc.T("SettingsKeyUnbound") : key);
        }

        /// <summary>更新结果和公告只写回仍打开的窗口，重启让 Boot 执行下载与安装。</summary>
        private async UniTaskVoid Check()
        {
            int request = ++generation;
            checking = true;
            pageDirty = true;
            restartPending = false;
            lastUpdate = null;
            message = "";
            try
            {
                var result = await Architecture.GetSystem<ResourceUpdateSystem>().CheckWithNoticeAsync(true);
                if (request != generation || this == null) return;
                lastUpdate = result;
            }
            catch (Exception error) { if (request == generation && this != null) message = error.Message; }
            finally
            {
                if (request == generation && this != null)
                {
                    checking = false;
                    pageDirty = true;
                }
            }
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
