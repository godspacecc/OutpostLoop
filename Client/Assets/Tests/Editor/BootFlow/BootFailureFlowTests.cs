using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Boot;
using Boot.UI;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.Tests.Flow
{
    [TestFixture]
    public sealed class BootFailureFlowTests
    {
        private const string BootScenePath = "Assets/Game/Scenes/Boot.unity";

        [TearDown]
        public void TearDown()
        {
            Launcher.ResetTestPorts();
            ResourceBoot.ResetTestHooks();
            CodeLoader.ResetTestHooks();
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(ResourcePlayMode.HostPlayMode, true, false, BootFailureCode.ResourceDownloadFailed, BootRetryScope.ResourceInitialization)]
        [TestCase(ResourcePlayMode.OfflinePlayMode, true, false, BootFailureCode.BuiltinResourceInvalid, BootRetryScope.None)]
        [TestCase(ResourcePlayMode.OfflinePlayMode, false, true, BootFailureCode.BuiltinResourceInvalid, BootRetryScope.None)]
        public void StartupAvailabilityRejectsMissingDependenciesBeforeLoad(ResourcePlayMode mode,
            bool needsDownload, bool queryThrows, BootFailureCode code, BootRetryScope retry)
        {
            var owner = new GameObject("资源可用性验证");
            try
            {
                var resource = owner.AddComponent<ResourceBoot>();
                typeof(ResourceBoot).GetField("_actualPlayMode", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(resource, mode);
                ResourceBoot.TestNeedsDownload = address => queryThrows ? throw new InvalidOperationException("测试：依赖无文件系统归属") : needsDownload;
                var result = (BootOperationResult)typeof(ResourceBoot).GetMethod("ValidateAvailableAddress", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(resource, new object[] { "Scenes/Main" });
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Failure.Code, Is.EqualTo(code));
                Assert.That(result.Failure.RetryScope, Is.EqualTo(retry));
                Assert.That(result.Failure.Stage, Is.EqualTo(BootFailureStage.ResourceValidation));
                Assert.That(result.Failure.DiagnosticDetails, Does.Contain("resourceAddress: Main"));
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        [TestCase(ResourcePlayMode.HostPlayMode)]
        [TestCase(ResourcePlayMode.OfflinePlayMode)]
        public void AvailableStartupDependenciesPass(ResourcePlayMode mode)
        {
            var owner = new GameObject("资源可用性验证");
            try
            {
                var resource = owner.AddComponent<ResourceBoot>();
                typeof(ResourceBoot).GetField("_actualPlayMode", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(resource, mode);
                ResourceBoot.TestNeedsDownload = _ => false;
                var result = (BootOperationResult)typeof(ResourceBoot).GetMethod("ValidateAvailableAddress", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(resource, new object[] { "Scenes/Main" });
                Assert.That(result.Succeeded, Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        [Test]
        public void FailedLocalFallbackPreservesNetworkRetryAndSuccessfulFallbackWins()
        {
            var network = BootOperationResult.Failed(BootFailure.Create(
                BootFailureCode.PackageVersionRequestFailed, BootFailureStage.ResourceVersion,
                BootRetryScope.ResourceInitialization));
            var builtin = BootOperationResult.Failed(BootFailure.Create(
                BootFailureCode.BuiltinResourceInvalid, BootFailureStage.ResourceInitialization));
            Assert.That(ResourceBoot.ResolveHostFallback(network, builtin), Is.SameAs(network));
            Assert.That(ResourceBoot.ResolveHostFallback(network, builtin).Failure.RetryScope,
                Is.EqualTo(BootRetryScope.ResourceInitialization));
            var success = BootOperationResult.Success();
            Assert.That(ResourceBoot.ResolveHostFallback(network, success), Is.SameAs(success));
        }

        [Test]
        public void IncompatibleBuiltinIsTerminalEvenWhenNetworkFailed()
        {
            var network = BootOperationResult.Failed(BootFailure.Create(
                BootFailureCode.PackageVersionRequestFailed, BootFailureStage.ResourceVersion,
                BootRetryScope.ResourceInitialization));
            var incompatible = BootOperationResult.Failed(BootFailure.Create(
                BootFailureCode.HostIncompatible, BootFailureStage.ResourceValidation, BootRetryScope.None));
            var result = ResourceBoot.ResolveHostFallback(network, incompatible);
            Assert.That(result, Is.SameAs(incompatible));
            Assert.That(result.Failure.RetryScope, Is.EqualTo(BootRetryScope.None));
        }

        [Test]
        public void ResultFactoriesRejectInvalidFailure()
        {
            var success = BootOperationResult.Success();
            Assert.That(success.Succeeded, Is.True);
            Assert.That(success.Failure, Is.Null);

            var failure = BootFailure.Create(BootFailureCode.GameDllMissing, BootFailureStage.GameAssembly);
            var failed = BootOperationResult.Failed(failure);
            Assert.That(failed.Succeeded, Is.False);
            Assert.That(failed.Failure, Is.SameAs(failure));
            Assert.Throws<ArgumentNullException>(() => BootOperationResult.Failed(null));
        }

        [Test]
        public void StructuredFailuresComeFromOwningLoaders()
        {
            var fixture = CreateLauncherFixture();
            try
            {
                ResourceBoot.TestRawInitializationOutcome = () => UniTask.FromResult("host-final-builtin-invalid");
                fixture.ResourceBoot.Configure(fixture.View);
                BootOperationResult resourceResult = fixture.ResourceBoot.InitializeAsync().GetAwaiter().GetResult();
                Assert.That(resourceResult.Succeeded, Is.False);
                Assert.That(resourceResult.Failure.Code, Is.EqualTo(BootFailureCode.BuiltinResourceInvalid));
                Assert.That(resourceResult.Failure.Stage, Is.EqualTo(BootFailureStage.ResourceInitialization));
                Assert.That(resourceResult.Failure.RetryScope, Is.EqualTo(BootRetryScope.None));

                ConfigureCodeLoaderAotMissing("mscorlib.dll");
                BootOperationResult codeResult = InvokeCodeLoaderLoadAsync();
                Assert.That(codeResult.Succeeded, Is.False);
                Assert.That(codeResult.Failure.Code, Is.EqualTo(BootFailureCode.AotDllMissing));
                Assert.That(codeResult.Failure.Stage, Is.EqualTo(BootFailureStage.AotMetadata));
                Assert.That(codeResult.Failure.RetryScope, Is.EqualTo(BootRetryScope.None));
                Assert.That(codeResult.Failure.DiagnosticDetails, Does.Contain("mscorlib.dll"));
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void FatalCodeFailuresOfferCopyAndQuitOnly()
        {
            var fixture = CreateLauncherFixture();
            try
            {
                ResourceBoot.TestRawInitializationOutcome = () => UniTask.FromResult("success");
                ConfigureCodeLoaderMetadataRejected("mscorlib.dll", 17);

                fixture.Launcher.StartForTestsAsync().GetAwaiter().GetResult();

                Assert.That(fixture.Title.text, Is.EqualTo("AOT 元数据不兼容"));
                Assert.That(fixture.Guidance.text, Does.Contain("完整安装包"));
                Assert.That(fixture.RetryButton.gameObject.activeSelf, Is.False);
                Assert.That(fixture.CopyButton.gameObject.activeSelf, Is.True);
                Assert.That(fixture.QuitButton.gameObject.activeSelf, Is.True);
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void RetryUsesTheFailureRetryScope()
        {
            var fixture = CreateLauncherFixture();
            var retryGate = new UniTaskCompletionSource<string>();
            int resourceCalls = 0;
            int sceneCalls = 0;
            try
            {
                ResourceBoot.TestRawInitializationOutcome = () =>
                {
                    resourceCalls++;
                    return resourceCalls == 1
                        ? UniTask.FromResult("resource-download-failed")
                        : retryGate.Task;
                };
                Launcher.TestMainSceneLoader = () =>
                {
                    sceneCalls++;
                    return UniTask.FromResult(true);
                };

                fixture.Launcher.StartForTestsAsync().GetAwaiter().GetResult();
                fixture.RetryButton.onClick.Invoke();
                fixture.RetryButton.onClick.Invoke();

                Assert.That(resourceCalls, Is.EqualTo(2));
                Assert.That(sceneCalls, Is.EqualTo(0));
                Assert.That(fixture.Status.text, Is.EqualTo("正在初始化资源..."));
                Assert.That(fixture.RetryButton.gameObject.activeSelf, Is.False);

                retryGate.TrySetResult("resource-download-failed");
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void CopyDetailsUsesBoundedSanitizedFields()
        {
            var fixture = CreateLauncherFixture();
            string copied = null;
            try
            {
                ResourceBoot.TestRawInitializationOutcome = () => UniTask.FromResult("success");
                ConfigureCodeLoaderGameDllLoadException(new BadImageFormatException("secret C:/Users/Admin/token"));
                Launcher.TestClipboardWriter = value => copied = value;
                LogAssert.Expect(LogType.Exception, new Regex("BadImageFormatException: secret"));

                fixture.Launcher.StartForTestsAsync().GetAwaiter().GetResult();
                fixture.CopyButton.onClick.Invoke();

                Assert.That(copied, Is.Not.Null);
                Assert.That(copied, Does.Contain("Code: GameDllLoadFailed"));
                Assert.That(copied, Does.Contain("Stage: GameAssembly"));
                Assert.That(copied, Does.Contain("Game.dll"));
                Assert.That(copied, Is.Not.StringContaining("secret C:/Users/Admin/token"));
                Assert.That(copied, Is.Not.StringContaining(" at "));
                Assert.That(copied.Length, Is.LessThanOrEqualTo(2048));
                Assert.That(fixture.Status.text, Is.EqualTo("已复制错误信息"));
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void QuitButtonInvokesBootQuitPort()
        {
            var fixture = CreateLauncherFixture();
            int quitCalls = 0;
            try
            {
                ResourceBoot.TestRawInitializationOutcome = () => UniTask.FromResult("success");
                ConfigureCodeLoaderAotMissing("mscorlib.dll");
                Launcher.TestQuitPort = () => quitCalls++;

                fixture.Launcher.StartForTestsAsync().GetAwaiter().GetResult();
                fixture.QuitButton.onClick.Invoke();

                Assert.That(quitCalls, Is.EqualTo(1));
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void UnexpectedExceptionKeepsTechnicalDetailsOutOfPlayerOutputs()
        {
            var fixture = CreateLauncherFixture();
            string copied = null;
            try
            {
                ResourceBoot.TestRawInitializationOutcome = () =>
                    throw new InvalidOperationException("secret path C:/Users/Admin/token");
                Launcher.TestClipboardWriter = value => copied = value;
                LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: secret path"));

                fixture.Launcher.StartForTestsAsync().GetAwaiter().GetResult();
                fixture.CopyButton.onClick.Invoke();

                Assert.That(copied, Is.Not.Null);
                Assert.That(fixture.Title.text, Is.Not.StringContaining("secret"));
                Assert.That(fixture.Guidance.text, Is.Not.StringContaining("C:/Users"));
                Assert.That(copied, Does.Contain("InvalidOperationException"));
                Assert.That(copied, Is.Not.StringContaining("secret path"));
                Assert.That(copied, Is.Not.StringContaining("C:/Users/Admin"));
                Assert.That(copied, Is.Not.StringContaining(" at "));
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void RetryButtonInvokesLauncherIntentThroughBootSceneEventSystem()
        {
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.OpenScene(BootScenePath, OpenSceneMode.Additive);
            int resourceCalls = 0;
            try
            {
                ResourceBoot.TestRawInitializationOutcome = () =>
                {
                    resourceCalls++;
                    return UniTask.FromResult("resource-download-failed");
                };

                var launcher = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<Launcher>(true))
                    .Single();
                var view = (HotUpdateView)new SerializedObject(launcher).FindProperty("_updateView").objectReferenceValue;
                var retry = (Button)new SerializedObject(view).FindProperty("_retryButton").objectReferenceValue;
                Assert.NotNull(retry, "按实际绑定定位重试按钮，不依赖参考场景的名称。");
                var eventSystem = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<EventSystem>(true))
                    .Single();

                launcher.InitializeForTests();
                launcher.StartForTestsAsync().GetAwaiter().GetResult();

                var eventData = new PointerEventData(eventSystem);
                ExecuteEvents.Execute(retry.gameObject, eventData, ExecuteEvents.pointerClickHandler);

                Assert.That(resourceCalls, Is.EqualTo(2));
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
        public void VersionPresentationUsesActiveAndDetectedVersions()
        {
            var fixture = CreateLauncherFixture();
            try
            {
                fixture.View.SetVersion(string.Empty, string.Empty);
                Assert.That(fixture.Version.text, Is.EqualTo("资源版本：检测中"));
                fixture.View.SetVersion("1.0.0", string.Empty);
                Assert.That(fixture.Version.text, Is.EqualTo("资源版本：1.0.0"));
                fixture.View.SetVersion("1.0.0", "1.0.0");
                Assert.That(fixture.Version.text, Is.EqualTo("资源版本：1.0.0"));
                fixture.View.SetVersion("1.0.0", "1.1.0");
                Assert.That(fixture.Version.text, Is.EqualTo("当前 1.0.0（目标 1.1.0 未应用）"));
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void BootAssemblyDoesNotReferenceGame()
        {
            var bootAssembly = typeof(Launcher).Assembly;
            Assert.That(bootAssembly.GetReferencedAssemblies().Select(name => name.Name), Does.Not.Contain("Game"));
        }

        [Test]
        public void BootFailurePresentationComesFromStableCode()
        {
            var failure = BootFailure.Create(BootFailureCode.GameDllMissing, BootFailureStage.GameAssembly);
            Assert.That(failure.PlayerTitle, Is.EqualTo("Game.dll 缺失"));
            Assert.That(failure.PlayerGuidance, Does.Contain("完整安装包"));
            Assert.That(failure.Actions, Is.EqualTo(BootFailureAction.CopyDetails | BootFailureAction.Quit));

            var retry = BootFailure.Create(
                BootFailureCode.ResourceDownloadFailed,
                BootFailureStage.ResourceDownload,
                BootRetryScope.ResourceInitialization);
            Assert.That((retry.Actions & BootFailureAction.Retry) != 0, Is.True);
            Assert.That(retry.RetryScope, Is.EqualTo(BootRetryScope.ResourceInitialization));
        }

        private static void ConfigureCodeLoaderAotMissing(string dllName)
        {
            byte[] aotList = Encoding.UTF8.GetBytes(dllName);
            CodeLoader.TestLoadBytesAsync = address =>
            {
                if (address == "HybridCLRAssemblyList.bytes")
                    return UniTask.FromResult(aotList);

                return UniTask.FromResult<byte[]>(null);
            };
        }

        private static void ConfigureCodeLoaderMetadataRejected(string dllName, int result)
        {
            byte[] aotList = Encoding.UTF8.GetBytes(dllName);
            CodeLoader.TestLoadBytesAsync = address =>
            {
                if (address == "HybridCLRAssemblyList.bytes")
                    return UniTask.FromResult(aotList);
                if (address == $"{dllName}.bytes")
                    return UniTask.FromResult(new byte[] { 1, 2, 3 });

                return UniTask.FromResult<byte[]>(null);
            };
            CodeLoader.TestLoadAotMetadataResult = (_, _) => result;
        }

        private static void ConfigureCodeLoaderGameDllLoadException(Exception exception)
        {
            byte[] aotList = Encoding.UTF8.GetBytes("mscorlib.dll");
            CodeLoader.TestLoadBytesAsync = address =>
            {
                if (address == "HybridCLRAssemblyList.bytes")
                    return UniTask.FromResult(aotList);
                if (address == "mscorlib.dll.bytes")
                    return UniTask.FromResult(new byte[] { 1, 2, 3 });
                if (address == "Game.dll.bytes")
                    return UniTask.FromResult(new byte[] { 4, 5, 6 });

                return UniTask.FromResult<byte[]>(null);
            };
            CodeLoader.TestLoadAotMetadataResult = (_, _) => 0;
            CodeLoader.TestLoadGameAssembly = _ => throw exception;
        }

        private static BootOperationResult InvokeCodeLoaderLoadAsync()
        {
            var method = typeof(CodeLoader).GetMethod(nameof(CodeLoader.LoadAsync), BindingFlags.Public | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            var task = (UniTask<BootOperationResult>)method.Invoke(null, new object[] { null });
            return task.GetAwaiter().GetResult();
        }

        private static LauncherFixture CreateLauncherFixture()
        {
            var root = new GameObject("BootFailureFlowLauncher");
            var canvasObject = new GameObject("HotUpdateCanvas", typeof(RectTransform), typeof(Canvas));
            var viewObject = new GameObject("Panel");
            viewObject.transform.SetParent(canvasObject.transform, false);
            root.SetActive(false);
            canvasObject.SetActive(false);
            var launcher = root.AddComponent<Launcher>();
            var resourceBoot = root.AddComponent<ResourceBoot>();
            var view = viewObject.AddComponent<HotUpdateView>();

            var status = AddText(viewObject.transform, "_statusText");
            var version = AddText(viewObject.transform, "_versionText");
            var title = AddText(viewObject.transform, "_titleText");
            var guidance = AddText(viewObject.transform, "_guidanceText");
            var details = AddText(viewObject.transform, "_detailsText");
            var progressText = AddText(viewObject.transform, "_progressText");
            var size = AddText(viewObject.transform, "_sizeText");
            var files = AddText(viewObject.transform, "_fileCountText");
            var speed = AddText(viewObject.transform, "_speedText");
            var progressArea = new GameObject("_progressArea");
            progressArea.transform.SetParent(viewObject.transform, false);
            var downloadStats = new GameObject("_downloadStatsArea");
            downloadStats.transform.SetParent(viewObject.transform, false);
            var buttonArea = new GameObject("_buttonArea");
            buttonArea.transform.SetParent(viewObject.transform, false);
            var slider = new GameObject("_progressSlider").AddComponent<Slider>();
            slider.transform.SetParent(progressArea.transform, false);
            var retry = CreateButton(buttonArea.transform, "_retryButton");
            var copy = CreateButton(buttonArea.transform, "_copyDetailsButton");
            var quit = CreateButton(buttonArea.transform, "_quitButton");

            var viewSerialized = new SerializedObject(view);
            viewSerialized.FindProperty("_statusText").objectReferenceValue = status;
            viewSerialized.FindProperty("_versionText").objectReferenceValue = version;
            viewSerialized.FindProperty("_titleText").objectReferenceValue = title;
            viewSerialized.FindProperty("_guidanceText").objectReferenceValue = guidance;
            viewSerialized.FindProperty("_detailsText").objectReferenceValue = details;
            viewSerialized.FindProperty("_progressArea").objectReferenceValue = progressArea;
            viewSerialized.FindProperty("_downloadStatsArea").objectReferenceValue = downloadStats;
            viewSerialized.FindProperty("_buttonArea").objectReferenceValue = buttonArea;
            viewSerialized.FindProperty("_progressSlider").objectReferenceValue = slider;
            viewSerialized.FindProperty("_progressText").objectReferenceValue = progressText;
            viewSerialized.FindProperty("_sizeText").objectReferenceValue = size;
            viewSerialized.FindProperty("_fileCountText").objectReferenceValue = files;
            viewSerialized.FindProperty("_speedText").objectReferenceValue = speed;
            viewSerialized.FindProperty("_retryButton").objectReferenceValue = retry;
            viewSerialized.FindProperty("_copyDetailsButton").objectReferenceValue = copy;
            viewSerialized.FindProperty("_quitButton").objectReferenceValue = quit;
            viewSerialized.ApplyModifiedPropertiesWithoutUndo();

            var launcherSerialized = new SerializedObject(launcher);
            launcherSerialized.FindProperty("_updateView").objectReferenceValue = view;
            launcherSerialized.FindProperty("_updateCanvas").objectReferenceValue = canvasObject.GetComponent<Canvas>();
            launcherSerialized.ApplyModifiedPropertiesWithoutUndo();

            SetNullPackageResolver();
            launcher.InitializeForTests();
            canvasObject.SetActive(true);
            root.SetActive(true);
            return new LauncherFixture(
                root,
                canvasObject,
                launcher,
                resourceBoot,
                view,
                status,
                version,
                title,
                guidance,
                retry,
                copy,
                quit);
        }

        private static void SetNullPackageResolver()
        {
            var property = typeof(Launcher).GetProperty("TestPackageResolver", BindingFlags.Public | BindingFlags.Static);
            Assert.That(property, Is.Not.Null);
            var invoke = property.PropertyType.GetMethod("Invoke");
            Assert.That(invoke, Is.Not.Null);
            var resolver = Expression.Lambda(property.PropertyType, Expression.Constant(null, invoke.ReturnType)).Compile();
            property.SetValue(null, resolver);
        }

        private static Text AddText(Transform parent, string name)
        {
            var text = new GameObject(name).AddComponent<Text>();
            text.transform.SetParent(parent, false);
            return text;
        }

        private static Button CreateButton(Transform parent, string name)
        {
            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            var image = buttonObject.GetComponent<Image>();
            image.raycastTarget = true;
            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            return button;
        }

        private static void DestroyFixture(LauncherFixture fixture)
        {
            UnityEngine.Object.DestroyImmediate(fixture.Root);
            UnityEngine.Object.DestroyImmediate(fixture.CanvasRoot);
        }

        private readonly struct LauncherFixture
        {
            public LauncherFixture(
                GameObject root,
                GameObject canvasRoot,
                Launcher launcher,
                ResourceBoot resourceBoot,
                HotUpdateView view,
                Text status,
                Text version,
                Text title,
                Text guidance,
                Button retryButton,
                Button copyButton,
                Button quitButton)
            {
                Root = root;
                CanvasRoot = canvasRoot;
                Launcher = launcher;
                ResourceBoot = resourceBoot;
                View = view;
                Status = status;
                Version = version;
                Title = title;
                Guidance = guidance;
                RetryButton = retryButton;
                CopyButton = copyButton;
                QuitButton = quitButton;
            }

            public GameObject Root { get; }
            public GameObject CanvasRoot { get; }
            public Launcher Launcher { get; }
            public ResourceBoot ResourceBoot { get; }
            public HotUpdateView View { get; }
            public Text Status { get; }
            public Text Version { get; }
            public Text Title { get; }
            public Text Guidance { get; }
            public Button RetryButton { get; }
            public Button CopyButton { get; }
            public Button QuitButton { get; }
        }
    }
}
