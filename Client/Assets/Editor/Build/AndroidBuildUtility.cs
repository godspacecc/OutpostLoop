#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using HybridCLR.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Callbacks;
using UnityEditor.Compilation;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>生成 Android 离线包、Host 基础包与增量资源，不负责正式签名或商店发布。</summary>
    public static class AndroidBuildUtility
    {
        private const string BootScene = "Assets/Game/Scenes/Boot.unity";
        private const string ApplicationIdentifier = "com.godspace.outpostloop";
        private const string HostConfigPath = "Assets/StreamingAssets/hostConfig.json";
        internal const string BuiltinResourcesScriptingDefine = "OUTPOSTLOOP_ANDROID_BUILTIN_RESOURCES";
        private const string OfflineWorkflowName = "Android 完整 Development APK";
        private const string HostBaseWorkflowName = "Android Host 基础包";
        private const string SmallHostWorkflowName = "Android 联网首包";
        private const string HostUpdateWorkflowName = "Android Host 代码+资源更新";
        private const string PendingWorkflowKey = "OutpostLoop.AndroidBuild.PendingWorkflow";
        private const string ReloadCompletedKey = "OutpostLoop.AndroidBuild.ReloadCompleted";

        private static bool s_isBuilding;

        internal static bool HasActiveWorkflow => s_isBuilding ||
            !string.IsNullOrEmpty(SessionState.GetString(PendingWorkflowKey, string.Empty));

        [MenuItem("Tools/打包/Android/完整打包", false, 150)]
        private static void BuildLatestMenu()
        {
            if (Application.isBatchMode || EditorUtility.DisplayDialog(
                    "Android 最新代码资源完整打包",
                    "生成 ARM64、IL2CPP、完整内置资源的 Development APK，首次启动无需联网下载资源。需要时会切换到 Android，并在脚本重载后继续。",
                    "开始构建",
                    "取消"))
            {
                BuildLatestCodeAndResourcesBatch();
            }
        }

        [MenuItem("Tools/打包/Android/完整打包（宿主基线）", false, 153)]
        private static void BuildHostBaseMenu()
        {
            if (Confirm(
                    "Android Host基础包完整打包",
                    "生成连接资源服且内置完整回退资源的 Development APK。首次启动会尝试联网更新，资源服不可用时可使用内置资源。hostConfig.json 必须使用手机可访问的地址。"))
            {
                BuildHostBaseCodeAndResourcesBatch();
            }
        }

        [MenuItem("Tools/打包/Android/启动包（首次下载资源）", false, 152)]
        private static void BuildSmallHostMenu()
        {
            if (Confirm(
                    "Android 首包完整打包",
                    "APK 只内置 Boot、DLL 和资源清单，不内置资源 Bundle；首次启动必须连接资源服下载完整资源，无网络且无缓存时不能进入游戏。"))
            {
                BuildSmallHostBaseCodeAndResourcesBatch();
            }
        }

        [MenuItem("Tools/打包/Android/构建热更新（代码+资源）", false, 151)]
        private static void BuildHostUpdateMenu()
        {
            BuildHostCodeAndResourcesBatch();
        }

        [MenuItem("Tools/服务器/Android/生成资源服", false, 210)]
        private static void PublishAndroidResourceServerMenu()
        {
            ShowResourceServerResult(ResourceServerPublishUtility.PublishLatestValidAndroidPackage());
        }

        [MenuItem("Tools/服务器/Android/更新资源服", false, 211)]
        private static void UpdateAndroidResourceServerMenu()
        {
            ShowResourceServerResult(ResourceServerPublishUtility.UpdateLatestAndroidBundle());
        }

        [MenuItem("Tools/服务器/Android/启动资源服", false, 212)]
        private static void StartAndroidResourceServerMenu()
        {
            StartAndroidResourceServerFromLatestPublishedBundle();
        }

        /// <summary>不重新编译资源，复用最近完整准备快照。</summary>
        [MenuItem("Tools/打包/Android/快速打包/复用本次 AOT和资源", false, 168)]
        private static void BuildQuickReuseApkMenu()
        {
            if (Confirm("Android 复用全部打包", "校验可信 AOT 和资源快照，仅重建最终 APK 并同步发布目录。")) BuildQuickReuseApkBatch();
        }

        /// <summary>复用已校验的同轮 AOT 与资源，只构建一次最终首包 APK；发布失败保留产物与复用快照。</summary>
        public static void BuildQuickReuseApkBatch() => BuildQuickApk(false);

        /// <summary>重新编译玩法和资源，但只使用可信记录中的冻结 AOT。</summary>
        public static void BuildQuickReuseAotBatch() => BuildQuickApk(true);

        [MenuItem("Tools/打包/Android/快速打包/复用 AOT 打包", false, 169)]
        private static void BuildQuickReuseAotMenu()
        {
            if (Application.isBatchMode || EditorUtility.DisplayDialog("Android 复用 AOT 打包",
                "重新编译玩法并重建资源，复用可靠准备记录中的 AOT，沿用完整包或启动包类型生成 APK。底层、桥接或原生需求不兼容时停止，并提示重新完整准备。",
                "开始打包", "取消")) BuildQuickReuseAotBatch();
        }

        /// <summary>两种快速包共用最终 APK 与发布事务。</summary>
        private static void BuildQuickApk(bool rebuildResources)
        {
            using var lease = WorkspaceBuildLock.Acquire(Directory.GetParent(Application.dataPath).FullName);
            if (!BeginWorkflow(rebuildResources ? "Android 复用 AOT 打包" : "Android 复用本次 AOT和资源快速打包")) return;
            var timing = new BuildStageTiming(rebuildResources ? "Android 复用 AOT 打包" : "Android 复用全部打包");
            var context = new AndroidBuildContext(DateTime.Now, Guid.NewGuid().ToString("N"));
            var previous = AndroidEditorOptions.Capture();
            bool finalApkValidated = false;
            bool outputPrepared = false;
            try
            {
                timing.Run("ValidateAndroidEnvironment", ValidateAndroidEnvironment);
                timing.Run("ValidateAndroidHostConfig", ValidateAndroidHostConfig);
                previous.ApplyDevelopmentApkOptions();
                var hostInputs = HostBaselineUtility.Capture();
                var inputs = rebuildResources ? AndroidQuickBuildUtility.LoadForAotReuse() : AndroidQuickBuildUtility.LoadAndValidate();
                Debug.Log($"[AndroidBuildUtility] 本次快速打包类型：{(inputs.Kind == "HostBase" ? "完整包" : "启动包（首次下载资源）")}，复用准备版本：{inputs.Version}");
                if (rebuildResources)
                {
                    var compilationFingerprint = AndroidQuickBuildUtility.CaptureCompilationFingerprint();
                    HybridCLRBuildUtility.CompileHotUpdateDll(BuildTarget.Android);
                    string game = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "HybridCLRData", "HotUpdateDlls", "Android", "Game.dll");
                    AndroidQuickAotUtility.ValidateGame(Path.Combine(inputs.AotDirectory, "baseline-game.bytes"), game);
                    HybridCLRBuildUtility.CopyGeneratedDllsFromBaseline(BuildTarget.Android, inputs.AotDirectory, Path.Combine(inputs.AotDirectory, "runtime-list.bytes"));
                    string version = ReserveAndroidResourceVersion(context.Timestamp);
                    string generatedFingerprint = AndroidQuickBuildUtility.CaptureGeneratedFingerprint();
                    var package = inputs.Kind == "HostBase" ? BuildPackageOrThrow(version, true) : BuildSmallHostPackageOrThrow(version);
                    AndroidQuickBuildUtility.LoadForAotReuse();
                    inputs = AndroidQuickBuildUtility.Capture(version, package.OutputPackageDirectory, compilationFingerprint, generatedFingerprint, inputs.Kind);
                }
                timing.Run("PrepareOutputDirectories", context.PrepareDirectories);
                outputPrepared = true;
                string appVersion = timing.Run("BuildQuickFinalApk", () => BuildFinalPlayerWithIncrementedPatchVersion(context, false));
                timing.Run("ValidateFinalQuickApk", context.ValidateFinalApk);
                AndroidQuickBuildUtility.ValidateApkResources(context.FinalApkPath, YooAssetBuildUtility.GetStreamingAssetsDirectory());
                finalApkValidated = true;
                timing.Run("RevalidateQuickReuseInputs", () => AndroidQuickBuildUtility.Validate(inputs));
                timing.Run("SaveAndroidHostState", () => SaveAndroidHostState(inputs.Version, inputs.Kind,
                    inputs.PackageDirectory, context.FinalApkPath, true, inputs.AotDirectory));

                timing.Run("CleanupPreparatoryDirectory", context.CleanupPreparatoryDirectory);
                var artifacts = CreateApkArtifacts(context.FinalApkPath, appVersion, inputs.Version, inputs.PackageDirectory, timing);
                timing.Run("ArchiveQuickHostBaseline", () => HostBaselineUtility.Archive(artifacts, hostInputs));
                var publish = timing.Run("PublishQuickArtifacts", () => artifacts.Publish());
                string bundle = publish.BundleDirectory;
                timing.Run("RevalidatePublishedResources", () => ValidateQuickPublishedResources(bundle, inputs));
                string update = artifacts.UpdateManifestPath;
                Debug.Log($"[AndroidBuildUtility] Android 快速打包完成：Version={inputs.Version}，AppVersion={appVersion}，APK={context.FinalApkPath}，更新描述={update}\n{timing.BuildSummary()}");
                ShowDialog("Android 快速打包成功", $"APK: {context.FinalApkPath}\n应用版本: {appVersion}\n复用资源版本: {inputs.Version}");
            }
            catch (Exception exception)
            {
                if (finalApkValidated) context.CleanupPreparatoryDirectory();
                else if (outputPrepared) context.CleanupFailedBuild();
                Debug.LogError($"[AndroidBuildUtility] 快速打包失败：{exception.Message}\n{timing.BuildSummary()}");
                ShowDialog("Android 快速打包失败", BuildWorkflowUtility.FormatFailure(timing, exception.Message,
                    finalApkValidated ? context.FinalApkPath : null));
                throw;
            }
            finally
            {
                previous.Restore();
                EditorUtility.ClearProgressBar();
                s_isBuilding = false;
            }
        }

        /// <summary>快速入口不修改远端资源，构建前后都要求目标资源版本和清单与复用输入一致。</summary>
        private static void ValidateQuickPublishedResources(string bundle, AndroidQuickBuildInputs inputs)
        {
            string resources = Path.Combine(bundle, "ResourceFiles");
            PcBuiltinResourceValidator.CaptureRemote(resources, inputs.Version);
            if (!YooAssetBuildUtility.ValidateBuildResult(resources, inputs.Version))
                throw new InvalidOperationException("已发布资源与复用版本不一致，请先发布匹配资源。");
            foreach (string extension in new[] { ".bytes", ".hash" })
            {
                string name = "OutpostLoop_" + inputs.Version + extension;
                if (!File.ReadAllBytes(Path.Combine(resources, name)).SequenceEqual(File.ReadAllBytes(Path.Combine(inputs.PackageDirectory, name))))
                    throw new InvalidOperationException("已发布资源清单内容与复用版本不一致。");
            }
        }

        public static void BuildLatestCodeAndResourcesBatch()
        {
            BuildLatestCodeAndResources(true);
        }

        /// <summary>同步只构建入口，不发布资源或 APK，不启动服务。</summary>
        public static BuildArtifacts BuildLatestCodeAndResourcesOnly()
        {
            BuildArtifacts.RequireReady(BuildTarget.Android);
            return BuildLatestCodeAndResources(false);
        }

        private static BuildArtifacts BuildLatestCodeAndResources(bool publish)
        {
            using var lease = WorkspaceBuildLock.Acquire(Directory.GetParent(Application.dataPath).FullName);
            if (!BeginWorkflow(OfflineWorkflowName))
                return null;

            var timing = new BuildStageTiming(OfflineWorkflowName);
            var context = new AndroidBuildContext(DateTime.Now, Guid.NewGuid().ToString("N"));
            var previous = AndroidEditorOptions.Capture();
            bool finalApkValidated = false;
            try
            {
                timing.Run("EnsureAndroidBuildTarget", EnsureAndroidBuildTarget);
                timing.Run("ValidateAndroidEnvironment", ValidateAndroidEnvironment);
                previous.ApplyDevelopmentApkOptions();

                timing.Run("PrepareOutputDirectories", context.PrepareDirectories);
                var copied = HybridCLRPreparation.Run(BuildTarget.Android, timing,
                    () => BuildPlayer(context.PreparatoryApkPath, true), ValidateAndroidAotOutput);

                string resourceVersion = ReserveAndroidResourceVersion(context.Timestamp);
                var package = timing.Run("BuildAndroidBuiltinPackage", () => BuildPackageOrThrow(resourceVersion));
                timing.Run("ValidateAndroidBuiltinPackage", () =>
                    ValidateBuiltinPackage(package.OutputPackageDirectory, resourceVersion));

                string appVersion = timing.Run("BuildFinalApk", () =>
                    BuildFinalPlayerWithIncrementedPatchVersion(context, true));
                timing.Run("ValidateFinalApk", context.ValidateFinalApk);
                finalApkValidated = true;
                CiBuildProgress.Artifact("Apk", context.FinalApkPath);
                timing.Run("CleanupPreparatoryPlayer", context.CleanupPreparatoryDirectory);
                timing.Run("SaveAndroidBuildState", () =>
                    SaveAndroidBuildState(resourceVersion, package.OutputPackageDirectory, context.FinalApkPath));
                var artifacts = CreateApkArtifacts(context.FinalApkPath, appVersion,
                    resourceVersion, package.OutputPackageDirectory, timing);
                if (publish) timing.Run("PublishArtifacts", () => artifacts.Publish());
                Debug.Log($"[AndroidBuildUtility] Android 完整包构建成功：APK={artifacts.PlayerPath}，清单={artifacts.UpdateManifestPath}");
                ShowDialog("Android 构建成功", $"APK: {artifacts.PlayerPath}\n资源版本: {resourceVersion}\n更新清单: {artifacts.UpdateManifestPath}");
                return artifacts;
            }
            catch (Exception exception)
            {
                if (finalApkValidated)
                    context.CleanupPreparatoryDirectory();
                else
                    context.CleanupFailedBuild();

                string failure = BuildWorkflowUtility.FormatFailure(
                    timing,
                    exception.Message,
                    finalApkValidated ? context.FinalApkPath : null);
                Debug.LogError($"[AndroidBuildUtility] Android 构建失败：{failure}");
                ShowDialog("Android 构建失败", failure);
                throw;
            }
            finally
            {
                previous.Restore();
                EditorUtility.ClearProgressBar();
                s_isBuilding = false;
            }
        }

        /// <summary>生成带完整内置回退资源的 Android Host 基础 APK，并发布对应资源服。</summary>
        public static void BuildHostBaseCodeAndResourcesBatch()
        {
            BuildHostBaseCodeAndResources(true);
        }

        /// <summary>同步只构建入口，不发布资源或 APK，不启动服务。</summary>
        public static BuildArtifacts BuildHostBaseCodeAndResourcesOnly()
        {
            BuildArtifacts.RequireReady(BuildTarget.Android);
            return BuildHostBaseCodeAndResources(false);
        }

        private static BuildArtifacts BuildHostBaseCodeAndResources(bool publish)
        {
            using var lease = WorkspaceBuildLock.Acquire(Directory.GetParent(Application.dataPath).FullName);
            if (!BeginWorkflow(HostBaseWorkflowName))
                return null;

            var timing = new BuildStageTiming(HostBaseWorkflowName);
            var context = new AndroidBuildContext(DateTime.Now, Guid.NewGuid().ToString("N"));
            var previous = AndroidEditorOptions.Capture();
            bool finalApkValidated = false;
            try
            {
                timing.Run("EnsureAndroidBuildTarget", EnsureAndroidBuildTarget);
                timing.Run("ValidateAndroidEnvironment", ValidateAndroidEnvironment);
                timing.Run("ValidateAndroidHostConfig", ValidateAndroidHostConfig);
                previous.ApplyDevelopmentApkOptions();
                var hostInputs = timing.Run("CaptureHostInputs", HostBaselineUtility.Capture);
                var quickCompilation = AndroidQuickBuildUtility.CaptureCompilationFingerprint();

                timing.Run("PrepareOutputDirectories", context.PrepareDirectories);
                var copied = HybridCLRPreparation.Run(BuildTarget.Android, timing,
                    () => BuildPlayer(context.PreparatoryApkPath, false), ValidateAndroidAotOutput);

                string resourceVersion = ReserveAndroidResourceVersion(context.Timestamp);
                string quickGenerated = AndroidQuickBuildUtility.CaptureGeneratedFingerprint();
                var package = timing.Run("BuildAndroidHostBasePackage", () =>
                    BuildPackageOrThrow(resourceVersion, true));
                timing.Run("ValidateAndroidBuiltinPackage", () =>
                    ValidateBuiltinPackage(package.OutputPackageDirectory, resourceVersion));
                AndroidQuickBuildUtility.Capture(resourceVersion, package.OutputPackageDirectory, quickCompilation, quickGenerated, "HostBase");
                string appVersion = timing.Run("BuildFinalHostApk", () =>
                    BuildFinalPlayerWithIncrementedPatchVersion(context, false));
                timing.Run("ValidateFinalHostApk", context.ValidateFinalApk);
                finalApkValidated = true;
                CiBuildProgress.Artifact("Apk", context.FinalApkPath);
                timing.Run("CleanupPreparatoryPlayer", context.CleanupPreparatoryDirectory);
                timing.Run("SaveAndroidHostState", () => SaveAndroidHostState(
                    resourceVersion,
                    "HostBase",
                    package.OutputPackageDirectory,
                    context.FinalApkPath,
                    true));

                var artifacts = CreateApkArtifacts(context.FinalApkPath, appVersion,
                    resourceVersion, package.OutputPackageDirectory, timing);
                timing.Run("ArchiveHostBaseline", () => HostBaselineUtility.Archive(artifacts, hostInputs));
                if (publish) timing.Run("PublishArtifacts", () => artifacts.Publish());
                Debug.Log($"[AndroidBuildUtility] Android Host 基础包成功：APK={artifacts.PlayerPath}，清单={artifacts.UpdateManifestPath}");
                ShowDialog("Android Host 基础包成功", $"APK: {artifacts.PlayerPath}\n资源版本: {resourceVersion}\n更新清单: {artifacts.UpdateManifestPath}");
                return artifacts;
            }
            catch (Exception exception)
            {
                HandlePlayerBuildFailure(timing, context, finalApkValidated, exception, "Android Host 基础包失败");
                throw;
            }
            finally
            {
                previous.Restore();
                EditorUtility.ClearProgressBar();
                s_isBuilding = false;
            }
        }

        /// <summary>生成只含 Boot、DLL 和资源清单的联网首包，并发布同版本完整远端资源。</summary>
        public static void BuildSmallHostBaseCodeAndResourcesBatch()
        {
            BuildSmallHostBaseCodeAndResources(true);
        }

        /// <summary>同步只构建入口，不发布资源或 APK，不启动服务。</summary>
        public static BuildArtifacts BuildSmallHostBaseCodeAndResourcesOnly()
        {
            BuildArtifacts.RequireReady(BuildTarget.Android);
            return BuildSmallHostBaseCodeAndResources(false);
        }

        private static BuildArtifacts BuildSmallHostBaseCodeAndResources(bool publish)
        {
            using var lease = WorkspaceBuildLock.Acquire(Directory.GetParent(Application.dataPath).FullName);
            if (!BeginWorkflow(SmallHostWorkflowName))
                return null;

            var timing = new BuildStageTiming(SmallHostWorkflowName);
            var context = new AndroidBuildContext(DateTime.Now, Guid.NewGuid().ToString("N"));
            var previous = AndroidEditorOptions.Capture();
            bool finalApkValidated = false;
            try
            {
                timing.Run("EnsureAndroidBuildTarget", EnsureAndroidBuildTarget);
                timing.Run("ValidateAndroidEnvironment", ValidateAndroidEnvironment);
                timing.Run("ValidateAndroidHostConfig", ValidateAndroidHostConfig);
                previous.ApplyDevelopmentApkOptions();
                var hostInputs = timing.Run("CaptureHostInputs", HostBaselineUtility.Capture);
                var quickCompilation = AndroidQuickBuildUtility.CaptureCompilationFingerprint();

                timing.Run("PrepareOutputDirectories", context.PrepareDirectories);
                var copied = HybridCLRPreparation.Run(BuildTarget.Android, timing,
                    () => BuildPlayer(context.PreparatoryApkPath, false), ValidateAndroidAotOutput);

                string resourceVersion = ReserveAndroidResourceVersion(context.Timestamp);
                string quickGenerated = AndroidQuickBuildUtility.CaptureGeneratedFingerprint();
                var package = timing.Run("BuildAndroidSmallHostPackage", () =>
                    BuildSmallHostPackageOrThrow(resourceVersion));
                timing.Run("ValidateAndroidHostPackage", () =>
                    ValidateHostPackage(package.OutputPackageDirectory, resourceVersion));
                timing.Run("ValidateManifestOnlyBuiltin", () =>
                    ValidateManifestOnlyBuiltin(resourceVersion));
                AndroidQuickBuildUtility.Capture(resourceVersion, package.OutputPackageDirectory, quickCompilation, quickGenerated, "SmallHostBase");
                string appVersion = timing.Run("BuildFinalSmallHostApk", () =>
                    BuildFinalPlayerWithIncrementedPatchVersion(context, false));
                timing.Run("ValidateFinalSmallHostApk", context.ValidateFinalApk);
                finalApkValidated = true;
                CiBuildProgress.Artifact("Apk", context.FinalApkPath);
                timing.Run("CleanupPreparatoryPlayer", context.CleanupPreparatoryDirectory);

                timing.Run("SaveAndroidHostState", () => SaveAndroidHostState(
                    resourceVersion,
                    "SmallHostBase",
                    package.OutputPackageDirectory,
                    context.FinalApkPath,
                    true));
                var artifacts = CreateApkArtifacts(context.FinalApkPath, appVersion,
                    resourceVersion, package.OutputPackageDirectory, timing);
                timing.Run("ArchiveHostBaseline", () => HostBaselineUtility.Archive(artifacts, hostInputs));
                if (publish)
                {
                    var published = timing.Run("PublishArtifacts", () => artifacts.Publish());
                    StartPublishedResourceServer(published.BundleDirectory, resourceVersion, timing);
                }
                Debug.Log($"[AndroidBuildUtility] Android 联网首包成功：APK={artifacts.PlayerPath}，清单={artifacts.UpdateManifestPath}");
                return artifacts;
            }
            catch (Exception exception)
            {
                HandlePlayerBuildFailure(timing, context, finalApkValidated, exception, "Android 联网首包失败");
                throw;
            }
            finally
            {
                previous.Restore();
                EditorUtility.ClearProgressBar();
                s_isBuilding = false;
            }
        }

        /// <summary>复用最近 Android Host 基础包的 AOT，只生成 Game DLL 与远端资源。</summary>
        public static void BuildHostCodeAndResourcesBatch()
        {
            BuildHostCodeAndResources(true);
        }

        /// <summary>同步只构建入口，不发布资源或 APK，不启动服务。</summary>
        public static BuildArtifacts BuildHostCodeAndResourcesOnly()
        {
            BuildArtifacts.RequireReady(BuildTarget.Android);
            return BuildHostCodeAndResources(false);
        }

        public static BuildArtifacts BuildHostCodeAndResourcesFromBaselineOnly(string baselineId)
        {
            BuildArtifacts.RequireReady(BuildTarget.Android);
            if (string.IsNullOrWhiteSpace(baselineId)) throw new ArgumentException("必须选择宿主基线。", nameof(baselineId));
            return BuildHostCodeAndResources(false, baselineId);
        }

        private static BuildArtifacts BuildHostCodeAndResources(bool publish, string baselineId = null)
        {
            using var lease = WorkspaceBuildLock.Acquire(Directory.GetParent(Application.dataPath).FullName);
            if (!BeginWorkflow(HostUpdateWorkflowName))
                return null;

            var timing = new BuildStageTiming(HostUpdateWorkflowName);
            string retainedPackageDirectory = null;
            try
            {
                timing.Run("EnsureAndroidBuildTarget", EnsureAndroidBuildTarget);
                timing.Run("ValidateAndroidEnvironment", ValidateAndroidEnvironment);
                timing.Run("ValidateAndroidHostConfig", ValidateAndroidHostConfig);
                var baseline = timing.Run("RestoreHostBaseline", () => HostBaselineUtility.Restore(BuildTarget.Android, baselineId));
                BuildState state = timing.Run("LoadAndroidHostState", LoadStateOrThrow);
                timing.Run("ValidateAndroidAotBaseline", () => ValidateAndroidAotBaseline(state));

                timing.Run("CompileHotUpdateDll", () =>
                    HybridCLRBuildUtility.CompileHotUpdateDll(BuildTarget.Android));
                timing.Run("ValidateHotUpdateAgainstBaseline", () => HybridCLRBuildUtility.ValidateHotUpdateAgainstBaseline(
                    BuildTarget.Android, state.LastAndroidAotBaseline, baseline));
                var copied = timing.Run("CopyGeneratedDllsFromBaseline", () =>
                    HybridCLRBuildUtility.CopyGeneratedDllsFromBaseline(
                        BuildTarget.Android,
                        state.LastAndroidAotBaseline.AotDirectory,
                        state.LastAndroidAotBaseline.RuntimeListPath));
                timing.Run("ValidateGeneratedDlls", HybridCLRBuildUtility.ValidateGeneratedDlls);

                string resourceVersion = ReserveAndroidResourceVersion(DateTime.Now, baseline.ResourceVersion);
                var package = timing.Run("BuildAndroidHostPackage", () =>
                    BuildPackageOrThrow(resourceVersion, false));
                timing.Run("ValidateAndroidHostPackage", () =>
                    ValidateHostPackage(package.OutputPackageDirectory, resourceVersion));
                retainedPackageDirectory = package.OutputPackageDirectory;
                timing.Run("SaveAndroidHostState", () => SaveAndroidHostState(
                    resourceVersion,
                    "Host",
                    package.OutputPackageDirectory,
                    null,
                    false));

                var artifacts = new BuildArtifacts
                {
                    Target = BuildTarget.Android, ResourceVersion = resourceVersion,
                    PackageDirectory = package.OutputPackageDirectory, HostBaselineId = baseline.Id,
                    AppVersion = baseline.AppVersion, AppVersionCode = baseline.AppVersionCode
                };
                if (publish)
                {
                    var published = timing.Run("PublishArtifacts", () => artifacts.Publish());
                    StartPublishedResourceServer(published.BundleDirectory, resourceVersion, timing);
                }
                Debug.Log($"[AndroidBuildUtility] Android Host 更新成功：资源版本={resourceVersion}，目录={artifacts.PackageDirectory}");
                return artifacts;
            }
            catch (Exception exception)
            {
                string failure = BuildWorkflowUtility.FormatFailure(
                    timing,
                    exception.Message,
                    retainedPackageDirectory);
                Debug.LogError($"[AndroidBuildUtility] Android Host 更新失败：{failure}");
                ShowDialog("Android Host 更新失败", failure);
                throw;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                s_isBuilding = false;
            }
        }

        private static string ReserveAndroidResourceVersion(DateTime timestamp, string minimumVersion = null) =>
            PersistentBuildVersions.ReserveResource(BuildDataStore.Root, BuildTarget.Android.ToString(),
                timestamp, YooAssetBuildUtility.GetPackageRoot(BuildTarget.Android), minimumVersion);

        private static BuildArtifacts CreateApkArtifacts(string apk, string appVersion,
            string resourceVersion, string packageDirectory, BuildStageTiming timing)
        {
            string manifest = timing.Run("GenerateApkUpdateManifest", () =>
                AndroidApkPublishUtility.GenerateManifest(apk, appVersion, PlayerSettings.Android.bundleVersionCode));
            return new BuildArtifacts
            {
                Target = BuildTarget.Android, ResourceVersion = resourceVersion,
                PackageDirectory = packageDirectory, PlayerPath = apk, UpdateManifestPath = manifest,
                AppVersion = appVersion, AppVersionCode = PlayerSettings.Android.bundleVersionCode
            };
        }

        private static bool BeginWorkflow(string workflowName)
        {
            if (!BeginBuild())
                return false;
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
                return true;

            var timing = new BuildStageTiming(workflowName);
            try
            {
                SessionState.SetString(PendingWorkflowKey, workflowName);
                SessionState.SetBool(ReloadCompletedKey, false);
                timing.Run("EnsureAndroidBuildTarget", EnsureAndroidBuildTarget);
                Debug.Log("[AndroidBuildUtility] 已切换 Android，等待脚本重载后自动继续。");
                return false;
            }
            catch (Exception exception)
            {
                ClearPendingWorkflow();
                ShowDialog("Android 平台切换失败", BuildWorkflowUtility.FormatFailure(timing, exception.Message));
                throw;
            }
            finally
            {
                s_isBuilding = false;
            }
        }

        private static bool BeginBuild()
        {
            if (HasActiveWorkflow || BuildUtility.HasActiveWorkflow)
            {
                Debug.LogWarning("[AndroidBuildUtility] 已有构建流程正在执行，请勿重复触发。");
                return false;
            }
            if (EditorApplication.isCompiling)
                throw new InvalidOperationException("Unity 正在编译，请稍后再试。");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Unity 正在进入或退出 PlayMode，请稍后再试。");

            s_isBuilding = true;
            return true;
        }

        private static void EnsureAndroidBuildTarget()
        {
            const BuildTarget target = BuildTarget.Android;
            if (EditorUserBuildSettings.activeBuildTarget == target)
                return;
            if (Application.isBatchMode)
                throw new InvalidOperationException(
                    $"批处理模式不能自动切换 Build Target，请使用 -buildTarget Android 启动 Unity；当前为 {EditorUserBuildSettings.activeBuildTarget}。");
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, target))
                throw new InvalidOperationException("当前 Unity 未安装可用的 Android Build Support。");

            BuildTarget previous = EditorUserBuildSettings.activeBuildTarget;
            bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, target);
            if (!switched || EditorUserBuildSettings.activeBuildTarget != target)
                throw new InvalidOperationException($"Build Target 自动切换失败：{previous} -> {target}");
        }

        internal static void ValidateAndroidEnvironment()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                throw new InvalidOperationException("当前 Unity 未安装可用的 Android Build Support。");
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new InvalidOperationException(
                    $"Build Target 自动切换后仍不是 Android，当前为 {EditorUserBuildSettings.activeBuildTarget}。");

            ValidateEmbeddedTool("SDK", "SDK");
            ValidateEmbeddedTool("NDK", "NDK");
            ValidateEmbeddedTool("OpenJDK", "OpenJDK");

            if (PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android) != ScriptingImplementation.IL2CPP)
                throw new InvalidOperationException("Android 脚本后端必须是 IL2CPP。");
            if (PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64)
                throw new InvalidOperationException($"Android CPU 必须仅为 ARM64，当前为 {PlayerSettings.Android.targetArchitectures}。");
            if (!string.Equals(PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android), ApplicationIdentifier,
                    StringComparison.Ordinal))
                throw new InvalidOperationException($"Android 包名必须是 {ApplicationIdentifier}。");
            if (PlayerSettings.Android.useCustomKeystore)
                throw new InvalidOperationException("Development APK 必须使用 Unity 调试签名，不能启用自定义 keystore。");
            if (PlayerSettings.defaultInterfaceOrientation != UIOrientation.AutoRotation ||
                PlayerSettings.allowedAutorotateToPortrait ||
                PlayerSettings.allowedAutorotateToPortraitUpsideDown ||
                !PlayerSettings.allowedAutorotateToLandscapeLeft ||
                !PlayerSettings.allowedAutorotateToLandscapeRight)
            {
                throw new InvalidOperationException("Android 屏幕方向必须为自动旋转且仅允许左右横屏。");
            }

            var enabledScenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).ToArray();
            if (enabledScenes.Length != 1 ||
                !string.Equals(enabledScenes[0].path, BootScene, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Build Settings 必须只启用 {BootScene}。");
            }

            RequireDirectory("Assets/Game/Config");
            RequireDirectory("Assets/Game/Dlls");
        }

        /// <summary>校验写入 Host APK 的资源地址确实可由 Android 真机访问。</summary>
        internal static void ValidateAndroidHostConfig()
        {
            RequireFile(HostConfigPath);
            var json = File.ReadAllText(HostConfigPath);
            var config = JsonUtility.FromJson<AndroidHostConfigData>(json);
            ValidateAndroidHostServerUrl(config?.resourceServer);
        }

        internal static void ValidateAndroidHostServerUrl(string resourceServer)
        {
            if (string.IsNullOrWhiteSpace(resourceServer) ||
                !Uri.TryCreate(resourceServer.Trim(), UriKind.Absolute, out Uri uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException("Android Host resourceServer 必须是绝对 HTTP/HTTPS URL。");
            }

            if (uri.IsLoopback ||
                string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Host, "0.0.0.0", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Android 真机不能使用回环资源地址 {uri}；请把 {HostConfigPath} 改为手机可访问的局域网 IP、域名或 HTTPS 地址。");
            }

            if (uri.Scheme == Uri.UriSchemeHttp &&
                PlayerSettings.insecureHttpOption != InsecureHttpOption.AlwaysAllowed)
            {
                throw new InvalidOperationException(
                    $"Android HTTP Host 要求允许不安全 HTTP，当前设置为 {PlayerSettings.insecureHttpOption}。");
            }
        }

        private static void ValidateEmbeddedTool(string directoryName, string displayName)
        {
            string path = Path.Combine(
                EditorApplication.applicationContentsPath,
                "PlaybackEngines",
                "AndroidPlayer",
                directoryName);
            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException($"Unity 内置 Android {displayName} 不存在: {path}");
        }

        private static void ValidateAndroidAotOutput()
        {
            string directory = HybridCLRBuildUtility.GetAotDirectory(BuildTarget.Android);
            if (!Directory.Exists(directory))
                throw new DirectoryNotFoundException($"Android Player AOT 输出目录不存在: {directory}");
            if (Directory.GetFiles(directory, "*.dll", SearchOption.TopDirectoryOnly).Length == 0)
                throw new InvalidOperationException($"Android Player AOT 输出为空: {directory}");
        }

        private static YooAssetBuildUtility.BuildResult BuildPackageOrThrow(string version)
        {
            return BuildPackageOrThrow(version, true);
        }

        private static YooAssetBuildUtility.BuildResult BuildPackageOrThrow(string version, bool fullBuiltin)
        {
            var result = YooAssetBuildUtility.BuildOutpostLoop(version, fullBuiltin);
            if (!result.Success)
                throw new InvalidOperationException(result.Error ?? "Android YooAsset 资源构建失败。");
            CiBuildProgress.Artifact("Resources", result.OutputPackageDirectory);
            return result;
        }

        private static YooAssetBuildUtility.BuildResult BuildSmallHostPackageOrThrow(string version)
        {
            var result = YooAssetBuildUtility.BuildManifestOnlyBuiltinPackage(version);
            if (!result.Success)
                throw new InvalidOperationException(result.Error ?? "Android 联网首包资源构建失败。");
            CiBuildProgress.Artifact("Resources", result.OutputPackageDirectory);
            return result;
        }

        private static void ValidateBuiltinPackage(string outputDirectory, string version)
        {
            if (!YooAssetBuildUtility.ValidateBuildResult(outputDirectory, version))
                throw new InvalidOperationException($"Android YooAsset 输出校验失败: {outputDirectory}");

            string builtinDirectory = YooAssetBuildUtility.GetStreamingAssetsDirectory();
            if (!YooAssetBuildUtility.ValidateBuildResult(builtinDirectory, version))
                throw new InvalidOperationException($"Android StreamingAssets 内置资源校验失败: {builtinDirectory}");
            RequireFile(Path.Combine(builtinDirectory, "BuildinCatalog.bytes"));
            RequireFile(Path.Combine(builtinDirectory, "BuildinCatalog.json"));
        }

        private static void ValidateHostPackage(string outputDirectory, string version)
        {
            if (!YooAssetBuildUtility.ValidateBuildResult(outputDirectory, version))
                throw new InvalidOperationException($"Android Host YooAsset 输出校验失败: {outputDirectory}");
        }

        private static void ValidateManifestOnlyBuiltin(string version)
        {
            if (!YooAssetBuildUtility.ValidateManifestOnlyBuiltinPackage(version))
                throw new InvalidOperationException("Android 联网首包内置清单校验失败。");
        }

        private static BuildState LoadStateOrThrow()
        {
            BuildState state = BuildStateStore.Load(out string warning);
            if (!string.IsNullOrEmpty(warning))
                Debug.LogWarning(warning);
            return state;
        }

        private static void ValidateAndroidAotBaseline(BuildState state)
        {
            if (!BuildStateStore.TryValidateAndroidAotBaseline(state, BuildTarget.Android, out string error))
            {
                throw new InvalidOperationException(
                    $"Android Host AOT 基线不可用：{error} 请先构建 Android Host 基础包或联网首包。");
            }
        }

        private static void BuildPlayer(string apkPath, bool forceBuiltinResources)
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { BootScene },
                locationPathName = apkPath,
                target = BuildTarget.Android,
                options = BuildOptions.Development | BuildOptions.CompressWithLz4,
                extraScriptingDefines = forceBuiltinResources
                    ? new[] { BuiltinResourcesScriptingDefine }
                    : Array.Empty<string>()
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report == null)
                throw new InvalidOperationException($"Android Player 构建未返回 BuildReport: {apkPath}");
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"Android Player 构建失败: {report.summary.result}");
            if (!File.Exists(apkPath) || new FileInfo(apkPath).Length <= 0)
                throw new InvalidOperationException($"Android Player 报告成功但 APK 不存在或为空: {apkPath}");
        }

        private static string BuildFinalPlayerWithIncrementedPatchVersion(
            AndroidBuildContext context,
            bool forceBuiltinResources)
        {
            string previousVersion = PlayerSettings.bundleVersion;
            int previousCode = PlayerSettings.Android.bundleVersionCode;
            if (previousCode == int.MaxValue)
                throw new InvalidOperationException("Android 版本码已达到上限。");

            var reservation = PersistentBuildVersions.ReserveApplication(BuildDataStore.Root, "Android", previousVersion, previousCode);
            string nextVersion = reservation.VersionName;
            context.SetFinalApkVersion(nextVersion);
            PlayerSettings.bundleVersion = nextVersion;
            PlayerSettings.Android.bundleVersionCode = reservation.VersionCode;
            try
            {
                BuildPlayer(context.FinalApkPath, forceBuiltinResources);
                AssetDatabase.SaveAssets();
                return nextVersion;
            }
            catch
            {
                PlayerSettings.bundleVersion = previousVersion;
                PlayerSettings.Android.bundleVersionCode = previousCode;
                AssetDatabase.SaveAssets();
                throw;
            }
        }

        internal static string IncrementAndroidPatchVersion(string version)
        {
            string[] parts = version?.Split('.');
            if (parts == null || parts.Length != 3 ||
                !int.TryParse(parts[0], out int major) || major < 0 ||
                !int.TryParse(parts[1], out int minor) || minor < 0 ||
                !int.TryParse(parts[2], out int patch) || patch < 0 || patch == int.MaxValue)
            {
                throw new InvalidOperationException(
                    $"Android 应用版本必须是 major.minor.patch 格式，当前值：{version}");
            }
            return $"{major}.{minor}.{patch + 1}";
        }

        internal static string BuildAndroidApkFileName(string appVersion)
        {
            if (string.IsNullOrWhiteSpace(appVersion))
                throw new ArgumentException("Android 应用版本不能为空。", nameof(appVersion));
            return $"OutpostLoop-{appVersion}.apk";
        }

        private static void SaveAndroidBuildState(string version, string packageDirectory, string playerPath)
        {
            BuildState state = BuildStateStore.Load(out string warning);
            if (!string.IsNullOrEmpty(warning))
                Debug.LogWarning(warning);

            long completedUtcTicks = DateTime.UtcNow.Ticks;
            state.LastAndroidPackage = new PackageBuildRecord
            {
                Version = version,
                BuildTarget = BuildTarget.Android.ToString(),
                OutputPackageDirectory = Path.GetFullPath(packageDirectory),
                BuildKind = "Full",
                CompletedUtcTicks = completedUtcTicks
            };
            state.LastAndroidPlayer = new PlayerBuildRecord
            {
                PlayerPath = Path.GetFullPath(playerPath),
                Version = version,
                BuildKind = "Full",
                CompletedUtcTicks = completedUtcTicks
            };
            BuildStateStore.Save(state);
        }

        private static void SaveAndroidHostState(
            string version,
            string buildKind,
            string packageDirectory,
            string playerPath,
            bool updateAot,
            string frozenAotDirectory = null)
        {
            BuildState state = LoadStateOrThrow();
            long completedUtcTicks = DateTime.UtcNow.Ticks;
            string gameDllDirectory = HybridCLRBuildUtility.GetGameDllDirectory();

            state.LastAndroidHost = new HostBuildRecord
            {
                BaselineId = updateAot ? null : state.LastAndroidAotBaseline?.BaselineId,
                Version = version,
                BuildTarget = BuildTarget.Android.ToString(),
                OutputPackageDirectory = Path.GetFullPath(packageDirectory),
                GameDllDirectory = gameDllDirectory,
                CompletedUtcTicks = completedUtcTicks
            };
            if (updateAot)
            {
                state.LastAndroidAotBaseline = new AotBaselineRecord
                {
                    BuildTarget = BuildTarget.Android.ToString(),
                    AotDirectory = frozenAotDirectory ?? HybridCLRBuildUtility.GetAotDirectory(BuildTarget.Android),
                    RuntimeListPath = frozenAotDirectory == null ? HybridCLRBuildUtility.GetRuntimeListPath() : Path.Combine(frozenAotDirectory, "runtime-list.bytes"),
                    GameDllDirectory = gameDllDirectory,
                    CompletedUtcTicks = completedUtcTicks
                };
            }

            state.LastAndroidPackage = new PackageBuildRecord
            {
                Version = version,
                BuildTarget = BuildTarget.Android.ToString(),
                OutputPackageDirectory = Path.GetFullPath(packageDirectory),
                BuildKind = buildKind,
                CompletedUtcTicks = completedUtcTicks
            };
            if (!string.IsNullOrEmpty(playerPath))
            {
                state.LastAndroidPlayer = new PlayerBuildRecord
                {
                    PlayerPath = Path.GetFullPath(playerPath),
                    Version = version,
                    BuildKind = buildKind,
                    CompletedUtcTicks = completedUtcTicks
                };
            }

            BuildStateStore.Save(state);
        }

        [InitializeOnLoadMethod]
        private static void ObservePendingWorkflow()
        {
            EditorApplication.update -= ResumePendingWorkflow;
            EditorApplication.update += ResumePendingWorkflow;
            CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompilationFinished;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
        }

        [DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            if (!string.IsNullOrEmpty(SessionState.GetString(PendingWorkflowKey, string.Empty)))
                SessionState.SetBool(ReloadCompletedKey, true);
        }

        private static void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            if (!messages.Any(message => message.type == CompilerMessageType.Error))
                return;
            CancelPendingWorkflow("PlatformScriptsCompilation", $"平台切换后脚本编译失败：{assemblyPath}");
        }

        private static void ResumePendingWorkflow()
        {
            string workflow = SessionState.GetString(PendingWorkflowKey, string.Empty);
            if (string.IsNullOrEmpty(workflow))
                return;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                CancelPendingWorkflow("ResumeAfterPlatformReload", "已进入 PlayMode，取消待恢复的 Android 构建。");
                return;
            }
            if (!BuildWorkflowUtility.CanResume(
                    SessionState.GetBool(ReloadCompletedKey, false),
                    EditorApplication.isCompiling,
                    EditorApplication.isUpdating))
            {
                return;
            }
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                CancelPendingWorkflow("ResumeAfterPlatformReload", "重载后目标不是 Android，已取消构建。");
                return;
            }
            Action action = ResolveWorkflow(workflow);
            if (action == null)
            {
                CancelPendingWorkflow("ResumeAfterPlatformReload", "无法恢复未知 Android 工作流：" + workflow);
                return;
            }

            ClearPendingWorkflow();
            try
            {
                action();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static Action ResolveWorkflow(string workflow)
        {
            switch (workflow)
            {
                case "Android 复用 AOT 打包": return BuildQuickReuseAotBatch;
                case "Android 复用本次 AOT和资源快速打包": return BuildQuickReuseApkBatch;
                case OfflineWorkflowName: return BuildLatestCodeAndResourcesBatch;
                case HostBaseWorkflowName: return BuildHostBaseCodeAndResourcesBatch;
                case SmallHostWorkflowName: return BuildSmallHostBaseCodeAndResourcesBatch;
                case HostUpdateWorkflowName: return BuildHostCodeAndResourcesBatch;
                default: return null;
            }
        }

        private static void CancelPendingWorkflow(string stage, string reason)
        {
            string workflow = SessionState.GetString(PendingWorkflowKey, string.Empty);
            if (string.IsNullOrEmpty(workflow))
                return;
            ClearPendingWorkflow();
            var timing = new BuildStageTiming(workflow);
            timing.Run(stage, () => { });
            string details = BuildWorkflowUtility.FormatFailure(timing, reason);
            Debug.LogError($"[AndroidBuildUtility] Android 平台切换取消：{details}");
            ShowDialog("Android 平台切换流程已取消", details);
        }

        private static void ClearPendingWorkflow()
        {
            SessionState.EraseString(PendingWorkflowKey);
            SessionState.EraseBool(ReloadCompletedKey);
        }

        /// <summary>从最近 Android 发布包启动资源服，不执行构建或发布。</summary>
        public static void StartAndroidResourceServerFromLatestPublishedBundle()
        {
            try
            {
                string projectPath = Directory.GetParent(Application.dataPath)?.FullName ??
                    throw new InvalidOperationException("无法确定 Unity 项目根目录。");
                string workspaceRoot = Directory.GetParent(projectPath)?.FullName ??
                    throw new InvalidOperationException("无法确定工作区根目录。");
                string releaseRoot = Path.Combine(workspaceRoot, "Release", "ResourceServer", "Android");
                if (!ResourceServerPublishUtility.TryFindLatestBundle(
                        releaseRoot,
                        out string bundleDirectory,
                        out string findError))
                {
                    throw new InvalidOperationException($"查找最新 Android 资源服发布包失败：{findError}");
                }

                string resourceRoot = Path.Combine(bundleDirectory, "ResourceFiles");
                string versionFile = Path.Combine(resourceRoot, "OutpostLoop.version");
                if (!File.Exists(versionFile))
                    throw new FileNotFoundException("Android 资源服版本文件不存在。", versionFile);
                string version = File.ReadAllText(versionFile).Trim();
                LocalServerStartResult result = LocalResourceServerUtility.StartAndProbe(resourceRoot, version);
                if (!result.Success)
                    throw new InvalidOperationException($"Android 资源服启动失败：{result.Error}");

                Debug.Log(
                    $"[AndroidBuildUtility] Android 资源服已启动：版本={version}，目录={bundleDirectory}，地址={result.BaseUrl}");
            }
            catch (Exception exception)
            {
                Debug.LogError($"[AndroidBuildUtility] Android 资源服启动失败：{exception.Message}");
                ShowDialog("Android 资源服启动失败", exception.Message);
            }
        }

        private static void StartPublishedResourceServer(
            string bundleDirectory,
            string version,
            BuildStageTiming timing)
        {
            string resourceRoot = Path.Combine(bundleDirectory, "ResourceFiles");
            LocalServerStartResult server = timing.Run(
                "StartAndroidResourceServer",
                () => LocalResourceServerUtility.StartAndProbe(resourceRoot, version));
            if (!server.Success)
            {
                ShowDialog(
                    "Android 资源已发布，资源服启动失败",
                    BuildWorkflowUtility.FormatFailure(timing, server.Error, bundleDirectory));
                return;
            }
            Debug.Log($"[AndroidBuildUtility] Android 资源服已启动：版本={version}，地址={server.BaseUrl}");
        }

        private static void HandlePlayerBuildFailure(
            BuildStageTiming timing,
            AndroidBuildContext context,
            bool finalApkValidated,
            Exception exception,
            string title)
        {
            if (finalApkValidated)
                context.CleanupPreparatoryDirectory();
            else
                context.CleanupFailedBuild();

            string failure = BuildWorkflowUtility.FormatFailure(
                timing,
                exception.Message,
                finalApkValidated ? context.FinalApkPath : null);
            Debug.LogError($"[AndroidBuildUtility] Android Player 构建失败：{failure}");
            ShowDialog(title, failure);
        }

        private static bool Confirm(string title, string message)
        {
            return Application.isBatchMode || EditorUtility.DisplayDialog(title, message, "开始构建", "取消");
        }

        private static void ShowResourceServerResult(ResourceServerPublishResult result)
        {
            if (result.Success)
            {
                ShowDialog(result.Operation + "成功", $"版本: {result.Version}\n目录: {result.BundleDirectory}");
                return;
            }
            ShowDialog(result.Operation + "失败", result.Error ?? "未知错误");
        }

        private static void RequireFile(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"必要文件缺失: {path}");
        }

        private static void RequireDirectory(string path)
        {
            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException($"必要目录缺失: {path}");
        }

        private static void ShowDialog(string title, string message)
        {
            if (Application.isBatchMode)
                Debug.Log($"[AndroidBuildUtility] 构建提示：{title}：{message}");
            else
                EditorUtility.DisplayDialog(title, message, "OK");
        }

        [Serializable]
        private sealed class AndroidHostConfigData
        {
            public string resourceServer;
        }

        private readonly struct AndroidEditorOptions
        {
            private readonly bool _development;
            private readonly bool _allowDebugging;
            private readonly bool _connectProfiler;
            private readonly bool _buildAppBundle;

            private AndroidEditorOptions(bool development, bool allowDebugging, bool connectProfiler, bool buildAppBundle)
            {
                _development = development;
                _allowDebugging = allowDebugging;
                _connectProfiler = connectProfiler;
                _buildAppBundle = buildAppBundle;
            }

            public static AndroidEditorOptions Capture() => new AndroidEditorOptions(
                EditorUserBuildSettings.development,
                EditorUserBuildSettings.allowDebugging,
                EditorUserBuildSettings.connectProfiler,
                EditorUserBuildSettings.buildAppBundle);

            public void ApplyDevelopmentApkOptions()
            {
                EditorUserBuildSettings.development = true;
                EditorUserBuildSettings.allowDebugging = false;
                EditorUserBuildSettings.connectProfiler = false;
                EditorUserBuildSettings.buildAppBundle = false;
            }

            public void Restore()
            {
                EditorUserBuildSettings.development = _development;
                EditorUserBuildSettings.allowDebugging = _allowDebugging;
                EditorUserBuildSettings.connectProfiler = _connectProfiler;
                EditorUserBuildSettings.buildAppBundle = _buildAppBundle;
            }
        }

        private sealed class AndroidBuildContext
        {
            public AndroidBuildContext(DateTime timestamp, string runId)
            {
                Timestamp = timestamp;
                string projectPath = Directory.GetParent(Application.dataPath)?.FullName ??
                    throw new InvalidOperationException("无法确定 Unity 项目根目录。");
                BatchDirectory = Path.GetFullPath(
                    Path.Combine(projectPath, "Build", "Android", timestamp.ToString("yyyyMMdd_HHmmss")));
                PreparatoryDirectory = Path.GetFullPath(
                    Path.Combine(projectPath, "Temp", "OutpostLoopBuild", "Android", runId));
                PreparatoryApkPath = Path.Combine(PreparatoryDirectory, "OutpostLoop-preparatory.apk");
            }

            public DateTime Timestamp { get; }
            public string BatchDirectory { get; }
            public string PreparatoryDirectory { get; }
            public string PreparatoryApkPath { get; }
            public string FinalApkPath { get; private set; }

            public void SetFinalApkVersion(string appVersion)
            {
                FinalApkPath = Path.Combine(BatchDirectory, BuildAndroidApkFileName(appVersion));
            }

            public void PrepareDirectories()
            {
                if (Directory.Exists(BatchDirectory) || File.Exists(BatchDirectory))
                    throw new InvalidOperationException($"Android APK 批次已存在，拒绝覆盖: {BatchDirectory}");
                if (Directory.Exists(PreparatoryDirectory) || File.Exists(PreparatoryDirectory))
                    throw new InvalidOperationException($"Android 临时 Player 目录已存在，拒绝覆盖: {PreparatoryDirectory}");
                Directory.CreateDirectory(BatchDirectory);
                Directory.CreateDirectory(PreparatoryDirectory);
            }

            public void ValidateFinalApk()
            {
                if (!File.Exists(FinalApkPath))
                    throw new FileNotFoundException($"最终 Android APK 不存在: {FinalApkPath}");
                if (new FileInfo(FinalApkPath).Length <= 0)
                    throw new InvalidOperationException($"最终 Android APK 为空: {FinalApkPath}");
            }

            public void CleanupPreparatoryDirectory() => DeleteDirectory(PreparatoryDirectory);

            public void CleanupFailedBuild()
            {
                DeleteDirectory(PreparatoryDirectory);
                DeleteDirectory(BatchDirectory);
            }

            private static void DeleteDirectory(string path)
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
        }
    }
}
#endif
