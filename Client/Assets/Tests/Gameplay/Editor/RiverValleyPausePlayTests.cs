using System.Collections;
using System.IO;
using System.Reflection;
using Game;
using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace OutpostLoop.Tests
{
    /// <summary>从 Boot 暂停，验证帮助和设置的嵌套返回及设置事务。</summary>
    public sealed class RiverValleyPausePlayTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [UnityTest]
        public IEnumerator BootPauseHelpAndSettingsReturnToTheSameRiverValleyRun()
        {
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity");
            yield return new EnterPlayMode();
            for (int frame = 0; frame < 600 && GameObject.Find("SettingsEntry") == null; frame++) yield return null;
            var controller = Object.FindObjectOfType<PrototypeController>();
            Assert.NotNull(controller);
            var type = typeof(PrototypeController);
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Backup/Artifacts/RiverValleyPauseSettings-20261120"));
            Directory.CreateDirectory(folder);
            type.GetField("survivalStore", PrivateInstance).SetValue(controller, new JsonFileStore<SurvivalSave>(Path.Combine(folder, "Save-" + System.Guid.NewGuid())));
            type.GetField("survivalSaveChecked", PrivateInstance).SetValue(controller, false);
            type.GetMethod("RefreshOverlay", PrivateInstance).Invoke(controller, null);
            GameObject.Find("StartSurvival").GetComponent<Button>().onClick.Invoke();
            float deadline = Time.realtimeSinceStartup + 20;
            while (controller.IsCrossing && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            var run = (ScavengeRun)type.GetField("scavengeRun", PrivateInstance).GetValue(controller);
            Assert.NotNull(run);
            Assert.IsFalse(controller.IsCrossing);
            var portal = type.GetProperty("RiverValleyPortalPlaying", PrivateInstance);
            deadline = Time.realtimeSinceStartup + 15;
            while ((bool)portal.GetValue(controller) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsFalse((bool)portal.GetValue(controller), "开场过场结束后再测试局内按键");

            var architecture = Game.Game.Interface;
            var config = architecture.GetSystem<SettingsSystem>();
            var language = architecture.GetModel<LocalizationModel>();
            var settings = new SettingsData
            {
                LanguageCode = language.CurrentLanguageCode.Value,
                Audio = architecture.GetModel<AudioSettingsModel>().ToData(),
                Display = architecture.GetModel<DisplaySettingsModel>().ToData(),
                HotKeys = architecture.GetModel<HotKeyModel>().ToData(),
                TestCommands = architecture.GetSystem<DebugCommandSystem>().CopyPreferences(),
                Perception = config.Perception.Clone()
            };
            var store = architecture.GetUtility<ISettingsFileStore>();
            var originalDisplay = architecture.GetUtility<IDisplayResolutionApplier>();
            var display = new DisplaySpy();
            var memory = new MemoryStore();
            architecture.RegisterUtility<ISettingsFileStore>(memory);
            architecture.RegisterUtility<IDisplayResolutionApplier>(display);
            var input = InputSystem.settings;
            var oldBackground = input.backgroundBehavior;
            var oldEditorInput = input.editorInputBehaviorInPlayMode;
            input.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            input.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var poll = type.GetMethod("PollScavenge", PrivateInstance);
            var present = type.GetMethod("LateUpdate", PrivateInstance);
            var paused = type.GetField("scavengePaused", PrivateInstance);
            var preview = architecture.GetSystem<DisplayPreviewTransaction>();
            var keys = architecture.GetSystem<HotKeySystem>();
            var hotKeys = architecture.GetModel<HotKeyModel>();
            try
            {
                // 先核对默认 Esc，再通过设置事务保存自定义键。测试结束恢复用户原绑定。
                Assert.IsTrue(hotKeys.TryReplace(HotKeyDefaults.Create()[0]));
                keys.RebuildActions();
                language.SetLanguageCode("zh-CN");
                PressKey(Key.Tab);
                yield return null;
                Assert.NotNull(GameObject.Find("CloseManagement"));
                PressKey(Key.Escape);
                yield return null;
                Assert.IsNull(GameObject.Find("CloseManagement"));
                Assert.IsFalse((bool)paused.GetValue(controller));
                PressKey(Key.Escape);
                yield return null;
                yield return null;
                Assert.IsTrue((bool)paused.GetValue(controller));
                double elapsed = run.Survival.elapsed;
                yield return Capture("paused-zh");
                Press("PauseHelp");
                yield return null;
                yield return null;
                Assert.NotNull(GameObject.Find("HelpPanel"));
                Assert.AreEqual(elapsed, run.Survival.elapsed);
                yield return Capture("help-zh");
                Press("BackFromHelp");
                yield return null;
                yield return null;
                Assert.NotNull(GameObject.Find("PausePanel"));
                Press("PauseSettings");
                yield return null;
                yield return null;
                var ctrl = UIHelper.GetUICtrl<PrototypeSettingsCtrl>();
                Assert.IsTrue(ctrl.IsShow());
                Assert.IsNull(GameObject.Find("PausePanel"));
                Press("Language-en");
                yield return null;
                Assert.AreEqual("en", language.CurrentLanguageCode.Value);
                yield return Capture("settings-en");

                // 经过设置入口验证独立草稿、指针缩放及取消，保留同一个暂停中的行动。
                Assert.IsTrue(config.Flush());
                var savedBeforeLayout = memory.Saved;
                Press("PerceptionLayout");
                yield return null;
                var editor = GameObject.Find("PerceptionEditor");
                Assert.NotNull(editor);
                Assert.NotNull(editor.GetComponentInChildren<RawImage>().texture);
                if (!Application.isBatchMode)
                {
                    string evidence = Path.GetFullPath(Path.Combine(Application.dataPath,
                        "../../Backup/Artifacts/PerceptionLayout-20261120"));
                    Directory.CreateDirectory(evidence);
                    var capture = typeof(PrototypeEditorPlayTests).GetMethod("CaptureSurvivalEvidence",
                        BindingFlags.Static | BindingFlags.NonPublic);
                    yield return (IEnumerator)capture.Invoke(null,
                        new object[] { "layout-en", 1280, 720, "PerceptionLayout-20261120" });
                }
                Press("Restore");
                Press("Visibility");
                GameObject.Find("Scale").GetComponent<Slider>().value = 25;
                PressKey(Key.Escape, GameObject.Find("Settings"));
                Assert.IsNull(GameObject.Find("PerceptionEditor"));
                Assert.AreEqual(settings.Perception.Action.Visible, config.Perception.Action.Visible);
                Assert.AreSame(savedBeforeLayout, memory.Saved);
                yield return null;

                Press("PerceptionLayout");
                yield return null;
                Press("Restore");
                var mapPreview = GameObject.Find("PerceptionEditor").transform.Find("GamePreview/Map");
                var rect = (RectTransform)mapPreview;
                var start = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
                var gesture = new PointerEventData(EventSystem.current)
                    { button = PointerEventData.InputButton.Left, position = start };
                ExecuteEvents.Execute(mapPreview.gameObject, gesture, ExecuteEvents.pointerDownHandler);
                gesture.position += new Vector2(-180, -80);
                ExecuteEvents.Execute(mapPreview.gameObject, gesture, ExecuteEvents.dragHandler);
                ExecuteEvents.Execute(mapPreview.gameObject, gesture, ExecuteEvents.pointerUpHandler);
                GameObject.Find("Scale").GetComponent<Slider>().value = 25;
                Press("Visibility");
                memory.Fail = true;
                LogAssert.Expect(LogType.Error, "[SettingsSystem] 保存设置失败：Test save failed");
                Press("Apply");
                Assert.NotNull(GameObject.Find("PerceptionEditor"));
                Assert.AreEqual(settings.Perception.Map.Visible, config.Perception.Map.Visible);
                memory.Fail = false;
                Press("Apply");
                Assert.IsNull(GameObject.Find("PerceptionEditor"));
                Assert.IsTrue(ctrl.IsShow());
                Assert.IsTrue((bool)paused.GetValue(controller));
                Assert.AreEqual(elapsed, run.Survival.elapsed);
                Assert.AreEqual(false, memory.Saved.Perception.Map.Visible);
                Assert.AreEqual(1.25f, memory.Saved.Perception.Map.Scale);
                Assert.Less(memory.Saved.Perception.Map.X.Value, 1);
                Assert.AreEqual(config.Perception.Map.X, memory.Saved.Perception.Map.X);
                yield return null;

                Press("SettingsNavAudio");
                var slider = GameObject.Find("MasterVolume").GetComponent<Slider>();
                slider.value = .42f;
                Assert.AreEqual(.42f, architecture.GetModel<AudioSettingsModel>().MasterVolume.Value);
                yield return Capture("audio-en");
                Press("SettingsNavGeneral");
                yield return null;
                Press("Resolution-1280x720");
                yield return null;
                Press("PreviewDisplay");
                yield return null;
                Assert.IsTrue(preview.IsActive);
                Assert.AreEqual("1280x720", display.Code);
                Assert.NotNull(GameObject.Find("DisplayCountdown"));
                PressKey(Key.Escape, GameObject.Find("Settings"));
                yield return null;
                Assert.IsFalse(preview.IsActive);
                Assert.AreEqual(settings.Display.ResolutionCode, display.Code);
                Assert.IsTrue(ctrl.IsShow());

                Press("SettingsNavControls");
                yield return null;
                Press("Bind-" + HotKeyActionIds.TogglePause);
                Assert.IsTrue(architecture.GetSystem<HotKeySystem>().IsCapturingRebind);
                PressKey(Key.Escape, GameObject.Find("Settings"));
                yield return null;
                Assert.IsFalse(keys.IsCapturingRebind);
                Assert.IsTrue(ctrl.IsShow());
                yield return null;
                Press("Bind-" + HotKeyActionIds.TogglePause);
                Assert.IsTrue(keys.ReceiveBindingPath("<Keyboard>/p").Success);
                yield return null;
                PressKey(Key.P, GameObject.Find("Settings"));
                Assert.IsTrue(ctrl.IsShow());
                Assert.IsTrue((bool)paused.GetValue(controller));
                PressKey(Key.Escape, GameObject.Find("Settings"));
                yield return null;
                yield return null;
                Assert.IsFalse(ctrl.IsShow());
                Assert.AreEqual(.42f, memory.Saved.Audio.MasterVolume);
                Assert.IsTrue(memory.Saved.HotKeys.Exists(binding =>
                    binding.ActionId == HotKeyActionIds.TogglePause && binding.BindingPath == "<Keyboard>/p"));
                Assert.IsTrue((bool)paused.GetValue(controller));
                Assert.AreEqual(elapsed, run.Survival.elapsed);
                PressKey(Key.P);
                yield return null;
                Assert.IsFalse((bool)paused.GetValue(controller));
                Assert.Greater(run.Survival.elapsed, elapsed);
                Assert.AreSame(run, type.GetField("scavengeRun", PrivateInstance).GetValue(controller));

                PressKey(Key.Escape);
                Assert.IsFalse((bool)paused.GetValue(controller), "自定义键后 Esc 不再从探索状态打开暂停");
                PressKey(Key.Tab);
                yield return null;
                Assert.NotNull(GameObject.Find("CloseManagement"));
                PressKey(Key.P);
                yield return null;
                Assert.NotNull(GameObject.Find("CloseManagement"), "自定义暂停键不抢占物品页");
                Assert.IsFalse((bool)paused.GetValue(controller));
                PressKey(Key.Escape);
                yield return null;
                Assert.IsNull(GameObject.Find("CloseManagement"));

                var mapGroup = type.GetMethod("PerceptionRect", PrivateInstance);
                var hit = type.GetMethod("SurvivalHudContains", PrivateInstance);
                var movedMap = (Rect)mapGroup.Invoke(controller, new object[] { PerceptionGroup.Map });
                var cursor = new Vector2(movedMap.center.x * PerceptionGeometry.ScreenScale,
                    Screen.height - movedMap.center.y * PerceptionGeometry.ScreenScale);
                Assert.IsFalse((bool)hit.Invoke(controller, new object[] { cursor }), "隐藏地图不截获场景输入");
                PressKey(Key.M);
                var map = type.GetField("survivalRegionMapOpen", PrivateInstance);
                Assert.IsTrue((bool)map.GetValue(controller));
                PressKey(Key.P);
                Assert.IsTrue((bool)map.GetValue(controller), "自定义暂停键不抢占地图");
                Assert.IsFalse((bool)paused.GetValue(controller));
                PressKey(Key.Escape);
                Assert.IsFalse((bool)map.GetValue(controller));
                var shown = config.Perception.Clone();
                shown.Map.Visible = true;
                Assert.IsTrue(config.SavePerception(shown, out var showError), showError);
                Assert.IsTrue((bool)hit.Invoke(controller, new object[] { cursor }), "地图恢复后在新位置截获场景输入");

                PressKey(Key.P);
                yield return null;
                Assert.IsTrue((bool)paused.GetValue(controller));
                Press("ResumeSurvival");
                yield return null;
                Assert.IsFalse((bool)paused.GetValue(controller));
            }
            finally
            {
                UIHelper.HideUI<PrototypeSettingsCtrl>();
                config.Apply(settings);
                architecture.GetSystem<AudioSystem>().ApplyVolumes();
                config.Flush();
                architecture.RegisterUtility<ISettingsFileStore>(store);
                architecture.RegisterUtility<IDisplayResolutionApplier>(originalDisplay);
                InputSystem.RemoveDevice(keyboard);
                input.backgroundBehavior = oldBackground;
                input.editorInputBehaviorInPlayMode = oldEditorInput;
            }
            yield return new ExitPlayMode();

            void PressKey(Key key, GameObject settingsObject = null)
            {
                keyboard.MakeCurrent();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
                InputSystem.Update();
                Assert.IsTrue(keyboard[key].wasPressedThisFrame, "未收到注入键 " + key);
                poll.Invoke(controller, null);
                present.Invoke(controller, null);
                if (settingsObject) settingsObject.SendMessage("LateUpdate");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.Update();
            }
        }

        private static IEnumerator Capture(string name)
        {
            if (Application.isBatchMode) yield break;
            var capture = typeof(PrototypeEditorPlayTests).GetMethod("CaptureSurvivalEvidence", BindingFlags.Static | BindingFlags.NonPublic);
            yield return (IEnumerator)capture.Invoke(null, new object[] {name, 1280, 720, "RiverValleyPauseSettings-20261120"});
        }

        private static void Press(string name)
        {
            var node = GameObject.Find(name);
            Assert.NotNull(node, name);
            var button = node.GetComponent<Button>();
            Assert.IsTrue(button.interactable, name);
            Canvas.ForceUpdateCanvases();
            var rect = (RectTransform)button.transform;
            var position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            var pointer = new PointerEventData(EventSystem.current) {position = position, button = PointerEventData.InputButton.Left};
            var hits = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            Assert.IsNotEmpty(hits, name);
            Assert.AreSame(node, ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject), name);
            ExecuteEvents.Execute(node, pointer, ExecuteEvents.pointerClickHandler);
        }

        private sealed class MemoryStore : ISettingsFileStore
        {
            public SettingsData LoadOrDefault(out SettingsLoadResult result) { result = SettingsLoadResult.CreatedDefault; return SettingsData.CreateDefault(); }
            public SettingsData Saved { get; private set; }
            public bool Fail { get; set; }
            public bool Save(SettingsData data, out string error)
            {
                if (Fail)
                {
                    error = "Test save failed";
                    return false;
                }
                Saved = data;
                error = null;
                return true;
            }
        }

        private sealed class DisplaySpy : IDisplayResolutionApplier
        {
            public string Code { get; private set; }
            public void Apply(DisplayResolutionPreset preset, FullScreenMode mode) => Code = preset.Code;
        }
    }
}
