using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Boot;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using QFramework;
using UnityEngine.TestTools;

namespace Game.Tests
{
    public sealed class ResourceUpdateTests
    {
        private FakeChecker checker;
        private FakeRemoteVersionProbe probe;
        private FakeNoticeClient noticeClient;

        [SetUp]
        public void SetUp()
        {
            ResetArchitecture();
            checker = new FakeChecker();
            probe = new FakeRemoteVersionProbe();
            noticeClient = new FakeNoticeClient();
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<IResourceUpdateRuntimeChecker>(checker);
                architecture.RegisterUtility<IRemoteResourceVersionProbe>(probe);
                architecture.RegisterUtility<IResourceUpdateNoticeClient>(noticeClient);
            };
        }

        [TearDown]
        public void TearDown()
        {
            ResetArchitecture();
            Architecture<Game>.OnRegisterPatch = _ => { };
        }

        [UnityTest]
        public IEnumerator CheckPublishesNoUpdateAndUpdateAvailableSnapshots()
        {
            var events = new List<ResourceUpdateStatus>();
            Game.Interface.RegisterEvent<ResourceUpdateChangedEvent>(e => events.Add(e.Snapshot.Status));
            checker.Next = ResourceUpdateCheckResult.Ok(new ResourceUpdateSnapshot(ResourceUpdateStatus.NoUpdate, "1", "1", -1, -9, null));

            var firstTask = Game.Interface.GetSystem<ResourceUpdateSystem>().CheckAsync(false).AsTask();
            yield return Await(firstTask);
            var first = firstTask.Result;

            Assert.That(first.Status, Is.EqualTo(ResourceUpdateStatus.NoUpdate));
            Assert.That(first.RequiresRestartToUpdate, Is.False);
            Assert.That(first.TotalDownloadCount, Is.EqualTo(0));
            Assert.That(first.TotalDownloadBytes, Is.EqualTo(0));
            Assert.That(events, Is.EqualTo(new[] { ResourceUpdateStatus.Checking, ResourceUpdateStatus.NoUpdate }));

            checker.Next = ResourceUpdateCheckResult.Ok(new ResourceUpdateSnapshot(ResourceUpdateStatus.UpdateAvailable, "1", "2", 3, 4096, null));
            var secondTask = Game.Interface.GetSystem<ResourceUpdateSystem>().CheckAsync(true).AsTask();
            yield return Await(secondTask);
            var second = secondTask.Result;

            Assert.That(second.Status, Is.EqualTo(ResourceUpdateStatus.UpdateAvailable));
            Assert.That(second.CurrentVersion, Is.EqualTo("1"));
            Assert.That(second.TargetVersion, Is.EqualTo("2"));
            Assert.That(second.TotalDownloadCount, Is.EqualTo(3));
            Assert.That(second.TotalDownloadBytes, Is.EqualTo(4096));
        }

