using System;
using System.IO;
using Game;
using QFramework;
using NUnit.Framework;
using UnityEngine;
using OutpostLoop.Resources;

namespace OutpostLoop.Tests
{
    /// <summary>使用隔离存储、显示和重启替身验证设置流程。</summary>
    public sealed class SettingsAlignmentTests
    {
        private string folder;
        private MemoryStore store;
        private DisplaySpy display;
        private Clock clock;
        private IArchitecture architecture;
        private SettingsSystem settings;
        private DisplayPreviewTransaction preview;

        [SetUp] public void Setup()
        {
            folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Backup/Artifacts/SettingsAlignment-20261107", Guid.NewGuid().ToString("N")));
            ResourceLoader.Use(new EditorSourceResourceLoader());
            LocalizationLanguages.ResetCache();
            architecture = Game.Game.Interface;
            store = new MemoryStore();
            display = new DisplaySpy();
            clock = new Clock();
            architecture.RegisterUtility<ISettingsFileStore>(store);
            architecture.RegisterUtility<IDisplayResolutionApplier>(display);
            settings = architecture.GetSystem<SettingsSystem>();
            preview = architecture.GetSystem<DisplayPreviewTransaction>();
            settings.SetClockForTests(clock);
            preview.SetClockForTests(clock);
            FoundationRuntime.Initialize();
        }

        [TearDown] public void Cleanup() { FoundationRuntime.Shutdown(); }

        [Test] public void SettingsFrameworkExistsInGameAssembly()
        {
            Assert.NotNull(settings);
            Assert.NotNull(preview);
            Assert.NotNull(architecture.GetSystem<HotKeySystem>());
            Assert.AreEqual(1, store.Loads);
            Assert.AreEqual("en", architecture.GetModel<LocalizationModel>().CurrentLanguageCode.Value);
            Assert.AreEqual("1280x720", display.Code);
            FoundationRuntime.Initialize();
            Assert.AreEqual(1, store.Loads);
        }

        [Test] public void PerceptionLayoutReadsVersionOneSettingsAndRoundTripsIndependently()
        {
            var disk = new SettingsFileStore(folder);
            Directory.CreateDirectory(disk.SettingsDirectory);
            File.WriteAllText(disk.SettingsPath, "{\"version\":1,\"languageCode\":\"en\"}");
            var legacy = disk.LoadOrDefault(out var loadResult);
            Assert.AreEqual(SettingsLoadResult.LoadedPrimary, loadResult);
            Assert.AreEqual(1f, legacy.Perception.Map.X);
            Assert.AreEqual(true, legacy.Perception.Interaction.Visible);

            var candidate = legacy.Clone();
            candidate.Perception.Map.Visible = false;
            candidate.Perception.Map.X = .35f;
            candidate.Perception.Map.Scale = 2;
            candidate.Perception.Body.Y = float.NaN;
            Assert.IsTrue(disk.Save(candidate, out var error), error);
            var saved = disk.LoadOrDefault(out _);
            Assert.AreEqual("en", saved.LanguageCode);
            Assert.AreEqual(false, saved.Perception.Map.Visible);
            Assert.AreEqual(.35f, saved.Perception.Map.X);
            Assert.AreEqual(1.4f, saved.Perception.Map.Scale);
            Assert.AreEqual(1f, saved.Perception.Body.Y);
            Assert.AreEqual(true, legacy.Perception.Map.Visible);

            // 参考坐标下覆盖 16:9、超宽和 4:3，并验证有刘海偏移时整个分组仍在安全区。
            var rectangles = new Rect[PerceptionGeometry.Count];
            foreach (var area in new[] { new Rect(0, 0, 1280, 720), new Rect(28, 10, 1520, 700), new Rect(0, 0, 1280, 960) })
            {
                PerceptionGeometry.Resolve(saved.Perception, area, rectangles);
                for (int i = 0; i < rectangles.Length; i++)
                {
                    var rect = rectangles[i];
                    Assert.GreaterOrEqual(rect.xMin, area.xMin + 16);
                    Assert.GreaterOrEqual(rect.yMin, area.yMin + 16);
                    Assert.LessOrEqual(rect.xMax, area.xMax - 15.99f);
                    Assert.LessOrEqual(rect.yMax, area.yMax - 15.99f);
                    for (int j = 0; j < i; j++) Assert.IsFalse(rect.Overlaps(rectangles[j]));
                }
            }
        }

