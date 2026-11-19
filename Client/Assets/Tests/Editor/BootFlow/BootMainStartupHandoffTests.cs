using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Boot;
using Boot.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.Tests.Flow
{
    [TestFixture]
    public sealed class BootMainStartupHandoffTests
    {
        private const string BootScenePath = "Assets/Game/Scenes/Boot.unity";
        private const string LauncherPath = "Assets/Boot/Launcher.cs";

        [TearDown]
        public void TearDown()
        {
            SetCurrentLauncher(null);
        }

        [Test]
        public void BootSceneBindsRequiredViewAndBlocksInputBehindOverlay()
        {
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.OpenScene(BootScenePath, OpenSceneMode.Additive);
            try
            {
                // 按真实组件和序列化绑定验收，临时面板不依赖参考项目美术命名或色值。
                var launcher = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Launcher>(true)).Single();
                var serialized = new SerializedObject(launcher);
                var canvas = serialized.FindProperty("_updateCanvas").objectReferenceValue as Canvas;
                var view = serialized.FindProperty("_updateView").objectReferenceValue as HotUpdateView;
                Assert.NotNull(canvas, "Launcher 必须绑定启动遮罩 Canvas。");
                Assert.NotNull(view, "Launcher 必须绑定 HotUpdateView。");
                Assert.That(view.transform.IsChildOf(canvas.transform), Is.True);
                Assert.That(canvas.GetComponent<GraphicRaycaster>(), Is.Not.Null, "启动遮罩必须接收 UI 射线。");
                InvokePrivate(launcher, "ValidateRequiredReferences");
                InvokePrivate(view, "ValidateRequiredReferences");
                var viewData = new SerializedObject(view);
                var progress = (GameObject)viewData.FindProperty("_progressArea").objectReferenceValue;
                var stats = (GameObject)viewData.FindProperty("_downloadStatsArea").objectReferenceValue;
                Assert.IsTrue(((Text)viewData.FindProperty("_progressText").objectReferenceValue).transform.IsChildOf(progress.transform));
                foreach(var field in new[]{"_sizeText","_fileCountText","_speedText"})
                    Assert.IsTrue(((Text)viewData.FindProperty(field).objectReferenceValue).transform.IsChildOf(stats.transform),field+" 必须随统计区域显隐");
                var blockers = canvas.GetComponentsInChildren<Image>(true).Where(image =>
                    image.raycastTarget && image.color.a > 0 && image.gameObject.activeSelf &&
                    image.rectTransform.anchorMin == Vector2.zero && image.rectTransform.anchorMax == Vector2.one &&
                    image.rectTransform.offsetMin == Vector2.zero && image.rectTransform.offsetMax == Vector2.zero).ToArray();
                Assert.That(blockers, Is.Not.Empty, "启动 Canvas 必须存在覆盖父区域且接收射线的遮罩。");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded)
                {
                    SceneManager.SetActiveScene(previous);
                }
            }
        }

        [Test]
        public void BootCanvasIsBoundAndSortsAboveMainUiRoot()
        {
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.OpenScene(BootScenePath, OpenSceneMode.Additive);
            try
            {
                var launcher = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<Launcher>(true))
                    .Single();
                var canvas = new SerializedObject(launcher).FindProperty("_updateCanvas").objectReferenceValue as Canvas;
                Assert.NotNull(canvas);
                Assert.That(canvas.overrideSorting, Is.True);
                Assert.That(canvas.sortingOrder, Is.GreaterThan(0));

                var serialized = new SerializedObject(launcher);
                var canvasProperty = serialized.FindProperty("_updateCanvas");
                var viewProperty = serialized.FindProperty("_updateView");
                Assert.NotNull(canvasProperty);
                Assert.NotNull(viewProperty);
                Assert.That(canvasProperty.objectReferenceValue, Is.SameAs(canvas));
                var view = (HotUpdateView)viewProperty.objectReferenceValue;
                Assert.NotNull(view);
                Assert.That(view.transform.IsChildOf(canvas.transform), Is.True);
                Assert.That(canvas.transform.parent, Is.Null);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded)
                {
                    SceneManager.SetActiveScene(previous);
                }
            }
        }

        [Test]
        public void LauncherExposesOnlyNarrowMainStartupReportingContract()
        {
            Assert.That(Enum.GetNames(typeof(BootMainStartupStage)), Is.EqualTo(new[]
            {
                "InitializingGame",
                "LoadingGameConfig",
                "PreparingStartUI"
            }));

            AssertStaticBoolMethod(nameof(Launcher.TryReportMainStage), typeof(BootMainStartupStage));
            AssertStaticBoolMethod(nameof(Launcher.TryReportMainReady));
            AssertStaticBoolMethod(nameof(Launcher.TryReportMainFailure));
        }

        [Test]
        public void LauncherKeepsOverlayVisibleUntilMainReady()
        {
            var fixture = CreateLauncherFixture();
            try
            {
                string source = File.ReadAllText(LauncherPath);
                Assert.That(source.IndexOf("_updateView.RenderNormal(\"正在进入游戏……\")", StringComparison.Ordinal),
                    Is.LessThan(source.IndexOf("ResourceSceneLoader.LoadMainAsync()", StringComparison.Ordinal)));
                Assert.That(source, Does.Not.Contain("_updateView.gameObject.SetActive(false);"));
                Assert.That(source, Does.Contain("BootFailureCode.MainSceneLoadFailed"));
                Assert.That(source, Does.Contain("BootRetryScope.MainScene"));
                Assert.That(source, Does.Contain("_updateView.gameObject.SetActive(true);"));
                Assert.That(source, Does.Contain("_isStarting = false;"));
                Assert.That(source, Does.Contain("await UniTask.NextFrame(cancellationToken:"));
                Assert.That(source, Does.Contain("DestroyOwnedObject(_updateCanvas.gameObject);"));
                Assert.That(source, Does.Contain("DestroyOwnedObject(gameObject);"));
                Assert.That(source, Does.Not.Contain("Boot Camera"));
                Assert.That(source, Does.Not.Contain("Boot Light"));
                Assert.That(source, Does.Not.Contain("EventSystem"));

                InvokePrivate(fixture.Launcher, "ValidateRequiredReferences");
                SetCurrentLauncher(fixture.Launcher);
                Assert.That(Launcher.TryReportMainStage(BootMainStartupStage.InitializingGame), Is.True);
                Assert.That(fixture.Status.text, Is.EqualTo("正在初始化游戏……"));
                Assert.That(fixture.View.gameObject.activeSelf, Is.True);

                fixture.Status.text = string.Empty;
                Assert.That(Launcher.TryReportMainStage(BootMainStartupStage.LoadingGameConfig), Is.True);
                Assert.That(fixture.Status.text, Is.EqualTo("正在加载游戏配置……"));
                Assert.That(Launcher.TryReportMainStage(BootMainStartupStage.PreparingStartUI), Is.True);
                Assert.That(fixture.Status.text, Is.EqualTo("正在准备开始界面……"));
                Assert.That(Launcher.TryReportMainReady(), Is.True);
                Assert.That(GetTerminalState(fixture.Launcher), Is.EqualTo("ReadyPendingRelease"));
                Assert.That(Launcher.TryReportMainStage(BootMainStartupStage.InitializingGame), Is.False);
                Assert.That(Launcher.TryReportMainFailure(), Is.False);
                Assert.That(GetCurrentLauncher(), Is.SameAs(fixture.Launcher));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(fixture.Root);
                UnityEngine.Object.DestroyImmediate(fixture.CanvasRoot);
            }
        }

        [Test]
        public void LauncherMainTerminalStateIsFirstWriterWins()
        {
            var fixture = CreateLauncherFixture();
            var readyFixture = CreateLauncherFixture("Stage9ReadyLauncher", "HotUpdateCanvasReady");
            var newOwnerFixture = CreateLauncherFixture("Stage9NewOwner", "HotUpdateCanvasNewOwner");
            var duplicateFixture = CreateLauncherFixture("Stage9Duplicate", "HotUpdateCanvasDuplicate");
            try
            {
                SetCurrentLauncher(fixture.Launcher);
                fixture.RetryButton.gameObject.SetActive(true);
                Assert.That(Launcher.TryReportMainFailure(), Is.True);
                Assert.That(fixture.Title.text, Is.EqualTo("游戏初始化失败"));
                Assert.That(fixture.RetryButton.gameObject.activeSelf, Is.False);
                Assert.That(fixture.CopyButton.gameObject.activeSelf, Is.True);
                Assert.That(fixture.QuitButton.gameObject.activeSelf, Is.True);
                Assert.That(Launcher.TryReportMainReady(), Is.False);
                Assert.That(Launcher.TryReportMainFailure(), Is.False);

                SetCurrentLauncher(readyFixture.Launcher);
                Assert.That(Launcher.TryReportMainReady(), Is.True);
                Assert.That(Launcher.TryReportMainFailure(), Is.False);
                Assert.That(GetTerminalState(readyFixture.Launcher), Is.EqualTo("ReadyPendingRelease"));

                SetCurrentLauncher(newOwnerFixture.Launcher);
                InvokePrivate(readyFixture.Launcher, "OnDestroy");
                Assert.That(GetCurrentLauncher(), Is.SameAs(newOwnerFixture.Launcher));

                SetCurrentLauncher(newOwnerFixture.Launcher);
                Assert.That(duplicateFixture.CanvasRoot.activeSelf, Is.True);
                InvokePrivate(duplicateFixture.Launcher, "Awake");
                Assert.That(GetCurrentLauncher(), Is.SameAs(newOwnerFixture.Launcher));
                Assert.That(duplicateFixture.Launcher.enabled, Is.False);
                Assert.That(duplicateFixture.CanvasRoot == null, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(fixture.Root);
                UnityEngine.Object.DestroyImmediate(fixture.CanvasRoot);
                UnityEngine.Object.DestroyImmediate(readyFixture.Root);
                UnityEngine.Object.DestroyImmediate(readyFixture.CanvasRoot);
                UnityEngine.Object.DestroyImmediate(newOwnerFixture.Root);
                UnityEngine.Object.DestroyImmediate(newOwnerFixture.CanvasRoot);
                UnityEngine.Object.DestroyImmediate(duplicateFixture.Root);
                UnityEngine.Object.DestroyImmediate(duplicateFixture.CanvasRoot);
            }
        }

        [Test]
        public void LauncherMainFailureCopiesSafeMainDiagnostics()
        {
            var fixture = CreateLauncherFixture();
            try
            {
                SetCurrentLauncher(fixture.Launcher);
                Assert.That(Launcher.TryReportMainFailure(new System.Collections.Generic.Dictionary<string, string>
                {
                    ["mainPhase"] = "SettingsLoadAndApply",
                    ["exceptionType"] = "System.MissingMethodException",
                    ["hResult"] = "-2146233069"
                }), Is.True);

                Assert.That(fixture.Title.text, Is.EqualTo("游戏初始化失败"));
                Assert.That(fixture.RetryButton.gameObject.activeSelf, Is.False);
                Assert.That(fixture.CopyButton.gameObject.activeSelf, Is.True);
                Assert.That(fixture.QuitButton.gameObject.activeSelf, Is.True);
                Assert.That(fixture.View, Is.Not.Null);

                var failure = GetCurrentFailure(fixture.Launcher);
                Assert.That(failure.DiagnosticDetails, Does.Contain("Code: MainInitializationFailed"));
                Assert.That(failure.DiagnosticDetails, Does.Contain("Stage: MainInitialization"));
                Assert.That(failure.DiagnosticDetails, Does.Contain("RetryScope: None"));
                Assert.That(failure.DiagnosticDetails, Does.Contain("mainPhase: SettingsLoadAndApply"));
                Assert.That(failure.DiagnosticDetails, Does.Contain("exceptionType: System.MissingMethodException"));
                Assert.That(failure.DiagnosticDetails, Does.Contain("hResult: -2146233069"));
                Assert.That(failure.DiagnosticDetails, Is.Not.StringContaining("secret"));
                Assert.That(failure.DiagnosticDetails, Is.Not.StringContaining("C:/Users/Admin"));
                Assert.That(failure.DiagnosticDetails, Is.Not.StringContaining(" at "));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(fixture.Root);
                UnityEngine.Object.DestroyImmediate(fixture.CanvasRoot);
            }
        }

        [Test]
        public void BootEventSystemDoesNotSurviveMainHandoff()
        {
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.OpenScene(BootScenePath, OpenSceneMode.Additive);
            try
            {
                var eventSystems = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<EventSystem>(true))
                    .ToArray();
                Assert.That(eventSystems, Has.Length.EqualTo(1));
                Assert.That(eventSystems[0].GetComponent(GetInputSystemUiInputModuleType()), Is.Not.Null);
                Assert.That(eventSystems[0].GetComponent<StandaloneInputModule>(), Is.Null);
                Assert.That(eventSystems[0].transform.parent, Is.Null);

                var launcher = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<Launcher>(true))
                    .Single();
                SetCurrentLauncher(launcher);
                Assert.That(Launcher.TryReportMainReady(), Is.True);
                Assert.That(GetTerminalState(launcher), Is.EqualTo("ReadyPendingRelease"));
                Assert.That(eventSystems[0].gameObject.scene, Is.EqualTo(scene));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded)
                {
                    SceneManager.SetActiveScene(previous);
                }
            }
        }

        [Test]
        public void HostProgressAcceptsNewBusinessStageAndRejectsLateReports()
        {
            var fixture = CreateLauncherFixture();
            try
            {
                SetCurrentLauncher(fixture.Launcher);
                Assert.IsTrue(BootHost.ReportProgress("正在初始化新增业务……"));
                Assert.AreEqual("正在初始化新增业务……", fixture.Status.text);
                Assert.IsFalse(BootHost.ReportProgress(" "));
                Assert.IsTrue(BootHost.ReportFailure(null));
                Assert.IsFalse(BootHost.ReportProgress("迟到进度"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(fixture.Root);
                UnityEngine.Object.DestroyImmediate(fixture.CanvasRoot);
            }
        }

        private static void AssertStaticBoolMethod(string name, params Type[] parameterTypes)
        {
            var method = typeof(Launcher).GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, parameterTypes, null);
            Assert.NotNull(method, name);
            Assert.That(method.ReturnType, Is.EqualTo(typeof(bool)));
        }

        private static void InvokePrivate(object target, string name)
        {
            var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method, name);
            method.Invoke(target, null);
        }

        private static void SetCurrentLauncher(Launcher launcher)
        {
            var field = typeof(Launcher).GetField("current", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field.SetValue(null, launcher);
        }

        private static Launcher GetCurrentLauncher()
        {
            var field = typeof(Launcher).GetField("current", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return (Launcher)field.GetValue(null);
        }

        private static string GetTerminalState(Launcher launcher)
        {
            var field = typeof(Launcher).GetField("_terminalState", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return field.GetValue(launcher).ToString();
        }

        private static BootFailure GetCurrentFailure(Launcher launcher)
        {
            var field = typeof(Launcher).GetField("_currentFailure", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return (BootFailure)field.GetValue(launcher);
        }

        private static Type GetInputSystemUiInputModuleType()
        {
            Type type = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            Assert.That(type, Is.Not.Null);
            return type;
        }

        private static LauncherFixture CreateLauncherFixture(bool withCanvas = true)
        {
            return CreateLauncherFixture("Stage9Launcher", "HotUpdateCanvas", withCanvas);
        }

        private static LauncherFixture CreateLauncherFixture(string launcherName, string canvasName, bool withCanvas = true)
        {
            var root = new GameObject(launcherName);
            var canvasObject = new GameObject(canvasName, typeof(RectTransform), typeof(Canvas));
            var viewObject = new GameObject("Panel");
            viewObject.transform.SetParent(canvasObject.transform, false);
            var launcher = root.AddComponent<Launcher>();
            root.AddComponent<ResourceBoot>();
            var view = viewObject.AddComponent<HotUpdateView>();

            var status = AddText(viewObject.transform, "_statusText");
            var version = AddText(viewObject.transform, "_versionText");
            var progressText = AddText(viewObject.transform, "_progressText");
            var sizeText = AddText(viewObject.transform, "_sizeText");
            var fileCountText = AddText(viewObject.transform, "_fileCountText");
            var speedText = AddText(viewObject.transform, "_speedText");
            var slider = new GameObject("_progressSlider").AddComponent<Slider>();
            slider.transform.SetParent(viewObject.transform, false);
            var downloadStats = new GameObject("_downloadStatsArea");
            downloadStats.transform.SetParent(viewObject.transform, false);
            var buttonArea = new GameObject("_buttonArea");
            buttonArea.transform.SetParent(viewObject.transform, false);
            var retry = new GameObject("_retryButton").AddComponent<Button>();
            retry.transform.SetParent(buttonArea.transform, false);
            var copy = new GameObject("_copyDetailsButton").AddComponent<Button>();
            copy.transform.SetParent(buttonArea.transform, false);
            var quit = new GameObject("_quitButton").AddComponent<Button>();
            quit.transform.SetParent(buttonArea.transform, false);

            var viewSerialized = new SerializedObject(view);
            viewSerialized.FindProperty("_statusText").objectReferenceValue = status;
            viewSerialized.FindProperty("_versionText").objectReferenceValue = version;
            viewSerialized.FindProperty("_titleText").objectReferenceValue = progressText;
            viewSerialized.FindProperty("_guidanceText").objectReferenceValue = sizeText;
            viewSerialized.FindProperty("_detailsText").objectReferenceValue = speedText;
            viewSerialized.FindProperty("_progressArea").objectReferenceValue = slider.gameObject;
            viewSerialized.FindProperty("_downloadStatsArea").objectReferenceValue = downloadStats;
            viewSerialized.FindProperty("_buttonArea").objectReferenceValue = buttonArea;
            viewSerialized.FindProperty("_progressSlider").objectReferenceValue = slider;
            viewSerialized.FindProperty("_progressText").objectReferenceValue = progressText;
            viewSerialized.FindProperty("_sizeText").objectReferenceValue = sizeText;
            viewSerialized.FindProperty("_fileCountText").objectReferenceValue = fileCountText;
            viewSerialized.FindProperty("_speedText").objectReferenceValue = speedText;
            viewSerialized.FindProperty("_retryButton").objectReferenceValue = retry;
            viewSerialized.FindProperty("_copyDetailsButton").objectReferenceValue = copy;
            viewSerialized.FindProperty("_quitButton").objectReferenceValue = quit;
            viewSerialized.ApplyModifiedPropertiesWithoutUndo();

            var launcherSerialized = new SerializedObject(launcher);
            launcherSerialized.FindProperty("_updateView").objectReferenceValue = view;
            if (withCanvas)
            {
                launcherSerialized.FindProperty("_updateCanvas").objectReferenceValue = canvasObject.GetComponent<Canvas>();
            }
            launcherSerialized.ApplyModifiedPropertiesWithoutUndo();

            return new LauncherFixture(root, canvasObject, launcher, view, status, progressText, retry, copy, quit);
        }

        private static Text AddText(Transform parent, string name)
        {
            var text = new GameObject(name).AddComponent<Text>();
            text.transform.SetParent(parent, false);
            return text;
        }

        private readonly struct LauncherFixture
        {
            public LauncherFixture(
                GameObject root,
                GameObject canvasRoot,
                Launcher launcher,
                HotUpdateView view,
                Text status,
                Text title,
                Button retryButton,
                Button copyButton,
                Button quitButton)
            {
                Root = root;
                CanvasRoot = canvasRoot;
                Launcher = launcher;
                View = view;
                Status = status;
                Title = title;
                RetryButton = retryButton;
                CopyButton = copyButton;
                QuitButton = quitButton;
            }

            public GameObject Root { get; }
            public GameObject CanvasRoot { get; }
            public Launcher Launcher { get; }
            public HotUpdateView View { get; }
            public Text Status { get; }
            public Text Title { get; }
            public Button RetryButton { get; }
            public Button CopyButton { get; }
            public Button QuitButton { get; }
        }
    }
}