        [UnityTest]
        public IEnumerator CachedNewVersionStillRequiresRestartWithoutChangingActiveVersion()
        {
            var system = Game.Interface.GetSystem<ResourceUpdateSystem>();
            checker.Next = ResourceUpdateCheckResult.Ok(new ResourceUpdateSnapshot(
                ResourceUpdateStatus.NoUpdate, "20261006222139", "20261006231309", 0, 0, null));

            var task = system.CheckAsync(true).AsTask();
            yield return Await(task);

            Assert.That(task.Result.Status, Is.EqualTo(ResourceUpdateStatus.UpdateAvailable));
            Assert.That(task.Result.RequiresRestartToUpdate, Is.True);
            Assert.That(task.Result.CurrentVersion, Is.EqualTo("20261006222139"));
            Assert.That(task.Result.TargetVersion, Is.EqualTo("20261006231309"));
            Assert.That(task.Result.TotalDownloadCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ConcurrentChecksShareSingleRuntimeRequest()
        {
            checker.Gate = new TaskCompletionSource<ResourceUpdateCheckResult>();
            var system = Game.Interface.GetSystem<ResourceUpdateSystem>();

            var first = system.CheckAsync(false).AsTask();
            var second = system.CheckAsync(true).AsTask();
            checker.Gate.SetResult(ResourceUpdateCheckResult.Ok(new ResourceUpdateSnapshot(ResourceUpdateStatus.NoUpdate, "1", "1", 0, 0, null)));
            yield return Await(Task.WhenAll(first, second));

            Assert.That(checker.CallCount, Is.EqualTo(1));
            Assert.That(first.Result.Status, Is.EqualTo(ResourceUpdateStatus.NoUpdate));
            Assert.That(second.Result.Status, Is.EqualTo(ResourceUpdateStatus.NoUpdate));
        }

        [UnityTest]
        public IEnumerator FailureBecomesDisplayableSnapshotAndDoesNotThrow()
        {
            checker.Next = ResourceUpdateCheckResult.Fail("1", "network down");

            var task = Game.Interface.GetSystem<ResourceUpdateSystem>().CheckAsync(true).AsTask();
            yield return Await(task);
            var snapshot = task.Result;

            Assert.That(snapshot.Status, Is.EqualTo(ResourceUpdateStatus.CheckFailed));
            Assert.That(snapshot.CurrentVersion, Is.EqualTo("1"));
            Assert.That(snapshot.Error, Is.EqualTo("资源更新检查失败：network down"));
        }

        [UnityTest]
        public IEnumerator UpdateSnapshotDrivesResourceRedPointOnlyWhenAvailable()
        {
            var system = Game.Interface.GetSystem<ResourceUpdateSystem>();
            checker.Next = ResourceUpdateCheckResult.Ok(new ResourceUpdateSnapshot(ResourceUpdateStatus.UpdateAvailable, "1", "2", 1, 10, null));
            yield return Await(system.CheckAsync(false).AsTask());
            LifeCycle.LateUpdate();

            Assert.IsTrue(Game.Interface.GetSystem<RedPointSystem>().GetState(RedPointIds.StartResourceUpdate).Active);

            checker.Next = ResourceUpdateCheckResult.Ok(new ResourceUpdateSnapshot(ResourceUpdateStatus.NoUpdate, "2", "2", 0, 0, null));
            yield return Await(system.CheckAsync(false).AsTask());
            LifeCycle.LateUpdate();

            Assert.IsFalse(Game.Interface.GetSystem<RedPointSystem>().GetState(RedPointIds.StartResourceUpdate).Active);
        }

        [UnityTest]
        public IEnumerator BuiltinFallbackSilentCheckDoesNotProbeAndKeepsBuiltinState()
        {
            var system = Game.Interface.GetSystem<ResourceUpdateSystem>();
            system.InitializeStartupInfo(new BootResourceStartupInfo(
                BootResourceSource.BuiltinFallback,
                "http://127.0.0.1:8000",
                "builtin-1",
                string.Empty));

            var task = system.CheckAsync(false).AsTask();
            yield return Await(task);
            var snapshot = task.Result;

            Assert.That(probe.CallCount, Is.EqualTo(0));
            Assert.That(checker.CallCount, Is.EqualTo(0));
            Assert.That(snapshot.Status, Is.EqualTo(ResourceUpdateStatus.NotChecked));
            Assert.That(snapshot.ResourceSource, Is.EqualTo(BootResourceSource.BuiltinFallback));
            Assert.That(snapshot.CurrentVersion, Is.EqualTo("builtin-1"));
        }

        [UnityTest]
        public IEnumerator BuiltinFallbackManualProbeFailureReportsCheckFailed()
        {
            var system = Game.Interface.GetSystem<ResourceUpdateSystem>();
            system.InitializeStartupInfo(new BootResourceStartupInfo(
                BootResourceSource.BuiltinFallback,
                "http://127.0.0.1:8000",
                "builtin-1",
                string.Empty));
            probe.Next = RemoteResourceVersionProbeResult.Fail("offline");

            var task = system.CheckAsync(true).AsTask();
            yield return Await(task);
            var snapshot = task.Result;

            Assert.That(probe.CallCount, Is.EqualTo(1));
            Assert.That(checker.CallCount, Is.EqualTo(0));
            Assert.That(snapshot.Status, Is.EqualTo(ResourceUpdateStatus.CheckFailed));
            Assert.That(snapshot.ResourceSource, Is.EqualTo(BootResourceSource.BuiltinFallback));
            Assert.That(snapshot.HasKnownDownloadSize, Is.False);
        }

        [UnityTest]
        public IEnumerator BuiltinFallbackSameVersionReportsNoUpdateWithoutDownloadSize()
        {
            var system = Game.Interface.GetSystem<ResourceUpdateSystem>();
            system.InitializeStartupInfo(new BootResourceStartupInfo(
                BootResourceSource.BuiltinFallback,
                "http://127.0.0.1:8000",
                "builtin-1",
                string.Empty));
            probe.Next = RemoteResourceVersionProbeResult.Ok("builtin-1");

            var task = system.CheckAsync(true).AsTask();
            yield return Await(task);
            var snapshot = task.Result;

            Assert.That(snapshot.Status, Is.EqualTo(ResourceUpdateStatus.NoUpdate));
            Assert.That(snapshot.TargetVersion, Is.EqualTo("builtin-1"));
            Assert.That(snapshot.HasKnownDownloadSize, Is.False);
            Assert.That(Game.Interface.GetSystem<RedPointSystem>().GetState(RedPointIds.StartResourceUpdate).Active, Is.False);
        }

        [UnityTest]
        public IEnumerator BuiltinFallbackDifferentVersionRequiresRestartAndRedPoint()
        {
            var system = Game.Interface.GetSystem<ResourceUpdateSystem>();
            system.InitializeStartupInfo(new BootResourceStartupInfo(
                BootResourceSource.BuiltinFallback,
                "http://127.0.0.1:8000",
                "builtin-1",
                string.Empty));
            probe.Next = RemoteResourceVersionProbeResult.Ok("host-2");

            var task = system.CheckAsync(true).AsTask();
            yield return Await(task);
            var snapshot = task.Result;

            Assert.That(snapshot.Status, Is.EqualTo(ResourceUpdateStatus.UpdateAvailable));
            Assert.That(snapshot.TargetVersion, Is.EqualTo("host-2"));
            Assert.That(snapshot.RequiresRestartToUpdate, Is.True);
            Assert.That(snapshot.HasKnownDownloadSize, Is.False);
            LifeCycle.LateUpdate();
            Assert.That(Game.Interface.GetSystem<RedPointSystem>().GetState(RedPointIds.StartResourceUpdate).Active, Is.True);
        }

        [UnityTest]
        public IEnumerator HostLatestStillUsesRuntimeCheckerAndNotRemoteProbe()
        {
            var system = Game.Interface.GetSystem<ResourceUpdateSystem>();
            system.InitializeStartupInfo(new BootResourceStartupInfo(
                BootResourceSource.HostLatest,
                "http://127.0.0.1:8000",
                "1",
                "2"));
            checker.Next = ResourceUpdateCheckResult.Ok(new ResourceUpdateSnapshot(ResourceUpdateStatus.NoUpdate, "1", "1", 0, 0, null));

            var task = system.CheckAsync(false).AsTask();
            yield return Await(task);

            Assert.That(checker.CallCount, Is.EqualTo(1));
            Assert.That(probe.CallCount, Is.EqualTo(0));
            Assert.That(task.Result.ResourceSource, Is.EqualTo(BootResourceSource.HostLatest));
            Assert.That(task.Result.HasKnownDownloadSize, Is.True);
        }

        [UnityTest]
        public IEnumerator SilentCheckNeverCallsNoticeClient()
        {
            var system = Game.Interface.GetSystem<ResourceUpdateSystem>();
            system.InitializeStartupInfo(new BootResourceStartupInfo(
                BootResourceSource.HostLatest,
                "http://127.0.0.1:8000",
                "1",
                "2"));
            checker.Next = ResourceUpdateCheckResult.Ok(new ResourceUpdateSnapshot(ResourceUpdateStatus.NoUpdate, "1", "2", 0, 0, null));

            var task = system.CheckWithNoticeAsync(false).AsTask();
            yield return Await(task);

            Assert.That(noticeClient.CallCount, Is.EqualTo(0));
            Assert.That(task.Result.Notice.HasNotice, Is.False);
        }

        [UnityTest]
        public IEnumerator ManualFailureDoesNotCallNoticeClient()
        {
            var system = Game.Interface.GetSystem<ResourceUpdateSystem>();
            system.InitializeStartupInfo(new BootResourceStartupInfo(
                BootResourceSource.HostLatest,
                "http://127.0.0.1:8000",
                "1",
                "2"));
            checker.Next = ResourceUpdateCheckResult.Fail("1", "offline");

            var task = system.CheckWithNoticeAsync(true).AsTask();
            yield return Await(task);

            Assert.That(task.Result.Snapshot.Status, Is.EqualTo(ResourceUpdateStatus.CheckFailed));
            Assert.That(noticeClient.CallCount, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator ManualSuccessRequestsNoticeWithTargetVersionServerLanguageAndTimeout()
        {
            var system = Game.Interface.GetSystem<ResourceUpdateSystem>();
            system.InitializeStartupInfo(new BootResourceStartupInfo(
                BootResourceSource.HostLatest,
                "http://127.0.0.1:8000",
                "1",
                "2"));
            Game.Interface.GetModel<LocalizationModel>().CurrentLanguageCode.Value = "en";
            checker.Next = ResourceUpdateCheckResult.Ok(new ResourceUpdateSnapshot(ResourceUpdateStatus.UpdateAvailable, "1", "2", 1, 10, null));
            noticeClient.Next = ResourceUpdateNoticeResult.Ok(new ResourceUpdateNoticeContent("2", "Title", "Summary", new[] { "Item" }));

            var task = system.CheckWithNoticeAsync(true).AsTask();
            yield return Await(task);

            Assert.That(noticeClient.CallCount, Is.EqualTo(1));
            Assert.That(noticeClient.LastRequest.ResourceServer, Is.EqualTo("http://127.0.0.1:8000"));
            Assert.That(noticeClient.LastRequest.Version, Is.EqualTo("2"));
            Assert.That(noticeClient.LastRequest.LanguageCode, Is.EqualTo("en"));
            Assert.That(noticeClient.LastRequest.TimeoutSeconds, Is.EqualTo(5));
            Assert.That(task.Result.Notice.HasNotice, Is.True);
        }

        [UnityTest]
        public IEnumerator NoticeFailureDoesNotChangeResourceSnapshot()
        {
            var system = Game.Interface.GetSystem<ResourceUpdateSystem>();
            system.InitializeStartupInfo(new BootResourceStartupInfo(
                BootResourceSource.HostLatest,
                "http://127.0.0.1:8000",
                "1",
                "2"));
            checker.Next = ResourceUpdateCheckResult.Ok(new ResourceUpdateSnapshot(ResourceUpdateStatus.NoUpdate, "1", "1", 0, 0, null));
            noticeClient.Next = ResourceUpdateNoticeResult.Fail("notice down");

            var task = system.CheckWithNoticeAsync(true).AsTask();
            yield return Await(task);

            Assert.That(task.Result.Snapshot.Status, Is.EqualTo(ResourceUpdateStatus.NoUpdate));
            Assert.That(task.Result.Notice.HasNotice, Is.False);
            Assert.That(Game.Interface.GetSystem<ResourceUpdateSystem>().Snapshot.Status, Is.EqualTo(ResourceUpdateStatus.NoUpdate));
        }

        [Test]
        public void RestartUtilityOnlyQuitsAfterSuccessfulLaunch()
        {
            var launcher = new FakeProcessLauncher();
            var quitter = new FakeQuitter();
            var restart = new ApplicationRestartUtility(launcher, quitter, () => false, () => @"C:\Game\OutpostLoop.exe");

            launcher.Result = false;
            Assert.IsFalse(restart.TryRestart(out var error));
            Assert.That(error, Is.EqualTo("launch failed"));
            Assert.IsFalse(quitter.QuitCalled);

            launcher.Result = true;
            Assert.IsTrue(restart.TryRestart(out error), error);
            Assert.IsTrue(quitter.QuitCalled);
            Assert.That(launcher.WorkingDirectory, Is.EqualTo(@"C:\Game"));
        }

        [Test]
        public void RestartUtilityRejectsEditorAndEmptyPath()
        {
            var launcher = new FakeProcessLauncher();
            var quitter = new FakeQuitter();

            var editorRestart = new ApplicationRestartUtility(launcher, quitter, () => true, () => @"C:\Game\OutpostLoop.exe");
            Assert.IsFalse(editorRestart.TryRestart(out var editorError));
            Assert.That(editorError, Does.Contain("编辑器内不支持重启应用"));
            Assert.IsFalse(quitter.QuitCalled);

            var missingPathRestart = new ApplicationRestartUtility(launcher, quitter, () => false, () => string.Empty);
            Assert.IsFalse(missingPathRestart.TryRestart(out var pathError));
            Assert.That(pathError, Does.Contain("可执行文件路径为空"));
            Assert.IsFalse(quitter.QuitCalled);
        }

        [UnityTest]
        public IEnumerator PendingCheckFinishesSafelyAfterArchitectureDeinit()
        {
            checker.Gate = new TaskCompletionSource<ResourceUpdateCheckResult>();
            var system = Game.Interface.GetSystem<ResourceUpdateSystem>();
            var task = system.CheckAsync(true).AsTask();
            Game.Interface.Deinit();
            checker.Gate.SetResult(ResourceUpdateCheckResult.Ok(new ResourceUpdateSnapshot(ResourceUpdateStatus.NoUpdate, "1", "1", 0, 0, null)));
            yield return Await(task);
            Assert.IsFalse(task.IsFaulted);
        }

        private static void ResetArchitecture()
        {
            if (Game.Interface != null) Game.Interface.Deinit();
            Architecture<Game>.OnRegisterPatch = _ => { };
            LifeCycle.ClearForTests();
        }

        private static IEnumerator Await(Task task)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted)
            {
                throw task.Exception;
            }

            if (task.IsCanceled)
            {
                Assert.Fail("Task was cancelled.");
            }
        }

        private sealed class FakeChecker : IResourceUpdateRuntimeChecker
        {
            public ResourceUpdateCheckResult Next { get; set; } =
                ResourceUpdateCheckResult.Ok(new ResourceUpdateSnapshot(ResourceUpdateStatus.NoUpdate, "1", "1", 0, 0, null));

            public TaskCompletionSource<ResourceUpdateCheckResult> Gate { get; set; }
            public int CallCount { get; private set; }
            public bool CanCheck => true;

            public UniTask<ResourceUpdateCheckResult> CheckAsync(int timeoutSeconds)
            {
                CallCount++;
                return (Gate?.Task ?? Task.FromResult(Next)).AsUniTask();
            }
        }

        private sealed class FakeRemoteVersionProbe : IRemoteResourceVersionProbe
        {
            public RemoteResourceVersionProbeResult Next { get; set; } =
                RemoteResourceVersionProbeResult.Ok("builtin-1");

            public int CallCount { get; private set; }

            public UniTask<RemoteResourceVersionProbeResult> ProbeAsync(
                string resourceServer,
                int timeoutSeconds)
            {
                CallCount++;
                return UniTask.FromResult(Next);
            }
        }

        private sealed class FakeNoticeClient : IResourceUpdateNoticeClient
        {
            public ResourceUpdateNoticeResult Next { get; set; } = ResourceUpdateNoticeResult.NoNotice;
            public int CallCount { get; private set; }
            public ResourceUpdateNoticeRequest LastRequest { get; private set; }

            public UniTask<ResourceUpdateNoticeResult> FetchAsync(ResourceUpdateNoticeRequest request)
            {
                CallCount++;
                LastRequest = request;
                return UniTask.FromResult(Next);
            }
        }

        private sealed class FakeProcessLauncher : IProcessLauncher
        {
            public bool Result { get; set; }
            public string WorkingDirectory { get; private set; }

            public bool TryLaunch(string fileName, string workingDirectory, out string error)
            {
                WorkingDirectory = workingDirectory;
                error = Result ? string.Empty : "launch failed";
                return Result;
            }
        }

        private sealed class FakeQuitter : IApplicationQuitter
        {
            public bool QuitCalled { get; private set; }

            public void Quit()
            {
                QuitCalled = true;
            }
        }
    }
}