        [Test] public void AtomicSaveAndCorruptPrimaryRecoverBackup()
        {
            var file = new SettingsFileStore(folder);
            var data = SettingsData.CreateDefault();
            Assert.IsTrue(file.Save(data, out var error), error);
            data.LanguageCode = "en";
            Assert.IsTrue(file.Save(data, out error), error);
            Assert.AreEqual("en", file.LoadOrDefault(out var result).LanguageCode);
            Assert.AreEqual(SettingsLoadResult.LoadedPrimary, result);
            Assert.IsFalse(File.Exists(file.TempPath));
            File.WriteAllText(file.SettingsPath, "broken");
            Assert.AreEqual("zh-CN", file.LoadOrDefault(out result).LanguageCode);
            Assert.AreEqual(SettingsLoadResult.LoadedBackup, result);
            File.WriteAllText(file.BackupPath, "{\"version\":99}");
            file.LoadOrDefault(out result);
            Assert.AreEqual(SettingsLoadResult.CreatedDefault, result);
        }

        [Test] public void SaveFailureReportsErrorWithoutCreatingTemporaryFile()
        {
            Directory.CreateDirectory(folder);
            var blockingFile = Path.Combine(folder, "blocked");
            File.WriteAllText(blockingFile, "occupied");
            var file = new SettingsFileStore(blockingFile);
            Assert.IsFalse(file.Save(SettingsData.CreateDefault(), out var error));
            Assert.IsNotEmpty(error);
            Assert.IsFalse(File.Exists(file.TempPath));
        }

        [Test] public void SanitizeAndCloneKeepIndependentValidData()
        {
            var data = new SettingsData { LanguageCode = "invalid" };
            data.Audio.MasterVolume = 5;
            data.Display.WindowMode = FullScreenMode.ExclusiveFullScreen;
            var clone = data.Clone();
            Assert.AreEqual("zh-CN", clone.LanguageCode);
            Assert.AreEqual(1f, clone.Audio.MasterVolume);
            Assert.AreEqual(FullScreenMode.Windowed, clone.Display.WindowMode);
            clone.HotKeys[0].BindingPath = "<Keyboard>/p";
            Assert.AreNotEqual(clone.HotKeys[0].BindingPath, data.HotKeys[0].BindingPath);
        }

        [Test] public void DirtyChangesDebounceAndShutdownFlushes()
        {
            settings.MarkDirty();
            clock.Now = .4f;
            LifeCycle.Update();
            Assert.AreEqual(0, store.Saves);
            settings.MarkDirty();
            clock.Now = .8f;
            LifeCycle.Update();
            Assert.AreEqual(0, store.Saves);
            clock.Now = 1f;
            LifeCycle.Update();
            Assert.AreEqual(1, store.Saves);
            settings.MarkDirty();
            FoundationRuntime.Shutdown();
            Assert.AreEqual(2, store.Saves);
            LifeCycle.Update();
            Assert.AreEqual(2, store.Saves);
        }

        [Test] public void PreviewTimeoutRollsBackWithoutSaving()
        {
            preview.BeginPreview("1920x1080", FullScreenMode.FullScreenWindow);
            Assert.AreEqual("1920x1080", display.Code);
            Assert.AreEqual("1280x720", architecture.GetModel<DisplaySettingsModel>().CurrentResolutionCode.Value);
            clock.Now = 11;
            LifeCycle.Update();
            Assert.IsFalse(preview.IsActive);
            Assert.AreEqual("1280x720", display.Code);
            Assert.AreEqual(0, store.Saves);
        }

        [Test] public void PreviewConfirmationPersistsAndNextPreviewCancelsToConfirmedValue()
        {
            preview.BeginPreview("1920x1080", FullScreenMode.FullScreenWindow);
            preview.Confirm();
            settings.Flush();
            Assert.AreEqual("1920x1080", store.Saved.Display.ResolutionCode);
            preview.BeginPreview("1600x900", FullScreenMode.Windowed);
            preview.Cancel();
            Assert.AreEqual("1920x1080", display.Code);
            Assert.AreEqual(FullScreenMode.FullScreenWindow, display.Mode);
        }

        [Test] public void ShutdownCancelsActivePreview()
        {
            preview.BeginPreview("1920x1080", FullScreenMode.Windowed);
            FoundationRuntime.Shutdown();
            Assert.IsFalse(preview.IsActive);
            Assert.AreEqual("1280x720", display.Code);
            Assert.AreEqual(0, store.Saves);
        }

