using System.Collections;
using Boot;
using Cysharp.Threading.Tasks;
using Game;
using NUnit.Framework;
using QFramework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace OutpostLoop.Tests
{
    /// <summary>从真实开始页打开设置，验证语言绑定、暂停恢复与显示预览取消。</summary>
    public sealed class SettingsEntryPlayTests
    {
        [UnityTest]
        public IEnumerator StartSettingsRefreshLanguageAndRestoreSession()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            for (int frame = 0; frame < 600 && Object.FindObjectOfType<PrototypeController>() == null; frame++) yield return null;
            Assert.NotNull(Object.FindObjectOfType<PrototypeController>(), "Boot 未进入 Main");
            for (int frame = 0; frame < 10; frame++) yield return null;
            Assert.IsTrue(FoundationRuntime.IsInitialized);
            var architecture = Game.Game.Interface;
            var settings = architecture.GetSystem<SettingsSystem>();
            var snapshot = new SettingsData
            {
                LanguageCode = architecture.GetModel<LocalizationModel>().CurrentLanguageCode.Value,
                Display = architecture.GetModel<DisplaySettingsModel>().ToData(),
                Audio = architecture.GetModel<AudioSettingsModel>().ToData(),
                HotKeys = architecture.GetModel<HotKeyModel>().ToData()
            };
            var store = architecture.GetUtility<ISettingsFileStore>();
            var display = architecture.GetUtility<IDisplayResolutionApplier>();
            architecture.RegisterUtility<ISettingsFileStore>(new MemoryStore());
            architecture.RegisterUtility<IDisplayResolutionApplier>(new DisplaySpy());
            try
            {
                GameObject.Find("SettingsEntry").GetComponent<Button>().onClick.Invoke();
                var ctrl = UIHelper.GetUICtrl<PrototypeSettingsCtrl>();
                Assert.IsTrue(ctrl.IsShow());
                GameObject.Find("PrimaryAction").GetComponent<Button>().onClick.Invoke();
                Assert.IsTrue(architecture.GetModel<PrototypeModel>().World.Run.Paused);
                architecture.GetModel<LocalizationModel>().SetLanguageCode("en");
                Assert.AreEqual("Defense trial  →", GameObject.Find("PrimaryAction").GetComponentInChildren<Text>().text);
                architecture.GetSystem<DisplayPreviewTransaction>().BeginPreview("1280x720", FullScreenMode.Windowed);
                ctrl.Hide();
                Assert.IsFalse(architecture.GetSystem<DisplayPreviewTransaction>().IsActive);
                Assert.IsTrue(architecture.GetModel<PrototypeModel>().World.Run.Paused);
                GameObject.Find("PrimaryAction").GetComponent<Button>().onClick.Invoke();
                Assert.IsFalse(architecture.GetModel<PrototypeModel>().World.Run.Paused);
                UIHelper.ShowUI<PrototypeSettingsCtrl>();
                Assert.IsTrue(architecture.GetModel<PrototypeModel>().World.Run.Paused);
                ctrl.Hide();
                Assert.IsFalse(architecture.GetModel<PrototypeModel>().World.Run.Paused);
                var hotkeys = architecture.GetSystem<HotKeySystem>();
                Assert.IsTrue(hotkeys.ExecuteBinding(HotKeyDefaults.F1));
                Assert.IsTrue(UIHelper.GetUICtrl<PrototypeLogCtrl>().IsShow());
                Assert.IsTrue(architecture.GetModel<PrototypeModel>().World.Run.Paused);
                Assert.IsFalse(hotkeys.ExecuteBinding(HotKeyDefaults.Escape));
                UIHelper.HideUI<PrototypeLogCtrl>();
                Assert.IsFalse(architecture.GetModel<PrototypeModel>().World.Run.Paused);
            }
            finally
            {
                if (FoundationRuntime.IsInitialized)
                {
                    settings.Apply(snapshot);
                    architecture.GetSystem<AudioSystem>().ApplyVolumes();
                    settings.Flush();
                    architecture.RegisterUtility<ISettingsFileStore>(store);
                    architecture.RegisterUtility<IDisplayResolutionApplier>(display);
                }
            }
            yield return new ExitPlayMode();
        }

        private sealed class MemoryStore : ISettingsFileStore
        {
            public SettingsData LoadOrDefault(out SettingsLoadResult result) { result = SettingsLoadResult.CreatedDefault; return SettingsData.CreateDefault(); }
            public bool Save(SettingsData data, out string error) { error = null; return true; }
        }

        [UnityTest]
        public IEnumerator StartPageChecksUpdatesAndShowsRedPoint()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            for (int frame = 0; frame < 600 && GameObject.Find("SettingsEntry") == null; frame++) yield return null;
            Assert.NotNull(GameObject.Find("SettingsEntry"), "Boot 未进入开始页");
            var architecture = Game.Game.Interface;
            var updates = architecture.GetSystem<ResourceUpdateSystem>();
            var checker = new UpdateChecker();
            var notices = new NoticeClient();
            architecture.RegisterUtility<IResourceUpdateRuntimeChecker>(checker);
            architecture.RegisterUtility<IResourceUpdateNoticeClient>(notices);
            updates.InitializeStartupInfo(new BootResourceStartupInfo(BootResourceSource.BuiltinFallback, "", "1", ""));
            var modal = UIHelper.GetUICtrl<PrototypeModalCtrl>();
            modal.Hide();
            for (int frame = 0; frame < 30 && !modal.IsShow(); frame++) yield return null;
            Assert.IsTrue(modal.IsShow());
            Assert.AreEqual(ResourceUpdateStatus.NotChecked, updates.Snapshot.Status, "内置回退不应自动检查");

            updates.InitializeStartupInfo(new BootResourceStartupInfo(BootResourceSource.HostLatest, "http://127.0.0.1:1", "1", "1"));
            modal.Hide();
            for (int frame = 0; frame < 30 && checker.CallCount == 0; frame++) yield return null;
            Assert.AreEqual(1, checker.CallCount, "开始页应自动检查一次");
            Assert.AreEqual(0, notices.CallCount, "静默检查不拉取公告");
            // 红点由通用系统在帧尾聚合，界面在下一帧读取。
            for (int frame = 0; frame < 30 && GameObject.Find("StartUpdateRedPoint") == null; frame++) yield return null;
            Assert.NotNull(GameObject.Find("StartUpdateRedPoint"), "发现更新后开始页应显示红点");
            var version = GameObject.Find("StartUpdateVersion")?.GetComponent<Text>();
            var status = GameObject.Find("StartUpdateStatus")?.GetComponent<Text>();
            Assert.NotNull(version, "开始页应展示当前资源版本");
            Assert.NotNull(status, "开始页应展示检查状态");
            Assert.That(version.text, Does.Contain("1"));
            Assert.That(status.text, Does.Contain("2"));
            for (int frame = 0; frame < 5; frame++) yield return null;
            Assert.AreEqual(1, checker.CallCount, "显示期间不应逐帧检查");

            checker.HasUpdate = false;
            GameObject.Find("StartUpdateEntry").GetComponent<Button>().onClick.Invoke();
            for (int frame = 0; frame < 30 && notices.CallCount == 0; frame++) yield return null;
            Assert.AreEqual(2, checker.CallCount);
            Assert.AreEqual(1, notices.CallCount, "主动检查应拉取公告");
            Assert.IsTrue(UIHelper.GetUICtrl<PrototypeSettingsCtrl>().IsShow());
            for (int frame = 0; frame < 30 && GameObject.Find("StartUpdateRedPoint") != null; frame++) yield return null;
            Assert.IsNull(GameObject.Find("StartUpdateRedPoint"), "没有更新时红点应清除");
            yield return new ExitPlayMode();
        }

        private sealed class UpdateChecker : IResourceUpdateRuntimeChecker
        {
            public bool CanCheck => true;
            public int CallCount { get; private set; }
            public bool HasUpdate { get; set; } = true;

            public UniTask<ResourceUpdateCheckResult> CheckAsync(int timeoutSeconds)
            {
                CallCount++;
                var status = HasUpdate ? ResourceUpdateStatus.UpdateAvailable : ResourceUpdateStatus.NoUpdate;
                return UniTask.FromResult(ResourceUpdateCheckResult.Ok(
                    new ResourceUpdateSnapshot(status, "1", HasUpdate ? "2" : "1", 0, 0, "")));
            }
        }

        private sealed class NoticeClient : IResourceUpdateNoticeClient
        {
            public int CallCount { get; private set; }

            public UniTask<ResourceUpdateNoticeResult> FetchAsync(ResourceUpdateNoticeRequest request)
            {
                CallCount++;
                return UniTask.FromResult(ResourceUpdateNoticeResult.NoNotice);
            }
        }

        private sealed class DisplaySpy : IDisplayResolutionApplier
        {
            public void Apply(DisplayResolutionPreset preset, FullScreenMode mode) { }
        }
    }
}
