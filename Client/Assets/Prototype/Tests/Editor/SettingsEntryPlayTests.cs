using System.Collections;
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
                Assert.AreEqual("Start session  →", GameObject.Find("PrimaryAction").GetComponentInChildren<Text>().text);
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

        private sealed class DisplaySpy : IDisplayResolutionApplier
        {
            public void Apply(DisplayResolutionPreset preset, FullScreenMode mode) { }
        }
    }
}