        [Test] public void HotKeyRebindExecutesRegisteredActionAndPersists()
        {
            var file = new SettingsFileStore(folder);
            architecture.RegisterUtility<ISettingsFileStore>(file);
            int calls = 0;
            architecture.GetUtility<HotKeyActionRegistry>().Register(new HotKeyAction
            {
                ActionId = HotKeyActionIds.ToggleRuntimeLog,
                Execute = () => calls++
            });
            var hotkeys = architecture.GetSystem<HotKeySystem>();
            Assert.IsTrue(hotkeys.BeginRebind(HotKeyActionIds.ToggleRuntimeLog).Success);
            Assert.IsTrue(hotkeys.ReceiveBindingPath("<Keyboard>/l").Success);
            Assert.IsTrue(hotkeys.ExecuteBinding("<Keyboard>/l"));
            Assert.AreEqual(1, calls);
            Assert.IsTrue(settings.Flush());
            StringAssert.Contains("<Keyboard>/l", File.ReadAllText(file.SettingsPath));

            // 重建架构并从真实 JSON 读取，覆盖默认键表与已保存键表的合并。
            FoundationRuntime.Shutdown();
            architecture = Game.Game.Interface;
            architecture.RegisterUtility<ISettingsFileStore>(new SettingsFileStore(folder));
            architecture.RegisterUtility<IDisplayResolutionApplier>(display);
            FoundationRuntime.Initialize();
            architecture.GetUtility<HotKeyActionRegistry>().Register(new HotKeyAction
            {
                ActionId = HotKeyActionIds.ToggleRuntimeLog,
                Execute = () => calls++
            });
            hotkeys = architecture.GetSystem<HotKeySystem>();
            Assert.AreEqual(SettingsLoadResult.LoadedPrimary, architecture.GetSystem<SettingsSystem>().LastLoadResult);
            Assert.IsTrue(hotkeys.ExecuteBinding("<Keyboard>/l"), "重启后应使用文件中已保存的日志键");
            Assert.IsFalse(hotkeys.ExecuteBinding(HotKeyDefaults.F1));
            Assert.AreEqual(2, calls);
        }

        [Test] public void LanguageResourceAndBorderlessSizingUseReferenceRules()
        {
            Assert.IsNotEmpty(new EditorSourceResourceLoader().LoadAsset<TextAsset>("languages.json").text);
            Assert.AreEqual(2, LocalizationLanguages.All.Count);
            Assert.AreEqual(new Vector2Int(2560, 1440), UnityDisplayResolutionApplier.ResolveSize(DisplayResolutionPresets.Default, FullScreenMode.FullScreenWindow, 2560, 1440));
            Assert.AreEqual(new Vector2Int(1600, 900), UnityDisplayResolutionApplier.ResolveSize(DisplayResolutionPresets.Default, FullScreenMode.Windowed, 2560, 1440));
        }

        [Test]
        public void UpdateRestartRequiresFreshConfirmationOnlyAfterRunStarts()
        {
            var restart = new RestartSpy();
            architecture.RegisterUtility<IApplicationRestartUtility>(restart);
            architecture.RegisterModel(new PrototypeModel());
            var session = new PrototypeSystem();
            architecture.RegisterSystem(session);
            var viewType = typeof(PrototypeSettingsCtrl).Assembly.GetType("OutpostLoop.PrototypeSettingsView", true);
            var root = new GameObject("Update restart test");
            try
            {
                var view = root.AddComponent(viewType);
                void Invoke(string method) => viewType.GetMethod(method,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(view, null);
                session.Begin(731, false);
                Invoke("RequestUpdateRestart");
                Assert.AreEqual(1, restart.Calls, "开局前可直接重启");

                var world = session.Begin(731, true);
                Invoke("RequestUpdateRestart");
                Assert.AreEqual(1, restart.Calls, "局内第一次点击只能发起确认");
                Invoke("Cancel");
                Invoke("ConfirmUpdateRestart");
                Assert.AreEqual(1, restart.Calls, "关闭设置后旧确认失效");
                Assert.AreSame(world, architecture.GetModel<PrototypeModel>().World);

                Invoke("RequestUpdateRestart");
                Invoke("ConfirmUpdateRestart");
                Invoke("ConfirmUpdateRestart");
                Assert.AreEqual(2, restart.Calls, "确认只生效一次");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private sealed class RestartSpy : IApplicationRestartUtility
        {
            public int Calls { get; private set; }
            public bool TryRestart(out string error) { Calls++; error = null; return true; }
        }

        private sealed class Clock : ISettingsClock { public float Now { get; set; } }
        private sealed class DisplaySpy : IDisplayResolutionApplier
        {
            public string Code;
            public FullScreenMode Mode;
            public void Apply(DisplayResolutionPreset preset, FullScreenMode mode) { Code = preset.Code; Mode = mode; }
        }
        private sealed class MemoryStore : ISettingsFileStore
        {
            public int Loads;
            public int Saves;
            public SettingsData Saved;
            public SettingsData LoadOrDefault(out SettingsLoadResult result)
            {
                Loads++;
                result = SettingsLoadResult.LoadedPrimary;
                var data = SettingsData.CreateDefault();
                data.LanguageCode = "en";
                data.Display.ResolutionCode = "1280x720";
                return data;
            }
            public bool Save(SettingsData data, out string error) { Saves++; Saved = data.Clone(); error = null; return true; }
        }
    }
}
