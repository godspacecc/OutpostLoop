using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>只用临时文件验证快速 APK 的版本预留与复用门禁；不调用 Player 或资源构建。</summary>
    public sealed class AndroidQuickBuildTests
    {
        private string root;
        [SetUp] public void SetUp() { root = Path.Combine(Path.GetTempPath(), "OutpostQuick", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); }
        [TearDown] public void TearDown() { Directory.Delete(root, true); }

        private static object Call(string type, string method, params object[] args)
        {
            var target = Type.GetType("Game.Editor." + type + ", Assembly-CSharp-Editor");
            Assert.That(target, Is.Not.Null, "缺少快速打包策略：" + type);
            try { return target.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args); }
            catch (TargetInvocationException e) { throw e.InnerException; }
        }

        [Test] public void InputChangesIdentifyAddedRemovedAndModifiedFiles()
        {
            var settings = Path.Combine(root, "ProjectSettings");
            Directory.CreateDirectory(settings);
            var config = Path.Combine(settings, "ProjectSettings.asset");
            File.WriteAllText(config, "  bundleVersion: 1\nother: 1\n");
            var changed = Path.Combine(root, "Font.asset");
            var removed = Path.Combine(root, "Before.cs");
            var added = Path.Combine(root, "After.cs");
            File.WriteAllText(changed, "abc");
            File.WriteAllText(removed, "class A {}");
            object Snapshot() => Call("AndroidQuickBuildUtility", "SnapshotInputs", root,
                Directory.GetFiles(root, "*", SearchOption.TopDirectoryOnly));
            var before = Snapshot();
            File.WriteAllText(config, "  bundleVersion: 2\nother: 1\n");
            File.SetLastWriteTimeUtc(changed, DateTime.UtcNow.AddHours(-1));
            Assert.DoesNotThrow(() => Call("AndroidQuickBuildUtility", "RequireSameInputs", before, Snapshot()));
            File.WriteAllText(changed, "xyz");
            File.Move(removed, added);
            File.AppendAllText(config, "changed: true");
            var error = Assert.Throws<InvalidOperationException>(() =>
                Call("AndroidQuickBuildUtility", "RequireSameInputs", before, Snapshot()));
            foreach (var detail in new[] { "新增：After.cs", "删除：Before.cs", "修改：Font.asset", "修改：ProjectSettings/ProjectSettings.asset" })
                Assert.That(error.Message, Does.Contain(detail));
        }

        [Test] public void MaterialFingerprintDetectsSameLengthContentChange()
        {
            string path = Path.Combine(root, "AOT.dll"); File.WriteAllText(path, "abc");
            var before = Call("AndroidQuickBuildUtility", "FingerprintFiles", new object[] { new[] { path } });
            File.WriteAllText(path, "xyz");
            Assert.That(Call("AndroidQuickBuildUtility", "FingerprintFiles", new object[] { new[] { path } }), Is.Not.EqualTo(before));
        }
        [Test] public void MissingReuseInputIsRejected() => Assert.Throws<FileNotFoundException>(() =>
            Call("AndroidQuickBuildUtility", "FingerprintFiles", new object[] { new[] { Path.Combine(root, "missing.dll") } }));
        [Test] public void RecoveryRejectsInputsEditedAfterPlayerStarted()
        {
            string path = Path.Combine(root, "Boot.cs"); File.WriteAllText(path, "source");
            Assert.Throws<InvalidOperationException>(() => Call("AndroidQuickBuildUtility", "ValidateRecoveryInputTimes", new[] { path }, DateTime.UtcNow.AddHours(-1)));
        }
        [Test] public void RecoveryAcceptsUnchangedOldInputs()
        {
            string path = Path.Combine(root, "Boot.cs"); File.WriteAllText(path, "source");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-2));
            Assert.DoesNotThrow(() => Call("AndroidQuickBuildUtility", "ValidateRecoveryInputTimes", new[] { path }, DateTime.UtcNow.AddHours(-1)));
        }
        [Test] public void QuickWorkflowIsRegisteredWithoutExecutingIt()
        {
            var action = (Action)Call("AndroidBuildUtility", "ResolveWorkflow", "Android 复用本次 AOT和资源快速打包");
            Assert.That(action, Is.Not.Null); Assert.That(action.Method.Name, Is.EqualTo("BuildQuickReuseApkBatch"));
        }

        [Test] public void UnknownPreparedKindIsRejected() => Assert.Throws<InvalidOperationException>(() =>
            Call("AndroidQuickBuildUtility", "ValidateIdentity", 2, "Android", "Channel"));
        [Test] public void CrossPlatformPreparedSnapshotIsRejected() => Assert.Throws<InvalidOperationException>(() =>
            Call("AndroidQuickBuildUtility", "ValidateIdentity", 2, "StandaloneWindows64", "HostBase"));
        [Test] public void LegacyPreparedSnapshotIsRejected() => Assert.Throws<InvalidOperationException>(() =>
            Call("AndroidQuickBuildUtility", "ValidateIdentity", 0, "Android", "SmallHostBase"));

        [TestCase("new", false)]
        [TestCase("old", true)]
        public void ApkResourceValidationRejectsTamperedOrExtraFiles(string content, bool extra)
        {
            string builtin = Path.Combine(root, "builtin"); Directory.CreateDirectory(builtin);
            File.WriteAllText(Path.Combine(builtin, "sample.bundle"), "old");
            string apk = Path.Combine(root, "sample.apk");
            using (var archive = ZipFile.Open(apk, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("assets/yoo/OutpostLoop/sample.bundle").Open())) writer.Write(content);
                if (extra) archive.CreateEntry("assets/yoo/OutpostLoop/stale.bundle");
            }
            Assert.Throws<InvalidOperationException>(() => Call("AndroidQuickBuildUtility", "ValidateApkResources", apk, builtin));
        }
        [Test] public void AotReuseWorkflowIsRegisteredWithoutExecutingIt()
        {
            var action = (Action)Call("AndroidBuildUtility", "ResolveWorkflow", "Android 复用 AOT 打包");
            Assert.That(action.Method.Name, Is.EqualTo("BuildQuickReuseAotBatch"));
        }

        [Test] public void NonMainProjectSettingsChangeInvalidatesPreparedInputs()
        {
            string settings = Path.Combine(root, "ProjectSettings"); Directory.CreateDirectory(settings);
            File.WriteAllText(Path.Combine(settings, "ProjectSettings.asset"), "  bundleVersion: 1.0.0\n  AndroidBundleVersionCode: 1\n");
            string quality = Path.Combine(settings, "QualitySettings.asset"); File.WriteAllText(quality, "old");
            object before = Call("AndroidQuickBuildUtility", "ComputeSettingsFingerprint", root);
            File.WriteAllText(quality, "new");
            Assert.That(Call("AndroidQuickBuildUtility", "ComputeSettingsFingerprint", root), Is.Not.EqualTo(before));
        }
        [Test] public void ApplicationVersionOnlyChangeKeepsPreparedInputs()
        {
            string settings = Path.Combine(root, "ProjectSettings"); Directory.CreateDirectory(settings);
            string path = Path.Combine(settings, "ProjectSettings.asset");
            File.WriteAllText(path, "  bundleVersion: 1.0.0\n  AndroidBundleVersionCode: 1\n");
            object before = Call("AndroidQuickBuildUtility", "ComputeSettingsFingerprint", root);
            File.WriteAllText(path, "  bundleVersion: 1.0.1\n  AndroidBundleVersionCode: 2\n");
            Assert.That(Call("AndroidQuickBuildUtility", "ComputeSettingsFingerprint", root), Is.EqualTo(before));
        }
        [Test] public void CrossVersionFrozenDirectoryIsRejected() => Assert.Throws<InvalidOperationException>(() =>
            Call("AndroidQuickBuildUtility", "ValidatePreparedPaths", "20261030000000", Path.Combine(root, "packages", "20261030000000"),
                Path.Combine(root, "cache", "20261029000000"), Path.Combine(root, "packages"), Path.Combine(root, "cache")));

        [Test] public void HostAssetSelectionIncludesEmbeddedConfigButExcludesRemoteArtAndBundles()
        {
            foreach (string relative in new[] { "Resources/local.txt", "StreamingAssets/host.json", "StreamingAssets/yoo/OutpostLoop/test.bundle", "Game/Art/test.png", "Game/Prefabs/boot.prefab" })
            {
                string path = Path.Combine(root, "Assets", relative); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, "data");
            }
            string boot = Path.GetFullPath(Path.Combine(root, "Assets", "Game/Prefabs/boot.prefab"));
            var files = (string[])Call("AndroidQuickAotUtility", "CollectHostAssetInputs", root, new[] { boot });
            Assert.That(files, Does.Contain(boot));
            Assert.That(files, Does.Contain(Path.GetFullPath(Path.Combine(root, "Assets", "Resources/local.txt"))));
            Assert.That(files, Does.Contain(Path.GetFullPath(Path.Combine(root, "Assets", "StreamingAssets/host.json"))));
            Assert.That(files, Does.Not.Contain(Path.GetFullPath(Path.Combine(root, "Assets", "StreamingAssets/yoo/OutpostLoop/test.bundle"))));
            Assert.That(files, Does.Not.Contain(Path.GetFullPath(Path.Combine(root, "Assets", "Game/Art/test.png"))));
        }

        private string Preserve()
        {
            string dlls = Path.Combine(root, "dlls"); Directory.CreateDirectory(dlls);
            string list = Path.Combine(root, "list.bytes"); File.WriteAllText(list, "Sample.dll");
            return (string)Call("AndroidQuickBuildUtility", "PreserveAotMetadata", "20261017170250", list, dlls, Path.Combine(root, "cache"));
        }
        [Test] public void PreservedAotIsIndependentOfMutableBuildInputs()
        {
            string dlls = Path.Combine(root, "dlls"); Directory.CreateDirectory(dlls);
            string source = Path.Combine(dlls, "Sample.dll.bytes"); File.WriteAllText(source, "old-metadata");
            string cache = Preserve(); File.Delete(source);
            Assert.That(File.ReadAllText(Path.Combine(cache, "Sample.dll")), Is.EqualTo("old-metadata"));
        }
        [Test] public void ChangedMetadataCannotOverwriteAnExistingVersionBaseline()
        {
            string dlls = Path.Combine(root, "dlls"); Directory.CreateDirectory(dlls);
            string source = Path.Combine(dlls, "Sample.dll.bytes"); File.WriteAllText(source, "old");
            string cache = Preserve(); File.WriteAllText(source, "new");
            Assert.Throws<InvalidOperationException>(() => Preserve());
            Assert.That(File.ReadAllText(Path.Combine(cache, "Sample.dll")), Is.EqualTo("old"));
        }
        [Test] public void MissingMetadataDoesNotLeaveAnAcceptedBaseline()
        {
            Assert.Throws<FileNotFoundException>(() => Preserve());
            Assert.That(Directory.Exists(Path.Combine(root, "cache", "20261017170250")), Is.False);
            Assert.That(Directory.GetDirectories(Path.Combine(root, "cache")), Is.Empty);
        }
    }
}
